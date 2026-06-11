// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Synchronizes one active save file across the local store and (when present)
    /// the cloud, and is the <see cref="INeoSaveLoader"/> the generated client
    /// consumes. Created by <see cref="NeoProjectStore"/> so it shares the
    /// <see cref="InternalProjectStore"/> with the browse list.
    /// </summary>
    /// <remarks>
    /// Load parallelizes the local read and the cloud fetch, matches by id,
    /// compares <see cref="RemoteGameSave.snapshotHash"/>, raises
    /// <see cref="OnConflict"/> on divergence (throwing when unhandled), runs
    /// <c>TryDeserialize</c> and raises <see cref="OnMigrationRequired"/> when the
    /// values can't be read, and raises <see cref="OnSelectedSaveRequiringClone"/>
    /// for a cross-channel save. Commit writes the local store first, then the
    /// cloud; a cloud conflict is resolved through <see cref="OnConflict"/>
    /// (keep-local writes a new head — never a destructive overwrite). A successful
    /// cloud commit is required vs. best-effort per
    /// <see cref="InternalProjectStore.RequireCloudCommit"/> (default best-effort).
    /// </remarks>
    public sealed class NeoSaveSynchronizer : INeoSaveLoader, IDisposable
    {
        private readonly InternalProjectStore core;
        private readonly bool isNewDraft;
        private readonly string? draftName;
        private LocalGameSave? active;
        private IDisposable? realtimeHeadSubscription;

        // --- Live session state (specs/live-save-sessions.md) ---------------
        // One synchronizer instance is one play session of its save.
        private readonly string liveSessionId = Guid.NewGuid().ToString();

        /// <summary>The live snapshot this session forked, or null before the
        /// first successful flush (the fork is lazy) and after the head moves
        /// past it (a newer session froze it).</summary>
        private string? liveSnapshotId;

        /// <summary>The server-acknowledged values map; the per-key diff base
        /// for the next flush. Null until a load/flush establishes it.</summary>
        private JObject? liveBaseline;

        /// <summary>The most recently staged local save; what the next flush
        /// diffs against <see cref="liveBaseline"/>.</summary>
        private LocalGameSave? stagedLive;

        private bool liveFlushLoopRunning;
        private bool liveTornDown;
        private double liveLastStagedAt;
        private double liveFirstDirtyAt = -1;
        private Action<NeoRealtimeConnectionState>? liveConnectionHook;

        /// <summary>
        /// The synchronizer's authoritative record of the save's server
        /// identity, updated at load and on every flush adoption. Staged game
        /// content may not carry it (a save created from defaults never saw
        /// it), and the staged object reference can be replaced mid-flush, so
        /// identity lives here rather than on whatever was staged last.
        /// </summary>
        private string? liveKnownServerId;
        private string? liveKnownSnapshotId;
        private string? liveKnownSnapshotHash;
        private double? liveKnownSynchronizedAt;

        /// <summary>Hashes this session produced, newest last. A head push
        /// carrying one of these is this session's own write echoing back.</summary>
        private readonly List<string> recentLiveHashes = new List<string>();
        private const int RecentLiveHashCapacity = 8;

        // Trailing debounce + max-latency cap on the flush pipeline. Internal
        // so tests can compress time; not public API (decision 9 of the spec).
        internal double LiveDebounceSeconds = 0.5;
        internal double LiveMaxLatencySeconds = 2.0;

        /// <summary>Monotonic clock + delay seams so the flush pipeline is
        /// deterministic under test.</summary>
        internal Func<double> LiveClock = DefaultLiveClock;
        internal Func<double, Awaitable> LiveDelay = DefaultLiveDelay;

        internal NeoSaveSynchronizer(
            InternalProjectStore core,
            string customId,
            bool isNewDraft,
            string? draftName = null)
        {
            this.core = core ?? throw new ArgumentNullException(nameof(core));
            if (string.IsNullOrWhiteSpace(customId))
            {
                throw new ArgumentException("Save customId cannot be empty.", nameof(customId));
            }

            CustomId = customId;
            this.isNewDraft = isNewDraft;
            this.draftName = draftName;
            State = isNewDraft ? NeoSaveSynchronizerState.Ready : NeoSaveSynchronizerState.Idle;
        }

        public string CustomId { get; private set; }

        public NeoSaveSynchronizerState State { get; private set; }

        public ProjectData Schema => core.Schema;

        /// <summary>The cloud save transport backing this synchronizer, or null when local-only.</summary>
        public INeoApiClient? ApiClient => core.ApiClient;

        /// <summary>The runtime authentication backing cloud sync, or null when local-only.</summary>
        public NeoAuthentication? Authentication => core.Authentication;

        /// <summary>The active save's current local representation, or null before load.</summary>
        public LocalGameSave? ActiveSave => active;

        public event Action<LocalGameSave>? OnLoaded;
        public event Action<LocalGameSave>? OnCommitSuccess;
        public event Action<Exception>? OnCommitError;
        public event Action<NeoSaveConflict, NeoSaveConflictContinuation>? OnConflict;
        public event Action<NeoSaveMigration, NeoSaveMigrationContinuation>? OnMigrationRequired;
        public event Action<NeoCloneRequest, NeoCloneContinuation>? OnSelectedSaveRequiringClone;
        public event Action<string>? OnSnapshotArchived;
        public event Action<string>? OnSaveArchived;

        /// <summary>
        /// Raised (opt-in) when a realtime push shows the cloud head diverging
        /// from the active local state — another device committed mid-session.
        /// Never auto-applies and never triggers <see cref="OnConflict"/> on its
        /// own; the existing conflict flow still runs only at load/commit time.
        /// </summary>
        public event Action<RemoteGameSave>? OnRemoteHeadChanged;

        /// <summary>
        /// Raised while a live session is active and a co-editor (e.g. the web
        /// tool) patched this session's live snapshot: the remote values were
        /// merged into the active save — locally dirty keys win until they
        /// flush — and the merged, serialized content is delivered for the
        /// game to re-apply. This session's own flushes echoing back never
        /// raise it.
        /// </summary>
        public event Action<string>? OnLiveContentChanged;

        public async Awaitable<string?> LoadSaveContentAsync()
        {
            // A from-scratch draft has nothing to load; the client builds defaults.
            if (isNewDraft && active == null)
            {
                State = NeoSaveSynchronizerState.Ready;
                return null;
            }

            State = NeoSaveSynchronizerState.Loading;
            try
            {
                var localContent = await core.LocalStore.LoadSaveAsync(CustomId);
                var remote = await ResolveRemoteForLoadAsync();

                LocalGameSaveLoader.TryLoad(localContent, out var local);
                var localOrNull = string.IsNullOrWhiteSpace(localContent) ? null : local;

                var resolved = await ResolveContentAsync(localOrNull, remote);
                if (resolved == null)
                {
                    State = NeoSaveSynchronizerState.Idle;
                    return null;
                }

                var migrated = await ApplyMigrationIfNeededAsync(resolved);
                if (migrated == null)
                {
                    State = NeoSaveSynchronizerState.Idle;
                    return null;
                }

                if (!LocalGameSaveLoader.TryLoad(migrated, out var loaded))
                {
                    throw new InvalidOperationException(
                        $"Resolved save content for \"{CustomId}\" could not be parsed.");
                }

                active = loaded;
                State = NeoSaveSynchronizerState.Ready;
                ResetLiveSessionBasis(loaded);
                AttachRealtimeHead();
                OnLoaded?.Invoke(loaded);
                return migrated;
            }
            catch
            {
                State = NeoSaveSynchronizerState.Error;
                throw;
            }
        }

        public async Awaitable CommitSaveContentAsync(string content, bool replaceSnapshot)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new ArgumentException("Save content cannot be empty.", nameof(content));
            }

            // Live sessions: the local store is still written immediately
            // (offline durability unchanged) but the cloud append is replaced
            // by the throttled patch pipeline. `replaceSnapshot` is moot here —
            // the live snapshot IS the in-place target.
            if (LiveModeEnabled)
            {
                await StageLiveCommitAsync(content);
                return;
            }

            State = NeoSaveSynchronizerState.Committing;
            try
            {
                // Local first — it is the durable source of truth and must not
                // depend on the network.
                await core.LocalStore.CommitSaveAsync(CustomId, content);
                var local = LocalGameSaveLoader.Load(content);
                if (string.IsNullOrEmpty(local.customId)) local.customId = CustomId;
                if (string.IsNullOrEmpty(local.releaseChannelId))
                {
                    local.releaseChannelId = core.TargetReleaseChannelId;
                }
                local.name = ResolveSaveName(local.name);

                if (!core.CloudEnabled)
                {
                    active = local;
                    core.RecordSavedFile(local, null);
                    State = NeoSaveSynchronizerState.Ready;
                    OnCommitSuccess?.Invoke(local);
                    return;
                }

                var committedRemote = await CommitToCloudAsync(local, replaceSnapshot);
                if (committedRemote != null)
                {
                    active = LocalGameSave.FromRemote(committedRemote);
                    // Re-stamp the local file with the server identity so a later
                    // load sees the synchronized snapshot hash.
                    await core.LocalStore.CommitSaveAsync(
                        CustomId, JsonConvert.SerializeObject(active));
                    // A brand-new save skipped the load path (and with it
                    // AttachRealtimeHead); attach now that the save exists so
                    // OnRemoteHeadChanged works for created saves too.
                    if (realtimeHeadSubscription == null)
                    {
                        AttachRealtimeHead();
                    }
                }
                else
                {
                    active = local;
                }

                core.RecordSavedFile(active, committedRemote);
                State = NeoSaveSynchronizerState.Ready;
                OnCommitSuccess?.Invoke(active);
            }
            catch (Exception)
            {
                State = NeoSaveSynchronizerState.Error;
                throw;
            }
        }

        /// <summary>
        /// Commit transport selection: the realtime provider when connected,
        /// otherwise REST. A provider failure (the socket can die between the
        /// <see cref="INeoRealtimeProvider.CanCommit"/> check and the call) falls
        /// back to one REST attempt so a flaky socket never costs a save.
        /// </summary>
        private async Awaitable<NeoCommitResult> CommitTransportAsync(
            NeoSaveCommitRequest request, bool replaceSnapshot)
        {
            var realtime = core.RealtimeProvider;
            if (realtime != null && realtime.CanCommit)
            {
                try
                {
                    return await realtime.CommitAsync(request, replaceSnapshot);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"[NeoCompose] Realtime commit for save \"{CustomId}\" failed; retrying " +
                        $"over REST. {exception.GetType().Name}: {exception.Message}");
                }
            }

            return await core.ApiClient!.CommitAsync(request, replaceSnapshot);
        }

        /// <summary>
        /// Cloud commit path: send the local edit; resolve a 409 through
        /// <see cref="OnConflict"/>. Returns the committed remote head, or null when
        /// a best-effort cloud commit failed (the local commit still stands).
        /// </summary>
        private async Awaitable<RemoteGameSave?> CommitToCloudAsync(
            LocalGameSave local, bool replaceSnapshot, string? createAsLiveSessionId = null)
        {
            NeoCommitResult result;
            try
            {
                result = await CommitTransportAsync(
                    BuildCommitRequest(local, active?.snapshotId, createAsLiveSessionId),
                    replaceSnapshot);
            }
            catch (Exception ex)
            {
                OnCommitError?.Invoke(ex);
                if (core.RequireCloudCommit) throw;
                // Best-effort: the local commit stands, but a cloud failure that
                // leaves no trace is undebuggable — the save silently stays
                // local-only forever. Surface why so it can be fixed (expired
                // session, missing scope, backend unreachable, …).
                Debug.LogWarning(
                    $"[NeoCompose] Cloud sync for save \"{CustomId}\" failed; keeping the local " +
                    $"copy (it stays local-only until the next successful commit). " +
                    $"{ex.GetType().Name}: {ex.Message}");
                return null;
            }

            if (!result.IsConflict) return result.CommittedSave;

            var serverHead = result.ServerHead!;
            if (OnConflict == null)
            {
                throw new NeoSaveConflictUnresolvedException(
                    $"Save \"{CustomId}\" conflicts with a newer cloud head and no OnConflict " +
                    "resolver is attached. Cloud sync requires a conflict resolver.");
            }

            State = NeoSaveSynchronizerState.Resolving;
            var continuation = new NeoSaveConflictContinuation();
            OnConflict.Invoke(new NeoSaveConflict(local, serverHead), continuation);
            var resolution = await continuation.Completion;
            State = NeoSaveSynchronizerState.Committing;

            if (resolution == NeoSaveConflictResolution.KeepRemote)
            {
                // Adopt the server head locally; the local edit is discarded by the
                // developer's explicit choice (no silent data loss).
                await core.LocalStore.CommitSaveAsync(
                    CustomId, JsonConvert.SerializeObject(serverHead));
                return serverHead;
            }

            // Keep local: write a NEW head on top of the server head — never a
            // destructive overwrite, so neither side's data is lost.
            var rebased = await CommitTransportAsync(
                BuildCommitRequest(local, serverHead.snapshotId), replaceSnapshot: false);
            if (rebased.IsConflict)
            {
                throw new NeoSaveConflictUnresolvedException(
                    $"Save \"{CustomId}\" conflicted again while writing a new head; retry the commit.");
            }

            return rebased.CommittedSave;
        }

        /// <summary>
        /// Chooses which content to load when local and cloud both exist: matched
        /// heads or one-sided load straight through; divergent heads raise
        /// <see cref="OnConflict"/>; a cross-channel cloud save raises
        /// <see cref="OnSelectedSaveRequiringClone"/>. Returns the chosen serialized
        /// content, or null when the developer declined (a no-op).
        /// </summary>
        private async Awaitable<string?> ResolveContentAsync(LocalGameSave? local, RemoteGameSave? remote)
        {
            if (remote != null && remote.releaseChannelId != core.TargetReleaseChannelId)
            {
                return await ResolveCloneAsync(remote);
            }

            if (local == null && remote == null) return null;
            if (remote == null) return JsonConvert.SerializeObject(local);
            if (local == null) return JsonConvert.SerializeObject(remote);

            if (local.snapshotHash == remote.snapshotHash)
            {
                // In sync; prefer the cloud head as the authoritative copy.
                return JsonConvert.SerializeObject(remote);
            }

            // A live snapshot patched in place by a co-editor (the web) while
            // this device was away: same snapshot identity and a fully-flushed
            // local copy mean there is nothing local to lose — adopt the cloud
            // copy without a conflict prompt (spec decision 8: "the next game
            // load picks it up"). A dirty local copy still goes through the
            // conflict flow below.
            if (local.liveFlushed
                && local.snapshotId == remote.snapshotId
                && !string.IsNullOrEmpty(remote.liveSessionId))
            {
                return JsonConvert.SerializeObject(remote);
            }

            // Divergent heads.
            if (OnConflict == null)
            {
                throw new NeoSaveConflictUnresolvedException(
                    $"Local and cloud heads for \"{CustomId}\" diverge and no OnConflict resolver " +
                    "is attached. Cloud sync requires a conflict resolver.");
            }

            State = NeoSaveSynchronizerState.Resolving;
            var continuation = new NeoSaveConflictContinuation();
            OnConflict.Invoke(new NeoSaveConflict(local, remote), continuation);
            var resolution = await continuation.Completion;

            return resolution == NeoSaveConflictResolution.KeepRemote
                ? JsonConvert.SerializeObject(remote)
                : JsonConvert.SerializeObject(local);
        }

        private async Awaitable<string?> ResolveCloneAsync(RemoteGameSave remote)
        {
            if (OnSelectedSaveRequiringClone == null)
            {
                throw new InvalidOperationException(
                    $"Save \"{CustomId}\" is bound to release channel \"{remote.releaseChannelId}\" " +
                    $"but the target is \"{core.TargetReleaseChannelId}\"; attach an " +
                    "OnSelectedSaveRequiringClone handler to clone it before loading.");
            }

            State = NeoSaveSynchronizerState.Resolving;
            var request = new NeoCloneRequest
            {
                snapshotId = remote.snapshotId,
                targetReleaseChannelId = core.TargetReleaseChannelId,
            };
            var continuation = new NeoCloneContinuation();
            OnSelectedSaveRequiringClone.Invoke(request, continuation);
            var decision = await continuation.Completion;
            if (!decision.Approved) return null; // no-op.

            var cloned = await core.ApiClient!.CloneSaveAsync(
                CustomId,
                new NeoCloneRequest
                {
                    cloneName = decision.NewName,
                    snapshotId = remote.snapshotId,
                    targetReleaseChannelId = core.TargetReleaseChannelId,
                });

            // The clone is a new save; switch the active id to it.
            CustomId = cloned.id;
            return JsonConvert.SerializeObject(cloned);
        }

        /// <summary>
        /// Runs the migration gate: if the chosen content's values can't be
        /// deserialized, raises <see cref="OnMigrationRequired"/> and either uses
        /// the developer-supplied migrated content or abandons the load (null).
        /// </summary>
        private async Awaitable<string?> ApplyMigrationIfNeededAsync(string content)
        {
            if (!LocalGameSaveLoader.TryLoad(content, out var save)) return content;
            if (save.TryDeserializeValues(out _)) return content;

            if (OnMigrationRequired == null)
            {
                throw new InvalidOperationException(
                    $"Save \"{CustomId}\" values cannot be read against the current schema and no " +
                    "OnMigrationRequired handler is attached.");
            }

            State = NeoSaveSynchronizerState.Resolving;
            var migration = new NeoSaveMigration(CustomId, save.values, core.Schema);
            var continuation = new NeoSaveMigrationContinuation();
            OnMigrationRequired.Invoke(migration, continuation);
            return await continuation.Completion;
        }

        /// <summary>
        /// (Re)subscribes to the active save's realtime cloud head. A pushed head
        /// primes the store's fresh-remote cache (the next load/commit sees it
        /// without a fetch) and raises <see cref="OnRemoteHeadChanged"/> when it
        /// diverges from the active local state. A no-op without a connected
        /// provider; re-runs on every successful load so a post-clone id switch
        /// follows the new save.
        /// </summary>
        private void AttachRealtimeHead()
        {
            realtimeHeadSubscription?.Dispose();
            realtimeHeadSubscription = null;
            var realtime = core.RealtimeProvider;
            if (realtime == null) return;
            realtimeHeadSubscription = realtime.SubscribeSaveHead(CustomId, OnRealtimeRemoteHead);
            if (liveConnectionHook == null)
            {
                // Reconnects restart the flush loop so offline-composed deltas
                // go out as soon as the transport is back.
                liveConnectionHook = OnLiveConnectionStateChanged;
                realtime.OnConnectionStateChanged += liveConnectionHook;
            }
        }

        private void OnRealtimeRemoteHead(RemoteGameSave remote)
        {
            core.RecordRealtimeRemoteHead(remote);
            if (TryHandleLiveRemoteHead(remote)) return;
            if (active != null && remote.snapshotHash != active.snapshotHash)
            {
                OnRemoteHeadChanged?.Invoke(remote);
            }
        }

        // ---------------------------------------------------------------------
        // Live save sessions (specs/live-save-sessions.md)
        // ---------------------------------------------------------------------

        /// <summary>
        /// True while this synchronizer runs its save as a live session:
        /// commits stage-and-stream into one live snapshot instead of
        /// appending classic cloud snapshots, and <see cref="NeoClient"/>
        /// auto-commits on every save-value write so the game never has to
        /// call save explicitly to be visualized live.
        /// </summary>
        public bool IsLiveSessionActive => LiveModeEnabled;

        /// <summary>Live sessions replace the classic cloud append whenever a
        /// realtime provider is registered (regardless of momentary connection
        /// state — offline flushes queue and compose) unless explicitly opted
        /// out via <see cref="NeoSaveOptions.LiveSessionsEnabled"/>.</summary>
        private bool LiveModeEnabled =>
            core.CloudEnabled
            && core.RealtimeProvider != null
            && core.Options.LiveSessionsEnabled;

        /// <summary>A fresh load is a fresh session basis: the fork is lazy
        /// (nothing staged, no live snapshot yet) and the loaded values become
        /// the diff baseline.</summary>
        private void ResetLiveSessionBasis(LocalGameSave loaded)
        {
            liveSnapshotId = null;
            stagedLive = null;
            liveFirstDirtyAt = -1;
            liveBaseline = AsValuesObject(loaded.values) is JObject values
                ? (JObject)values.DeepClone()
                : null;
            RecordKnownLiveIdentity(
                loaded.serverId, loaded.snapshotId, loaded.snapshotHash, loaded.synchronizedAt);
        }

        private void RecordKnownLiveIdentity(
            string? serverId, string? snapshotId, string? snapshotHash, double? synchronizedAt)
        {
            liveKnownServerId = serverId;
            liveKnownSnapshotId = snapshotId;
            liveKnownSnapshotHash = snapshotHash;
            liveKnownSynchronizedAt = synchronizedAt;
        }

        /// <summary>Fills server-identity fields the staged content does not
        /// carry from the synchronizer's authoritative record.</summary>
        private void MergeKnownLiveIdentityInto(LocalGameSave local)
        {
            if (string.IsNullOrEmpty(local.serverId)) local.serverId = liveKnownServerId;
            if (string.IsNullOrEmpty(local.snapshotId)) local.snapshotId = liveKnownSnapshotId;
            if (string.IsNullOrEmpty(local.snapshotHash))
            {
                local.snapshotHash = liveKnownSnapshotHash;
            }
            local.synchronizedAt ??= liveKnownSynchronizedAt;
        }

        /// <summary>Persists a flushed local only while it is still the newest
        /// staged content: a stage that landed mid-flush already persisted
        /// newer content (and restamps identity with its own flush), so
        /// overwriting it would window data loss on a crash. The persisted
        /// copy is marked <see cref="LocalGameSave.liveFlushed"/> — it is
        /// exactly the server-acknowledged state.</summary>
        private async Awaitable PersistFlushedLocalAsync(LocalGameSave local)
        {
            if (!ReferenceEquals(stagedLive, local)) return;
            local.liveFlushed = true;
            await core.LocalStore.CommitSaveAsync(CustomId, JsonConvert.SerializeObject(local));
            liveFirstDirtyAt = -1;
        }

        /// <summary>
        /// Live-mode commit: write the local store immediately (durability never
        /// depends on the network), surface success, and mark the session dirty
        /// so the throttled flush pipeline picks the change up.
        /// </summary>
        private async Awaitable StageLiveCommitAsync(string content)
        {
            State = NeoSaveSynchronizerState.Committing;
            try
            {
                await core.LocalStore.CommitSaveAsync(CustomId, content);
                var local = LocalGameSaveLoader.Load(content);
                if (string.IsNullOrEmpty(local.customId)) local.customId = CustomId;
                if (string.IsNullOrEmpty(local.releaseChannelId))
                {
                    local.releaseChannelId = core.TargetReleaseChannelId;
                }

                local.name = ResolveSaveName(local.name);

                // The game's serialized payload may not carry the server
                // identity — a save created from defaults has never seen it.
                // The synchronizer's own record is authoritative: without this
                // merge, every flush of a new save would look local-only and
                // re-enter the classic create/append path instead of patching
                // its live snapshot.
                MergeKnownLiveIdentityInto(local);

                active = local;
                stagedLive = local;
                core.RecordSavedFile(local, null);
                State = NeoSaveSynchronizerState.Ready;
                OnCommitSuccess?.Invoke(local);

                liveLastStagedAt = LiveClock();
                if (liveFirstDirtyAt < 0) liveFirstDirtyAt = liveLastStagedAt;
                KickLiveFlushLoop();
            }
            catch (Exception)
            {
                State = NeoSaveSynchronizerState.Error;
                throw;
            }
        }

        /// <summary>Starts the flush loop if it isn't already running.
        /// <c>async void</c> on purpose: the loop owns its lifetime, never
        /// throws past its own catch, and callers must not await it.</summary>
        private async void KickLiveFlushLoop()
        {
            if (liveFlushLoopRunning || liveTornDown) return;
            if (core.RealtimeProvider == null) return;
            liveFlushLoopRunning = true;
            try
            {
                await RunLiveFlushLoopAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[NeoCompose] Live flush loop for save \"{CustomId}\" stopped unexpectedly; " +
                    $"staged changes are retried on the next save or reconnect. " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
            finally
            {
                liveFlushLoopRunning = false;
            }
        }

        /// <summary>
        /// The throttle: waits out the trailing debounce window (restarted by
        /// each stage) bounded by the max-latency cap (anchored at the first
        /// unflushed stage), then flushes. Exits when nothing is dirty or the
        /// transport is down — the connection hook restarts it on reconnect.
        /// </summary>
        private async Awaitable RunLiveFlushLoopAsync()
        {
            while (!liveTornDown && liveFirstDirtyAt >= 0)
            {
                var now = LiveClock();
                var due = Math.Min(
                    liveLastStagedAt + LiveDebounceSeconds,
                    liveFirstDirtyAt + LiveMaxLatencySeconds);
                if (due > now)
                {
                    await LiveDelay(due - now);
                    continue;
                }

                var realtime = core.RealtimeProvider;
                if (realtime == null || !realtime.CanCommit)
                {
                    // Offline: staged deltas compose; resume on reconnect.
                    return;
                }

                await FlushLiveOnceAsync(realtime);
            }
        }

        /// <summary>
        /// One flush: diff the staged values against the baseline, then fork
        /// (first flush of the session) or patch (every one after). All
        /// failures keep the dirty state staged — deltas compose — and re-arm
        /// the throttle so a failing transport is never hot-looped.
        /// </summary>
        private async Awaitable FlushLiveOnceAsync(INeoRealtimeProvider realtime)
        {
            var local = stagedLive;
            if (local == null)
            {
                liveFirstDirtyAt = -1;
                return;
            }

            // Re-merge at flush time: a stage that raced the create round-trip
            // copied identity before the create stamped it.
            MergeKnownLiveIdentityInto(local);

            // A save that has never synced has no head to fork from; create it
            // through the classic commit path and fork on a later flush.
            if (local.IsLocalOnly || string.IsNullOrEmpty(local.snapshotId))
            {
                await FlushLiveViaClassicCommitAsync(local);
                return;
            }

            var staged = AsValuesObject(local.values);
            if (staged == null)
            {
                Debug.LogWarning(
                    $"[NeoCompose] Save \"{CustomId}\" staged values are not a JSON object; " +
                    "live patching is impossible, falling back to a classic commit.");
                await FlushLiveViaClassicCommitAsync(local);
                return;
            }

            var baseline = liveBaseline ?? new JObject();
            var patch = BuildLivePatch(baseline, staged);
            if (patch.IsEmpty)
            {
                if (ReferenceEquals(stagedLive, local)) liveFirstDirtyAt = -1;
                return;
            }

            if (liveSnapshotId == null)
            {
                await ForkLiveSessionAsync(realtime, local, staged, patch, local.snapshotId!);
                return;
            }

            NeoLivePatchResult result;
            try
            {
                result = await realtime.PatchLiveAsync(new NeoLivePatchRequest
                {
                    customId = CustomId,
                    snapshotId = liveSnapshotId,
                    patch = patch,
                });
            }
            catch (Exception exception)
            {
                HandleLiveFlushFailure(exception);
                return;
            }

            if (result.IsStaleTarget)
            {
                // Our live snapshot froze (a newer session forked past it, or
                // the head was repointed). Re-fork immediately; the stale base
                // then surfaces through the typed conflict contract.
                liveSnapshotId = null;
                await ForkLiveSessionAsync(
                    realtime, local, staged, patch, result.ServerHead!.snapshotId);
                return;
            }

            RecordLiveHash(result.SnapshotHash);
            liveBaseline = (JObject)staged.DeepClone();
            local.snapshotId = result.SnapshotId;
            local.snapshotHash = result.SnapshotHash;
            local.synchronizedAt = result.SynchronizedAt.EpochMilliseconds;
            RecordKnownLiveIdentity(
                local.serverId, result.SnapshotId, result.SnapshotHash, local.synchronizedAt);
            await PersistFlushedLocalAsync(local);
        }

        /// <summary>First flush of the session (or a re-fork after the live
        /// snapshot froze): fork the head, stamping this session's id.</summary>
        private async Awaitable ForkLiveSessionAsync(
            INeoRealtimeProvider realtime,
            LocalGameSave local,
            JObject staged,
            NeoSavePatch patch,
            string baseSnapshotId)
        {
            NeoCommitResult result;
            try
            {
                result = await realtime.ForkLiveAsync(
                    BuildLiveForkRequest(local, patch, baseSnapshotId));
            }
            catch (Exception exception)
            {
                HandleLiveFlushFailure(exception);
                return;
            }

            if (result.IsConflict)
            {
                await ResolveLiveForkConflictAsync(realtime, local, staged, patch, result.ServerHead!);
                return;
            }

            AdoptForkedHead(local, staged, result.CommittedSave!);
            await PersistFlushedLocalAsync(local);
        }

        /// <summary>
        /// Fork-base conflict: the head moved between this session's load and
        /// its first flush. Resolved through the existing <see cref="OnConflict"/>
        /// contract — keep-remote adopts the server head (and tells the game via
        /// <see cref="OnLiveContentChanged"/>); keep-local re-forks on top of the
        /// moved head, so the per-key patch merges instead of overwriting.
        /// </summary>
        private async Awaitable ResolveLiveForkConflictAsync(
            INeoRealtimeProvider realtime,
            LocalGameSave local,
            JObject staged,
            NeoSavePatch patch,
            RemoteGameSave serverHead)
        {
            if (OnConflict == null)
            {
                throw new NeoSaveConflictUnresolvedException(
                    $"Save \"{CustomId}\" live session is based on a stale head and no OnConflict " +
                    "resolver is attached. Cloud sync requires a conflict resolver.");
            }

            State = NeoSaveSynchronizerState.Resolving;
            var continuation = new NeoSaveConflictContinuation();
            OnConflict.Invoke(new NeoSaveConflict(local, serverHead), continuation);
            var resolution = await continuation.Completion;
            State = NeoSaveSynchronizerState.Ready;

            if (resolution == NeoSaveConflictResolution.KeepRemote)
            {
                var adopted = LocalGameSave.FromRemote(serverHead);
                adopted.liveFlushed = true;
                RecordKnownLiveIdentity(
                    serverHead.serverId,
                    serverHead.snapshotId,
                    serverHead.snapshotHash,
                    adopted.synchronizedAt);
                await core.LocalStore.CommitSaveAsync(
                    CustomId, JsonConvert.SerializeObject(adopted));
                active = adopted;
                stagedLive = null;
                liveSnapshotId = null;
                liveBaseline = AsValuesObject(serverHead.values) is JObject values
                    ? (JObject)values.DeepClone()
                    : null;
                liveFirstDirtyAt = -1;
                RecordLiveHash(serverHead.snapshotHash);
                OnLiveContentChanged?.Invoke(JsonConvert.SerializeObject(adopted));
                return;
            }

            NeoCommitResult rebased;
            try
            {
                rebased = await realtime.ForkLiveAsync(
                    BuildLiveForkRequest(local, patch, serverHead.snapshotId));
            }
            catch (Exception exception)
            {
                HandleLiveFlushFailure(exception);
                return;
            }

            if (rebased.IsConflict)
            {
                throw new NeoSaveConflictUnresolvedException(
                    $"Save \"{CustomId}\" conflicted again while re-forking its live session; " +
                    "retry the commit.");
            }

            AdoptForkedHead(local, staged, rebased.CommittedSave!);
            await PersistFlushedLocalAsync(local);
        }

        /// <summary>Local-only saves can't fork (no cloud head exists); the
        /// classic commit path creates the save with this session's id riding
        /// along, so the created head is a live snapshot from snapshot one and
        /// later flushes patch it directly. (If the save turned out to already
        /// exist server-side, the commit appends a classic snapshot — the id is
        /// ignored — and the next flush forks as usual.)</summary>
        private async Awaitable FlushLiveViaClassicCommitAsync(LocalGameSave local)
        {
            RemoteGameSave? committed;
            try
            {
                committed = await CommitToCloudAsync(
                    local, replaceSnapshot: false, createAsLiveSessionId: liveSessionId);
            }
            catch (Exception exception)
            {
                HandleLiveFlushFailure(exception);
                return;
            }

            if (committed == null)
            {
                // Best-effort cloud commit failed (already logged); stay dirty.
                ReArmLiveFlush();
                return;
            }

            if (!string.IsNullOrEmpty(committed.liveSessionId))
            {
                // The server stamped the created head live: it is this
                // session's live snapshot; subsequent flushes patch in place.
                liveSnapshotId = committed.snapshotId;
            }

            RecordLiveHash(committed.snapshotHash);
            liveBaseline = AsValuesObject(committed.values) is JObject values
                ? (JObject)values.DeepClone()
                : null;
            local.serverId = committed.serverId;
            local.snapshotId = committed.snapshotId;
            local.snapshotHash = committed.snapshotHash;
            local.snapshotName = committed.snapshotName;
            local.synchronizedAt = committed.synchronizedAt.EpochMilliseconds;
            RecordKnownLiveIdentity(
                committed.serverId,
                committed.snapshotId,
                committed.snapshotHash,
                local.synchronizedAt);
            await PersistFlushedLocalAsync(local);

            // A brand-new save never went through the load path (the new-draft
            // branch returns before AttachRealtimeHead), so the realtime head
            // subscription — the channel inbound co-editor patches arrive on —
            // does not exist yet. Now that the save has a cloud identity, the
            // subscription attaches cleanly; without it, web edits stream to
            // the server but never reach the running game.
            if (realtimeHeadSubscription == null)
            {
                AttachRealtimeHead();
            }
        }

        /// <summary>Adopts a successful fork: the committed snapshot becomes
        /// this session's live snapshot and the server's values the new diff
        /// baseline. The game-side values stay authoritative on the local copy;
        /// only identity/sync fields are restamped.</summary>
        private void AdoptForkedHead(LocalGameSave local, JObject staged, RemoteGameSave committed)
        {
            RecordLiveHash(committed.snapshotHash);
            liveSnapshotId = committed.snapshotId;
            liveBaseline = AsValuesObject(committed.values) is JObject serverValues
                ? (JObject)serverValues.DeepClone()
                : (JObject)staged.DeepClone();
            local.serverId = committed.serverId;
            local.snapshotId = committed.snapshotId;
            local.snapshotHash = committed.snapshotHash;
            local.snapshotName = committed.snapshotName;
            local.synchronizedAt = committed.synchronizedAt.EpochMilliseconds;
            RecordKnownLiveIdentity(
                committed.serverId,
                committed.snapshotId,
                committed.snapshotHash,
                local.synchronizedAt);
        }

        /// <summary>Transport-level flush failure: surface it, keep the dirty
        /// state staged (deltas compose into the next flush), and re-arm the
        /// throttle so a persistently failing transport backs off a full
        /// debounce window instead of hot-looping.</summary>
        private void HandleLiveFlushFailure(Exception exception)
        {
            OnCommitError?.Invoke(exception);
            Debug.LogWarning(
                $"[NeoCompose] Live flush for save \"{CustomId}\" failed; the staged changes " +
                $"compose into the next flush. {exception.GetType().Name}: {exception.Message}");
            ReArmLiveFlush();
        }

        private void ReArmLiveFlush()
        {
            var now = LiveClock();
            liveLastStagedAt = now;
            // Push the latency anchor forward too: it exists to cap stage
            // coalescing, not to force a hot loop against a failing transport.
            liveFirstDirtyAt = now;
        }

        private NeoLiveForkRequest BuildLiveForkRequest(
            LocalGameSave local, NeoSavePatch patch, string baseSnapshotId)
        {
            return new NeoLiveForkRequest
            {
                customId = string.IsNullOrEmpty(local.customId) ? CustomId : local.customId,
                liveSessionId = liveSessionId,
                baseSnapshotId = baseSnapshotId,
                version = local.version,
                patch = patch,
                platforms = local.platforms,
                systems = local.systems,
                inputDevices = local.inputDevices,
                updatedAt = local.updatedAt,
            };
        }

        /// <summary>
        /// Live handling of a pushed head. True when fully handled here:
        /// this session's own write echoing back (dropped), or a co-editor's
        /// patch of the session's live snapshot (merged + applied). False
        /// falls through to the classic <see cref="OnRemoteHeadChanged"/>
        /// divergence event.
        /// </summary>
        private bool TryHandleLiveRemoteHead(RemoteGameSave remote)
        {
            if (!LiveModeEnabled) return false;
            if (recentLiveHashes.Contains(remote.snapshotHash))
            {
                return true;
            }

            if (liveSnapshotId == null || remote.snapshotId != liveSnapshotId)
            {
                if (liveSnapshotId != null)
                {
                    // A newer session forked past ours; our snapshot froze. The
                    // next flush re-forks (stale base ⇒ typed conflict contract).
                    liveSnapshotId = null;
                }

                return false;
            }

            ApplyLiveRemoteValues(remote);
            return true;
        }

        /// <summary>
        /// A co-editor (e.g. the web tool) patched this session's live
        /// snapshot: merge the remote values into the active save with locally
        /// dirty keys winning until they flush, persist, and hand the merged
        /// content to the game via <see cref="OnLiveContentChanged"/>.
        /// </summary>
        private void ApplyLiveRemoteValues(RemoteGameSave remote)
        {
            var current = active;
            if (current == null) return;
            var remoteValues = AsValuesObject(remote.values);
            if (remoteValues == null)
            {
                Debug.LogWarning(
                    $"[NeoCompose] Live head push for save \"{CustomId}\" carried non-object " +
                    "values; skipping the apply.");
                return;
            }

            var merged = (JObject)remoteValues.DeepClone();
            var staged = stagedLive != null ? AsValuesObject(stagedLive.values) : null;
            if (staged != null && liveBaseline != null && liveFirstDirtyAt >= 0)
            {
                var dirty = BuildLivePatch(liveBaseline, staged);
                foreach (var pair in dirty.entries) merged[pair.Key] = pair.Value;
                foreach (var key in dirty.restoredToAuthored) merged.Remove(key);
            }

            liveBaseline = (JObject)remoteValues.DeepClone();
            current.values = new NeoSaveValues(merged);
            current.snapshotId = remote.snapshotId;
            current.snapshotHash = remote.snapshotHash;
            current.synchronizedAt = remote.synchronizedAt.EpochMilliseconds;
            current.liveFlushed = liveFirstDirtyAt < 0;
            RecordKnownLiveIdentity(
                current.serverId,
                remote.snapshotId,
                remote.snapshotHash,
                current.synchronizedAt);
            stagedLive = current;
            // Re-pushed identical state must not re-apply; the ring doubles as
            // an applied-push dedupe.
            RecordLiveHash(remote.snapshotHash);
            PersistLiveApplyQuietly(current);
            OnLiveContentChanged?.Invoke(JsonConvert.SerializeObject(current));
        }

        /// <summary>Best-effort persistence of an inbound live apply; a local
        /// write failure must not break the realtime stream.</summary>
        private async void PersistLiveApplyQuietly(LocalGameSave save)
        {
            try
            {
                await core.LocalStore.CommitSaveAsync(CustomId, JsonConvert.SerializeObject(save));
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[NeoCompose] Persisting an applied live edit for \"{CustomId}\" failed: " +
                    $"{exception.Message}");
            }
        }

        private void OnLiveConnectionStateChanged(NeoRealtimeConnectionState state)
        {
            if (state != NeoRealtimeConnectionState.Connected) return;
            if (liveFirstDirtyAt < 0) return;
            KickLiveFlushLoop();
        }

        private void RecordLiveHash(string snapshotHash)
        {
            if (string.IsNullOrEmpty(snapshotHash)) return;
            recentLiveHashes.Remove(snapshotHash);
            recentLiveHashes.Add(snapshotHash);
            if (recentLiveHashes.Count > RecentLiveHashCapacity)
            {
                recentLiveHashes.RemoveAt(0);
            }
        }

        /// <summary>Per-key diff: entries for added/changed keys, restores for
        /// keys the staged map no longer carries.</summary>
        private static NeoSavePatch BuildLivePatch(JObject baseline, JObject staged)
        {
            var patch = new NeoSavePatch();
            foreach (var property in staged.Properties())
            {
                if (!baseline.TryGetValue(property.Name, out var existing)
                    || !JToken.DeepEquals(existing, property.Value))
                {
                    patch.entries[property.Name] = property.Value;
                }
            }

            foreach (var property in baseline.Properties())
            {
                if (!staged.ContainsKey(property.Name))
                {
                    patch.restoredToAuthored.Add(property.Name);
                }
            }

            return patch;
        }

        /// <summary>The values map as a JSON object: a null token reads as an
        /// empty overlay; any other non-object shape is unpatchable (null).</summary>
        private static JObject? AsValuesObject(NeoSaveValues values)
        {
            if (values.Raw is JObject asObject) return asObject;
            if (values.IsNull) return new JObject();
            return null;
        }

        private static double DefaultLiveClock() =>
            System.Diagnostics.Stopwatch.GetTimestamp()
            / (double)System.Diagnostics.Stopwatch.Frequency;

        private static async Awaitable DefaultLiveDelay(double seconds)
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
        }

        /// <summary>Releases the realtime subscriptions and hooks, kicking one
        /// best-effort teardown flush first so a session's last edits go out
        /// when the transport is still up.</summary>
        public void Dispose()
        {
            if (!liveTornDown && liveFirstDirtyAt >= 0 && LiveModeEnabled)
            {
                var realtime = core.RealtimeProvider;
                if (realtime != null && realtime.CanCommit)
                {
                    KickTeardownFlush(realtime);
                }
            }

            liveTornDown = true;
            if (liveConnectionHook != null && core.RealtimeProvider != null)
            {
                core.RealtimeProvider.OnConnectionStateChanged -= liveConnectionHook;
                liveConnectionHook = null;
            }

            realtimeHeadSubscription?.Dispose();
            realtimeHeadSubscription = null;
        }

        /// <summary><c>async void</c> on purpose: Dispose cannot await, and the
        /// teardown flush is strictly best-effort.</summary>
        private async void KickTeardownFlush(INeoRealtimeProvider realtime)
        {
            try
            {
                await FlushLiveOnceAsync(realtime);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[NeoCompose] Teardown flush for save \"{CustomId}\" failed; the change " +
                    $"is in the local store and reconciles on the next load. " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        /// <summary>Archives the active save (cloud + list); raises <see cref="OnSaveArchived"/>.</summary>
        public async Awaitable ArchiveAsync()
        {
            if (core.ApiClient != null)
            {
                await core.ApiClient.ArchiveSaveAsync(CustomId);
            }

            await core.LocalStore.DeleteSaveAsync(CustomId);
            core.RecordArchivedSave(CustomId);
            realtimeHeadSubscription?.Dispose();
            realtimeHeadSubscription = null;
            OnSaveArchived?.Invoke(CustomId);
        }

        /// <summary>Archives a single snapshot (cloud only); raises <see cref="OnSnapshotArchived"/>.</summary>
        public async Awaitable ArchiveSnapshotAsync(string snapshotId)
        {
            if (core.ApiClient == null)
            {
                throw new InvalidOperationException(
                    "Snapshot archival requires cloud sync (no API client is configured).");
            }

            var updated = await core.ApiClient.ArchiveSnapshotAsync(CustomId, snapshotId);
            active = LocalGameSave.FromRemote(updated);
            OnSnapshotArchived?.Invoke(snapshotId);
        }

        /// <summary>
        /// Resolves the cloud head for a load: none when cloud sync is off; the head
        /// cached by a recent <c>RefreshListAsync</c> when it is still fresh (avoiding
        /// a redundant per-save network read right after the browse list loaded);
        /// otherwise a fresh, failure-tolerant fetch.
        /// </summary>
        private async Awaitable<RemoteGameSave?> ResolveRemoteForLoadAsync()
        {
            if (core.ApiClient == null) return null;
            if (core.TryGetFreshRemote(CustomId, out var cached)) return cached;
            return await SafeGetRemoteAsync(CustomId);
        }

        private async Awaitable<RemoteGameSave?> SafeGetRemoteAsync(string customId)
        {
            try
            {
                return await core.ApiClient!.GetSaveAsync(customId);
            }
            catch (Exception)
            {
                // Cloud unavailable — not signed in, missing, forbidden, or offline — is
                // not fatal to a local load: playing a local save never requires sign-in.
                // The cloud copy layers back in once the player signs in and reloads.
                return null;
            }
        }

        private string ResolveSaveName(string? existing)
        {
            if (!string.IsNullOrWhiteSpace(existing)) return existing!;
            if (!string.IsNullOrWhiteSpace(draftName)) return draftName!;
            var custom = core.Options.BuildSaveName?.Invoke();
            return string.IsNullOrWhiteSpace(custom)
                ? $"Save {DateTime.UtcNow:yyyy-MM-dd HH:mm}"
                : custom!;
        }

        private NeoSaveCommitRequest BuildCommitRequest(
            LocalGameSave local, string? baseSnapshotId, string? createAsLiveSessionId = null)
        {
            return new NeoSaveCommitRequest
            {
                customId = string.IsNullOrEmpty(local.customId) ? CustomId : local.customId,
                name = local.name,
                version = local.version,
                targetReleaseChannelId = core.TargetReleaseChannelId,
                values = local.values,
                platforms = local.platforms,
                systems = local.systems,
                inputDevices = local.inputDevices,
                createdAt = local.createdAt,
                updatedAt = local.updatedAt,
                baseSnapshotId = baseSnapshotId,
                snapshotName = local.snapshotName,
                liveSessionId = createAsLiveSessionId,
            };
        }
    }
}
