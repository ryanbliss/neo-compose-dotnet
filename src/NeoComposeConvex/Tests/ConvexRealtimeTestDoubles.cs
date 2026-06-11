// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Convex.Client.Features.Security.Authentication;
using Convex.Client.Infrastructure.Common;
using Convex.Client.Infrastructure.Connection;
using NeoCompose.Runtime;

namespace NeoCompose.Convex.Tests
{
    internal sealed class FakeAccessTokenProvider : INeoComposeAccessTokenProvider
    {
        public string? Token = "session-token";

        public string GetAccessToken(string apiBaseUrl)
        {
            if (Token == null)
            {
                throw new NeoComposeNotSignedInException("Not signed in (test).");
            }

            return Token;
        }

        public bool TryGetAccessToken(string apiBaseUrl, out string token)
        {
            token = Token ?? "";
            return Token != null;
        }
    }

    internal sealed class FakeHttpClient : INeoComposeHttpClient
    {
        public readonly Queue<NeoComposeWebResponse> Responses = new Queue<NeoComposeWebResponse>();

        public readonly List<(string url, string method, string? body, string? bearer)> Requests =
            new List<(string, string, string?, string?)>();

        public Task<NeoComposeWebResponse> SendAsync(
            string url, string method, string? jsonBody, string? bearerToken)
        {
            Requests.Add((url, method, jsonBody, bearerToken));
            if (Responses.Count == 0)
            {
                throw new InvalidOperationException("FakeHttpClient has no queued response.");
            }

            return Task.FromResult(Responses.Dequeue());
        }

        public Task<byte[]> DownloadAsync(string url) =>
            throw new NotSupportedException("Downloads are not part of the JWT mint path.");
    }

    internal sealed class ManualDispatcher
    {
        private readonly List<Action> pending = new List<Action>();

        public int PendingCount => pending.Count;

        public void Dispatch(Action action) => pending.Add(action);

        public void Flush()
        {
            var actions = pending.ToArray();
            pending.Clear();
            foreach (var action in actions)
            {
                action();
            }
        }
    }

    internal sealed class FakeRealtimeSocket : IConvexRealtimeSocket
    {
        private readonly Subject<ConnectionState> stateSubject = new Subject<ConnectionState>();

        public ConnectionState ConnectionState { get; set; } = ConnectionState.Disconnected;

        public IObservable<ConnectionState> ConnectionStateChanges => stateSubject;

        public event EventHandler<AuthenticationStateChangedEventArgs>? AuthenticationStateChanged;

        public IAuthTokenProvider? AuthProvider;
        public int SetAuthCalls;
        public Func<Task>? EnsureConnectedImpl;
        public bool ClearAuthCalled;
        public bool Disposed;

        public Task SetAuthTokenProviderAsync(
            IAuthTokenProvider provider, CancellationToken cancellationToken)
        {
            AuthProvider = provider;
            SetAuthCalls++;
            return Task.CompletedTask;
        }

        public async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            if (EnsureConnectedImpl != null)
            {
                await EnsureConnectedImpl();
                return;
            }

            ConnectionState = ConnectionState.Connected;
        }

        public Task ClearAuthAsync(CancellationToken cancellationToken)
        {
            ClearAuthCalled = true;
            return Task.CompletedTask;
        }

        public void Dispose() => Disposed = true;

        public void PushState(ConnectionState state) => stateSubject.OnNext(state);

        public void RaiseAuthFailed(string message) =>
            AuthenticationStateChanged?.Invoke(
                this,
                new AuthenticationStateChangedEventArgs(
                    AuthenticationState.AuthenticationFailed, message));
    }

    internal static class TestJwt
    {
        public static string WithExpiry(DateTimeOffset expiresAt)
        {
            var payload = $"{{\"exp\":{expiresAt.ToUnixTimeSeconds()}}}";
            return "header." + Base64Url(payload) + ".signature";
        }

        public static string MintResponseJson(string jwt) => $"{{\"token\":\"{jwt}\"}}";

        private static string Base64Url(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
    }
}
