// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoGameSaveModelTests
    {
        private const string RemoteJson =
            "{" +
            "\"serverId\":\"server-1\"," +
            "\"id\":\"save-1\"," +
            "\"snapshotId\":\"snap-1\"," +
            "\"snapshotRevision\":1," +
            "\"releaseChannelId\":\"channel-dev\"," +
            "\"snapshotName\":\"Auto 1\"," +
            "\"name\":\"My Save\"," +
            "\"projectId\":\"project-1\"," +
            "\"version\":{\"id\":\"v1\",\"label\":\"1.0\"}," +
            "\"author\":{\"kind\":\"user\",\"id\":\"user-1\"}," +
            "\"actor\":{\"kind\":\"user\",\"id\":\"user-1\"}," +
            "\"values\":{\"v1\":{\"id\":\"v1\",\"value\":true,\"createdAt\":1,\"updatedAt\":2}}," +
            "\"staticBindings\":{\"member-current\":\"v1\",\"member-cleared\":null}," +
            "\"platforms\":null,\"systems\":null,\"inputDevices\":null," +
            "\"createdAt\":1,\"updatedAt\":2,\"synchronizedAt\":3,\"archivedAt\":null" +
            "}";

        [TestCase(0)]
        [TestCase(3)]
        public void UnsupportedSaveFormatCannotFallBackToAnEmptyLocalOrRemoteSave(int required)
        {
            var json = JObject.Parse(RemoteJson);
            json["requiredSaveFormatRevision"] = required;
            string content = json.ToString(Formatting.None);
            Assert.Throws<NeoUnsupportedSaveFormatException>(() => RemoteGameSaveLoader.Load(content));
            Assert.Throws<NeoUnsupportedSaveFormatException>(() => RemoteGameSaveLoader.TryLoad(content, out _));
            Assert.Throws<NeoUnsupportedSaveFormatException>(() => LocalGameSaveLoader.Load(content));
            Assert.Throws<NeoUnsupportedSaveFormatException>(() => LocalGameSaveLoader.TryLoad(content, out _));
            Assert.Throws<NeoUnsupportedSaveFormatException>(() => LocalGameSaveLoader.FromSnapshot(json));
        }

        [TestCase(null)]
        [TestCase(2)]
        public void SaveFormatRequirementSurvivesLocalCopiesWithoutListenerRecords(int? required)
        {
            var json = JObject.Parse(RemoteJson);
            if (required is not null)
                json["requiredSaveFormatRevision"] = required;
            var remote = RemoteGameSaveLoader.Load(json.ToString(Formatting.None));
            Assert.That(RemoteGameSaveSummary.FromRemote(remote).requiredSaveFormatRevision, Is.EqualTo(required));
            var local = LocalGameSave.FromRemote(remote).DetachedCopy();
            Assert.That(local.requiredSaveFormatRevision, Is.EqualTo(required));
            var reloaded = LocalGameSaveLoader.Load(LocalGameSaveLoader.Serialize(local));
            Assert.That(reloaded.requiredSaveFormatRevision, Is.EqualTo(required));
            Assert.That(reloaded.changeListeners, Is.Null);
        }

        [TestCase("2.5")]
        [TestCase("\"2\"")]
        [TestCase("true")]
        [TestCase("{}")]
        [TestCase("[]")]
        [TestCase("2147483648")]
        public void MalformedSaveFormatNeverUsesCorruptSaveFallback(string marker)
        {
            var json = JObject.Parse(RemoteJson);
            json["requiredSaveFormatRevision"] = JToken.Parse(marker);
            string content = json.ToString(Formatting.None);
            Assert.Throws<NeoUnsupportedSaveFormatException>(() => RemoteGameSaveLoader.Load(content));
            Assert.Throws<NeoUnsupportedSaveFormatException>(() => RemoteGameSaveLoader.TryLoad(content, out _));
            Assert.Throws<NeoUnsupportedSaveFormatException>(() => LocalGameSaveLoader.Load(content));
            Assert.Throws<NeoUnsupportedSaveFormatException>(() => LocalGameSaveLoader.TryLoad(content, out _));
            Assert.Throws<NeoUnsupportedSaveFormatException>(() => LocalGameSaveLoader.FromSnapshot(json));
        }

        [Test]
        public void SaveFormatAcceptsIntegralFloat64FromRealtimeWire()
        {
            var json = JObject.Parse(RemoteJson);
            json["requiredSaveFormatRevision"] = 2.0;
            Assert.That(LocalGameSaveLoader.FromSnapshot(json).requiredSaveFormatRevision, Is.EqualTo(2));
        }

        [Test]
        public void RemoteLoader_LoadsEnvelope_AndKeepsValuesOpaque()
        {
            var save = RemoteGameSaveLoader.Load(RemoteJson);

            Assert.That(save.id, Is.EqualTo("save-1"));
            Assert.That(save.serverId, Is.EqualTo("server-1"));
            Assert.That(save.snapshotRevision, Is.EqualTo(1));
            Assert.That(save.releaseChannelId, Is.EqualTo("channel-dev"));
            Assert.That(save.author.id, Is.EqualTo("user-1"));
            // values stayed opaque: the raw token is preserved, not pre-typed.
            Assert.That(save.values.Raw.Type, Is.EqualTo(JTokenType.Object));
            Assert.That((bool)save.values.Raw["v1"]!["value"]!, Is.True);
            Assert.That(save.staticBindings["member-current"], Is.EqualTo("v1"));
            Assert.That(save.staticBindings.ContainsKey("member-cleared"), Is.True);
            Assert.That(save.staticBindings["member-cleared"], Is.Null);
        }

        [Test]
        public void RemoteLoader_UserValueKeysThatMatchOldFieldsRemainOpaque()
        {
            var json = JObject.Parse(RemoteJson);
            json["values"]!["v1"]!["value"] = new JObject
            {
                ["attributeId"] = "authored dictionary key",
                ["typeId"] = "another authored dictionary key",
            };

            var save = RemoteGameSaveLoader.Load(json.ToString());

            Assert.That(
                (string)save.values.Raw["v1"]!["value"]!["attributeId"]!,
                Is.EqualTo("authored dictionary key"));
            Assert.That(
                (string)save.values.Raw["v1"]!["value"]!["typeId"]!,
                Is.EqualTo("another authored dictionary key"));
        }

        [Test]
        public void RemoteLoader_NullArchivedAt_LeavesArchivedAtUnset()
        {
            var save = RemoteGameSaveLoader.Load(RemoteJson);
            Assert.That(save.archivedAt, Is.Null);
        }

        [Test]
        public void TryDeserializeValues_True_MaterializesTypedRows()
        {
            var save = RemoteGameSaveLoader.Load(RemoteJson);

            var ok = save.TryDeserializeValues(out var values);

            Assert.That(ok, Is.True);
            Assert.That(values.ContainsKey("v1"), Is.True);
            Assert.That(values["v1"], Is.InstanceOf<BoolMemberValue>());
            Assert.That(((BoolMemberValue)values["v1"]).value, Is.True);
        }

        [Test]
        public void TryDeserialize_False_KeepsValuesOpaque()
        {
            // A values token the SDK cannot interpret as typed rows (an array, not
            // a map). TryDeserialize must report failure and leave the raw token
            // intact and readable for a later clone/migration.
            var opaque = new NeoSaveValues(JToken.Parse("[1,2,3]"));

            var ok = opaque.TryDeserialize(out var values);

            Assert.That(ok, Is.False);
            Assert.That(values, Is.Empty);
            Assert.That(opaque.Raw.Type, Is.EqualTo(JTokenType.Array));
            Assert.That((int)opaque.Raw[0]!, Is.EqualTo(1));
        }

        [Test]
        public void LocalLoader_TryLoad_False_OnGarbage()
        {
            Assert.That(LocalGameSaveLoader.TryLoad("not json", out _), Is.False);
            Assert.That(LocalGameSaveLoader.TryLoad("", out _), Is.False);
            Assert.That(LocalGameSaveLoader.TryLoad(null, out _), Is.False);
        }

        [Test]
        public void LocalSave_RoundTrips_AndPreservesOpaqueValues()
        {
            var remote = RemoteGameSaveLoader.Load(RemoteJson);
            var local = LocalGameSave.FromRemote(remote);

            var json = LocalGameSaveLoader.Serialize(local);
            var reloaded = LocalGameSaveLoader.Load(json);

            Assert.That(reloaded.customId, Is.EqualTo("save-1"));
            Assert.That(reloaded.serverId, Is.EqualTo("server-1"));
            Assert.That(reloaded.snapshotId, Is.EqualTo("snap-1"));
            Assert.That((bool)reloaded.values.Raw["v1"]!["value"]!, Is.True);
            Assert.That(reloaded.staticBindings["member-current"], Is.EqualTo("v1"));
            Assert.That(reloaded.staticBindings.ContainsKey("member-cleared"), Is.True);
            Assert.That(reloaded.staticBindings["member-cleared"], Is.Null);
            Assert.That(reloaded.IsLocalOnly, Is.False);
        }

        [Test]
        public void LocalSave_FromRemote_CopiesIdentityAndContent()
        {
            var remote = RemoteGameSaveLoader.Load(RemoteJson);

            var local = LocalGameSave.FromRemote(remote);

            Assert.That(local.customId, Is.EqualTo(remote.id));
            Assert.That(local.releaseChannelId, Is.EqualTo("channel-dev"));
            Assert.That(local.name, Is.EqualTo("My Save"));
            Assert.That(local.synchronizedAt, Is.EqualTo(3d));
            Assert.That(local.staticBindings["member-current"], Is.EqualTo("v1"));
        }

        [Test]
        public void SaveModels_MissingStaticBindingsDefaultToEmpty()
        {
            var remote = RemoteGameSaveLoader.Load("{}");
            var local = LocalGameSaveLoader.Load("{}");
            var projectSave = JsonConvert.DeserializeObject<ProjectSaveData>("{}")!;
            var commit = JsonConvert.DeserializeObject<NeoSaveCommitRequest>("{}")!;

            Assert.That(remote.staticBindings, Is.Empty);
            Assert.That(local.staticBindings, Is.Empty);
            Assert.That(projectSave.staticBindings, Is.Empty);
            Assert.That(commit.staticBindings, Is.Empty);
        }

        [Test]
        public void ProjectAndCommitStaticBindings_RoundTripNullTombstones()
        {
            var bindings = new Dictionary<string, string?>
            {
                ["member-current"] = "v-runtime",
                ["member-cleared"] = null,
            };
            var projectSave = new ProjectSaveData { staticBindings = bindings };
            var commit = new NeoSaveCommitRequest { staticBindings = bindings };

            var projectRoundTrip = JsonConvert.DeserializeObject<ProjectSaveData>(
                JsonConvert.SerializeObject(projectSave))!;
            var commitRoundTrip = JsonConvert.DeserializeObject<NeoSaveCommitRequest>(
                JsonConvert.SerializeObject(commit))!;

            Assert.That(projectRoundTrip.staticBindings["member-current"], Is.EqualTo("v-runtime"));
            Assert.That(projectRoundTrip.staticBindings.ContainsKey("member-cleared"), Is.True);
            Assert.That(projectRoundTrip.staticBindings["member-cleared"], Is.Null);
            Assert.That(commitRoundTrip.staticBindings["member-current"], Is.EqualTo("v-runtime"));
            Assert.That(commitRoundTrip.staticBindings.ContainsKey("member-cleared"), Is.True);
            Assert.That(commitRoundTrip.staticBindings["member-cleared"], Is.Null);
        }

        [Test]
        public void LivePatchStaticBindings_RoundTripUpsertsRestoresAndTombstones()
        {
            var patch = new NeoSavePatch
            {
                changes = new List<GameSaveRecordChange>
                {
                    new GameSaveStaticBindingSetChange
                    {
                        memberId = "member-current",
                        valueId = "v-runtime",
                    },
                    new GameSaveStaticBindingSetChange
                    {
                        memberId = "member-cleared",
                        valueId = null,
                    },
                    new GameSaveStaticBindingRestoreToAuthoredChange
                    {
                        memberId = "member-restored",
                        baseRecordStateId = "binding-state-1",
                        baseRecordRevisionToken = "binding-token-1",
                    },
                },
            };

            var roundTripped = JsonConvert.DeserializeObject<NeoSavePatch>(
                JsonConvert.SerializeObject(patch))!;

            var current = (GameSaveStaticBindingSetChange)roundTripped.changes[0];
            Assert.That(current.memberId, Is.EqualTo("member-current"));
            Assert.That(current.valueId, Is.EqualTo("v-runtime"));
            var cleared = (GameSaveStaticBindingSetChange)roundTripped.changes[1];
            Assert.That(cleared.memberId, Is.EqualTo("member-cleared"));
            Assert.That(cleared.valueId, Is.Null,
                "null remains a semantic binding tombstone");
            var restored = (GameSaveStaticBindingRestoreToAuthoredChange)
                roundTripped.changes[2];
            Assert.That(restored.memberId, Is.EqualTo("member-restored"));
            Assert.That(restored.baseRecordStateId, Is.EqualTo("binding-state-1"));
            Assert.That(roundTripped.IsEmpty, Is.False);
        }

        [Test]
        public void LocalSave_IsLocalOnly_WhenNeverSynced()
        {
            var local = new LocalGameSave
            {
                customId = "local-only-1",
                releaseChannelId = "channel-dev",
                name = "Scratch",
                projectId = "project-1",
                values = NeoSaveValues.Empty,
            };

            Assert.That(local.IsLocalOnly, Is.True);
            Assert.That(local.serverId, Is.Null);
        }
    }
}
