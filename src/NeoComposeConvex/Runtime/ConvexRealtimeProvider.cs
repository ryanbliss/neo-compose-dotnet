// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using Convex.Client.Features.Security.Authentication;
using Convex.Client.Infrastructure.Connection;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NeoCompose.Convex
{
    /// <summary>
    /// Owns one authenticated websocket connection to the project's Convex
    /// deployment: mints/refreshes the socket JWT from the signed-in session
    /// (via <see cref="ConvexJwtTokenProvider"/>), tracks connection state, and
    /// tears down with sign-out. Subscriptions and the commit path plug in
    /// during later phases (see <c>specs/convex-realtime-sync.md</c>).
    /// </summary>
    /// <remarks>
    /// Transient drops are retried by the underlying client's reconnection
    /// policy (exponential backoff with jitter) and surface here as
    /// <see cref="NeoRealtimeConnectionState.Connecting"/>. A credential
    /// rejection (signed out, server 401) is terminal: the provider enters
    /// <see cref="NeoRealtimeConnectionState.Denied"/>, drops the socket, and
    /// never retries on its own — a fresh <see cref="ConnectAsync"/> (after
    /// e.g. re-sign-in) is required. State events are raised on the thread the
    /// provider was constructed on (Unity main thread in production).
    /// </remarks>
    public sealed class ConvexRealtimeProvider : INeoRealtimeProvider
    {
        private readonly Func<IConvexRealtimeSocket> socketFactory;
        private readonly Action<Action> dispatch;
        private readonly string projectId;

        private IConvexRealtimeSocket? socket;
        private IDisposable? stateSubscription;
        private bool disposed;

        public ConvexRealtimeProvider(ConvexRealtimeOptions options)
            : this(options, socketFactory: null, dispatcher: null)
        {
        }

        internal ConvexRealtimeProvider(
            ConvexRealtimeOptions options,
            Func<IConvexRealtimeSocket>? socketFactory,
            Action<Action>? dispatcher)
        {
#if UNITY_WEBGL
            throw new NotSupportedException(
                "Convex realtime sync is not supported on WebGL: the transport requires " +
                "System.Net.WebSockets, which Unity WebGL does not provide.");
#else
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            JwtProvider = new ConvexJwtTokenProvider(
                options.apiBaseUrl, options.sessionTokenProvider, options.httpClient, options.now);
            projectId = options.projectId;
            this.socketFactory = socketFactory
                ?? (() => new ConvexClientRealtimeSocket(options.convexUrl));
            this.dispatch = dispatcher ?? CreateContextDispatcher();
#endif
        }

        /// <summary>The JWT mint/refresh pipeline backing the socket's auth.</summary>
        internal ConvexJwtTokenProvider JwtProvider { get; }

        public NeoRealtimeConnectionState State { get; private set; } =
            NeoRealtimeConnectionState.Disconnected;

        public event Action<NeoRealtimeConnectionState>? OnConnectionStateChanged;

        /// <summary>
        /// Connects (or reconnects after an explicit disconnect / denial). No-op
        /// while already connecting or connected.
        /// </summary>
        public async Awaitable ConnectAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (State == NeoRealtimeConnectionState.Connecting
                || State == NeoRealtimeConnectionState.Connected)
            {
                return;
            }

            SetState(NeoRealtimeConnectionState.Connecting);
            try
            {
                EnsureSocket();
                await socket!.SetAuthTokenProviderAsync(JwtProvider, cancellationToken);
                await socket.EnsureConnectedAsync(cancellationToken);
                SetState(MapConnectionState(socket.ConnectionState));
            }
            catch (NeoComposeNotSignedInException)
            {
                EnterDenied();
                throw;
            }
            catch
            {
                TearDownSocket();
                SetState(NeoRealtimeConnectionState.Disconnected);
                throw;
            }
        }

        /// <summary>
        /// Sign-out teardown: best-effort auth clear on the socket, then an
        /// unconditional local teardown. Call before clearing the token store so
        /// no socket outlives its credential.
        /// </summary>
        public async Awaitable DisconnectAsync()
        {
            ThrowIfDisposed();
            var current = socket;
            if (current != null)
            {
                try
                {
                    await current.ClearAuthAsync(CancellationToken.None);
                }
                catch (Exception exception)
                {
                    // Best effort: never let a failed remote clear block local
                    // teardown, but leave a trace for debugging.
                    Debug.LogWarning(
                        $"[NeoCompose] Convex realtime auth clear failed during disconnect; " +
                        $"tearing the socket down anyway. " +
                        $"{exception.GetType().Name}: {exception.Message}");
                }
            }

            TearDownSocket();
            JwtProvider.Invalidate();
            SetState(NeoRealtimeConnectionState.Disconnected);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            TearDownSocket();
        }

        /// <summary>True when connected; the synchronizer's transport check.</summary>
        public bool CanCommit => State == NeoRealtimeConnectionState.Connected;

        public IDisposable SubscribeSaveList(
            string? targetReleaseChannelId, Action<NeoSaveFileList> onChanged)
        {
            if (onChanged == null)
            {
                throw new ArgumentNullException(nameof(onChanged));
            }

            return SubscribeCore(
                "gameSaves:list",
                new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["targetReleaseChannelId"] = targetReleaseChannelId,
                },
                ParseSaveFileList,
                onChanged);
        }

        public IDisposable SubscribeSaveHead(string customId, Action<RemoteGameSave> onChanged)
        {
            if (string.IsNullOrWhiteSpace(customId))
            {
                throw new ArgumentException("Save customId cannot be empty.", nameof(customId));
            }
            if (onChanged == null)
            {
                throw new ArgumentNullException(nameof(onChanged));
            }

            return SubscribeCore(
                "gameSaves:get",
                new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["customId"] = customId,
                },
                ParseRemoteGameSave,
                onChanged);
        }

        public async Awaitable<NeoCommitResult> CommitAsync(
            NeoSaveCommitRequest request, bool replaceSnapshot)
        {
            ThrowIfDisposed();
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var current = socket;
            if (current == null || State != NeoRealtimeConnectionState.Connected)
            {
                throw new InvalidOperationException(
                    "Realtime commit requires a connected provider; check CanCommit first.");
            }

            var args = new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["save"] = ToJsonElement(request),
                ["replaceSnapshot"] = replaceSnapshot,
            };
            var json = await current.MutateAsync("gameSaves:commit", args, CancellationToken.None);
            return ParseCommitResult(json);
        }

        /// <summary>
        /// Raw-JSON subscription for sibling assemblies (the editor facade)
        /// building non-save subscriptions on the same connection machinery.
        /// Same gating and dispatcher semantics as the typed subscriptions.
        /// </summary>
        internal IDisposable SubscribeRaw(
            string functionName, Dictionary<string, object?> args, Action<string> onJson)
        {
            if (onJson == null)
            {
                throw new ArgumentNullException(nameof(onJson));
            }

            return SubscribeCore(functionName, args, json => json, onJson);
        }

        /// <summary>
        /// Shared subscription plumbing: skip (inert) while not connected, parse
        /// pushed JSON into the core DTO, and deliver on the dispatcher. Parse
        /// and subscription errors are logged, never thrown into the socket.
        /// </summary>
        private IDisposable SubscribeCore<T>(
            string functionName,
            Dictionary<string, object?> args,
            Func<string, T> parse,
            Action<T> onChanged)
        {
            ThrowIfDisposed();
            var current = socket;
            if (current == null || State != NeoRealtimeConnectionState.Connected)
            {
                Debug.LogWarning(
                    $"[NeoCompose] Realtime subscription to {functionName} skipped: the provider " +
                    "is not connected. It re-attaches on the next Connected transition.");
                return EmptyDisposable.Instance;
            }

            return current.ObserveQuery(
                functionName,
                args,
                json => dispatch(() =>
                {
                    if (disposed) return;
                    T value;
                    try
                    {
                        value = parse(json);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            $"[NeoCompose] Realtime payload from {functionName} could not be " +
                            $"parsed: {exception.Message}");
                        return;
                    }

                    onChanged(value);
                }),
                error => dispatch(() =>
                {
                    if (disposed) return;
                    Debug.LogWarning(
                        $"[NeoCompose] Realtime subscription to {functionName} errored: " +
                        $"{error.GetType().Name}: {error.Message}");
                }));
        }

        private static NeoSaveFileList ParseSaveFileList(string json)
        {
            var list = JsonConvert.DeserializeObject<NeoSaveFileList>(json);
            if (list == null)
            {
                throw new InvalidOperationException(
                    "Realtime save list payload deserialized to null.");
            }

            return list;
        }

        private static RemoteGameSave ParseRemoteGameSave(string json)
        {
            var save = JsonConvert.DeserializeObject<RemoteGameSave>(json);
            if (save == null)
            {
                throw new InvalidOperationException(
                    "Realtime save head payload deserialized to null.");
            }

            return save;
        }

        /// <summary>
        /// Newtonsoft-serialized core DTO re-parsed as a System.Text.Json element
        /// so the vendored serializer transmits it verbatim — the two JSON stacks
        /// never disagree about the wire shape.
        /// </summary>
        private static System.Text.Json.JsonElement ToJsonElement(object value)
        {
            var json = JsonConvert.SerializeObject(value);
            using (var document = System.Text.Json.JsonDocument.Parse(json))
            {
                return document.RootElement.Clone();
            }
        }

        private static NeoCommitResult ParseCommitResult(string json)
        {
            var result = JObject.Parse(json);
            var kind = (string?)result["kind"];
            if (kind == null)
            {
                throw new InvalidOperationException(
                    "Realtime commit result did not include a \"kind\" field.");
            }

            if (kind == "committed")
            {
                var save = result["save"];
                if (save == null)
                {
                    throw new InvalidOperationException(
                        "Realtime commit result was committed but did not include the save.");
                }

                return NeoCommitResult.Committed(ParseRemoteGameSave(save.ToString()));
            }

            if (kind == "conflict")
            {
                var serverHead = result["serverHead"];
                if (serverHead == null)
                {
                    throw new InvalidOperationException(
                        "Realtime commit result was a conflict but did not include the server head.");
                }

                return NeoCommitResult.Conflict(ParseRemoteGameSave(serverHead.ToString()));
            }

            throw new InvalidOperationException(
                $"Realtime commit result had an unknown kind \"{kind}\".");
        }

        private sealed class EmptyDisposable : IDisposable
        {
            public static readonly EmptyDisposable Instance = new EmptyDisposable();

            public void Dispose()
            {
            }
        }

        private void EnsureSocket()
        {
            if (socket != null) return;
            socket = socketFactory();
            stateSubscription = socket.ConnectionStateChanges.Subscribe(
                new ConnectionStateObserver(OnSocketConnectionState));
            socket.AuthenticationStateChanged += OnSocketAuthenticationStateChanged;
        }

        private void OnSocketConnectionState(ConnectionState state)
        {
            dispatch(() =>
            {
                if (disposed) return;
                // Denied is terminal until an explicit ConnectAsync; the dying
                // socket's trailing transitions must not mask it.
                if (State == NeoRealtimeConnectionState.Denied) return;
                SetState(MapConnectionState(state));
            });
        }

        private void OnSocketAuthenticationStateChanged(
            object? sender, AuthenticationStateChangedEventArgs e)
        {
            if (e.State != AuthenticationState.AuthenticationFailed) return;
            dispatch(() =>
            {
                if (disposed) return;
                // Only a credential rejection is terminal; a transient mint
                // failure (connection blip) leaves the client's own retry loop
                // in charge.
                if (JwtProvider.LastFailureWasAuthRejection)
                {
                    EnterDenied();
                }
            });
        }

        private void EnterDenied()
        {
            TearDownSocket();
            JwtProvider.Invalidate();
            SetState(NeoRealtimeConnectionState.Denied);
        }

        private void TearDownSocket()
        {
            stateSubscription?.Dispose();
            stateSubscription = null;
            var current = socket;
            if (current == null) return;
            socket = null;
            current.AuthenticationStateChanged -= OnSocketAuthenticationStateChanged;
            current.Dispose();
        }

        private void SetState(NeoRealtimeConnectionState value)
        {
            if (State == value) return;
            State = value;
            OnConnectionStateChanged?.Invoke(value);
        }

        private static NeoRealtimeConnectionState MapConnectionState(ConnectionState state)
        {
            switch (state)
            {
                case ConnectionState.Connected:
                    return NeoRealtimeConnectionState.Connected;
                case ConnectionState.Connecting:
                case ConnectionState.Reconnecting:
                    return NeoRealtimeConnectionState.Connecting;
                case ConnectionState.Disconnected:
                case ConnectionState.Failed:
                    return NeoRealtimeConnectionState.Disconnected;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(state), state, "Unhandled Convex connection state.");
            }
        }

        private static Action<Action> CreateContextDispatcher()
        {
            var context = SynchronizationContext.Current;
            if (context == null)
            {
                return action => action();
            }

            return action => context.Post(state => ((Action)state!)(), action);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ConvexRealtimeProvider));
            }
        }

        private sealed class ConnectionStateObserver : IObserver<ConnectionState>
        {
            private readonly Action<ConnectionState> onNext;

            public ConnectionStateObserver(Action<ConnectionState> onNext)
            {
                this.onNext = onNext;
            }

            public void OnNext(ConnectionState value) => onNext(value);

            public void OnError(Exception error)
            {
                // The connection-state stream erroring is itself a disconnect
                // signal; the socket's own state still governs.
            }

            public void OnCompleted()
            {
            }
        }
    }
}
