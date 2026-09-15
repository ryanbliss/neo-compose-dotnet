// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Threading.Tasks;
using NeoCompose.Runtime.Json;
using NeoCompose.Unity.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class NeoComposeEditorTests
    {
        [Test]
        public async Task Synchronizer_RebuildsLegacyCacheOnceToRepairAlreadyCorruptedExport()
        {
            var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString());
            try
            {
                var state = new NeoComposeUnityExportSyncState();
                var assets = new FakeAssetService();
                const string path = "Assets/Resources/Neo/project.json";
                var project = PackedSyncProject(true);
                assets.files[path] = project.ToString(Formatting.None);
                StampCachedExport(assets, state);
                var valid = assets.files[path];
                project = JObject.Parse(valid);
                PackedSyncRows(project, true)["child"] =
                    JObject.Parse("{'id':'child','value':'stale','createdAt':0,'updatedAt':0}");
                assets.files[path] = project.ToString(Formatting.None);
                assets.files["Assets/Scripts/Neo/Generated/Project.g.cs"] = "// existing";
                SeedGeneratedFiles(assets);
                string fileName;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                {
                    fileName = System.BitConverter.ToString(sha.ComputeHash(
                        System.Text.Encoding.UTF8.GetBytes("project-1\nversion-1"))).Replace("-", "").ToLowerInvariant() + ".json";
                }
                var legacyDirectory = System.IO.Path.Combine(directory, "Library", "NeoCompose", "ExportCache");
                System.IO.Directory.CreateDirectory(legacyDirectory);
                System.IO.File.WriteAllText(System.IO.Path.Combine(legacyDirectory, fileName), JsonConvert.SerializeObject(state));
                var api = new FakeApiClient();
                api.exportResponse.projectJson = valid;
                api.exportResponse.syncState = state;
                var cache = new NeoComposeEditorExportCache(() => directory);
                var sync = new NeoComposeSynchronizer(api, new FakeConfirmationService(true), assets, cache);

                var first = await sync.SynchronizeAsync(MakeConfig());

                Assert.IsTrue(first.success, first.message);
                Assert.AreEqual(1, api.fullExportCalls);
                Assert.AreEqual(0, api.deltaExportCalls);
                Assert.DoesNotThrow(() => JsonConvert.DeserializeObject<ProjectData>(assets.files[path]));
                Assert.IsNotNull(cache.Load("project-1", "version-1"));

                var second = await sync.SynchronizeAsync(MakeConfig());

                Assert.IsTrue(second.success, second.message);
                Assert.AreEqual(1, api.fullExportCalls, "Repair must not disable future incremental synchronization.");
                Assert.AreEqual(1, api.deltaExportCalls);
            }
            finally
            {
                if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, true);
            }
        }

        [TestCase(false, false, false)]
        [TestCase(false, false, true)]
        [TestCase(false, true, false)]
        [TestCase(false, true, true)]
        [TestCase(true, false, false)]
        [TestCase(true, false, true)]
        [TestCase(true, true, false)]
        [TestCase(true, true, true)]
        public async Task Synchronizer_RequiresMatchingFileRevisionForPackedDelta(
            bool matchingRevision, bool changed, bool partitioned)
        {
            const string path = "Assets/Resources/Neo/project.json";
            var api = new FakeApiClient();
            var assets = new FakeAssetService();
            var state = new NeoComposeUnityExportSyncState();
            state.heads.Add(new NeoComposeUnityExportHeadDescriptor
            {
                recordKind = "value", recordId = "parent", snapshotId = "packed-v1", contentHash = "packed-v1-hash",
            });
            // The cursor already consumed the child's removal when it was packed.
            // A Git revert restores an older file where that child is still separate.
            var local = PackedSyncProject(partitioned);
            if (!matchingRevision)
            {
                var rows = PackedSyncRows(local, partitioned);
                rows["parent"]!["value"]!["Name"] = "child";
                rows["child"] = JObject.Parse("{'id':'child','value':'stale','createdAt':0,'updatedAt':0}");
            }
            assets.files[path] = local.ToString(Formatting.None);
            StampCachedExport(assets, state);
            if (!matchingRevision)
            {
                local = JObject.Parse(assets.files[path]);
                local["metadata"]!["projectDocumentContentHash"] = "older-sparse-revision";
                assets.files[path] = local.ToString(Formatting.None);
            }
            assets.files["Assets/Scripts/Neo/Generated/Project.g.cs"] = "// existing";
            SeedGeneratedFiles(assets);
            var latest = PackedSyncProject(partitioned);
            if (changed)
            {
                var parent = PackedSyncRows(latest, partitioned)["parent"]!;
                parent["value"]!["Name"]!["~packed"]!["value"] = "updated";
                api.deltaResponse.records.Add(new NeoComposeUnityExportHeadDescriptor
                {
                    recordKind = "value", recordId = "parent", snapshotId = "packed-v2",
                });
                api.snapshotResponse.snapshots.Add(new NeoComposeUnityExportCachedSnapshot
                {
                    id = "packed-v2", recordKind = "value", recordId = "parent",
                    contentHash = "packed-v2-hash", data = parent.DeepClone(),
                });
            }
            api.exportResponse.projectJson = latest.ToString(Formatting.None);
            var cache = new FakeExportCache { state = state };
            var result = await new NeoComposeSynchronizer(
                api, new FakeConfirmationService(true), assets, cache).SynchronizeAsync(MakeConfig());

            Assert.IsTrue(result.success, result.message);
            Assert.AreEqual(matchingRevision ? 0 : 1, api.fullExportCalls);
            Assert.AreEqual(matchingRevision ? 1 : 0, api.deltaExportCalls);
            var written = JObject.Parse(assets.files[path]);
            Assert.IsNull(PackedSyncRows(written, partitioned)["child"], "The former sparse row must not survive beside its packed replacement.");
            Assert.DoesNotThrow(() => JsonConvert.DeserializeObject<ProjectData>(assets.files[path]));
            Assert.AreEqual(changed ? "updated" : "current",
                PackedSyncRows(written, partitioned)["parent"]!["value"]!["Name"]!["~packed"]!["value"]!.Value<string>());
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PackedExport_RejectsConflictingStoredChild(bool partitioned)
        {
            var project = PackedSyncProject(partitioned);
            PackedSyncRows(project, partitioned)["child"] =
                JObject.Parse("{'id':'child','value':'stale','createdAt':0,'updatedAt':0}");
            Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<ProjectData>(project.ToString()));
        }

        private static JObject PackedSyncProject(bool partitioned)
        {
            var root = JObject.Parse(ProjectJsonWithFiles(""));
            var parent = JObject.Parse(@"{
                'id':'parent', 'classId':'parent-class', 'createdAt':0, 'updatedAt':0,
                'value':{'Name':{'~packed':{'id':'child','value':'current','createdAt':0,'updatedAt':0}}}
            }");
            var rows = new JObject { ["parent"] = parent };
            if (partitioned)
            {
                parent["mapKey"] = "world:test";
                root["valuePartitions"] = new JObject { ["world:test"] = rows };
            }
            else root["values"] = rows;
            return root;
        }

        private static JObject PackedSyncRows(JObject project, bool partitioned) =>
            (JObject)(partitioned ? project["valuePartitions"]!["world:test"]! : project["values"]!);
    }
}
