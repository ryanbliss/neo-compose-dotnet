// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// The shared, non-public core held by both the list-managing
    /// <see cref="NeoProjectStore"/> and every active-file
    /// <see cref="NeoSaveSynchronizer"/> it creates. Owning the project schema, the
    /// save-list cache, and the persistence seams in one place is what keeps the
    /// browse view and the active file from desyncing: a commit through a
    /// synchronizer upserts the same list the project store reads.
    /// </summary>
    public sealed class InternalProjectStore
    {
        /// <summary>
        /// How long a full cloud head fetched by a detail query or realtime-head
        /// subscription is trusted for a subsequent <c>Open</c>. Payload-light
        /// list rows are never inserted into this cache.
        /// </summary>
        public static readonly TimeSpan RemoteListFreshness = TimeSpan.FromMinutes(15);

        private readonly Dictionary<string, NeoSaveListEntry> saves = new();
        private readonly Dictionary<string, (RemoteGameSave save, DateTimeOffset cachedAt)>
            remoteDetailCache = new();
        private readonly HashSet<string> listedRemoteIds = new();
        private readonly Func<DateTimeOffset> now;
        private IDisposable? realtimeListSubscription;

        public InternalProjectStore(
            ProjectData schema,
            INeoLocalSaveStore localStore,
            INeoApiClient? apiClient,
            string targetReleaseChannelId,
            NeoSaveOptions? options = null,
            bool requireCloudCommit = false,
            NeoAuthentication? authentication = null,
            Func<DateTimeOffset>? now = null,
            INeoRealtimeProvider? realtimeProvider = null)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
            LocalStore = localStore ?? throw new ArgumentNullException(nameof(localStore));
            ApiClient = apiClient;
            TargetReleaseChannelId = targetReleaseChannelId ?? "";
            Options = options ?? new NeoSaveOptions();
            RequireCloudCommit = requireCloudCommit;
            Authentication = authentication;
            this.now = now ?? (() => DateTimeOffset.UtcNow);
            RealtimeProvider = realtimeProvider;
        }

        /// <summary>
        /// Returns a full cloud head cached by a detail/realtime-head path. Summary
        /// rows from list queries deliberately cannot satisfy this method.
        /// </summary>
        public bool TryGetFreshRemote(string customId, out RemoteGameSave remote)
        {
            remote = null!;
            if (!remoteDetailCache.TryGetValue(customId, out var cached)) return false;
            if (now() - cached.cachedAt > RemoteListFreshness)
            {
                remoteDetailCache.Remove(customId);
                return false;
            }
            remote = cached.save;
            return true;
        }

        public ProjectData Schema { get; }
        public INeoLocalSaveStore LocalStore { get; }
        public INeoApiClient? ApiClient { get; }
        public string TargetReleaseChannelId { get; }
        public NeoSaveOptions Options { get; }
        public bool RequireCloudCommit { get; }

        /// <summary>
        /// The runtime authentication backing cloud sync, when one was wired (e.g. by
        /// the <see cref="NeoProjectStore"/> constructor's config master switch).
        /// Surfaced so the generated client can expose an <c>Authentication</c>
        /// accessor; null for local-only stores.
        /// </summary>
        public NeoAuthentication? Authentication { get; }

        /// <summary>True when cloud sync is active (an API client is present).</summary>
        public bool CloudEnabled => ApiClient != null;

        /// <summary>
        /// The optional realtime transport (see
        /// <c>specs/convex-realtime-sync.md</c>); null in REST/local-only builds.
        /// </summary>
        public INeoRealtimeProvider? RealtimeProvider { get; }

        /// <summary>
        /// (Re)attaches the live save-list subscription. A no-op unless the
        /// provider is currently connected; safe to call on every Connected
        /// transition (the previous subscription is replaced).
        /// </summary>
        internal void AttachRealtimeSubscriptions()
        {
            realtimeListSubscription?.Dispose();
            realtimeListSubscription = null;
            if (RealtimeProvider == null) return;
            if (RealtimeProvider.State != NeoRealtimeConnectionState.Connected) return;
            realtimeListSubscription = RealtimeProvider.SubscribeSaveList(
                TargetReleaseChannelId, ApplyRealtimeSaveList);
        }

        internal void DetachRealtimeSubscriptions()
        {
            realtimeListSubscription?.Dispose();
            realtimeListSubscription = null;
        }

        /// <summary>
        /// Applies a pushed cloud save list: the same merge + freshness + orphan
        /// reconciliation a successful <see cref="RefreshListAsync"/> cloud fetch
        /// performs, without touching the local entries.
        /// </summary>
        internal void ApplyRealtimeSaveList(NeoSaveFileList remoteList)
        {
            listedRemoteIds.Clear();
            foreach (var remote in remoteList.saves)
            {
                listedRemoteIds.Add(remote.id);
                EvictMovedDetail(remote);
                MergeRemote(remote, remoteList.RequiresClone(remote.id));
            }

            EvictUnlistedDetails();
            ReconcileOrphanedLocalSaves();
            ListChanged?.Invoke();
        }

        /// <summary>
        /// Applies a pushed cloud head for one save: primes the fresh-remote
        /// cache (so the next load skips the per-save fetch) and updates the
        /// browse list entry.
        /// </summary>
        internal void RecordRealtimeRemoteHead(RemoteGameSave remote)
        {
            remoteDetailCache[remote.id] = (remote, now());
            listedRemoteIds.Add(remote.id);
            MergeRemote(remote, remote.releaseChannelId != TargetReleaseChannelId);
            ListChanged?.Invoke();
        }

        public string ProjectId => Schema.metadata?.projectId ?? "";

        /// <summary>Raised whenever the save-list cache changes.</summary>
        public event Action? ListChanged;

        /// <summary>A snapshot of the current save list.</summary>
        public IReadOnlyList<NeoSaveListEntry> Saves => new List<NeoSaveListEntry>(saves.Values);

        public bool TryGetEntry(string customId, out NeoSaveListEntry entry) =>
            saves.TryGetValue(customId, out entry!);

        /// <summary>
        /// Starts one clone request, then follows a durable large-snapshot copy
        /// by the destination id returned from that request. The clone mutation
        /// is never repeated: doing so would allocate duplicate save files.
        /// </summary>
        internal async Awaitable<RemoteGameSave> CloneSaveToReadyAsync(
            string sourceCustomId,
            NeoCloneRequest request)
        {
            if (ApiClient == null)
            {
                throw new InvalidOperationException(
                    "Cloud clone polling requires an API client.");
            }

            var accepted = await ApiClient.CloneSaveAsync(sourceCustomId, request);
            if (!accepted.IsTransitioning)
            {
                return accepted.ClonedSave
                    ?? throw new InvalidOperationException(
                        "Neo Compose reported a completed clone without a save.");
            }

            while (true)
            {
                var status = await ApiClient.GetSaveTransitionStatusAsync(
                    accepted.CustomId);
                if (status.Outcome == NeoSaveTransitionOutcome.Ready)
                {
                    var ready = status.ReadySave
                        ?? throw new InvalidOperationException(
                            "Neo Compose reported a ready clone without a save.");
                    RequireMatchingCloneTransition(accepted, ready.id, ready.snapshotId);
                    return ready;
                }

                RequireMatchingCloneTransition(
                    accepted, status.CustomId, status.TargetSnapshotId);
                if (status.Outcome == NeoSaveTransitionOutcome.Failed)
                {
                    var detail = string.IsNullOrWhiteSpace(status.Error)
                        ? "The server did not provide an error."
                        : status.Error;
                    throw new InvalidOperationException(
                        $"Neo Compose clone transition for save \"{accepted.CustomId}\" " +
                        $"and snapshot \"{accepted.TargetSnapshotId}\" failed. {detail}");
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }

        private static void RequireMatchingCloneTransition(
            NeoCloneResult accepted,
            string customId,
            string targetSnapshotId)
        {
            if (customId != accepted.CustomId
                || targetSnapshotId != accepted.TargetSnapshotId)
            {
                throw new InvalidOperationException(
                    "Neo Compose clone status did not match the accepted destination " +
                    $"'{accepted.CustomId}' / '{accepted.TargetSnapshotId}'.");
            }
        }

        /// <summary>
        /// Rebuilds the save-list cache: local saves first, then merged with the
        /// cloud list (when an API client is present). Local-only saves stay
        /// listed; cloud saves bound to a different channel are flagged
        /// <see cref="NeoSaveListEntry.requiresClone"/>.
        /// </summary>
        public async Awaitable RefreshListAsync()
        {
            saves.Clear();
            listedRemoteIds.Clear();

            var localIds = await LocalStore.ListSaveIdsAsync();
            foreach (var customId in localIds)
            {
                var content = await LocalStore.LoadSaveAsync(customId);
                if (!LocalGameSaveLoader.TryLoad(content, out var local)) continue;
                saves[customId] = EntryFromLocal(local, customId);
            }

            if (ApiClient != null)
            {
                bool cloudListed = false;
                try
                {
                    var remoteList = await ApiClient.ListSavesAsync(TargetReleaseChannelId);
                    foreach (var remote in remoteList.saves)
                    {
                        listedRemoteIds.Add(remote.id);
                        EvictMovedDetail(remote);
                        MergeRemote(remote, remoteList.RequiresClone(remote.id));
                    }
                    EvictUnlistedDetails();
                    cloudListed = true;
                }
                catch (Exception ex)
                {
                    // Cloud unavailable (not signed in / offline) — show local saves only;
                    // cloud saves layer in once the player signs in and the list refreshes.
                    // Still surface why: a silently-empty cloud list is indistinguishable
                    // from "no cloud saves" and hides real auth/network failures.
                    Debug.LogWarning(
                        $"[NeoCompose] Could not load the cloud save list (showing local saves " +
                        $"only). {ex.GetType().Name}: {ex.Message}");
                }

                // Reconcile only against a successful cloud list — a failed fetch
                // can't distinguish "deleted server-side" from "offline", so it must
                // not downgrade anything.
                if (cloudListed)
                {
                    ReconcileOrphanedLocalSaves();
                }
            }

            ListChanged?.Invoke();
        }

        /// <summary>
        /// Records a just-saved file into the list (after a synchronizer commit),
        /// keeping the browse view in sync with the active file.
        /// </summary>
        public void RecordSavedFile(LocalGameSave local, RemoteGameSave? remote)
        {
            var customId = local.customId;
            var entry = new NeoSaveListEntry
            {
                customId = customId,
                name = local.name,
                releaseChannelId = remote?.releaseChannelId ?? local.releaseChannelId,
                snapshotRevision = remote?.snapshotRevision ?? local.snapshotRevision,
                snapshotHash = remote?.snapshotHash ?? local.snapshotHash,
                isLocalOnly = remote == null,
                existsRemotely = remote != null,
                requiresClone = false,
                needsMigration = false,
                archivedAt = remote?.archivedAt,
            };
            saves[customId] = entry;
            if (remote != null)
            {
                remoteDetailCache[customId] = (remote, now());
                listedRemoteIds.Add(customId);
            }
            ListChanged?.Invoke();
        }

        /// <summary>Marks a save archived in the list (or drops it when only local).</summary>
        public void RecordArchivedSave(string customId)
        {
            if (saves.TryGetValue(customId, out var entry))
            {
                entry.archivedAt = NeoTimestamp.Now().EpochMilliseconds;
            }

            ListChanged?.Invoke();
        }

        private NeoSaveListEntry EntryFromLocal(LocalGameSave local, string customId)
        {
            var channel = string.IsNullOrEmpty(local.releaseChannelId)
                ? TargetReleaseChannelId
                : local.releaseChannelId;
            return new NeoSaveListEntry
            {
                customId = customId,
                name = local.name,
                releaseChannelId = channel,
                snapshotRevision = local.snapshotRevision,
                snapshotHash = local.snapshotHash,
                isLocalOnly = local.IsLocalOnly,
                existsRemotely = !local.IsLocalOnly,
                requiresClone = channel != TargetReleaseChannelId,
                needsMigration = !local.TryDeserializeValues(out _),
                archivedAt = null,
            };
        }

        /// <summary>
        /// After a successful cloud list, downgrades any local save that claims a
        /// cloud copy (<see cref="NeoSaveListEntry.existsRemotely"/>) but was absent
        /// from the fetched list — it was deleted server-side while a stale copy
        /// lingered locally. Without this, such a save keeps reading as "synced"
        /// and the delete path tries to archive a cloud copy that no longer exists.
        /// Saves on a different channel still appear in the list (with
        /// <c>requiresClone</c>), so they are correctly left alone.
        /// </summary>
        private void ReconcileOrphanedLocalSaves()
        {
            foreach (var entry in saves.Values)
            {
                if (entry.existsRemotely && !listedRemoteIds.Contains(entry.customId))
                {
                    entry.existsRemotely = false;
                    entry.isLocalOnly = true;
                }
            }
        }

        private void MergeRemote(RemoteGameSaveSummary remote, bool requiresClone)
        {
            if (!saves.TryGetValue(remote.id, out var entry))
            {
                entry = new NeoSaveListEntry { customId = remote.id };
                saves[remote.id] = entry;
            }

            entry.name = remote.name;
            entry.releaseChannelId = remote.releaseChannelId;
            entry.snapshotRevision = remote.snapshotRevision;
            entry.snapshotHash = remote.snapshotHash;
            entry.existsRemotely = true;
            entry.isLocalOnly = false;
            entry.requiresClone = requiresClone;
            // Compatibility needs the payload and is resolved when the save is
            // opened through the full-detail endpoint.
            entry.needsMigration = false;
            entry.archivedAt = remote.archivedAt;
        }

        private void MergeRemote(RemoteGameSave remote, bool requiresClone)
        {
            MergeRemote(RemoteGameSaveSummary.FromRemote(remote), requiresClone);
        }

        private void EvictMovedDetail(RemoteGameSaveSummary summary)
        {
            if (remoteDetailCache.TryGetValue(summary.id, out var cached)
                && (cached.save.snapshotId != summary.snapshotId
                    || cached.save.snapshotRevision != summary.snapshotRevision))
            {
                remoteDetailCache.Remove(summary.id);
            }
        }

        private void EvictUnlistedDetails()
        {
            var unlistedIds = new List<string>();
            foreach (string id in remoteDetailCache.Keys)
            {
                if (!listedRemoteIds.Contains(id)) unlistedIds.Add(id);
            }
            foreach (string id in unlistedIds)
            {
                remoteDetailCache.Remove(id);
            }
        }
    }
}
