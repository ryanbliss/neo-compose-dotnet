// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using UnityEngine;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;

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
    public sealed class NeoSaveSynchronizer : INeoSaveLoader
    {
        private readonly InternalProjectStore core;
        private readonly bool isNewDraft;
        private readonly string? draftName;
        private LocalGameSave? active;

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
        /// Cloud commit path: send the local edit; resolve a 409 through
        /// <see cref="OnConflict"/>. Returns the committed remote head, or null when
        /// a best-effort cloud commit failed (the local commit still stands).
        /// </summary>
        private async Awaitable<RemoteGameSave?> CommitToCloudAsync(LocalGameSave local, bool replaceSnapshot)
        {
            NeoCommitResult result;
            try
            {
                result = await core.ApiClient!.CommitAsync(
                    BuildCommitRequest(local, active?.snapshotId), replaceSnapshot);
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
            var rebased = await core.ApiClient!.CommitAsync(
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

        /// <summary>Archives the active save (cloud + list); raises <see cref="OnSaveArchived"/>.</summary>
        public async Awaitable ArchiveAsync()
        {
            if (core.ApiClient != null)
            {
                await core.ApiClient.ArchiveSaveAsync(CustomId);
            }

            await core.LocalStore.DeleteSaveAsync(CustomId);
            core.RecordArchivedSave(CustomId);
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

        private NeoSaveCommitRequest BuildCommitRequest(LocalGameSave local, string? baseSnapshotId)
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
            };
        }
    }
}
