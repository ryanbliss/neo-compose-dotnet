// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
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
        private readonly bool localStorePassed;
        private readonly string targetReleaseChannelId;
        private readonly NeoSaveOptions options;
        private readonly bool requireCloudCommit;
        // A local export (P101 §4) replaces these for the rest of the store's life.
        private INeoLocalSaveStore localStore;
        private INeoApiClient? apiClient;
        private NeoAuthentication? authentication;
        private INeoRealtimeProvider? realtimeProvider;
        private bool localExport;

        private InternalProjectStore? core;
        private bool disposed;

        // P104 §4.1: the current store owns the process's user file.
        private readonly NeoComposeConfig? config;
        private readonly bool loadUserFile;
        private readonly NeoSaveSynchronizer userSynchronizer = new(NeoSaveFileKind.User);
        private NeoClient? userClient;
        private NeoUserClient? userClientView;
        private bool signedInDuringLoad;
        private static readonly List<AwaitableCompletionSource> userClientWaiters = new();

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
        /// <param name="loadUserFile">
        /// Whether this store may own the player's user file (P104 §4.1). The
        /// first such store to start <see cref="LoadAsync"/> becomes
        /// <see cref="Current"/>. Editor and tooling stores pass false.
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
            INeoRealtimeProvider? realtimeProvider = null,
            bool loadUserFile = true)
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
            this.config = config;
            this.loadUserFile = loadUserFile;
            this.localStorePassed = localStore != null;
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
            if (provider == null)
                return null;

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
                    "no Convex URL; realtime stays off. Run `neo pull` and `neo export` " +
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
                    "Run `neo pull` and `neo export` to fill it in, or disable cloud save sync.");
            }

            resolvedApiClient = null;
            return null;
        }

        /// <summary>The runtime authentication backing cloud sync, or null when local-only.</summary>
        public NeoAuthentication? Authentication => authentication;

        /// <summary>The store that owns the process's user file, or null.</summary>
        public static NeoProjectStore? Current
        {
            get; private set;
        }

        /// <summary>
        /// The user file's synchronizer, for conflict and migration handlers.
        /// Attach them before <see cref="LoadAsync"/>, which loads the file.
        /// </summary>
        public NeoSaveSynchronizer User => loadUserFile
            ? userSynchronizer
            : throw new InvalidOperationException("This store was built with loadUserFile: false, so it has no user file.");

        /// <summary>The loaded user client, or null until this store has loaded the user file.</summary>
        internal NeoClient? LoadedUserClient => userClient;

        /// <summary>The generated user client over <see cref="LoadedUserClient"/>, built once per store.</summary>
        internal T UserClientView<T>(Func<NeoClient, T> create) where T : NeoUserClient
        {
            userClientView ??= create(userClient!);
            return (T)userClientView;
        }

        /// <summary>Whether <see cref="Dispose"/> has run.</summary>
        internal bool IsDisposed => disposed;

        /// <summary>Whether <paramref name="synchronizer"/> was opened by this store.</summary>
        internal bool Opened(NeoSaveSynchronizer synchronizer) =>
            core != null && ReferenceEquals(synchronizer.Core, core);

        /// <summary>
        /// The user client once this store's load in flight finishes: null
        /// when the load failed or the project has no User root.
        /// </summary>
        internal async Awaitable<NeoClient?> LoadedUserClientAsync(CancellationToken cancellationToken)
        {
            while (userClient == null && State == NeoProjectStoreState.Loading)
                await NextLoadFinishedAsync(cancellationToken);
            return userClient;
        }

        /// <summary>Completes once a current store has loaded the user file.</summary>
        internal static async Awaitable WhenUserClientLoadedAsync(CancellationToken cancellationToken)
        {
            while (Current?.userClient == null)
                await NextLoadFinishedAsync(cancellationToken);
        }

        /// <summary>Completes when any store's load next finishes, loaded or failed.</summary>
        private static async Awaitable NextLoadFinishedAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var waiter = new AwaitableCompletionSource();
            userClientWaiters.Add(waiter);
            try
            {
                using (cancellationToken.Register(() => waiter.TrySetCanceled()))
                    await waiter.Awaitable;
            }
            finally
            {
                userClientWaiters.Remove(waiter);
            }
        }

        private static void SignalLoadFinished()
        {
            foreach (var waiter in userClientWaiters.ToArray())
                waiter.TrySetResult();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetCurrent()
        {
            Current = null;
            userClientWaiters.Clear();
        }

        /// <summary>The registered realtime transport, or null (including when it
        /// was dropped because cloud sync is off).</summary>
        public INeoRealtimeProvider? RealtimeProvider => realtimeProvider;

        public NeoProjectStoreState State { get; private set; } = NeoProjectStoreState.Idle;

        /// <summary>The project schema; valid once Ready.</summary>
        public ProjectData? Schema => core?.Schema;

        /// <summary>The current browsable save list; empty until loaded.</summary>
        public IReadOnlyList<NeoSaveListEntry> Saves =>
            core?.Saves ?? (IReadOnlyList<NeoSaveListEntry>)Array.Empty<NeoSaveListEntry>();

        /// <summary>
        /// The local byte store selected by <see cref="LoadAsync"/>. Local exports
        /// replace the constructor's store with an isolated file store. Use this
        /// after loading when inspecting save files so reads match <see cref="Open"/>.
        /// </summary>
        public INeoLocalSaveStore LocalStore => RequireReady().LocalStore;

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
            if (loadUserFile && (Current == null || Current == this))
                Current = this;
            bool ownsUserFile = Current == this;
            Awaitable<NeoSaveSynchronizer.UserFileQuery>? userQuery = null;
            if (ownsUserFile)
            {
                // Subscribe first, so a sign-in mid-load reruns the cloud step.
                if (authentication != null)
                {
                    authentication.OnStateChanged -= OnUserAuthenticationStateChanged;
                    authentication.OnStateChanged += OnUserAuthenticationStateChanged;
                }
                // Overlaps the schema read; never throws.
                userQuery = NeoSaveSynchronizer.QueryUserFileAsync(
                    apiClient, authentication, targetReleaseChannelId);
            }
            try
            {
                ProjectData schema;
                if (dataSource is IParsedProjectDataSource parsedSource)
                {
                    schema = await parsedSource.ReadProjectDataAsync();
                }
                else
                {
                    var json = await dataSource.ReadProjectJsonAsync();
                    schema = ProjectDataConverter.Read(
                        json,
                        dataSource.ReadPartitionJson(NeoProjectExportContract.MainPartitionFile),
                        dataSource.ReadPartitionJson);
                }

                ThrowIfDisposed();
                NeoProjectDataValidator.Validate(schema);
                if (schema.metadata?.localExport == true && !localExport)
                {
                    EnterLocalExport();
                }
                if (localExport)
                    userQuery = null;
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
                core.LoadsUserFile = loadUserFile;
                core.ListChanged += () => OnListChanged?.Invoke();
                if (ownsUserFile && userClient == null
                    && !string.IsNullOrEmpty(schema.project?.rootUserMemberId))
                {
                    // The user client builds while the list request is in flight.
                    var listRefresh = core.RefreshListAsync();
                    try
                    {
                        userSynchronizer.Bind(core);
                        string? content = await userSynchronizer.LoadUserContentAsync(userQuery);
                        ThrowIfDisposed();
                        NeoClient built = await BuildUserClientAsync(schema, content);
                        if (disposed)
                        {
                            built.Dispose();
                            throw new ObjectDisposedException(nameof(NeoProjectStore));
                        }
                        userClient = built;
                        userSynchronizer.BindUserClient(userClient);
                    }
                    finally
                    {
                        await listRefresh;
                    }
                    if (signedInDuringLoad)
                        OnUserAuthenticationStateChanged(NeoAuthenticationState.SignedIn);
                }
                else
                {
                    await core.RefreshListAsync();
                }
                if (!localExport)
                {
                    await BringUpRealtimeAsync();
                }
                State = NeoProjectStoreState.Ready;
            }
            catch (Exception)
            {
                State = NeoProjectStoreState.Errored;
                throw;
            }
            finally
            {
                SignalLoadFinished();
            }
        }

        private async Awaitable<NeoClient> BuildUserClientAsync(ProjectData schema, string? content)
        {
            var localization = NeoLocalization.LoadMain(
                schema.localization,
                new NeoResourcesLocalizationLocaleFileSource(),
                (config ?? NeoComposeConfig.LoadDefault())?.ToLocalizationOptions());
            return await NeoClient.CreateAsync(
                userSynchronizer,
                content,
                NeoAssetDatabase.LoadDefault(),
                localization,
                options,
                CancellationToken.None,
                NeoValueOwnership.User);
        }

        /// <summary>
        /// A sign-in reruns the user file's cloud step for that account
        /// (P104 §4.4). Signing out changes nothing locally.
        /// </summary>
        private async void OnUserAuthenticationStateChanged(NeoAuthenticationState state)
        {
            if (disposed || state != NeoAuthenticationState.SignedIn)
                return;
            if (userClient == null)
            {
                signedInDuringLoad = true;
                return;
            }
            signedInDuringLoad = false;
            try
            {
                await userSynchronizer.ReconcileSignInAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[NeoCompose] Could not load the signed-in account's user file; keeping the " +
                    $"current one. {exception.GetType().Name}: {exception.Message}");
            }
        }

        private void ThrowIfUserFile(string customId, string verb)
        {
            if (userClient != null && customId == userSynchronizer.CustomId)
                throw new InvalidOperationException($"The user file can't be {verb}.");
        }

        /// <summary>
        /// A local export's schema can declare records the server lacks, so its
        /// saves stay on disk, in their own folder so cloud sync never uploads
        /// them later, and nothing reaches the server (P101 §4).
        /// </summary>
        private void EnterLocalExport()
        {
            localExport = true;
            var ignored = new List<string>();
            if (localStorePassed)
                ignored.Add("the local save store");
            if (apiClient != null)
                ignored.Add("cloud saves");
            if (authentication != null)
            {
                ignored.Add("sign-in");
                authentication.OnStateChanged -= OnAuthenticationStateChanged;
                authentication.OnStateChanged -= OnUserAuthenticationStateChanged;
            }
            if (realtimeProvider != null)
            {
                ignored.Add("realtime");
                realtimeProvider.OnConnectionStateChanged -= OnRealtimeConnectionStateChanged;
                core?.DetachRealtimeSubscriptions();
                realtimeProvider.Dispose();
            }

            string directory = Path.Combine(
                Application.persistentDataPath, "NeoCompose", "LocalExport");
            localStore = new NeoFileLocalSaveStore(directory);
            apiClient = null;
            authentication = null;
            realtimeProvider = null;
            string ignoring = ignored.Count == 0
                ? ""
                : $" Ignoring {string.Join(", ", ignored)}.";
            Debug.LogWarning(
                $"[NeoCompose] This project.json is a local export, so saves stay in {directory}." +
                $"{ignoring} Run `neo push` or a clean `neo export` to use the server again.");
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
            if (realtimeProvider == null || core == null)
                return;

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

            if (authentication is not { IsSignedIn: true })
                return;
            await ConnectRealtimeBestEffortAsync("during project load");
        }

        private void OnRealtimeConnectionStateChanged(NeoRealtimeConnectionState state)
        {
            if (state != NeoRealtimeConnectionState.Connected)
                return;
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
            if (disposed || realtimeProvider == null)
                return;

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
            if (disposed)
                return;
            disposed = true;

            if (authentication != null)
            {
                authentication.OnStateChanged -= OnAuthenticationStateChanged;
                authentication.OnStateChanged -= OnUserAuthenticationStateChanged;
            }
            // Attached save clients keep running, detached (P104 §4.1).
            userClient?.Dispose();
            userClient = null;
            userClientView = null;
            if (userSynchronizer.IsBound)
                userSynchronizer.Dispose();
            if (Current == this)
                Current = null;
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
            ThrowIfUserFile(customId, "cloned");
            if (core.ApiClient == null)
            {
                return await CloneLocalAsync(core, customId, newName);
            }

            var cloned = await core.CloneSaveToReadyAsync(
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
        /// operation. When authentication is configured but signed out, the cloud
        /// archive is deliberately skipped and only the local copy is removed;
        /// signing in again can restore the surviving cloud copy. Other cloud
        /// failures while signed in (for example, an offline transport) propagate
        /// so the local file is not dropped unexpectedly.
        /// </summary>
        public async Awaitable ArchiveAsync(string customId)
        {
            var core = RequireReady();
            ThrowIfUserFile(customId, "archived");
            bool existsRemotely = core.TryGetEntry(customId, out var entry) && entry.existsRemotely;
            bool canArchiveRemotely = core.ApiClient != null
                && existsRemotely
                && (core.Authentication == null || core.Authentication.IsSignedIn);
            if (canArchiveRemotely)
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
