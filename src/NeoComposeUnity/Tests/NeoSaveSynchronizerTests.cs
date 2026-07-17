// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

        private static string LargeValuesJson(int count)
        {
            var values = new JObject();
            for (int index = 0; index < count; index++)
            {
                string id = $"value-{index}";
                values[id] = new JObject
                {
                    ["id"] = id,
                    ["value"] = index,
                };
            }
            return values.ToString(Formatting.None);
        }

        private static RemoteGameSave CompletedLargeSave(string valuesJson)
        {
            var save = NeoSaveTestSupport.Remote(
                "save-1", "snap-large", snapshotRevision: 2);
            save.values = new NeoSaveValues(JObject.Parse(valuesJson));
            return save;
        }

        private static RemoteGameSave MaterializedRemote(
            string snapshotId,
            long snapshotRevision,
            string valuesJson,
            string? liveSessionId = null)
        {
            var save = NeoSaveTestSupport.Remote(
                "save-1", snapshotId, snapshotRevision);
            save.values = new NeoSaveValues(JObject.Parse(valuesJson));
            save.liveSessionId = liveSessionId;
            save.recordCache.snapshotId = snapshotId;
            save.recordCache.snapshotRevision = snapshotRevision;
            foreach (var property in ((JObject)save.values.Raw).Properties())
            {
                var descriptor = new GameSaveRecordDescriptor
                {
                    recordKind = NeoGameSaveRecordKinds.Value,
                    recordId = property.Name,
                    recordStateId = $"{snapshotId}:{property.Name}:{snapshotRevision}",
                    recordRevisionToken = $"token:{snapshotRevision}:{property.Name}",
                    contentHash = $"hash:{snapshotRevision}:{property.Name}",
                };
                save.recordCache.descriptors[descriptor.LogicalKey] = descriptor;
            }
            return save;
        }

        private static async Task<(NeoProjectStore store, NeoSaveSynchronizer sync,
            FakeApiClient api)> LoadedExistingSaveAsync(RemoteGameSave remote)
        {
            var api = new FakeApiClient { getResult = remote };
            api.list.saves.Add(RemoteGameSaveSummary.FromRemote(remote));
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: new NeoInMemoryLocalSaveStore(),
                apiClient: api,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel);
            await store.LoadAsync();
            var sync = store.Open("save-1");
            await sync.LoadSaveContentAsync();
            return (store, sync, api);
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
                NeoCommitResult.Conflict(NeoSaveTestSupport.Remote("save-1", "remote-head")));

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
                NeoCommitResult.Conflict(NeoSaveTestSupport.Remote("save-1", "remote-head")));
            api.commitResults.Enqueue(
                NeoCommitResult.Committed(NeoSaveTestSupport.Remote("save-1", "new-head")));

            var sync = store.CreateNew("save-1");
            sync.OnConflict += (_, continuation) => continuation.KeepLocal();

            await sync.CommitSaveContentAsync(NeoSaveTestSupport.SaveContent("Local"), replaceSnapshot: false);

            Assert.That(sync.State, Is.EqualTo(NeoSaveSynchronizerState.Ready));
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("new-head"));
            // The new head is written on top of the server head — no overwrite.
            Assert.That(api.commits, Has.Count.EqualTo(1));
            Assert.That(api.sparseCommits, Has.Count.EqualTo(1));
            Assert.That(
                api.sparseCommits[0].request.baseSnapshotId,
                Is.EqualTo("remote-head"));
        }

        [Test]
        public async Task Commit_CloudConflict_KeepRemote_AdoptsServerHead()
        {
            var (store, api, local) = await ReadyStoreWithCloudAsync();
            api.commitResults.Enqueue(
                NeoCommitResult.Conflict(NeoSaveTestSupport.Remote("save-1", "remote-head")));

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
        public async Task Commit_RealtimeMetadataOnlySuccess_HydratesRecordContent()
        {
            var api = new FakeApiClient();
            var local = new NeoInMemoryLocalSaveStore();
            var realtime = new FakeRealtimeProvider
            {
                State = NeoRealtimeConnectionState.Connected,
                canCommit = true,
            };
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                apiClient: api,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel,
                options: new NeoSaveOptions { LiveSessionsEnabled = false },
                realtimeProvider: realtime);
            await store.LoadAsync();

            api.SetValueManifest(
                "new-head", 2, "{\"a\":{\"id\":\"a\",\"value\":1}}");
            realtime.commitResults.Enqueue(NeoCommitResult.Committed(
                NeoSaveTestSupport.Remote("save-1", "new-head", snapshotRevision: 2)));

            var sync = store.CreateNew("save-1");
            await sync.CommitSaveContentAsync(
                NeoSaveTestSupport.SaveContent(
                    "Local", "{\"a\":{\"id\":\"a\",\"value\":1}}"),
                replaceSnapshot: false);

            var values = (JObject)sync.ActiveSave!.values.Raw;
            Assert.That(values["a"]?["value"]?.Value<int>(), Is.EqualTo(1));
            Assert.That(sync.ActiveSave.recordCache.descriptors, Has.Count.EqualTo(1));
            Assert.That(await local.LoadSaveAsync("save-1"), Does.Contain("\"a\""));
        }

        [Test]
        public async Task Commit_RealtimeMetadataOnlyConflict_HydratesBeforeKeepRemote()
        {
            var api = new FakeApiClient();
            var local = new NeoInMemoryLocalSaveStore();
            var realtime = new FakeRealtimeProvider
            {
                State = NeoRealtimeConnectionState.Connected,
                canCommit = true,
            };
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                apiClient: api,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel,
                options: new NeoSaveOptions { LiveSessionsEnabled = false },
                realtimeProvider: realtime);
            await store.LoadAsync();

            api.SetValueManifest(
                "remote-head", 3, "{\"remote\":{\"id\":\"remote\",\"value\":9}}");
            realtime.commitResults.Enqueue(NeoCommitResult.Conflict(
                NeoSaveTestSupport.Remote("save-1", "remote-head", snapshotRevision: 3)));

            RemoteGameSave? presentedRemote = null;
            var sync = store.CreateNew("save-1");
            sync.OnConflict += (conflict, continuation) =>
            {
                presentedRemote = conflict.Remote;
                continuation.KeepRemote();
            };
            await sync.CommitSaveContentAsync(
                NeoSaveTestSupport.SaveContent(
                    "Local", "{\"local\":{\"id\":\"local\",\"value\":1}}"),
                replaceSnapshot: false);

            Assert.That(
                ((JObject)presentedRemote!.values.Raw)["remote"]?["value"]?.Value<int>(),
                Is.EqualTo(9));
            Assert.That(
                ((JObject)sync.ActiveSave!.values.Raw)["remote"]?["value"]?.Value<int>(),
                Is.EqualTo(9));
            Assert.That(await local.LoadSaveAsync("save-1"), Does.Contain("\"remote\""));
        }

        [Test]
        public async Task ExistingCommit_SendsOnlySparseChangedRecords()
        {
            var remote = MaterializedRemote(
                "snap-1",
                4,
                "{\"a\":{\"id\":\"a\",\"value\":1}," +
                "\"b\":{\"id\":\"b\",\"value\":2}}");
            var (_, sync, api) = await LoadedExistingSaveAsync(remote);
            var committed = MaterializedRemote(
                "snap-2",
                1,
                "{\"a\":{\"id\":\"a\",\"value\":9}," +
                "\"b\":{\"id\":\"b\",\"value\":2}}");
            api.sparseCommitResults.Enqueue(NeoCommitResult.Committed(committed));
            var local = LocalGameSave.FromRemote(remote);
            local.values = new NeoSaveValues(JObject.Parse(
                "{\"a\":{\"id\":\"a\",\"value\":9}," +
                "\"b\":{\"id\":\"b\",\"value\":2}}"));

            await sync.CommitSaveContentAsync(
                JsonConvert.SerializeObject(local), replaceSnapshot: false);

            Assert.That(api.commits, Is.Empty);
            Assert.That(api.sparseCommits, Has.Count.EqualTo(1));
            var request = api.sparseCommits[0].request;
            Assert.That(request.baseSnapshotId, Is.EqualTo("snap-1"));
            Assert.That(request.baseSnapshotRevision, Is.EqualTo(4));
            Assert.That(request.changes, Has.Count.EqualTo(1));
            Assert.That(
                ((GameSaveValuePatchChange)request.changes[0]).valueId,
                Is.EqualTo("a"));
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("snap-2"));
        }

        [Test]
        public async Task ExistingCommit_FollowsTransitionWithoutRepeatingMutation()
        {
            var remote = MaterializedRemote(
                "snap-1", 4, "{\"a\":{\"id\":\"a\",\"value\":1}}");
            var (_, sync, api) = await LoadedExistingSaveAsync(remote);
            var ready = MaterializedRemote(
                "snap-2", 1, "{\"a\":{\"id\":\"a\",\"value\":2}}");
            api.sparseCommitResults.Enqueue(
                NeoCommitResult.Transitioning("save-1", "snap-2"));
            api.transitionStatuses.Enqueue(
                NeoSaveTransitionStatus.Copying("save-1", "snap-2"));
            api.transitionStatuses.Enqueue(NeoSaveTransitionStatus.Ready(ready));
            var local = LocalGameSave.FromRemote(remote);
            local.values = new NeoSaveValues(JObject.Parse(
                "{\"a\":{\"id\":\"a\",\"value\":2}}"));

            await sync.CommitSaveContentAsync(
                JsonConvert.SerializeObject(local), replaceSnapshot: false);

            Assert.That(api.sparseCommits, Has.Count.EqualTo(1));
            Assert.That(
                api.transitionStatusRequests,
                Is.EqualTo(new[] { "save-1", "save-1" }));
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("snap-2"));
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
        public async Task LargeInitialCreate_AppendsAtMost64RecordsBeforeActivation()
        {
            var (store, api, _) = await ReadyStoreWithCloudAsync();
            string values = LargeValuesJson(65);
            api.chunkedCreateTarget = new NeoChunkedCreateTarget
            {
                customId = "save-1",
                snapshotId = "snap-large",
                resumeToken = "resume-large",
            };
            api.chunkedCompleteResult = CompletedLargeSave(values);

            var sync = store.CreateNew("save-1");
            await sync.CommitSaveContentAsync(
                NeoSaveTestSupport.SaveContent("Large", values),
                replaceSnapshot: false);

            Assert.That(api.commits, Is.Empty, "the unbounded classic payload is never sent");
            Assert.That(api.chunkedAppends, Has.Count.EqualTo(2));
            Assert.That(api.chunkedAppends[0], Has.Count.EqualTo(64));
            Assert.That(api.chunkedAppends[1], Has.Count.EqualTo(1));
            Assert.That(api.chunkedAppendBaseRevisions, Is.EqualTo(new long[] { 0, 1 }));
            Assert.That(
                api.chunkedAppendResumeTokens,
                Is.All.EqualTo("resume-large"));
            Assert.That(api.chunkedCompleteCalls, Is.EqualTo(1));
            Assert.That(api.chunkedCompleteResumeTokens, Is.EqualTo(new[] { "resume-large" }));
            Assert.That(api.chunkedBeginFingerprints[0], Does.StartWith("sha256:"));
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("snap-large"));
        }

        [Test]
        public async Task LargeInitialCreate_OrdersParentsBeforeChildrenAndBindingsLast()
        {
            var (store, api, _) = await ReadyStoreWithCloudAsync();
            var values = new JObject();
            for (var index = 0; index < 63; index++)
            {
                var id = $"root-{index:00}";
                values[id] = new JObject { ["id"] = id, ["value"] = index };
            }
            values["z-parent"] = new JObject
            {
                ["id"] = "z-parent",
                ["value"] = new JObject(),
            };
            values["a-child"] = new JObject
            {
                ["id"] = "a-child",
                ["containerId"] = "z-parent",
                ["value"] = 1,
            };
            var content = JObject.Parse(NeoSaveTestSupport.SaveContent(
                "Large", values.ToString(Formatting.None)));
            content["staticBindings"] = new JObject { ["member-1"] = "z-parent" };
            api.chunkedCreateTarget = new NeoChunkedCreateTarget
            {
                customId = "save-1",
                snapshotId = "snap-large",
                resumeToken = "resume-large",
            };
            api.chunkedCompleteResult = CompletedLargeSave(
                values.ToString(Formatting.None));

            var sync = store.CreateNew("save-1");
            await sync.CommitSaveContentAsync(
                content.ToString(Formatting.None), replaceSnapshot: false);

            Assert.That(api.chunkedAppends, Has.Count.EqualTo(2));
            Assert.That(
                api.chunkedAppends[0].OfType<GameSaveValueReplaceChange>()
                    .Select(change => change.valueId),
                Does.Contain("z-parent"));
            Assert.That(
                api.chunkedAppends[0].OfType<GameSaveValueReplaceChange>()
                    .Select(change => change.valueId),
                Does.Not.Contain("a-child"));
            Assert.That(
                ((GameSaveValueReplaceChange)api.chunkedAppends[1][0]).valueId,
                Is.EqualTo("a-child"));
            Assert.That(
                api.chunkedAppends[1][1],
                Is.TypeOf<GameSaveStaticBindingSetChange>());
        }

        [Test]
        public async Task LargeInitialCreate_LostAppendResponseReplaysTheExactChunkAndRevision()
        {
            var (store, api, _) = await ReadyStoreWithCloudAsync();
            string values = LargeValuesJson(65);
            api.chunkedCreateTarget = new NeoChunkedCreateTarget
            {
                customId = "save-1",
                snapshotId = "snap-large",
                resumeToken = "resume-large",
            };
            api.chunkedCompleteResult = CompletedLargeSave(values);
            api.chunkedAppendFailures.Enqueue(null);
            api.chunkedAppendFailures.Enqueue(
                new InvalidOperationException("append response lost"));

            var sync = store.CreateNew("save-1");
            await sync.CommitSaveContentAsync(
                NeoSaveTestSupport.SaveContent("Large", values),
                replaceSnapshot: false);

            Assert.That(api.chunkedAppends, Has.Count.EqualTo(3));
            Assert.That(api.chunkedAppends[0], Has.Count.EqualTo(64));
            Assert.That(api.chunkedAppends[1], Has.Count.EqualTo(1));
            Assert.That(api.chunkedAppends[2], Has.Count.EqualTo(1),
                "the lost response retries the same bounded mutation");
            Assert.That(
                api.chunkedAppendBaseRevisions,
                Is.EqualTo(new long[] { 0, 1, 1 }));
            Assert.That(
                api.chunkedAppends[2][0],
                Is.SameAs(api.chunkedAppends[1][0]));
            Assert.That(api.transitionStatusRequests, Is.Empty,
                "pending manifests and copy status are not part of upload recovery");
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("snap-large"));
        }

        [Test]
        public async Task LargeInitialCreate_ReplayedBeginSkipsAcceptedRevisionBatches()
        {
            var (store, api, _) = await ReadyStoreWithCloudAsync();
            string values = LargeValuesJson(65);
            api.chunkedCreateTarget = new NeoChunkedCreateTarget
            {
                customId = "save-1",
                snapshotId = "snap-large",
                snapshotRevision = 1,
                resumeToken = "resume-large",
            };
            api.chunkedCompleteResult = CompletedLargeSave(values);

            var sync = store.CreateNew("save-1");
            await sync.CommitSaveContentAsync(
                NeoSaveTestSupport.SaveContent("Large", values),
                replaceSnapshot: false);

            Assert.That(api.chunkedAppends, Has.Count.EqualTo(1));
            Assert.That(api.chunkedAppends[0], Has.Count.EqualTo(1));
            Assert.That(api.chunkedAppendBaseRevisions, Is.EqualTo(new long[] { 1 }));
            Assert.That(api.transitionStatusRequests, Is.Empty);
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("snap-large"));
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
            api.getResult = NeoSaveTestSupport.Remote(
                "save-1", "snap-1", channel: "channel-prod");
            api.cloneResult = NeoCloneResult.Cloned(
                NeoSaveTestSupport.Remote(
                    "save-2",
                    "snap-2",
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
                "save-1", "snap-1", channel: "channel-prod");
            api.cloneResult = NeoCloneResult.Transitioning("save-2", "snap-2");
            api.transitionStatuses.Enqueue(
                NeoSaveTransitionStatus.Copying("save-2", "snap-2"));
            api.transitionStatuses.Enqueue(NeoSaveTransitionStatus.Ready(
                NeoSaveTestSupport.Remote(
                    "save-2",
                    "snap-2",
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
            api.getResult = NeoSaveTestSupport.Remote(
                "save-1", "snap-1", channel: "channel-prod");

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
