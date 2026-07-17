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
using UnityEngine.TestTools;

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
            long snapshotRevision = 1)
        {
            return "{\"name\":\"Live Save\",\"projectId\":\"project-1\"," +
                "\"customId\":\"save-1\",\"releaseChannelId\":\"" + LiveChannel + "\"," +
                "\"serverId\":\"server-save-1\",\"snapshotId\":\"" + snapshotId + "\"," +
                "\"snapshotRevision\":" + snapshotRevision + ",\"synchronizedAt\":3," +
                "\"version\":{\"id\":\"v1\",\"label\":\"1.0\"}," +
                "\"values\":" + values +
                ",\"createdAt\":1,\"updatedAt\":2}";
        }

        private static RemoteGameSave RemoteWithValues(
            string snapshotId, string valuesJson,
            string? liveSessionId = null,
            long snapshotRevision = 1)
        {
            var remote = NeoSaveTestSupport.Remote(
                "save-1", snapshotId, snapshotRevision);
            remote.values = new NeoSaveValues(JToken.Parse(valuesJson));
            remote.liveSessionId = liveSessionId;
            remote.snapshotRevision = snapshotRevision;
            remote.recordCache.snapshotId = snapshotId;
            remote.recordCache.snapshotRevision = snapshotRevision;
            foreach (var property in ((JObject)remote.values.Raw).Properties())
            {
                var descriptor = new GameSaveRecordDescriptor
                {
                    recordKind = NeoGameSaveRecordKinds.Value,
                    recordId = property.Name,
                    recordStateId = $"{snapshotId}:{property.Name}:{snapshotRevision}",
                    recordRevisionToken = $"token:{snapshotRevision}:{property.Name}",
                    contentHashAlgorithm = "sha256-canonical-json-v1",
                    contentHash = $"content:{snapshotRevision}:{property.Name}",
                };
                remote.recordCache.descriptors[descriptor.LogicalKey] = descriptor;
            }
            return remote;
        }

        private static NeoLivePatchResult Patched(string snapshotId, long revision) =>
            NeoLivePatchResult.Patched(
                snapshotId,
                revision,
                new NeoTimestamp(40 + revision),
                new List<GameSaveRecordDescriptor>());

        private static IEnumerable<string> ChangedValueIds(NeoSavePatch patch) =>
            patch.changes.Select(change => change switch
            {
                GameSaveValuePatchChange valuePatch => valuePatch.valueId,
                GameSaveValueReplaceChange replace => replace.valueId,
                _ => null,
            }).Where(id => id != null).Select(id => id!);

        private static JToken ReplacedValue(NeoSavePatch patch, string valueId) =>
            patch.changes.OfType<GameSaveValueReplaceChange>()
                .Single(change => change.valueId == valueId)
                .value;

        private static JToken AppliedValue(string content, string valueId) =>
            JObject.Parse(content)["values"]![valueId]!;

        private static string NumberedValues(int count)
        {
            var values = new JObject();
            for (var index = 0; index < count; index++)
            {
                values[$"value-{index:000}"] = index;
            }
            return values.ToString(Formatting.None);
        }

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
                getResult = RemoteWithValues("snap-1", "{}"),
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
        /// from an established live snapshot at revision one.</summary>
        private static async Task ForkEstablishedAsync(
            NeoSaveSynchronizer sync,
            FakeRealtimeProvider realtime,
            ManualLiveScheduler scheduler,
            string forkedValues = "{\"a\":1}")
        {
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live", forkedValues, "session-x")));
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
                RemoteWithValues("snap-live", "{\"a\":1}", "session-x")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}"),
                replaceSnapshot: false,
                flushLiveImmediately: true);

            Assert.That(realtime.forks, Has.Count.EqualTo(1),
                "explicit save must not wait for the live flush debounce");
            Assert.That(
                realtime.forks[0].patch.changes,
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
                RemoteWithValues("snap-live", "{\"a\":1}", "session-x")));

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
            Assert.That(ChangedValueIds(fork.patch), Is.EquivalentTo(new[] { "a" }));
            Assert.That(
                fork.patch.changes.OfType<GameSaveValueRestoreToAuthoredChange>(),
                Is.Empty);

            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("snap-live"));
            Assert.That(sync.ActiveSave.snapshotRevision, Is.EqualTo(1));
        }

        [Test]
        public async Task MetadataOnlyFork_HydratesBaselineAndPreservesRecordCache()
        {
            var (_, sync, api, _, realtime, scheduler) = await LiveSessionAsync();
            api.SetValueManifest(
                "snap-live", 2, "{\"a\":{\"id\":\"a\",\"value\":1}}");
            var committedMetadata = NeoSaveTestSupport.Remote(
                "save-1", "snap-live", snapshotRevision: 2);
            committedMetadata.liveSessionId = "session-x";
            realtime.forkResults.Enqueue(
                NeoCommitResult.Committed(committedMetadata));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":{\"id\":\"a\",\"value\":1}}"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(sync.ActiveSave!.recordCache.descriptors, Has.Count.EqualTo(1));

            realtime.livePatchResults.Enqueue(Patched("snap-live", 3));
            await sync.CommitSaveContentAsync(
                LiveSaveContent(
                    "{\"a\":{\"id\":\"a\",\"value\":1}," +
                    "\"b\":{\"id\":\"b\",\"value\":2}}",
                    "snap-live",
                    snapshotRevision: 2),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.livePatches, Has.Count.EqualTo(1));
            Assert.That(
                ChangedValueIds(realtime.livePatches[0].patch),
                Is.EquivalentTo(new[] { "b" }),
                "the hydrated fork baseline prevents an unrelated re-upload of a");
        }

        [Test]
        public async Task FirstFork_StagesMoreThan64ChangesBeforeAtomicActivation()
        {
            var (_, sync, api, _, realtime, scheduler) = await LiveSessionAsync();
            var allValues = JObject.Parse(NumberedValues(65));
            api.stagedBeginResults.Enqueue(
                NeoCommitResult.Transitioning("save-1", "snap-live"));
            api.transitionStatuses.Enqueue(
                NeoSaveTransitionStatus.Staging(
                    "save-1", "snap-live", 0, "snap-live"));
            api.chunkedCompleteResult = RemoteWithValues(
                "snap-live",
                allValues.ToString(Formatting.None),
                "session-x",
                snapshotRevision: 2);

            await sync.CommitSaveContentAsync(
                LiveSaveContent(allValues.ToString(Formatting.None)),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.forks, Is.Empty,
                "no partially populated live head is published");
            Assert.That(realtime.livePatches, Is.Empty);
            Assert.That(api.chunkedAppends.Select(batch => batch.Count),
                Is.EqualTo(new[] { 64, 1 }));
            Assert.That(api.stagedBegins[0].request.liveSessionId, Is.Not.Empty);
            Assert.That(api.chunkedCompleteCalls, Is.EqualTo(1));
            Assert.That(sync.ActiveSave!.snapshotRevision, Is.EqualTo(2));
        }

        [Test]
        public async Task EstablishedLiveHead_SplitsLargeBurstsIntoSequentialBatches()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler, "{\"seed\":0}");
            realtime.livePatches.Clear();
            realtime.livePatchResults.Enqueue(Patched("snap-live", 2));
            realtime.livePatchResults.Enqueue(Patched("snap-live", 3));
            realtime.livePatchResults.Enqueue(Patched("snap-live", 4));

            await sync.CommitSaveContentAsync(
                LiveSaveContent(NumberedValues(130), "snap-live", snapshotRevision: 1),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(
                realtime.livePatches.Select(call => call.patch.changes.Count),
                Is.EqualTo(new[] { 64, 64, 3 }));
            Assert.That(sync.ActiveSave!.snapshotRevision, Is.EqualTo(4));
        }

        [Test]
        public async Task ForkTransition_IsPolledWithoutRepeatingTheForkMutation()
        {
            var (_, sync, api, _, realtime, _) = await LiveSessionAsync();
            realtime.forkResults.Enqueue(
                NeoCommitResult.Transitioning("save-1", "snap-live"));
            api.transitionStatuses.Enqueue(NeoSaveTransitionStatus.Ready(
                RemoteWithValues("snap-live", "{\"a\":1}", "session-x")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}"),
                replaceSnapshot: false,
                flushLiveImmediately: true);

            Assert.That(realtime.forks, Has.Count.EqualTo(1));
            Assert.That(api.transitionStatusRequests, Is.EqualTo(new[] { "save-1" }));
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("snap-live"));
        }

        [Test]
        public async Task RapidCommits_CoalesceIntoOneFlushOfTheLatestState()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live", "{\"a\":2}", "session-x")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}"), replaceSnapshot: false);
            scheduler.Advance(0.3);
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}"), replaceSnapshot: false);
            scheduler.Advance(0.3);
            Assert.That(realtime.forks, Is.Empty, "the second stage restarted the debounce");

            scheduler.Advance(0.2);
            Assert.That(realtime.forks, Has.Count.EqualTo(1), "one coalesced flush");
            var entry = (int?)ReplacedValue(realtime.forks[0].patch, "a");
            Assert.That(entry, Is.EqualTo(2), "the flush carries the latest staged state");
        }

        [Test]
        public async Task ContinuousCommits_FlushAtTheMaxLatencyCap()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live", "{\"a\":4}", "session-x")));

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

            realtime.livePatchResults.Enqueue(Patched("snap-live", 2));
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2,\"b\":true}", "snap-live"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.forks, Has.Count.EqualTo(1), "no second fork");
            Assert.That(realtime.livePatches, Has.Count.EqualTo(1));
            var patch = realtime.livePatches[0];
            Assert.That(patch.snapshotId, Is.EqualTo("snap-live"));
            Assert.That(ChangedValueIds(patch.patch), Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(sync.ActiveSave!.snapshotRevision, Is.EqualTo(2));
            Assert.That(api.commits, Is.Empty, "the classic path never ran");
        }

        [Test]
        public async Task RemovedKeys_FlushAsRestoredToAuthored()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler, "{\"a\":1,\"b\":2}");

            realtime.livePatchResults.Enqueue(Patched("snap-live", 2));
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}", "snap-live"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            var patch = realtime.livePatches[0].patch;
            Assert.That(ChangedValueIds(patch), Is.Empty, "\"a\" is unchanged");
            Assert.That(
                patch.changes.OfType<GameSaveValueRestoreToAuthoredChange>()
                    .Select(change => change.valueId),
                Is.EquivalentTo(new[] { "b" }));
        }

        [Test]
        public async Task OfflineCommits_ComposeIntoOneFlushOnReconnect()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);

            realtime.canCommit = false;
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":3,\"c\":1}", "snap-live"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);
            Assert.That(realtime.livePatches, Is.Empty, "offline: deltas stay staged");

            realtime.livePatchResults.Enqueue(Patched("snap-live", 2));
            realtime.canCommit = true;
            realtime.SetState(NeoRealtimeConnectionState.Connected);

            Assert.That(realtime.livePatches, Has.Count.EqualTo(1), "one composed patch");
            var patch = realtime.livePatches[0].patch;
            Assert.That(ChangedValueIds(patch), Is.EquivalentTo(new[] { "a", "c" }));
            Assert.That((int?)ReplacedValue(patch, "a"), Is.EqualTo(3));
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
                "snap-live", "{\"a\":1}", "session-x"));

            Assert.That(headChanges, Is.Empty);
            Assert.That(liveChanges, Is.Empty);
        }

        [Test]
        public async Task CoEditorPatch_AutoAppliesAndRaisesLiveContentChanged()
        {
            var (_, sync, api, local, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);
            var headChanges = new List<RemoteGameSave>();
            var liveChanges = new List<string>();
            sync.OnRemoteHeadChanged += headChanges.Add;
            sync.OnLiveContentChanged += liveChanges.Add;

            api.SetValueDelta(
                "snap-live", 2, "{\"a\":{\"value\":1},\"web\":{\"value\":5}}");
            realtime.PushHead(RemoteWithValues(
                "snap-live",
                "{}",
                "session-x",
                snapshotRevision: 2));

            Assert.That(headChanges, Is.Empty, "live applies replace the divergence event");
            Assert.That(liveChanges, Has.Count.EqualTo(1));
            Assert.That((int?)AppliedValue(liveChanges[0], "web")["value"], Is.EqualTo(5));
            Assert.That(sync.ActiveSave!.snapshotRevision, Is.EqualTo(2));
            Assert.That(
                (int?)AppliedValue((await local.LoadSaveAsync("save-1"))!, "web")["value"],
                Is.EqualTo(5));
        }

        [Test]
        public async Task CoEditorPatch_NeverStompsLocallyDirtyKeys()
        {
            var (_, sync, api, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);
            var liveChanges = new List<string>();
            sync.OnLiveContentChanged += liveChanges.Add;

            // Stage a=2 but do NOT advance the clock: the key is dirty.
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live"),
                replaceSnapshot: false);

            api.SetValueDelta(
                "snap-live", 2, "{\"a\":{\"value\":9},\"web\":{\"value\":5}}");
            realtime.PushHead(RemoteWithValues(
                "snap-live",
                "{}",
                "session-x",
                snapshotRevision: 2));

            Assert.That(liveChanges, Has.Count.EqualTo(1));
            Assert.That(liveChanges[0], Does.Contain("\"a\":2"), "the dirty key wins");
            Assert.That((int?)AppliedValue(liveChanges[0], "web")["value"], Is.EqualTo(5),
                "the clean key applies");

            // The pending flush then sends only the dirty key.
            realtime.livePatchResults.Enqueue(Patched("snap-live", 3));
            scheduler.Advance(0.5);
            Assert.That(realtime.livePatches, Has.Count.EqualTo(1));
            Assert.That(
                ChangedValueIds(realtime.livePatches[0].patch),
                Is.EquivalentTo(new[] { "a" }));
        }

        [Test]
        public async Task AnotherSessionForkingPast_FreezesOursAndTheNextFlushReForks()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);
            var headChanges = new List<RemoteGameSave>();
            sync.OnRemoteHeadChanged += headChanges.Add;

            // A different session's fork moves the snapshot identity.
            realtime.PushHead(RemoteWithValues(
                "snap-other", "{\"o\":1}", "session-other"));
            Assert.That(headChanges, Has.Count.EqualTo(1), "classic divergence event");

            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live-2", "{\"a\":2}", "session-x")));
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live"),
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
                new GameSaveSnapshotRevisionSignal
                {
                    snapshotId = "snap-other",
                    snapshotRevision = 2,
                }));
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live-2", "{\"a\":2}", "session-x")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live"),
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
            var (_, sync, api, local, realtime, scheduler) = await LiveSessionAsync();
            var liveChanges = new List<string>();
            sync.OnLiveContentChanged += liveChanges.Add;
            sync.OnConflict += (_, continuation) => continuation.KeepRemote();

            api.SetValueManifest(
                "snap-2", 2, "{\"s\":{\"id\":\"s\",\"value\":1}}");
            realtime.forkResults.Enqueue(NeoCommitResult.Conflict(
                NeoSaveTestSupport.Remote("save-1", "snap-2", snapshotRevision: 2)));
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}"), replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.forks, Has.Count.EqualTo(1));
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("snap-2"));
            Assert.That(liveChanges, Has.Count.EqualTo(1), "the game is told to re-apply");
            Assert.That(liveChanges[0], Does.Contain("\"s\""));
            Assert.That(await local.LoadSaveAsync("save-1"), Does.Contain("\"s\""));

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
                RemoteWithValues("snap-2", "{\"s\":1}")));
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-live", "{\"s\":1,\"a\":1}", "session-x")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}"), replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.forks, Has.Count.EqualTo(2));
            Assert.That(realtime.forks[1].baseSnapshotId, Is.EqualTo("snap-2"));
            Assert.That(sync.ActiveSave!.snapshotId, Is.EqualTo("snap-live"));
        }

        [Test]
        public async Task LiveSessionsDisabled_KeepsTheSparseSnapshotCommitPath()
        {
            var (_, sync, api, _, realtime, _) = await LiveSessionAsync(
                liveSessionsEnabled: false);
            api.sparseCommitResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues("snap-2", "{\"a\":1}")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":1}"), replaceSnapshot: false);

            Assert.That(api.sparseCommits, Has.Count.EqualTo(1), "sparse REST commit");
            Assert.That(realtime.commits, Is.Empty);
            Assert.That(realtime.forks, Is.Empty);
            Assert.That(realtime.livePatches, Is.Empty);
        }

        [Test]
        public async Task Dispose_FlushesStagedChangesBestEffort()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);

            realtime.livePatchResults.Enqueue(Patched("snap-live", 2));
            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live"),
                replaceSnapshot: false);

            sync.Dispose();

            Assert.That(realtime.livePatches, Has.Count.EqualTo(1),
                "the teardown flush bypasses the throttle");
            Assert.That(
                ChangedValueIds(realtime.livePatches[0].patch),
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
                LiveSaveContent("{\"a\":2}", "snap-live"),
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
                LiveSaveContent("{\"a\":2}", "snap-live"),
                replaceSnapshot: false);

            scheduler.Advance(0.5);
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(realtime.livePatches, Has.Count.EqualTo(1));

            scheduler.Advance(10);
            realtime.SetState(NeoRealtimeConnectionState.Connected);

            Assert.That(realtime.livePatches, Has.Count.EqualTo(1),
                "provider cancellation during shutdown is terminal for this live session");
        }

        [Test]
        public async Task ServerRejection_LogsAnErrorAndKeepsRetrying()
        {
            var (_, sync, _, _, realtime, scheduler) = await LiveSessionAsync();
            await ForkEstablishedAsync(sync, realtime, scheduler);
            var errors = new List<Exception>();
            sync.OnCommitError += errors.Add;

            // The Convex client's response parser surfaces a server function
            // rejection as this envelope; unlike a disposed/canceled provider it
            // is not terminal, so the session must keep retrying (and, because the
            // payload is the problem, surface an error rather than a warning).
            realtime.livePatchThrows = new InvalidOperationException(
                "Mutation 'gameSaves:patchLiveSnapshot' failed: [Request ID: abc] Server Error");
            LogAssert.Expect(
                LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "Live flush for save \"save-1\" was rejected by the server"));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"a\":2}", "snap-live"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(errors, Has.Count.EqualTo(1), "OnCommitError still fires");
            Assert.That(realtime.livePatches, Has.Count.EqualTo(1), "first attempt reached the server");

            // Not terminal: the re-armed staged delta retries on the next window.
            // Once the underlying cause clears, a later flush succeeds.
            realtime.livePatchThrows = null;
            realtime.livePatchResults.Enqueue(Patched("snap-live", 2));
            scheduler.Advance(10);

            Assert.That(realtime.livePatches, Has.Count.EqualTo(2),
                "server rejection is not terminal; the staged delta retries and then succeeds");
            Assert.That(sync.ActiveSave!.snapshotRevision, Is.EqualTo(2));
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
                getResult = RemoteWithValues("snap-1", "{}"),
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

            // The game just plays — no CommitAsync anywhere.
            app.Save.Score = 41;
            var firstValues = (JObject)JObject.Parse(
                app.SerializeSaveData())["values"]!;
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues(
                    "snap-live",
                    firstValues.ToString(Formatting.None),
                    "session-x")));

            Assert.That(realtime.forks, Is.Empty, "the auto-commit coalesces first");
            autoCommitScheduler.Advance(0.3);
            Assert.That(realtime.forks, Is.Empty, "then the flush debounce throttles");
            flushScheduler.Advance(0.5);

            Assert.That(realtime.forks, Has.Count.EqualTo(1), "the write streamed out");
            Assert.That(realtime.forks[0].patch.changes, Is.Not.Empty);
            Assert.That(realtime.commits, Is.Empty, "never the classic commit path");

            app.Save.Score = 42;
            realtime.livePatchResults.Enqueue(Patched("snap-live", 2));
            autoCommitScheduler.Advance(0.3);
            flushScheduler.Advance(0.5);

            Assert.That(realtime.livePatches, Has.Count.EqualTo(1));
            Assert.That(realtime.livePatches[0].patch.changes, Has.Count.EqualTo(1),
                "a generated scalar setter targets only its value record");
            var scalarPatch = realtime.livePatches[0].patch.changes.Single()
                as GameSaveValuePatchChange;
            Assert.That(
                scalarPatch,
                Is.Not.Null,
                JsonConvert.SerializeObject(realtime.livePatches[0].patch));
            Assert.That(scalarPatch!.set.Keys, Is.EqualTo(new[] { "value" }));
            Assert.That(scalarPatch.unset, Is.Empty);

            app.Dispose();
            store.Dispose();
        }

        [Test]
        public async Task GeneratedCollectionWrites_ReplaceOwningValueRecord()
        {
            var api = new FakeApiClient
            {
                getResult = RemoteWithValues("snap-1", "{}"),
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

            app.Save.Heroes.Add(new global::Assets.Scripts.Neo.Hero(
                Name: "Ada", Health: 7));
            var heroesNode = app.Client.save.Get<NeoMemberListWritable>("Heroes");
            string ownerValueId = heroesNode.overrideValueId!;
            var firstValues = (JObject)JObject.Parse(
                app.SerializeSaveData())["values"]!;
            realtime.forkResults.Enqueue(NeoCommitResult.Committed(
                RemoteWithValues(
                    "snap-live",
                    firstValues.ToString(Formatting.None),
                    "session-x")));
            autoCommitScheduler.Advance(0.3);
            flushScheduler.Advance(0.5);

            app.Save.Heroes.Add(new global::Assets.Scripts.Neo.Hero(
                Name: "Grace", Health: 9));
            realtime.livePatchResults.Enqueue(Patched("snap-live", 2));
            autoCommitScheduler.Advance(0.3);
            flushScheduler.Advance(0.5);

            Assert.That(realtime.livePatches, Has.Count.EqualTo(1));
            var ownerChanges = realtime.livePatches[0].patch.changes
                .Where(change => change switch
                {
                    GameSaveValueReplaceChange replace =>
                        replace.valueId == ownerValueId,
                    GameSaveValuePatchChange fieldPatch =>
                        fieldPatch.valueId == ownerValueId,
                    _ => false,
                })
                .ToList();
            Assert.That(ownerChanges, Has.Count.EqualTo(1));
            Assert.That(ownerChanges[0], Is.TypeOf<GameSaveValueReplaceChange>(),
                "collection structure is committed with value.replace");

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
                RemoteWithValues("snap-created", "{\"a\":1}", "stamped")));
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
            realtime.livePatchResults.Enqueue(Patched("snap-created", 2));
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
            api.SetValueDelta(
                "snap-created", 3, "{\"a\":{\"value\":2},\"web\":{\"value\":5}}");
            realtime.PushHead(RemoteWithValues(
                "snap-created",
                "{}",
                "stamped",
                snapshotRevision: 3));
            Assert.That(liveChanges, Has.Count.EqualTo(1), "web edits reach the game");
            Assert.That((int?)AppliedValue(liveChanges[0], "web")["value"], Is.EqualTo(5));
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
                    "snap-live", "{}", "session-prior", snapshotRevision: 2),
            };
            var local = new NeoInMemoryLocalSaveStore();
            // The previous session flushed fully before closing: the persisted
            // copy is exactly the server-acknowledged state of snap-live.
            await local.CommitSaveAsync(
                "save-1",
                LiveSaveContent("{}", "snap-live")
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

            Assert.That(content, Does.Contain("\"snapshotRevision\":2"),
                "the cloud copy was adopted");
        }

        [Test]
        public async Task Load_StillConflictsWhenTheLocalLiveCopyIsDirty()
        {
            var api = new FakeApiClient
            {
                getResult = RemoteWithValues(
                    "snap-live", "{\"a\":9}", "session-prior", snapshotRevision: 2),
            };
            var local = new NeoInMemoryLocalSaveStore();
            // Same snapshot, but the local copy has unflushed offline edits
            // (no liveFlushed marker): the conflict contract must still run.
            await local.CommitSaveAsync(
                "save-1", LiveSaveContent("{}", "snap-live"));
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
                RemoteWithValues("snap-live", "{}", "session-x")));

            await sync.CommitSaveContentAsync(
                LiveSaveContent("{\"stamp\":{\"value\":\"2026-06-11T11:50:29.643Z\"}}"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);

            Assert.That(realtime.forks, Has.Count.EqualTo(1));
            var entry = ReplacedValue(realtime.forks[0].patch, "stamp");
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
                LiveSaveContent("{\"a\":2}", "snap-live"),
                replaceSnapshot: false);
            scheduler.Advance(0.5);
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(realtime.livePatches, Has.Count.EqualTo(1), "one failed attempt");

            // The failure re-armed the throttle; the next window retries the
            // same composed delta.
            realtime.livePatchThrows = null;
            realtime.livePatchResults.Enqueue(Patched("snap-live", 2));
            scheduler.Advance(0.5);
            Assert.That(realtime.livePatches, Has.Count.EqualTo(2));
            Assert.That(
                ChangedValueIds(realtime.livePatches[1].patch),
                Is.EquivalentTo(new[] { "a" }));
        }
    }
}
