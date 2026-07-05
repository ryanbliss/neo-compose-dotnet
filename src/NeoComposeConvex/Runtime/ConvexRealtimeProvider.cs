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
    public sealed class ConvexRealtimeProvider : INeoRealtimeProvider, INeoRealtimeConfigurable
    {
        private readonly Func<IConvexRealtimeSocket>? socketFactoryOverride;
        private readonly Action<Action> dispatch;

        private Func<IConvexRealtimeSocket>? socketFactory;
        private string? projectId;
        private IConvexRealtimeSocket? socket;
        private IDisposable? stateSubscription;
        private bool disposed;

        /// <summary>
        /// The zero-configuration path: register the provider with a
        /// <c>NeoProjectStore</c> and the store injects the project's Convex
        /// URL, API origin, and signed-in session from its own config (see
        /// <see cref="INeoRealtimeConfigurable"/>).
        /// </summary>
        public ConvexRealtimeProvider()
            : this(options: null, socketFactory: null, dispatcher: null)
        {
        }

        /// <summary>The manual path: explicit options, no store configuration.</summary>
        public ConvexRealtimeProvider(ConvexRealtimeOptions options)
            : this(
                options ?? throw new ArgumentNullException(nameof(options)),
                socketFactory: null,
                dispatcher: null)
        {
        }

        internal ConvexRealtimeProvider(
            ConvexRealtimeOptions? options,
            Func<IConvexRealtimeSocket>? socketFactory,
            Action<Action>? dispatcher)
        {
#if UNITY_WEBGL
            throw new NotSupportedException(
                "Convex realtime sync is not supported on WebGL: the transport requires " +
                "System.Net.WebSockets, which Unity WebGL does not provide.");
#else
            socketFactoryOverride = socketFactory;
            this.dispatch = dispatcher ?? CreateContextDispatcher();
            if (options != null)
            {
                ApplyOptions(options);
            }
#endif
        }

        /// <summary>
        /// The JWT mint/refresh pipeline backing the socket's auth; null until
        /// the provider is configured.
        /// </summary>
        internal ConvexJwtTokenProvider? JwtProvider { get; private set; }

        /// <summary>
        /// True once the provider knows its Convex deployment and session
        /// source — either constructed with <see cref="ConvexRealtimeOptions"/>
        /// or configured by the registering store.
        /// </summary>
        public bool IsConfigured => JwtProvider != null;

        /// <summary>
        /// Deferred configuration (see <see cref="INeoRealtimeConfigurable"/>):
        /// the registering <c>NeoProjectStore</c> injects the shared context so
        /// <c>new ConvexRealtimeProvider()</c> needs no arguments.
        /// </summary>
        public void Configure(NeoRealtimeProviderContext context)
        {
            ThrowIfDisposed();
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (IsConfigured)
            {
                throw new InvalidOperationException(
                    "ConvexRealtimeProvider is already configured. Construct it with " +
                    "ConvexRealtimeOptions or let the registering NeoProjectStore configure " +
                    "it — not both.");
            }

            ApplyOptions(new ConvexRealtimeOptions(
                context.ConvexUrl,
                context.ApiBaseUrl,
                context.ProjectId,
                context.SessionTokenProvider));
        }

        private void ApplyOptions(ConvexRealtimeOptions options)
        {
            JwtProvider = new ConvexJwtTokenProvider(
                options.apiBaseUrl, options.sessionTokenProvider, options.httpClient, options.now);
            projectId = options.projectId;
            socketFactory = socketFactoryOverride
                ?? (() => new ConvexClientRealtimeSocket(options.convexUrl));
        }

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
            if (!IsConfigured)
            {
                throw new InvalidOperationException(
                    "ConvexRealtimeProvider is not configured: register it with a " +
                    "NeoProjectStore (which injects the project's Convex URL and signed-in " +
                    "session from its config) or construct it with ConvexRealtimeOptions " +
                    "before connecting.");
            }
            if (State == NeoRealtimeConnectionState.Connecting
                || State == NeoRealtimeConnectionState.Connected)
            {
                return;
            }

            SetState(NeoRealtimeConnectionState.Connecting);
            try
            {
                EnsureSocket();
                await socket!.SetAuthTokenProviderAsync(JwtProvider!, cancellationToken);
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
            JwtProvider?.Invalidate();
            SetState(NeoRealtimeConnectionState.Disconnected);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            TearDownSocket();
            SetState(NeoRealtimeConnectionState.Disconnected);
        }

        /// <summary>True when connected; the synchronizer's transport check.</summary>
        public bool CanCommit => !disposed && State == NeoRealtimeConnectionState.Connected;

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
                ["save"] = ToWireArgs(request),
                ["replaceSnapshot"] = replaceSnapshot,
            };
            var json = await current.MutateAsync("gameSaves:commit", args, CancellationToken.None);
            return ParseCommitResult(json);
        }

        public async Awaitable<NeoCommitResult> ForkLiveAsync(NeoLiveForkRequest request)
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
                    "Realtime live fork requires a connected provider; check CanCommit first.");
            }

            var args = new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["save"] = ToWireArgs(request),
            };
            var json = await current.MutateAsync(
                "gameSaves:forkLiveSnapshot", args, CancellationToken.None);
            return ParseCommitResult(json);
        }

        public async Awaitable<NeoLivePatchResult> PatchLiveAsync(NeoLivePatchRequest request)
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
                    "Realtime live patch requires a connected provider; check CanCommit first.");
            }

            var args = new Dictionary<string, object?>
            {
                ["projectId"] = projectId,
                ["customId"] = request.customId,
                ["snapshotId"] = request.snapshotId,
                ["patch"] = ToWireArgs(request.patch),
            };
            var json = await current.MutateAsync(
                "gameSaves:patchLiveSnapshot", args, CancellationToken.None);
            return ParseLivePatchResult(json);
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
            var list = JsonConvert.DeserializeObject<NeoSaveFileList>(
                json, NeoSaveJson.ContentSettings);
            if (list == null)
            {
                throw new InvalidOperationException(
                    "Realtime save list payload deserialized to null.");
            }

            return list;
        }

        /// <summary>Parses a mutation result envelope without Newtonsoft's date
        /// auto-detection: nested save payloads re-serialize via ToString() and
        /// any date-looking value strings must survive byte for byte.</summary>
        private static JObject ParseResultEnvelope(string json)
        {
            using (var reader = new JsonTextReader(new System.IO.StringReader(json))
            {
                DateParseHandling = DateParseHandling.None,
            })
            {
                return JObject.Load(reader);
            }
        }

        private static NeoLivePatchResult ParseLivePatchResult(string json)
        {
            var result = ParseResultEnvelope(json);
            var kind = (string?)result["kind"];
            if (kind == null)
            {
                throw new InvalidOperationException(
                    "Realtime live patch result did not include a \"kind\" field.");
            }

            if (kind == "patched")
            {
                var snapshotId = (string?)result["snapshotId"];
                if (snapshotId == null)
                {
                    throw new InvalidOperationException(
                        "Realtime live patch result was patched but did not include the snapshotId.");
                }

                var snapshotHash = (string?)result["snapshotHash"];
                if (snapshotHash == null)
                {
                    throw new InvalidOperationException(
                        "Realtime live patch result was patched but did not include the snapshotHash.");
                }

                var synchronizedAt = result["synchronizedAt"];
                if (synchronizedAt == null)
                {
                    throw new InvalidOperationException(
                        "Realtime live patch result was patched but did not include synchronizedAt.");
                }

                return NeoLivePatchResult.Patched(
                    snapshotId,
                    snapshotHash,
                    synchronizedAt.ToObject<NeoTimestamp>());
            }

            if (kind == "staleTarget")
            {
                var serverHead = result["serverHead"];
                if (serverHead == null)
                {
                    throw new InvalidOperationException(
                        "Realtime live patch result was a stale target but did not include the server head.");
                }

                return NeoLivePatchResult.StaleTarget(ParseRemoteGameSave(serverHead.ToString()));
            }

            throw new InvalidOperationException(
                $"Realtime live patch result had an unknown kind \"{kind}\".");
        }

        private static RemoteGameSave ParseRemoteGameSave(string json)
        {
            var save = JsonConvert.DeserializeObject<RemoteGameSave>(
                json, NeoSaveJson.ContentSettings);
            if (save == null)
            {
                throw new InvalidOperationException(
                    "Realtime save head payload deserialized to null.");
            }

            return save;
        }

        /// <summary>
        /// Newtonsoft-serialized core DTO converted into the plain
        /// dictionary/list/primitive graph the vendored Convex serializer
        /// transmits verbatim. The vendored serializer reflects public
        /// <b>properties</b> of unknown objects (our DTOs expose fields) and
        /// knows nothing about <c>JsonElement</c> or <c>JToken</c> — both leak
        /// wrong shapes onto the wire (a <c>JsonElement</c> arrives as
        /// <c>{"valueKind":…}</c>). Plain dictionaries are its lingua franca.
        /// </summary>
        internal static object? ToWireArgs(object value)
        {
            return ToPlainGraph(JToken.FromObject(value));
        }

        private static object? ToPlainGraph(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                {
                    var result = new Dictionary<string, object?>();
                    foreach (var property in ((JObject)token).Properties())
                    {
                        result[property.Name] = ToPlainGraph(property.Value);
                    }

                    return result;
                }
                case JTokenType.Array:
                {
                    var result = new List<object?>();
                    foreach (var item in (JArray)token)
                    {
                        result.Add(ToPlainGraph(item));
                    }

                    return result;
                }
                case JTokenType.Integer:
                    // Convex wire numbers are float64: a CLR long would be
                    // encoded as a bigint ($integer) and rejected by v.number()
                    // validators. Integral JSON round-trips losslessly within
                    // double precision, same as JSON itself.
                    return token.ToObject<double>();
                case JTokenType.Float:
                    return token.ToObject<double>();
                case JTokenType.Boolean:
                    return token.ToObject<bool>();
                case JTokenType.String:
                    return token.ToObject<string>();
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return null;
                case JTokenType.Date:
                case JTokenType.Guid:
                case JTokenType.TimeSpan:
                case JTokenType.Uri:
                    // Defensive: content parsed before the DateParseHandling.None
                    // chokepoints (e.g. an old local-store file read by a previous
                    // SDK version) can still carry coerced tokens. Emit them as the
                    // JSON string Newtonsoft itself would have written, instead of
                    // failing the whole flush.
                    return JsonConvert.SerializeObject(token).Trim('"');
                default:
                    throw new InvalidOperationException(
                        $"Realtime args contained an unsupported JSON token of type {token.Type}.");
            }
        }

        private static NeoCommitResult ParseCommitResult(string json)
        {
            var result = ParseResultEnvelope(json);
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
            // Only reachable from ConnectAsync, after the IsConfigured guard.
            socket = socketFactory!();
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
                if (JwtProvider is { LastFailureWasAuthRejection: true })
                {
                    EnterDenied();
                }
            });
        }

        private void EnterDenied()
        {
            TearDownSocket();
            JwtProvider?.Invalidate();
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
