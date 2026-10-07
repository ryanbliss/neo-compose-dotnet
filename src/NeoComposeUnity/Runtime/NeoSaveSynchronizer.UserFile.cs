// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using UnityEngine;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;

namespace NeoCompose.Runtime
{
    /// <summary>The kind of file a <see cref="NeoSaveSynchronizer"/> synchronizes.</summary>
    public enum NeoSaveFileKind
    {
        Save,

        /// <summary>The player's user file: one per channel, never archived, cloned or deleted by the game (P104 §4.4).</summary>
        User,
    }

    public sealed partial class NeoSaveSynchronizer
    {
        // P104 §4.4: the user file reuses the save paths, and differs in four
        // places so it never blocks the main menu: another account's or
        // channel's file gives way, conflicts resolve without a handler, a
        // server deletion loads defaults, and unreadable content is set aside.

        internal const string UserKey = "user";
        internal const string UnreadableUserKey = "user.unreadable";
        internal const string UserFileName = "User";
        internal const string UserFileKind = "user";

        // The next capture diffs the whole file: the baseline holds rows the
        // loaded content dropped (a migration) or another head's rows (a
        // rebase), which no write marked.
        private bool fullDiffPending;
        private NeoClient? userClient;

        /// <summary>A user synchronizer. It exists before the schema loads, so handlers attach first; <see cref="Bind"/> supplies the store.</summary>
        internal NeoSaveSynchronizer(NeoSaveFileKind kind)
        {
            Kind = kind;
            core = null!;
            CustomId = Guid.NewGuid().ToString();
            State = NeoSaveSynchronizerState.Idle;
        }

        internal void Bind(InternalProjectStore core) =>
            this.core = core ?? throw new ArgumentNullException(nameof(core));

        internal InternalProjectStore Core => core;

        internal bool IsBound => core != null;

        internal void BindUserClient(NeoClient client) => userClient = client;

        private Awaitable<string?> LoadLocalAsync() =>
            Kind == NeoSaveFileKind.User
                ? core.LocalStore.LoadUserAsync(UserKey)
                : core.LocalStore.LoadSaveAsync(CustomId);

        private Awaitable CommitLocalAsync(string content) =>
            Kind == NeoSaveFileKind.User
                ? core.LocalStore.CommitUserAsync(UserKey, content)
                : core.LocalStore.CommitSaveAsync(CustomId, content);

        private void ThrowIfUserFile(string verb)
        {
            if (Kind == NeoSaveFileKind.User)
                throw new InvalidOperationException($"The user file can't be {verb}.");
        }

        /// <summary>
        /// Asks <see cref="OnConflict"/>. Without a handler a save throws
        /// <paramref name="unresolvedMessage"/>, and a user file keeps the
        /// newer head.
        /// </summary>
        private async Awaitable<NeoSaveConflictResolution> ResolveConflictAsync(
            LocalGameSave local,
            RemoteGameSave remote,
            string unresolvedMessage)
        {
            if (OnConflict == null)
            {
                if (Kind == NeoSaveFileKind.Save)
                    throw new NeoSaveConflictUnresolvedException(unresolvedMessage);
                return local.updatedAt.EpochMilliseconds > remote.updatedAt.EpochMilliseconds
                    ? NeoSaveConflictResolution.KeepLocal
                    : NeoSaveConflictResolution.KeepRemote;
            }

            State = NeoSaveSynchronizerState.Resolving;
            var continuation = new NeoSaveConflictContinuation();
            OnConflict.Invoke(new NeoSaveConflict(local, remote), continuation);
            return await continuation.Completion;
        }

        /// <summary>The cloud query's outcome. A failure never counts as missing: it can't tell deleted from offline.</summary>
        internal sealed class UserFileQuery
        {
            internal static readonly UserFileQuery Failed = new(null, missing: false);
            internal static readonly UserFileQuery Missing = new(null, missing: true);

            private UserFileQuery(RemoteGameSave? file, bool missing)
            {
                File = file;
                IsMissing = missing;
            }

            internal static UserFileQuery Found(RemoteGameSave file) => new(file, missing: false);

            internal RemoteGameSave? File
            {
                get;
            }

            internal bool IsMissing
            {
                get;
            }

            internal bool IsFailed => File == null && !IsMissing;
        }

        /// <summary>Queries the caller's user file for <paramref name="releaseChannelId"/>. Never throws.</summary>
        internal static async Awaitable<UserFileQuery> QueryUserFileAsync(
            INeoApiClient? apiClient,
            NeoAuthentication? authentication,
            string releaseChannelId)
        {
            if (apiClient == null || authentication is { IsSignedIn: false })
                return UserFileQuery.Failed;
            try
            {
                return UserFileQuery.Found(await apiClient.GetUserFileAsync(releaseChannelId));
            }
            catch (NeoComposeNotFoundException)
            {
                return UserFileQuery.Missing;
            }
            catch (Exception exception)
            {
                if (exception is not NeoComposeNotSignedInException)
                    Debug.LogWarning(
                        "[NeoCompose] Could not load the cloud user file; keeping the local copy. " +
                        $"{exception.GetType().Name}: {exception.Message}");
                return UserFileQuery.Failed;
            }
        }

        private enum UserFileChoice
        {
            Local,
            Remote,
            InSync,
            Defaults,
        }

        /// <summary>The user file rules of §4.4, shared by load and sign-in.</summary>
        private async Awaitable<UserFileChoice> ChooseUserFileAsync(LocalGameSave? local, UserFileQuery query)
        {
            if (query.IsFailed)
                return local == null ? UserFileChoice.Defaults : UserFileChoice.Local;
            if (query.File is not { } remote)
            {
                // Gone, or another account's: only a never-uploaded copy stays.
                return local is { IsLocalOnly: true } ? UserFileChoice.Local : UserFileChoice.Defaults;
            }
            if (local == null || (!local.IsLocalOnly && local.serverId != remote.serverId))
                return UserFileChoice.Remote;
            if (local.snapshotId == remote.snapshotId
                && (local.snapshotRevision == remote.snapshotRevision
                    || (local.liveFlushed && !string.IsNullOrEmpty(remote.liveSessionId))))
                return UserFileChoice.InSync;
            var resolution = await ResolveConflictAsync(local, remote, "");
            return resolution == NeoSaveConflictResolution.KeepRemote
                ? UserFileChoice.Remote
                : UserFileChoice.Local;
        }

        /// <summary>
        /// Loads the user file (§4.1, §4.4): the local copy, then the cloud
        /// query's answer. Returns the content to build the user client from,
        /// or null for authored defaults.
        /// </summary>
        internal async Awaitable<string?> LoadUserContentAsync(Awaitable<UserFileQuery>? query)
        {
            State = NeoSaveSynchronizerState.Loading;
            try
            {
                LocalGameSave? local = null;
                string? localContent = await LoadLocalAsync();
                if (!string.IsNullOrWhiteSpace(localContent))
                {
                    if (LocalGameSaveLoader.TryLoad(localContent, out var parsed))
                    {
                        local = parsed;
                    }
                    else
                    {
                        await core.LocalStore.CommitUserAsync(UnreadableUserKey, localContent!);
                        Debug.LogWarning(
                            $"[NeoCompose] The local user file could not be parsed; moved it to \"{UnreadableUserKey}\".");
                    }
                }
                // Another channel's copy counts as absent for this load.
                if (local != null
                    && !string.IsNullOrEmpty(local.releaseChannelId)
                    && local.releaseChannelId != core.TargetReleaseChannelId)
                    local = null;

                var answer = query == null ? UserFileQuery.Failed : await query;
                var choice = await ChooseUserFileAsync(local, answer);
                LocalGameSave? baseline = choice switch
                {
                    UserFileChoice.Local => local,
                    UserFileChoice.Defaults => null,
                    _ => LocalGameSave.FromRemote(answer.File!),
                };
                LocalGameSave? loaded = baseline;
                if (choice == UserFileChoice.Local && answer.File != null)
                {
                    // Keep the local values as a new head on the cloud file.
                    baseline = LocalGameSave.FromRemote(answer.File);
                    loaded = RebasedOnto(local!, answer.File);
                    fullDiffPending = true;
                }

                if (loaded == null)
                {
                    if (local != null && !local.IsLocalOnly)
                    {
                        // The server deleted the file: drop the copy and its server id.
                        await CommitLocalAsync(JsonConvert.SerializeObject(DefaultUserFile()));
                    }
                    CustomId = Guid.NewGuid().ToString();
                    active = null;
                    uncapturedDirty = new DirtyRecords();
                    SettleStagedDirty();
                    State = NeoSaveSynchronizerState.Ready;
                    return null;
                }

                if (!loaded.TryDeserializeValues(out _))
                {
                    string unreadable = JsonConvert.SerializeObject(loaded);
                    string? migrated = OnMigrationRequired == null
                        ? null
                        : await ApplyMigrationIfNeededAsync(unreadable);
                    if (migrated != null && LocalGameSaveLoader.TryLoad(migrated, out var migratedSave))
                    {
                        loaded = migratedSave;
                    }
                    else
                    {
                        // Set the content aside and load defaults under the
                        // same identity, so the next commit is a new head.
                        await core.LocalStore.CommitUserAsync(UnreadableUserKey, unreadable);
                        loaded = LocalGameSaveLoader.Load(unreadable);
                        loaded.values = NeoSaveValues.Empty;
                        loaded.valuePartitions = null;
                        loaded.staticBindings = new();
                        loaded.changeListeners = null;
                    }
                    fullDiffPending = true;
                }
                else if (choice != UserFileChoice.Local)
                {
                    // Keep the copy on this device in step with the account.
                    await CommitLocalAsync(JsonConvert.SerializeObject(loaded));
                }

                CustomId = loaded.customId;
                active = baseline;
                uncapturedDirty = new DirtyRecords();
                SettleStagedDirty();
                State = NeoSaveSynchronizerState.Ready;
                ResetLiveSessionBasis(baseline!);
                if (!baseline!.IsLocalOnly)
                    AttachRealtimeHead();
                OnLoaded?.Invoke(loaded);
                return JsonConvert.SerializeObject(loaded);
            }
            catch
            {
                State = NeoSaveSynchronizerState.Error;
                throw;
            }
        }

        /// <summary><paramref name="local"/>'s content under <paramref name="remote"/>'s identity.</summary>
        private static LocalGameSave RebasedOnto(LocalGameSave local, RemoteGameSave remote)
        {
            var rebased = LocalGameSaveLoader.Load(JsonConvert.SerializeObject(local));
            rebased.customId = remote.id;
            rebased.serverId = remote.serverId;
            rebased.snapshotId = remote.snapshotId;
            rebased.snapshotRevision = remote.snapshotRevision;
            rebased.synchronizedAt = remote.synchronizedAt.EpochMilliseconds;
            rebased.liveSessionId = remote.liveSessionId;
            rebased.recordCache = remote.recordCache;
            rebased.liveFlushed = false;
            return rebased;
        }

        private LocalGameSave DefaultUserFile()
        {
            var now = NeoTimestamp.Now();
            return new LocalGameSave
            {
                customId = Guid.NewGuid().ToString(),
                releaseChannelId = core.TargetReleaseChannelId,
                name = UserFileName,
                projectId = core.ProjectId,
                createdAt = now,
                updatedAt = now,
            };
        }

        /// <summary>
        /// Loads defaults once the server confirms the file is gone. A failed
        /// check proves nothing, so it changes nothing.
        /// </summary>
        private async void DropUserFileIfGone()
        {
            string customId = CustomId;
            if (active is not { IsLocalOnly: false } || core.ApiClient == null)
                return;
            try
            {
                await core.ApiClient.GetSaveAsync(customId);
                return;
            }
            catch (NeoComposeNotFoundException)
            {
            }
            catch (Exception)
            {
                return;
            }
            ReplaceUserContentLater(DefaultUserFile(), customId);
        }

        /// <summary>
        /// Replaces the running user client's content on its next commit turn.
        /// Never awaited by a commit or a revision drain, which hold that turn.
        /// </summary>
        private async void ReplaceUserContentLater(LocalGameSave content, string? expectedCustomId = null)
        {
            var client = userClient;
            if (client == null)
                return;
            try
            {
                await client.RunCommitTurnAsync(async () =>
                {
                    if (expectedCustomId == null || CustomId == expectedCustomId)
                        await ApplyUserReplacementAsync(client, content);
                });
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[NeoCompose] Could not replace the user file's content. " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        /// <summary>Adopts <paramref name="content"/> in place. The caller holds the commit turn.</summary>
        private async Awaitable ApplyUserReplacementAsync(NeoClient client, LocalGameSave content)
        {
            string serialized = JsonConvert.SerializeObject(content);
            CustomId = content.customId;
            active = content.IsLocalOnly ? null : content;
            unsyncedActive = null;
            pendingRealtimeRevision = null;
            fullDiffPending = false;
            client.ReplacePersistedContent(serialized);
            ResetLiveSessionBasis(content);
            SettleStagedDirty();
            if (content.IsLocalOnly)
            {
                realtimeHeadSubscription?.Dispose();
                realtimeHeadSubscription = null;
            }
            else
            {
                AttachRealtimeHead();
            }
            await CommitLocalAsync(serialized);
        }

        /// <summary>
        /// Reruns the load's cloud step for the signed-in account (§4.4),
        /// under the user client's commit turn so no commit interleaves.
        /// </summary>
        internal async Awaitable ReconcileSignInAsync()
        {
            var client = userClient;
            if (client == null || core.ApiClient == null)
                return;
            await client.RunCommitTurnAsync(async () =>
            {
                var answer = await QueryUserFileAsync(
                    core.ApiClient, core.Authentication, core.TargetReleaseChannelId);
                if (answer.IsFailed)
                    return;
                var choice = await ChooseUserFileAsync(active, answer);
                State = NeoSaveSynchronizerState.Ready;
                if (choice == UserFileChoice.Defaults && active != null)
                {
                    await ApplyUserReplacementAsync(client, DefaultUserFile());
                }
                else if (choice == UserFileChoice.Remote)
                {
                    await ApplyUserReplacementAsync(client, LocalGameSave.FromRemote(answer.File!));
                }
                else if (choice == UserFileChoice.Local && answer.File is { } remote)
                {
                    // Pending writes stay dirty; the next commit writes them,
                    // and the rest of the local copy, as a new head.
                    var baseline = LocalGameSave.FromRemote(remote);
                    client.AdoptPersistedIdentity(baseline);
                    CustomId = baseline.customId;
                    active = baseline;
                    unsyncedActive = null;
                    fullDiffPending = true;
                    ResetLiveSessionBasis(baseline);
                    AttachRealtimeHead();
                }
            });
        }
    }
}
