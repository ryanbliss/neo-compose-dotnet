// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;


namespace NeoCompose.Runtime
{
    /// <summary>
    /// Thrown when an authenticated editor request is attempted without a valid,
    /// unexpired signed-in token. Lets callers fail fast and route to sign-in
    /// instead of issuing an unauthenticated request the server will reject.
    /// </summary>
    public sealed class NeoComposeNotSignedInException : Exception
    {
        public NeoComposeNotSignedInException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Supplies the current valid bearer access token for a given origin, or
    /// fails fast when the user is signed out or the token has expired.
    /// </summary>
    public interface INeoComposeAccessTokenProvider
    {
        /// <summary>
        /// Returns a valid, unexpired access token for the origin, or throws
        /// <see cref="NeoComposeNotSignedInException"/>.
        /// </summary>
        string GetAccessToken(string apiBaseUrl);

        bool TryGetAccessToken(string apiBaseUrl, out string token);
    }

    public sealed class NeoComposeTokenStoreAccessTokenProvider : INeoComposeAccessTokenProvider
    {
        private readonly Func<string, INeoComposeTokenStore> storeFactory;
        private readonly Func<DateTimeOffset> now;

        public NeoComposeTokenStoreAccessTokenProvider(
            Func<string, INeoComposeTokenStore> storeFactory,
            Func<DateTimeOffset>? now = null)
        {
            this.storeFactory = storeFactory;
            this.now = now ?? (() => DateTimeOffset.UtcNow);
        }

        public string GetAccessToken(string apiBaseUrl)
        {
            if (TryGetAccessToken(apiBaseUrl, out var token)) return token;
            throw new NeoComposeNotSignedInException(
                "You are not signed in to Neo Compose. Sign in from the Neo Compose window to continue.");
        }

        public bool TryGetAccessToken(string apiBaseUrl, out string token)
        {
            token = "";
            var stored = storeFactory(apiBaseUrl).Load();
            if (stored == null || !stored.HasAccessToken) return false;
            if (stored.IsExpired(now())) return false;

            token = stored.accessToken;
            return true;
        }
    }
}
