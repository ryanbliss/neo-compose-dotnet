// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoSaveSynchronizerTests
    {
        private static async Task<(NeoProjectStore store, FakeApiClient api, NeoInMemoryLocalSaveStore local)>
            ReadyStoreWithCloudAsync()
        {
            var api = new FakeApiClient();
            var local = new NeoInMemoryLocalSaveStore();
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                apiClient: api,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);
            await store.LoadAsync();
            return (store, api, local);
        }

        [Test]
        public async Task Load_WhenCloudUnavailable_DegradesToLocal()
        {
            var (store, api, local) = await ReadyStoreWithCloudAsync();
            await local.CommitSaveAsync("save-1", NeoSaveTestSupport.SaveContent("Local"));
            // Cloud configured but the player is signed out — must not be fatal to a
            // local load (playing a local save never requires sign-in).
            api.getThrows = new NeoComposeNotSignedInException("Not signed in.");

            var sync = store.Open("save-1");
            var content = await sync.LoadSaveContentAsync();

            Assert.That(content, Is.Not.Null, "A signed-out cloud load degrades to the local save.");
            Assert.That(sync.State, Is.EqualTo(NeoSaveSynchronizerState.Ready));
        }

        [Test]
        public async Task Commit_CloudConflict_NoResolver_Throws()
        {
            var (store, api, _) = await ReadyStoreWithCloudAsync();
            api.commitResults.Enqueue(
                NeoCommitResult.Conflict(NeoSaveTestSupport.Remote("save-1", "remote-head", "hr")));

            var sync = store.CreateNew("save-1");

            Assert.ThrowsAsync<NeoSaveConflictUnresolvedException>(
                async () => await sync.CommitSaveContentAsync(
                    NeoSaveTestSupport.SaveContent("Local"), replaceSnapshot: false));
            Assert.That(sync.State, Is.EqualTo(NeoSaveSynchronizerState.Error));
        }

        [Test]
        public async Task Commit_CloudConflict_KeepLocal_WritesNewHeadOnServerHead()
        {
            var (store, api, _) = await ReadyStoreWithCloudAsync();
            api.commitResults.Enqueue(
                NeoCommitResult.Conflict(NeoSaveTestSupport.Remote("save-1", "remote-head", "hr")));
            api.commitResults.Enqueue(
                NeoCommitResult.Committed(NeoSaveTestSupport.Remote("save-1", "new-head", "hn")));

            var sync = store.CreateNew("save-1");
            sync.OnConflict += (_, continuation) => continuation.KeepLocal();

            await sync.CommitSaveContentAsync(NeoSaveTestSupport.SaveContent("Local"), replaceSnapshot: false);

            Assert.That(sync.State, Is.EqualTo(NeoSaveSynchronizerState.Ready));
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("new-head"));
            // The new head is written on top of the server head — no overwrite.
            Assert.That(api.commits, Has.Count.EqualTo(2));
            Assert.That(api.commits[1].request.baseSnapshotId, Is.EqualTo("remote-head"));
        }

        [Test]
        public async Task Commit_CloudConflict_KeepRemote_AdoptsServerHead()
        {
            var (store, api, local) = await ReadyStoreWithCloudAsync();
            api.commitResults.Enqueue(
                NeoCommitResult.Conflict(NeoSaveTestSupport.Remote("save-1", "remote-head", "hr")));

            var sync = store.CreateNew("save-1");
            sync.OnConflict += (_, continuation) => continuation.KeepRemote();

            await sync.CommitSaveContentAsync(NeoSaveTestSupport.SaveContent("Local"), replaceSnapshot: false);

            Assert.That(sync.State, Is.EqualTo(NeoSaveSynchronizerState.Ready));
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("remote-head"));
            Assert.That(api.commits, Has.Count.EqualTo(1), "Keep-remote does not re-commit.");
            // The adopted server head was written to the local store.
            var localContent = await local.LoadSaveAsync("save-1");
            StringAssert.Contains("remote-head", localContent);
        }

        [Test]
        public async Task Commit_BestEffortCloudFailure_KeepsLocalCommit()
        {
            var (store, api, local) = await ReadyStoreWithCloudAsync();
            // Dequeue on an empty queue throws inside CommitAsync → simulates a
            // cloud failure. Default best-effort: the local commit must stand.
            var sync = store.CreateNew("save-1");
            var commitErrors = 0;
            sync.OnCommitError += _ => commitErrors++;

            await sync.CommitSaveContentAsync(NeoSaveTestSupport.SaveContent("Local"), replaceSnapshot: false);

            Assert.That(sync.State, Is.EqualTo(NeoSaveSynchronizerState.Ready));
            Assert.That(commitErrors, Is.EqualTo(1));
            Assert.That(await local.LoadSaveAsync("save-1"), Is.Not.Null);
            Assert.That(store.Saves[0].isLocalOnly, Is.True);
        }

        [Test]
        public async Task Load_MigrationRequired_FiresEvent_AndLoadsResolvedContent()
        {
            var local = new NeoInMemoryLocalSaveStore();
            // values as an array can't deserialize into typed rows → migration.
            await local.CommitSaveAsync("save-1", NeoSaveTestSupport.SaveContent("Old", values: "[1,2,3]"));
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);
            await store.LoadAsync();

            Assert.That(store.Saves[0].needsMigration, Is.True);

            var sync = store.Open("save-1");
            var migrationFired = false;
            sync.OnMigrationRequired += (_, continuation) =>
            {
                migrationFired = true;
                continuation.ResolveWith(NeoSaveTestSupport.SaveContent("Migrated"));
            };

            var content = await sync.LoadSaveContentAsync();

            Assert.That(migrationFired, Is.True);
            StringAssert.Contains("Migrated", content);
            Assert.That(sync.State, Is.EqualTo(NeoSaveSynchronizerState.Ready));
        }

        [Test]
        public async Task Load_CrossChannelSave_FiresCloneEvent_AndApproveLoadsClone()
        {
            var (store, api, _) = await ReadyStoreWithCloudAsync();
            // The cloud save is bound to a different channel than the target.
            api.getResult = NeoSaveTestSupport.Remote("save-1", "snap-1", "h1", channel: "channel-prod");
            api.cloneResult = NeoCloneResult.Cloned(
                NeoSaveTestSupport.Remote(
                    "save-2",
                    "snap-2",
                    "h2",
                    channel: NeoSaveTestSupport.TargetChannel));

            var sync = store.Open("save-1");
            var cloneFired = false;
            sync.OnSelectedSaveRequiringClone += (_, continuation) =>
            {
                cloneFired = true;
                continuation.Approve("My Clone");
            };

            var content = await sync.LoadSaveContentAsync();

            Assert.That(cloneFired, Is.True);
            Assert.That(sync.CustomId, Is.EqualTo("save-2"), "Loading switches to the clone's id.");
            StringAssert.Contains("save-2", content);
        }

        [Test]
        public async Task Load_CrossChannelTransitioningClone_PollsOnlyTheReturnedDestination()
        {
            var (store, api, _) = await ReadyStoreWithCloudAsync();
            api.getResult = NeoSaveTestSupport.Remote(
                "save-1", "snap-1", "h1", channel: "channel-prod");
            api.cloneResult = NeoCloneResult.Transitioning("save-2", "snap-2");
            api.transitionStatuses.Enqueue(
                NeoSaveTransitionStatus.Copying("save-2", "snap-2"));
            api.transitionStatuses.Enqueue(NeoSaveTransitionStatus.Ready(
                NeoSaveTestSupport.Remote(
                    "save-2",
                    "snap-2",
                    "h2",
                    channel: NeoSaveTestSupport.TargetChannel)));

            var sync = store.Open("save-1");
            sync.OnSelectedSaveRequiringClone += (_, continuation) =>
                continuation.Approve("My Clone");

            var content = await sync.LoadSaveContentAsync();

            Assert.That(api.cloneRequests, Is.EqualTo(new[] { "save-1" }),
                "the accepted clone mutation must never be repeated");
            Assert.That(
                api.transitionStatusRequests,
                Is.EqualTo(new[] { "save-2", "save-2" }));
            Assert.That(sync.CustomId, Is.EqualTo("save-2"));
            StringAssert.Contains("save-2", content);
        }

        [Test]
        public async Task Load_CrossChannelSave_DenyClone_IsNoOp()
        {
            var (store, api, _) = await ReadyStoreWithCloudAsync();
            api.getResult = NeoSaveTestSupport.Remote("save-1", "snap-1", "h1", channel: "channel-prod");

            var sync = store.Open("save-1");
            sync.OnSelectedSaveRequiringClone += (_, continuation) => continuation.Deny();

            var content = await sync.LoadSaveContentAsync();

            Assert.That(content, Is.Null);
            Assert.That(sync.CustomId, Is.EqualTo("save-1"));
        }

        [Test]
        public async Task Archive_RemovesLocal_AndFiresEvent()
        {
            var (store, api, local) = await ReadyStoreWithCloudAsync();
            await local.CommitSaveAsync("save-1", NeoSaveTestSupport.SaveContent("Doomed"));

            var sync = store.Open("save-1");
            var archivedId = "";
            sync.OnSaveArchived += id => archivedId = id;

            await sync.ArchiveAsync();

            Assert.That(api.archivedSaves, Does.Contain("save-1"));
            Assert.That(await local.LoadSaveAsync("save-1"), Is.Null);
            Assert.That(archivedId, Is.EqualTo("save-1"));
        }
    }
}
