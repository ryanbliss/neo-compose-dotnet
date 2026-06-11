// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading;
using System.Text.Json;
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

        public IDisposable ObserveQuery(
            string functionName,
            object args,
            Action<string> onJson,
            Action<Exception> onError)
        {
            return client
                .Observe<JsonElement, object>(functionName, args)
                .Subscribe(new JsonObserver(onJson, onError));
        }

        public async Task<string> MutateAsync(
            string functionName, object args, CancellationToken cancellationToken)
        {
            var result = await client
                .Mutate<JsonElement>(functionName)
                .WithArgs(args)
                .ExecuteAsync(cancellationToken);
            return result.GetRawText();
        }

        public void Dispose() => client.Dispose();

        private sealed class JsonObserver : IObserver<JsonElement>
        {
            private readonly Action<string> onJson;
            private readonly Action<Exception> onError;

            public JsonObserver(Action<string> onJson, Action<Exception> onError)
            {
                this.onJson = onJson;
                this.onError = onError;
            }

            public void OnNext(JsonElement value) => onJson(value.GetRawText());

            public void OnError(Exception error) => onError(error);

            public void OnCompleted()
            {
            }
        }
    }
}
