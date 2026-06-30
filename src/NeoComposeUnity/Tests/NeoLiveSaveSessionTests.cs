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
using UnityEngine;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Live save sessions (specs/live-save-sessions.md) at the synchronizer
    /// seam: commits stage locally and flush through a debounce/max-latency
    /// throttle, the first flush forks the head, later flushes patch in place,
    /// offline deltas compose, inbound co-editor patches auto-apply with dirty
    /// keys winning, and this session's own writes echo back silently.
    /// </summary>
    public class NeoLiveSaveSessionTests
    {
        /// <summary>Deterministic clock + delay seams for the flush throttle.</summary>
        private sealed class ManualLiveScheduler
        {
            private readonly List<(double dueAt, AwaitableCompletionSource done)> waits = new();

            public double NowSeconds { get; private set; }

            public double Now() => NowSeconds;

            public Awaitable Delay(double seconds)
            {
                var source = new AwaitableCompletionSource();
                waits.Add((NowSeconds + seconds, source));
                return source.Awaitable;
            }

            public void Advance(double seconds)
            {
                NowSeconds += seconds;
                // Completing one wait can park a new one; loop until quiescent.
                while (true)
                {
                    var due = waits.Where(wait => wait.dueAt <= NowSeconds).ToList();
                    if (due.Count == 0) return;
                    foreach (var wait in due)
                    {
                        waits.Remove(wait);
                        wait.done.TrySetResult();
                    }
                }
            }
        }

        private const string LiveChannel = NeoSaveTestSupport.TargetChannel;

        private static string LiveSaveContent(
            string values,
            string snapshotId = "snap-1",
            string snapshotHash = "hash-1",
            string tileGridDeltas = "{}")
        {
            return "{\"name\":\"Live Save\",\"projectId\":\"project-1\"," +
                "\"customId\":\"save-1\",\"releaseChannelId\":\"" + LiveChannel + "\"," +
                "\"serverId\":\"server-save-1\",\"snapshotId\":\"" + snapshotId + "\"," +
                "\"snapshotHash\":\"" + snapshotHash + "\",\"synchronizedAt\":3," +
                "\"version\":{\"id\":\"v1\",\"label\":\"1.0\"}," +
                "\"values\":" + values + ",\"tileGridDeltas\":" + tileGridDeltas +
                ",\"createdAt\":1,\"updatedAt\":2}";
        }

        private static RemoteGameSave RemoteWithValues(
            string snapshotId, string snapshotHash, string valuesJson,
            string? liveSessionId = null,
            string tileGridDeltasJson = "{}")
        {
            var remote = NeoSaveTestSupport.Remote("save-1", snapshotId, snapshotHash);
            remote.values = new NeoSaveValues(JToken.Parse(valuesJson));
            remote.tileGridDeltas =
                JsonConvert.DeserializeObject<Dictionary<string, TileGridDeltaContent>>(
                    tileGridDeltasJson)
                ?? new Dictionary<string, TileGridDeltaContent>();
            remote.liveSessionId = liveSessionId;
            return remote;
        }

        private static string TileGridDeltasJson(string tileValueId) =>
            "{\"grid-value-1\":{\"schemaVersion\":1,\"regions\":[{" +
            "\"gridValueId\":\"grid-value-1\",\"layerId\":\"background-layer\"," +
            "\"layerKind\":\"tile\",\"regionKey\":\"0:0\",\"regionX\":0,\"regionY\":0," +
            "\"dataSchemaVersion\":1,\"delta\":{\"entries\":{\"0,0\":{\"tileValueId\":\"" +
            tileValueId +
            "\"}},\"removedInstanceIds\":[],\"restoredToAuthored\":[]}," +
            "\"contentHash\":\"hash-" + tileValueId + "\"}]}}";

        private static async Task<(NeoProjectStore store,
            NeoSaveSynchronizer sync,
            FakeApiClient api,
            NeoInMemoryLocalSaveStore local,
            FakeRealtimeProvider realtime,
            ManualLiveScheduler scheduler)> LiveSessionAsync(
            bool liveSessionsEnabled = true)
        {
            var api = new FakeApiClient
            {
                getResult = RemoteWithValues("snap-1", "hash-1", "{}"),
            };
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
                targetReleaseChannelId: LiveChannel,
                options: new NeoSaveOptions { LiveSessionsEnabled = liveSessionsEnabled },
                realtimeProvider: realtime);
            await store.LoadAsync();

            var sync = store.Open("save-1");
            var scheduler = new ManualLiveScheduler();
            sync.LiveClock = scheduler.Now;
            sync.LiveDelay = scheduler.Delay;
            await sync.LoadSaveContentAsync();
            return (store, sync, api, local, realtime, scheduler);
        }

        /// <summary>Drives a session through its fork so patch-path tests start
        /// from an established live snapshot ("snap-live"/"hash-live").</summary>
        private static async Task ForkEstablishedAsync(
            NeoSaveSynchronizer sync,
            FakeRealtimeProvider realtime,
            ManualLiveScheduler scheduler,
            string forkedValues = "{\"a\":1}")
        {
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live", "hash-live", forkedValues, "session-x")));
            await sync.CommitSaveContentAsync(
                LiveSaveContent(forkedValues), replaceSnapshot: false);
            scheduler.Advance(0.5);
            Assert.That(realtime.forks, Has.Count.EqualTo(1), "fork should have flushed");
        }

        [Test]
        public async Task Commit_StagesLocallyWithoutAnyImmediateCloudWrite()
        {
            var (_, sync, api, local, realtime, _) = await LiveSessionAsync();
            var commits = new List<LocalGameSave>();
            sync.OnCommitSuccess += commits.Add;

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}"), replaceSnapshot: false);

            Assert.That(commits, Has.Count.EqualTo(1), "local commit succeeds immediately");
            Assert.That(await local.LoadSaveAsync("save-1"), Does.Contain("\"a\":1"));
            Assert.That(api.commits, Is.Empty, "no classic REST commit");
            Assert.That(realtime.commits, Is.Empty, "no classic realtime commit");
            Assert.That(realtime.forks, Is.Empty, "the fork waits for the debounce");
        }

        [Test]
        public async Task ExplicitCommit_FlushesLiveSnapshotBeforeReturning()
        {
            var (_, sync, api, local, realtime, _) = await LiveSessionAsync();
            var commits = new List<LocalGameSave>();
            sync.OnCommitSuccess += commits.Add;

            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live", "hash-live", "{\"a\":1}", "session-x")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}"),
                replaceSnapshot: false,
                flushLiveImmediately: true);

            Assert.That(realtime.forks, Has.Count.EqualTo(1),
                "explicit save must not wait for the live flush debounce");
            Assert.That(
                realtime.forks[0].patch.entries,
                Is.Not.Empty,
                "the immediate flush carries the staged save-value write");
            Assert.That(commits, Has.Count.EqualTo(1), "local commit still succeeds");
            Assert.That(await local.LoadSaveAsync("save-1"), Does.Contain("\"a\":1"));
            Assert.That(api.commits, Is.Empty, "still no classic REST commit");
        }

        [Test]
        public async Task FirstFlush_ForksTheHeadWithThePerKeyPatch()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live", "hash-live", "{\"a\":1}", "session-x")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}"), replaceSnapshot: false);
            scheduler.Advance(0.49);
            Assert.That(realtime.forks, Is.Empty, "still inside the debounce window");

            scheduler.Advance(0.01);
            Assert.That(realtime.forks, Has.Count.EqualTo(1));
            var fork = realtime.forks[0];
            Assert.That(fork.customId, Is.EqualTo("save-1"));
            Assert.That(fork.baseSnapshotId, Is.EqualTo("snap-1"));
            Assert.That(fork.liveSessionId, Is.Not.Empty);
            Assert.That(fork.patch.entries.Keys, Is.EquivalentTo(new[] { "a" }));
            Assert.That(fork.patch.restoredToAuthored, Is.Empty);

            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("snap-live"));
            Assert.That(sync.ActiveSave.snapshotHash, Is.EqualTo("hash-live"));
        }

        [Test]
        public async Task RapidCommits_CoalesceIntoOneFlushOfTheLatestState()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live", "hash-live", "{\"a\":2}", "session-x")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}"), replaceSnapshot: false);
            scheduler.Advance(0.3);
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}"), replaceSnapshot: false);
            scheduler.Advance(0.3);
            Assert.That(realtime.forks, Is.Empty, "the second stage restarted the debounce");

            scheduler.Advance(0.2);
            Assert.That(realtime.forks, Has.Count.EqualTo(1), "one coalesced flush");
            var entry = (int?)realtime.forks[0].patch.entries["a"];
            Assert.That(entry, Is.EqualTo(2), "the flush carries the latest staged state");
        }

        [Test]
        public async Task ContinuousCommits_FlushAtTheMaxLatencyCap()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live", "hash-live", "{\"a\":4}", "session-x")));

            // A stage every 0.4s keeps the debounce window perpetually moving;
            // the latency cap (2s) must force the flush anyway.
            for (var step = 0; step < 5; step++)
            {
                await sync.CommitSaveContentAsync(
                    LiveSaveContent("{\"a\":" + step + "}"), replaceSnapshot: false);
                Assert.That(realtime.forks, Is.Empty);
                scheduler.Advance(0.4);
            }

            Assert.That(realtime.forks, Has.Count.EqualTo(1), "flushed at the 2s cap");
        }

        [Test]
        public async Task LaterFlushes_PatchTheLiveSnapshotInPlace()
        {
            var (_, sync, api, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);

            realtime.livePatchResults.Enqueue(NeoLivePatchResult.Patched(
                "snap-live", "hash-2", new NeoTimestamp(42)));
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2,\"b\":true}", "snap-live", "hash-live"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.forks, Has.Count.EqualTo(1), "no second fork");
            Assert.That(realtime.livePatches, Has.Count.EqualTo(1));
            var patch = realtime.livePatches[0];
            Assert.That(patch.snapshotId, Is.EqualTo("snap-live"));
            Assert.That(patch.patch.entries.Keys, Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(sync.ActiveSave!.snapshotHash, Is.EqualTo("hash-2"));
            Assert.That(api.commits, Is.Empty, "the classic path never ran");
        }

        [Test]
        public async Task TileGridDeltaChanges_FlushAsLiveSidecarPatch()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);

            realtime.livePatchResults.Enqueue(NeoLivePatchResult.Patched(
                "snap-live", "hash-2", new NeoTimestamp(42)));
            await sync.CommitSaveContentAsync(
                LiveSaveContent(
                    "{\"a\":1}",
                    "snap-live",
                    "hash-live",
                    TileGridDeltasJson("session-dirt")),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.livePatches, Has.Count.EqualTo(1));
            var patch = realtime.livePatches[0].patch;
            Assert.That(patch.entries, Is.Empty, "no ordinary values changed");
            Assert.That(patch.restoredToAuthored, Is.Empty);
            Assert.That(patch.tileGridDeltas, Is.Not.Null);
            var region = patch.tileGridDeltas!["grid-value-1"].regions[0];
            Assert.That(
                region.delta.entries["0,0"]["tileValueId"]!.ToString(),
                Is.EqualTo("session-dirt"));
        }

        [Test]
        public async Task RemovedKeys_FlushAsRestoredToAuthored()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler, "{\"a\":1,\"b\":2}");

            realtime.livePatchResults.Enqueue(NeoLivePatchResult.Patched(
                "snap-live", "hash-2", new NeoTimestamp(42)));
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}", "snap-live", "hash-live"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            var patch = realtime.livePatches[0].patch;
            Assert.That(patch.entries, Is.Empty, "\"a\" is unchanged");
            Assert.That(patch.restoredToAuthored, Is.EquivalentTo(new[] { "b" }));
        }

        [Test]
        public async Task OfflineCommits_ComposeIntoOneFlushOnReconnect()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);

            realtime.canCommit = false;
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live", "hash-live"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":3,\"c\":1}", "snap-live", "hash-live"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);
            Assert.That(realtime.livePatches, Is.Empty, "offline: deltas stay staged");

            realtime.livePatchResults.Enqueue(NeoLivePatchResult.Patched(
                "snap-live", "hash-2", new NeoTimestamp(42)));
            realtime.canCommit = true;
            realtime.SetState(NeoRealtimeConnectionState.Connected);

            Assert.That(realtime.livePatches, Has.Count.EqualTo(1), "one composed patch");
            var patch = realtime.livePatches[0].patch;
            Assert.That(patch.entries.Keys, Is.EquivalentTo(new[] { "a", "c" }));
            Assert.That((int?)patch.entries["a"], Is.EqualTo(3));
        }

        [Test]
        public async Task OwnFlushEchoingBack_IsDroppedSilently()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);
            var headChanges = new List<RemoteGameSave>();
            var liveChanges = new List<string>();
            sync.OnRemoteHeadChanged += headChanges.Add;
            sync.OnLiveContentChanged += liveChanges.Add;

            realtime.PushHead(RemoteWithValues(
                "snap-live", "hash-live", "{\"a\":1}", "session-x"));

            Assert.That(headChanges, Is.Empty);
            Assert.That(liveChanges, Is.Empty);
        }

        [Test]
        public async Task CoEditorPatch_AutoAppliesAndRaisesLiveContentChanged()
        {
            var (_, sync, _, local, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);
            var headChanges = new List<RemoteGameSave>();
            var liveChanges = new List<string>();
            sync.OnRemoteHeadChanged += headChanges.Add;
            sync.OnLiveContentChanged += liveChanges.Add;

            realtime.PushHead(RemoteWithValues(
                "snap-live", "hash-web", "{\"a\":1,\"web\":5}", "session-x"));

            Assert.That(headChanges, Is.Empty, "live applies replace the divergence event");
            Assert.That(liveChanges, Has.Count.EqualTo(1));
            Assert.That(liveChanges[0], Does.Contain("\"web\":5"));
            Assert.That(sync.ActiveSave!.snapshotHash, Is.EqualTo("hash-web"));
            Assert.That(await local.LoadSaveAsync("save-1"), Does.Contain("\"web\":5"));
        }

        [Test]
        public async Task CoEditorPatch_AppliesTileGridDeltasWhenClean()
        {
            var (_, sync, _, local, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);
            var liveChanges = new List<string>();
            sync.OnLiveContentChanged += liveChanges.Add;

            realtime.PushHead(RemoteWithValues(
                "snap-live",
                "hash-web",
                "{\"a\":1}",
                "session-x",
                TileGridDeltasJson("web-path")));

            Assert.That(liveChanges, Has.Count.EqualTo(1));
            Assert.That(liveChanges[0], Does.Contain("\"web-path\""));
            Assert.That(sync.ActiveSave!.tileGridDeltas, Contains.Key("grid-value-1"));
            Assert.That(await local.LoadSaveAsync("save-1"), Does.Contain("\"web-path\""));
        }

        [Test]
        public async Task CoEditorPatch_NeverStompsLocallyDirtyKeys()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);
            var liveChanges = new List<string>();
            sync.OnLiveContentChanged += liveChanges.Add;

            // Stage a=2 but do NOT advance the clock: the key is dirty.
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live", "hash-live"),
                replaceSnapshot: false);

            realtime.PushHead(RemoteWithValues(
                "snap-live", "hash-web", "{\"a\":9,\"web\":5}", "session-x"));

            Assert.That(liveChanges, Has.Count.EqualTo(1));
            Assert.That(liveChanges[0], Does.Contain("\"a\":2"), "the dirty key wins");
            Assert.That(liveChanges[0], Does.Contain("\"web\":5"), "the clean key applies");

            // The pending flush then sends only the dirty key.
            realtime.livePatchResults.Enqueue(NeoLivePatchResult.Patched(
                "snap-live", "hash-3", new NeoTimestamp(43)));
            scheduler.Advance(0.5);
            Assert.That(realtime.livePatches, Has.Count.EqualTo(1));
            Assert.That(
                realtime.livePatches[0].patch.entries.Keys,
                Is.EquivalentTo(new[] { "a" }));
        }

        [Test]
        public async Task AnotherSessionForkingPast_FreezesOursAndTheNextFlushReForks()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);
            var headChanges = new List<RemoteGameSave>();
            sync.OnRemoteHeadChanged += headChanges.Add;

            // A different session's fork moves the head (hash not in our ring).
            realtime.PushHead(RemoteWithValues(
                "snap-other", "hash-other", "{\"o\":1}", "session-other"));
            Assert.That(headChanges, Has.Count.EqualTo(1), "classic divergence event");

            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live-2", "hash-live-2", "{\"a\":2}", "session-x")));
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live", "hash-live"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.livePatches, Is.Empty, "the frozen snapshot is never patched");
            Assert.That(realtime.forks, Has.Count.EqualTo(2), "the session re-forked");
        }

        [Test]
        public async Task StaleTargetPatchResult_ReForksOnTheCurrentHead()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);

            realtime.livePatchResults.Enqueue(NeoLivePatchResult.StaleTarget(
                RemoteWithValues("snap-other", "hash-other", "{\"o\":1}", "session-other")));
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live-2", "hash-live-2", "{\"a\":2}", "session-x")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live", "hash-live"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.livePatches, Has.Count.EqualTo(1));
            Assert.That(realtime.forks, Has.Count.EqualTo(2));
            Assert.That(
                realtime.forks[1].baseSnapshotId,
                Is.EqualTo("snap-other"),
                "the re-fork bases on the returned current head");
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("snap-live-2"));
        }

        [Test]
        public async Task ForkConflict_KeepRemoteAdoptsTheServerHead()
        {
            var (_, sync, _, local, realtime, scheduler) = await LiveSessionAsync();
            var liveChanges = new List<string>();
            sync.OnLiveContentChanged += liveChanges.Add;
            sync.OnConflict += (_, continuation) => continuation.KeepRemote();

            realtime.forkResults.Enqueue(NeoCommitResult.Conflict(
                RemoteWithValues("snap-2", "hash-2", "{\"s\":1}")));
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}"), replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.forks, Has.Count.EqualTo(1));
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("snap-2"));
            Assert.That(liveChanges, Has.Count.EqualTo(1), "the game is told to re-apply");
            Assert.That(liveChanges[0], Does.Contain("\"s\":1"));
            Assert.That(await local.LoadSaveAsync("save-1"), Does.Contain("\"s\":1"));

            // Dirt was discarded by the developer's explicit choice: nothing
            // further flushes.
            scheduler.Advance(5);
            Assert.That(realtime.forks, Has.Count.EqualTo(1));
            Assert.That(realtime.livePatches, Is.Empty);
        }

        [Test]
        public async Task ForkConflict_KeepLocalReForksOnTopOfTheServerHead()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            sync.OnConflict += (_, continuation) => continuation.KeepLocal();

            realtime.forkResults.Enqueue(NeoCommitResult.Conflict(
                RemoteWithValues("snap-2", "hash-2", "{\"s\":1}")));
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live", "hash-live", "{\"s\":1,\"a\":1}", "session-x")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}"), replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.forks, Has.Count.EqualTo(2));
            Assert.That(realtime.forks[1].baseSnapshotId, Is.EqualTo("snap-2"));
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("snap-live"));
        }

        [Test]
        public async Task LiveSessionsDisabled_KeepsTheClassicCommitPath()
        {
            var (_, sync, _, _, realtime, _) = await LiveSessionAsync(
                liveSessionsEnabled: false);
            realtime.commitResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-2", "hash-2", "{\"a\":1}")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}"), replaceSnapshot: false);

            Assert.That(realtime.commits, Has.Count.EqualTo(1), "classic realtime commit");
            Assert.That(realtime.forks, Is.Empty);
            Assert.That(realtime.livePatches, Is.Empty);
        }

        [Test]
        public async Task Dispose_FlushesStagedChangesBestEffort()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);

            realtime.livePatchResults.Enqueue(NeoLivePatchResult.Patched(
                "snap-live", "hash-2", new NeoTimestamp(42)));
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live", "hash-live"),
                replaceSnapshot: false);

            sync.Dispose();

            Assert.That(realtime.livePatches, Has.Count.EqualTo(1),
                "the teardown flush bypasses the throttle");
            Assert.That(
                realtime.livePatches[0].patch.entries.Keys,
                Is.EquivalentTo(new[] { "a" }));
        }

        [Test]
        public async Task DisposedProviderFailure_StopsLiveFlushRetries()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);
            var errors = new List<Exception>();
            sync.OnCommitError += errors.Add;

            realtime.livePatchThrows =
                new ObjectDisposedException("ConvexRealtimeProvider");
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live", "hash-live"),
                replaceSnapshot: false);

            scheduler.Advance(0.5);
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(realtime.livePatches, Has.Count.EqualTo(1));

            scheduler.Advance(10);
            realtime.SetState(NeoRealtimeConnectionState.Connected);

            Assert.That(realtime.livePatches, Has.Count.EqualTo(1),
                "provider disposal is terminal for this live session; it must not retry forever");
        }

        [Test]
        public async Task CanceledProviderFailure_StopsLiveFlushRetries()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);
            var errors = new List<Exception>();
            sync.OnCommitError += errors.Add;

            realtime.livePatchThrows = new TaskCanceledException("The operation was canceled.");
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live", "hash-live"),
                replaceSnapshot: false);

            scheduler.Advance(0.5);
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(realtime.livePatches, Has.Count.EqualTo(1));

            scheduler.Advance(10);
            realtime.SetState(NeoRealtimeConnectionState.Connected);

            Assert.That(realtime.livePatches, Has.Count.EqualTo(1),
                "provider cancellation during shutdown is terminal for this live session");
        }

        /// <summary>
        /// The auto-write trigger: while a live session is active the game
        /// never calls save — every save-value write schedules an automatic
        /// commit (coalesced), which the flush throttle then streams out.
        /// </summary>
        [Test]
        public async Task SaveValueWrites_AutoCommitWhileLive_WithoutExplicitSave()
        {
            var api = new FakeApiClient
            {
                getResult = RemoteWithValues("snap-1", "hash-1", "{}"),
            };
            var local = new NeoInMemoryLocalSaveStore();
            var realtime = new FakeRealtimeProvider
            {
                State = NeoRealtimeConnectionState.Connected,
                canCommit = true,
            };
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(
                    System.IO.File.ReadAllText(
                        "Packages/com.ryanbliss.neocompose/Tests/synth-example.json")),
                localStore: local,
                apiClient: api,
                targetReleaseChannelId: LiveChannel,
                realtimeProvider: realtime);
            await store.LoadAsync();
            var sync = store.Open("save-1");
            var flushScheduler = new ManualLiveScheduler();
            sync.LiveClock = flushScheduler.Now;
            sync.LiveDelay = flushScheduler.Delay;

            var app = await global::Assets.Scripts.Neo.TestProjectNeo.Load(sync);
            var autoCommitScheduler = new ManualLiveScheduler();
            app.Client.LiveAutoCommitDelay = autoCommitScheduler.Delay;

            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live", "hash-live", "{}", "session-x")));

            // The game just plays — no CommitAsync anywhere.
            app.Save.Score = 41;

            Assert.That(realtime.forks, Is.Empty, "the auto-commit coalesces first");
            autoCommitScheduler.Advance(0.3);
            Assert.That(realtime.forks, Is.Empty, "then the flush debounce throttles");
            flushScheduler.Advance(0.5);

            Assert.That(realtime.forks, Has.Count.EqualTo(1), "the write streamed out");
            Assert.That(realtime.forks[0].patch.entries, Is.Not.Empty);
            Assert.That(realtime.commits, Is.Empty, "never the classic commit path");

            app.Dispose();
            store.Dispose();
        }

        /// <summary>
        /// A brand-new save created by a live session is live from snapshot
        /// one: the classic create rides the session id, the server stamps the
        /// created head, and every later flush patches it directly — so the
        /// web's write-through engages the moment the save is first viewed.
        /// </summary>
        [Test]
        public async Task NewSave_CreatesALiveStampedHeadAndPatchesItDirectly()
        {
            var api = new FakeApiClient(); // no remote save exists yet
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
                targetReleaseChannelId: LiveChannel,
                realtimeProvider: realtime);
            await store.LoadAsync();
            var sync = store.Open("save-1");
            var scheduler = new ManualLiveScheduler();
            sync.LiveClock = scheduler.Now;
            sync.LiveDelay = scheduler.Delay;

            realtime.commitResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-created", "hash-created", "{\"a\":1}", "stamped")));
            await sync.CommitSaveContentAsync(
                NeoSaveTestSupport.SaveContent("New Save", "{\"a\":1}"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.commits, Has.Count.EqualTo(1), "classic create path");
            Assert.That(
                realtime.commits[0].request.liveSessionId,
                Is.Not.Null.And.Not.Empty,
                "the create carries the session id");
            Assert.That(realtime.forks, Is.Empty);

            // The very next change patches the stamped head — never a fork.
            // The content deliberately carries NO server identity, exactly like
            // the game's serialized payload for a save created from defaults:
            // the synchronizer's own record must supply it, or this flush would
            // fall back into the classic append path and freeze the live head.
            realtime.livePatchResults.Enqueue(NeoLivePatchResult.Patched(
                "snap-created", "hash-2", new NeoTimestamp(42)));
            await sync.CommitSaveContentAsync(
                NeoSaveTestSupport.SaveContent("New Save", "{\"a\":2}"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.commits, Has.Count.EqualTo(1), "no classic re-commit");
            Assert.That(realtime.forks, Is.Empty, "the stamped head is already live");
            Assert.That(realtime.livePatches, Has.Count.EqualTo(1));
            Assert.That(realtime.livePatches[0].snapshotId, Is.EqualTo("snap-created"));

            // A created save never went through the load path, so the create
            // flush must attach the realtime head subscription itself — it is
            // the channel inbound co-editor (web) patches arrive on.
            Assert.That(
                realtime.HeadSubscriptions,
                Has.Count.EqualTo(1),
                "the create flush attaches the head subscription");
            var liveChanges = new List<string>();
            sync.OnLiveContentChanged += liveChanges.Add;
            realtime.PushHead(RemoteWithValues(
                "snap-created", "hash-web", "{\"a\":2,\"web\":5}", "stamped"));
            Assert.That(liveChanges, Has.Count.EqualTo(1), "web edits reach the game");
            Assert.That(liveChanges[0], Does.Contain("\"web\":5"));
        }

        /// <summary>
        /// A live snapshot patched in place by a co-editor (the web) while the
        /// game was closed: same snapshot identity + a fully-flushed local
        /// copy means there is nothing local to lose, so the load adopts the
        /// cloud copy silently — no conflict prompt. A dirty local copy still
        /// goes through the conflict flow.
        /// </summary>
        [Test]
        public async Task Load_AdoptsACoEditedLiveSnapshotWithoutConflict()
        {
            var api = new FakeApiClient
            {
                getResult = RemoteWithValues(
                    "snap-live", "hash-web", "{}", "session-prior"),
            };
            var local = new NeoInMemoryLocalSaveStore();
            // The previous session flushed fully before closing: the persisted
            // copy is exactly the server-acknowledged state of snap-live.
            await local.CommitSaveAsync(
                "save-1",
                LiveSaveContent("{}", "snap-live", "hash-old")
                    .Replace("\"serverId\"", "\"liveFlushed\":true,\"serverId\""));
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                apiClient: api,
                targetReleaseChannelId: LiveChannel,
                realtimeProvider: new FakeRealtimeProvider
                {
                    State = NeoRealtimeConnectionState.Connected,
                    canCommit = true,
                });
            await store.LoadAsync();
            var sync = store.Open("save-1");

            // No OnConflict handler attached: a conflict here would throw.
            var content = await sync.LoadSaveContentAsync();

            Assert.That(content, Does.Contain("hash-web"), "the cloud copy was adopted");
        }

        [Test]
        public async Task Load_StillConflictsWhenTheLocalLiveCopyIsDirty()
        {
            var api = new FakeApiClient
            {
                getResult = RemoteWithValues(
                    "snap-live", "hash-web", "{\"a\":9}", "session-prior"),
            };
            var local = new NeoInMemoryLocalSaveStore();
            // Same snapshot, but the local copy has unflushed offline edits
            // (no liveFlushed marker): the conflict contract must still run.
            await local.CommitSaveAsync(
                "save-1", LiveSaveContent("{}", "snap-live", "hash-old"));
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                apiClient: api,
                targetReleaseChannelId: LiveChannel,
                realtimeProvider: new FakeRealtimeProvider
                {
                    State = NeoRealtimeConnectionState.Connected,
                    canCommit = true,
                });
            await store.LoadAsync();
            var sync = store.Open("save-1");

            Assert.ThrowsAsync<NeoSaveConflictUnresolvedException>(
                async () => await sync.LoadSaveContentAsync());
        }

        /// <summary>
        /// Save values are opaque JSON: a date-looking string must reach the
        /// wire byte for byte, not be coerced into a date token (which the
        /// transport cannot carry) or reformatted.
        /// </summary>
        [Test]
        public async Task DateLookingValueStrings_FlushAsVerbatimStrings()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live", "hash-live", "{}", "session-x")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"stamp\":{\"value\":\"2026-06-11T11:50:29.643Z\"}}"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.forks, Has.Count.EqualTo(1));
            var entry = realtime.forks[0].patch.entries["stamp"];
            var value = entry["value"];
            Assert.That(value, Is.Not.Null);
            Assert.That(value!.Type, Is.EqualTo(JTokenType.String));
            Assert.That((string?)value, Is.EqualTo("2026-06-11T11:50:29.643Z"));
        }

        [Test]
        public async Task TransportFailure_KeepsTheDeltaStagedAndRetriesComposed()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);
            var errors = new List<Exception>();
            sync.OnCommitError += errors.Add;

            realtime.livePatchThrows = new InvalidOperationException("socket died");
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live", "hash-live"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(realtime.livePatches, Has.Count.EqualTo(1), "one failed attempt");

            // The failure re-armed the throttle; the next window retries the
            // same composed delta.
            realtime.livePatchThrows = null;
            realtime.livePatchResults.Enqueue(NeoLivePatchResult.Patched(
                "snap-live", "hash-2", new NeoTimestamp(42)));
            scheduler.Advance(0.5);
            Assert.That(realtime.livePatches, Has.Count.EqualTo(2));
            Assert.That(
                realtime.livePatches[1].patch.entries.Keys,
                Is.EquivalentTo(new[] { "a" }));
        }
    }
}
