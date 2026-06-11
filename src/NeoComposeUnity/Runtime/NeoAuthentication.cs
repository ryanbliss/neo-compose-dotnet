// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public enum NeoAuthenticationState
    {
        SignedOut,

        /// <summary>
        /// A device-authorization sign-in is in progress and the user code has
        /// been surfaced; waiting for the player to approve in the browser.
        /// </summary>
        AwaitingUserAuthorization,

        SignedIn,

        /// <summary>A token exists but has expired; re-authentication required.</summary>
        Expired,
    }

    /// <summary>
    /// Owns the runtime player's authentication state. Runs the OAuth 2.0 Device
    /// Authorization Grant for the project's runtime OAuth client, persists the
    /// token through a <see cref="NeoMultiPlatformTokenStore"/>, performs rolling
    /// session refresh, and signs out (best-effort revoke + always clear).
    /// </summary>
    /// <remarks>
    /// Reuses the shared device-flow / refresh core
    /// (<see cref="NeoComposeDeviceAuthorizationFlow"/>,
    /// <see cref="NeoComposeSessionRefresher"/>,
    /// <see cref="NeoComposeTokenStoreAccessTokenProvider"/>) — the editor and
    /// runtime differ only in their token store and OAuth client. The clock,
    /// flow, refresher, and revoker are injectable for deterministic tests.
    /// </remarks>
    public sealed class NeoAuthentication
    {
        private readonly NeoAuthenticationOptions options;
        private readonly INeoComposeTokenStore store;
        private readonly Func<NeoComposeDeviceAuthorizationFlow> flowFactory;
        private readonly INeoComposeSessionRefresher sessionRefresher;
        private readonly INeoComposeTokenRevoker revoker;
        private readonly INeoComposeAccessTokenProvider accessTokenProvider;
        private readonly Func<DateTimeOffset> now;

        private CancellationTokenSource? signInCancellation;

        public NeoAuthentication(
            NeoAuthenticationOptions options,
            INeoComposeTokenStore? store = null,
            Func<NeoComposeDeviceAuthorizationFlow>? flowFactory = null,
            INeoComposeSessionRefresher? sessionRefresher = null,
            INeoComposeTokenRevoker? revoker = null,
            Func<DateTimeOffset>? now = null)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.now = now ?? (() => DateTimeOffset.UtcNow);
            this.store = store ?? CreateDefaultStore(options);
            this.flowFactory = flowFactory ?? CreateDefaultFlow;
            this.sessionRefresher =
                sessionRefresher ?? new NeoComposeSessionRefresher(_ => this.store);
            this.revoker = revoker ?? new NeoComposeTokenRevoker();
            this.accessTokenProvider =
                new NeoComposeTokenStoreAccessTokenProvider(_ => this.store, this.now);
            RefreshState();
        }

        private NeoAuthenticationState state = NeoAuthenticationState.SignedOut;

        public NeoAuthenticationState State
        {
            get => state;
            private set
            {
                if (state == value) return;
                state = value;
                OnStateChanged?.Invoke(value);
            }
        }

        /// <summary>
        /// Raised whenever <see cref="State"/> changes value (sign-in,
        /// sign-out, expiry). Session-dependent transports hang off this — the
        /// project store connects its realtime provider on
        /// <see cref="NeoAuthenticationState.SignedIn"/> and tears it down on
        /// sign-out/expiry, so no socket outlives its credential.
        /// </summary>
        public event Action<NeoAuthenticationState>? OnStateChanged;

        public string DisplayName { get; private set; } = "";
        public string DisplayEmail { get; private set; } = "";
        public bool IsBusy { get; private set; }

        public bool IsSignedIn => State == NeoAuthenticationState.SignedIn;
        public bool HasIdentity => DisplayName.Length > 0 || DisplayEmail.Length > 0;

        /// <summary>
        /// Raised once the device code is available so the game can show the user
        /// code + verification URI (RFC 8628). State is
        /// <see cref="NeoAuthenticationState.AwaitingUserAuthorization"/> while
        /// the player approves.
        /// </summary>
        public event Action<NeoComposeDeviceCodeResponse>? OnDeviceAuthorizationPrompt;

        /// <summary>A valid, unexpired bearer token for API calls, or null.</summary>
        public string? CurrentAccessToken =>
            accessTokenProvider.TryGetAccessToken(options.apiBaseUrl, out var token) ? token : null;

        /// <summary>
        /// The token provider bound to this authentication's store. The seam
        /// optional plugins (e.g. Convex realtime sync) use to derive their own
        /// credentials from the signed-in session without touching credential
        /// storage directly.
        /// </summary>
        public INeoComposeAccessTokenProvider AccessTokenProvider => accessTokenProvider;

        public bool TryGetAccessToken(out string token) =>
            accessTokenProvider.TryGetAccessToken(options.apiBaseUrl, out token);

        /// <summary>
        /// Builds a <see cref="NeoApiClient"/> bound to this authentication's token
        /// provider and rolling session refresher, targeting the configured project.
        /// The returned client attaches the player's bearer token and fails fast when
        /// signed out. This is how the runtime turns a signed-in (or
        /// sign-in-capable) authentication into the cloud save transport.
        /// </summary>
        /// <param name="httpClient">
        /// Optional transport override for tests; production uses the default Unity
        /// web-request transport.
        /// </param>
        public NeoApiClient CreateApiClient(INeoComposeHttpClient? httpClient = null)
        {
            return new NeoApiClient(
                options.apiBaseUrl,
                options.projectId,
                accessTokenProvider,
                httpClient ?? new NeoComposeUnityHttpClient(),
                sessionRefresher);
        }

        /// <summary>
        /// Re-reads the persisted sign-in hint without unlocking the secret store.
        /// </summary>
        public void RefreshState()
        {
            var hint = store.PeekHint();
            if (hint == null)
            {
                SetSignedOut();
                return;
            }
            DisplayName = hint.displayName;
            DisplayEmail = hint.displayEmail;
            State = hint.IsExpired(now())
                ? NeoAuthenticationState.Expired
                : NeoAuthenticationState.SignedIn;
        }

        /// <summary>Rolling session refresh; re-reads state after.</summary>
        public async Awaitable<bool> RefreshSessionIfDueAsync()
        {
            try
            {
                var refreshed = await sessionRefresher.RefreshIfDueAsync(options.apiBaseUrl);
                RefreshState();
                return refreshed;
            }
            catch (NeoComposeNotSignedInException exception)
            {
                HandleApiException(exception);
                return false;
            }
        }

        /// <summary>
        /// Runs the device authorization flow, capped by
        /// <see cref="NeoAuthenticationOptions.overallTimeoutSeconds"/> on top of
        /// the server's device-code expiry. Surfaces the user code through
        /// <see cref="OnDeviceAuthorizationPrompt"/>.
        /// </summary>
        public async Awaitable<NeoComposeDeviceAuthResult> SignInAsync(
            CancellationToken cancellationToken = default)
        {
            if (IsBusy)
            {
                return NeoComposeDeviceAuthResult.Failed(
                    NeoComposeDeviceAuthOutcome.Failed,
                    "A sign-in is already in progress.");
            }

            CancelSignIn();
            // Link Unity's exit token so the device-poll loop (and its inter-poll
            // delay) stops the moment Play mode exits — never poll into Edit mode.
            signInCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, Application.exitCancellationToken);
            signInCancellation.CancelAfter(TimeSpan.FromSeconds(options.overallTimeoutSeconds));
            IsBusy = true;
            try
            {
                var flow = flowFactory();
                return await flow.AuthorizeAsync(
                    options.apiBaseUrl,
                    OnCodeReady,
                    signInCancellation.Token);
            }
            finally
            {
                IsBusy = false;
                signInCancellation?.Dispose();
                signInCancellation = null;
                RefreshState();
            }
        }

        /// <summary>Cancels any in-flight sign-in.</summary>
        public void CancelSignIn()
        {
            try
            {
                signInCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already completed.
            }
        }

        /// <summary>
        /// Signs out: best-effort server-side revoke followed by an unconditional
        /// local clear. Local credentials are always removed, even when the
        /// revoke call fails or times out.
        /// </summary>
        public async Awaitable SignOutAsync()
        {
            var token = store.Load();
            if (token != null && token.HasAccessToken)
            {
                try
                {
                    await revoker.RevokeAsync(options.apiBaseUrl, token.accessToken);
                }
                catch (Exception)
                {
                    // Best effort: never let a failed revoke block local clearing.
                }
            }
            store.Clear();
            SetSignedOut();
        }

        /// <summary>
        /// Updates state in response to an API failure. A
        /// <see cref="NeoComposeNotSignedInException"/> (401 / proactive expiry)
        /// drops the dead token and moves to expired / signed-out.
        /// </summary>
        public void HandleApiException(Exception exception)
        {
            if (exception is NeoComposeNotSignedInException)
            {
                store.Clear();
                State = HasIdentity
                    ? NeoAuthenticationState.Expired
                    : NeoAuthenticationState.SignedOut;
            }
        }

        private void OnCodeReady(NeoComposeDeviceCodeResponse code)
        {
            State = NeoAuthenticationState.AwaitingUserAuthorization;
            OnDeviceAuthorizationPrompt?.Invoke(code);
        }

        private void SetSignedOut()
        {
            // Clear the identity before raising the state event so subscribers
            // observe a fully signed-out authentication.
            DisplayName = "";
            DisplayEmail = "";
            State = NeoAuthenticationState.SignedOut;
        }

        private static INeoComposeTokenStore CreateDefaultStore(NeoAuthenticationOptions options)
        {
            var credentialNamespace = options.sharedCredentialNamespace == null
                ? null
                : NeoAuthCredentialNamespace.ForApplication(options.sharedCredentialNamespace);
            return NeoMultiPlatformTokenStore.Create(
                options.apiBaseUrl,
                options.projectId,
                credentialNamespace);
        }

        private NeoComposeDeviceAuthorizationFlow CreateDefaultFlow()
        {
            return new NeoComposeDeviceAuthorizationFlow(
                new NeoComposeDeviceAuthTransport(),
                store,
                now,
                (seconds, token) => Task.Delay(TimeSpan.FromSeconds(seconds), token),
                Application.OpenURL,
                options.clientId,
                options.scopes);
        }
    }
}
