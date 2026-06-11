// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Realtime integration at the core seam (<see cref="INeoRealtimeProvider"/>):
    /// list subscriptions feed the save-list cache, head pushes prime the
    /// fresh-remote cache and raise the opt-in divergence event, and commits
    /// route through the provider with a REST fallback.
    /// </summary>
    public class NeoRealtimeSaveTests
    {
        private static async Task<(NeoProjectStore store, FakeApiClient api, NeoInMemoryLocalSaveStore local, FakeRealtimeProvider realtime)>
            ReadyStoreWithRealtimeAsync(NeoRealtimeConnectionState initialState)
        {
            var api = new FakeApiClient();
            var local = new NeoInMemoryLocalSaveStore();
            var realtime = new FakeRealtimeProvider { State = initialState };
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: local,
                apiClient: api,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel,
                realtimeProvider: realtime);
            await store.LoadAsync();
            return (store, api, local, realtime);
        }

        [Test]
        public async Task Load_WithConnectedProvider_AttachesTheListSubscription()
        {
            var (_, _, _, realtime) =
                await ReadyStoreWithRealtimeAsync(NeoRealtimeConnectionState.Connected);

            Assert.That(realtime.ListSubscriptions, Has.Count.EqualTo(1));
            Assert.That(
                realtime.ListSubscriptions[0].channel,
                Is.EqualTo(NeoSaveTestSupport.TargetChannel));
        }

        [Test]
        public async Task ConnectedTransition_AttachesTheListSubscription()
        {
            var (_, _, _, realtime) =
                await ReadyStoreWithRealtimeAsync(NeoRealtimeConnectionState.Disconnected);
            Assert.That(realtime.ListSubscriptions, Is.Empty);

            realtime.SetState(NeoRealtimeConnectionState.Connected);

            Assert.That(realtime.ListSubscriptions, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task PushedList_UpdatesTheBrowseListAndPrimesTheFreshRemoteCache()
        {
            var (store, api, _, realtime) =
                await ReadyStoreWithRealtimeAsync(NeoRealtimeConnectionState.Connected);
            var listChanges = 0;
            store.OnListChanged += () => listChanges++;

            var remote = NeoSaveTestSupport.Remote("save-1", "snap-1", "hash-1");
            realtime.PushList(new NeoSaveFileList
            {
                saves = new List<RemoteGameSave> { remote },
                cloneRequired = new Dictionary<string, bool> { ["save-1"] = false },
            });

            Assert.That(listChanges, Is.EqualTo(1));
            Assert.That(store.Saves, Has.Count.EqualTo(1));
            Assert.That(store.Saves[0].existsRemotely, Is.True);

            // The pushed head must satisfy the next load without a per-save
            // fetch: a fetch would throw here.
            api.getThrows = new InvalidOperationException(
                "The fresh-remote cache should have served this load.");
            var sync = store.Open("save-1");
            var content = await sync.LoadSaveContentAsync();
            Assert.That(content, Does.Contain("hash-1"));
        }

        [Test]
        public async Task HeadPush_RaisesTheDivergenceEventOnlyWhenTheHashMoves()
        {
            var (store, api, local, realtime) =
                await ReadyStoreWithRealtimeAsync(NeoRealtimeConnectionState.Connected);
            await local.CommitSaveAsync("save-1", NeoSaveTestSupport.SyncedSaveContent("Local"));
            api.getResult = NeoSaveTestSupport.Remote("save-1", "snap-1", "hash-1");

            var sync = store.Open("save-1");
            await sync.LoadSaveContentAsync();
            Assert.That(realtime.HeadSubscriptions, Has.Count.EqualTo(1));
            Assert.That(realtime.HeadSubscriptions[0].customId, Is.EqualTo("save-1"));

            var divergences = new List<RemoteGameSave>();
            sync.OnRemoteHeadChanged += divergences.Add;

            // Same hash as the active state: cache primed, no event.
            realtime.PushHead(NeoSaveTestSupport.Remote("save-1", "snap-1", "hash-1"));
            Assert.That(divergences, Is.Empty);

            // A new head from another device: event fires, never auto-applies.
            realtime.PushHead(NeoSaveTestSupport.Remote("save-1", "snap-2", "hash-2"));
            Assert.That(divergences, Has.Count.EqualTo(1));
            Assert.That(divergences[0].snapshotId, Is.EqualTo("snap-2"));
            Assert.That(sync.ActiveSave!.snapshotHash, Is.EqualTo("hash-1"), "no auto-apply");
        }

        [Test]
        public async Task Commit_RoutesThroughTheProviderWhenItCanCommit()
        {
            var (store, api, _, realtime) =
                await ReadyStoreWithRealtimeAsync(NeoRealtimeConnectionState.Connected);
            realtime.canCommit = true;
            realtime.commitResults.Enqueue(
                NeoCommitResult.Committed(NeoSaveTestSupport.Remote("save-1", "snap-1", "hash-1")));

            var sync = store.CreateNew("save-1");
            await sync.CommitSaveContentAsync(
                NeoSaveTestSupport.SaveContent("Local"), replaceSnapshot: false);

            Assert.That(realtime.commits, Has.Count.EqualTo(1));
            Assert.That(api.commits, Is.Empty);
        }

        [Test]
        public async Task Commit_FallsBackToRestWhenTheProviderFails()
        {
            var (store, api, _, realtime) =
                await ReadyStoreWithRealtimeAsync(NeoRealtimeConnectionState.Connected);
            realtime.canCommit = true;
            realtime.commitThrows = new InvalidOperationException("socket died (test)");
            api.commitResults.Enqueue(
                NeoCommitResult.Committed(NeoSaveTestSupport.Remote("save-1", "snap-1", "hash-1")));

            var sync = store.CreateNew("save-1");
            await sync.CommitSaveContentAsync(
                NeoSaveTestSupport.SaveContent("Local"), replaceSnapshot: false);

            Assert.That(realtime.commits, Has.Count.EqualTo(1));
            Assert.That(api.commits, Has.Count.EqualTo(1), "one REST retry after the socket failure");
        }

        [Test]
        public async Task Commit_UsesRestWhenTheProviderCannotCommit()
        {
            var (store, api, _, realtime) =
                await ReadyStoreWithRealtimeAsync(NeoRealtimeConnectionState.Connected);
            realtime.canCommit = false;
            api.commitResults.Enqueue(
                NeoCommitResult.Committed(NeoSaveTestSupport.Remote("save-1", "snap-1", "hash-1")));

            var sync = store.CreateNew("save-1");
            await sync.CommitSaveContentAsync(
                NeoSaveTestSupport.SaveContent("Local"), replaceSnapshot: false);

            Assert.That(realtime.commits, Is.Empty);
            Assert.That(api.commits, Has.Count.EqualTo(1));
        }

        [Test]
        public void Registration_IsDroppedAndDisposedWithoutCloudSync()
        {
            var realtime = new FakeRealtimeProvider();
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: new NeoInMemoryLocalSaveStore(),
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel,
                realtimeProvider: realtime);

            Assert.That(store.RealtimeProvider, Is.Null);
            Assert.That(realtime.DisposeCalls, Is.EqualTo(1), "the store owns dropped providers");
        }

        [Test]
        public void Registration_ConfiguresAnUnconfiguredProviderFromConfigAndAuth()
        {
            var config = ScriptableObject.CreateInstance<NeoComposeConfig>();
            config.projectId = "project-1";
            config.apiBaseUrl = "https://api.example";
            config.convexUrl = "https://deployment.convex.cloud";
            var auth = CreateSignedOutAuthentication(new TestTokenStore());
            var realtime = new FakeRealtimeProvider { IsConfigured = false };

            var store = new NeoProjectStore(
                config: config,
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: new NeoInMemoryLocalSaveStore(),
                apiClient: new FakeApiClient(),
                authentication: auth,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel,
                realtimeProvider: realtime);

            Assert.That(store.RealtimeProvider, Is.SameAs(realtime));
            Assert.That(realtime.ConfiguredContext, Is.Not.Null);
            Assert.That(
                realtime.ConfiguredContext!.ConvexUrl,
                Is.EqualTo("https://deployment.convex.cloud"));
            Assert.That(realtime.ConfiguredContext.ApiBaseUrl, Is.EqualTo("https://api.example"));
            Assert.That(realtime.ConfiguredContext.ProjectId, Is.EqualTo("project-1"));
            Assert.That(
                realtime.ConfiguredContext.SessionTokenProvider,
                Is.SameAs(auth.AccessTokenProvider),
                "the socket credential must derive from the store's own sign-in");
        }

        [Test]
        public void Registration_DropsAnUnconfiguredProviderWhenTheConfigHasNoConvexUrl()
        {
            var config = ScriptableObject.CreateInstance<NeoComposeConfig>();
            config.projectId = "project-1";
            config.apiBaseUrl = "https://api.example";
            var auth = CreateSignedOutAuthentication(new TestTokenStore());
            var realtime = new FakeRealtimeProvider { IsConfigured = false };

            var store = new NeoProjectStore(
                config: config,
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: new NeoInMemoryLocalSaveStore(),
                apiClient: new FakeApiClient(),
                authentication: auth,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel,
                realtimeProvider: realtime);

            Assert.That(store.RealtimeProvider, Is.Null);
            Assert.That(realtime.ConfiguredContext, Is.Null);
            Assert.That(realtime.DisposeCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task SignInLifecycle_ConnectsAndDisconnectsRealtime()
        {
            var tokenStore = new TestTokenStore();
            var auth = CreateSignedOutAuthentication(tokenStore);
            var realtime = new FakeRealtimeProvider();
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(NeoSaveTestSupport.ProjectJson),
                localStore: new NeoInMemoryLocalSaveStore(),
                apiClient: new FakeApiClient(),
                authentication: auth,
                targetReleaseChannelId: NeoSaveTestSupport.TargetChannel,
                realtimeProvider: realtime);
            await store.LoadAsync();
            Assert.That(realtime.ConnectCalls, Is.EqualTo(0), "signed out at load: no connect");

            // Sign-in (a token appears and the state flips) connects realtime
            // without any explicit call from the game.
            tokenStore.token = new NeoComposeStoredToken(
                "access-token", long.MaxValue, new[] { "openid" },
                "https://api.example", "Ada Lovelace", "ada@example.test");
            auth.RefreshState();
            Assert.That(auth.IsSignedIn, Is.True);
            Assert.That(realtime.ConnectCalls, Is.EqualTo(1));
            Assert.That(realtime.ListSubscriptions, Has.Count.EqualTo(1), "attached on connect");

            // Sign-out tears the socket down so it never outlives the credential.
            tokenStore.token = null;
            auth.RefreshState();
            Assert.That(realtime.DisconnectCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task Dispose_DisposesTheOwnedProviderOnceAndBlocksFurtherUse()
        {
            var (store, _, _, realtime) =
                await ReadyStoreWithRealtimeAsync(NeoRealtimeConnectionState.Connected);

            store.Dispose();
            store.Dispose();

            Assert.That(realtime.DisposeCalls, Is.EqualTo(1));
            Assert.Throws<ObjectDisposedException>(() => store.Open("save-1"));
        }

        private static NeoAuthentication CreateSignedOutAuthentication(TestTokenStore tokenStore) =>
            new NeoAuthentication(
                new NeoAuthenticationOptions(
                    "https://api.example", "project-1", "client-1", "openid"),
                tokenStore,
                now: () => DateTimeOffset.FromUnixTimeSeconds(0));

        internal sealed class TestTokenStore : INeoComposeTokenStore
        {
            public NeoComposeStoredToken? token;

            public NeoComposeStoredToken? Load() => token;

            public void Save(NeoComposeStoredToken value) => token = value;

            public void Clear() => token = null;

            public NeoComposeTokenHint? PeekHint() => token?.ToHint();
        }
    }

    internal sealed class FakeRealtimeProvider : INeoRealtimeProvider, INeoRealtimeConfigurable
    {
        public NeoRealtimeConnectionState State { get; set; } =
            NeoRealtimeConnectionState.Disconnected;

        public event Action<NeoRealtimeConnectionState>? OnConnectionStateChanged;

        public readonly List<(string? channel, Action<NeoSaveFileList> onChanged)> ListSubscriptions =
            new();

        public readonly List<(string customId, Action<RemoteGameSave> onChanged)> HeadSubscriptions =
            new();

        public readonly Queue<NeoCommitResult> commitResults = new();
        public readonly List<(NeoSaveCommitRequest request, bool replaceSnapshot)> commits = new();
        public Exception? commitThrows;
        public bool canCommit;
        public int ConnectCalls;
        public int DisconnectCalls;
        public int DisposeCalls;

        // Configured by default so the store leaves the fake alone; set false
        // to exercise the store's deferred-configuration path.
        public bool IsConfigured { get; set; } = true;

        public NeoRealtimeProviderContext? ConfiguredContext { get; private set; }

        public void Configure(NeoRealtimeProviderContext context)
        {
            if (IsConfigured)
            {
                throw new InvalidOperationException(
                    "Configure must only be called while unconfigured (test).");
            }

            ConfiguredContext = context;
            IsConfigured = true;
        }

        public bool CanCommit => canCommit;

        public Awaitable ConnectAsync(CancellationToken cancellationToken = default)
        {
            ConnectCalls++;
            SetState(NeoRealtimeConnectionState.Connected);
            return NeoAwaitable.Completed();
        }

        public Awaitable DisconnectAsync()
        {
            DisconnectCalls++;
            SetState(NeoRealtimeConnectionState.Disconnected);
            return NeoAwaitable.Completed();
        }

        public IDisposable SubscribeSaveList(
            string? targetReleaseChannelId, Action<NeoSaveFileList> onChanged)
        {
            ListSubscriptions.Add((targetReleaseChannelId, onChanged));
            return new SubscriptionHandle();
        }

        public IDisposable SubscribeSaveHead(string customId, Action<RemoteGameSave> onChanged)
        {
            HeadSubscriptions.Add((customId, onChanged));
            return new SubscriptionHandle();
        }

        public Awaitable<NeoCommitResult> CommitAsync(
            NeoSaveCommitRequest request, bool replaceSnapshot)
        {
            commits.Add((request, replaceSnapshot));
            if (commitThrows != null) throw commitThrows;
            return NeoAwaitable.FromResult(commitResults.Dequeue());
        }

        public void SetState(NeoRealtimeConnectionState state)
        {
            State = state;
            OnConnectionStateChanged?.Invoke(state);
        }

        public void PushList(NeoSaveFileList list)
        {
            foreach (var subscription in ListSubscriptions.ToArray())
            {
                subscription.onChanged(list);
            }
        }

        public void PushHead(RemoteGameSave remote)
        {
            foreach (var subscription in HeadSubscriptions.ToArray())
            {
                subscription.onChanged(remote);
            }
        }

        public void Dispose()
        {
            DisposeCalls++;
        }

        private sealed class SubscriptionHandle : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }
}
