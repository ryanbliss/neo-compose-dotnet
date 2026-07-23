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
