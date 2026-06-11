// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Convex.Client;
using Convex.Client.Features.Security.Authentication;
using Convex.Client.Infrastructure.Common;
using Convex.Client.Infrastructure.Connection;

namespace NeoCompose.Convex
{
    /// <inheritdoc cref="IConvexRealtimeSocket"/>
    internal sealed class ConvexClientRealtimeSocket : IConvexRealtimeSocket
    {
        private readonly ConvexClient client;

        public ConvexClientRealtimeSocket(string convexUrl)
        {
            client = new ConvexClient(convexUrl);
        }

        public ConnectionState ConnectionState => client.ConnectionState;

        public IObservable<ConnectionState> ConnectionStateChanges => client.ConnectionStateChanges;

        public event EventHandler<AuthenticationStateChangedEventArgs>? AuthenticationStateChanged
        {
            add => client.Auth.AuthenticationStateChanged += value;
            remove => client.Auth.AuthenticationStateChanged -= value;
        }

        public Task SetAuthTokenProviderAsync(
            IAuthTokenProvider provider, CancellationToken cancellationToken) =>
            client.Auth.SetAuthTokenProviderAsync(provider, cancellationToken);

        public Task EnsureConnectedAsync(CancellationToken cancellationToken) =>
            client.EnsureConnectedAsync(cancellationToken);

        public Task ClearAuthAsync(CancellationToken cancellationToken) =>
            client.Auth.ClearAuthAsync(cancellationToken);

        public void Dispose() => client.Dispose();
    }
}
