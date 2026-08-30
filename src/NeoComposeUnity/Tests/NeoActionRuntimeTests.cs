// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using NUnit.Framework;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P62 SDK runtime coverage for NSAction members. Fixtures are built
    /// directly rather than through the sample's generated types, which carry
    /// no action members yet.
    /// </summary>
    public class NeoActionRuntimeTests
    {
        private const string ProjectId = "project-action";
        private const string ActionMemberId = "action-member";
        private const string ActionValueId = "action-value";
        private const string FirstListenerId = "listener-first";
        private const string SecondListenerId = "listener-second";

        [Test]
        public void ActionMemberDto_DeserializesAtOrdinal26WithAListenerSet()
        {
            const string json = @"{
                'id':'action-member','projectId':'project-action','name':'OnDamaged','kind':26,
                'argumentTypes':[{'name':'amount','type':2,'required':true}],
                'defaultValue':{'value':{'listeners':[
                    {'memberId':'listener-first','valueId':null},
                    {'memberId':'listener-second','valueId':'receiver-value'}
                ]}},
                'createdAt':'x','updatedAt':'x'
            }";

            var member = (ActionMember)JsonConvert.DeserializeObject<JsonMember>(json)!;

            Assert.AreEqual(26, (int)member.kind);
            Assert.AreEqual("amount", member.argumentTypes[0].name);
            Assert.IsInstanceOf<ActionMemberValueBase>(member.defaultValue);
            Assert.AreEqual(2, member.defaultValue!.value!.listeners.Count);
            Assert.AreEqual(
                FirstListenerId,
                member.defaultValue.value.listeners[0].memberId);
            Assert.IsNull(member.defaultValue.value.listeners[0].valueId);
            Assert.AreEqual(
                "receiver-value",
                member.defaultValue.value.listeners[1].valueId);
        }

        [Test]
        public void ActionValue_RoundTripsThroughTheStrictConverter()
        {
            var value = new NeoActionValue();
            value.listeners.Add(MemberTarget(FirstListenerId, null));
            value.listeners.Add(MemberTarget(SecondListenerId, "receiver-value"));

            string json = JsonConvert.SerializeObject(value);
            NeoActionValue? restored =
                JsonConvert.DeserializeObject<NeoActionValue>(json);

            Assert.NotNull(restored);
            Assert.AreEqual(2, restored!.listeners.Count);
            Assert.AreEqual(FirstListenerId, restored.listeners[0].memberId);
            Assert.IsNull(restored.listeners[0].valueId);
            Assert.AreEqual("receiver-value", restored.listeners[1].valueId);
        }

        [Test]
        public void ActionValue_RejectsAClosureListener()
        {
            const string json =
                @"{'listeners':[{'code':'() => 1','action':{'compilerRevision':7}}]}";

            var error = Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<NeoActionValue>(json));

            StringAssert.Contains("listener 0", error!.Message);
            StringAssert.Contains("closure", error.Message);
        }

        [Test]
        public void ActionValue_RejectsADuplicateListenerIdentity()
        {
            const string json = @"{'listeners':[
                {'memberId':'listener-first','valueId':null},
                {'memberId':'listener-first','valueId':null}
            ]}";

            var error = Assert.Throws<JsonSerializationException>(
                () => JsonConvert.DeserializeObject<NeoActionValue>(json));

            StringAssert.Contains("duplicates an earlier listener identity", error!.Message);
        }

        [Test]
        public void Bind_ReturnsOneCachedReferenceStableInstance()
        {
            using NeoClient client = BuildClient();
            using var node = new NeoMemberAction(client, ActionMember(), null);

            NeoAction<long> first = node.Bind<long>();
            NeoAction<long> second = node.Bind<long>();

            Assert.AreSame(first, second);
            Assert.AreEqual(0, first.Listeners.Count);
        }

        [Test]
        public void Bind_RejectsASecondArityOnTheSameNode()
        {
            using NeoClient client = BuildClient();
            using var node = new NeoMemberAction(client, ActionMember(), null);
            node.Bind<long>();

            Assert.Throws<InvalidOperationException>(() => node.Bind());
        }

        [Test]
        public void AddListener_PersistsTheSetAndIsIdempotentByIdentity()
        {
            using NeoClient client = BuildClient();
            using var node = new NeoMemberActionWritable(
                client,
                ActionMember(ActionValueId),
                null,
                NeoValueOwnership.Session);

            node.AddListener(MemberTarget(FirstListenerId, null));
            node.AddListener(MemberTarget(SecondListenerId, "receiver-value"));
            node.AddListener(MemberTarget(FirstListenerId, null));

            Assert.AreEqual(2, node.CurrentListeners().Count);
            Assert.AreEqual(FirstListenerId, node.CurrentListeners()[0].memberId);
            Assert.AreEqual(SecondListenerId, node.CurrentListeners()[1].memberId);
            Assert.IsTrue(
                client.TryGetWritableValue(
                    NeoValueOwnership.Session,
                    ActionValueId,
                    out ActionMemberValue? stored),
                "a subscription is a durable member-value write");
            Assert.AreEqual(2, stored!.value!.listeners.Count);
        }

        [Test]
        public void AddListener_TreatsTheSameMemberOnDifferentValuesAsDistinct()
        {
            using NeoClient client = BuildClient();
            using var node = new NeoMemberActionWritable(
                client,
                ActionMember(ActionValueId),
                null,
                NeoValueOwnership.Session);

            node.AddListener(MemberTarget(FirstListenerId, null));
            node.AddListener(MemberTarget(FirstListenerId, "receiver-value"));

            Assert.AreEqual(2, node.CurrentListeners().Count);
        }

        [Test]
        public void RemoveListener_DropsTheIdentityAndIgnoresAbsentOnes()
        {
            using NeoClient client = BuildClient();
            using var node = new NeoMemberActionWritable(
                client,
                ActionMember(ActionValueId),
                null,
                NeoValueOwnership.Session);
            node.AddListener(MemberTarget(FirstListenerId, null));
            node.AddListener(MemberTarget(SecondListenerId, null));

            node.RemoveListener(MemberTarget(FirstListenerId, null));
            node.RemoveListener(MemberTarget(FirstListenerId, null));

            Assert.AreEqual(1, node.CurrentListeners().Count);
            Assert.AreEqual(SecondListenerId, node.CurrentListeners()[0].memberId);
        }

        [Test]
        public void SetListeners_RejectsDuplicateIdentities()
        {
            using NeoClient client = BuildClient();
            using var node = new NeoMemberActionWritable(
                client,
                ActionMember(ActionValueId),
                null,
                NeoValueOwnership.Session);

            Assert.Throws<NeoActionListenerException>(() => node.SetListeners(
                new[]
                {
                    MemberTarget(FirstListenerId, null),
                    MemberTarget(FirstListenerId, null),
                }));
        }

        [Test]
        public void SetListeners_RejectsAClosureEntry()
        {
            using NeoClient client = BuildClient();
            using var node = new NeoMemberActionWritable(
                client,
                ActionMember(ActionValueId),
                null,
                NeoValueOwnership.Session);

            Assert.Throws<NeoActionListenerException>(() => node.SetListeners(
                new[]
                {
                    new NeoDelegateValue
                    {
                        code = "() => 1",
                        action = new FunctionWithReturnType { compilerRevision = 7 },
                    },
                }));
        }

        [Test]
        public void Subscribing_ThroughAReadOnlyNodeIsRejected()
        {
            using NeoClient client = BuildClient();
            using var node = new NeoMemberAction(client, ActionMember(ActionValueId), null);
            NeoAction action = node.Bind();

            Assert.Throws<InvalidOperationException>(
                () => action.AddListener(MemberTarget(FirstListenerId, null)));
        }

        [Test]
        public void ActionOperators_WriteThroughAndReturnTheSameInstance()
        {
            using NeoClient client = BuildClient();
            using var node = new NeoMemberActionWritable(
                client,
                ActionMember(ActionValueId),
                null,
                NeoValueOwnership.Session);
            NeoAction action = node.Bind();

            NeoAction afterAdd = action + MemberTarget(FirstListenerId, null);
            Assert.AreSame(action, afterAdd);
            Assert.AreEqual(1, action.Listeners.Count);

            NeoAction afterRemove = action - MemberTarget(FirstListenerId, null);
            Assert.AreSame(action, afterRemove);
            Assert.AreEqual(0, action.Listeners.Count);
        }

        [Test]
        public void ListenerTargetOf_ResolvesAStampedStaticMethodGroup()
        {
            NeoDelegateValue target =
                NeoGeneratedTypesSupport.ListenerTargetOf(
                    (Action)StampedListener.Ping);

            Assert.AreEqual(FirstListenerId, target.memberId);
            Assert.IsNull(target.valueId);
        }

        [Test]
        public void ListenerTargetOf_RejectsANativeLambda()
        {
            var error = Assert.Throws<NeoActionListenerException>(
                () => NeoGeneratedTypesSupport.ListenerTargetOf((Action)(() => { })));

            StringAssert.Contains("not a Neo listener", error!.Message);
        }

        [Test]
        public void ListenerTargetOf_RejectsAnUnstampedInstanceMethodGroup()
        {
            var receiver = new StampedListener();

            Assert.Throws<NeoActionListenerException>(
                () => NeoGeneratedTypesSupport.ListenerTargetOf(
                    (Action)receiver.Unstamped));
        }

        [Test]
        public void ListenerTargetOf_RejectsACombinedMulticastDelegate()
        {
            Action combined = StampedListener.Ping;
            combined += StampedListener.Ping;

            var error = Assert.Throws<NeoActionListenerException>(
                () => NeoGeneratedTypesSupport.ListenerTargetOf(combined));

            StringAssert.Contains("multicast", error!.Message);
        }

        [Test]
        public void RequireSameAction_AcceptsTheLiveInstanceAndRejectsEverythingElse()
        {
            using NeoClient client = BuildClient();
            using var node = new NeoMemberAction(client, ActionMember(), null);
            using var otherNode = new NeoMemberAction(client, ActionMember(), null);
            NeoAction action = node.Bind();

            Assert.DoesNotThrow(
                () => NeoGeneratedTypesSupport.RequireSameAction(
                    action,
                    action,
                    "Receiver.OnDamaged"));
            Assert.Throws<NeoActionReassignmentException>(
                () => NeoGeneratedTypesSupport.RequireSameAction(
                    null,
                    action,
                    "Receiver.OnDamaged"));
            // Another node's live action for the same member: the reference
            // check is what catches it, so the label never has to be trusted.
            Assert.Throws<NeoActionReassignmentException>(
                () => NeoGeneratedTypesSupport.RequireSameAction(
                    otherNode.Bind(),
                    action,
                    "Receiver.OnDamaged"));
        }

        [Test]
        public void CreateFromDefault_DeepCopiesTheAuthoredListenerSet()
        {
            ActionMember member = ActionMember();
            member.defaultValue = new ActionMemberValueBase
            {
                value = new NeoActionValue
                {
                    listeners = { MemberTarget(FirstListenerId, null) },
                },
            };

            var row = (ActionMemberValue)MemberValueFactory.CreateFromDefault(
                member,
                "action-default-row",
                "x",
                "x")!;
            row.value!.listeners.Add(MemberTarget(SecondListenerId, null));

            Assert.AreEqual(2, row.value.listeners.Count);
            Assert.AreEqual(
                1,
                member.defaultValue.value!.listeners.Count,
                "a row must never alias the shared declaration default");
            Assert.AreNotSame(
                member.defaultValue.value.listeners[0],
                row.value.listeners[0]);
        }

        [Test]
        public void Invoke_WithNoListenersIsASuccessfulNoOp()
        {
            using NeoClient client = BuildClient();
            using var node = new NeoMemberAction(client, ActionMember(), null);

            Assert.DoesNotThrow(() => node.Invoke());
        }

        [Test]
        public void Invoke_FiresEveryListenerSequentiallyInStoredOrder()
        {
            var calls = new List<string>();
            using NeoClient client = BuildClient(
                (FirstListenerId, () => calls.Add("first")),
                (SecondListenerId, () => calls.Add("second")));
            ActionMember member = ActionMember();
            member.defaultValue = new ActionMemberValueBase
            {
                value = new NeoActionValue
                {
                    listeners =
                    {
                        MemberTarget(FirstListenerId, null),
                        MemberTarget(SecondListenerId, null),
                    },
                },
            };
            using var node = new NeoMemberAction(client, member, null);

            node.Invoke();

            CollectionAssert.AreEqual(new[] { "first", "second" }, calls);
        }

        [Test]
        public void Invoke_FailsFastNamingTheListenerFrame()
        {
            var calls = new List<string>();
            using NeoClient client = BuildClient(
                (FirstListenerId, () => throw new NSGetterRuntimeError("boom")),
                (SecondListenerId, () => calls.Add("second")));
            ActionMember member = ActionMember();
            member.defaultValue = new ActionMemberValueBase
            {
                value = new NeoActionValue
                {
                    listeners =
                    {
                        MemberTarget(FirstListenerId, null),
                        MemberTarget(SecondListenerId, null),
                    },
                },
            };
            using var node = new NeoMemberAction(client, member, null);

            var error = Assert.Throws<NSGetterRuntimeError>(() => node.Invoke());

            StringAssert.Contains("OnDamaged[default] listener 0", error!.Message);
            StringAssert.Contains("boom", error.Message);
            CollectionAssert.IsEmpty(calls);
        }

        /// <summary>
        /// A budget fault is a non-catchable control fault. Renaming it into
        /// a plain <see cref="NSGetterRuntimeError"/> would let an authored
        /// <c>catch</c> swallow a fail-closed safety limit, so the fan-out
        /// must let it through as itself.
        /// </summary>
        [Test]
        public void Invoke_LetsAControlFaultThroughUnwrapped()
        {
            using NeoClient client = BuildClient(
                (FirstListenerId, () => throw new NeoScriptResourceLimitError(
                    "work unit budget exhausted")));
            using var node = new NeoMemberAction(
                client,
                ActionMemberWithListeners(MemberTarget(FirstListenerId, null)),
                null);

            var error = Assert.Throws<NeoScriptResourceLimitError>(
                () => node.Invoke());

            Assert.AreEqual("work unit budget exhausted", error!.Message);
        }

        [Test]
        public void Invoke_LetsADeferredFunctionFaultKeepItsClass()
        {
            using NeoClient client = BuildClient(
                (FirstListenerId, () => throw new NeoDeferredFunctionRuntimeError(
                    "deferred Function is not immediate")));
            using var node = new NeoMemberAction(
                client,
                ActionMemberWithListeners(MemberTarget(FirstListenerId, null)),
                null);

            Assert.Throws<NeoDeferredFunctionRuntimeError>(() => node.Invoke());
        }

        [Test]
        public void Load_RejectsAnActionMemberWithDuplicateListenerIdentities()
        {
            ActionMember member = ActionMember();
            member.defaultValue = new ActionMemberValueBase
            {
                value = new NeoActionValue
                {
                    listeners =
                    {
                        MemberTarget(FirstListenerId, null),
                        MemberTarget(FirstListenerId, null),
                    },
                },
            };

            var error = Assert.Throws<InvalidOperationException>(
                () => BuildClient(new JsonMember[] { member }).Dispose());

            StringAssert.Contains(
                "duplicates an earlier listener identity",
                error!.Message);
        }

        [Test]
        public void Load_RejectsAnActionMemberDeclaredRequired()
        {
            ActionMember member = ActionMember();
            member.DeclaredRequirement = NeoMemberRequirementKind.Required;

            var error = Assert.Throws<InvalidOperationException>(
                () => BuildClient(new JsonMember[] { member }).Dispose());

            StringAssert.Contains("never nullable", error!.Message);
        }

        [Test]
        public void Load_RejectsAnActionMemberBeyondTheArityCap()
        {
            ActionMember member = ActionMember();
            member.argumentTypes = new FunctionArgumentTypeInfo[17];
            for (int index = 0; index < member.argumentTypes.Length; index++)
            {
                member.argumentTypes[index] = new FunctionArgumentTypeInfo
                {
                    name = $"p{index}",
                    type = MemberKind.Int,
                    required = true,
                };
            }

            var error = Assert.Throws<InvalidOperationException>(
                () => BuildClient(new JsonMember[] { member }).Dispose());

            StringAssert.Contains("16-argument arity cap", error!.Message);
        }

        private static ActionMember ActionMemberWithListeners(
            params NeoDelegateValue[] listeners)
        {
            ActionMember member = ActionMember();
            var value = new NeoActionValue();
            value.listeners.AddRange(listeners);
            member.defaultValue = new ActionMemberValueBase { value = value };
            return member;
        }

        private static NeoDelegateValue MemberTarget(string memberId, string? valueId) =>
            new() { memberId = memberId, valueId = valueId };

        private static ActionMember ActionMember(string? valueId = null) => new()
        {
            id = ActionMemberId,
            projectId = ProjectId,
            name = "OnDamaged",
            kind = MemberKind.NSAction,
            Requirement = NeoMemberRequirementKind.Optional,
            valueId = valueId,
            Storage = valueId is null ? NeoMemberStorage.Inherit : NeoMemberStorage.Session,
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            createdAt = "x",
            updatedAt = "x",
        };

        private static FunctionMember VoidListener(string id) => new()
        {
            id = id,
            projectId = ProjectId,
            name = id,
            kind = MemberKind.Function,
            Modifier = NeoMemberModifierKind.Static,
            returnTypeInfo = new VoidTypeInfo
            {
                type = MemberKind.Void,
                required = true,
            },
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            Dispatch = NeoFunctionDispatchKind.Synchronous,
            createdAt = "x",
            updatedAt = "x",
        };

        /// <summary>
        /// A project whose only content is the roots plus one static native
        /// Function per requested listener, wired to the supplied handler.
        /// </summary>
        private static NeoClient BuildClient(
            params (string memberId, Action handler)[] listeners) =>
            BuildClient(Array.Empty<JsonMember>(), listeners);

        private static NeoClient BuildClient(
            JsonMember[] extraMembers,
            params (string memberId, Action handler)[] listeners)
        {
            ClassMember assets = RootMember("root-assets", "Assets", "root-assets-value");
            ClassMember save = RootMember("root-save", "Save", "root-save-value", NeoMemberStorage.Save);
            ClassMember session = RootMember("root-session", "Session", "root-session-value", NeoMemberStorage.Session);
            var members = new Dictionary<string, JsonMember>
            {
                [assets.id] = assets,
                [save.id] = save,
                [session.id] = session,
            };
            foreach (JsonMember extra in extraMembers) members[extra.id] = extra;
            var invokers = new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>();
            foreach ((string memberId, Action handler) in listeners)
            {
                members[memberId] = VoidListener(memberId);
                invokers[memberId] = (_, _, _) =>
                {
                    handler();
                    return null;
                };
            }

            var values = new Dictionary<string, MemberValue>
            {
                [assets.valueId!] = ObjectValue(assets.valueId!),
                [save.valueId!] = ObjectValue(save.valueId!),
                [session.valueId!] = ObjectValue(session.valueId!),
                [ActionValueId] = new ActionMemberValue
                {
                    id = ActionValueId,
                    value = new NeoActionValue(),
                    createdAt = "x",
                    updatedAt = "x",
                },
            };

            NeoClient client = NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "NSAction Tests",
                    rootAssetsMemberId = assets.id,
                    rootSaveFileMemberId = save.id,
                    rootSessionMemberId = session.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = members,
                values = values,
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["root-class"] = new NeoSchemaClass
                    {
                        id = "root-class",
                        projectId = ProjectId,
                        name = "Root",
                        schema = new Dictionary<string, string>(),
                        createdAt = "x",
                        updatedAt = "x",
                    },
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
                metadata = new ProjectExportMetadata
                {
                    schemaVersion = NeoProjectExportContract.CurrentSchemaVersion,
                    projectId = ProjectId,
                    versionId = "unit-test-version",
                },
                internalRecordRelations =
                    new Dictionary<string, InternalRecordRelation>(),
            });
            client.RegisterNativeFunctionInvokers(invokers);
            return client;
        }

        private static ClassMember RootMember(
            string id,
            string name,
            string valueId,
            NeoMemberStorage storage = NeoMemberStorage.Inherit) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Class,
            classId = "root-class",
            valueId = valueId,
            Storage = storage,
            createdAt = "x",
            updatedAt = "x",
        };

        private static ObjectMemberValue ObjectValue(string id) => new()
        {
            id = id,
            classId = "root-class",
            value = new Dictionary<string, string>(),
            createdAt = "x",
            updatedAt = "x",
        };

        /// <summary>
        /// Stands in for generated C#: codegen stamps every void-compatible
        /// Function / NSFunction method with its member id, which is what
        /// makes a method group subscribable.
        /// </summary>
        private sealed class StampedListener
        {
            [NeoMemberMethod(FirstListenerId)]
            public static void Ping() { }

            public void Unstamped() { }
        }
    }
}
