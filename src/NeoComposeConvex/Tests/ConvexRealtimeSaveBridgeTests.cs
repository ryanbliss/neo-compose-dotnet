// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
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
            "\"snapshotId\":\"snap-1\",\"snapshotHash\":\"hash-1\"," +
            "\"version\":{\"id\":\"v1\",\"label\":\"1.0\"},\"values\":{}," +
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
        public async Task SaveHeadSubscriptionTargetsTheGetQueryAndParses()
        {
            var provider = await CreateConnectedProviderAsync();
            var received = new List<RemoteGameSave>();
            provider.SubscribeSaveHead("save-1", received.Add);

            var (functionName, args) = socket.Observations[0];
            Assert.That(functionName, Is.EqualTo("gameSaves:get"));
            Assert.That(args["customId"], Is.EqualTo("save-1"));

            socket.PushJson("gameSaves:get", RemoteSaveJson);
            dispatcher.Flush();

            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received[0].snapshotHash, Is.EqualTo("hash-1"));
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
            var save = (JsonElement)args["save"]!;
            Assert.That(save.GetProperty("customId").GetString(), Is.EqualTo("save-1"));
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
    }
}
