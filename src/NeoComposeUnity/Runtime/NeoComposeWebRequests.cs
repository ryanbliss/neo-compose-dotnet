// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// The outcome of a single HTTP request, including the body even for HTTP
    /// error statuses so callers can parse structured error payloads (such as
    /// RFC 8628 device-flow errors or 401/403 API responses).
    /// </summary>
    public readonly struct NeoComposeWebResponse
    {
        public NeoComposeWebResponse(
            long statusCode,
            bool isConnectionError,
            string text,
            string error,
            IReadOnlyDictionary<string, string>? headers = null)
        {
            StatusCode = statusCode;
            IsConnectionError = isConnectionError;
            Text = text;
            Error = error;
            Headers = headers ?? new Dictionary<string, string>();
        }

        /// <summary>HTTP status code, or 0 on a connection error/timeout.</summary>
        public long StatusCode { get; }

        /// <summary>
        /// True for transport-level failures (no HTTP response): connection
        /// errors, timeouts, or data processing errors. HTTP error statuses such
        /// as 401/403/400 are <em>not</em> connection errors.
        /// </summary>
        public bool IsConnectionError { get; }

        public string Text { get; }
        public string Error { get; }
        public IReadOnlyDictionary<string, string> Headers { get; }

        public bool IsSuccessStatus => StatusCode >= 200 && StatusCode < 300;

        public string? GetHeader(string name)
        {
            foreach (var header in Headers)
            {
                if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
                {
                    return header.Value;
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Thin <see cref="UnityWebRequest"/> wrapper that resolves a Task when the
    /// request completes, captures the response body for any HTTP status, and
    /// enforces a hard timeout. Shared by the device-authorization transport and
    /// the editor API client.
    /// </summary>
    public static class NeoComposeWebRequests
    {
        public const int DefaultTimeoutSeconds = 30;

        public static async Task<NeoComposeWebResponse> SendAsync(
            string url,
            string method,
            string? jsonBody = null,
            string? bearerToken = null,
            int timeoutSeconds = DefaultTimeoutSeconds,
            CancellationToken cancellationToken = default)
        {
            using var request = new UnityWebRequest(url, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = timeoutSeconds,
            };
            if (jsonBody != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            // Authentication is bearer-only, so the request must never carry
            // cookies: better-auth Set-Cookies session and cached-JWT cookies
            // on its endpoints, UnityWebRequest's process-wide cookie jar
            // replays them across editor and play mode (and across ports on
            // the same host), and the server resolves identity cookie-first —
            // a play-mode runtime sign-in would hijack the editor's identity
            // (and vice versa). Setting a Cookie header does NOT suppress the
            // jar (the engine appends its own Cookie line regardless), so the
            // jar is cleared for this host before every send. This also drops
            // jar cookies for any non-SDK UnityWebRequest to the same host,
            // which is the intended zero-trust posture for the auth host.
            UnityWebRequest.ClearCookieCache(new Uri(url));
            request.SetRequestHeader("Accept", "application/json");
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.SetRequestHeader("Authorization", "Bearer " + bearerToken);
            }

            await SendOperationAsync(request, timeoutSeconds, cancellationToken);

            var text = request.downloadHandler?.text ?? "";
            var isConnectionError =
                request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError;
            return new NeoComposeWebResponse(
                request.responseCode,
                isConnectionError,
                text,
                request.error ?? "",
                request.GetResponseHeaders());
        }

        /// <summary>
        /// Downloads raw bytes from a pre-signed storage URL. No bearer token is
        /// attached; these URLs carry their own signed authorization.
        /// </summary>
        public static async Task<byte[]> DownloadBytesAsync(
            string url,
            int timeoutSeconds = 120,
            CancellationToken cancellationToken = default)
        {
            using var request = UnityWebRequest.Get(url);
            request.timeout = timeoutSeconds;
            await SendOperationAsync(request, timeoutSeconds, cancellationToken);
            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new InvalidOperationException(
                    $"Neo Compose file download failed ({request.responseCode}) {url}: {request.error}");
            }

            return request.downloadHandler.data;
        }

        private static async Task SendOperationAsync(
            UnityWebRequest request,
            int timeoutSeconds,
            CancellationToken cancellationToken)
        {
            // Always link Unity's exit token so an in-flight request is cancelled
            // when the developer stops Play mode (or the player quits). Without this
            // the UnityWebRequest keeps running, and its completion fires into a
            // torn-down editor — crashing it or flooding the console. The runtime
            // surface is allocation-free Awaitable; this Task layer is the one place
            // that composes a request against a timeout (which Awaitable can't), so
            // it is also where we make HTTP a good citizen on shutdown.
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, Application.exitCancellationToken);
            var token = linked.Token;

            var completion = new TaskCompletionSource<bool>();
            var operation = request.SendWebRequest();
            operation.completed += _ =>
            {
                if (!completion.Task.IsCompleted) completion.SetResult(true);
            };

            // Abort the native request immediately on cancel, not just when the
            // using-scope unwinds, so nothing keeps downloading after Stop.
            using var registration = token.Register(() =>
            {
                if (!request.isDone) request.Abort();
                completion.TrySetCanceled(token);
            });

            var timeout = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds + 1));
            var completed = await Task.WhenAny(completion.Task, timeout);
            if (completed == completion.Task)
            {
                await completion.Task;
                return;
            }

            request.Abort();
            throw new TimeoutException($"HTTP request to {request.url} timed out after {timeoutSeconds} seconds.");
        }
    }
}
