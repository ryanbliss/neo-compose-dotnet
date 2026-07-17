// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

            return await WaitForSaveTransitionReadyAsync(
                accepted.CustomId, accepted.TargetSnapshotId);
        }

        internal async Awaitable<RemoteGameSave> WaitForSaveTransitionReadyAsync(
            string customId,
            string targetSnapshotId)
        {
            if (ApiClient == null)
            {
                throw new InvalidOperationException(
                    "Cloud transition polling requires an API client.");
            }
            while (true)
            {
                var status = await ApiClient.GetSaveTransitionStatusAsync(
                    customId);
                if (status.Outcome == NeoSaveTransitionOutcome.Ready)
                {
                    var ready = status.ReadySave
                        ?? throw new InvalidOperationException(
                            "Neo Compose reported a ready transition without a save.");
                    RequireMatchingTransition(
                        customId, targetSnapshotId, ready.id, ready.snapshotId);
                    return ready;
                }

                RequireMatchingTransition(
                    customId,
                    targetSnapshotId,
                    status.CustomId,
                    status.TargetSnapshotId);
                if (status.Outcome == NeoSaveTransitionOutcome.Failed)
                {
                    throw BuildTransitionFailure(
                        customId, targetSnapshotId, status.Error);
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }
        }

        /// <summary>
        /// Creates a large local artifact through the hidden begin/append/complete
        /// protocol. The deterministic upload plan maps one bounded batch to one
        /// snapshot revision, so replayed begin responses can resume without ever
        /// exposing a pending snapshot's manifest.
        /// </summary>
        internal async Awaitable<RemoteGameSave> CreateSaveInChunksAsync(
            NeoChunkedCreateRequest request,
            IReadOnlyList<GameSaveRecordChange> changes)
        {
            if (ApiClient == null)
            {
                throw new InvalidOperationException(
                    "Chunked cloud creation requires an API client.");
            }

            var batches = BuildChunkedCreateBatches(changes);
            request.uploadFingerprint = BuildChunkedCreateFingerprint(request, batches);

            NeoChunkedCreateTarget target;
            try
            {
                target = await ApiClient.BeginChunkedCreateAsync(request);
            }
            catch
            {
                // BEGIN is keyed by the upload fingerprint. If the first response
                // was lost, this exact replay returns the same hidden target.
                target = await ApiClient.BeginChunkedCreateAsync(request);
            }

            if (target.customId != request.customId)
            {
                throw new InvalidOperationException(
                    "Neo Compose accepted a chunked create under a different save id.");
            }
            if (target.snapshotRevision < 0
                || target.snapshotRevision > batches.Count)
            {
                throw new InvalidOperationException(
                    "Neo Compose chunked create revision does not match the upload plan.");
            }

            for (var batchIndex = (int)target.snapshotRevision;
                 batchIndex < batches.Count;
                 batchIndex++)
            {
                var chunk = batches[batchIndex];
                NeoLivePatchResult result;
                try
                {
                    result = await ApiClient.AppendChunkedCreateAsync(
                        target.customId,
                        target.resumeToken,
                        batchIndex,
                        chunk,
                        request.updatedAt);
                }
                catch
                {
                    // APPEND makes an exact immediate replay idempotent. Retain
                    // both the batch and base revision after a lost response.
                    result = await ApiClient.AppendChunkedCreateAsync(
                        target.customId,
                        target.resumeToken,
                        batchIndex,
                        chunk,
                        request.updatedAt);
                }
                if (result.IsConflict)
                {
                    throw new InvalidOperationException(
                        "A fresh chunked save create reported a record conflict.");
                }
                if (result.IsStaleTarget
                    || result.SnapshotId != target.snapshotId
                    || result.SnapshotRevision != batchIndex + 1)
                {
                    throw new InvalidOperationException(
                        "A chunked save append returned an unexpected snapshot revision.");
                }
            }

            try
            {
                return await ApiClient.CompleteChunkedCreateAsync(
                    target.customId, target.resumeToken);
            }
            catch
            {
                // COMPLETE is idempotent, including after activation. Retry the
                // same resume token instead of consulting a copy-status endpoint.
                return await ApiClient.CompleteChunkedCreateAsync(
                    target.customId, target.resumeToken);
            }
        }

        private static List<List<GameSaveRecordChange>> BuildChunkedCreateBatches(
            IReadOnlyList<GameSaveRecordChange> changes)
        {
            var values = new Dictionary<string, GameSaveValueReplaceChange>(
                StringComparer.Ordinal);
            var bindings = new Dictionary<string, GameSaveStaticBindingSetChange>(
                StringComparer.Ordinal);
            foreach (var change in changes)
            {
                switch (change)
                {
                    case GameSaveValueReplaceChange value
                        when value.baseRecordStateId == null
                             && value.baseRecordRevisionToken == null:
                        if (!values.TryAdd(value.valueId, value))
                        {
                            throw new InvalidOperationException(
                                $"Chunked create repeats value record \"{value.valueId}\".");
                        }
                        break;
                    case GameSaveStaticBindingSetChange binding
                        when binding.baseRecordStateId == null
                             && binding.baseRecordRevisionToken == null:
                        if (!bindings.TryAdd(binding.memberId, binding))
                        {
                            throw new InvalidOperationException(
                                $"Chunked create repeats static binding \"{binding.memberId}\".");
                        }
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Chunked create accepts only base-free value.replace and " +
                            "static-binding.set changes.");
                }
            }

            var ordered = new List<GameSaveRecordChange>(changes.Count);
            var remaining = new SortedSet<string>(values.Keys, StringComparer.Ordinal);
            var emitted = new HashSet<string>(StringComparer.Ordinal);
            while (remaining.Count != 0)
            {
                var ready = remaining.Where(valueId =>
                {
                    var value = values[valueId].value as JObject;
                    var containerId = value?["containerId"]?.Value<string>();
                    return string.IsNullOrEmpty(containerId)
                        || !values.ContainsKey(containerId)
                        || emitted.Contains(containerId);
                }).ToList();
                if (ready.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Chunked create value ownership contains a cycle.");
                }
                foreach (var valueId in ready)
                {
                    remaining.Remove(valueId);
                    emitted.Add(valueId);
                    ordered.Add(values[valueId]);
                }
            }
            ordered.AddRange(bindings.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => pair.Value));

            var batches = new List<List<GameSaveRecordChange>>();
            for (var index = 0; index < ordered.Count; index += 64)
            {
                batches.Add(ordered.GetRange(index, Math.Min(64, ordered.Count - index)));
            }
            return batches;
        }

        private static string BuildChunkedCreateFingerprint(
            NeoChunkedCreateRequest request,
            IReadOnlyList<List<GameSaveRecordChange>> batches)
        {
            var metadata = JObject.FromObject(request);
            metadata.Remove(nameof(request.uploadFingerprint));
            var serializedBatches = new JArray(
                batches.Select(batch => JArray.FromObject(batch)));
            var plan = new JObject
            {
                ["protocol"] = "neo-chunked-create-v1",
                ["metadata"] = metadata,
                ["batches"] = serializedBatches,
            };
            var canonical = NeoSemanticJson.Canonicalize(plan)
                .ToString(Formatting.None);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(canonical));
            var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return "sha256:" + hex;
        }

        private static InvalidOperationException BuildTransitionFailure(
            string customId,
            string snapshotId,
            string? error)
        {
            var detail = string.IsNullOrWhiteSpace(error)
                ? "The server did not provide an error."
                : error;
            return new InvalidOperationException(
                $"Neo Compose transition for save \"{customId}\" and snapshot " +
                $"\"{snapshotId}\" failed. {detail}");
        }

        private static void RequireMatchingTransition(
            string expectedCustomId,
            string expectedTargetSnapshotId,
            string actualCustomId,
            string actualTargetSnapshotId)
        {
            if (actualCustomId != expectedCustomId
                || actualTargetSnapshotId != expectedTargetSnapshotId)
            {
                throw new InvalidOperationException(
                    "Neo Compose transition status did not match the accepted destination " +
                    $"'{expectedCustomId}' / '{expectedTargetSnapshotId}'.");
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
