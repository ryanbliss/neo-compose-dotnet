// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoGameSaveRecordProtocolTests
    {
        [Test]
        public void ListenerEndpointHints_RoundTripTypedStepsAndSelectOnlyAddedWiring()
        {
            var old = new NeoDelegateValue { memberId = "old", valueId = "old-receiver" };
            var added = new NeoDelegateValue { memberId = "new", valueId = "new-receiver" };
            var edits = new List<GameSaveListenerOwnerEdit>
            {
                new() { ownerId = "removed", expected = new() { ["Count"] = new[] { old } }, entry = null },
                new() { ownerId = "suppressed", entry = new() { ["Count"] = System.Array.Empty<NeoDelegateValue>() } },
                new() { ownerId = "changed", expected = new() { ["Count"] = new[] { old } }, entry = new() { ["Count"] = new[] { added, old } } },
                new() { ownerId = "retained", expected = new() { ["Count"] = new[] { old, added } }, entry = new() { ["Count"] = new[] { added } } },
            };
            Assert.That(NeoChangeListenerPatches.RequiredEndpointIds(edits),
                Is.EquivalentTo(new[] { "suppressed", "changed", "new-receiver" }));
            var operation = new GameSaveChangeListenersPatchChange
            {
                rootId = "root",
                edits = edits,
                endpoints = new()
                {
                    new()
                    {
                        valueId = "changed", rootId = "root", rootMemberId = "binding",
                        steps = new()
                        {
                            new GameSaveListenerMemberStep { memberId = "Items" },
                            new GameSaveListenerListStep { index = 2 },
                            new GameSaveListenerDictionaryStep { key = "slot" },
                            new GameSaveListenerEntryStep { valueId = "changed" },
                        },
                    },
                },
            };
            var json = JsonConvert.SerializeObject(operation);
            var decoded = (GameSaveChangeListenersPatchChange)JsonConvert.DeserializeObject<GameSaveRecordChange>(json)!;
            Assert.That(decoded.endpoints[0].steps.Select(step => step.GetType()), Is.EqualTo(new[]
            {
                typeof(GameSaveListenerMemberStep), typeof(GameSaveListenerListStep),
                typeof(GameSaveListenerDictionaryStep), typeof(GameSaveListenerEntryStep),
            }));
            Assert.That(JToken.DeepEquals(JToken.Parse(json), JToken.FromObject(decoded)), Is.True);
        }

        [Test]
        public void RecordChanges_RoundTripAsTypedDiscriminatedUnion()
        {
            var patch = new NeoSavePatch
            {
                changes = new List<GameSaveRecordChange>
                {
                    new GameSaveValuePatchChange
                    {
                        valueId = "value-1",
                        baseRecordStateId = "state-1",
                        baseRecordRevisionToken = "token-1",
                        set = { ["value"] = 7 },
                        unset = { "mark" },
                    },
                    new GameSaveStaticBindingSetChange
                    {
                        memberId = "member-1",
                        valueId = null,
                    },
                },
            };

            var json = JsonConvert.SerializeObject(patch);
            var roundTrip = JsonConvert.DeserializeObject<NeoSavePatch>(json)!;

            Assert.That(roundTrip.changes[0], Is.TypeOf<GameSaveValuePatchChange>());
            var value = (GameSaveValuePatchChange)roundTrip.changes[0];
            Assert.That(value.baseRecordStateId, Is.EqualTo("state-1"));
            Assert.That((int?)value.set["value"], Is.EqualTo(7));
            Assert.That(roundTrip.changes[1], Is.TypeOf<GameSaveStaticBindingSetChange>());
            Assert.That(
                ((GameSaveStaticBindingSetChange)roundTrip.changes[1]).valueId,
                Is.Null,
                "null is a semantic static-binding tombstone, not a restore");
        }

        [Test]
        public void Cache_KeysPayloadByRevisionAndRehydratesCanonicalHeadFields()
        {
            var cache = new GameSaveRecordCache();
            var descriptor = Descriptor("token-1", "hash-1");
            cache.StoreStates(
                new[] { descriptor },
                new[]
                {
                    new GameSaveRecordState
                    {
                        id = "state-1",
                        recordKind = NeoGameSaveRecordKinds.Value,
                        recordId = "value-1",
                        dataJson = "{\"value\":3,\"updatedAt\":10}",
                    },
                });
            var values = new JObject();
            cache.ApplyDescriptors(
                new[] { descriptor }, values, new Dictionary<string, string?>());

            Assert.That((string?)values["value-1"]!["id"], Is.EqualTo("value-1"));
            Assert.That((string?)values["value-1"]!["mapKey"], Is.EqualTo("world:grid"));
            Assert.That(cache.FindMissingStateIds(new[] { descriptor }), Is.Empty);

            var rotated = Descriptor("token-2", "hash-1");
            Assert.That(
                cache.FindMissingStateIds(new[] { rotated }),
                Is.EqualTo(new[] { "state-1" }),
                "same mutable state id with a new revision token must invalidate payload");

            cache.StoreStates(
                new[] { rotated },
                new[]
                {
                    new GameSaveRecordState
                    {
                        id = "state-1",
                        recordKind = NeoGameSaveRecordKinds.Value,
                        recordId = "value-1",
                        dataJson = "{\"value\":4}",
                    },
                });
            Assert.That(cache.states, Has.Count.EqualTo(1));
            Assert.That(cache.states.ContainsKey(rotated.StateCacheKey!), Is.True);
        }

        [Test]
        public void BuildLivePatch_EmitsSparseFieldsWithRecordOccBase()
        {
            var baseline = JObject.Parse(
                "{\"value-1\":{\"id\":\"value-1\",\"value\":1," +
                "\"classId\":\"class-1\",\"updatedAt\":1}}");
            var staged = JObject.Parse(
                "{\"value-1\":{\"id\":\"value-1\",\"value\":2," +
                "\"classId\":\"class-1\"," +
                "\"updatedAt\":2}}");
            var cache = new GameSaveRecordCache();
            var descriptor = Descriptor("token-1", "hash-1");
            cache.descriptors[descriptor.LogicalKey] = descriptor;

            var patch = NeoSaveSynchronizer.BuildLivePatch(baseline, staged, cache);

            Assert.That(patch.changes, Has.Count.EqualTo(1));
            var change = patch.changes.Single() as GameSaveValuePatchChange;
            Assert.That(change, Is.Not.Null);
            Assert.That(change!.baseRecordStateId, Is.EqualTo("state-1"));
            Assert.That(change.baseRecordRevisionToken, Is.EqualTo("token-1"));
            Assert.That(change.set.Keys, Is.EqualTo(new[] { "value" }));
            Assert.That(change.unset, Is.Empty);
            Assert.That(change.set.ContainsKey("updatedAt"), Is.False);
        }

        private static Dictionary<string, NeoDelegateValue[]> ListenerEntry(string handler) => new()
        {
            ["Bar"] = new[] { new NeoDelegateValue { memberId = handler, valueId = "receiver" } },
        };

        private static Dictionary<string, GameSaveListenerEndpointLocator> ListenerHints(params string[] ids) =>
            ids.ToDictionary(id => id, id => new GameSaveListenerEndpointLocator
            {
                valueId = id,
                rootId = "root",
                rootMemberId = "root-member",
                steps = new() { new GameSaveListenerMemberStep { memberId = id + "-member" } },
            });

        [Test]
        public void ListenerPatches_JsonOnlyAdditionsFailWithoutPathsButStaleRemovalsSucceed()
        {
            var listeners = new Dictionary<string, NeoChangeListenerMap>
            {
                ["root"] = new() { ["owner"] = ListenerEntry("handler") },
            };
            Assert.That(() => NeoSaveSynchronizer.BuildLivePatch(new(), new(), stagedListeners: listeners),
                Throws.InvalidOperationException.With.Message.Contains("this save capture"));
            var patch = NeoSaveSynchronizer.BuildLivePatch(new(), new(), baselineListeners: listeners);
            var change = (GameSaveChangeListenersPatchChange)patch.changes.Single();
            Assert.That(change.edits.Single().entry, Is.Null);
            Assert.That(change.endpoints, Is.Empty);
        }

        [Test]
        public void ListenerPatches_ReplaceOnlyChangedOwnersWithoutValueRecords()
        {
            var entry = ListenerEntry("handler");
            var baseline = new Dictionary<string, NeoChangeListenerMap>
            {
                ["root"] = new() { ["untouched"] = entry, ["removed"] = entry },
            };
            var staged = new Dictionary<string, NeoChangeListenerMap>
            {
                ["root"] = new() { ["untouched"] = entry, ["owner.with%path"] = entry },
            };
            var patch = NeoSaveSynchronizer.BuildLivePatch(new(), new(), baselineListeners: baseline, stagedListeners: staged,
                listenerEndpoints: ListenerHints("owner.with%path", "receiver"));
            var change = (GameSaveChangeListenersPatchChange)patch.changes.Single();
            Assert.That(change.rootId, Is.EqualTo("root"));
            Assert.That(change.edits.Select(edit => edit.ownerId), Is.EquivalentTo(new[] { "removed", "owner.with%path" }));
            Assert.That(change.edits.Single(edit => edit.ownerId == "removed").entry, Is.Null);
            Assert.That(change.edits.Single(edit => edit.ownerId == "owner.with%path").expected, Is.Null);
            var roundTrip = JsonConvert.DeserializeObject<GameSaveRecordChange>(JsonConvert.SerializeObject(change));
            Assert.That(roundTrip, Is.TypeOf<GameSaveChangeListenersPatchChange>());
            NeoChangeListenerPatches.Apply(baseline, patch);
            Assert.That(NeoChangeListenerMap.Same(baseline["root"], staged["root"]), Is.True);
        }

        [Test]
        public void ListenerPatches_HydrationRetainsConflictBasesAndUntouchedRemoteOwners()
        {
            var baseline = new Dictionary<string, NeoChangeListenerMap>
            {
                ["root"] = new() { ["owner"] = ListenerEntry("remote"), ["other"] = ListenerEntry("other") },
            };
            var pending = new NeoSavePatch
            {
                changes = { new GameSaveChangeListenersPatchChange
            {
                rootId = "root", edits = { new() { ownerId = "owner", expected = ListenerEntry("original"), entry = null } },
            } }
            };
            NeoChangeListenerPatches.Apply(baseline, pending, expected: true);
            Assert.That(baseline["root"]["owner"]["Bar"][0].memberId, Is.EqualTo("original"));
            Assert.That(baseline["root"]["other"]["Bar"][0].memberId, Is.EqualTo("other"));
            NeoChangeListenerPatches.Apply(baseline, pending);
            Assert.That(baseline["root"].ContainsKey("owner"), Is.False);
        }

        [TestCase("owner-id")]
        [TestCase("owner.with%path")]
        [TestCase("🦊/leaf")]
        [TestCase("__proto__")]
        public void ListenerPatches_RoundTripOpaqueOwnerIds(string owner)
        {
            var patch = new NeoSavePatch();
            NeoChangeListenerPatches.AppendChanges(patch, null, new Dictionary<string, NeoChangeListenerMap>
            {
                ["root"] = new() { [owner] = ListenerEntry("handler") },
            }, null, endpoints: ListenerHints(owner, "receiver"));
            var roundTrip = JsonConvert.DeserializeObject<GameSaveChangeListenersPatchChange>(JsonConvert.SerializeObject(patch.changes.Single()))!;
            Assert.That(roundTrip.edits.Single().ownerId, Is.EqualTo(owner));
        }

        [Test]
        public void BuildLivePatch_ReplacesRecordWhenStructuralFieldsChange()
        {
            var baseline = JObject.Parse(
                "{\"value-1\":{\"id\":\"value-1\",\"value\":1," +
                "\"classId\":\"class-1\"}}");
            var staged = JObject.Parse(
                "{\"value-1\":{\"id\":\"value-1\",\"value\":1," +
                "\"classId\":\"class-2\"}}");
            var cache = new GameSaveRecordCache();
            var descriptor = Descriptor("token-1", "hash-1");
            cache.descriptors[descriptor.LogicalKey] = descriptor;

            var patch = NeoSaveSynchronizer.BuildLivePatch(baseline, staged, cache);

            Assert.That(patch.changes, Has.Count.EqualTo(1));
            var change = patch.changes.Single() as GameSaveValueReplaceChange;
            Assert.That(change, Is.Not.Null,
                "fields outside the server allowlist use value.replace");
            Assert.That(change!.baseRecordStateId, Is.EqualTo("state-1"));
            Assert.That(change.baseRecordRevisionToken, Is.EqualTo("token-1"));
            Assert.That((string?)change.value["classId"], Is.EqualTo("class-2"));
        }

        [Test]
        public void DeletedDescriptorsRestoreValueAndBindingToAuthored()
        {
            var cache = new GameSaveRecordCache();
            var values = JObject.Parse("{\"value-1\":{\"value\":1}}");
            var bindings = new Dictionary<string, string?> { ["member-1"] = null };
            cache.ApplyDescriptors(
                new[]
                {
                    new GameSaveRecordDescriptor
                    {
                        recordKind = NeoGameSaveRecordKinds.Value,
                        recordId = "value-1",
                        deleted = true,
                    },
                    new GameSaveRecordDescriptor
                    {
                        recordKind = NeoGameSaveRecordKinds.StaticBinding,
                        recordId = "member-1",
                        deleted = true,
                    },
                },
                values,
                bindings);

            Assert.That(values.ContainsKey("value-1"), Is.False);
            Assert.That(bindings.ContainsKey("member-1"), Is.False);
        }

        private static GameSaveRecordDescriptor Descriptor(string token, string hash) => new()
        {
            recordKind = NeoGameSaveRecordKinds.Value,
            recordId = "value-1",
            mapKey = "world:grid",
            recordStateId = "state-1",
            recordRevisionToken = token,
            contentHash = hash,
        };
    }
}
