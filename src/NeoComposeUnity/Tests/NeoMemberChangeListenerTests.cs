// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using NeoCompose.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoMemberChangeListenerTests
    {
        private const string Metadata = "{\"owner\":{\"field\":[{\"memberId\":\"handler\",\"valueId\":null}]}}";

        [TestCase(false)]
        [TestCase(true)]
        public void LookupListenerUsesTheDeclaredSelectionType(bool multiple)
        {
            using var client = BuildClient(configure: data =>
            {
                data.members["entry"] = new ClassMember { id = "entry", kind = MemberKind.Class, name = "Entry", classId = "owner-class" };
                data.members["choices"] = new ListMember { id = "choices", kind = MemberKind.List, name = "Choices", entryMemberId = "entry" };
                data.members["selection"] = new LookupMember
                {
                    id = "selection",
                    kind = MemberKind.Lookup,
                    name = "Selection",
                    collectionMemberId = "choices",
                    Requirement = NeoMemberRequirementKind.Required,
                    Selection = multiple ? NeoMemberSelectionKind.Multi : NeoMemberSelectionKind.Single,
                };
            });
            Assert.That(client.TryGetMember("selection", out Member? selection), Is.True);
            var type = NeoNSFunctionRuntime.TypeInfoFromBindingMember(client, selection!,
                new Dictionary<string, NeoGenericEnvEntry>(), new HashSet<string>());
            Assert.That(type.required, Is.True);
            Assert.That(type.type, Is.EqualTo(multiple ? MemberKind.Lookup : MemberKind.Class));
            var entry = multiple ? ((LookupTypeInfo)type).entryTypeInfo : type;
            Assert.That(entry, Is.TypeOf<ClassTypeInfo>());
            Assert.That(((ClassTypeInfo)entry!).classId, Is.EqualTo("owner-class"));
            Assert.That(entry!.required, Is.True);
        }

        [Test]
        public async Task JsonImport_ForcedPublicCommitCapturesListenerPaths()
        {
            using var source = BuildClient();
            var baseline = JObject.Parse(source.SerializeSaveData());
            var remote = baseline.ToObject<RemoteGameSave>()!;
            remote.id = "imported";
            remote.serverId = "server-imported";
            remote.snapshotId = "baseline";
            remote.snapshotRevision = 1;
            remote.releaseChannelId = NeoSaveTestSupport.TargetChannel;
            remote.recordCache.snapshotId = remote.snapshotId;
            remote.recordCache.snapshotRevision = remote.snapshotRevision;
            var api = new FakeApiClient { getResult = remote };
            var localStore = new NeoInMemoryLocalSaveStore();
            using var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(JsonConvert.SerializeObject(source.ProjectDataForRuntime)),
                localStore: localStore,
                apiClient: api,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);
            await store.LoadAsync();
            var sync = store.Open("imported");
            string content = (await sync.LoadSaveContentAsync())!;
            var imported = JObject.Parse(content);
            imported["changeListeners"] = JObject.Parse("{\"save\":{\"save\":{\"field\":[{\"memberId\":\"handler\",\"valueId\":\"save\"}]}}}");
            imported["requiredSaveFormatRevision"] = NeoSaveFormat.ListenerRevision;
            using var client = new NeoClient(sync, imported.ToString(Formatting.None), saveOptions: new NeoSaveOptions { DiagnosticsEnabled = false });
            await client.CommitAsync();
            Assert.That(api.sparseCommits, Is.Empty, "The ordinary unchanged client commit remains a no-op.");
            var committed = imported.ToObject<RemoteGameSave>()!;
            committed.id = remote.id;
            committed.snapshotId = "imported-snapshot";
            committed.snapshotName = "Snapshot 1791144000000";
            committed.recordCache.snapshotId = committed.snapshotId;
            committed.recordCache.snapshotRevision = committed.snapshotRevision;
            api.sparseCommitResults.Enqueue(NeoCommitResult.Committed(committed));
            await client.CommitAsync(forceCapture: true);
            var patch = api.sparseCommits.Single().request.changes.OfType<GameSaveChangeListenersPatchChange>().Single();
            var endpoint = patch.endpoints.Single();
            Assert.That(endpoint.valueId, Is.EqualTo("save"));
            Assert.That(endpoint.rootId, Is.EqualTo("save"));
            Assert.That(endpoint.rootMemberId, Is.EqualTo("save-member"));
            Assert.That(endpoint.steps, Is.Empty);
            Assert.That(await localStore.LoadSaveAsync("imported"), Does.Not.Contain("listenerEndpoints"));
            await client.CommitAsync(forceCapture: true);
            Assert.That(api.sparseCommits, Has.Count.EqualTo(1), "An identical forced capture must not create a successor snapshot. " + JsonConvert.SerializeObject(api.sparseCommits));
            var persisted = JObject.Parse((await localStore.LoadSaveAsync("imported"))!);
            Assert.That((string?)persisted["snapshotId"], Is.EqualTo(committed.snapshotId));
            Assert.That((long?)persisted["snapshotRevision"], Is.EqualTo(committed.snapshotRevision));

            var noOp = client.CommitAsync(forceCapture: true);
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                new NeoDelegateValue { memberId = "handler", valueId = "assets" }, true);
            var queuedRemote = JObject.Parse(client.SerializeSaveData()).ToObject<RemoteGameSave>()!;
            queuedRemote.id = remote.id;
            queuedRemote.snapshotId = "queued-snapshot";
            queuedRemote.recordCache.snapshotId = queuedRemote.snapshotId;
            queuedRemote.recordCache.snapshotRevision = queuedRemote.snapshotRevision;
            api.sparseCommitResults.Enqueue(NeoCommitResult.Committed(queuedRemote));
            var queued = client.CommitAsync(forceCapture: true);
            await noOp;
            await queued;
            Assert.That(api.sparseCommits, Has.Count.EqualTo(2), "The queued edit survives the preceding forced no-op.");
            var queuedPatch = api.sparseCommits.Last().request.changes.OfType<GameSaveChangeListenersPatchChange>().Single();
            Assert.That(queuedPatch.endpoints.Select(endpoint => endpoint.valueId), Does.Contain("assets"));
            Assert.That(queuedPatch.edits.Single().entry!["field"], Has.Length.EqualTo(2));

            var renamed = JObject.Parse((await localStore.LoadSaveAsync("imported"))!);
            renamed["snapshotName"] = "Checkpoint";
            using var headerClient = new NeoClient(sync, renamed.ToString(Formatting.None), saveOptions: new NeoSaveOptions { DiagnosticsEnabled = false });
            var renamedRemote = renamed.ToObject<RemoteGameSave>()!;
            renamedRemote.id = remote.id;
            renamedRemote.snapshotId = "renamed-snapshot";
            renamedRemote.recordCache.snapshotId = renamedRemote.snapshotId;
            renamedRemote.recordCache.snapshotRevision = renamedRemote.snapshotRevision;
            api.sparseCommitResults.Enqueue(NeoCommitResult.Committed(renamedRemote));
            await headerClient.CommitAsync(forceCapture: true);
            Assert.That(api.sparseCommits, Has.Count.EqualTo(3), "A real snapshot-header edit is not suppressed.");
            Assert.That(api.sparseCommits.Last().request.snapshotName, Is.EqualTo("Checkpoint"));
        }

        [TestCase("member")]
        [TestCase("virtual")]
        [TestCase("list")]
        [TestCase("dictionary")]
        [TestCase("entry")]
        public void CapturedListenerPathsUseOwningPositionsAndStayDetached(string kind)
        {
            using var client = BuildClient(configure: data =>
            {
                data.classes["child-class"] = new NeoSchemaClass
                {
                    id = "child-class",
                    projectId = "listeners",
                    name = "Child",
                    schema = new() { ["Count"] = "field", ["Changed"] = "handler" },
                };
                data.members["child-member"] = new ClassMember
                {
                    id = "child-member",
                    projectId = "listeners",
                    name = "Child",
                    kind = MemberKind.Class,
                    classId = "child-class",
                    Requirement = NeoMemberRequirementKind.Required,
                    defaultValue = new ObjectMemberValueBase { classId = "child-class", value = new() },
                };
                bool collection = kind is "list" or "dictionary" or "entry";
                data.classes["owner-class"].schema["Child"] = collection ? "items-member" : "child-member";
                if (kind != "virtual")
                {
                    ((ObjectMemberValue)data.values["save"]).value!["Child"] = collection ? "items" : "child";
                    data.values["child"] = new ObjectMemberValue
                    {
                        id = "child",
                        classId = "child-class",
                        value = new(),
                        containerId = kind == "entry" ? "items" : null,
                    };
                }
                if (kind == "dictionary")
                {
                    data.members["items-member"] = new DictionaryMember
                    {
                        id = "items-member",
                        projectId = "listeners",
                        name = "Items",
                        kind = MemberKind.Dictionary,
                        entryMemberId = "child-member",
                    };
                    data.values["items"] = new ObjectMemberValue { id = "items", value = new() { ["slot"] = "child" } };
                }
                else if (collection)
                {
                    data.members["items-member"] = new ListMember
                    {
                        id = "items-member",
                        projectId = "listeners",
                        name = "Items",
                        kind = MemberKind.List,
                        entryMemberId = "child-member",
                        ListKind = kind == "entry" ? NeoListKind.Unordered : NeoListKind.Ordered,
                    };
                    data.values["items"] = new ArrayMemberValue { id = "items", value = kind == "entry" ? Array.Empty<string>() : new[] { "child" } };
                }
            });
            string ownerId = "child";
            if (kind == "virtual")
            {
                using var root = new NeoMemberClassWritable(client, "save-member", "save", NeoValueOwnership.Save);
                ownerId = root.Get<NeoMemberClassWritable>("Child").value!.id;
            }
            client.EditMemberChangeListener(ownerId, NeoValueOwnership.Save, "field", IntType(),
                new NeoDelegateValue { memberId = "handler", valueId = "save" }, true);
            var hints = client.CaptureListenerEndpoints();
            Assert.That(hints[ownerId].rootId, Is.EqualTo("save"));
            Assert.That(hints[ownerId].rootMemberId, Is.EqualTo("save-member"));
            Assert.That(hints[ownerId].steps[0], Is.TypeOf<GameSaveListenerMemberStep>());
            var last = hints[ownerId].steps[hints[ownerId].steps.Count - 1];
            Assert.That(last.kind, Is.EqualTo(kind == "virtual" ? "member" : kind));
            if (last is GameSaveListenerEntryStep entry)
                Assert.That(entry.valueId, Is.EqualTo(ownerId));
            if (last is GameSaveListenerListStep ordered)
                Assert.That(ordered.index, Is.Zero);
            if (last is GameSaveListenerDictionaryStep dictionary)
                Assert.That(dictionary.key, Is.EqualTo("slot"));
            var local = new LocalGameSave { listenerEndpoints = hints };
            Assert.That(JsonConvert.SerializeObject(local), Does.Not.Contain("listenerEndpoints"));
            Assert.That(local.DetachedCopy().listenerEndpoints, Is.SameAs(hints));
            string captured = JsonConvert.SerializeObject(hints);
            client.EditMemberChangeListener(ownerId, NeoValueOwnership.Save, "field", IntType(),
                new NeoDelegateValue { memberId = "handler", valueId = "save" }, false);
            Assert.That(JsonConvert.SerializeObject(hints), Is.EqualTo(captured));
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void RootPartitionReloadRestoresAuthoredWiringAndRetainsSuppression(bool suppress, bool sessionReceiver)
        {
            var heard = new List<double>();
            var target = new NeoDelegateValue { memberId = "handler", valueId = sessionReceiver ? "session" : null };
            using var client = BuildClient(heard.Add, data =>
            {
                var root = data.values["save"];
                root.changeListeners = new NeoChangeListenerMap
                {
                    ["save"] = new Dictionary<string, NeoDelegateValue[]> { ["field"] = new[] { target } },
                };
                data.valuePartitions = new Dictionary<string, JToken>
                {
                    ["anchor"] = new JObject { ["save"] = JObject.FromObject(root) },
                };
                data.values.Remove("save");
            });
            Assert.That(client.IsValuePartitionLoaded("anchor"), Is.False);
            client.LoadValuePartition("anchor");
            if (suppress)
                client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), target, false);
            Assert.That(heard, Is.Empty);
            client.UnloadValuePartition("anchor");
            Assert.That(client.values.ContainsKey("count"), Is.True, "The explicitly main-resident child stays loaded.");
            Assert.That(client.values.ContainsKey("save"), Is.False);
            Assert.That(heard, Is.Empty);
            client.LoadValuePartition("anchor");
            Assert.That(heard, Is.Empty);
            WriteCount(client, 4);
            CollectionAssert.AreEqual(suppress ? Array.Empty<double>() : new[] { 4d }, heard);
        }

        [Test]
        public void ListenerCapacityIncludesTheServerEnvelopeAndUtf8Payload()
        {
            var save = new ProjectSaveData { projectId = "listeners", version = new VersionData { id = "version" } };
            // Cross-host vectors computed with Convex getDocumentSize and JSON.stringify.
            foreach (var (receiver, expectedBytes) in new[]
            {
                ("受信者", 555), ("\u0085", 548), ("\u2028\u2029", 552),
                ("\ud800", 552), ("\udc00", 552), ("😀", 550),
                ("\"\\\n\t\b\f\r\u0000", 566),
            })
            {
                var map = new NeoChangeListenerMap
                {
                    ["owner"] = new Dictionary<string, NeoDelegateValue[]>
                    {
                        ["field"] = new[] { new NeoDelegateValue { memberId = "handler", valueId = receiver } },
                    },
                };
                Assert.That(NeoChangeListenerPatches.EncodedRecordSize(save, "root", map), Is.EqualTo(expectedBytes));
            }
        }

        [TestCase(667, 122639)]
        [TestCase(5726, 1048436)]
        [TestCase(5727, 1048619)]
        public void ListenerCapacityFixtureMeasuresTheCompleteRecord(int ownerCount, int expectedBytes)
        {
            const string id = "00000000-0000-0000-0000-000000000000";
            var save = new ProjectSaveData { projectId = id, version = new VersionData { id = id } };
            var map = new NeoChangeListenerMap();
            for (int index = 0; index < ownerCount; index++)
                map["00000000-0000-0000-0000-" + index.ToString("D12")] = new Dictionary<string, NeoDelegateValue[]>
                {
                    [id] = new[] { new NeoDelegateValue { memberId = id, valueId = id } },
                };
            int bytes = NeoChangeListenerPatches.EncodedRecordSize(save, id, map);
            Assert.That(bytes, Is.EqualTo(expectedBytes));
            Assert.That(bytes <= NeoChangeListenerPatches.MaxRecordBytes, Is.EqualTo(ownerCount <= 5726));
        }

        [TestCase(1, "leaf")]
        [TestCase(2, "leaf")]
        [TestCase(1, "class")]
        [TestCase(1, "wrapper")]
        [TestCase(1, "classRead")]
        [TestCase(1, "classRepeat")]
        public void ListenerEnvelopeOverflowRollsBackThePendingValueAndMetadataBatch(int targetCount, string writePath)
        {
            // The map payload fits by itself; the persisted record envelope does not.
            var targets = new NeoDelegateValue[targetCount];
            for (int index = 0; index < targets.Length; index++)
                targets[index] = new NeoDelegateValue { memberId = new string('h', (NeoChangeListenerPatches.MaxRecordBytes - 300) / targetCount) + index };
            var heard = new List<double>();
            using var client = BuildClient(heard.Add, data =>
            {
                for (int index = 0; index < targets.Length; index++)
                {
                    string id = targets[index].memberId!;
                    string name = "Handler" + index;
                    data.members[id] = new FunctionMember
                    {
                        id = id,
                        projectId = "listeners",
                        name = name,
                        kind = MemberKind.Function,
                        returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                        argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "next", type = MemberKind.Int, required = true } },
                    };
                    data.classes["owner-class"].schema[name] = id;
                }
            });
            var map = new NeoChangeListenerMap
            {
                ["save"] = new Dictionary<string, NeoDelegateValue[]>
                {
                    ["field"] = targets,
                },
            };
            var encoded = new JObject { ["changeListeners"] = JObject.FromObject(map) };
            Assert.That(System.Text.Encoding.UTF8.GetByteCount(encoded.ToString(Formatting.None)), Is.LessThan(NeoChangeListenerPatches.MaxRecordBytes));
            string before = client.SerializeSaveData();
            int parentNotifications = 0;
            client.save.OnChanged += _ => parentNotifications++;
            client.EnterScriptWrites();
            try
            {
                if (writePath.StartsWith("class", StringComparison.Ordinal))
                {
                    client.save.SetSerializedValue("Count", NeoValueWritePayload.FromValue(7));
                    if (writePath == "classRead")
                        Assert.That(client.save.Get<NeoMemberIntWritable>("Count").value!.value, Is.EqualTo(7));
                    if (writePath != "class")
                        client.save.SetSerializedValue("Count", NeoValueWritePayload.FromValue(8));
                }
                else if (writePath == "wrapper")
                    client.save.Get<NeoMemberIntWritable>("Count").Set(7);
                else
                    WriteCount(client, 7);
                foreach (var target in targets)
                    client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), target, true);
                var error = Assert.Throws<NeoCompose.Runtime.NeoScript.NSGetterRuntimeError>(() => client.CommitScriptWrites());
                Assert.That(error!.Message, Does.Contain("save.field"));
            }
            finally
            {
                client.ExitScriptWrites();
            }
            bool earlierReadCommitted = writePath is "classRead" or "classRepeat";
            if (earlierReadCommitted)
            {
                var committed = JObject.Parse(client.SerializeSaveData());
                Assert.That(committed["values"]!["count"]!["value"]!.Value<double>(), Is.EqualTo(7));
                Assert.That(committed["changeListeners"], Is.Null);
            }
            else
                Assert.That(client.SerializeSaveData(), Is.EqualTo(before));
            Assert.That(heard, Is.Empty);
            Assert.That(parentNotifications, Is.EqualTo(earlierReadCommitted ? 1 : 0));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PublicHeldScalarWritePublishesOneParentAndListenerNotification(bool wrapper)
        {
            var heard = new List<double>();
            using var client = BuildClient(heard.Add);
            int parents = 0;
            client.save.OnChanged += _ => parents++;
            client.EnterScriptWrites();
            try
            {
                if (wrapper)
                    client.save.Get<NeoMemberIntWritable>("Count").Set(7);
                else
                    client.save.SetSerializedValue("Count", NeoValueWritePayload.FromValue(7));
                client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                    new NeoDelegateValue { memberId = "handler" }, true);
                Assert.That(parents, Is.Zero);
                Assert.That(heard, Is.Empty);
            }
            finally { client.ExitScriptWrites(); }
            Assert.That(parents, Is.EqualTo(1));
            CollectionAssert.AreEqual(new[] { 7d }, heard);
        }

        [TestCase(null)]
        [TestCase("save")]
        public void HandlerOverridesShareIdentityAndDispatchAgainstTheirReceiver(string? receiverId)
        {
            var heard = new List<double>();
            using var client = BuildClient(heard.Add, data =>
            {
                data.classes["derived-owner"] = new NeoSchemaClass
                {
                    id = "derived-owner",
                    projectId = "listeners",
                    name = "DerivedOwner",
                    extendsClassId = "owner-class",
                    schema = new Dictionary<string, string> { ["Changed"] = "derived-handler" },
                };
                data.members["derived-handler"] = new FunctionMember
                {
                    id = "derived-handler",
                    projectId = "listeners",
                    name = "Changed",
                    kind = MemberKind.Function,
                    extendsMemberId = "handler",
                    returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                    argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "next", type = MemberKind.Int, required = true } },
                };
                ((ObjectMemberValue)data.values["save"]).classId = "derived-owner";
            });
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["derived-handler"] = (_, _, arguments) => { heard.Add(100 + Convert.ToDouble(arguments[0])); return null; },
            });
            var baseTarget = new NeoDelegateValue { memberId = "handler", valueId = receiverId };
            var derivedTarget = new NeoDelegateValue { memberId = "derived-handler", valueId = receiverId };
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), baseTarget, true);
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), derivedTarget, true);
            Assert.That(client.SerializeSaveData(), Does.Not.Contain("derived-handler"));
            WriteCount(client, 7);
            CollectionAssert.AreEqual(new[] { 107d }, heard);
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), derivedTarget, false);
            WriteCount(client, 8);
            CollectionAssert.AreEqual(new[] { 107d }, heard);
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), derivedTarget, true);
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), baseTarget, false);
            Assert.That(client.SerializeSaveData(), Does.Not.Contain("changeListeners"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ListenersCanTargetCallableMembersByTheirDeclaredSignature(bool multicast)
        {
            var heard = new List<double>();
            using var client = BuildClient(heard.Add, data =>
            {
                data.classes["owner-class"].schema["Proxy"] = "proxy";
                var target = new NeoDelegateValue { memberId = "handler", valueId = "save" };
                var arguments = new[] { new FunctionArgumentTypeInfo { name = "next", type = MemberKind.Int, required = true } };
                data.members["proxy"] = multicast
                    ? new ActionMember
                    {
                        id = "proxy",
                        projectId = "listeners",
                        name = "Proxy",
                        kind = MemberKind.NSAction,
                        argumentTypes = arguments,
                        defaultValue = new ActionMemberValueBase { value = new NeoActionValue { listeners = { target } } },
                    }
                    : new DelegateMember
                    {
                        id = "proxy",
                        projectId = "listeners",
                        name = "Proxy",
                        kind = MemberKind.NSDelegate,
                        returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                        Requirement = NeoMemberRequirementKind.Required,
                        argumentTypes = arguments,
                        defaultValue = new DelegateMemberValueBase { value = target },
                    };
                ((ObjectMemberValue)data.values["save"]).value!["Proxy"] = "proxy-value";
                data.values["proxy-value"] = multicast
                    ? new ActionMemberValue { id = "proxy-value", value = new NeoActionValue { listeners = { target } } }
                    : new DelegateMemberValue { id = "proxy-value", value = target };
            });
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                new NeoDelegateValue { memberId = "proxy", valueId = "save" }, true);
            WriteCount(client, 7);
            CollectionAssert.AreEqual(new[] { 7d }, heard);
        }

        [Test]
        public void DeferredHandlersAreRejectedBeforeWiringChanges()
        {
            using var client = BuildClient(configure: data =>
                ((FunctionMember)data.members["handler"]).Dispatch = NeoFunctionDispatchKind.Asynchronous);
            var error = Assert.Throws<NeoCompose.Runtime.NeoScript.NSGetterRuntimeError>(() =>
                client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                    new NeoDelegateValue { memberId = "handler", valueId = "save" }, true));
            Assert.That(error!.Message, Does.Contain("immediately"));
            Assert.That(client.SerializeSaveData(), Does.Not.Contain("changeListeners"));
        }

        [TestCase("signature")]
        [TestCase("asynchronous")]
        [TestCase("lifetime")]
        public void StaleSavedHandlersAreSkippedWithoutFailingTheWrite(string change)
        {
            using var original = BuildClient();
            original.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                new NeoDelegateValue { memberId = "handler", valueId = "save" }, true);
            var saved = JObject.Parse(original.SerializeSaveData());
            if (change == "lifetime")
                saved["changeListeners"]!["save"]!["save"]!["field"]![0]!["valueId"] = "session";
            var heard = new List<double>();
            using var client = BuildClient(heard.Add, data =>
            {
                var handler = (FunctionMember)data.members["handler"];
                if (change == "signature")
                    handler.argumentTypes![0].type = MemberKind.String;
                if (change == "asynchronous")
                    handler.Dispatch = NeoFunctionDispatchKind.Asynchronous;
            }, saved.ToString(Formatting.None));
            Assert.DoesNotThrow(() => WriteCount(client, 7));
            Assert.That(heard, Is.Empty);
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, "count", out var row), Is.True);
            Assert.That(((NumberMemberValue)row!).value, Is.EqualTo(7));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void StaleGenericHandlersDoNotPreventSubsequentValidListeners(bool removedBinding)
        {
            using var original = BuildClient();
            original.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                new NeoDelegateValue { memberId = "handler", valueId = "save" }, true);
            var saved = JObject.Parse(original.SerializeSaveData());
            ((JArray)saved["changeListeners"]!["save"]!["save"]!["field"]!).Insert(0,
                JObject.FromObject(new NeoDelegateValue { memberId = "stale-handler", valueId = "save" }));
            var heard = new List<double>();
            using var client = BuildClient(heard.Add, data =>
            {
                data.classes["owner-class"].schema["Stale"] = "stale-handler";
                data.members["stale-handler"] = new FunctionMember
                {
                    id = "stale-handler",
                    projectId = "listeners",
                    name = "Stale",
                    kind = MemberKind.Function,
                    returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                    argumentTypes = new[] { new FunctionArgumentTypeInfo
                    {
                        name = "next", type = MemberKind.Generic, genericParamId = "stale-type", required = true,
                    } },
                };
                if (removedBinding)
                    ((ObjectMemberValue)data.values["save"]).genericBindings =
                        new Dictionary<string, string> { ["stale-type"] = "removed-binding" };
            }, saved.ToString(Formatting.None));
            Assert.DoesNotThrow(() => WriteCount(client, 7));
            CollectionAssert.AreEqual(new[] { 7d }, heard);
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, "count", out var row), Is.True);
            Assert.That(((NumberMemberValue)row!).value, Is.EqualTo(7));
        }

        [Test]
        public void InheritedDefaultWiringUsesTheDeclaringMembersNamespace()
        {
            var heard = new List<double>();
            using var client = BuildClient(heard.Add, data =>
            {
                data.members["base-default"] = new ClassMember
                {
                    id = "base-default",
                    projectId = "listeners",
                    name = "Template",
                    kind = MemberKind.Class,
                    classId = "owner-class",
                    defaultValue = new ObjectMemberValueBase
                    {
                        value = new Dictionary<string, string> { ["Count"] = "count" },
                        changeListeners = new NeoChangeListenerMap
                        {
                            [NeoClient.DerivedMemberValueId("base-default")] = new Dictionary<string, NeoDelegateValue[]>
                            {
                                ["field"] = new[] { new NeoDelegateValue { memberId = "handler", valueId = null } },
                            },
                        },
                    },
                };
                data.members["override-default"] = new ClassMember
                {
                    id = "override-default",
                    projectId = "listeners",
                    name = "Template",
                    kind = MemberKind.Class,
                    classId = "owner-class",
                    extendsMemberId = "base-default",
                };
            });
            Assert.That(client.TryGetMember("override-default", out var member), Is.True);
            var constructed = NeoGeneratedTypesSupport.MaterializeStoredClassMemberDefault(client, (ClassMember)member!,
                new ObjectMemberValue { id = "default-instance", classId = "owner-class", value = new Dictionary<string, string>() });
            using var value = new NeoMemberClassWritable(client, "override-default", constructed.value.id, NeoValueOwnership.Session);
            value.Get<NeoMemberIntWritable>("Count").Set(7);
            CollectionAssert.AreEqual(new[] { 7d }, heard);
        }

        [Test]
        public void UnorderedSessionEntryRetainsItsSaveContainerScope()
        {
            using var client = BuildClient(configure: data =>
            {
                data.classes["owner-class"].schema["Items"] = "items-member";
                data.members["items-member"] = new ListMember
                {
                    id = "items-member",
                    projectId = "listeners",
                    name = "Items",
                    kind = MemberKind.List,
                    ListKind = NeoListKind.Unordered,
                    entryMemberId = "items-entry",
                };
                data.members["items-entry"] = new ClassMember
                {
                    id = "items-entry",
                    projectId = "listeners",
                    name = "Entry",
                    kind = MemberKind.Class,
                    classId = "entry-class",
                    Storage = NeoMemberStorage.Session,
                };
                data.classes["entry-class"] = new NeoSchemaClass
                {
                    id = "entry-class",
                    projectId = "listeners",
                    name = "Entry",
                    schema = new Dictionary<string, string> { ["Count"] = "field" },
                };
                ((ObjectMemberValue)data.values["save"]).value!["Items"] = "items";
                data.values["items"] = new ArrayMemberValue { id = "items", value = Array.Empty<string>() };
            });
            var list = new NeoMemberListWritable(client, "items-member", "items", NeoValueOwnership.Save);
            var plan = new NeoWritePlan(client);
            string entry = list.PrepareAddSerialized(plan, NeoValueWritePayload.FromValue(
                new NeoValuePayload(new Dictionary<string, string>(), "entry-class")));
            plan.Commit();
            Assert.That(client.TryFindOwnedParent(NeoValueOwnership.Session, entry, out string? parent, out NeoValueOwnership parentScope), Is.True);
            Assert.That(parent, Is.EqualTo("items"));
            Assert.That(parentScope, Is.EqualTo(NeoValueOwnership.Save));
            client.EditMemberChangeListener(entry, NeoValueOwnership.Session, "field", IntType(),
                new NeoDelegateValue { memberId = "handler", valueId = "save" }, true);
            Assert.That(client.SerializeSaveData(), Does.Not.Contain("changeListeners"), "Session entry wiring stays transient even beneath a Save root.");
        }

        [Test]
        public void DetachedDefaultListenersFollowSameStoreAdoptionAndResetAtTheNewRoot()
        {
            var heard = new List<double>();
            using var client = BuildClient(heard.Add, data =>
            {
                data.classes["container"] = new NeoSchemaClass
                {
                    id = "container",
                    projectId = "listeners",
                    name = "Container",
                    schema = new Dictionary<string, string> { ["Child"] = "child-member" },
                };
                ((ClassMember)data.members["session-member"]).classId = "container";
                data.values["session"].classId = "container";
                data.members["child-member"] = new ClassMember
                {
                    id = "child-member",
                    projectId = "listeners",
                    name = "Child",
                    kind = MemberKind.Class,
                    classId = "owner-class",
                    defaultValue = new ObjectMemberValueBase
                    {
                        value = new Dictionary<string, string> { ["Count"] = "count" },
                        changeListeners = new NeoChangeListenerMap
                        {
                            [NeoClient.DerivedMemberValueId("child-member")] = new Dictionary<string, NeoDelegateValue[]>
                            {
                                ["field"] = new[] { new NeoDelegateValue { memberId = "handler" } },
                            },
                        },
                    },
                };
            });
            client.TryGetMember("child-member", out Member? childMember);
            var constructed = NeoGeneratedTypesSupport.MaterializeStoredClassMemberDefault(client, (ClassMember)childMember!,
                new ObjectMemberValue { id = "detached", classId = "owner-class", value = new Dictionary<string, string>() });
            string ownerId = constructed.value.id;
            Attach("session");
            Write(7);
            CollectionAssert.AreEqual(new[] { 7d }, heard);
            heard.Clear();
            client.EditMemberChangeListener(ownerId, NeoValueOwnership.Session, "field", IntType(), new NeoDelegateValue { memberId = "handler" }, false);
            Write(8);
            Assert.That(heard, Is.Empty);
            var move = new NeoWritePlan(client);
            var source = (ObjectMemberValue)client.CloneRowForWrite(move.Resolve(NeoValueOwnership.Session, "session")!);
            source.value!.Remove("Child");
            move.Set(NeoValueOwnership.Session, source);
            move.Set(NeoValueOwnership.Session, new ObjectMemberValue { id = "sink", classId = "container", value = new Dictionary<string, string>() });
            Attach("sink", move);
            Write(9);
            Assert.That(heard, Is.Empty, "The suppression follows the same identity to its second root.");
            var reset = new NeoWritePlan(client);
            reset.SetListenerEntry(NeoValueOwnership.Session, "sink", ownerId, null);
            reset.Commit();
            Write(10);
            CollectionAssert.AreEqual(new[] { 10d }, heard);
            Assert.That(JObject.Parse(client.SerializeSaveData())["changeListeners"], Is.Null);

            void Attach(string root, NeoWritePlan? existing = null)
            {
                var plan = existing ?? new NeoWritePlan(client);
                using (client.ReadCandidate(plan))
                {
                    Assert.That(client.ImportValueReference(plan, NeoValueOwnership.Session, ownerId, out bool _), Is.EqualTo(ownerId));
                    var parent = (ObjectMemberValue)client.CloneRowForWrite(plan.Resolve(NeoValueOwnership.Session, root)!);
                    parent.value!["Child"] = ownerId;
                    plan.Set(NeoValueOwnership.Session, parent);
                }
                plan.Commit();
            }
            void Write(double next)
            {
                using var owner = new NeoMemberClassWritable(client, "child-member", ownerId, NeoValueOwnership.Session);
                owner.Get<NeoMemberIntWritable>("Count").Set((int)next);
            }
        }

        [TestCase(false, false, false, false, false, "stored")]
        [TestCase(false, true, false, false, false, "stored")]
        [TestCase(false, true, true, false, false, "stored")]
        [TestCase(true, false, false, false, false, "stored")]
        [TestCase(true, true, false, false, false, "stored")]
        [TestCase(true, true, true, false, false, "stored")]
        [TestCase(true, true, false, true, false, "stored")]
        [TestCase(true, true, true, true, false, "stored")]
        [TestCase(true, true, false, false, true, "stored")]
        [TestCase(true, true, false, false, false, "virtual")]
        [TestCase(true, true, false, false, true, "virtual")]
        [TestCase(true, true, false, false, false, "unordered")]
        [TestCase(true, true, false, false, true, "unordered")]
        [TestCase(true, true, false, false, false, "overlap")]
        [TestCase(true, false, false, false, true, "overlap")]
        public void ReleasedListenerOwnerKeepsWiringAcrossAdoption(bool session, bool pending, bool removeAfter, bool promoteEndpoints, bool conflict, string topology)
        {
            bool virtualChild = topology != "stored";
            bool unordered = topology == "unordered";
            bool nestedCollection = unordered || topology == "overlap";
            var heard = new List<double>();
            var scope = session ? NeoValueOwnership.Session : NeoValueOwnership.Save;
            using var client = BuildClient(heard.Add, data =>
            {
                data.classes["entry-class"] = new NeoSchemaClass
                {
                    id = "entry-class",
                    projectId = "listeners",
                    name = "Entry",
                    schema = new Dictionary<string, string> { ["Count"] = "field", ["Changed"] = "handler" },
                };
                if (virtualChild)
                {
                    data.classes["leaf-class"] = new NeoSchemaClass
                    {
                        id = "leaf-class",
                        projectId = "listeners",
                        name = "Leaf",
                        schema = new Dictionary<string, string> { ["Count"] = "field", ["Changed"] = "handler" },
                    };
                    if (nestedCollection)
                    {
                        data.classes["inner-class"] = new NeoSchemaClass
                        {
                            id = "inner-class",
                            projectId = "listeners",
                            name = "Inner",
                            schema = new Dictionary<string, string> { ["Nested"] = "nested-member" },
                        };
                        data.classes["entry-class"].schema!["NestedItems"] = "nested-items-member";
                        data.members["nested-items-member"] = new ListMember
                        {
                            id = "nested-items-member",
                            projectId = "listeners",
                            name = "NestedItems",
                            kind = MemberKind.List,
                            ListKind = unordered ? NeoListKind.Unordered : NeoListKind.Ordered,
                            entryMemberId = "nested-entry",
                        };
                        data.members["nested-entry"] = new ClassMember
                        {
                            id = "nested-entry",
                            projectId = "listeners",
                            name = "Inner",
                            kind = MemberKind.Class,
                            classId = "inner-class",
                        };
                    }
                    else
                        data.classes["entry-class"].schema!["Nested"] = "nested-member";
                    data.members["nested-member"] = new ClassMember
                    {
                        id = "nested-member",
                        projectId = "listeners",
                        name = "Nested",
                        kind = MemberKind.Class,
                        classId = "leaf-class",
                        Requirement = NeoMemberRequirementKind.Required,
                        defaultValue = new ObjectMemberValueBase { value = new Dictionary<string, string>() },
                    };
                }
                data.classes["owner-class"].schema!["Items"] = "items-member";
                data.classes["owner-class"].schema!["Child"] = "child-member";
                data.members["items-member"] = new ListMember
                {
                    id = "items-member",
                    projectId = "listeners",
                    name = "Items",
                    kind = MemberKind.List,
                    entryMemberId = "child-member",
                };
                data.members["child-member"] = new ClassMember
                {
                    id = "child-member",
                    projectId = "listeners",
                    name = "Child",
                    kind = MemberKind.Class,
                    classId = "entry-class",
                };
                ((ObjectMemberValue)data.values[session ? "session" : "save"]).value!["Items"] = "items";
                data.values["items"] = new ArrayMemberValue { id = "items", value = Array.Empty<string>() };
                if (session && !promoteEndpoints)
                    data.members["field"].Storage = NeoMemberStorage.Session;
            });
            var setup = new NeoWritePlan(client);
            setup.Set(scope, new ObjectMemberValue
            {
                id = "moving",
                classId = "entry-class",
                value = new Dictionary<string, string> { ["Count"] = "moving-count" },
            });
            setup.Set(scope, new NumberMemberValue { id = "moving-count", value = 0 });
            setup.Set(scope, new ArrayMemberValue { id = "items", value = new[] { "moving" } });
            if (nestedCollection)
            {
                ((ObjectMemberValue)setup.Resolve(scope, "moving")!).value!["NestedItems"] = "nested-list";
                setup.Set(scope, new ArrayMemberValue { id = "nested-list", value = unordered ? Array.Empty<string>() : new[] { "inner" } });
                setup.Set(scope, new ObjectMemberValue
                {
                    id = "inner",
                    classId = "inner-class",
                    containerId = unordered ? "nested-list" : null,
                    value = new Dictionary<string, string>(),
                });
            }
            setup.Commit();
            string listenerOwner = "moving";
            if (virtualChild)
            {
                using var owner = new NeoMemberClassWritable(client, nestedCollection ? "nested-entry" : "child-member", nestedCollection ? "inner" : "moving", scope);
                listenerOwner = owner.Get<NeoMemberClassWritable>("Nested").value!.id;
                Assert.That(client.TryGetWritableValue(scope, listenerOwner, out MemberValue? _), Is.False);
            }
            var target = new NeoDelegateValue { memberId = "handler", valueId = promoteEndpoints ? "moving" : session ? "session" : "save" };
            if (!pending)
                client.EditMemberChangeListener(listenerOwner, scope, "field", IntType(), target, true);
            string beforeMove = client.SerializeSaveData();
            client.EnterScriptWrites();
            try
            {
                var batch = client.ScriptWriteBatch("items")!;
                batch.Stage(staged =>
                {
                    if (pending)
                        client.EditMemberChangeListener(listenerOwner, scope, "field", IntType(), target, true);
                    if (topology == "overlap")
                    {
                        var nested = staged.List(scope, "nested-list", new Runtime.NeoScript.NSGetterEvaluator.Context(client, null, null), null);
                        client.TryGetMember("nested-entry", out Member? nestedMember);
                        string released = nested.RemoveAt(0, NeoTimestamp.Now());
                        staged.Release(scope, released, nestedMember, "nested-list");
                        string adopted = client.ImportValueReference(staged.Plan, scope, released, out bool _);
                        nested.Insert(0, adopted, NeoTimestamp.Now());
                        staged.Gain(scope, adopted, "nested-list");
                    }
                    var list = staged.List(scope, "items", new Runtime.NeoScript.NSGetterEvaluator.Context(client, null, null), null);
                    client.TryGetMember("child-member", out Member? entryMember);
                    staged.Release(scope, list.RemoveAt(0, NeoTimestamp.Now()), entryMember, "items");
                    string moved = client.ImportValueReference(staged.Plan, NeoValueOwnership.Save, "moving", out bool _);
                    Assert.That(moved, Is.EqualTo("moving"));
                    var destination = (ObjectMemberValue)client.CloneRowForWrite(staged.Plan.Resolve(NeoValueOwnership.Save, "save")!);
                    destination.value!["Child"] = moved;
                    staged.Plan.Set(NeoValueOwnership.Save, destination);
                    if (conflict)
                        staged.Plan.SetListenerEntry(NeoValueOwnership.Session, "save", listenerOwner,
                            new Dictionary<string, NeoDelegateValue[]> { ["field"] = new[] { target } }, NeoValueOwnership.Save);
                    if (removeAfter)
                        client.EditMemberChangeListener(listenerOwner, NeoValueOwnership.Save, "field", IntType(), target, false);
                });
            }
            finally
            {
                if (conflict)
                    Assert.That(() => client.ExitScriptWrites(), Throws.TypeOf<Runtime.NeoScript.NSGetterRuntimeError>().With.Message.Contains("already has wiring"));
                else
                    client.ExitScriptWrites();
            }
            if (conflict)
            {
                Assert.That(client.SerializeSaveData(), Is.EqualTo(beforeMove));
                Assert.That(client.TryGetValue(scope, "items", out ArrayMemberValue? source), Is.True);
                CollectionAssert.AreEqual(new[] { "moving" }, source!.value);
                if (!pending)
                {
                    using var restored = new NeoMemberClassWritable(client, virtualChild ? "nested-member" : "child-member", listenerOwner, scope);
                    restored.Get<NeoMemberIntWritable>("Count").Set(7);
                    CollectionAssert.AreEqual(new[] { 7d }, heard, "Rollback preserves the original root's listener registration.");
                }
                return;
            }
            if (virtualChild)
                Assert.That(JObject.Parse(client.SerializeSaveData())["values"]?[listenerOwner], Is.Null,
                    "Moving wiring for the virtual descendant must not pin a value override.");
            if (promoteEndpoints && !removeAfter)
                client.EditMemberChangeListener(listenerOwner, NeoValueOwnership.Save, "field", IntType(), target, true);
            using (var owner = new NeoMemberClassWritable(client, virtualChild ? "nested-member" : "child-member", listenerOwner, NeoValueOwnership.Save))
                owner.Get<NeoMemberIntWritable>("Count").Set(7);
            CollectionAssert.AreEqual(removeAfter ? Array.Empty<double>() : new[] { 7d }, heard);
            var saved = JObject.Parse(client.SerializeSaveData());
            if (session || removeAfter)
                Assert.That(saved["changeListeners"], Is.Null);
            else
                Assert.That(saved["changeListeners"]?["save"]?[listenerOwner]?["field"]?[0]?["valueId"]?.Value<string>(), Is.EqualTo("save"));
            if (promoteEndpoints && !removeAfter)
            {
                client.EditMemberChangeListener(listenerOwner, NeoValueOwnership.Save, "field", IntType(), target, false);
                Assert.That(JObject.Parse(client.SerializeSaveData())["changeListeners"], Is.Null);
                heard.Clear();
                using (var owner = new NeoMemberClassWritable(client, virtualChild ? "nested-member" : "child-member", listenerOwner, NeoValueOwnership.Save))
                    owner.Get<NeoMemberIntWritable>("Count").Set(8);
                Assert.That(heard, Is.Empty);
                client.EditMemberChangeListener(listenerOwner, NeoValueOwnership.Save, "field", IntType(), target, true);
                Assert.That(JObject.Parse(client.SerializeSaveData())["changeListeners"]?["save"]?[listenerOwner]?["field"], Is.Not.Null,
                    "After removal, a new registration uses its now-durable endpoints.");
            }
        }

        [Test]
        public void ScriptBatchDispatchesOnceWithTheCommittedValueEvenWhenSubscribedAfterWriting()
        {
            var heard = new List<double>();
            using var client = BuildClient(value => heard.Add(value));
            client.EnterScriptWrites();
            try
            {
                WriteCount(client, 1);
                WriteCount(client, 2);
                client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                    new NeoDelegateValue { memberId = "handler", valueId = "save" }, true);
                Assert.That(heard, Is.Empty);
            }
            finally
            {
                client.ExitScriptWrites();
            }
            CollectionAssert.AreEqual(new[] { 2d }, heard);
            WriteCount(client, 2);
            CollectionAssert.AreEqual(new[] { 2d, 2d }, heard, "An explicit equal local write still dispatches.");
            WriteCount(client, 3);
            CollectionAssert.AreEqual(new[] { 2d, 2d, 3d }, heard);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SaveListenerUsesSaveOwnerWhenSessionShadowsTheSameId(bool explicitReceiver)
        {
            var heard = new List<double>();
            using var client = BuildClient(heard.Add);
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                new NeoDelegateValue { memberId = "handler", valueId = explicitReceiver ? "save" : null }, true);
            client.SetWritableValue(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = "save",
                classId = "owner-class",
                value = new Dictionary<string, string> { ["Count"] = "session-count" },
            });
            client.SetWritableValue(NeoValueOwnership.Session, new NumberMemberValue { id = "session-count", value = 91 });
            var hints = client.CaptureListenerEndpoints();
            Assert.That(hints["save"].rootId, Is.EqualTo("save"));
            Assert.That(hints["save"].rootMemberId, Is.EqualTo("save-member"));
            Assert.That(hints["save"].steps, Is.Empty);
            Assert.That(client.CaptureListenerEndpoints(new Dictionary<string, HashSet<string>>()), Is.Empty);
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["handler"] = (_, receiver, args) =>
                {
                    Assert.That(receiver, Is.InstanceOf<IDictionary<string, object?>>());
                    Assert.That(((IDictionary<string, object?>)receiver!)["Count"], Is.EqualTo("count"),
                        "the handler receives the Save row, not the same-id Session shadow pointing to session-count");
                    heard.Add(Convert.ToDouble(args[0]));
                    return null;
                },
            });
            heard.Clear();
            WriteCount(client, 7);
            CollectionAssert.AreEqual(new[] { 7d }, heard);
        }

        [Test]
        public void SaveAndSessionListenersWithTheSameReceiverIdDispatchInTheirOwnScopes()
        {
            using var client = BuildClient();
            var target = new NeoDelegateValue { memberId = "handler", valueId = "save" };
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), target, true);
            client.SetWritableValue(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = "save",
                classId = "owner-class",
                value = new Dictionary<string, string> { ["Count"] = "session-count" },
            });
            client.SetWritableValue(NeoValueOwnership.Session, new NumberMemberValue { id = "session-count", value = 91 });
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), target, true);
            var receivers = new List<string>();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["handler"] = (_, receiver, args) =>
                {
                    receivers.Add((string)((IDictionary<string, object?>)receiver!)["Count"]!);
                    Assert.That(Convert.ToDouble(args[0]), Is.EqualTo(7d));
                    return null;
                },
            });

            WriteCount(client, 7);

            CollectionAssert.AreEqual(new[] { "count", "session-count" }, receivers);
        }

        [Test]
        public void ReplacingTheObservedChildRebindsTheListenerIndex()
        {
            var heard = new List<double>();
            using var client = BuildClient(heard.Add);
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                new NeoDelegateValue { memberId = "handler" }, true);
            var plan = new NeoWritePlan(client);
            plan.Set(NeoValueOwnership.Save, new NumberMemberValue { id = "replacement-count", value = 5 });
            plan.Set(NeoValueOwnership.Save, new ObjectMemberValue
            {
                id = "save",
                classId = "owner-class",
                value = new Dictionary<string, string> { ["Count"] = "replacement-count" },
            });
            plan.Commit();
            client.TryGetMember("field", out Member? member);
            Assert.That(client.TryWriteLeaf(NeoValueOwnership.Save,
                new NumberMemberValue { id = "replacement-count", value = 8 }, member!, "value"), Is.True);
            CollectionAssert.AreEqual(new[] { 5d, 8d }, heard);
        }

        [Test]
        public void RebindingAwayAndBackStillNotifiesOnceWithTheFinalValue()
        {
            var heard = new List<double>();
            using var client = BuildClient(heard.Add);
            client.SetWritableValue(NeoValueOwnership.Save, new NumberMemberValue { id = "other-count", value = 9 });
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                new NeoDelegateValue { memberId = "handler" }, true);
            client.EnterScriptWrites();
            try
            {
                foreach (string id in new[] { "other-count", "count" })
                    client.SetWritableValue(NeoValueOwnership.Save, new ObjectMemberValue
                    {
                        id = "save",
                        classId = "owner-class",
                        value = new Dictionary<string, string> { ["Count"] = id },
                    });
            }
            finally
            {
                client.ExitScriptWrites();
            }
            CollectionAssert.AreEqual(new[] { 0d }, heard);
        }

        [Test]
        public void SameIdClassReplacementNotifiesButDeepFieldWritesDoNot()
        {
            int calls = 0;
            using var client = BuildClient(configure: data =>
            {
                data.members["field"] = new ClassMember
                {
                    id = "field",
                    projectId = "listeners",
                    name = "Count",
                    kind = MemberKind.Class,
                    classId = "child-class",
                    Requirement = NeoMemberRequirementKind.Required,
                };
                data.members["child-field"] = new IntMember
                {
                    id = "child-field",
                    projectId = "listeners",
                    name = "Value",
                    kind = MemberKind.Int,
                    Requirement = NeoMemberRequirementKind.Required,
                };
                data.classes["child-class"] = new NeoSchemaClass
                {
                    id = "child-class",
                    projectId = "listeners",
                    name = "Child",
                    schema = new Dictionary<string, string> { ["Value"] = "child-field" },
                };
                data.values["count"] = new ObjectMemberValue
                {
                    id = "count",
                    classId = "child-class",
                    value = new Dictionary<string, string> { ["Value"] = "child-value" },
                };
                data.values["child-value"] = new NumberMemberValue { id = "child-value", value = 1 };
                ((FunctionMember)data.members["handler"]).argumentTypes = new[]
                {
                    new FunctionArgumentTypeInfo { name = "next", type = MemberKind.Class, classId = "child-class", required = true },
                };
            });
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["handler"] = (_, _, _) => { calls++; return null; },
            });
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field",
                new ClassTypeInfo { type = MemberKind.Class, classId = "child-class", required = true },
                new NeoDelegateValue { memberId = "handler" }, true);
            client.TryGetMember("child-field", out Member? childField);
            Assert.That(client.TryWriteLeaf(NeoValueOwnership.Save,
                new NumberMemberValue { id = "child-value", value = 2 }, childField!, "value"), Is.True);
            Assert.That(calls, Is.Zero);
            var plan = new NeoWritePlan(client);
            client.TryGetMember("field", out Member? field);
            client.StageInPlaceReplacement(plan, NeoValueOwnership.Save, new ObjectMemberValue
            {
                id = "count",
                classId = "child-class",
                value = new Dictionary<string, string> { ["Value"] = "child-value" },
            }, field);
            plan.Commit();
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void NestedListMembershipDoesNotNotifyOuterButEntryReplacementDoes()
        {
            int calls = 0;
            var innerType = new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = IntType() };
            using var client = BuildClient(configure: data =>
            {
                data.members["field"] = new ListMember
                {
                    id = "field",
                    projectId = "listeners",
                    name = "Count",
                    kind = MemberKind.List,
                    entryMemberId = "inner-member",
                    Requirement = NeoMemberRequirementKind.Required,
                };
                data.members["inner-member"] = new ListMember
                {
                    id = "inner-member",
                    projectId = "listeners",
                    name = "Inner",
                    kind = MemberKind.List,
                    entryMemberId = "entry-member",
                    Requirement = NeoMemberRequirementKind.Required,
                };
                data.members["entry-member"] = new IntMember
                {
                    id = "entry-member",
                    projectId = "listeners",
                    name = "Entry",
                    kind = MemberKind.Int,
                    Requirement = NeoMemberRequirementKind.Required,
                };
                data.values["count"] = new ArrayMemberValue { id = "count", value = new[] { "inner" } };
                data.values["inner"] = new ArrayMemberValue { id = "inner", value = Array.Empty<string>() };
                ((FunctionMember)data.members["handler"]).argumentTypes = new[]
                {
                    new FunctionArgumentTypeInfo { name = "next", type = MemberKind.List, entryTypeInfo = innerType, required = true },
                };
            });
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["handler"] = (_, _, _) => { calls++; return null; },
            });
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field",
                new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = innerType },
                new NeoDelegateValue { memberId = "handler" }, true);
            using (var inner = new NeoMemberListWritable(client, "inner-member", "inner", NeoValueOwnership.Save))
                inner.AddSerialized(NeoValueWritePayload.FromValue(3));
            Assert.That(calls, Is.Zero);
            var plan = new NeoWritePlan(client);
            client.TryGetMember("inner-member", out Member? member);
            client.StageInPlaceReplacement(plan, NeoValueOwnership.Save,
                new ArrayMemberValue { id = "inner", value = Array.Empty<string>() }, member);
            plan.Commit();
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void FailedHandlerLeavesTheWriteCommittedAndNextBatchUsable()
        {
            int calls = 0;
            using var client = BuildClient(_ =>
            {
                if (++calls == 1)
                    throw new InvalidOperationException("fixture callback failure");
            });
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                new NeoDelegateValue { memberId = "handler", valueId = "save" }, true);
            Assert.Catch(() => WriteCount(client, 1));
            client.TryGetValue(NeoValueOwnership.Save, "count", out MemberValue? row);
            Assert.That(((NumberMemberValue)row!).value, Is.EqualTo(1));
            Assert.DoesNotThrow(() => WriteCount(client, 2));
            Assert.That(calls, Is.EqualTo(2));
        }

        [Test]
        public void SubscriptionsChangeOnlyRootMetadataAndEmptyPairsLeaveNoDelta()
        {
            using var client = BuildClient();
            using var owner = new NeoMemberClassWritable(client, "save-member", "save", NeoValueOwnership.Save);
            int notifications = 0;
            owner.OnChanged += _ => notifications++;
            var target = new NeoDelegateValue { memberId = "handler", valueId = "save" };

            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), target, true);
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), target, true);
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, "save", out MemberValue? root), Is.True);
            var saved = JObject.Parse(client.SerializeSaveData());
            Assert.That(((JArray)saved["changeListeners"]!["save"]!["save"]!["field"]!).Count, Is.EqualTo(1));
            Assert.That(saved["values"]!["save"], Is.Null);
            Assert.That(root!.changeListeners, Is.Null);
            Assert.That(((ObjectMemberValue)root).value!["Count"], Is.EqualTo("count"));
            Assert.That(notifications, Is.Zero);

            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), target, false);
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), target, false);
            client.TryGetValue(NeoValueOwnership.Save, "save", out root);
            Assert.That(root!.changeListeners, Is.Null);
            Assert.That(JObject.Parse(client.SerializeSaveData())["changeListeners"], Is.Null);
            Assert.That(notifications, Is.Zero);
        }

        [TestCase("owner", false)]
        [TestCase("member", false)]
        [TestCase("receiver", false)]
        [TestCase("empty", false)]
        [TestCase("lifetime", false)]
        [TestCase("owner", true)]
        [TestCase("receiver", true)]
        public void ListenerEditsPruneStaleEntriesWhileValueWritesAndDormantPartitionsPreserveThem(string stale, bool dormant)
        {
            using var original = BuildClient();
            var saved = JObject.Parse(original.SerializeSaveData());
            string ownerId = stale == "owner" ? "missing-owner" : "save";
            string memberId = stale == "member" ? "missing-member" : "field";
            string receiverId = stale == "receiver" ? "missing-receiver" : stale == "lifetime" ? "session" : "save";
            var entry = new JObject
            {
                [memberId] = stale == "empty" ? new JArray() : new JArray(
                    JObject.FromObject(new NeoDelegateValue { memberId = "handler", valueId = receiverId })),
            };
            saved["changeListeners"] = new JObject { ["save"] = new JObject { [ownerId] = entry } };
            using var client = BuildClient(configure: data =>
            {
                data.classes["owner-class"].schema["Other"] = "other-field";
                data.members["other-field"] = new IntMember
                {
                    id = "other-field",
                    projectId = "listeners",
                    name = "Other",
                    kind = MemberKind.Int,
                    Requirement = NeoMemberRequirementKind.Required,
                    defaultValue = new NumberMemberValueBase { value = 0 },
                };
                if (dormant)
                    data.valuePartitions = new Dictionary<string, JToken> { ["world:inactive"] = new JObject() };
            }, loadedSaveContent: saved.ToString(Formatting.None));
            WriteCount(client, 1);
            Assert.That(JToken.DeepEquals(JObject.Parse(client.SerializeSaveData())["changeListeners"], saved["changeListeners"]), Is.True);
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "other-field", IntType(),
                new NeoDelegateValue { memberId = "handler", valueId = "save" }, true);
            var after = JObject.Parse(client.SerializeSaveData())["changeListeners"]!["save"]!;
            Assert.That(after["save"]!["other-field"], Is.Not.Null);
            Assert.That(after[ownerId]?[memberId] is not null, Is.EqualTo(dormant));
        }

        [Test]
        public void SessionSuppressionSurvivesUnknownInheritedReceiverResidency()
        {
            using var client = BuildClient(configure: data =>
            {
                data.valuePartitions = new Dictionary<string, JToken> { ["world:inactive"] = new JObject() };
                data.values["save"].changeListeners = new NeoChangeListenerMap
                {
                    ["save"] = new Dictionary<string, NeoDelegateValue[]>
                    {
                        ["field"] = new[] { new NeoDelegateValue { memberId = "handler", valueId = "missing-receiver" } },
                    },
                };
            });
            var plan = new NeoWritePlan(client);
            plan.SetListenerEntry(NeoValueOwnership.Session, "save", "save",
                new Dictionary<string, NeoDelegateValue[]>
                {
                    ["field"] = Array.Empty<NeoDelegateValue>(),
                    ["missing-member"] = Array.Empty<NeoDelegateValue>(),
                }, NeoValueOwnership.Save);
            plan.Commit();
            var entry = plan.ListenerEntries![(NeoValueOwnership.Session, NeoValueOwnership.Save, "save", "save")];
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.ContainsKey("field"), Is.True);
            Assert.That(entry.ContainsKey("missing-member"), Is.False);
        }

        [Test]
        public void RemovingTheLastSessionListenerDoesNotExposeAnUnresolvedDefaultLater()
        {
            var heard = new List<double>();
            using var client = BuildClient(heard.Add, data =>
            {
                data.valuePartitions = new Dictionary<string, JToken> { ["world:inactive"] = new JObject() };
                data.values["save"].changeListeners = new NeoChangeListenerMap
                {
                    ["save"] = new Dictionary<string, NeoDelegateValue[]>
                    {
                        ["field"] = new[] { new NeoDelegateValue { memberId = "handler", valueId = "late-receiver" } },
                    },
                };
            });
            var temporary = new NeoDelegateValue { memberId = "handler", valueId = "session" };
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), temporary, true);
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), temporary, false);
            var load = new NeoWritePlan(client);
            load.Set(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = "late-receiver",
                classId = "owner-class",
                value = new Dictionary<string, string>(),
            });
            load.Commit();
            WriteCount(client, 7);
            Assert.That(heard, Is.Empty);
        }

        [Test]
        public void DurableListenerFormatPromotionSurvivesRemovingTheLastListener()
        {
            using var client = BuildClient();
            Assert.That(JObject.Parse(client.SerializeSaveData())["requiredSaveFormatRevision"], Is.Null);
            var target = new NeoDelegateValue { memberId = "handler" };
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), target, true);
            Assert.That((int?)JObject.Parse(client.SerializeSaveData())["requiredSaveFormatRevision"], Is.EqualTo(2));
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), target, false);
            var saved = JObject.Parse(client.SerializeSaveData());
            Assert.That(saved["changeListeners"], Is.Null);
            Assert.That((int?)saved["requiredSaveFormatRevision"], Is.EqualTo(2));
            using var reloaded = BuildClient(loadedSaveContent: saved.ToString(Formatting.None));
            Assert.That((int?)JObject.Parse(reloaded.SerializeSaveData())["requiredSaveFormatRevision"], Is.EqualTo(2));
        }

        [Test]
        public void UnsupportedSaveFormatCannotBecomeDefaultGameplayState()
        {
            using var client = BuildClient();
            var saved = JObject.Parse(client.SerializeSaveData());
            saved["requiredSaveFormatRevision"] = 3;
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(loadedSaveContent: saved.ToString(Formatting.None)));
            StringAssert.Contains("Upgrade", error!.Message);
        }

        [TestCase("2.5")]
        [TestCase("\"2\"")]
        [TestCase("true")]
        [TestCase("{}")]
        [TestCase("[]")]
        [TestCase("2147483648")]
        public void MalformedSaveFormatCannotBecomeDefaultGameplayState(string marker)
        {
            using var client = BuildClient();
            var saved = JObject.Parse(client.SerializeSaveData());
            saved["requiredSaveFormatRevision"] = JToken.Parse(marker);
            Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(loadedSaveContent: saved.ToString(Formatting.None)));
        }

        [Test]
        public void ExternalApplyRetainsMarkerAfterAllListenersWereRemoved()
        {
            using var client = BuildClient();
            var saved = JObject.Parse(client.SerializeSaveData());
            saved["requiredSaveFormatRevision"] = 2;
            client.ApplyExternalSaveContent(saved.ToString(Formatting.None));
            Assert.That((int?)JObject.Parse(client.SerializeSaveData())["requiredSaveFormatRevision"], Is.EqualTo(2));
            saved.Remove("requiredSaveFormatRevision");
            client.ApplyExternalSaveContent(saved.ToString(Formatting.None));
            Assert.That((int?)JObject.Parse(client.SerializeSaveData())["requiredSaveFormatRevision"], Is.EqualTo(2));
        }

        [Test]
        public void FailedExternalApplyDoesNotPromoteSaveFormat()
        {
            string handlerId = new string('h', NeoChangeListenerPatches.MaxRecordBytes);
            using var client = BuildClient(configure: data =>
            {
                var handler = JsonConvert.DeserializeObject<FunctionMember>(JsonConvert.SerializeObject(data.members["handler"]))!;
                handler.id = handlerId;
                data.members[handlerId] = handler;
                data.classes["owner-class"].schema["LargeHandler"] = handlerId;
            });
            string before = client.SerializeSaveData();
            var incoming = JObject.Parse(before);
            incoming["requiredSaveFormatRevision"] = 2;
            incoming["values"] = JObject.FromObject(new Dictionary<string, MemberValue>
            {
                ["count"] = new NumberMemberValue { id = "count", value = 7 },
            });
            incoming["changeListeners"] = JObject.FromObject(new Dictionary<string, NeoChangeListenerMap>
            {
                ["save"] = new NeoChangeListenerMap
                {
                    ["save"] = new Dictionary<string, NeoDelegateValue[]>
                    {
                        ["field"] = new[] { new NeoDelegateValue { memberId = handlerId } },
                    },
                },
            });
            var error = Assert.Catch(() => client.ApplyExternalSaveContent(incoming.ToString(Formatting.None)));
            Assert.That(error!.Message, Does.Contain("save"));
            Assert.That(client.SerializeSaveData(), Is.EqualTo(before));
        }

        [Test]
        public void SessionReceiverNeverEntersSaveMetadata()
        {
            using var client = BuildClient();
            var before = client.SerializeSaveData();
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                new NeoDelegateValue { memberId = "handler", valueId = "session" }, true);
            Assert.That(client.SerializeSaveData(), Is.EqualTo(before));
        }

        [Test]
        public void PlanCheckpointRollsBackListenerEdits()
        {
            using var client = BuildClient();
            var plan = new NeoWritePlan(client);
            var checkpoint = plan.Open();
            plan.SetListenerEntry(NeoValueOwnership.Save, "save", "save",
                new Dictionary<string, NeoDelegateValue[]> { ["field"] = Array.Empty<NeoDelegateValue>() });
            plan.Rollback(checkpoint);
            Assert.That(plan.ListenerEntries, Is.Empty);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        public void IndependentCloneRemapsSavedOrAuthoredInternalWiring(bool saved, bool inferredClass)
        {
            var heard = new List<double>();
            using var client = BuildClient(heard.Add, configure: data =>
            {
                if (!saved)
                    data.values["save"].changeListeners = new NeoChangeListenerMap
                    {
                        ["save"] = new Dictionary<string, NeoDelegateValue[]>
                        {
                            ["field"] = new[] { new NeoDelegateValue { memberId = "handler", valueId = "save" } },
                        },
                    };
                if (inferredClass)
                    data.values["save"].classId = null;
            });
            if (saved)
                client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                new NeoDelegateValue { memberId = "handler", valueId = "save" }, true);
            string clonedId = client.CloneValueReference("save", NeoValueOwnership.Save);
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, clonedId, out MemberValue? clone), Is.True);
            Assert.That(clone!.copiedChangeListeners![clonedId]["field"][0].valueId, Is.EqualTo(clonedId));
            Assert.That(heard, Is.Empty, "Copying the initial state must not invoke listeners.");
            using var wrapper = new NeoMemberClassWritable(client, "save-member", clonedId, NeoValueOwnership.Session);
            wrapper.Get<NeoMemberIntWritable>("Count").Set(7);
            CollectionAssert.AreEqual(new[] { 7d }, heard);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void ConstructionProjectionPreservesExternalDefaultsWhileIndependentCloneDropsThem(bool projection, bool sessionImport)
        {
            string source = sessionImport ? "session" : "save";
            string receiver = sessionImport ? "save" : "session";
            using var client = BuildClient(configure: data => data.values[source].changeListeners = new NeoChangeListenerMap
            {
                [source] = new Dictionary<string, NeoDelegateValue[]>
                {
                    ["field"] = new[] { new NeoDelegateValue { memberId = "handler", valueId = receiver } },
                },
            });
            string clonedId;
            if (sessionImport)
            {
                client.ValueNode(source)!.sharedGetterResult = true;
                clonedId = client.ImportValueReference(NeoValueOwnership.Session, source,
                    listenerCopyIntent: projection ? NeoClient.ListenerCopyIntent.ConstructionProjection : NeoClient.ListenerCopyIntent.IndependentCopy);
            }
            else
            {
                Assert.That(client.TryGetMember("save-member", out Member? member), Is.True);
                clonedId = projection
                    ? client.CloneOwnedValueReferenceForNewParent(NeoValueOwnership.Session, NeoValueOwnership.Save, source, member)
                    : client.CloneValueReference(source, NeoValueOwnership.Save);
            }
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, clonedId, out MemberValue? clone), Is.True);
            var targets = clone!.copiedChangeListeners![clonedId]["field"];
            Assert.That(targets.Length, Is.EqualTo(projection ? 1 : 0));
            if (projection)
                Assert.That(targets[0].valueId, Is.EqualTo(receiver));
        }

        [Test]
        public void PendingSessionConstructionProjectionRollsBackWithItsEnclosingOperation()
        {
            using var client = BuildClient();
            var before = client.SerializeSaveData();
            var plan = new NeoWritePlan(client);
            var checkpoint = plan.Open();
            plan.Set(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = "session",
                classId = "owner-class",
                value = new Dictionary<string, string>(),
                copiedChangeListeners = new NeoChangeListenerMap
                {
                    ["session"] = new Dictionary<string, NeoDelegateValue[]>
                    {
                        ["field"] = new[] { new NeoDelegateValue { memberId = "handler", valueId = "save" } },
                    },
                },
            });
            string imported;
            using (client.ReadCandidate(plan))
            {
                client.ValueNode("session")!.sharedGetterResult = true;
                imported = client.ImportValueReference(NeoValueOwnership.Session, "session",
                    listenerCopyIntent: NeoClient.ListenerCopyIntent.ConstructionProjection);
                Assert.That(imported, Is.Not.EqualTo("session"));
                Assert.That(plan.Resolve(NeoValueOwnership.Session, imported)!.copiedChangeListeners![imported]["field"][0].valueId,
                    Is.EqualTo("save"));
            }
            plan.Rollback(checkpoint);
            Assert.That(plan.Rows, Is.Empty);
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, imported, out MemberValue? _), Is.False);
            Assert.That(client.SerializeSaveData(), Is.EqualTo(before));
        }

        [Test]
        public void PendingCopiedBaselinesSeparateSaveAndSessionInstancesWithTheSameId()
        {
            using var client = BuildClient();
            var plan = new NeoWritePlan(client);
            foreach (var scope in new[] { NeoValueOwnership.Save, NeoValueOwnership.Session })
                plan.Set(scope, new ObjectMemberValue
                {
                    id = "shared-id",
                    classId = "owner-class",
                    value = new Dictionary<string, string>(),
                    copiedChangeListeners = new NeoChangeListenerMap
                    {
                        ["shared-id"] = new Dictionary<string, NeoDelegateValue[]>
                        {
                            ["field"] = scope == NeoValueOwnership.Save ? Array.Empty<NeoDelegateValue>()
                                : new[] { new NeoDelegateValue { memberId = "handler" } },
                        },
                    },
                });
            using (client.ReadCandidate(plan))
                foreach (var scope in new[] { NeoValueOwnership.Save, NeoValueOwnership.Session })
                {
                    string copy = client.CloneValueReference("shared-id", scope);
                    Assert.That(client.TryGetValue(NeoValueOwnership.Session, copy, out MemberValue? row), Is.True);
                    Assert.That(row!.copiedChangeListeners![copy]["field"].Length,
                        Is.EqualTo(scope == NeoValueOwnership.Save ? 0 : 1));
                }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CopyOfAPendingAttachedCopyRetainsItsBaseline(bool rollbackCarrier)
        {
            using var client = BuildClient(configure: data =>
            {
                data.values["save"].changeListeners = new NeoChangeListenerMap
                {
                    ["save"] = new Dictionary<string, NeoDelegateValue[]> { ["field"] = new[] { new NeoDelegateValue { memberId = "handler", valueId = "save" } } },
                };
                data.classes["container"] = new NeoSchemaClass
                {
                    id = "container",
                    projectId = "listeners",
                    name = "Container",
                    schema = new Dictionary<string, string> { ["Child"] = "child-member" },
                };
                data.members["child-member"] = new ClassMember
                {
                    id = "child-member",
                    projectId = "listeners",
                    name = "Child",
                    kind = MemberKind.Class,
                    classId = "owner-class",
                };
                ((ClassMember)data.members["session-member"]).classId = "container";
                data.values["session"].classId = "container";
            });
            var plan = new NeoWritePlan(client);
            string before = client.SerializeSaveData();
            string first = client.ImportValueReference(plan, NeoValueOwnership.Session, "save", out bool _);
            plan.Set(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = "session",
                classId = "container",
                value = new Dictionary<string, string> { ["Child"] = first },
            });
            if (rollbackCarrier)
            {
                Assert.That(plan.CopiedListenerCarriers(first), Is.Not.Empty);
                var checkpoint = plan.Open();
                plan.Remove(NeoValueOwnership.Session, first);
                Assert.That(plan.CopiedListenerCarriers(first), Is.Empty);
                plan.Rollback(checkpoint);
            }
            string second;
            using (client.ReadCandidate(plan))
                second = client.ImportValueReference(plan, NeoValueOwnership.Save, first, out bool _);
            Assert.That(second, Is.Not.EqualTo(first));
            var clone = plan.Resolve(NeoValueOwnership.Save, second);
            Assert.That(clone!.copiedChangeListeners![second]["field"][0].valueId, Is.EqualTo(second));
            Assert.That(client.SerializeSaveData(), Is.EqualTo(before));
        }

        [TestCase(false, false, false)]
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public void AdoptedClonePreservesSessionSubscriptionsWithoutPersistingThem(bool sessionMember, bool pendingEdit, bool editCopiedSlot)
        {
            var heard = new List<double>();
            void Configure(ProjectData data)
            {
                data.classes["container"] = new NeoSchemaClass
                {
                    id = "container",
                    projectId = "listeners",
                    name = "Container",
                    schema = new Dictionary<string, string> { ["Source"] = "source-member", ["Child"] = "child-member" },
                };
                ((ClassMember)data.members["save-member"]).classId = "container";
                data.members["source-member"] = new ClassMember
                {
                    id = "source-member",
                    projectId = "listeners",
                    name = "Source",
                    kind = MemberKind.Class,
                    classId = "owner-class",
                };
                data.values["source"] = new ObjectMemberValue
                {
                    id = "source",
                    classId = "owner-class",
                    value = new Dictionary<string, string> { ["Count"] = "count" },
                };
                data.values["save"] = new ObjectMemberValue
                {
                    id = "save",
                    classId = "container",
                    value = new Dictionary<string, string> { ["Source"] = "source" },
                };
                data.members["child-member"] = new ClassMember
                {
                    id = "child-member",
                    projectId = "listeners",
                    name = "Child",
                    kind = MemberKind.Class,
                    classId = "owner-class",
                };
                data.classes["owner-class"].schema!["Other"] = "other-field";
                data.members["other-field"] = new IntMember { id = "other-field", projectId = "listeners", name = "Other", kind = MemberKind.Int };
                if (sessionMember)
                    data.members["field"].Storage = NeoMemberStorage.Session;
                data.values["save"].changeListeners = new NeoChangeListenerMap
                {
                    ["source"] = new Dictionary<string, NeoDelegateValue[]>
                    {
                        ["field"] = new[] { new NeoDelegateValue { memberId = "handler", valueId = "source" } },
                        ["other-field"] = new[] { new NeoDelegateValue { memberId = "handler", valueId = "source" } },
                    },
                };
            }
            using var client = BuildClient(heard.Add, Configure);
            string copy = client.CloneValueReference("source", NeoValueOwnership.Save);
            client.EditMemberChangeListener(copy, NeoValueOwnership.Session, "field", IntType(),
                new NeoDelegateValue { memberId = "handler", valueId = "session" }, true);
            var plan = new NeoWritePlan(client);
            if (pendingEdit)
            {
                var copiedOwner = (ObjectMemberValue)plan.Resolve(NeoValueOwnership.Session, copy)!;
                plan.Set(NeoValueOwnership.Session, new NumberMemberValue { id = copiedOwner.value!["Count"], value = 5 });
            }
            string adopted = client.ImportValueReference(plan, NeoValueOwnership.Save, copy, out bool moved);
            Assert.That(moved, Is.True);
            Assert.That(adopted, Is.EqualTo(copy));
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, "save", out MemberValue? root), Is.True);
            var parent = (ObjectMemberValue)client.CloneRowForWrite(root!);
            parent.value!["Child"] = adopted;
            plan.Set(NeoValueOwnership.Save, parent);
            if (editCopiedSlot)
                plan.SetListenerEntry(NeoValueOwnership.Save, "save", adopted,
                    new Dictionary<string, NeoDelegateValue[]> { ["other-field"] = Array.Empty<NeoDelegateValue>() });
            plan.Commit();
            CollectionAssert.AreEqual(pendingEdit ? new[] { 5d, 5d } : Array.Empty<double>(), heard);
            heard.Clear();
            using (var wrapper = new NeoMemberClassWritable(client, "child-member", adopted, NeoValueOwnership.Save))
                wrapper.Get<NeoMemberIntWritable>("Count").Set(7);
            CollectionAssert.AreEqual(new[] { 7d, 7d }, heard, "The baseline and temporary subscription both follow this identity-preserving adoption.");
            if (editCopiedSlot)
            {
                heard.Clear();
                using var wrapper = new NeoMemberClassWritable(client, "child-member", adopted, NeoValueOwnership.Save);
                wrapper.Get<NeoMemberIntWritable>("Other").Set(9);
                Assert.That(heard, Is.Empty, "Editing one copied slot must win without discarding another slot.");
            }
            var serialized = JObject.Parse(client.SerializeSaveData());
            var savedTargets = serialized["changeListeners"]?["save"]?[adopted]?["field"];
            if (sessionMember)
                Assert.That(savedTargets, Is.Null);
            else
                Assert.That(savedTargets?[0]?["valueId"]?.Value<string>(), Is.EqualTo(adopted));
            using var reloaded = BuildClient(heard.Add, Configure, serialized.ToString(Formatting.None));
            heard.Clear();
            using (var wrapper = new NeoMemberClassWritable(reloaded, "child-member", adopted, NeoValueOwnership.Save))
                wrapper.Get<NeoMemberIntWritable>("Count").Set(8);
            CollectionAssert.AreEqual(sessionMember ? Array.Empty<double>() : new[] { 8d }, heard);
            string again = client.CloneValueReference(adopted, NeoValueOwnership.Save);
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, again, out MemberValue? second), Is.True);
            Assert.That(second!.copiedChangeListeners![again]["field"][0].valueId, Is.EqualTo(again));
            if (!sessionMember)
            {
                var reset = new NeoWritePlan(client);
                reset.SetListenerEntry(NeoValueOwnership.Save, "save", adopted, null);
                reset.Commit();
                string afterReset = client.CloneValueReference(adopted, NeoValueOwnership.Save);
                Assert.That(client.TryGetValue(NeoValueOwnership.Session, afterReset, out MemberValue? resetCopy), Is.True);
                Assert.That(resetCopy!.copiedChangeListeners?[afterReset]["field"], Is.Null.Or.Empty,
                    "A copied baseline must not resurrect reset durable wiring.");
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void AdoptedCloneRetainsBaselineForASessionOwnedDescendant(bool strippedExternal)
        {
            var heard = new List<double>();
            using var client = BuildClient(heard.Add, data =>
            {
                data.classes["container"] = new NeoSchemaClass
                {
                    id = "container",
                    projectId = "listeners",
                    name = "Container",
                    schema = new Dictionary<string, string> { ["Source"] = "source-member", ["Copy"] = "copy-member" },
                };
                foreach (string name in new[] { "Source", "Copy" })
                    data.members[name.ToLowerInvariant() + "-member"] = new ClassMember
                    {
                        id = name.ToLowerInvariant() + "-member",
                        projectId = "listeners",
                        name = name,
                        kind = MemberKind.Class,
                        classId = "owner-class",
                    };
                ((ClassMember)data.members["save-member"]).classId = "container";
                data.values["save"] = new ObjectMemberValue
                {
                    id = "save",
                    classId = "container",
                    value = new Dictionary<string, string> { ["Source"] = "source" },
                };
                data.classes["owner-class"].schema!["Part"] = "part-member";
                data.classes["part-class"] = new NeoSchemaClass
                {
                    id = "part-class",
                    projectId = "listeners",
                    name = "Part",
                    schema = new Dictionary<string, string> { ["Count"] = "field", ["Changed"] = "handler" },
                };
                data.members["part-member"] = new ClassMember
                {
                    id = "part-member",
                    projectId = "listeners",
                    name = "Part",
                    kind = MemberKind.Class,
                    classId = "part-class",
                    Storage = NeoMemberStorage.Session,
                };
                data.values["source"] = new ObjectMemberValue
                {
                    id = "source",
                    classId = "owner-class",
                    value = new Dictionary<string, string> { ["Part"] = "part" },
                };
                data.values["part"] = new ObjectMemberValue { id = "part", classId = "part-class", value = new Dictionary<string, string>() };
                data.values["save"].changeListeners = new NeoChangeListenerMap
                {
                    ["part"] = new Dictionary<string, NeoDelegateValue[]>
                    {
                        ["field"] = new[] { new NeoDelegateValue { memberId = "handler", valueId = strippedExternal ? "session" : null } },
                    },
                };
            });
            string copy = client.CloneValueReference("source", NeoValueOwnership.Save);
            var plan = new NeoWritePlan(client);
            client.ImportValueReference(plan, NeoValueOwnership.Save, copy, out bool moved);
            Assert.That(moved, Is.True);
            var parent = (ObjectMemberValue)client.CloneRowForWrite(plan.Resolve(NeoValueOwnership.Save, "save")!);
            parent.value!["Copy"] = copy;
            plan.Set(NeoValueOwnership.Save, parent);
            plan.Commit();
            using var wrapper = new NeoMemberClassWritable(client, "copy-member", copy, NeoValueOwnership.Save);
            var part = wrapper.Get<NeoMemberClassWritable>("Part");
            Assert.That(part.ownership, Is.EqualTo(NeoValueOwnership.Session));
            part.Get<NeoMemberIntWritable>("Count").Set(7);
            CollectionAssert.AreEqual(strippedExternal ? Array.Empty<double>() : new[] { 7d }, heard);
            Assert.That(JObject.Parse(client.SerializeSaveData())["changeListeners"], Is.Null);
        }

        [Test]
        public void IndependentCloneOmitsRuntimeSessionSubscriptions()
        {
            using var client = BuildClient();
            client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                new NeoDelegateValue { memberId = "handler", valueId = "session" }, true);
            string clonedId = client.CloneValueReference("save", NeoValueOwnership.Save);
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, clonedId, out MemberValue? clone), Is.True);
            Assert.That(clone!.copiedChangeListeners, Is.Null);
        }

        [Test]
        public void CandidateSubscriptionUsesTheNewParentAndRollsBackWithIt()
        {
            using var client = BuildClient(configure: data =>
            {
                data.classes["owner-class"].schema["Child"] = "child-member";
                data.classes["child-class"] = new NeoSchemaClass
                {
                    id = "child-class",
                    projectId = "listeners",
                    name = "Child",
                    schema = new Dictionary<string, string> { ["Count"] = "field", ["Changed"] = "handler" },
                };
                data.members["child-member"] = new ClassMember
                {
                    id = "child-member",
                    projectId = "listeners",
                    name = "Child",
                    kind = MemberKind.Class,
                    classId = "child-class",
                    Storage = NeoMemberStorage.Save,
                };
            });
            string before = client.SerializeSaveData();
            var plan = new NeoWritePlan(client);
            var checkpoint = plan.Open();
            Assert.That(client.TryGetValue("save", out ObjectMemberValue? root), Is.True);
            var parent = new ObjectMemberValue { id = root!.id, classId = root.classId, value = new Dictionary<string, string>(root.value!) };
            parent.value!["Child"] = "pending-child";
            plan.Set(NeoValueOwnership.Save, parent);
            plan.Set(NeoValueOwnership.Save, new ObjectMemberValue
            {
                id = "pending-child",
                classId = "child-class",
                value = new Dictionary<string, string>(),
            });
            using (client.ReadCandidate(plan))
            {
                Assert.That(client.TryFindOwnedParent(NeoValueOwnership.Save, "pending-child", out string? parentId), Is.True);
                Assert.That(parentId, Is.EqualTo("save"));
                client.EditMemberChangeListener("pending-child", NeoValueOwnership.Save, "field", IntType(),
                    new NeoDelegateValue { memberId = "handler" }, true);
            }
            Assert.That(plan.ListenerEntries!.ContainsKey((NeoValueOwnership.Save, NeoValueOwnership.Save, "save", "pending-child")), Is.True);
            plan.Rollback(checkpoint);
            Assert.That(plan.ListenerEntries, Is.Empty);
            Assert.That(client.SerializeSaveData(), Is.EqualTo(before));
        }

        [Test]
        public void CandidateSubscriptionRollsBackWithoutPublishingMetadata()
        {
            using var client = BuildClient();
            var before = client.SerializeSaveData();
            var plan = new NeoWritePlan(client);
            var checkpoint = plan.Open();
            using (client.ReadCandidate(plan))
                client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(),
                    new NeoDelegateValue { memberId = "handler" }, true);
            Assert.That(client.SerializeSaveData(), Is.EqualTo(before));
            Assert.That(plan.ListenerEntries!.Count, Is.EqualTo(1));
            plan.Rollback(checkpoint);
            Assert.That(plan.ListenerEntries, Is.Empty);
            Assert.That(client.SerializeSaveData(), Is.EqualTo(before));
        }

        [Test]
        public void HeldSubscriptionEditsReadEachOtherAndCommitWithoutValueRows()
        {
            using var client = BuildClient();
            long revision = client.WriteRevision;
            client.EnterScriptWrites();
            try
            {
                var first = new NeoDelegateValue { memberId = "handler" };
                var second = new NeoDelegateValue { memberId = "handler", valueId = "save" };
                client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), first, true);
                client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), second, true);
                client.EditMemberChangeListener("save", NeoValueOwnership.Save, "field", IntType(), first, false);
                Assert.That(client.WriteRevision, Is.EqualTo(revision));
                Assert.That(client.PendingScriptWrites!.Plan.Rows, Is.Empty);
            }
            finally
            {
                client.ExitScriptWrites();
            }
            var saved = JObject.Parse(client.SerializeSaveData());
            var targets = (JArray)saved["changeListeners"]!["save"]!["save"]!["field"]!;
            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0]["valueId"]!.Value<string>(), Is.EqualTo("save"));
            Assert.That(((JObject)saved["values"]!).Count, Is.Zero);
        }

        [Test]
        public void ListenerMetadataSurvivesDefaultConversionWithoutSharingMutableSets()
        {
            var source = JsonConvert.DeserializeObject<MemberValueBase<double?>>(
                "{\"value\":7,\"changeListeners\":" + Metadata + "}")!;
            var member = new IntMember { id = "field", defaultValue = source };
            var row = MemberValueFactory.CreateFromDefault(member, "value", "x", "x")!;
            var copy = MemberValueFactory.ConvertDeclarationDefault(source, typeof(MemberValueBase<double?>));

            row.changeListeners!["owner"]["field"][0].memberId = "changed";
            Assert.That(source.changeListeners!["owner"]["field"][0].memberId, Is.EqualTo("handler"));
            Assert.That(copy.changeListeners!["owner"]["field"][0].memberId, Is.EqualTo("handler"));
            var encoded = JObject.FromObject(row);
            Assert.That(encoded["changeListeners"]!["owner"]!["field"]![0]!["memberId"]!.Value<string>(), Is.EqualTo("changed"));
            Assert.That(encoded["value"]!.Value<int>(), Is.EqualTo(7));
        }

        [TestCase("{\"owner\":null}")]
        [TestCase("{\"owner\":{\"field\":null}}")]
        [TestCase("{\"owner\":{\"field\":[null]}}")]
        [TestCase("{\"\":{\"field\":[]}}")]
        [TestCase("{\"owner\":{\"\":[]}}")]
        [TestCase("{\"owner\":{\"field\":[{\"memberId\":\"handler\",\"valueId\":null},{\"memberId\":\"handler\",\"valueId\":null}]}}")]
        public void RejectsMalformedListenerMetadata(string json)
        {
            Assert.Catch(() => JsonConvert.DeserializeObject<NeoChangeListenerMap>(json));
        }

        [TestCase(InstructionKind.AddChangeListener, typeof(AddChangeListenerInstruction))]
        [TestCase(InstructionKind.RemoveChangeListener, typeof(RemoveChangeListenerInstruction))]
        public void SubscriptionIrCarriesAnOwnerAndEvaluatedReceiver(string kind, System.Type concrete)
        {
            var json = new JObject
            {
                ["type"] = kind,
                ["target"] = new JObject
                {
                    ["owner"] = new JObject { ["type"] = "variable", ["variableId"] = "owner" },
                    ["memberId"] = "field",
                    ["typeInfo"] = new JObject { ["type"] = (int)MemberKind.Int, ["required"] = true },
                    ["writability"] = "save",
                },
                ["listener"] = new JObject
                {
                    ["type"] = "memberTarget",
                    ["memberId"] = "handler",
                    ["receiver"] = new JObject
                    {
                        ["kind"] = "instance",
                        ["pointer"] = new JObject { ["type"] = "variable", ["variableId"] = "receiver" },
                    },
                },
            };
            var instruction = json.ToObject<Instruction>();
            Assert.That(instruction, Is.TypeOf(concrete));
            var subscription = (ChangeListenerInstruction)instruction!;
            Assert.That(subscription.target.memberId, Is.EqualTo("field"));
            Assert.That(subscription.listener, Is.TypeOf<MemberTargetPointer>());
            Assert.That(((MemberTargetPointer)subscription.listener).receiver.IsInstance, Is.True);
            var roundTrip = JsonConvert.DeserializeObject<Instruction>(JsonConvert.SerializeObject(instruction));
            Assert.That(roundTrip, Is.TypeOf(concrete));
            var receiver = (MemberTargetPointer)((ChangeListenerInstruction)roundTrip!).listener;
            Assert.That(((VariablePointer)receiver.receiver.pointer!).variableId, Is.EqualTo("receiver"));
            Assert.That(((ChangeListenerInstruction)roundTrip).target.typeInfo.type, Is.EqualTo(MemberKind.Int));
        }

        private static PrimitiveTypeInfo IntType() => new() { type = MemberKind.Int, required = true };

        private static void WriteCount(NeoClient client, double value)
        {
            Assert.That(client.TryGetMember("field", out Member? member), Is.True);
            Assert.That(client.TryWriteLeaf(NeoValueOwnership.Save,
                new NumberMemberValue { id = "count", value = value }, member!, "value"), Is.True);
        }

        [Test]
        public void IndependentCloneRemapsNestedRepeatedClassListenersToTheirOwnOccurrence()
        {
            using var client = BuildClient(configure: data =>
            {
                data.classes["owner-class"].schema["Groups"] = "groups";
                foreach (string kind in new[] { "group", "child" })
                {
                    data.classes[kind + "-class"] = new NeoSchemaClass
                    {
                        id = kind + "-class",
                        projectId = "listeners",
                        name = kind,
                        schema = new Dictionary<string, string> { ["Count"] = "field", ["Changed"] = "handler" },
                    };
                    data.members[kind + "-entry"] = new ClassMember
                    {
                        id = kind + "-entry",
                        name = kind,
                        kind = MemberKind.Class,
                        classId = kind + "-class",
                    };
                    data.values[kind + "-count"] = new NumberMemberValue { id = kind + "-count", value = 0 };
                    data.values["source-" + kind] = new ObjectMemberValue
                    {
                        id = "source-" + kind,
                        classId = kind + "-class",
                        value = new Dictionary<string, string> { ["Count"] = kind + "-count" },
                    };
                }
                data.classes["group-class"].schema["Children"] = "children";
                data.members["groups"] = new ListMember { id = "groups", name = "Groups", kind = MemberKind.List, entryMemberId = "group-entry" };
                data.members["children"] = new ListMember { id = "children", name = "Children", kind = MemberKind.List, entryMemberId = "child-entry" };
                ((ObjectMemberValue)data.values["save"]).value!["Groups"] = "source-groups";
                ((ObjectMemberValue)data.values["source-group"]).value!["Children"] = "source-children";
                data.values["source-groups"] = new ArrayMemberValue { id = "source-groups", value = new[] { "source-group", "source-group" } };
                data.values["source-children"] = new ArrayMemberValue { id = "source-children", value = new[] { "source-child", "source-child" } };
                data.values["save"].changeListeners = new NeoChangeListenerMap
                {
                    ["source-child"] = new Dictionary<string, NeoDelegateValue[]>
                    {
                        ["field"] = new[]
                        {
                            new NeoDelegateValue { memberId = "handler", valueId = "source-child" },
                            new NeoDelegateValue { memberId = "handler", valueId = "source-group" },
                        },
                    },
                };
            });
            var heard = new List<(string countId, double value)>();
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["handler"] = (_, receiver, args) =>
                {
                    heard.Add(((string)((IDictionary<string, object?>)receiver!)["Count"]!, Convert.ToDouble(args[0])));
                    return null;
                },
            });
            string copyId = client.CloneValueReference("save", NeoValueOwnership.Save);
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, copyId, out ObjectMemberValue? copy), Is.True);
            Assert.That(client.TryGetValue(NeoValueOwnership.Session, copy!.value!["Groups"], out ArrayMemberValue? groups), Is.True);
            Assert.That(groups!.value!.Distinct().Count(), Is.EqualTo(2), "Repeated sources produce independent owned rows.");
            var children = new HashSet<string>();
            foreach (string groupId in groups.value!)
            {
                Assert.That(client.TryGetValue(NeoValueOwnership.Session, groupId, out ObjectMemberValue? group), Is.True);
                Assert.That(client.TryGetValue(NeoValueOwnership.Session, group!.value!["Children"], out ArrayMemberValue? entries), Is.True);
                foreach (string childId in entries!.value!)
                {
                    Assert.That(children.Add(childId), Is.True);
                    CollectionAssert.AreEqual(new[] { childId, groupId },
                        copy.copiedChangeListeners![childId]["field"].Select(target => target.valueId),
                        "A nested repeated occurrence binds its self and enclosing receivers within that occurrence.");
                    Assert.That(client.TryGetValue(NeoValueOwnership.Session, childId, out ObjectMemberValue? child), Is.True);
                    using var wrapper = new NeoMemberClassWritable(client, "child-entry", childId, NeoValueOwnership.Session);
                    heard.Clear();
                    wrapper.Get<NeoMemberIntWritable>("Count").Set(children.Count);
                    CollectionAssert.AreEqual(new[] { (child!.value!["Count"], (double)children.Count), (group.value["Count"], (double)children.Count) }, heard);
                }
            }
            Assert.That(children.Count, Is.EqualTo(4));
        }


        private static NeoClient BuildClient(Action<double>? handler = null, Action<ProjectData>? configure = null, string? loadedSaveContent = null)
        {
            var members = new Dictionary<string, Member>();
            var values = new Dictionary<string, MemberValue>();
            foreach (var (key, storage) in new[] { ("assets", NeoMemberStorage.Immutable), ("save", NeoMemberStorage.Save), ("session", NeoMemberStorage.Session) })
            {
                members[key + "-member"] = new ClassMember
                {
                    id = key + "-member",
                    projectId = "listeners",
                    name = key,
                    kind = MemberKind.Class,
                    classId = "owner-class",
                    valueId = key,
                    Storage = storage,
                };
                values[key] = new ObjectMemberValue
                {
                    id = key,
                    classId = "owner-class",
                    value = key == "save" ? new Dictionary<string, string> { ["Count"] = "count" } : new Dictionary<string, string>(),
                };
            }
            members["field"] = new IntMember
            {
                id = "field",
                projectId = "listeners",
                name = "Count",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
                defaultValue = new NumberMemberValueBase { value = 0 },
            };
            members["handler"] = new FunctionMember
            {
                id = "handler",
                projectId = "listeners",
                name = "Changed",
                kind = MemberKind.Function,
                returnTypeInfo = new VoidTypeInfo { type = MemberKind.Void, required = true },
                argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "next", type = MemberKind.Int, required = true } },
            };
            values["count"] = new NumberMemberValue { id = "count", value = 0 };
            var data = new ProjectData
            {
                project = new Project
                {
                    id = "listeners",
                    name = "Member listener fixture",
                    rootAssetsMemberId = "assets-member",
                    rootSaveFileMemberId = "save-member",
                    rootSessionMemberId = "session-member",
                },
                members = members,
                values = values,
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["owner-class"] = new NeoSchemaClass
                    {
                        id = "owner-class",
                        projectId = "listeners",
                        name = "Owner",
                        schema = new Dictionary<string, string> { ["Count"] = "field", ["Changed"] = "handler" },
                    },
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
            configure?.Invoke(data);
            var client = NeoTestSaveStack.ClientFromSchema(data, loadedSaveContent: loadedSaveContent);
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["handler"] = (_, _, arguments) =>
                {
                    handler?.Invoke(Convert.ToDouble(arguments[0]));
                    return null;
                },
            });
            return client;
        }
    }
}
