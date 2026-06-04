// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// A persisted Neo Compose Unity OAuth bearer token plus the non-secret
    /// metadata needed to render auth UI and validate expiry.
    /// </summary>
    /// <remarks>
    /// The <see cref="accessToken"/> is the only secret. Everything else is
    /// non-secret hint material that can be persisted separately so the editor
    /// can render the signed-in identity and expiry without unlocking the OS
    /// secret store. This is a user-account token: it covers every project the
    /// signed-in user can currently access, and the web backend re-validates
    /// project policies on every call.
    /// </remarks>
    public sealed class NeoComposeStoredToken
    {
        public NeoComposeStoredToken(
            string accessToken,
            long expiresAtUnixSeconds,
            string[] scopes,
            string authBaseUrl,
            string displayName,
            string displayEmail)
        {
            this.accessToken = accessToken;
            this.expiresAtUnixSeconds = expiresAtUnixSeconds;
            this.scopes = scopes;
            this.authBaseUrl = authBaseUrl;
            this.displayName = displayName;
            this.displayEmail = displayEmail;
        }

        /// <summary>
        /// The bearer access token. This is the secret value and must only ever
        /// be persisted through the OS-native secret store.
        /// </summary>
        public string accessToken { get; }

        /// <summary>
        /// Absolute expiry as a Unix timestamp in seconds. The device flow does
        /// not issue refresh tokens, so expiry forces re-authentication.
        /// </summary>
        public long expiresAtUnixSeconds { get; }

        /// <summary>
        /// The scopes granted to this token.
        /// </summary>
        public string[] scopes { get; }

        /// <summary>
        /// The auth base URL that issued this token. Tokens are keyed per auth
        /// base URL so localhost and production credentials never collide.
        /// </summary>
        public string authBaseUrl { get; }

        /// <summary>
        /// Signed-in user display name from <c>profile:read</c>, for UI only.
        /// </summary>
        public string displayName { get; }

        /// <summary>
        /// Signed-in user email from <c>profile:read</c>, for UI only.
        /// </summary>
        public string displayEmail { get; }

        public bool HasAccessToken => !string.IsNullOrWhiteSpace(accessToken);

        public bool IsExpired(DateTimeOffset now) =>
            expiresAtUnixSeconds <= now.ToUnixTimeSeconds();

        /// <summary>
        /// The non-secret hint subset of this token, safe to persist outside the
        /// OS secret store.
        /// </summary>
        public NeoComposeTokenHint ToHint() =>
            new NeoComposeTokenHint(
                expiresAtUnixSeconds,
                scopes,
                authBaseUrl,
                displayName,
                displayEmail);
    }

    /// <summary>
    /// The non-secret subset of a stored token, used to render auth UI without
    /// unlocking the OS secret store. Never contains the access token.
    /// </summary>
    public sealed class NeoComposeTokenHint
    {
        public NeoComposeTokenHint(
            long expiresAtUnixSeconds,
            string[] scopes,
            string authBaseUrl,
            string displayName,
            string displayEmail)
        {
            this.expiresAtUnixSeconds = expiresAtUnixSeconds;
            this.scopes = scopes;
            this.authBaseUrl = authBaseUrl;
            this.displayName = displayName;
            this.displayEmail = displayEmail;
        }

        public long expiresAtUnixSeconds { get; }
        public string[] scopes { get; }
        public string authBaseUrl { get; }
        public string displayName { get; }
        public string displayEmail { get; }

        public bool IsExpired(DateTimeOffset now) =>
            expiresAtUnixSeconds <= now.ToUnixTimeSeconds();
    }
}
