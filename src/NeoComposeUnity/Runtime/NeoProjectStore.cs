// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// The project root: owns the project schema and the browsable save list, and
    /// is the factory/owner of active-file <see cref="NeoSaveSynchronizer"/>s (each
    /// sharing the same <see cref="InternalProjectStore"/> so the list and the
    /// active file never desync).
    /// </summary>
    /// <remarks>
    /// Brought up asynchronously via <see cref="LoadAsync"/> (Loading → Ready),
    /// which resolves the schema (from an <see cref="IProjectDataSource"/>) and the
    /// initial save list together. <see cref="Open"/> / <see cref="CreateNew"/> are
    /// valid only once Ready. The schema is config-driven by default so the
    /// generated client no longer needs a <c>projectJson</c> argument.
    /// </remarks>
    public sealed class NeoProjectStore : IDisposable
    {
        private readonly IProjectDataSource dataSource;
        private readonly INeoLocalSaveStore localStore;
        private readonly INeoApiClient? apiClient;
        private readonly string targetReleaseChannelId;
        private readonly NeoSaveOptions options;
        private readonly bool requireCloudCommit;
        private readonly NeoAuthentication? authentication;
        private readonly INeoRealtimeProvider? realtimeProvider;

        private InternalProjectStore? core;
        private bool disposed;

        /// <summary>
        /// Builds a project store. With no arguments everything is inferred from the
        /// project's <see cref="NeoComposeConfig"/> in Resources: the schema (from the
        /// configured project JSON), the target release channel, a durable
        /// <see cref="NeoFileLocalSaveStore"/> under
        /// <see cref="Application.persistentDataPath"/>, and — per the
        /// <see cref="NeoComposeConfig.enableOAuthCloudSync"/> master switch — cloud
        /// sync. Every part can be overridden for tests or customization.
        /// </summary>
        /// <param name="config">
        /// The Neo Compose config; defaults to <see cref="NeoComposeConfig.LoadDefault"/>.
        /// Only consulted when a default is actually needed, so passing an explicit
        /// <paramref name="dataSource"/> keeps the store isolated from any Resources config.
        /// </param>
        /// <param name="dataSource">The project schema source; defaults to the config's project JSON.</param>
        /// <param name="localStore">The local byte layer; defaults to a <see cref="NeoFileLocalSaveStore"/>.</param>
        /// <param name="apiClient">An explicit cloud transport; when set, wins over the config master switch.</param>
        /// <param name="authentication">An explicit authentication; its api client is used when no <paramref name="apiClient"/> is given.</param>
        /// <param name="targetReleaseChannelId">Defaults to the config's target channel.</param>
        /// <param name="realtimeProvider">
        /// Optional realtime transport (explicit registration; see
        /// <c>specs/convex-realtime-sync.md</c>). The store owns it from here:
        /// an unconfigured <see cref="INeoRealtimeConfigurable"/> provider is
        /// configured from the project config + this store's authentication,
        /// the socket connects around the sign-in lifecycle (during
        /// <see cref="LoadAsync"/> when already signed in, and automatically on
        /// later sign-in), and it is disposed with <see cref="Dispose"/>.
        /// Requires cloud sync — with no API client the registration is dropped
        /// with a warning (and disposed).
        /// </param>
        public NeoProjectStore(
            NeoComposeConfig? config = null,
            IProjectDataSource? dataSource = null,
            INeoLocalSaveStore? localStore = null,
            INeoApiClient? apiClient = null,
            NeoAuthentication? authentication = null,
            string? targetReleaseChannelId = null,
            NeoSaveOptions? options = null,
            bool requireCloudCommit = false,
            INeoRealtimeProvider? realtimeProvider = null)
        {
            // Only fall back to the Resources config when a config-derived default is
            // actually needed — so tests that inject their own data source stay isolated.
            if (dataSource == null)
            {
                config ??= NeoComposeConfig.LoadDefault();
                if (config == null)
                {
                    throw new InvalidOperationException(
                        "NeoProjectStore needs a NeoComposeConfig (none found in Resources) or an " +
                        "explicit project data source.");
                }
                dataSource = NeoResourcesProjectDataSource.FromConfig(config);
            }

            this.dataSource = dataSource;
            this.localStore = localStore ?? new NeoFileLocalSaveStore();
            this.options = options ?? new NeoSaveOptions();
            this.requireCloudCommit = requireCloudCommit;
            this.targetReleaseChannelId = targetReleaseChannelId ?? config?.targetReleaseChannelId ?? "";
            this.authentication = ResolveAuthentication(config, apiClient, authentication, out this.apiClient);
            this.realtimeProvider = ResolveRealtimeProvider(
                realtimeProvider, config, this.authentication, this.apiClient);
        }

        /// <summary>
        /// Realtime registration. The store owns the provider from the moment
        /// it is passed in — it is disposed with the store, or immediately when
        /// the registration is dropped here. An unconfigured
        /// <see cref="INeoRealtimeConfigurable"/> provider is configured from
        /// the project config + the store's authentication, so the socket
        /// credential always derives from the same sign-in the REST client
        /// uses; a provider constructed with explicit options is left as-is.
        /// Every drop is graceful (warn, never throw): realtime is an overlay,
        /// and the REST/local store must come up regardless.
        /// </summary>
        private static INeoRealtimeProvider? ResolveRealtimeProvider(
            INeoRealtimeProvider? provider,
            NeoComposeConfig? config,
            NeoAuthentication? authentication,
            INeoApiClient? apiClient)
        {
            if (provider == null) return null;

            if (apiClient == null)
            {
                Debug.LogWarning(
                    "[NeoCompose] A realtime provider was registered but cloud sync is disabled " +
                    "(no API client); realtime stays off for this project store.");
                provider.Dispose();
                return null;
            }

            if (provider is not INeoRealtimeConfigurable { IsConfigured: false } configurable)
            {
                return provider;
            }

            config ??= NeoComposeConfig.LoadDefault();
            if (config == null)
            {
                Debug.LogWarning(
                    "[NeoCompose] The realtime provider needs configuration but no " +
                    "NeoComposeConfig was found in Resources; realtime stays off. Pass a " +
                    "config to NeoProjectStore or construct the provider with explicit options.");
                provider.Dispose();
                return null;
            }

            if (string.IsNullOrWhiteSpace(config.convexUrl))
            {
                Debug.LogWarning(
                    "[NeoCompose] The realtime provider needs configuration but the config has " +
                    "no Convex URL; realtime stays off. Synchronize the project in the editor " +
                    "to receive it (NeoComposeConfig.convexUrl).");
                provider.Dispose();
                return null;
            }

            if (authentication == null)
            {
                Debug.LogWarning(
                    "[NeoCompose] The realtime provider needs configuration but the store has " +
                    "no NeoAuthentication to derive the socket credential from (an explicit " +
                    "apiClient was supplied without one); realtime stays off. Pass " +
                    "authentication, or construct the provider with explicit options.");
                provider.Dispose();
                return null;
            }

            configurable.Configure(new NeoRealtimeProviderContext(
                config.convexUrl,
                config.apiBaseUrl,
                config.projectId,
                authentication.AccessTokenProvider));
            return provider;
        }

        /// <summary>
        /// Cloud wiring: an explicit <paramref name="apiClient"/> or
        /// <paramref name="authentication"/> wins; otherwise the config's
        /// <see cref="NeoComposeConfig.enableOAuthCloudSync"/> master switch
        /// auto-constructs auth from the synced runtime OAuth config, debug-warning and
        /// falling back to local-only when the credential is missing.
        /// </summary>
        private static NeoAuthentication? ResolveAuthentication(
            NeoComposeConfig? config,
            INeoApiClient? apiClient,
            NeoAuthentication? authentication,
            out INeoApiClient? resolvedApiClient)
        {
            if (apiClient != null)
            {
                resolvedApiClient = apiClient;
                return authentication;
            }

            if (authentication != null)
            {
                resolvedApiClient = authentication.CreateApiClient();
                return authentication;
            }

            if (config != null && config.enableOAuthCloudSync)
            {
                if (config.TryBuildAuthenticationOptions(out var authOptions))
                {
                    var auth = new NeoAuthentication(authOptions!);
                    resolvedApiClient = auth.CreateApiClient();
                    return auth;
                }

                Debug.LogWarning(
                    "Neo Compose cloud save sync is enabled but the runtime OAuth client " +
                    "configuration is incomplete; falling back to local-only saves. " +
                    "Synchronize the project to pre-fill it, or disable cloud save sync.");
            }

            resolvedApiClient = null;
            return null;
        }

        /// <summary>The runtime authentication backing cloud sync, or null when local-only.</summary>
        public NeoAuthentication? Authentication => authentication;

        /// <summary>The registered realtime transport, or null (including when it
        /// was dropped because cloud sync is off).</summary>
        public INeoRealtimeProvider? RealtimeProvider => realtimeProvider;

        public NeoProjectStoreState State { get; private set; } = NeoProjectStoreState.Idle;

        /// <summary>The project schema; valid once Ready.</summary>
        public ProjectData? Schema => core?.Schema;

        /// <summary>The current browsable save list; empty until loaded.</summary>
        public IReadOnlyList<NeoSaveListEntry> Saves =>
            core?.Saves ?? (IReadOnlyList<NeoSaveListEntry>)Array.Empty<NeoSaveListEntry>();

        public event Action? OnListChanged;
        public event Action<string>? OnSaveArchived;
        public event Action<string>? OnSnapshotArchived;
        public event Action<RemoteGameSave>? OnSaveCloned;

        /// <summary>
        /// Resolves the schema and the initial save list together, moving the store
        /// Loading → Ready (or → Errored on failure).
        /// </summary>
        public async Awaitable LoadAsync()
        {
            ThrowIfDisposed();
            if (State == NeoProjectStoreState.Loading)
            {
                throw new InvalidOperationException("Neo Compose project store is already loading.");
            }

            State = NeoProjectStoreState.Loading;
            try
            {
                var json = await dataSource.ReadProjectJsonAsync();
                var schema = JsonConvert.DeserializeObject<ProjectData>(json);
                if (schema == null)
                {
                    throw new InvalidOperationException(
                        "Neo Compose project JSON could not be deserialized.");
                }

                NeoProjectDataValidator.Validate(schema);
                core = new InternalProjectStore(
                    schema,
                    localStore,
                    apiClient,
                    targetReleaseChannelId,
                    options,
                    requireCloudCommit,
                    authentication,
                    now: null,
                    realtimeProvider);
                core.ListChanged += () => OnListChanged?.Invoke();
                await core.RefreshListAsync();
                await BringUpRealtimeAsync();
                State = NeoProjectStoreState.Ready;
            }
            catch (Exception)
            {
                State = NeoProjectStoreState.Errored;
                throw;
            }
        }

        /// <summary>Re-reads the save list from local + cloud.</summary>
        public Awaitable RefreshSavesAsync() => RequireReady().RefreshListAsync();

        /// <summary>
        /// Hooks the provider's state events (re-attaching subscriptions on every
        /// Connected transition), ties the socket to the sign-in lifecycle, and,
        /// when the player is already signed in, connects. A failed connect only
        /// warns: loading never depends on realtime, and the REST/local paths
        /// stand unchanged.
        /// </summary>
        private async Awaitable BringUpRealtimeAsync()
        {
            if (realtimeProvider == null || core == null) return;

            // Unhook-then-hook so a re-load never double-subscribes.
            realtimeProvider.OnConnectionStateChanged -= OnRealtimeConnectionStateChanged;
            realtimeProvider.OnConnectionStateChanged += OnRealtimeConnectionStateChanged;
            if (authentication != null)
            {
                authentication.OnStateChanged -= OnAuthenticationStateChanged;
                authentication.OnStateChanged += OnAuthenticationStateChanged;
            }

            if (realtimeProvider.State == NeoRealtimeConnectionState.Connected)
            {
                core.AttachRealtimeSubscriptions();
                return;
            }

            if (authentication is not { IsSignedIn: true }) return;
            await ConnectRealtimeBestEffortAsync("during project load");
        }

        private void OnRealtimeConnectionStateChanged(NeoRealtimeConnectionState state)
        {
            if (state != NeoRealtimeConnectionState.Connected) return;
            core?.AttachRealtimeSubscriptions();
        }

        /// <summary>
        /// The sign-in lifecycle drives the socket: a successful sign-in
        /// connects realtime automatically, and sign-out / expiry tears it down
        /// so no socket outlives its credential. Both directions are
        /// best-effort — they warn rather than throw into the authentication
        /// flow, and saves keep working over REST/local either way.
        /// </summary>
        private async void OnAuthenticationStateChanged(NeoAuthenticationState state)
        {
            if (disposed || realtimeProvider == null) return;

            if (state == NeoAuthenticationState.SignedIn)
            {
                await ConnectRealtimeBestEffortAsync("after sign-in");
                return;
            }

            if (state != NeoAuthenticationState.SignedOut
                && state != NeoAuthenticationState.Expired)
            {
                return;
            }

            try
            {
                core?.DetachRealtimeSubscriptions();
                await realtimeProvider.DisconnectAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[NeoCompose] Realtime disconnect after sign-out failed; the socket is " +
                    $"torn down locally regardless. " +
                    $"{exception.GetType().Name}: {exception.Message}");
            }
        }

        private async Awaitable ConnectRealtimeBestEffortAsync(string when)
        {
            try
            {
                await realtimeProvider!.ConnectAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"[NeoCompose] Realtime connect failed {when}; continuing with " +
                    $"REST/local saves. {exception.GetType().Name}: {exception.Message}");
            }
        }

        /// <summary>
        /// Explicitly connects the registered realtime provider — the call a game
        /// makes after a successful sign-in (load-time connect only happens when
        /// already signed in).
        /// </summary>
        public async Awaitable ConnectRealtimeAsync()
        {
            ThrowIfDisposed();
            RequireReady();
            if (realtimeProvider == null)
            {
                throw new InvalidOperationException(
                    "No realtime provider is registered for this project store.");
            }

            await realtimeProvider.ConnectAsync();
        }

        /// <summary>
        /// Explicitly disconnects realtime (sign-out teardown). Call before
        /// signing out so no socket outlives its credential.
        /// </summary>
        public async Awaitable DisconnectRealtimeAsync()
        {
            ThrowIfDisposed();
            if (realtimeProvider == null)
            {
                throw new InvalidOperationException(
                    "No realtime provider is registered for this project store.");
            }

            core?.DetachRealtimeSubscriptions();
            await realtimeProvider.DisconnectAsync();
        }

        /// <summary>
        /// Tears down realtime: unhooks the sign-in lifecycle, detaches
        /// subscriptions, and disposes the registered provider (the store owns
        /// it). The local/REST save layers need no teardown. Safe to call
        /// repeatedly; the store is unusable afterwards.
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            if (authentication != null)
            {
                authentication.OnStateChanged -= OnAuthenticationStateChanged;
            }
            if (realtimeProvider != null)
            {
                realtimeProvider.OnConnectionStateChanged -= OnRealtimeConnectionStateChanged;
                core?.DetachRealtimeSubscriptions();
                realtimeProvider.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(NeoProjectStore));
            }
        }

        /// <summary>Returns a synchronizer for an existing save (local and/or cloud).</summary>
        public NeoSaveSynchronizer Open(string customId)
        {
            if (string.IsNullOrWhiteSpace(customId))
            {
                throw new ArgumentException("Save customId cannot be empty.", nameof(customId));
            }

            return new NeoSaveSynchronizer(RequireReady(), customId, isNewDraft: false);
        }

        /// <summary>
        /// Seeds a brand-new local draft from scratch. <paramref name="customId"/>
        /// defaults to a fresh UUID; <paramref name="name"/> defaults via
        /// <c>BuildSaveName</c>. The draft is in-memory/local-only (Ready) and
        /// nothing is persisted locally or in the cloud until the first commit.
        /// </summary>
        public NeoSaveSynchronizer CreateNew(string? customId = null, string? name = null)
        {
            var core = RequireReady();
            var id = string.IsNullOrWhiteSpace(customId) ? Guid.NewGuid().ToString("N") : customId!;
            return new NeoSaveSynchronizer(core, id, isNewDraft: true, draftName: name);
        }

        /// <summary>
        /// Clones an existing save and lists + returns its synchronizer. With cloud
        /// sync the clone is created server-side on the target channel; without it
        /// (local-only / signed out) the local save content is copied into a fresh
        /// local-only save, so Clone works consistently either way.
        /// </summary>
        public async Awaitable<NeoSaveSynchronizer> CloneAsync(
            string customId,
            string? newName = null,
            string? snapshotId = null)
        {
            var core = RequireReady();
            if (core.ApiClient == null)
            {
                return await CloneLocalAsync(core, customId, newName);
            }

            var cloned = await core.ApiClient.CloneSaveAsync(
                customId,
                new NeoCloneRequest
                {
                    cloneName = newName,
                    snapshotId = snapshotId,
                    targetReleaseChannelId = targetReleaseChannelId,
                });

            await core.LocalStore.CommitSaveAsync(cloned.id, JsonConvert.SerializeObject(cloned));
            core.RecordSavedFile(LocalGameSave.FromRemote(cloned), cloned);
            OnSaveCloned?.Invoke(cloned);
            return new NeoSaveSynchronizer(core, cloned.id, isNewDraft: false);
        }

        /// <summary>
        /// Copies a save's local content into a brand-new local-only save (fresh
        /// <c>customId</c>, server identity stripped), for cloning while signed out.
        /// </summary>
        private static async Awaitable<NeoSaveSynchronizer> CloneLocalAsync(
            InternalProjectStore core,
            string customId,
            string? newName)
        {
            var content = await core.LocalStore.LoadSaveAsync(customId);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException(
                    $"Cannot clone save \"{customId}\": it has no local content to copy.");
            }
            if (!LocalGameSaveLoader.TryLoad(content, out var clone))
            {
                throw new InvalidOperationException(
                    $"Cannot clone save \"{customId}\": its local content could not be parsed.");
            }

            var sourceName = clone.name;
            clone.customId = Guid.NewGuid().ToString("N");
            clone.name = string.IsNullOrWhiteSpace(newName) ? $"{sourceName} (copy)" : newName!;
            // Strip server identity so the copy is a fresh local-only save.
            clone.serverId = null;
            clone.snapshotId = null;
            clone.snapshotHash = null;
            clone.synchronizedAt = null;

            await core.LocalStore.CommitSaveAsync(
                clone.customId, JsonConvert.SerializeObject(clone));
            core.RecordSavedFile(clone, null);
            return new NeoSaveSynchronizer(core, clone.customId, isNewDraft: false);
        }

        /// <summary>
        /// Archives a save (cloud + local + list). The cloud archive runs only
        /// when the save still has a cloud copy (<see cref="NeoSaveListEntry.existsRemotely"/>),
        /// so a local-only save — including one whose server copy was deleted and
        /// reconciled to local-only by <see cref="RefreshSavesAsync"/> — skips it.
        /// A stale entry that still claims a cloud copy is tolerated: a
        /// <see cref="NeoComposeNotFoundException"/> (the server copy is already
        /// gone) falls through to the local delete rather than failing the whole
        /// operation. Other cloud failures (auth/offline) propagate so the local
        /// file is not dropped while the cloud copy survives.
        /// </summary>
        public async Awaitable ArchiveAsync(string customId)
        {
            var core = RequireReady();
            bool existsRemotely = core.TryGetEntry(customId, out var entry) && entry.existsRemotely;
            if (core.ApiClient != null && existsRemotely)
            {
                try
                {
                    await core.ApiClient.ArchiveSaveAsync(customId);
                }
                catch (NeoComposeNotFoundException)
                {
                    // Already deleted server-side — clean up the local orphan.
                }
            }

            await core.LocalStore.DeleteSaveAsync(customId);
            core.RecordArchivedSave(customId);
            OnSaveArchived?.Invoke(customId);
        }

        /// <summary>Archives a single snapshot of a save (cloud only).</summary>
        public async Awaitable ArchiveSnapshotAsync(string customId, string snapshotId)
        {
            var core = RequireReady();
            if (core.ApiClient == null)
            {
                throw new InvalidOperationException(
                    "Snapshot archival requires cloud sync (no API client is configured).");
            }

            await core.ApiClient.ArchiveSnapshotAsync(customId, snapshotId);
            OnSnapshotArchived?.Invoke(snapshotId);
        }

        private InternalProjectStore RequireReady()
        {
            ThrowIfDisposed();
            if (State == NeoProjectStoreState.Idle)
            {
                throw new InvalidOperationException(
                    "Neo Compose project store is not loaded. Call LoadAsync() first.");
            }
            if (State == NeoProjectStoreState.Loading)
            {
                throw new InvalidOperationException(
                    "Neo Compose project store is still loading. Await LoadAsync() before opening saves.");
            }
            if (State == NeoProjectStoreState.Errored || core == null)
            {
                throw new InvalidOperationException(
                    "Neo Compose project store failed to load; saves are unavailable.");
            }

            return core;
        }
    }
}
