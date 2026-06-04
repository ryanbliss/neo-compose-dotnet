// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Drives the OAuth 2.0 Device Authorization Grant for the
    /// <c>neo-compose-unity</c> client: requests a device code, surfaces the
    /// user code, opens the verification page, polls for approval, fetches the
    /// signed-in identity, and persists the resulting token.
    /// </summary>
    /// <remarks>
    /// The clock, delay, and browser-open behaviors are injected so the poller
    /// can be unit-tested deterministically without real time or a real browser.
    /// </remarks>
    public sealed class NeoComposeDeviceAuthorizationFlow
    {
        private const int SlowDownIncrementSeconds = 5;
        private const int MinimumIntervalSeconds = 1;
        private const int FallbackExpirySeconds = 600;

        private readonly INeoComposeDeviceAuthTransport transport;
        private readonly INeoComposeTokenStore tokenStore;
        private readonly string clientId;
        private readonly string scopes;
        private readonly Func<DateTimeOffset> now;
        private readonly Func<int, CancellationToken, Task> delaySeconds;
        private readonly Action<string> openVerificationUri;

        public NeoComposeDeviceAuthorizationFlow(
            INeoComposeDeviceAuthTransport transport,
            INeoComposeTokenStore tokenStore,
            Func<DateTimeOffset> now,
            Func<int, CancellationToken, Task> delaySeconds,
            Action<string> openVerificationUri,
            string clientId = NeoComposeEditorDefaults.OAuthClientId,
            string scopes = NeoComposeEditorDefaults.OAuthScopes)
        {
            this.transport = transport;
            this.tokenStore = tokenStore;
            this.now = now;
            this.delaySeconds = delaySeconds;
            this.openVerificationUri = openVerificationUri;
            this.clientId = clientId;
            this.scopes = scopes;
        }

        public static NeoComposeDeviceAuthorizationFlow Create(
            string apiBaseUrl,
            INeoComposeDeviceAuthTransport? transport = null)
        {
            return new NeoComposeDeviceAuthorizationFlow(
                transport ?? new NeoComposeDeviceAuthTransport(),
                NeoComposeTokenStore.Create(apiBaseUrl),
                () => DateTimeOffset.UtcNow,
                (seconds, token) => Task.Delay(TimeSpan.FromSeconds(seconds), token),
                Application.OpenURL);
        }

        /// <summary>
        /// Runs the full device authorization flow against the given origin.
        /// <paramref name="onCodeReady"/> is invoked once the user code is
        /// available so the editor can display it before approval completes.
        /// </summary>
        public async Task<NeoComposeDeviceAuthResult> AuthorizeAsync(
            string apiBaseUrl,
            Action<NeoComposeDeviceCodeResponse>? onCodeReady,
            CancellationToken cancellationToken)
        {
            NeoComposeDeviceCodeResponse code;
            try
            {
                code = await transport.RequestDeviceCodeAsync(apiBaseUrl, clientId, scopes, cancellationToken);
            }
            catch (NeoComposeDeviceAuthException exception)
            {
                return NeoComposeDeviceAuthResult.Failed(NeoComposeDeviceAuthOutcome.Failed, exception.Message);
            }

            onCodeReady?.Invoke(code);
            openVerificationUri(NeoComposeAuthEndpoints.ResolveVerificationUri(apiBaseUrl, code));

            var expirySeconds = code.expiresInSeconds > 0 ? code.expiresInSeconds : FallbackExpirySeconds;
            var deadline = now().AddSeconds(expirySeconds);
            var intervalSeconds = Math.Max(code.intervalSeconds, MinimumIntervalSeconds);

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return NeoComposeDeviceAuthResult.Failed(NeoComposeDeviceAuthOutcome.Canceled, "Sign-in canceled.");
                }

                try
                {
                    await delaySeconds(intervalSeconds, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return NeoComposeDeviceAuthResult.Failed(NeoComposeDeviceAuthOutcome.Canceled, "Sign-in canceled.");
                }

                if (now() >= deadline)
                {
                    return NeoComposeDeviceAuthResult.Failed(
                        NeoComposeDeviceAuthOutcome.TimedOut,
                        "The sign-in request expired before it was approved. Please try again.");
                }

                var poll = await transport.PollDeviceTokenAsync(apiBaseUrl, clientId, code.deviceCode, cancellationToken);
                switch (poll.status)
                {
                    case NeoComposeDevicePollStatus.Pending:
                        continue;
                    case NeoComposeDevicePollStatus.SlowDown:
                        intervalSeconds += SlowDownIncrementSeconds;
                        continue;
                    case NeoComposeDevicePollStatus.Denied:
                        return NeoComposeDeviceAuthResult.Failed(
                            NeoComposeDeviceAuthOutcome.Denied,
                            "The sign-in request was denied.");
                    case NeoComposeDevicePollStatus.Expired:
                        return NeoComposeDeviceAuthResult.Failed(
                            NeoComposeDeviceAuthOutcome.Expired,
                            "The sign-in request expired before it was approved. Please try again.");
                    case NeoComposeDevicePollStatus.Error:
                        return NeoComposeDeviceAuthResult.Failed(NeoComposeDeviceAuthOutcome.Failed, poll.message);
                    case NeoComposeDevicePollStatus.Success:
                        return await CompleteAsync(apiBaseUrl, poll.token!, cancellationToken);
                    default:
                        return NeoComposeDeviceAuthResult.Failed(
                            NeoComposeDeviceAuthOutcome.Failed,
                            "Unexpected device authorization state.");
                }
            }
        }

        private async Task<NeoComposeDeviceAuthResult> CompleteAsync(
            string apiBaseUrl,
            NeoComposeDeviceTokenSuccess token,
            CancellationToken cancellationToken)
        {
            var profile = await transport.GetProfileAsync(apiBaseUrl, token.accessToken, cancellationToken);
            var grantedScopes = SplitScopes(token.scope);
            var expirySeconds = token.expiresInSeconds > 0 ? token.expiresInSeconds : FallbackExpirySeconds;
            var issuedAt = now().ToUnixTimeSeconds();
            var expiresAt = issuedAt + expirySeconds;

            var stored = new NeoComposeStoredToken(
                token.accessToken,
                expiresAt,
                issuedAt,
                grantedScopes,
                NeoComposeAuthEndpoints.Origin(apiBaseUrl),
                profile.name,
                profile.email);
            tokenStore.Save(stored);
            return NeoComposeDeviceAuthResult.Success(stored);
        }

        private string[] SplitScopes(string scope)
        {
            var source = string.IsNullOrWhiteSpace(scope) ? scopes : scope;
            return source.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
