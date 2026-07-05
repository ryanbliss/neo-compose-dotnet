// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Convex.Client.Infrastructure.Connection;
using NeoCompose.Runtime;
using NUnit.Framework;

namespace NeoCompose.Convex.Tests
{
    public sealed class ConvexRealtimeProviderTests
    {
        private FakeAccessTokenProvider tokens = null!;
        private FakeHttpClient http = null!;
        private FakeRealtimeSocket socket = null!;
        private ManualDispatcher dispatcher = null!;
        private DateTimeOffset now;

        [SetUp]
        public void SetUp()
        {
            tokens = new FakeAccessTokenProvider();
            http = new FakeHttpClient();
            socket = new FakeRealtimeSocket();
            dispatcher = new ManualDispatcher();
            now = new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);
        }

        private ConvexRealtimeProvider CreateProvider()
        {
            var options = new ConvexRealtimeOptions(
                "https://deployment.convex.cloud/",
                "https://api.example",
                "project-1",
                tokens,
                http,
                () => now);
            return new ConvexRealtimeProvider(options, () => socket, dispatcher.Dispatch);
        }

        private ConvexRealtimeProvider CreateUnconfiguredProvider() =>
            new ConvexRealtimeProvider(options: null, () => socket, dispatcher.Dispatch);

        private NeoRealtimeProviderContext CreateContext() =>
            new NeoRealtimeProviderContext(
                "https://deployment.convex.cloud/",
                "https://api.example",
                "project-1",
                tokens);

        [Test]
        public void UnconfiguredProviderReportsAndRefusesToConnect()
        {
            var provider = CreateUnconfiguredProvider();

            Assert.That(provider.IsConfigured, Is.False);
            var exception = Assert.ThrowsAsync<InvalidOperationException>(
                async () => await provider.ConnectAsync());
            Assert.That(exception!.Message, Does.Contain("not configured"));
        }

        [Test]
        public async Task ConfigureEnablesTheProviderWithTheInjectedContext()
        {
            var provider = CreateUnconfiguredProvider();

            provider.Configure(CreateContext());

            Assert.That(provider.IsConfigured, Is.True);
            await provider.ConnectAsync();
            Assert.That(provider.State, Is.EqualTo(NeoRealtimeConnectionState.Connected));
            Assert.That(socket.AuthProvider, Is.SameAs(provider.JwtProvider));
        }

        [Test]
        public void ConfigureTwiceThrows()
        {
            var provider = CreateUnconfiguredProvider();
            provider.Configure(CreateContext());

            var exception = Assert.Throws<InvalidOperationException>(
                () => provider.Configure(CreateContext()));
            Assert.That(exception!.Message, Does.Contain("already configured"));
        }

        [Test]
        public void OptionsConstructionIsAlreadyConfigured()
        {
            var provider = CreateProvider();

            Assert.That(provider.IsConfigured, Is.True);
            Assert.Throws<InvalidOperationException>(() => provider.Configure(CreateContext()));
        }

        [Test]
        public async Task ConnectTransitionsThroughConnectingToConnected()
        {
            var provider = CreateProvider();
            var observed = new List<NeoRealtimeConnectionState>();
            provider.OnConnectionStateChanged += observed.Add;

            await provider.ConnectAsync();

            Assert.That(provider.State, Is.EqualTo(NeoRealtimeConnectionState.Connected));
            Assert.That(observed, Is.EqualTo(new[]
            {
                NeoRealtimeConnectionState.Connecting,
                NeoRealtimeConnectionState.Connected,
            }));
            Assert.That(socket.AuthProvider, Is.SameAs(provider.JwtProvider));
        }

        [Test]
        public async Task ConnectIsIdempotentWhileConnected()
        {
            var provider = CreateProvider();
            await provider.ConnectAsync();
            await provider.ConnectAsync();

            Assert.That(socket.SetAuthCalls, Is.EqualTo(1));
        }

        [Test]
        public void NotSignedInDuringConnectEntersDeniedAndDropsTheSocket()
        {
            socket.EnsureConnectedImpl =
                () => throw new NeoComposeNotSignedInException("Session expired (test).");
            var provider = CreateProvider();

            Assert.ThrowsAsync<NeoComposeNotSignedInException>(async () => await provider.ConnectAsync());

            Assert.That(provider.State, Is.EqualTo(NeoRealtimeConnectionState.Denied));
            Assert.That(socket.Disposed, Is.True);
        }

        [Test]
        public void TransientConnectFailureReturnsToDisconnected()
        {
            socket.EnsureConnectedImpl =
                () => throw new InvalidOperationException("socket refused (test)");
            var provider = CreateProvider();

            Assert.ThrowsAsync<InvalidOperationException>(async () => await provider.ConnectAsync());

            Assert.That(provider.State, Is.EqualTo(NeoRealtimeConnectionState.Disconnected));
            Assert.That(socket.Disposed, Is.True);
        }

        [Test]
        public async Task SocketStateChangesAreMarshaledThroughTheDispatcher()
        {
            var provider = CreateProvider();
            await provider.ConnectAsync();

            socket.PushState(ConnectionState.Reconnecting);
            Assert.That(
                provider.State,
                Is.EqualTo(NeoRealtimeConnectionState.Connected),
                "state must not change until the dispatcher runs");

            dispatcher.Flush();
            Assert.That(provider.State, Is.EqualTo(NeoRealtimeConnectionState.Connecting));
        }

        [Test]
        public async Task DeniedIgnoresTrailingSocketStates()
        {
            var provider = CreateProvider();
            await provider.ConnectAsync();

            // An authoritative credential rejection recorded by the JWT pipeline…
            http.Responses.Enqueue(new NeoComposeWebResponse(401, false, "", ""));
            Assert.ThrowsAsync<NeoComposeNotSignedInException>(
                () => provider.JwtProvider!.GetTokenAsync());

            // …surfaced through the socket's auth-failed event enters Denied…
            socket.RaiseAuthFailed("Server rejected token (test).");
            dispatcher.Flush();
            Assert.That(provider.State, Is.EqualTo(NeoRealtimeConnectionState.Denied));
            Assert.That(socket.Disposed, Is.True);

            // …and the dying socket's trailing transitions cannot mask it.
            socket.PushState(ConnectionState.Connected);
            dispatcher.Flush();
            Assert.That(provider.State, Is.EqualTo(NeoRealtimeConnectionState.Denied));
        }

        [Test]
        public async Task TransientMintFailureDoesNotEnterDenied()
        {
            var provider = CreateProvider();
            await provider.ConnectAsync();

            http.Responses.Enqueue(new NeoComposeWebResponse(0, true, "", "timeout"));
            Assert.ThrowsAsync<InvalidOperationException>(
                () => provider.JwtProvider!.GetTokenAsync());

            socket.RaiseAuthFailed("Transient mint failure (test).");
            dispatcher.Flush();

            Assert.That(provider.State, Is.EqualTo(NeoRealtimeConnectionState.Connected));
            Assert.That(socket.Disposed, Is.False);
        }

        [Test]
        public async Task DisconnectClearsAuthThenTearsDown()
        {
            var provider = CreateProvider();
            await provider.ConnectAsync();

            await provider.DisconnectAsync();

            Assert.That(socket.ClearAuthCalled, Is.True);
            Assert.That(socket.Disposed, Is.True);
            Assert.That(provider.State, Is.EqualTo(NeoRealtimeConnectionState.Disconnected));
        }

        [Test]
        public async Task ConnectAfterDeniedRetriesWithAFreshSocket()
        {
            socket.EnsureConnectedImpl =
                () => throw new NeoComposeNotSignedInException("Session expired (test).");
            var provider = CreateProvider();
            Assert.ThrowsAsync<NeoComposeNotSignedInException>(async () => await provider.ConnectAsync());

            // Player signs in again; an explicit reconnect is allowed.
            var freshSocket = new FakeRealtimeSocket();
            socket = freshSocket;
            await provider.ConnectAsync();

            Assert.That(provider.State, Is.EqualTo(NeoRealtimeConnectionState.Connected));
            Assert.That(freshSocket.SetAuthCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task DisposeStopsEventDelivery()
        {
            var provider = CreateProvider();
            await provider.ConnectAsync();
            Assert.That(provider.CanCommit, Is.True);

            socket.PushState(ConnectionState.Reconnecting);
            provider.Dispose();
            dispatcher.Flush();

            Assert.That(provider.State, Is.EqualTo(NeoRealtimeConnectionState.Disconnected));
            Assert.That(provider.CanCommit, Is.False);
            Assert.That(socket.Disposed, Is.True);
        }
    }
}
