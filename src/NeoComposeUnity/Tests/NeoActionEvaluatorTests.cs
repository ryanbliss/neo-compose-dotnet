// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P62 NSAction evaluator coverage: <c>callAction</c> invocation, the two
    /// subscription instructions as durable set-invariant member-value
    /// writes, the revision-8 gate, and the new IR variants' JSON validation.
    /// </summary>
    public class NeoActionEvaluatorTests
    {
        private const string ProjectId = "project-action";
        private const string SaveRootValueId = "value-save";
        private const string ActionValueId = "value-action";
        private const string CounterValueId = "value-counter";
        private const string ActionMemberId = "member-action";
        private const string CounterMemberId = "member-counter";
        private const string BumpOneMemberId = "member-bump-one";
        private const string BumpTwoMemberId = "member-bump-two";
        private const string BoomMemberId = "member-boom";
        private const string RelayMemberId = "member-relay";
        private const string ElsewhereMemberId = "member-elsewhere";
        private const string StaticActionMemberId = "member-static-action";
        private const string StaticActionValueId = "value-static-action";

        // -------------------------------------------------------------
        // Invocation (spec §3.1)
        // -------------------------------------------------------------

        [Test]
        public void InvokeAction_FiresListenersInStoredOrder()
        {
            NeoClient client = BuildClient();
            NSGetterEvaluator.Context ctx = ContextFor(client);

            NSGetterEvaluator.InvokeAction(
                ListenerSet(
                    Listener(BumpOneMemberId),
                    Listener(BumpTwoMemberId)),
                Array.Empty<object?>(),
                ctx);

            // Each listener folds its own digit in, so the stored counter
            // spells the order they ran in.
            Assert.AreEqual(12d, CounterValue(client));
        }

        [Test]
        public void InvokeAction_StoredOrderIsTheOnlyOrder()
        {
            NeoClient client = BuildClient();
            NSGetterEvaluator.Context ctx = ContextFor(client);

            NSGetterEvaluator.InvokeAction(
                ListenerSet(
                    Listener(BumpTwoMemberId),
                    Listener(BumpOneMemberId)),
                Array.Empty<object?>(),
                ctx);

            Assert.AreEqual(21d, CounterValue(client));
        }

        [Test]
        public void InvokeAction_EmptyListenerSetIsASuccessfulNoOp()
        {
            NeoClient client = BuildClient();
            NSGetterEvaluator.Context ctx = ContextFor(client);

            NSGetterEvaluator.InvokeAction(
                new NeoActionValue(),
                Array.Empty<object?>(),
                ctx);

            Assert.AreEqual(0d, CounterValue(client));
        }

        [Test]
        public void InvokeAction_AbsentValueIsTheRestState()
        {
            NeoClient client = BuildClient();
            NSGetterEvaluator.Context ctx = ContextFor(client);

            // An action is never nullable, so a missing stored value is the
            // empty set rather than the delegate's null-target throw.
            Assert.DoesNotThrow(() => NSGetterEvaluator.InvokeAction(
                null,
                Array.Empty<object?>(),
                ctx));
        }

        [Test]
        public void InvokeAction_StopsAtTheFirstThrowingListener()
        {
            NeoClient client = BuildClient();
            NSGetterEvaluator.Context ctx = ContextFor(client);

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.InvokeAction(
                    ListenerSet(
                        Listener(BumpOneMemberId),
                        Listener(BoomMemberId),
                        Listener(BumpTwoMemberId)),
                    Array.Empty<object?>(),
                    ctx))!;

            // The frame names the action that fanned out, not the listener
            // that threw (P62 §3.1) — the same shape the web evaluator
            // reports. This call site names no member, so it reports the
            // generic action label both runtimes fall back to.
            StringAssert.Contains("action[default] listener 1 threw:", error.Message);
            StringAssert.Contains("listener boom", error.Message);
            // Fail-fast: listener 0 ran, listener 2 never did.
            Assert.AreEqual(1d, CounterValue(client));
        }

        [Test]
        public void InvokeAction_DetectsAnActionDelegateActionCycle()
        {
            NeoClient client = BuildClient(
                actionDefault: ListenerSet(Listener(RelayMemberId, valueId: null)));
            NSGetterEvaluator.Context ctx = ContextFor(client);
            Assert.IsTrue(client.TryGetMember(
                ActionMemberId,
                out ActionMember? actionMember));

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                NSGetterEvaluator.InvokeAction(
                    actionMember!.defaultValue!.value,
                    Array.Empty<object?>(),
                    ctx))!;

            StringAssert.Contains("cycle", error.Message);
            // The delegate-stack frames carry no listener suffix: a member
            // re-entered at a different listener position is the same frame,
            // which is how the web evaluator keys the cycle guard too.
            StringAssert.Contains(
                "Relay[default] -> OnDamaged[default] -> Relay[default]",
                error.Message);
            StringAssert.DoesNotContain("Relay[default] listener", error.Message);
        }

        [Test]
        public void CallActionPointer_InvokesTheStoredListenerSet()
        {
            NeoClient client = BuildClient(
                storedListeners: ListenerSet(Listener(BumpOneMemberId)));
            RunBody(client, Revision8(CallActionInstruction()));

            Assert.AreEqual(1d, CounterValue(client));
        }

        /// <summary>
        /// P62 §3.3. Both authored forms of a listener store a null
        /// <c>valueId</c> — a declaration default has no row to name, and
        /// <c>this.OnX += this.Handler</c> lowers to it because the resolver
        /// has no static row identity for <c>this</c>. The row that owns the
        /// action is the only receiver those entries can mean, so the
        /// listener's <c>this.Counter</c> write lands on it.
        /// </summary>
        [Test]
        public void CallActionPointer_BindsAValueIdLessListenerToTheOwningRow()
        {
            NeoClient client = BuildClient(
                storedListeners: ListenerSet(
                    Listener(BumpOneMemberId, valueId: null)));

            RunBody(client, Revision8(OwnerCallActionInstruction()));

            Assert.AreEqual(1d, CounterValue(client));
        }

        [Test]
        public void CallActionPointer_LeavesAForeignClassListenerReceiverLess()
        {
            // `Elsewhere` is a member of no class schema, so the owning row
            // cannot supply it and must not be handed over as its `this`.
            NeoClient client = BuildClient(
                storedListeners: ListenerSet(
                    Listener(ElsewhereMemberId, valueId: null)));

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                RunBody(client, Revision8(OwnerCallActionInstruction())))!;

            // Receiver-less is the pre-P62 behavior: the listener runs with a
            // null `this`, so its own field write is what fails. The frame
            // names the action and the row it was read off.
            StringAssert.Contains(
                $"action[{SaveRootValueId}] listener 0 threw:",
                error.Message);
            Assert.AreEqual(0d, CounterValue(client));
        }

        /// <summary>
        /// P62 §3.3. The owning row is reported out of the one evaluation of
        /// the action pointer, so a deeper receiver chain
        /// (<c>__root__.Save.OnDamaged</c>, and equally
        /// <c>this.Rooms[0].OnEnter()</c>) binds its null-<c>valueId</c>
        /// listeners exactly as a plain reference receiver does. Deriving the
        /// owner from the pointer shape instead dropped it for every chain
        /// deeper than one hop, which the TS evaluator never did.
        /// </summary>
        [Test]
        public void CallActionPointer_BindsAValueIdLessListenerThroughANestedReceiver()
        {
            NeoClient client = BuildClient(
                storedListeners: ListenerSet(
                    Listener(BumpOneMemberId, valueId: null)));

            RunBody(client, Revision8(CallActionInstruction()));

            Assert.AreEqual(1d, CounterValue(client));
        }

        [Test]
        public void CallActionPointer_NamesTheOwningRowInAFrameThroughANestedReceiver()
        {
            NeoClient client = BuildClient(
                storedListeners: ListenerSet(
                    Listener(ElsewhereMemberId, valueId: null)));

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                RunBody(client, Revision8(CallActionInstruction())))!;

            StringAssert.Contains(
                $"action[{SaveRootValueId}] listener 0 threw:",
                error.Message);
        }

        /// <summary>
        /// A static NSAction listener fans out through its live row, not its
        /// declaration default: subscriptions write the row, so the row is
        /// what the action means. Mirrors the TS <c>storedActionListeners</c>
        /// static branch.
        /// </summary>
        [Test]
        public void CallActionPointer_FansOutThroughAStaticActionsLiveRow()
        {
            NeoClient client = BuildClient(
                storedListeners: ListenerSet(
                    Listener(StaticActionMemberId, valueId: null)),
                staticActionListeners: ListenerSet(Listener(BumpOneMemberId)));

            RunBody(client, Revision8(CallActionInstruction()));

            Assert.AreEqual(1d, CounterValue(client));
        }

        [Test]
        public void CallActionPointer_TreatsAnUnboundStaticActionAsEmpty()
        {
            NeoClient client = BuildClient(
                storedListeners: ListenerSet(
                    Listener(StaticActionMemberId, valueId: null)),
                staticActionListeners: new NeoActionValue());

            Assert.DoesNotThrow(() =>
                RunBody(client, Revision8(CallActionInstruction())));
            Assert.AreEqual(0d, CounterValue(client));
        }

        // -------------------------------------------------------------
        // Subscription instructions (spec §3.2, §3.3)
        // -------------------------------------------------------------

        /// <summary>
        /// A subscription persists the listener's identity fields only. The
        /// evaluated right-hand side may be a captured value holding the
        /// subscribing row's lexical environment, and storing that would keep
        /// the whole graph reachable for the life of the subscription.
        /// </summary>
        [Test]
        public void AddActionListener_StoresAPersistedCopyOfTheListener()
        {
            NeoClient client = BuildClient();

            RunBody(client, Revision8(AddListener(BumpOneMemberId)));

            NeoDelegateValue stored = StoredListeners(client).listeners[0];
            Assert.IsFalse(
                stored.hasLexicalEnvironment,
                "a stored listener must not retain the subscribing row graph");
            Assert.IsNull(stored.lexicalThis);
        }

        [Test]
        public void AddActionListener_WritesTheListenerSetDurably()
        {
            NeoClient client = BuildClient();

            RunBody(client, Revision8(AddListener(BumpOneMemberId)));

            NeoActionValue stored = StoredListeners(client);
            Assert.AreEqual(1, stored.listeners.Count);
            Assert.AreEqual(BumpOneMemberId, stored.listeners[0].memberId);
            Assert.AreEqual(SaveRootValueId, stored.listeners[0].valueId);
        }

        [Test]
        public void AddActionListener_RepeatedIdentityIsANoOp()
        {
            NeoClient client = BuildClient();

            RunBody(client, Revision8(
                AddListener(BumpOneMemberId),
                AddListener(BumpOneMemberId)));

            Assert.AreEqual(1, StoredListeners(client).listeners.Count);
        }

        [Test]
        public void AddActionListener_DistinctValueIdIsADistinctIdentity()
        {
            NeoClient client = BuildClient();

            RunBody(client, Revision8(
                AddListener(BumpOneMemberId),
                AddListener(BumpOneMemberId, valueId: null)));

            NeoActionValue stored = StoredListeners(client);
            Assert.AreEqual(2, stored.listeners.Count);
            Assert.AreEqual(SaveRootValueId, stored.listeners[0].valueId);
            Assert.IsNull(stored.listeners[1].valueId);
        }

        [Test]
        public void AddActionListener_AppendsInSubscriptionOrder()
        {
            NeoClient client = BuildClient();

            RunBody(client, Revision8(
                AddListener(BumpTwoMemberId),
                AddListener(BumpOneMemberId)));

            NeoActionValue stored = StoredListeners(client);
            Assert.AreEqual(BumpTwoMemberId, stored.listeners[0].memberId);
            Assert.AreEqual(BumpOneMemberId, stored.listeners[1].memberId);
        }

        [Test]
        public void RemoveActionListener_DropsTheMatchingIdentity()
        {
            NeoClient client = BuildClient(
                storedListeners: ListenerSet(
                    Listener(BumpOneMemberId),
                    Listener(BumpTwoMemberId)));

            RunBody(client, Revision8(RemoveListener(BumpOneMemberId)));

            NeoActionValue stored = StoredListeners(client);
            Assert.AreEqual(1, stored.listeners.Count);
            Assert.AreEqual(BumpTwoMemberId, stored.listeners[0].memberId);
        }

        [Test]
        public void RemoveActionListener_AbsentIdentityIsANoOp()
        {
            NeoClient client = BuildClient(
                storedListeners: ListenerSet(Listener(BumpTwoMemberId)));

            Assert.DoesNotThrow(() =>
                RunBody(client, Revision8(RemoveListener(BumpOneMemberId))));

            NeoActionValue stored = StoredListeners(client);
            Assert.AreEqual(1, stored.listeners.Count);
            Assert.AreEqual(BumpTwoMemberId, stored.listeners[0].memberId);
        }

        [Test]
        public void SubscribedListenerRunsOnTheNextInvocation()
        {
            NeoClient client = BuildClient();

            RunBody(client, Revision8(
                AddListener(BumpOneMemberId),
                AddListener(BumpTwoMemberId),
                CallActionInstruction()));

            // No in-memory registry exists: the invocation reads exactly what
            // the two subscriptions wrote (§3.3).
            Assert.AreEqual(12d, CounterValue(client));
        }

        [Test]
        public void AddActionListener_RejectsAClosureRightHandSide()
        {
            NeoClient client = BuildClient();
            var instruction = new AddActionListenerInstruction
            {
                type = InstructionKind.AddActionListener,
                target = ActionWriteTarget(),
                listener = new ValuePointer
                {
                    type = PointerKind.Value,
                    value = new Value
                    {
                        typeInfo = ActionType(),
                        value = JObject.Parse(
                            "{'code':'() => 0','action':{'parameters':[],'instructions':[],'typeInfo':{'type':2,'required':true}}}"),
                    },
                },
            };

            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(() =>
                RunBody(client, Revision8(instruction)))!;

            StringAssert.Contains("member-target listener", error.Message);
        }

        // -------------------------------------------------------------
        // Revision-8 gate (spec §3.2)
        // -------------------------------------------------------------

        [Test]
        public void CallActionPointer_RequiresRevisionEight()
        {
            NeoClient client = BuildClient();
            FunctionWithReturnType body = Body(CallActionInstruction());
            body.compilerRevision = 7;

            NeoScriptPreExecutionValidationError error =
                Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                    RunBody(client, body))!;

            StringAssert.Contains("NSAction IR requires compiler revision 8", error.Message);
        }

        [Test]
        public void AddActionListener_RequiresRevisionEight()
        {
            NeoClient client = BuildClient();
            FunctionWithReturnType body = Body(AddListener(BumpOneMemberId));
            body.compilerRevision = 7;

            NeoScriptPreExecutionValidationError error =
                Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                    RunBody(client, body))!;

            StringAssert.Contains("NSAction IR requires compiler revision 8", error.Message);
        }

        [Test]
        public void RemoveActionListener_RequiresRevisionEight()
        {
            NeoClient client = BuildClient();
            FunctionWithReturnType body = Body(RemoveListener(BumpOneMemberId));
            body.compilerRevision = 7;

            NeoScriptPreExecutionValidationError error =
                Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                    RunBody(client, body))!;

            StringAssert.Contains("NSAction IR requires compiler revision 8", error.Message);
        }

        [Test]
        public void ActionIrNestedInAnExpressionStillTripsTheGate()
        {
            NeoClient client = BuildClient();
            FunctionWithReturnType body = Body(new IfInstruction
            {
                type = InstructionKind.If,
                branches = new[]
                {
                    new ConditionalBranch
                    {
                        expression = new BooleanExpression
                        {
                            condition = new Condition
                            {
                                type = OperatorKind.EqualTo,
                                operand1 = Number(1),
                                operand2 = Number(1),
                            },
                        },
                        instructions = new Instruction[] { CallActionInstruction() },
                    },
                },
            });
            body.compilerRevision = 7;

            NeoScriptPreExecutionValidationError error =
                Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                    RunBody(client, body))!;

            StringAssert.Contains("NSAction IR requires compiler revision 8", error.Message);
        }

        [Test]
        public void FutureRevisionIsRejectedAboveTheCurrentCeiling()
        {
            NeoClient client = BuildClient();
            FunctionWithReturnType body = Body(CallActionInstruction());
            body.compilerRevision =
                FunctionWithReturnType.CurrentCompilerRevision + 1;

            NeoScriptPreExecutionValidationError error =
                Assert.Throws<NeoScriptPreExecutionValidationError>(() =>
                    RunBody(client, body))!;

            StringAssert.Contains("Unsupported NeoScript compiler revision", error.Message);
        }

        [Test]
        public void StaleRevisionBodiesWithoutActionIrStayValid()
        {
            NeoClient client = BuildClient();
            FunctionWithReturnType body = Body();
            body.compilerRevision = 7;

            Assert.DoesNotThrow(() => RunBody(client, body));
        }

        /// <summary>
        /// Spec §7 promises no fleet recompile, so a stale-revision body is
        /// re-executed indefinitely — per frame for an animation setter. The
        /// gate's IR sweep therefore has to be paid once per body, not once
        /// per invocation.
        /// </summary>
        [Test]
        public void StaleRevisionGateSweepsEachBodyOnce()
        {
            NeoClient client = BuildClient();
            FunctionWithReturnType body = Body(SetSaveCounter(5));
            body.compilerRevision = 7;

            Assert.AreEqual(
                0,
                NeoScriptExecutor.IrDiscriminatorScanCount(body.instructions));

            RunBody(client, body);
            Assert.AreEqual(
                1,
                NeoScriptExecutor.IrDiscriminatorScanCount(body.instructions));

            RunBody(client, body);
            RunBody(client, body);
            Assert.AreEqual(
                1,
                NeoScriptExecutor.IrDiscriminatorScanCount(body.instructions),
                "the revision gate must not re-serialize the IR tree per call");
            Assert.AreEqual(5d, CounterValue(client));
        }

        [Test]
        public void CurrentCompilerRevisionIsEight()
        {
            Assert.AreEqual(8, FunctionWithReturnType.CurrentCompilerRevision);
        }

        // -------------------------------------------------------------
        // Wire validation (spec §3.1, §3.2)
        // -------------------------------------------------------------

        [Test]
        public void CallActionPointer_RoundTripsFromWire()
        {
            const string json = @"{
                'type':'callAction',
                'action':{'type':'variable','variableId':'onDamaged'},
                'args':[{'type':'value','value':{'typeInfo':{'type':2,'required':true},'value':3}}],
                'callSiteId':'action-0'
            }";

            var pointer = (CallActionPointer)
                JsonConvert.DeserializeObject<Pointer>(json)!;

            Assert.AreEqual(PointerKind.CallAction, pointer.type);
            Assert.AreEqual("action-0", pointer.callSiteId);
            Assert.AreEqual(1, pointer.args.Length);
            Assert.IsInstanceOf<VariablePointer>(pointer.action);
        }

        [Test]
        public void CallActionPointer_RejectsAMissingCallSiteId()
        {
            AssertPointerRejected(@"{
                'type':'callAction',
                'action':{'type':'variable','variableId':'onDamaged'},
                'args':[]
            }");
        }

        [Test]
        public void CallActionPointer_RejectsAMissingActionPointer()
        {
            AssertPointerRejected(@"{
                'type':'callAction','args':[],'callSiteId':'action-0'
            }");
        }

        [Test]
        public void CallActionPointer_RejectsMissingArgs()
        {
            AssertPointerRejected(@"{
                'type':'callAction',
                'action':{'type':'variable','variableId':'onDamaged'},
                'callSiteId':'action-0'
            }");
        }

        [Test]
        public void CallActionPointer_RejectsAnOptionalInvocation()
        {
            // An action is never nullable, so `?.` cannot arise (§2.1).
            AssertPointerRejected(@"{
                'type':'callAction',
                'action':{'type':'variable','variableId':'onDamaged'},
                'args':[],'optional':true,'callSiteId':'action-0'
            }");
        }

        [Test]
        public void ActionListenerInstructions_RoundTripFromWire()
        {
            const string add = @"{
                'type':'addActionListener',
                'target':{
                    'pointer':{'type':'variable','variableId':'onDamaged'},
                    'typeInfo':{'type':26,'required':true,'argumentTypes':[]},
                    'writability':'save'
                },
                'listener':{'type':'value','value':{
                    'typeInfo':{'type':26,'required':true,'argumentTypes':[]},
                    'value':{'memberId':'member-bump','valueId':null}
                }}
            }";

            var instruction = (AddActionListenerInstruction)
                JsonConvert.DeserializeObject<Instruction>(add)!;

            Assert.AreEqual(InstructionKind.AddActionListener, instruction.type);
            Assert.AreEqual(WritabilityKind.Save, instruction.target.writability);
            Assert.IsInstanceOf<ActionTypeInfo>(instruction.target.typeInfo);
            Assert.IsInstanceOf<ValuePointer>(instruction.listener);

            var removeInstruction = (RemoveActionListenerInstruction)
                JsonConvert.DeserializeObject<Instruction>(
                    add.Replace("addActionListener", "removeActionListener"))!;

            Assert.AreEqual(
                InstructionKind.RemoveActionListener,
                removeInstruction.type);
        }

        [Test]
        public void ActionListenerInstruction_RejectsAMissingListener()
        {
            AssertInstructionRejected(@"{
                'type':'addActionListener',
                'target':{
                    'pointer':{'type':'variable','variableId':'onDamaged'},
                    'typeInfo':{'type':26,'required':true,'argumentTypes':[]}
                }
            }");
        }

        [Test]
        public void ActionListenerInstruction_RejectsAMissingTarget()
        {
            AssertInstructionRejected(@"{
                'type':'removeActionListener',
                'listener':{'type':'variable','variableId':'handler'}
            }");
        }

        // -------------------------------------------------------------
        // Fixture
        // -------------------------------------------------------------

        private static void AssertPointerRejected(string json)
        {
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<Pointer>(json));
        }

        private static void AssertInstructionRejected(string json)
        {
            Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<Instruction>(json));
        }

        private static NSGetterEvaluator.Context ContextFor(NeoClient client) =>
            new(client, thisValue: null, rootValue: null);

        private static void RunBody(
            NeoClient client,
            FunctionWithReturnType body)
        {
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            Dictionary<string, object?> root = RuntimeRoot(client, ctx);
            ctx = ctx.WithRoot(root);
            NeoScriptExecutor.Execute(
                client,
                body,
                new Dictionary<string, object?> { ["__root__"] = root },
                ctx);
        }

        private static double CounterValue(NeoClient client)
        {
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                CounterValueId,
                out NumberMemberValue? counter));
            return counter!.value ?? 0d;
        }

        private static NeoActionValue StoredListeners(NeoClient client)
        {
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                ActionValueId,
                out ActionMemberValue? row));
            Assert.IsNotNull(row!.value);
            return row.value!;
        }

        private static NeoActionValue ListenerSet(
            params NeoDelegateValue[] listeners)
        {
            var value = new NeoActionValue();
            value.listeners.AddRange(listeners);
            return value;
        }

        private static NeoDelegateValue Listener(
            string memberId,
            string? valueId = SaveRootValueId) => new()
        {
            memberId = memberId,
            valueId = valueId,
        };

        private static FunctionWithReturnType Revision8(
            params Instruction[] instructions)
        {
            FunctionWithReturnType body = Body(instructions);
            body.compilerRevision =
                FunctionWithReturnType.CurrentCompilerRevision;
            return body;
        }

        private static FunctionWithReturnType Body(
            params Instruction[] instructions) => new()
        {
            parameters = Array.Empty<Variable>(),
            instructions = instructions,
            typeInfo = new VoidTypeInfo
            {
                type = MemberKind.Void,
                required = true,
            },
        };

        private static FunctionCallInstruction CallActionInstruction() => new()
        {
            type = InstructionKind.FunctionCall,
            call = new CallActionPointer
            {
                type = PointerKind.CallAction,
                action = ActionPointer(),
                args = Array.Empty<Pointer>(),
                callSiteId = "action-call-0",
            },
        };

        private static AddActionListenerInstruction AddListener(
            string memberId,
            string? valueId = SaveRootValueId) => new()
        {
            type = InstructionKind.AddActionListener,
            target = ActionWriteTarget(),
            listener = ListenerPointer(memberId, valueId),
        };

        private static RemoveActionListenerInstruction RemoveListener(
            string memberId,
            string? valueId = SaveRootValueId) => new()
        {
            type = InstructionKind.RemoveActionListener,
            target = ActionWriteTarget(),
            listener = ListenerPointer(memberId, valueId),
        };

        private static WriteTarget ActionWriteTarget() => new()
        {
            pointer = ActionPointer(),
            typeInfo = ActionType(),
            writability = WritabilityKind.Save,
        };

        /// <summary>
        /// The pointer form a method group lowers to at a delegate position:
        /// a literal member target, never a closure (§3.2).
        /// </summary>
        private static ValuePointer ListenerPointer(
            string memberId,
            string? valueId) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = ActionType(),
                value = new JObject
                {
                    ["memberId"] = memberId,
                    ["valueId"] = valueId is null
                        ? JValue.CreateNull()
                        : new JValue(valueId),
                },
            },
        };

        private static KeyOfPointer ActionPointer() =>
            KeyOf(KeyOf(RootVariable(), "Save"), "OnDamaged");

        /// <summary>
        /// The same invocation read off a plain project reference rather than
        /// through the root chain. Both shapes report the same owning row
        /// (P62 §3.3) — the receiver is captured from the evaluation that
        /// already happens, so the pointer's depth does not change it.
        /// </summary>
        private static FunctionCallInstruction OwnerCallActionInstruction() => new()
        {
            type = InstructionKind.FunctionCall,
            call = new CallActionPointer
            {
                type = PointerKind.CallAction,
                action = KeyOf(SaveRootReference(), "OnDamaged"),
                args = Array.Empty<Pointer>(),
                callSiteId = "action-call-owner",
            },
        };

        private static ReferencePointer SaveRootReference() => new()
        {
            type = PointerKind.Reference,
            valueId = SaveRootValueId,
        };

        private static ActionTypeInfo ActionType() => new()
        {
            type = MemberKind.NSAction,
            required = true,
            argumentTypes = Array.Empty<TypeInfo>(),
        };

        private static AssignInstruction SetSaveCounter(double value) => new()
        {
            type = InstructionKind.Assign,
            target = new WriteTarget
            {
                pointer = KeyOf(KeyOf(RootVariable(), "Save"), "Counter"),
                typeInfo = IntType(),
                writability = WritabilityKind.Save,
            },
            operatorValue = "=",
            pointer = Number(value),
        };

        private static NeoClient BuildClient(
            NeoActionValue? actionDefault = null,
            NeoActionValue? storedListeners = null,
            NeoActionValue? staticActionListeners = null)
        {
            ClassMember assets = RootClassMember(
                "member-root-assets", "Assets", "class-root", "value-assets");
            ClassMember save = RootClassMember(
                "member-root-save", "Save", "class-save-root", SaveRootValueId, "save");
            ClassMember session = RootClassMember(
                "member-root-session", "Session", "class-root", "value-session");
            var counter = new IntMember
            {
                id = CounterMemberId,
                projectId = ProjectId,
                name = "Counter",
                kind = MemberKind.Int,
                required = true,
                valueId = CounterValueId,
                createdAt = "x",
                updatedAt = "x",
            };
            var action = new ActionMember
            {
                id = ActionMemberId,
                projectId = ProjectId,
                name = "OnDamaged",
                kind = MemberKind.NSAction,
                // Never nullable: the empty set is the rest state (§2.1).
                required = false,
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                valueId = ActionValueId,
                defaultValue = new ActionMemberValueBase
                {
                    value = actionDefault ?? new NeoActionValue(),
                },
                createdAt = "x",
                updatedAt = "x",
            };
            // A static action is bound to a live row, and only that row —
            // never the declaration default — is what a fan-out through it
            // means (P62 §3.3, mirroring the TS static branch).
            var staticAction = new ActionMember
            {
                id = StaticActionMemberId,
                projectId = ProjectId,
                name = "OnGlobalReset",
                kind = MemberKind.NSAction,
                required = false,
                isStatic = true,
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                valueId = StaticActionValueId,
                defaultValue = new ActionMemberValueBase
                {
                    value = new NeoActionValue(),
                },
                createdAt = "x",
                updatedAt = "x",
            };
            NSFunctionMember bumpOne = Bump(BumpOneMemberId, "BumpOne", 1);
            // Deliberately absent from every class schema: a listener whose
            // member the owning row cannot supply (P62 §3.3).
            NSFunctionMember elsewhere = Bump(ElsewhereMemberId, "Elsewhere", 9);
            NSFunctionMember bumpTwo = Bump(BumpTwoMemberId, "BumpTwo", 2);
            NSFunctionMember boom = VoidFunction(
                BoomMemberId,
                "Boom",
                new ThrowInstruction
                {
                    type = InstructionKind.Throw,
                    pointer = Text("listener boom"),
                });
            var relay = new DelegateMember
            {
                id = RelayMemberId,
                projectId = ProjectId,
                name = "Relay",
                kind = MemberKind.NSDelegate,
                required = true,
                returnTypeInfo = new VoidTypeInfo
                {
                    type = MemberKind.Void,
                    required = true,
                },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                defaultValue = new DelegateMemberValueBase
                {
                    value = new NeoDelegateValue
                    {
                        memberId = ActionMemberId,
                        valueId = null,
                    },
                },
                createdAt = "x",
                updatedAt = "x",
            };

            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "NSAction Evaluator Tests",
                    rootAssetsMemberId = assets.id,
                    rootSaveFileMemberId = save.id,
                    rootSessionMemberId = session.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = new Dictionary<string, JsonMember>
                {
                    [assets.id] = assets,
                    [save.id] = save,
                    [session.id] = session,
                    [counter.id] = counter,
                    [action.id] = action,
                    [staticAction.id] = staticAction,
                    [bumpOne.id] = bumpOne,
                    [elsewhere.id] = elsewhere,
                    [bumpTwo.id] = bumpTwo,
                    [boom.id] = boom,
                    [relay.id] = relay,
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["value-assets"] = ObjectValue("value-assets", "class-root"),
                    ["value-session"] = ObjectValue("value-session", "class-root"),
                    [SaveRootValueId] = ObjectValue(
                        SaveRootValueId,
                        "class-save-root",
                        ("Counter", CounterValueId),
                        ("OnDamaged", ActionValueId)),
                    [CounterValueId] = new NumberMemberValue
                    {
                        id = CounterValueId,
                        value = 0,
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    [ActionValueId] = new ActionMemberValue
                    {
                        id = ActionValueId,
                        value = storedListeners ?? new NeoActionValue(),
                        createdAt = "x",
                        updatedAt = "x",
                    },
                    [StaticActionValueId] = new ActionMemberValue
                    {
                        id = StaticActionValueId,
                        value = staticActionListeners ?? new NeoActionValue(),
                        createdAt = "x",
                        updatedAt = "x",
                    },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    // The static action is declared on a class the owning
                    // save row is not an instance of, so it can never be
                    // reached through a receiver — only through its own row.
                    ["class-root"] = SchemaClass(
                        "class-root",
                        "Root",
                        ("OnGlobalReset", StaticActionMemberId)),
                    ["class-save-root"] = SchemaClass(
                        "class-save-root",
                        "SaveRoot",
                        ("Counter", CounterMemberId),
                        ("OnDamaged", ActionMemberId),
                        ("BumpOne", BumpOneMemberId),
                        ("BumpTwo", BumpTwoMemberId),
                        ("Boom", BoomMemberId),
                        ("Relay", RelayMemberId)),
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            });
        }

        /// <summary>
        /// A void listener that folds its digit into the save counter, so the
        /// final value spells the order the listeners ran in.
        /// </summary>
        private static NSFunctionMember Bump(
            string id,
            string name,
            int digit) => VoidFunction(
                id,
                name,
                new AssignInstruction
                {
                    type = InstructionKind.Assign,
                    target = new WriteTarget
                    {
                        pointer = CounterPointer(),
                        typeInfo = IntType(),
                        writability = WritabilityKind.Save,
                    },
                    operatorValue = "=",
                    pointer = Arithmetic(
                        ArithmeticOpKind.Addition,
                        Arithmetic(
                            ArithmeticOpKind.Multiplication,
                            CounterPointer(),
                            Number(10)),
                        Number(digit)),
                });

        private static NSFunctionMember VoidFunction(
            string id,
            string name,
            params Instruction[] instructions) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.NSFunction,
            code = "compiled test listener",
            returnTypeInfo = new VoidTypeInfo
            {
                type = MemberKind.Void,
                required = true,
            },
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            deferred = false,
            action = new FunctionWithReturnType
            {
                parameters = new[]
                {
                    Parameter("__this__", new ClassTypeInfo
                    {
                        type = MemberKind.Class,
                        required = true,
                        classId = "class-save-root",
                    }),
                    Parameter("__root__", new ClassTypeInfo
                    {
                        type = MemberKind.Class,
                        required = true,
                        classId = "class-root",
                    }),
                },
                instructions = instructions,
                // A void NSFunction's compiled body carries the Null
                // statement-body result marker, not Void.
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Null,
                    required = true,
                },
            },
            createdAt = "x",
            updatedAt = "x",
        };

        private static Dictionary<string, object?> RuntimeRoot(
            NeoClient client,
            NSGetterEvaluator.Context ctx)
        {
            return new Dictionary<string, object?>
            {
                ["Assets"] = client.assets.value is ObjectMemberValue assets
                    ? NSGetterEvaluator.UnwrapRow(
                        assets, ctx, NeoValueOwnership.Asset)
                    : null,
                ["Save"] = client.save.value is ObjectMemberValue save
                    ? NSGetterEvaluator.UnwrapRow(
                        save, ctx, NeoValueOwnership.Save)
                    : null,
                ["Session"] = client.session.value is ObjectMemberValue session
                    ? NSGetterEvaluator.UnwrapRow(
                        session, ctx, NeoValueOwnership.Session)
                    : null,
            };
        }

        private static ClassMember RootClassMember(
            string id,
            string name,
            string classId,
            string valueId,
            string? storage = null) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Class,
            classId = classId,
            valueId = valueId,
            storage = storage,
            createdAt = "x",
            updatedAt = "x",
        };

        private static NeoSchemaClass SchemaClass(
            string id,
            string name,
            params (string key, string memberId)[] entries)
        {
            var schema = new Dictionary<string, string>();
            foreach ((string key, string memberId) in entries)
            {
                schema[key] = memberId;
            }
            return new NeoSchemaClass
            {
                id = id,
                projectId = ProjectId,
                name = name,
                schema = schema,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static ObjectMemberValue ObjectValue(
            string id,
            string classId,
            params (string key, string valueId)[] entries)
        {
            var value = new Dictionary<string, string>();
            foreach ((string key, string valueId) in entries)
            {
                value[key] = valueId;
            }
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = value,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static Variable Parameter(string id, TypeInfo typeInfo) => new()
        {
            id = id,
            typeInfo = typeInfo,
            pointer = new VariablePointer
            {
                type = PointerKind.Variable,
                variableId = id,
            },
        };

        private static KeyOfPointer CounterPointer() =>
            KeyOf(ThisVariable(), "Counter");

        private static KeyOfPointer KeyOf(Pointer receiver, string key) => new()
        {
            type = PointerKind.KeyOf,
            keyOf = new KeyOf
            {
                pointer = receiver,
                key = Text(key),
            },
        };

        private static OperationPointer Arithmetic(
            string op,
            params Pointer[] pointers) => new()
        {
            type = PointerKind.Operation,
            operation = new ArithmeticOperation
            {
                type = OperationKind.Arithmetic,
                arithmetic = new ArithmeticOpInfo
                {
                    type = op,
                    pointers = pointers,
                },
            },
        };

        private static VariablePointer RootVariable() => new()
        {
            type = PointerKind.Variable,
            variableId = "__root__",
        };

        private static VariablePointer ThisVariable() => new()
        {
            type = PointerKind.Variable,
            variableId = "__this__",
        };

        private static ValuePointer Number(double value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = IntType(),
                value = JToken.FromObject(value),
            },
        };

        private static ValuePointer Text(string value) => new()
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.String,
                    required = true,
                },
                value = JToken.FromObject(value),
            },
        };

        private static PrimitiveTypeInfo IntType() => new()
        {
            type = MemberKind.Int,
            required = true,
        };
    }
}
