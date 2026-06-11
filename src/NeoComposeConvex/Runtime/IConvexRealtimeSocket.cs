// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Convex.Client.Features.Security.Authentication;
using Convex.Client.Infrastructure.Common;
using Convex.Client.Infrastructure.Connection;

namespace NeoCompose.Convex
{
    /// <summary>
    /// The narrow slice of the vendored Convex client the realtime provider
    /// actually drives, so provider behavior (state transitions, denial,
    /// teardown) is unit-testable against a fake instead of the full client.
    /// </summary>
    internal interface IConvexRealtimeSocket : IDisposable
    {
        ConnectionState ConnectionState { get; }

        IObservable<ConnectionState> ConnectionStateChanges { get; }

        event EventHandler<AuthenticationStateChangedEventArgs>? AuthenticationStateChanged;

        Task SetAuthTokenProviderAsync(IAuthTokenProvider provider, CancellationToken cancellationToken);

        Task EnsureConnectedAsync(CancellationToken cancellationToken);

        Task ClearAuthAsync(CancellationToken cancellationToken);
    }
}
