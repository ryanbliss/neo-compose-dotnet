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
    /// P62 cross-language parity for NSAction members: a C# subscription
    /// mints the identity NeoScript's <c>+=</c> lowers to, a C#
    /// <c>Invoke</c> binds the same owner receiver the <c>callAction</c>
    /// pointer binds, and the generated setter's identity check is pure
    /// reference identity (§5.2).
    /// </summary>
    public class NeoActionOwnerIdentityTests
    {
        private const string ProjectId = "project-action-owner";
        private const string SaveRootValueId = "value-save";
        private const string SessionRootValueId = "value-session";
        private const string ActionValueId = "value-action";
        private const string CounterValueId = "value-counter";
        private const string ActionMemberId = "member-action";
        private const string CounterMemberId = "member-counter";
        private const string BumpOneMemberId = "member-bump-one";
        private const string SaveRootClassId = "class-save-root";
        private const string RootClassId = "class-root";

        // -------------------------------------------------------------
        // Owner receiver on the C# entry point (spec §3.3)
        // -------------------------------------------------------------

        /// <summary>
        /// The spec's §2 example invoked from C#: an authored
        /// <c>= [BumpOne]</c> default stores <c>valueId: null</c>, and that
        /// listener's <c>this.Counter</c> write has to land on the row that
        /// owns the action — the same binding the NeoScript call performs.
        /// </summary>
        [Test]
        public void Invoke_BindsAValueIdLessListenerToTheOwningRow()
        {
            using NeoClient client = BuildClient(
                storedListeners: ListenerSet(Listener(BumpOneMemberId, null)));
            NeoMemberActionWritable node = ActionNode(client);

            node.Invoke();

            Assert.AreEqual(1d, CounterValue(client));
        }

        [Test]
        public void Invoke_WithNoListenersStillTouchesNothing()
        {
            using NeoClient client = BuildClient();
            NeoMemberActionWritable node = ActionNode(client);

            Assert.DoesNotThrow(() => node.Invoke());
            Assert.AreEqual(0d, CounterValue(client));
        }

        /// <summary>
        /// A failing listener is reported under
        /// <c>{actionMemberName}[{owningRowId ?? "default"}]</c> — the action
        /// member and the row it fanned out from, byte-identical to the frame
        /// the evaluator builds for the same failure. The action's own
        /// value-row id would name the same failure differently depending on
        /// which language invoked it.
        /// </summary>
        [Test]
        public void Invoke_ReportsTheActionFrameForAFailingListener()
        {
            using NeoClient client = BuildClient(
                storedListeners: ListenerSet(Listener(MissingMemberId, null)));
            NeoMemberActionWritable node = ActionNode(client);

            NSGetterRuntimeError error =
                Assert.Throws<NSGetterRuntimeError>(() => node.Invoke())!;

            StringAssert.Contains(
                $"OnDamaged[{SaveRootValueId}] listener 0 threw:",
                error.Message);
            // Not the action's own value row — that is a different id.
            StringAssert.DoesNotContain(ActionValueId, error.Message);
        }

        // -------------------------------------------------------------
        // Listener identity parity (spec §5.2)
        // -------------------------------------------------------------

        [Test]
        public void Subscribing_OverTheOwningRowStoresANullValueId()
        {
            using NeoClient client = BuildClient();
            NeoMemberActionWritable node = ActionNode(client);
            using var owner = new SaveRootValue(client, SaveRootNode(client));

            node.Bind().AddListener((Action)owner.BumpOne);

            NeoDelegateValue stored = node.CurrentListeners()[0];
            Assert.AreEqual(BumpOneMemberId, stored.memberId);
            Assert.IsNull(
                stored.valueId,
                "a subscription on the action's own row is the null-valueId identity NeoScript mints");
            // Byte-identical to what `this.OnDamaged += this.BumpOne` lowers
            // to, so the two languages deduplicate and remove each other's
            // entries.
            Assert.AreEqual(
                JsonConvert.SerializeObject(
                    new NeoDelegateValue
                    {
                        memberId = BumpOneMemberId,
                        valueId = null,
                    }),
                JsonConvert.SerializeObject(stored));
        }

        [Test]
        public void Subscribing_OverAForeignRowKeepsThatRowsId()
        {
            using NeoClient client = BuildClient();
            NeoMemberActionWritable node = ActionNode(client);
            using var foreignRow = new SessionRootValue(
                client,
                SessionRootNode(client));

            node.Bind().AddListener((Action)foreignRow.BumpOne);

            NeoDelegateValue stored = node.CurrentListeners()[0];
            Assert.AreEqual(SessionRootValueId, stored.valueId);
        }

        [Test]
        public void Unsubscribing_OverTheOwningRowRemovesTheAuthoredEntry()
        {
            using NeoClient client = BuildClient(
                storedListeners: ListenerSet(Listener(BumpOneMemberId, null)));
            NeoMemberActionWritable node = ActionNode(client);
            using var owner = new SaveRootValue(client, SaveRootNode(client));

            node.Bind().RemoveListener((Action)owner.BumpOne);

            CollectionAssert.IsEmpty(node.CurrentListeners());
        }

        [Test]
        public void Subscribing_TwiceOverTheOwningRowIsOneListener()
        {
            using NeoClient client = BuildClient(
                storedListeners: ListenerSet(Listener(BumpOneMemberId, null)));
            NeoMemberActionWritable node = ActionNode(client);
            using var owner = new SaveRootValue(client, SaveRootNode(client));

            node.Bind().AddListener((Action)owner.BumpOne);

            Assert.AreEqual(
                1,
                node.CurrentListeners().Count,
                "the authored entry and the C# subscription are one identity");
        }

        // -------------------------------------------------------------
        // Setter identity check (spec §5.2)
        // -------------------------------------------------------------

        [Test]
        public void RequireSameAction_AcceptsTheMembersOwnLiveAction()
        {
            using NeoClient client = BuildClient();
            NeoMemberActionWritable node = ActionNode(client);
            NeoAction action = node.Bind();

            Assert.DoesNotThrow(() => NeoGeneratedTypesSupport.RequireSameAction(
                action,
                action,
                "SaveRoot.OnDamaged"));
        }

        [Test]
        public void RequireSameAction_RejectsAnotherRowsAction()
        {
            using NeoClient client = BuildClient();
            NeoMemberActionWritable node = ActionNode(client);
            using var other = new NeoMemberActionWritable(
                client,
                ActionMemberRecord(),
                ActionValueId,
                NeoValueOwnership.Save);

            NeoActionReassignmentException error =
                Assert.Throws<NeoActionReassignmentException>(
                    () => NeoGeneratedTypesSupport.RequireSameAction(
                        other.Bind(),
                        node.Bind(),
                        "SaveRoot.OnDamaged"))!;

            StringAssert.Contains("other than its own", error.Message);
        }

        [Test]
        public void RequireSameAction_RejectsNull()
        {
            using NeoClient client = BuildClient();
            NeoMemberActionWritable node = ActionNode(client);

            Assert.Throws<NeoActionReassignmentException>(
                () => NeoGeneratedTypesSupport.RequireSameAction(
                    null,
                    node.Bind(),
                    "SaveRoot.OnDamaged"));
        }

        /// <summary>
        /// The member's Neo display name and its C# schema key are
        /// independent — renaming a member never rewrites the key — so the
        /// check must not compare the label against either.
        /// </summary>
        [Test]
        public void RequireSameAction_IgnoresALabelThatDiffersFromTheMemberName()
        {
            using NeoClient client = BuildClient();
            NeoMemberActionWritable node = ActionNode(client);
            NeoAction action = node.Bind();

            Assert.DoesNotThrow(() => NeoGeneratedTypesSupport.RequireSameAction(
                action,
                action,
                "SaveRoot.SomethingElseEntirely"));
        }

        // -------------------------------------------------------------
        // Fixture
        // -------------------------------------------------------------

        private const string MissingMemberId = "member-does-not-exist";

        private static NeoMemberActionWritable ActionNode(NeoClient client) =>
            SaveRootNode(client).Get<NeoMemberActionWritable>("OnDamaged");

        private static NeoMemberClassWritable SaveRootNode(NeoClient client) =>
            client.save;

        private static NeoMemberClass SessionRootNode(NeoClient client) =>
            client.session;

        private static double CounterValue(NeoClient client)
        {
            Assert.IsTrue(client.TryGetValue(
                NeoValueOwnership.Save,
                CounterValueId,
                out NumberMemberValue? counter));
            return counter!.value ?? 0d;
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
            string? valueId) => new()
        {
            memberId = memberId,
            valueId = valueId,
        };

        private static ActionMember ActionMemberRecord() => new()
        {
            id = ActionMemberId,
            projectId = ProjectId,
            name = "OnDamaged",
            kind = MemberKind.NSAction,
            Requirement = NeoMemberRequirementKind.Optional,
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            valueId = ActionValueId,
            Storage = NeoMemberStorage.Save,
            createdAt = "x",
            updatedAt = "x",
        };

        private static NeoClient BuildClient(NeoActionValue? storedListeners = null)
        {
            ClassMember assets = RootClassMember(
                "member-root-assets", "Assets", RootClassId, "value-assets");
            ClassMember save = RootClassMember(
                "member-root-save", "Save", SaveRootClassId, SaveRootValueId, NeoMemberStorage.Save);
            ClassMember session = RootClassMember(
                "member-root-session",
                "Session",
                RootClassId,
                SessionRootValueId,
                NeoMemberStorage.Session);
            var counter = new IntMember
            {
                id = CounterMemberId,
                projectId = ProjectId,
                name = "Counter",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = CounterValueId,
                Storage = NeoMemberStorage.Save,
                createdAt = "x",
                updatedAt = "x",
            };
            ActionMember action = ActionMemberRecord();
            NSFunctionMember bumpOne = BumpOne();

            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "NSAction Owner Identity Tests",
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
                    [bumpOne.id] = bumpOne,
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["value-assets"] = ObjectValue("value-assets", RootClassId),
                    [SessionRootValueId] = ObjectValue(
                        SessionRootValueId,
                        RootClassId),
                    [SaveRootValueId] = ObjectValue(
                        SaveRootValueId,
                        SaveRootClassId,
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
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [RootClassId] = SchemaClass(RootClassId, "Root"),
                    [SaveRootClassId] = SchemaClass(
                        SaveRootClassId,
                        "SaveRoot",
                        ("Counter", CounterMemberId),
                        ("OnDamaged", ActionMemberId),
                        ("BumpOne", BumpOneMemberId)),
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            });
        }

        /// <summary>
        /// A void NSFunction that increments the save counter through its own
        /// <c>this</c>, so a listener that ran receiver-less would throw
        /// rather than quietly do nothing.
        /// </summary>
        private static NSFunctionMember BumpOne() => new()
        {
            id = BumpOneMemberId,
            projectId = ProjectId,
            name = "BumpOne",
            kind = MemberKind.NSFunction,
            code = "compiled test listener",
            returnTypeInfo = new VoidTypeInfo
            {
                type = MemberKind.Void,
                required = true,
            },
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            Dispatch = NeoFunctionDispatchKind.Synchronous,
            action = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[]
                {
                    Parameter("__this__", new ClassTypeInfo
                    {
                        type = MemberKind.Class,
                        required = true,
                        classId = SaveRootClassId,
                    }),
                    Parameter("__root__", new ClassTypeInfo
                    {
                        type = MemberKind.Class,
                        required = true,
                        classId = RootClassId,
                    }),
                },
                instructions = new Instruction[]
                {
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
                            CounterPointer(),
                            Number(1)),
                    },
                },
                typeInfo = new PrimitiveTypeInfo
                {
                    type = MemberKind.Null,
                    required = true,
                },
            },
            createdAt = "x",
            updatedAt = "x",
        };

        private static ClassMember RootClassMember(
            string id,
            string name,
            string classId,
            string valueId,
            NeoMemberStorage storage = NeoMemberStorage.Inherit) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Class,
            classId = classId,
            valueId = valueId,
            Storage = storage,
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

        /// <summary>
        /// Stands in for the generated wrapper of the row that owns the
        /// action: its stamped method group is the C# spelling of
        /// <c>this.BumpOne</c>.
        /// </summary>
        private sealed class SaveRootValue : NeoGeneratedClassValue
        {
            public SaveRootValue(NeoClient client, NeoMemberClassWritable node)
                : base(
                    client,
                    node,
                    SaveRootClassId,
                    isReadOnly: false,
                    NeoValueOwnership.Save)
            {
            }

            [NeoMemberMethod(BumpOneMemberId)]
            public void BumpOne() { }
        }

        /// <summary>
        /// A different row exposing the same member: its subscription keeps
        /// that row's id, because it is a genuinely foreign receiver.
        /// </summary>
        private sealed class SessionRootValue : NeoGeneratedClassValue
        {
            public SessionRootValue(NeoClient client, NeoMemberClass node)
                : base(client, node, RootClassId)
            {
            }

            [NeoMemberMethod(BumpOneMemberId)]
            public void BumpOne() { }
        }
    }
}
