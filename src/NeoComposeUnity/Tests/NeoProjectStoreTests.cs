// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeoCompose.Tests
{
    public class NeoProjectStoreTests
    {
        private sealed class EmptyTokenStore : INeoComposeTokenStore
        {
            public NeoComposeStoredToken? Load() => null;
            public void Save(NeoComposeStoredToken token)
            {
            }
            public void Clear()
            {
            }
            public NeoComposeTokenHint? PeekHint() => null;
        }

        [Test]
        public void DisposedStoreDoesNotPublishALateProjectLoad()
        {
            var source = new ControllableProjectDataSource();
            var store = new NeoProjectStore(dataSource: source,
                localStore: new NeoInMemoryLocalSaveStore());
            var loading = store.LoadAsync();
            store.Dispose();
            source.Complete(NeoSaveTestSupport.ProjectJson);
            Assert.ThrowsAsync<System.ObjectDisposedException>(async () => await loading);
            Assert.That(store.Schema, Is.Null);
            Assert.That(store.State, Is.EqualTo(NeoProjectStoreState.Errored));
        }

        [Test]
        public async Task LoadAsync_ReadsOnlyProjectJsonAndTheMainPartition()
        {
            var corpus = JObject.Parse(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/synth-example.json"));
            corpus["valuePartitions"] = new JObject { ["world:grid"] = new JObject() };
            var source = new ControllableProjectDataSource();
            using var store = new NeoProjectStore(
                dataSource: source,
                localStore: new NeoInMemoryLocalSaveStore());
            var loading = store.LoadAsync();
            source.Complete(corpus.ToString());
            await loading;

            CollectionAssert.AreEqual(new[] { NeoProjectExportContract.MainPartitionFile }, source.partitionReads);

            using NeoClient client = NeoTestSaveStack.LoadSynchronously(store.Open("save-1"));
            Assert.AreEqual(1, source.partitionReads.Count, "Opening a save reads no named partition.");
            client.LoadValuePartition("world:grid");
            Assert.AreEqual(2, source.partitionReads.Count);
        }

        [Test]
        public async Task LoadAsync_GoesLoadingThenReady_AndGatesOpenUntilReady()
        {
            var source = new ControllableProjectDataSource();
            var store = new NeoProjectStore(
                dataSource: source,
                localStore: new NeoInMemoryLocalSaveStore(),
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);

            Assert.That(store.State, Is.EqualTo(NeoProjectStoreState.Idle));
            Assert.Throws<System.InvalidOperationException>(() => store.Open("save-1"));

            var loadTask = store.LoadAsync();
            // The async source has not completed yet: the store is mid-load and
            // still rejects Open.
            Assert.That(store.State, Is.EqualTo(NeoProjectStoreState.Loading));
            Assert.Throws<System.InvalidOperationException>(() => store.Open("save-1"));

            source.Complete(NeoSaveTestSupport.ProjectJson);
            await loadTask;

            Assert.That(store.State, Is.EqualTo(NeoProjectStoreState.Ready));
            Assert.That(store.Schema, Is.Not.Null);
            Assert.DoesNotThrow(() => store.Open("save-1"));
        }

        [Test]
        public async Task CreateNew_IsLocalOnlyUntilCommit_ThenListsTheSave()
        {
            var local = new NeoInMemoryLocalSaveStore();
            var store = new NeoProjectStore(
                dataSource: NeoTestExport.Source(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);
            await store.LoadAsync();

            Assert.That(store.LocalStore, Is.SameAs(local));

            var listChanges = 0;
            store.OnListChanged += () => listChanges++;

            var sync = store.CreateNew("save-1", "My Save");

            // Ready immediately, nothing persisted, nothing listed.
            Assert.That(sync.State, Is.EqualTo(NeoSaveSynchronizerState.Ready));
            Assert.That(store.Saves, Is.Empty);
            Assert.That(await local.LoadSaveAsync("save-1"), Is.Null);
            Assert.That(await sync.LoadSaveContentAsync(), Is.Null, "A new draft has nothing to load.");

            await sync.CommitSaveContentAsync(NeoSaveTestSupport.SaveContent("My Save"), replaceSnapshot: false);

            // First commit persists locally and surfaces in the list.
            Assert.That(await local.LoadSaveAsync("save-1"), Is.Not.Null);
            Assert.That(store.Saves, Has.Count.EqualTo(1));
            Assert.That(store.Saves[0].customId, Is.EqualTo("save-1"));
            Assert.That(store.Saves[0].isLocalOnly, Is.True);
            Assert.That(listChanges, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public async Task ReusedJsonDataSource_ReusesParsedProjectSchemaAcrossStores()
        {
            var source = NeoTestExport.Source(NeoSaveTestSupport.ProjectJson);
            var first = new NeoProjectStore(
                dataSource: source,
                localStore: new NeoInMemoryLocalSaveStore());
            var second = new NeoProjectStore(
                dataSource: source,
                localStore: new NeoInMemoryLocalSaveStore());

            try
            {
                await first.LoadAsync();
                await second.LoadAsync();

                Assert.AreSame(
                    first.Schema,
                    second.Schema,
                    "One immutable JSON data source should deserialize its project schema only once.");
            }
            finally
            {
                second.Dispose();
                first.Dispose();
            }
        }

        [Test]
        public async Task Commit_ThroughSynchronizer_KeepsListInSync()
        {
            var local = new NeoInMemoryLocalSaveStore();
            await local.CommitSaveAsync("save-1", NeoSaveTestSupport.SaveContent("Original"));
            var store = new NeoProjectStore(
                dataSource: NeoTestExport.Source(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);
            await store.LoadAsync();

            Assert.That(store.Saves[0].name, Is.EqualTo("Original"));

            var sync = store.Open("save-1");
            await sync.CommitSaveContentAsync(NeoSaveTestSupport.SaveContent("Renamed"), replaceSnapshot: false);

            // The list the project store exposes reflects the active-file commit.
            Assert.That(store.Saves, Has.Count.EqualTo(1));
            Assert.That(store.Saves[0].name, Is.EqualTo("Renamed"));
        }

        [Test]
        public async Task RefreshSaves_DowngradesCloudDeletedSaveToLocalOnly_AndDeleteSkipsCloud()
        {
            var api = new FakeApiClient(); // cloud list is empty (the save was deleted server-side)
            var local = new NeoInMemoryLocalSaveStore();
            // A previously-synced local save (serverId set) whose cloud copy is gone.
            await local.CommitSaveAsync("save-1", NeoSaveTestSupport.SyncedSaveContent("Orphan"));
            var store = new NeoProjectStore(
                dataSource: NeoTestExport.Source(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                apiClient: api,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);

            await store.LoadAsync();

            // Reconciled against the cloud list: no longer reads as "synced".
            Assert.That(store.Saves, Has.Count.EqualTo(1));
            Assert.That(store.Saves[0].isLocalOnly, Is.True);
            Assert.That(store.Saves[0].existsRemotely, Is.False);

            // Deleting the orphan must not call the cloud archive (it would 404) and
            // must still remove the local file.
            await store.ArchiveAsync("save-1");
            Assert.That(api.archivedSaves, Is.Empty);
            Assert.That(await local.LoadSaveAsync("save-1"), Is.Null);
        }

        [Test]
        public async Task ArchiveSave_ToleratesCloudNotFound_AndStillDeletesLocal()
        {
            var api = new FakeApiClient
            {
                // The cloud still lists the save (so it reads as synced) ...
                list = new NeoSaveFileList
                {
                    saves = { NeoSaveTestSupport.Summary("save-1", "snap-1") },
                },
                // ... but it is deleted before the archive lands — the archive 404s.
                archiveThrows = new NeoComposeNotFoundException("save was not found"),
            };
            var local = new NeoInMemoryLocalSaveStore();
            await local.CommitSaveAsync("save-1", NeoSaveTestSupport.SyncedSaveContent("Racing"));
            var store = new NeoProjectStore(
                dataSource: NeoTestExport.Source(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                apiClient: api,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);
            await store.LoadAsync();
            Assert.That(store.Saves[0].existsRemotely, Is.True);

            // The cloud archive 404s (already gone) — tolerated; local still deleted.
            await store.ArchiveAsync("save-1");
            Assert.That(api.archivedSaves, Does.Contain("save-1"));
            Assert.That(await local.LoadSaveAsync("save-1"), Is.Null);
        }

        [Test]
        public async Task ArchiveSave_WhenSignedOut_DeletesOnlyLocalCopy()
        {
            var api = new FakeApiClient
            {
                list = new NeoSaveFileList
                {
                    saves = { NeoSaveTestSupport.Summary("save-1", "snap-1") },
                },
                archiveThrows = new NeoComposeNotSignedInException("signed out"),
            };
            var local = new NeoInMemoryLocalSaveStore();
            await local.CommitSaveAsync(
                "save-1",
                NeoSaveTestSupport.SyncedSaveContent("Cloud Save"));
            var authentication = new NeoAuthentication(
                new NeoAuthenticationOptions(
                    "https://example.test",
                    "project-1",
                    "runtime-client",
                    "project:project-1:save:write"),
                new EmptyTokenStore());
            var store = new NeoProjectStore(
                dataSource: NeoTestExport.Source(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                apiClient: api,
                authentication: authentication,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);
            await store.LoadAsync();

            Assert.That(authentication.IsSignedIn, Is.False);
            Assert.That(store.Saves[0].existsRemotely, Is.True);

            await store.ArchiveAsync("save-1");

            Assert.That(
                api.archivedSaves,
                Is.Empty,
                "A signed-out delete must not call the authenticated cloud archive.");
            Assert.That(await local.LoadSaveAsync("save-1"), Is.Null);
            Assert.That(store.Saves[0].IsArchived, Is.True);
        }

        [Test]
        public async Task LoadAsync_OfALocalExport_KeepsSavesInTheirOwnFolderAndNeverReachesTheServer()
        {
            var api = new FakeApiClient();
            var passed = new NeoInMemoryLocalSaveStore();
            var realtime = new FakeRealtimeProvider();
            var authentication = new NeoAuthentication(
                new NeoAuthenticationOptions(
                    "https://example.test",
                    "project-1",
                    "runtime-client",
                    "project:project-1:save:write"),
                new EmptyTokenStore());
            var store = new NeoProjectStore(
                dataSource: NeoTestExport.Source(NeoSaveTestSupport.ProjectJson.Replace(
                    "\"schemaVersion\"",
                    "\"localExport\":true,\"schemaVersion\"")),
                localStore: passed,
                apiClient: api,
                authentication: authentication,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel,
                realtimeProvider: realtime);
            LogAssert.Expect(
                LogType.Warning,
                new Regex("local export.*Ignoring the local save store, cloud saves, sign-in, realtime\\."));

            await store.LoadAsync();

            Assert.That(store.Schema!.metadata!.localExport, Is.True);
            Assert.That(store.Authentication, Is.Null);
            Assert.That(store.RealtimeProvider, Is.Null);
            Assert.That(realtime.DisposeCalls, Is.EqualTo(1));
            Assert.That(realtime.ConnectCalls, Is.EqualTo(0));
            Assert.That(api.listCalls, Is.EqualTo(0));

            string customId = "local-export-" + System.Guid.NewGuid().ToString("N");
            var folder = new NeoFileLocalSaveStore(
                Path.Combine(Application.persistentDataPath, "NeoCompose", "LocalExport"));
            try
            {
                await store.CreateNew(customId, "Local")
                    .CommitSaveContentAsync(NeoSaveTestSupport.SaveContent("Local"), replaceSnapshot: false);

                Assert.That(await folder.LoadSaveAsync(customId), Is.Not.Null);
                Assert.That(await passed.ListSaveIdsAsync(), Is.Empty);
                Assert.That(api.commits, Is.Empty);
            }
            finally
            {
                await folder.DeleteSaveAsync(customId);
            }
        }

        [Test]
        public async Task LocalExport_RecreatedStoresReadTheSameSaveAndPreserveRejectedBytes()
        {
            string customId = "local-export-" + System.Guid.NewGuid().ToString("N");
            string projectJson = NeoSaveTestSupport.ProjectJson.Replace(
                "\"schemaVersion\"", "\"localExport\":true,\"schemaVersion\"");
            var folder = new NeoFileLocalSaveStore(
                Path.Combine(Application.persistentDataPath, "NeoCompose", "LocalExport"));
            try
            {
                string? persisted = null;
                for (int session = 0; session < 3; session++)
                {
                    // Reparse identical export bytes with a new source and byte store.
                    var passed = new NeoInMemoryLocalSaveStore();
                    using var store = new NeoProjectStore(
                        dataSource: NeoTestExport.Source(projectJson), localStore: passed,
                        targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);
                    LogAssert.Expect(LogType.Warning, new Regex("local export.*Ignoring the local save store\\."));
                    Assert.Throws<System.InvalidOperationException>(() => _ = store.LocalStore);
                    await store.LoadAsync();
                    Assert.That(((NeoFileLocalSaveStore)store.LocalStore).DirectoryPath,
                        Is.EqualTo(folder.DirectoryPath));
                    if (session == 0)
                    {
                        var content = JObject.Parse(NeoSaveTestSupport.SaveContent("Across sessions"));
                        content["customId"] = customId;
                        await store.CreateNew(customId, "Across sessions").CommitSaveContentAsync(
                            content.ToString(), replaceSnapshot: false);
                        persisted = await folder.LoadSaveAsync(customId);
                    }
                    Assert.That(await passed.LoadSaveAsync(customId), Is.Null,
                        "The constructor store is not the local-export store.");
                    Assert.That(await store.LocalStore.LoadSaveAsync(customId), Is.EqualTo(persisted));
                    Assert.That(store.Saves, Has.Some.Matches<NeoSaveListEntry>(save => save.customId == customId));
                    var sync = store.Open(customId);
                    var loaded = LocalGameSaveLoader.Load((await sync.LoadSaveContentAsync())!);
                    Assert.That(loaded.customId, Is.EqualTo(customId));
                    Assert.That(loaded.name, Is.EqualTo("Across sessions"));
                    Assert.That(sync.State, Is.EqualTo(NeoSaveSynchronizerState.Ready));
                    Assert.That(await folder.LoadSaveAsync(customId), Is.EqualTo(persisted));
                }

                using var reopened = new NeoProjectStore(dataSource: NeoTestExport.Source(projectJson));
                LogAssert.Expect(LogType.Warning, new Regex("local export"));
                await reopened.LoadAsync();
                var incompatible = JObject.Parse(persisted!);
                incompatible["requiredSaveFormatRevision"] = NeoSaveFormat.SupportedRevision + 1;
                string rejected = incompatible.ToString();
                await folder.CommitSaveAsync(customId, rejected);
                var rejectedSync = reopened.Open(customId);
                var error = Assert.ThrowsAsync<NeoUnsupportedSaveFormatException>(
                    async () => await rejectedSync.LoadSaveContentAsync());
                Assert.That(error!.Message, Does.Contain("requires format revision"));
                Assert.That(await folder.LoadSaveAsync(customId), Is.EqualTo(rejected));

                incompatible.Remove("requiredSaveFormatRevision");
                incompatible["values"] = new JArray(1, 2, 3);
                rejected = incompatible.ToString();
                await folder.CommitSaveAsync(customId, rejected);
                var migrationSync = reopened.Open(customId);
                bool migrationRequested = false;
                migrationSync.OnMigrationRequired += (_, continuation) =>
                {
                    migrationRequested = true;
                    continuation.Skip();
                };
                Assert.That(await migrationSync.LoadSaveContentAsync(), Is.Null);
                Assert.That(migrationRequested, Is.True);
                Assert.That(migrationSync.State, Is.EqualTo(NeoSaveSynchronizerState.Idle));
                Assert.That(await folder.LoadSaveAsync(customId), Is.EqualTo(rejected));
            }
            finally
            {
                await folder.DeleteSaveAsync(customId);
            }
        }

        [Test]
        public void Open_BeforeLoad_Throws()
        {
            var store = new NeoProjectStore(
                dataSource: NeoTestExport.Source(NeoSaveTestSupport.ProjectJson),
                localStore: new NeoInMemoryLocalSaveStore());

            Assert.Throws<System.InvalidOperationException>(() => store.Open("save-1"));
            Assert.Throws<System.InvalidOperationException>(() => store.CreateNew());
        }
    }
}
