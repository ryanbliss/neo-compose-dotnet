// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
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
        /// How long a cloud save fetched by <see cref="RefreshListAsync"/> is trusted
        /// for a subsequent <c>Open</c>: within this window the cached cloud head is
        /// reused instead of issuing a per-save network read.
        /// </summary>
        public static readonly TimeSpan RemoteListFreshness = TimeSpan.FromMinutes(15);

        private readonly Dictionary<string, NeoSaveListEntry> saves = new();
        // The full cloud heads from the last list refresh (with values), so an Open
        // shortly after a list can load without a second network round trip.
        private readonly Dictionary<string, RemoteGameSave> remoteCache = new();
        private readonly Func<DateTimeOffset> now;
        private DateTimeOffset lastListRefreshAt = DateTimeOffset.MinValue;

        public InternalProjectStore(
            ProjectData schema,
            INeoLocalSaveStore localStore,
            INeoApiClient? apiClient,
            string targetReleaseChannelId,
            NeoSaveOptions? options = null,
            bool requireCloudCommit = false,
            NeoAuthentication? authentication = null,
            Func<DateTimeOffset>? now = null)
        {
            Schema = schema ?? throw new ArgumentNullException(nameof(schema));
            LocalStore = localStore ?? throw new ArgumentNullException(nameof(localStore));
            ApiClient = apiClient;
            TargetReleaseChannelId = targetReleaseChannelId ?? "";
            Options = options ?? new NeoSaveOptions();
            RequireCloudCommit = requireCloudCommit;
            Authentication = authentication;
            this.now = now ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Returns the cloud head cached by the most recent <see cref="RefreshListAsync"/>
        /// when that refresh is still within <see cref="RemoteListFreshness"/> — letting
        /// <c>Open</c> skip a redundant per-save network read. Returns false when stale
        /// or uncached, so the caller falls back to a fresh fetch.
        /// </summary>
        public bool TryGetFreshRemote(string customId, out RemoteGameSave remote)
        {
            remote = null!;
            if (now() - lastListRefreshAt > RemoteListFreshness) return false;
            return remoteCache.TryGetValue(customId, out remote!);
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

        public string ProjectId => Schema.metadata?.projectId ?? "";

        /// <summary>Raised whenever the save-list cache changes.</summary>
        public event Action? ListChanged;

        /// <summary>A snapshot of the current save list.</summary>
        public IReadOnlyList<NeoSaveListEntry> Saves => new List<NeoSaveListEntry>(saves.Values);

        public bool TryGetEntry(string customId, out NeoSaveListEntry entry) =>
            saves.TryGetValue(customId, out entry!);

        /// <summary>
        /// Rebuilds the save-list cache: local saves first, then merged with the
        /// cloud list (when an API client is present). Local-only saves stay
        /// listed; cloud saves bound to a different channel are flagged
        /// <see cref="NeoSaveListEntry.requiresClone"/>.
        /// </summary>
        public async Awaitable RefreshListAsync()
        {
            saves.Clear();
            remoteCache.Clear();

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
                        remoteCache[remote.id] = remote;
                        MergeRemote(remote, remoteList.RequiresClone(remote.id));
                    }
                    // Stamp freshness only on a successful cloud list, so a failed
                    // refresh never serves stale heads from a prior fetch.
                    lastListRefreshAt = now();
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
                snapshotHash = remote?.snapshotHash ?? local.snapshotHash,
                isLocalOnly = remote == null,
                existsRemotely = remote != null,
                requiresClone = false,
                needsMigration = false,
                archivedAt = remote?.archivedAt,
            };
            saves[customId] = entry;
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
                if (entry.existsRemotely && !remoteCache.ContainsKey(entry.customId))
                {
                    entry.existsRemotely = false;
                    entry.isLocalOnly = true;
                }
            }
        }

        private void MergeRemote(RemoteGameSave remote, bool requiresClone)
        {
            if (!saves.TryGetValue(remote.id, out var entry))
            {
                entry = new NeoSaveListEntry { customId = remote.id };
                saves[remote.id] = entry;
            }

            entry.name = remote.name;
            entry.releaseChannelId = remote.releaseChannelId;
            entry.snapshotHash = remote.snapshotHash;
            entry.existsRemotely = true;
            entry.isLocalOnly = false;
            entry.requiresClone = requiresClone;
            entry.needsMigration = !remote.TryDeserializeValues(out _);
            entry.archivedAt = remote.archivedAt;
        }
    }
}
