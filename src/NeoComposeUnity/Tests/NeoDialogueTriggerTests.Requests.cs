// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>P105: one presenter, one queue, and the NeoScript requests.</summary>
    public partial class NeoDialogueTriggerTests
    {
        [Test]
        public void GroupRequests_RaiseOnTriggerBeforeStart()
        {
            var root = new TestDialogues(CreateClient());
            var states = new List<NeoDialogueState>();
            root.OnTrigger += dialogue => states.Add(dialogue.State);

            Assert.IsTrue(new TestStandardDialogueGroup(root, "group-priority").TryTrigger());
            root.LastPresented.Dispose();
            Assert.IsTrue(new TestLookupDialogueGroup(root, "group-lookup").TryTrigger(new TestLookupValue("lookup-value-b")));

            CollectionAssert.AreEqual(new[] { NeoDialogueState.Created, NeoDialogueState.Created }, states);
            Assert.AreEqual("dialogue-lookup-b", root.LastPresented.Id);
            Assert.IsTrue(root.LastPresented.IsStarted);
        }

        [Test]
        public void TryTrigger_WithoutPresenter_ReturnsFalseAndWarnsOnce()
        {
            var client = CreateClient();
            var root = CreateLoggedDialogues(client, out TestDialogueLogger logger);
            root.OnTrigger -= root.Present;

            Assert.IsFalse(root.TryTrigger("dialogue-direct"));
            Assert.IsFalse(new TestStandardDialogueGroup(root, "group-standard").TryTrigger());

            Assert.AreEqual(1, logger.Warnings.Count);
            StringAssert.Contains("Subscribe to Dialogues.OnTrigger", logger.Warnings[0]);
            Assert.AreEqual(0, client.ActiveDialogueCount);
        }

        [Test]
        public void TryTrigger_DuringDialogue_QueuesAndRunsOnFinishBeforeTheNextTurn()
        {
            var root = new TestDialogues(CreateClient());
            var log = new List<string>();
            NeoDialogueTextNode? shown = null;
            root.OnTrigger += dialogue =>
            {
                log.Add($"trigger {dialogue.Id}");
                dialogue.OnShow += node => shown = node;
            };

            Assert.IsTrue(root.TryTrigger("dialogue-direct", () => log.Add("finish dialogue-direct")));
            Assert.IsTrue(root.TryTrigger("dialogue-priority-high", () => log.Add("finish dialogue-priority-high")));
            CollectionAssert.AreEqual(new[] { "trigger dialogue-direct" }, log);

            shown!.Next();

            CollectionAssert.AreEqual(
                new[] { "trigger dialogue-direct", "finish dialogue-direct", "trigger dialogue-priority-high" },
                log);
        }

        [Test]
        public void TryTrigger_RunningOrQueuedDialogue_ReturnsFalse()
        {
            var root = new TestDialogues(CreateClient());
            NeoDialogueTextNode? shown = null;
            root.OnTrigger += dialogue => dialogue.OnShow += node => shown = node;

            Assert.IsTrue(root.TryTrigger("dialogue-direct"));
            Assert.IsFalse(root.TryTrigger("dialogue-direct"));
            Assert.IsTrue(new TestStandardDialogueGroup(root, "group-priority").TryTrigger());
            Assert.IsFalse(root.TryTrigger("dialogue-priority-high"));

            shown!.Next();
            Assert.AreEqual("dialogue-priority-high", root.LastPresented.Id);
            Assert.IsTrue(root.TryTrigger("dialogue-direct"));
            shown!.Next();

            Assert.AreEqual("dialogue-direct", root.LastPresented.Id);
            Assert.AreEqual(3, root.Presented.Count);
        }

        [Test]
        public void Queue_StartsInRequestOrder_IncludingARequestFromOnFinish()
        {
            var root = new TestDialogues(CreateClient());
            NeoDialogueTextNode? shown = null;
            root.OnTrigger += dialogue => dialogue.OnShow += node => shown = node;

            Assert.IsTrue(root.TryTrigger("dialogue-direct", () => root.TryTrigger("dialogue-visit-b")));
            Assert.IsTrue(root.TryTrigger("dialogue-priority-high"));
            Assert.IsTrue(root.TryTrigger("dialogue-visit-a"));
            for (int i = 0; i < 3; i++)
                shown!.Next();

            CollectionAssert.AreEqual(
                new[] { "dialogue-direct", "dialogue-priority-high", "dialogue-visit-a", "dialogue-visit-b" },
                root.Presented.ConvertAll(dialogue => dialogue.Id));
        }

        [Test]
        public void OnFinish_RunsAfterAFailureAndAPresenterDispose()
        {
            var root = CreateLoggedDialogues(CreateClient(), out TestDialogueLogger logger);
            var finished = new List<string>();

            Assert.IsTrue(root.TryTrigger("dialogue-action-error", () => finished.Add("failed")));
            Assert.AreEqual(NeoDialogueState.Disposed, root.LastPresented.State);
            Assert.AreEqual(1, logger.Exceptions.Count, "The failure is logged once.");
            Assert.IsTrue(root.TryTrigger("dialogue-direct", () => finished.Add("disposed")));
            root.LastPresented.Dispose();

            CollectionAssert.AreEqual(new[] { "failed", "disposed" }, finished);
        }

        [Test]
        public void PresenterThatDisposesAndThrows_IsLoggedAndEndsTheTurn()
        {
            var root = CreateLoggedDialogues(CreateClient(), out TestDialogueLogger logger);
            root.OnTrigger -= root.Present;
            var finished = new List<string>();
            root.OnTrigger += dialogue =>
            {
                dialogue.Dispose();
                throw new InvalidOperationException("presenter boom");
            };

            Assert.IsTrue(root.TryTrigger("dialogue-direct", () => finished.Add("thrown")));

            Assert.AreEqual(1, logger.Exceptions.Count);
            StringAssert.Contains("presenter boom", logger.Exceptions[0].Message);
            CollectionAssert.AreEqual(new[] { "thrown" }, finished);
        }

        [Test]
        public void ClientDispose_DropsTheQueueWithoutOnFinish()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            var finished = new List<string>();
            Assert.IsTrue(root.TryTrigger("dialogue-direct", () => finished.Add("running")));
            Assert.IsTrue(root.TryTrigger("dialogue-priority-high", () => finished.Add("queued")));

            client.Dispose();

            CollectionAssert.IsEmpty(finished);
            Assert.AreEqual(1, root.Presented.Count);
            Assert.AreEqual(NeoDialogueState.Disposed, root.Presented[0].State);
        }

        [Test]
        public void QueuedDialogue_RecordsNoVisitUntilItStarts()
        {
            var root = new TestDialogues(CreateClient(), memoryStore: new TestMemoryStore());
            NeoDialogueTextNode? shown = null;
            root.OnTrigger += dialogue => dialogue.OnShow += node => shown = node;

            Assert.IsTrue(root.TryTrigger("dialogue-direct"));
            Assert.IsTrue(root.TryTrigger("dialogue-priority-high"));
            Assert.AreEqual(1, root.VisitCount("dialogue-direct"));
            Assert.AreEqual(0, root.VisitCount("dialogue-priority-high"));

            shown!.Next();

            Assert.AreEqual(1, root.VisitCount("dialogue-priority-high"));
        }

        [Test]
        public void NeoScript_EveryRequestKind_PresentsItsDialogue()
        {
            var client = CreateScriptClient(
                Request(DialogueOp.TryTrigger, StringPointer("dialogue-direct")),
                GroupRequest(DialogueOp.TryTrigger, "group-priority"),
                GroupRequest(DialogueOp.TryTrigger, "group-lookup", LookupDirectValue()),
                Request(DialogueOp.TryTrigger, GreetingId()));
            var root = CreateScriptDialogues(client);

            for (int i = 0; i < 4; i++)
            {
                Assert.AreEqual(true, RunScript(client, i), $"fn-{i}");
                root.LastPresented.Dispose();
            }

            CollectionAssert.AreEqual(
                new[] { "dialogue-direct", "dialogue-priority-high", "dialogue-lookup-direct", "dialogue-visit-a" },
                root.Presented.ConvertAll(dialogue => dialogue.Id));
        }

        [Test]
        public void NeoScript_EveryRequestKind_RunsFromAnOnFinishLambda()
        {
            // TryTrigger("dialogue-direct", () => { Priority.TryTrigger(() => {
            //   Lookup.TryTrigger(entry, () => { Greeting.TryTrigger(); }); }); });
            Pointer reference = Request(DialogueOp.TryTrigger, GreetingId());
            Pointer lookup = GroupRequest(DialogueOp.TryTrigger, "group-lookup", LookupDirectValue(), Lambda(reference));
            Pointer group = GroupRequest(DialogueOp.TryTrigger, "group-priority", onFinish: Lambda(lookup));
            var client = CreateScriptClient(
                Request(DialogueOp.TryTrigger, StringPointer("dialogue-direct"), Lambda(group)));
            var root = CreateScriptDialogues(client);

            Assert.AreEqual(true, RunScript(client, 0));
            for (int i = 0; i < 4; i++)
                root.LastPresented.Dispose();

            CollectionAssert.AreEqual(
                new[] { "dialogue-direct", "dialogue-priority-high", "dialogue-lookup-direct", "dialogue-visit-a" },
                root.Presented.ConvertAll(dialogue => dialogue.Id));
        }

        [Test]
        public void NeoScript_OnFinish_WritesThroughThisAfterTheDialogueEnds()
        {
            // int bonus = 4;
            // root.Dialogues.TryTrigger("dialogue-direct", () => { this.Score = this.Score + bonus; });
            // return root.Dialogues.TryTrigger("dialogue-priority-high", this.TenfoldScore);
            Pointer score = KeyOfPointer(ThisPointer(), "Score");
            Instruction AssignScore(Pointer value) => new AssignInstruction
            {
                type = InstructionKind.Assign,
                target = new WriteTarget
                {
                    pointer = score,
                    typeInfo = IntTypeInfo(),
                    writability = WritabilityKind.Save,
                },
                operatorValue = "=",
                pointer = value,
            };
            var voidType = new VoidTypeInfo
            {
                type = MemberKind.Void,
                required = true,
            };
            var onFinishType = new DelegateTypeInfo
            {
                type = MemberKind.NSDelegate,
                required = false,
                returnTypeInfo = voidType,
                argumentTypes = Array.Empty<TypeInfo>(),
            };
            // A void body's compiled action returns null.
            var actionVoidType = new PrimitiveTypeInfo
            {
                type = MemberKind.Null,
                required = true,
            };
            FunctionWithReturnType closure = ScriptBody(
                actionVoidType,
                AssignScore(ArithmeticPointer(
                    ArithmeticOpKind.Addition,
                    score,
                    new VariablePointer { type = PointerKind.Variable, variableId = "__capture_0_0__" })));
            var bonus = ScriptParameter("__capture_0_0__");
            bonus.typeInfo = IntTypeInfo();
            closure.parameters = new[] { closure.parameters[0], closure.parameters[1], bonus };
            var lambda = new DelegateClosurePointer
            {
                type = PointerKind.DelegateClosure,
                typeInfo = onFinishType,
                action = closure,
                captures = new Pointer[] { new VariablePointer { type = PointerKind.Variable, variableId = "bonus" } },
            };
            var methodGroup = new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = onFinishType,
                    value = new JObject { ["memberId"] = "fn-tenfold", ["valueId"] = null },
                },
            };
            var client = CreateClient(data =>
            {
                data.members["fn-talk"] = InstanceFunction(
                    "fn-talk",
                    "Talk",
                    BoolTypeInfo(),
                    ScriptBody(
                        BoolTypeInfo(),
                        new VariableInstruction
                        {
                            type = InstructionKind.Variable,
                            variable = new Variable
                            {
                                id = "bonus",
                                typeInfo = IntTypeInfo(),
                                pointer = NumberPointer(4),
                            },
                        },
                        new FunctionCallInstruction
                        {
                            type = InstructionKind.FunctionCall,
                            call = Request(DialogueOp.TryTrigger, StringPointer("dialogue-direct"), lambda),
                        },
                        Return(Request(DialogueOp.TryTrigger, StringPointer("dialogue-priority-high"), methodGroup))));
                data.members["fn-tenfold"] = InstanceFunction(
                    "fn-tenfold",
                    "TenfoldScore",
                    voidType,
                    ScriptBody(
                        actionVoidType,
                        AssignScore(ArithmeticPointer(ArithmeticOpKind.Multiplication, score, NumberPointer(10)))));
                data.classes["class-root"].schema["Talk"] = "fn-talk";
                data.classes["class-root"].schema["TenfoldScore"] = "fn-tenfold";
            });
            var root = CreateLoggedDialogues(client, out TestDialogueLogger logger);
            var talk = new NeoMemberNSFunction(client, "fn-talk", null, NeoValueOwnership.Save);

            Assert.AreEqual(true, talk.Invoke("root-save-default-value", Array.Empty<object?>()));
            Assert.AreEqual(1d, SaveScore(client), "onFinish waits for the dialogue to end.");
            root.LastPresented.Dispose();
            CollectionAssert.IsEmpty(logger.Exceptions);
            Assert.AreEqual(5d, SaveScore(client));
            root.LastPresented.Dispose();

            Assert.AreEqual(50d, SaveScore(client));
        }

        [Test]
        public void NeoScript_CanTrigger_MatchesCSharpAndQueuesNothing()
        {
            var client = CreateScriptClient(
                Request(DialogueOp.CanTrigger, StringPointer("dialogue-direct")),
                Request(DialogueOp.CanTrigger, StringPointer("dialogue-condition-false")),
                GroupRequest(DialogueOp.CanTrigger, "group-priority"),
                GroupRequest(DialogueOp.CanTrigger, "group-lookup", LookupDirectValue()),
                Request(DialogueOp.CanTrigger, GreetingId()));
            var root = CreateScriptDialogues(client);
            var expected = new[]
            {
                root.CanTrigger("dialogue-direct"),
                root.CanTrigger("dialogue-condition-false"),
                new TestStandardDialogueGroup(root, "group-priority").CanTrigger(),
                new TestLookupDialogueGroup(root, "group-lookup").CanTrigger(new TestLookupValue("lookup-value-direct")),
                new NeoDialogueReference(client, "dialogue-visit-a").CanTrigger(),
            };

            for (int i = 0; i < expected.Length; i++)
                Assert.AreEqual(expected[i], RunScript(client, i), $"fn-{i}");

            CollectionAssert.AreEqual(new[] { true, false, true, true, true }, expected);
            CollectionAssert.IsEmpty(root.Presented);
            Assert.AreEqual(0, client.ActiveDialogueCount);
        }

        [Test]
        public void NeoScript_CanTriggerInAGetter_RecomputesWhenItsConditionReadChanges()
        {
            var client = CreateClient(data =>
            {
                data.dialogues["dialogue-score-gated"] = Dialogue(
                    "dialogue-score-gated",
                    "Score Gated",
                    "group-standard",
                    conditions: new[]
                    {
                        Condition(BoolExpressionGetter(
                            RootKeyPointer("Save", "Score"),
                            OperatorKind.GreaterThan,
                            NumberPointer(1))),
                    });
                data.members["member-can-gate"] = new NSPropertyMember
                {
                    id = "member-can-gate",
                    projectId = ProjectId,
                    name = "CanGate",
                    kind = MemberKind.NSProperty,
                    code = "compiled test getter",
                    returnTypeInfo = BoolTypeInfo(),
                    getter = ScriptBody(
                        BoolTypeInfo(),
                        Return(Request(DialogueOp.CanTrigger, StringPointer("dialogue-score-gated")))),
                };
                data.classes["class-root"].schema["CanGate"] = "member-can-gate";
            });
            CreateScriptDialogues(client);
            Assert.IsTrue(client.TryGetMember("member-can-gate", out NSPropertyMember? getter));
            var node = new NeoMemberNSProperty(client, getter!, null, NeoValueOwnership.Save);
            Assert.AreEqual(false, node.Compute("root-save-default-value").value);
            Assert.IsTrue(client.TryGetMember("member-score", out Member? score));

            Assert.IsTrue(client.TryWriteLeaf(
                NeoValueOwnership.Save,
                new NumberMemberValue { id = "score-default-value", value = 5 },
                score!,
                "value"));

            Assert.AreEqual(true, node.Compute("root-save-default-value").value);
        }

        /// <summary>
        /// P105 §2.5. The .NET half of the dialogue request parity gate, over a
        /// verbatim copy of the fixture the web evaluator runs.
        /// </summary>
        [Test]
        public void NeoScript_RequestsMatchTheSharedParityFixture()
        {
            NeoClient client = CreateClient();
            TestDialogues root = CreateLoggedDialogues(client, out _);
            root.OnTrigger -= root.Present;
            NSGetterEvaluator.Context ctx = client.CreateGetterContext(NeoValueOwnership.Save);
            var cases = (JArray)JObject.Parse(NeoScriptDialogueRequestParityFixture.Json)["evaluateCases"]!;
            Assert.AreEqual(7, cases.Count, "The vendored fixture lost or gained cases.");

            foreach (JObject testCase in cases)
            {
                string name = testCase["name"]!.Value<string>()!;
                var getter = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = Array.Empty<Variable>(),
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = testCase["pointer"]!.ToObject<Pointer>()!,
                        },
                    },
                    typeInfo = BoolTypeInfo(),
                };
                if (testCase["expectedError"] is JToken expectedError)
                {
                    var error = Assert.Throws<NSGetterRuntimeError>(() => NSGetterEvaluator.Evaluate(getter, ctx), name);
                    Assert.AreEqual(expectedError.Value<string>(), error.Message, name);
                    continue;
                }
                Assert.AreEqual(testCase["expectedResult"]!.ToObject<bool?>(), NSGetterEvaluator.Evaluate(getter, ctx), name);
            }
            CollectionAssert.IsEmpty(root.Presented);
        }

        // Each body is the return value of a static NeoScript function `fn-{i}`.
        // root.Save.Greeting is a NeoDialogueReference to dialogue-visit-a.
        private static NeoClient CreateScriptClient(params Pointer[] bodies)
        {
            return CreateClient(data =>
            {
                for (int i = 0; i < bodies.Length; i++)
                {
                    data.members[$"fn-{i}"] = new NSFunctionMember
                    {
                        id = $"fn-{i}",
                        projectId = ProjectId,
                        name = $"Fn{i}",
                        kind = MemberKind.NSFunction,
                        code = "compiled test function",
                        returnTypeInfo = BoolTypeInfo(),
                        argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                        Dispatch = NeoFunctionDispatchKind.Synchronous,
                        Modifier = NeoMemberModifierKind.Static,
                        action = ScriptBody(BoolTypeInfo(), Return(bodies[i])),
                    };
                }
                data.members["member-greeting"] = new DialogueLookupMember
                {
                    id = "member-greeting",
                    projectId = ProjectId,
                    name = "Greeting",
                    kind = MemberKind.DialogueLookup,
                };
                data.classes["class-root"].schema["Greeting"] = "member-greeting";
                ((ObjectMemberValue)data.values["root-save-default-value"]).value!["Greeting"] = "greeting-value";
                data.values["greeting-value"] = new ArrayMemberValue
                {
                    id = "greeting-value",
                    value = new[] { "dialogue-visit-a" },
                };
            });
        }

        private static NSFunctionMember InstanceFunction(
            string id,
            string name,
            TypeInfo returnType,
            FunctionWithReturnType action)
        {
            return new NSFunctionMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.NSFunction,
                code = "compiled test function",
                returnTypeInfo = returnType,
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                Dispatch = NeoFunctionDispatchKind.Synchronous,
                action = action,
            };
        }

        private static double SaveScore(NeoClient client)
        {
            var read = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                instructions = new Instruction[] { Return(RootKeyPointer("Save", "Score")) },
                typeInfo = IntTypeInfo(),
            };
            NSGetterEvaluator.Context ctx = client.CreateGetterContext(NeoValueOwnership.Save);
            ctx.BindRoot(NeoScriptValueMarshaller.ResolveRoot(client, ctx));
            return Convert.ToDouble(NSGetterEvaluator.Evaluate(read, ctx));
        }

        private static TestDialogues CreateScriptDialogues(NeoClient client)
        {
            return new TestDialogues(
                client,
                valueResolver: valueId => ResolveClientValue(client, valueId));
        }

        private static object? RunScript(NeoClient client, int index)
        {
            return new NeoMemberNSFunction(client, $"fn-{index}", null, NeoValueOwnership.Save).InvokeStatic();
        }

        private static Pointer Request(string op, Pointer dialogueId, Pointer? onFinish = null)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new DialogueFunction
                {
                    type = FunctionKind.Dialogue,
                    info = new FunctionDialogueInfo
                    {
                        op = op,
                        dialogueIdPointer = dialogueId,
                        onFinishPointer = onFinish,
                    },
                },
            };
        }

        private static Pointer GroupRequest(string op, string groupId, Pointer? value = null, Pointer? onFinish = null)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new DialogueGroupFunction
                {
                    type = FunctionKind.DialogueGroup,
                    info = new FunctionDialogueGroupInfo
                    {
                        op = op,
                        groupId = groupId,
                        valuePointer = value,
                        onFinishPointer = onFinish,
                    },
                },
            };
        }

        private static Pointer LookupDirectValue()
        {
            return new ReferencePointer
            {
                type = PointerKind.Reference,
                valueId = "lookup-value-direct",
            };
        }

        // root.Save.Greeting.TryTrigger() reads the reference's id at index 0.
        private static Pointer GreetingId()
        {
            return new KeyOfPointer
            {
                type = PointerKind.KeyOf,
                keyOf = new KeyOf
                {
                    pointer = RootKeyPointer("Save", "Greeting"),
                    key = NumberPointer(0),
                },
            };
        }

        // () => { call; }
        private static Pointer Lambda(Pointer call)
        {
            var voidType = new PrimitiveTypeInfo
            {
                type = MemberKind.Null,
                required = false,
            };
            return new DelegateClosurePointer
            {
                type = PointerKind.DelegateClosure,
                typeInfo = new DelegateTypeInfo
                {
                    type = MemberKind.NSDelegate,
                    required = true,
                    returnTypeInfo = voidType,
                    argumentTypes = Array.Empty<TypeInfo>(),
                },
                action = ScriptBody(
                    voidType,
                    new FunctionCallInstruction
                    {
                        type = InstructionKind.FunctionCall,
                        call = call,
                    }),
                captures = Array.Empty<Pointer>(),
            };
        }

        private static FunctionWithReturnType ScriptBody(TypeInfo returnType, params Instruction[] instructions)
        {
            return new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[] { ScriptParameter("__this__"), ScriptParameter("__root__") },
                instructions = instructions,
                typeInfo = returnType,
            };
        }

        private static Variable ScriptParameter(string id)
        {
            return new Variable
            {
                id = id,
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Null,
                    required = true,
                },
                pointer = new VariablePointer
                {
                    type = PointerKind.Variable,
                    variableId = id,
                },
            };
        }

        private static ReturnInstruction Return(Pointer pointer)
        {
            return new ReturnInstruction
            {
                type = InstructionKind.Return,
                pointer = pointer,
            };
        }
    }
}
