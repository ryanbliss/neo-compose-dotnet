// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    public enum NeoComposeAuthState
    {
        SignedOut,
        SignedIn,
        Expired,
    }

    /// <summary>
    /// Owns the editor's authentication state: reads the persisted sign-in,
    /// runs the device flow, reacts to authentication/authorization failures,
    /// and exposes whether auth-sensitive controls should be enabled. The editor
    /// window renders a thin cell over this controller.
    /// </summary>
    public sealed class NeoComposeEditorAuthController
    {
        private readonly Func<string, NeoComposeTokenStore> storeFactory;
        private readonly Func<string, NeoComposeTokenStore, NeoComposeDeviceAuthorizationFlow> flowFactory;
        private readonly INeoComposeTokenRevoker revoker;
        private readonly Func<DateTimeOffset> now;

        private CancellationTokenSource? signInCancellation;

        public NeoComposeEditorAuthController(
            Func<string, NeoComposeTokenStore>? storeFactory = null,
            Func<string, NeoComposeTokenStore, NeoComposeDeviceAuthorizationFlow>? flowFactory = null,
            Func<DateTimeOffset>? now = null,
            INeoComposeTokenRevoker? revoker = null)
        {
            this.storeFactory = storeFactory ?? (apiBaseUrl => NeoComposeTokenStore.Create(apiBaseUrl));
            this.now = now ?? (() => DateTimeOffset.UtcNow);
            this.flowFactory = flowFactory ?? DefaultFlowFactory;
            this.revoker = revoker ?? new NeoComposeTokenRevoker();
        }

        public NeoComposeAuthState State { get; private set; } = NeoComposeAuthState.SignedOut;
        public string DisplayName { get; private set; } = "";
        public string DisplayEmail { get; private set; } = "";

        /// <summary>The most recent authorization (403) message, or null.</summary>
        public string? AuthorizationMessage { get; private set; }

        public bool IsBusy { get; private set; }

        /// <summary>
        /// True only when there is a valid, unexpired sign-in and no sign-in is
        /// in progress. Drives the greying of auth-sensitive controls.
        /// </summary>
        public bool AreAuthSensitiveControlsEnabled =>
            State == NeoComposeAuthState.SignedIn && !IsBusy;

        public bool HasIdentity => DisplayName.Length > 0 || DisplayEmail.Length > 0;

        /// <summary>
        /// Re-reads the persisted sign-in hint for the origin without unlocking
        /// the OS secret store. Safe to call on every repaint.
        /// </summary>
        public void RefreshState(string apiBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                SetSignedOut();
                return;
            }

            var hint = storeFactory(apiBaseUrl).PeekHint();
            if (hint == null)
            {
                SetSignedOut();
                return;
            }

            DisplayName = hint.displayName;
            DisplayEmail = hint.displayEmail;
            State = hint.IsExpired(now()) ? NeoComposeAuthState.Expired : NeoComposeAuthState.SignedIn;
        }

        /// <summary>
        /// Runs the device authorization flow. The user code is surfaced through
        /// <paramref name="onCodeReady"/> so the editor can display it before
        /// approval completes.
        /// </summary>
        public async Task<NeoComposeDeviceAuthResult> SignInAsync(
            string apiBaseUrl,
            Action<NeoComposeDeviceCodeResponse>? onCodeReady)
        {
            if (IsBusy)
            {
                return NeoComposeDeviceAuthResult.Failed(
                    NeoComposeDeviceAuthOutcome.Failed,
                    "A sign-in is already in progress.");
            }

            CancelSignIn();
            signInCancellation = new CancellationTokenSource();
            IsBusy = true;
            AuthorizationMessage = null;
            try
            {
                var store = storeFactory(apiBaseUrl);
                var flow = flowFactory(apiBaseUrl, store);
                var result = await flow.AuthorizeAsync(apiBaseUrl, onCodeReady, signInCancellation.Token);
                return result;
            }
            finally
            {
                IsBusy = false;
                signInCancellation?.Dispose();
                signInCancellation = null;
                RefreshState(apiBaseUrl);
            }
        }

        /// <summary>
        /// Cancels any in-flight sign-in. Called on window close or explicit
        /// sign-out so polling does not outlive the editor session.
        /// </summary>
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
        /// Updates state in response to an editor API failure. A
        /// <see cref="NeoComposeNotSignedInException"/> (401 or proactive expiry)
        /// drops the dead token and moves to the expired/signed-out state. A
        /// <see cref="NeoComposeApiAuthorizationException"/> (403) keeps the user
        /// signed in and records the capability message. Other exceptions are
        /// left to the caller.
        /// </summary>
        public void HandleApiException(string apiBaseUrl, Exception exception)
        {
            switch (exception)
            {
                case NeoComposeNotSignedInException:
                    storeFactory(apiBaseUrl).Clear();
                    // Retain the last known identity for the "session expired"
                    // message when we had one; otherwise fall back to signed-out.
                    State = HasIdentity ? NeoComposeAuthState.Expired : NeoComposeAuthState.SignedOut;
                    break;
                case NeoComposeApiAuthorizationException authorization:
                    AuthorizationMessage = authorization.Message;
                    break;
            }
        }

        public void ClearAuthorizationMessage() => AuthorizationMessage = null;

        /// <summary>
        /// Clears the local sign-in and returns to the signed-out state without
        /// contacting the server.
        /// </summary>
        public void ClearLocal(string apiBaseUrl)
        {
            storeFactory(apiBaseUrl).Clear();
            SetSignedOut();
        }

        /// <summary>
        /// Disconnects: best-effort server-side revoke followed by an
        /// unconditional local clear. Local credentials are always removed, even
        /// when the revoke call fails or times out.
        /// </summary>
        public async Task DisconnectAsync(string apiBaseUrl)
        {
            var store = storeFactory(apiBaseUrl);
            var token = store.Load();
            if (token != null && token.HasAccessToken)
            {
                try
                {
                    await revoker.RevokeAsync(apiBaseUrl, token.accessToken);
                }
                catch (Exception)
                {
                    // Best effort: never let a failed revoke block local clearing.
                }
            }

            store.Clear();
            SetSignedOut();
        }

        private void SetSignedOut()
        {
            State = NeoComposeAuthState.SignedOut;
            DisplayName = "";
            DisplayEmail = "";
        }

        private static NeoComposeDeviceAuthorizationFlow DefaultFlowFactory(
            string apiBaseUrl,
            NeoComposeTokenStore store)
        {
            return new NeoComposeDeviceAuthorizationFlow(
                new NeoComposeDeviceAuthTransport(),
                store,
                () => DateTimeOffset.UtcNow,
                (seconds, token) => Task.Delay(TimeSpan.FromSeconds(seconds), token),
                Application.OpenURL);
        }
    }
}
