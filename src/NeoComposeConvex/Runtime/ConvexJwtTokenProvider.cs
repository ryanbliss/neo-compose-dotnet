// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Convex.Client.Infrastructure.Common;
using NeoCompose.Runtime;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Convex
{
    /// <summary>
    /// Mints the short-lived Convex JWT the websocket authenticates with, from
    /// the signed-in device-flow session token, via better-auth's
    /// <c>GET {apiBaseUrl}/api/auth/convex/token</c>. The JWT carries the
    /// session id, so the backend's central scope enforcement applies to the
    /// socket exactly as it does to REST.
    /// </summary>
    /// <remarks>
    /// The minted JWT is cached until <see cref="ExpirySlackSeconds"/> before
    /// its <c>exp</c> claim; the vendored client asks this provider for a token
    /// whenever it (re)authenticates the socket, so refresh is demand-driven
    /// rather than a parallel timer. Thread-safe: socket worker threads and the
    /// main thread may call <see cref="GetTokenAsync"/> concurrently.
    /// </remarks>
    public sealed class ConvexJwtTokenProvider : IAuthTokenProvider
    {
        /// <summary>Refresh margin before the JWT's expiry.</summary>
        public const int ExpirySlackSeconds = 60;

        private readonly string apiBaseUrl;
        private readonly INeoComposeAccessTokenProvider sessionTokenProvider;
        private readonly INeoComposeHttpClient httpClient;
        private readonly Func<DateTimeOffset> now;
        private readonly SemaphoreSlim mintLock = new SemaphoreSlim(1, 1);
        private readonly object cacheGate = new object();

        private string? cachedJwt;
        private DateTimeOffset cachedJwtExpiresAt;
        private bool lastFailureWasAuthRejection;

        public ConvexJwtTokenProvider(
            string apiBaseUrl,
            INeoComposeAccessTokenProvider sessionTokenProvider,
            INeoComposeHttpClient? httpClient = null,
            Func<DateTimeOffset>? now = null)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                throw new ArgumentException("API base URL cannot be empty.", nameof(apiBaseUrl));
            }

            this.apiBaseUrl = apiBaseUrl.Trim().TrimEnd('/');
            this.sessionTokenProvider = sessionTokenProvider
                ?? throw new ArgumentNullException(nameof(sessionTokenProvider));
            this.httpClient = httpClient ?? new NeoComposeUnityHttpClient();
            this.now = now ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// True when the most recent mint attempt failed because the credential
        /// itself was rejected (signed out locally, or the server returned 401)
        /// rather than a transient transport/protocol failure. The realtime
        /// provider uses this to decide between Denied and retryable.
        /// </summary>
        internal bool LastFailureWasAuthRejection
        {
            get
            {
                lock (cacheGate)
                {
                    return lastFailureWasAuthRejection;
                }
            }
        }

        /// <summary>Drops the cached JWT (sign-out, forced re-auth).</summary>
        public void Invalidate()
        {
            lock (cacheGate)
            {
                cachedJwt = null;
                cachedJwtExpiresAt = default;
            }
        }

        public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default)
        {
            if (TryGetCachedJwt(out var cached)) return cached;

            await mintLock.WaitAsync(cancellationToken);
            try
            {
                if (TryGetCachedJwt(out cached)) return cached;
                return await MintAsync();
            }
            finally
            {
                mintLock.Release();
            }
        }

        private bool TryGetCachedJwt(out string jwt)
        {
            lock (cacheGate)
            {
                if (cachedJwt != null
                    && now() < cachedJwtExpiresAt - TimeSpan.FromSeconds(ExpirySlackSeconds))
                {
                    jwt = cachedJwt;
                    return true;
                }
            }

            jwt = "";
            return false;
        }

        private async Task<string> MintAsync()
        {
            string sessionToken;
            try
            {
                // Throws NeoComposeNotSignedInException when signed out/expired —
                // fail fast before any network call.
                sessionToken = sessionTokenProvider.GetAccessToken(apiBaseUrl);
            }
            catch (NeoComposeNotSignedInException)
            {
                RecordFailure(authRejection: true);
                throw;
            }

            var url = $"{apiBaseUrl}/api/auth/convex/token";
            NeoComposeWebResponse response;
            try
            {
                response = await httpClient.SendAsync(url, "GET", null, sessionToken);
            }
            catch (Exception exception)
            {
                RecordFailure(authRejection: false);
                throw new InvalidOperationException(
                    $"Convex JWT request to {url} failed before a response was received.", exception);
            }

            if (response.IsConnectionError)
            {
                RecordFailure(authRejection: false);
                throw new InvalidOperationException(
                    $"Convex JWT request failed (connection) {url}: {response.Error}");
            }

            if (response.StatusCode == 401)
            {
                RecordFailure(authRejection: true);
                throw new NeoComposeNotSignedInException(
                    "Your Neo Compose session was rejected while minting a Convex realtime token. " +
                    "Sign in again to continue.");
            }

            if (!response.IsSuccessStatus)
            {
                RecordFailure(authRejection: false);
                throw new InvalidOperationException(
                    $"Convex JWT request failed with HTTP {response.StatusCode} {url}: {Snippet(response.Text)}");
            }

            string? jwt;
            try
            {
                jwt = (string?)JObject.Parse(response.Text)["token"];
            }
            catch (Exception exception)
            {
                RecordFailure(authRejection: false);
                throw new InvalidOperationException(
                    $"Convex JWT response from {url} is not valid JSON: {Snippet(response.Text)}", exception);
            }

            if (string.IsNullOrEmpty(jwt))
            {
                RecordFailure(authRejection: false);
                throw new InvalidOperationException(
                    $"Convex JWT response from {url} did not contain a \"token\" field.");
            }

            DateTimeOffset expiresAt;
            try
            {
                expiresAt = ReadJwtExpiry(jwt!);
            }
            catch (Exception)
            {
                RecordFailure(authRejection: false);
                throw;
            }

            lock (cacheGate)
            {
                cachedJwt = jwt;
                cachedJwtExpiresAt = expiresAt;
                lastFailureWasAuthRejection = false;
            }

            return jwt!;
        }

        private void RecordFailure(bool authRejection)
        {
            lock (cacheGate)
            {
                cachedJwt = null;
                cachedJwtExpiresAt = default;
                lastFailureWasAuthRejection = authRejection;
            }
        }

        private static string Snippet(string text)
        {
            const int maxLength = 200;
            if (text.Length <= maxLength) return text;
            return text.Substring(0, maxLength) + "…";
        }

        /// <summary>
        /// Reads the <c>exp</c> claim from a JWT payload. No signature
        /// validation — the value only schedules the client-side refresh; the
        /// server validates the token cryptographically.
        /// </summary>
        internal static DateTimeOffset ReadJwtExpiry(string jwt)
        {
            var segments = jwt.Split('.');
            if (segments.Length != 3)
            {
                throw new InvalidOperationException(
                    $"Convex JWT is malformed: expected 3 dot-separated segments, found {segments.Length}.");
            }

            string payloadJson;
            try
            {
                payloadJson = Encoding.UTF8.GetString(DecodeBase64Url(segments[1]));
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException(
                    "Convex JWT payload segment is not valid base64url.", exception);
            }

            JObject payload;
            try
            {
                payload = JObject.Parse(payloadJson);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Convex JWT payload is not valid JSON.", exception);
            }

            var exp = payload["exp"];
            if (exp == null)
            {
                throw new InvalidOperationException(
                    "Convex JWT payload has no \"exp\" claim; cannot schedule refresh.");
            }

            return DateTimeOffset.FromUnixTimeSeconds((long)exp);
        }

        private static byte[] DecodeBase64Url(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
            }

            return Convert.FromBase64String(padded);
        }
    }
}
