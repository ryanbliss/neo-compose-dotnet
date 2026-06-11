// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NeoCompose.Convex.Editor;
using NeoCompose.Unity.Editor;
using NUnit.Framework;

namespace NeoCompose.Convex.Tests
{
    /// <summary>
    /// The editor facade's bridge: version-metadata change signal (payload
    /// ignored, signal-then-pull) and the export sync signal (parsed, null for
    /// a version with no transactions yet).
    /// </summary>
    public sealed class ConvexEditorRealtimeProviderTests
    {
        private FakeRealtimeSocket socket = null!;
        private ManualDispatcher dispatcher = null!;

        [SetUp]
        public void SetUp()
        {
            socket = new FakeRealtimeSocket();
            dispatcher = new ManualDispatcher();
        }

        private async Task<ConvexEditorRealtimeProvider> CreateConnectedProviderAsync()
        {
            var options = new ConvexRealtimeOptions(
                "https://deployment.convex.cloud",
                "https://api.example",
                "project-1",
                new FakeAccessTokenProvider(),
                new FakeHttpClient());
            var inner = new ConvexRealtimeProvider(options, () => socket, dispatcher.Dispatch);
            var provider = new ConvexEditorRealtimeProvider(inner);
            await provider.ConnectAsync();
            return provider;
        }

        [Test]
        public async Task VersionMetadataSubscriptionSignalsWithoutThePayload()
        {
            var provider = await CreateConnectedProviderAsync();
            var changes = 0;
            provider.SubscribeVersionMetadata("project-1", () => changes++);

            var (functionName, args) = socket.Observations[0];
            Assert.That(functionName, Is.EqualTo("projectVersions:listMetadata"));
            Assert.That(args["projectId"], Is.EqualTo("project-1"));

            socket.PushJson(
                "projectVersions:listMetadata",
                "{\"versions\":[],\"versionStatuses\":[],\"releaseChannels\":[]}");
            dispatcher.Flush();

            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public async Task ExportSignalParsesTheHeadTransaction()
        {
            var provider = await CreateConnectedProviderAsync();
            var received = new List<NeoComposeExportSignal?>();
            provider.SubscribeExportSignal("project-1", "version-1", received.Add);

            var (functionName, args) = socket.Observations[0];
            Assert.That(functionName, Is.EqualTo("projectExportData:exportSignal"));
            Assert.That(args["versionId"], Is.EqualTo("version-1"));

            socket.PushJson(
                "projectExportData:exportSignal",
                "{\"versionId\":\"version-1\",\"transactionId\":\"transaction-2\"," +
                "\"transactionHash\":\"hash-2\",\"transactionAt\":200}");
            dispatcher.Flush();

            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received[0]!.transactionId, Is.EqualTo("transaction-2"));
            Assert.That(received[0]!.transactionAt, Is.EqualTo(200));
        }

        [Test]
        public async Task ExportSignalMapsNullForAVersionWithNoTransactions()
        {
            var provider = await CreateConnectedProviderAsync();
            var received = new List<NeoComposeExportSignal?>();
            provider.SubscribeExportSignal("project-1", "version-1", received.Add);

            socket.PushJson("projectExportData:exportSignal", "null");
            dispatcher.Flush();

            Assert.That(received, Has.Count.EqualTo(1));
            Assert.That(received[0], Is.Null);
        }
    }
}
