// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Convex.Tests
{
    /// <summary>
    /// The provider's save bridge: Convex subscriptions/mutations in, core DTOs
    /// out. Verifies function names, args, JSON mapping across the
    /// System.Text.Json/Newtonsoft boundary, dispatcher delivery, and the typed
    /// commit contract.
    /// </summary>
    public sealed class ConvexRealtimeSaveBridgeTests
    {
        private const string RemoteSaveJson =
            "{\"id\":\"save-1\",\"serverId\":\"server-1\",\"name\":\"Cloud Save\"," +
            "\"projectId\":\"project-1\",\"releaseChannelId\":\"channel-dev\"," +
            "\"snapshotId\":\"snap-1\"," +
            "\"version\":{\"id\":\"v1\",\"label\":\"1.0\"}," +
            "\"createdAt\":1,\"updatedAt\":2,\"synchronizedAt\":3}";

        private FakeAccessTokenProvider tokens = null!;
        private FakeHttpClient http = null!;
        private FakeRealtimeSocket socket = null!;
        private ManualDispatcher dispatcher = null!;

        [SetUp]
        public void SetUp()
        {
            tokens = new FakeAccessTokenProvider();
            http = new FakeHttpClient();
            socket = new FakeRealtimeSocket();
            dispatcher = new ManualDispatcher();
        }

        private async Task<ConvexRealtimeProvider> CreateConnectedProviderAsync()
        {
            var options = new ConvexRealtimeOptions(
                "https://deployment.convex.cloud",
                "https://api.example",
                "project-1",
                tokens,
                http,
                () => DateTimeOffset.UtcNow);
            var provider = new ConvexRealtimeProvider(options, () => socket, dispatcher.Dispatch);
            await provider.ConnectAsync();
            return provider;
        }

        [Test]
        public async Task SaveListSubscriptionTargetsTheListQueryAndParses()
        {
            var provider = await CreateConnectedProviderAsync();
            var received = new List<NeoSaveFileList>();
            provider.SubscribeSaveList("channel-dev", received.Add);

            Assert.That(socket.Observations, Has.Count.EqualTo(1));
            var (functionName, args) = socket.Observations[0];
            Assert.That(functionName, Is.EqualTo("gameSaves:list"));
            Assert.That(args["projectId"], Is.EqualTo("project-1"));
            Assert.That(args["targetReleaseChannelId"], Is.EqualTo("channel-dev"));

            socket.PushJson(
                "gameSaves:list",
                "{\"saves\":[" + RemoteSaveJson + "],\"cloneRequired\":{\"save-1\":false}}");
            Assert.That(received, Is.Empty, "delivery must wait for the dispatcher");

            dispatcher.Flush();
            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received[0].saves, Has.Count.EqualTo(1));
            Assert.That(received[0].saves[0].id, Is.EqualTo("save-1"));
            Assert.That(received[0].RequiresClone("save-1"), Is.False);
        }

        [Test]
        public async Task SaveRevisionSubscriptionTargetsCompactQueryAndParses()
        {
            var provider = await CreateConnectedProviderAsync();
            var received = new List<GameSaveSnapshotRevisionSignal>();
            provider.SubscribeSaveRevision("save-1", received.Add);

            var (functionName, args) = socket.Observations[0];
            Assert.That(functionName, Is.EqualTo("gameSaveRecordReads:getSnapshotRevision"));
            Assert.That(args["customId"], Is.EqualTo("save-1"));

            socket.PushJson(
                "gameSaveRecordReads:getSnapshotRevision",
                "{\"snapshotId\":\"snap-1\",\"snapshotRevision\":7}");
            dispatcher.Flush();

            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received[0].snapshotRevision, Is.EqualTo(7));
        }

        [Test]
        public void SubscriptionsAreInertWhileDisconnected()
        {
            var options = new ConvexRealtimeOptions(
                "https://deployment.convex.cloud",
                "https://api.example",
                "project-1",
                tokens,
                http);
            var provider = new ConvexRealtimeProvider(options, () => socket, dispatcher.Dispatch);

            using var subscription = provider.SubscribeSaveList(null, _ => { });

            Assert.That(socket.Observations, Is.Empty);
        }

        [Test]
        public async Task CommitSendsTheMutationWithTheNewtonsoftShapedSave()
        {
            var provider = await CreateConnectedProviderAsync();
            socket.MutateImpl = _ => "{\"kind\":\"committed\",\"save\":" + RemoteSaveJson + "}";

            var request = new NeoSaveCommitRequest
            {
                customId = "save-1",
                name = "My Save",
                targetReleaseChannelId = "channel-dev",
            };
            var result = await provider.CommitAsync(request, replaceSnapshot: true);

            Assert.That(result.Outcome, Is.EqualTo(NeoCommitOutcome.Committed));
            Assert.That(result.CommittedSave!.id, Is.EqualTo("save-1"));

            var (functionName, args) = socket.Mutations[0];
            Assert.That(functionName, Is.EqualTo("gameSaves:commit"));
            Assert.That(args["replaceSnapshot"], Is.EqualTo(true));
            var save = (Dictionary<string, object?>)args["save"]!;
            Assert.That(save["customId"], Is.EqualTo("save-1"));
        }

        [Test]
        public async Task CommitMapsATypedConflict()
        {
            var provider = await CreateConnectedProviderAsync();
            socket.MutateImpl = _ => "{\"kind\":\"conflict\",\"serverHead\":" + RemoteSaveJson + "}";

            var result = await provider.CommitAsync(
                new NeoSaveCommitRequest { customId = "save-1" }, replaceSnapshot: false);

            Assert.That(result.IsConflict, Is.True);
            Assert.That(result.ServerHead!.snapshotId, Is.EqualTo("snap-1"));
        }

        [Test]
        public async Task CommitMapsADurableTransition()
        {
            var provider = await CreateConnectedProviderAsync();
            socket.MutateImpl = _ =>
                "{\"kind\":\"transitioning\",\"customId\":\"save-1\"," +
                "\"targetSnapshotId\":\"snap-next\"}";

            var result = await provider.CommitAsync(
                new NeoSaveCommitRequest { customId = "save-1" },
                replaceSnapshot: false);

            Assert.That(result.IsTransitioning, Is.True);
            Assert.That(result.CustomId, Is.EqualTo("save-1"));
            Assert.That(result.TargetSnapshotId, Is.EqualTo("snap-next"));
        }

        [Test]
        public async Task CommitRejectsAnUnknownResultKind()
        {
            var provider = await CreateConnectedProviderAsync();
            socket.MutateImpl = _ => "{\"kind\":\"weird\"}";

            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await provider.CommitAsync(
                    new NeoSaveCommitRequest { customId = "save-1" }, replaceSnapshot: false));
            Assert.That(exception!.Message, Does.Contain("unknown kind"));
        }

        [Test]
        public void CommitWhileDisconnectedThrowsDistinctly()
        {
            var options = new ConvexRealtimeOptions(
                "https://deployment.convex.cloud",
                "https://api.example",
                "project-1",
                tokens,
                http);
            var provider = new ConvexRealtimeProvider(options, () => socket, dispatcher.Dispatch);

            Assert.That(provider.CanCommit, Is.False);
            var exception = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await provider.CommitAsync(
                    new NeoSaveCommitRequest { customId = "save-1" }, replaceSnapshot: false));
            Assert.That(exception!.Message, Does.Contain("CanCommit"));
        }

        [Test]
        public async Task ForkLiveSendsTheMutationWithTheNewtonsoftShapedRequest()
        {
            var provider = await CreateConnectedProviderAsync();
            socket.MutateImpl = _ => "{\"kind\":\"committed\",\"save\":" + LiveRemoteSaveJson + "}";

            var request = new NeoLiveForkRequest
            {
                customId = "save-1",
                liveSessionId = "session-1",
                baseSnapshotId = "snap-0",
            };
            request.patch.changes.Add(new GameSaveValueReplaceChange
            {
                valueId = "value-1",
                value = JToken.FromObject(11),
            });

            var result = await provider.ForkLiveAsync(request);

            Assert.That(result.Outcome, Is.EqualTo(NeoCommitOutcome.Committed));
            Assert.That(result.CommittedSave!.liveSessionId, Is.EqualTo("session-1"));

            var (functionName, args) = socket.Mutations[0];
            Assert.That(functionName, Is.EqualTo("gameSaves:forkLiveSnapshot"));
            Assert.That(args["projectId"], Is.EqualTo("project-1"));
            var save = (Dictionary<string, object?>)args["save"]!;
            Assert.That(save["liveSessionId"], Is.EqualTo("session-1"));
            Assert.That(save["baseSnapshotId"], Is.EqualTo("snap-0"));
            var patch = (Dictionary<string, object?>)save["patch"]!;
            var changes = (List<object?>)patch["changes"]!;
            Assert.That(((Dictionary<string, object?>)changes[0]!)["kind"],
                Is.EqualTo("value.replace"));
        }

        [Test]
        public async Task ForkLiveMapsATypedConflict()
        {
            var provider = await CreateConnectedProviderAsync();
            socket.MutateImpl = _ => "{\"kind\":\"conflict\",\"serverHead\":" + RemoteSaveJson + "}";

            var result = await provider.ForkLiveAsync(
                new NeoLiveForkRequest { customId = "save-1", liveSessionId = "session-1" });

            Assert.That(result.IsConflict, Is.True);
            Assert.That(result.ServerHead!.snapshotId, Is.EqualTo("snap-1"));
            Assert.That(result.ServerHead.liveSessionId, Is.Null);
        }

        [Test]
        public async Task PatchLiveSendsTheMutationAndParsesTheResult()
        {
            var provider = await CreateConnectedProviderAsync();
            socket.MutateImpl = _ =>
                "{\"kind\":\"patched\",\"snapshotId\":\"snap-live\"," +
                "\"snapshotRevision\":2,\"synchronizedAt\":42," +
                "\"changedDescriptors\":[]}";

            var request = new NeoLivePatchRequest
            {
                customId = "save-1",
                snapshotId = "snap-live",
            };
            request.patch.changes.Add(new GameSaveValueReplaceChange
            {
                valueId = "value-1",
                value = JToken.FromObject(new { mark = "removed" }),
            });

            var result = await provider.PatchLiveAsync(request);

            Assert.That(result.Outcome, Is.EqualTo(NeoLivePatchOutcome.Patched));
            Assert.That(result.SnapshotId, Is.EqualTo("snap-live"));
            Assert.That(result.SnapshotRevision, Is.EqualTo(2));
            Assert.That(result.SynchronizedAt.EpochMilliseconds, Is.EqualTo(42));

            var (functionName, args) = socket.Mutations[0];
            Assert.That(functionName, Is.EqualTo("gameSaves:patchLiveSnapshot"));
            Assert.That(args["customId"], Is.EqualTo("save-1"));
            Assert.That(args["snapshotId"], Is.EqualTo("snap-live"));
            var changes = (List<object?>)args["changes"]!;
            var replace = (Dictionary<string, object?>)changes[0]!;
            Assert.That(replace["kind"], Is.EqualTo("value.replace"));
        }

        [Test]
        public async Task PatchLiveMapsATypedStaleTarget()
        {
            var provider = await CreateConnectedProviderAsync();
            socket.MutateImpl = _ =>
                "{\"kind\":\"staleTarget\",\"serverHead\":" +
                "{\"snapshotId\":\"snap-live\",\"snapshotRevision\":3}}";

            var result = await provider.PatchLiveAsync(
                new NeoLivePatchRequest { customId = "save-1", snapshotId = "snap-stale" });

            Assert.That(result.IsStaleTarget, Is.True);
            Assert.That(result.ServerHead!.snapshotId, Is.EqualTo("snap-live"));
        }

        /// <summary>
        /// Defensive wire conversion: a patch entry that was already coerced
        /// into a date token (content parsed before the date-neutral loaders,
        /// e.g. an old local-store file) transmits as the ISO string Newtonsoft
        /// itself would have written — never failing the flush.
        /// </summary>
        [Test]
        public async Task CoercedDateTokensTransmitAsIsoStrings()
        {
            var provider = await CreateConnectedProviderAsync();
            socket.MutateImpl = _ => "{\"kind\":\"committed\",\"save\":" + LiveRemoteSaveJson + "}";

            var request = new NeoLiveForkRequest
            {
                customId = "save-1",
                liveSessionId = "session-1",
                baseSnapshotId = "snap-0",
            };
            request.patch.changes.Add(new GameSaveValueReplaceChange
            {
                valueId = "stamp",
                value = new JValue(
                    new DateTime(2026, 6, 11, 11, 50, 29, 643, DateTimeKind.Utc)),
            });

            await provider.ForkLiveAsync(request);

            var (_, args) = socket.Mutations[0];
            var save = (Dictionary<string, object?>)args["save"]!;
            var patch = (Dictionary<string, object?>)save["patch"]!;
            var changes = (List<object?>)patch["changes"]!;
            var replace = (Dictionary<string, object?>)changes[0]!;
            Assert.That(replace["value"], Is.EqualTo("2026-06-11T11:50:29.643Z"));
        }

        [Test]
        public void LiveMutationsWhileDisconnectedThrowDistinctly()
        {
            var options = new ConvexRealtimeOptions(
                "https://deployment.convex.cloud",
                "https://api.example",
                "project-1",
                tokens,
                http);
            var provider = new ConvexRealtimeProvider(options, () => socket, dispatcher.Dispatch);

            var forkException = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await provider.ForkLiveAsync(
                    new NeoLiveForkRequest { customId = "save-1", liveSessionId = "session-1" }));
            Assert.That(forkException!.Message, Does.Contain("live fork"));

            var patchException = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await provider.PatchLiveAsync(
                    new NeoLivePatchRequest { customId = "save-1", snapshotId = "snap-1" }));
            Assert.That(patchException!.Message, Does.Contain("live patch"));
        }

        /// <summary>The remote head as the wire returns it mid-session: stamped live.</summary>
        private const string LiveRemoteSaveJson =
            "{\"id\":\"save-1\",\"serverId\":\"server-1\",\"name\":\"Cloud Save\"," +
            "\"projectId\":\"project-1\",\"releaseChannelId\":\"channel-dev\"," +
            "\"snapshotId\":\"snap-live\"," +
            "\"version\":{\"id\":\"v1\",\"label\":\"1.0\"}," +
            "\"createdAt\":1,\"updatedAt\":2,\"synchronizedAt\":3," +
            "\"liveSessionId\":\"session-1\"}";
    }
}
