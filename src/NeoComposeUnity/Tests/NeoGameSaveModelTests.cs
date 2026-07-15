// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;
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
            "\"snapshotHash\":\"hash-1\"," +
            "\"releaseChannelId\":\"channel-dev\"," +
            "\"snapshotName\":\"Auto 1\"," +
            "\"name\":\"My Save\"," +
            "\"projectId\":\"project-1\"," +
            "\"version\":{\"id\":\"v1\",\"label\":\"1.0\"}," +
            "\"author\":{\"kind\":\"user\",\"id\":\"user-1\"}," +
            "\"actor\":{\"kind\":\"user\",\"id\":\"user-1\"}," +
            "\"values\":{\"v1\":{\"id\":\"v1\",\"value\":true,\"createdAt\":1,\"updatedAt\":2}}," +
            "\"staticBindings\":{\"attr-current\":\"v1\",\"attr-cleared\":null}," +
            "\"platforms\":null,\"systems\":null,\"inputDevices\":null," +
            "\"createdAt\":1,\"updatedAt\":2,\"synchronizedAt\":3,\"archivedAt\":null" +
            "}";

        [Test]
        public void RemoteLoader_LoadsEnvelope_AndKeepsValuesOpaque()
        {
            var save = RemoteGameSaveLoader.Load(RemoteJson);

            Assert.That(save.id, Is.EqualTo("save-1"));
            Assert.That(save.serverId, Is.EqualTo("server-1"));
            Assert.That(save.snapshotHash, Is.EqualTo("hash-1"));
            Assert.That(save.releaseChannelId, Is.EqualTo("channel-dev"));
            Assert.That(save.author.id, Is.EqualTo("user-1"));
            // values stayed opaque: the raw token is preserved, not pre-typed.
            Assert.That(save.values.Raw.Type, Is.EqualTo(JTokenType.Object));
            Assert.That((bool)save.values.Raw["v1"]!["value"]!, Is.True);
            Assert.That(save.staticBindings["attr-current"], Is.EqualTo("v1"));
            Assert.That(save.staticBindings.ContainsKey("attr-cleared"), Is.True);
            Assert.That(save.staticBindings["attr-cleared"], Is.Null);
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
            Assert.That(values["v1"], Is.InstanceOf<BoolAttributeValue>());
            Assert.That(((BoolAttributeValue)values["v1"]).value, Is.True);
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
            Assert.That(reloaded.snapshotHash, Is.EqualTo("hash-1"));
            Assert.That((bool)reloaded.values.Raw["v1"]!["value"]!, Is.True);
            Assert.That(reloaded.staticBindings["attr-current"], Is.EqualTo("v1"));
            Assert.That(reloaded.staticBindings.ContainsKey("attr-cleared"), Is.True);
            Assert.That(reloaded.staticBindings["attr-cleared"], Is.Null);
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
            Assert.That(local.staticBindings["attr-current"], Is.EqualTo("v1"));
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
                ["attr-current"] = "v-runtime",
                ["attr-cleared"] = null,
            };
            var projectSave = new ProjectSaveData { staticBindings = bindings };
            var commit = new NeoSaveCommitRequest { staticBindings = bindings };

            var projectRoundTrip = JsonConvert.DeserializeObject<ProjectSaveData>(
                JsonConvert.SerializeObject(projectSave))!;
            var commitRoundTrip = JsonConvert.DeserializeObject<NeoSaveCommitRequest>(
                JsonConvert.SerializeObject(commit))!;

            Assert.That(projectRoundTrip.staticBindings["attr-current"], Is.EqualTo("v-runtime"));
            Assert.That(projectRoundTrip.staticBindings.ContainsKey("attr-cleared"), Is.True);
            Assert.That(projectRoundTrip.staticBindings["attr-cleared"], Is.Null);
            Assert.That(commitRoundTrip.staticBindings["attr-current"], Is.EqualTo("v-runtime"));
            Assert.That(commitRoundTrip.staticBindings.ContainsKey("attr-cleared"), Is.True);
            Assert.That(commitRoundTrip.staticBindings["attr-cleared"], Is.Null);
        }

        [Test]
        public void LivePatchStaticBindings_RoundTripUpsertsRestoresAndTombstones()
        {
            var patch = new NeoSavePatch
            {
                staticBindings = new Dictionary<string, string?>
                {
                    ["attr-current"] = "v-runtime",
                    ["attr-cleared"] = null,
                },
                restoredStaticBindingsToAuthored = new List<string>
                {
                    "attr-restored",
                },
            };

            var roundTripped = JsonConvert.DeserializeObject<NeoSavePatch>(
                JsonConvert.SerializeObject(patch))!;

            Assert.That(roundTripped.staticBindings["attr-current"], Is.EqualTo("v-runtime"));
            Assert.That(roundTripped.staticBindings.ContainsKey("attr-cleared"), Is.True);
            Assert.That(roundTripped.staticBindings["attr-cleared"], Is.Null);
            CollectionAssert.AreEqual(
                new[] { "attr-restored" },
                roundTripped.restoredStaticBindingsToAuthored);
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
