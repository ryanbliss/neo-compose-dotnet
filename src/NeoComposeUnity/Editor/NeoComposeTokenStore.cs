// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using Newtonsoft.Json;
using UnityEditor;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Persists the signed-in Neo Compose Unity user token. Editor-only; never
    /// referenced from the runtime assembly. The access token is stored only in
    /// the OS-native secret store, while non-secret hints are stored separately
    /// so auth UI can render without unlocking the secret store.
    /// </summary>
    public interface INeoComposeTokenStore
    {
        NeoComposeStoredToken? Load();
        void Save(NeoComposeStoredToken token);
        void Clear();
        NeoComposeTokenHint? PeekHint();
    }

    /// <summary>
    /// Stores small, non-secret hint strings keyed by a stable key. Backed by
    /// <c>EditorPrefs</c> in production. Must never hold the access token.
    /// </summary>
    public interface INeoComposeTokenHintStore
    {
        string? Read(string key);
        void Write(string key, string value);
        void Delete(string key);
    }

    public sealed class NeoComposeEditorPrefsTokenHintStore : INeoComposeTokenHintStore
    {
        public string? Read(string key) =>
            EditorPrefs.HasKey(key) ? EditorPrefs.GetString(key) : null;

        public void Write(string key, string value) => EditorPrefs.SetString(key, value);

        public void Delete(string key) => EditorPrefs.DeleteKey(key);
    }

    public sealed class NeoComposeTokenStore : INeoComposeTokenStore
    {
        private const string SecretService = "NeoComposeUnity";
        private const string HintKeyPrefix = "NeoCompose.Unity.TokenHint.";

        private readonly string authBaseUrl;
        private readonly string account;
        private readonly string hintKey;
        private readonly INeoComposeTokenSecretBackend secretBackend;
        private readonly INeoComposeTokenHintStore hintStore;

        public NeoComposeTokenStore(
            string authBaseUrl,
            INeoComposeTokenSecretBackend secretBackend,
            INeoComposeTokenHintStore hintStore)
        {
            if (string.IsNullOrWhiteSpace(authBaseUrl))
            {
                throw new ArgumentException("Auth base URL cannot be empty.", nameof(authBaseUrl));
            }

            this.authBaseUrl = authBaseUrl.Trim();
            this.account = NeoComposeSecretKey.Sanitize(this.authBaseUrl);
            this.hintKey = HintKeyPrefix + this.account;
            this.secretBackend = secretBackend;
            this.hintStore = hintStore;
        }

        public static NeoComposeTokenStore Create(string authBaseUrl) =>
            new NeoComposeTokenStore(
                authBaseUrl,
                NeoComposeTokenSecretBackends.CreateDefault(),
                new NeoComposeEditorPrefsTokenHintStore());

        public NeoComposeStoredToken? Load()
        {
            var hint = PeekHint();
            if (hint == null) return null;

            var accessToken = secretBackend.Read(SecretService, account);
            if (string.IsNullOrWhiteSpace(accessToken)) return null;

            return new NeoComposeStoredToken(
                accessToken!,
                hint.expiresAtUnixSeconds,
                hint.updatedAtUnixSeconds,
                hint.sessionCheckedAtUnixSeconds,
                hint.scopes,
                hint.authBaseUrl,
                hint.displayName,
                hint.displayEmail);
        }

        public void Save(NeoComposeStoredToken token)
        {
            if (!token.HasAccessToken)
            {
                throw new ArgumentException("Token must have an access token.", nameof(token));
            }

            secretBackend.Write(SecretService, account, token.accessToken);
            hintStore.Write(hintKey, JsonConvert.SerializeObject(token.ToHint()));
        }

        public void Clear()
        {
            secretBackend.Delete(SecretService, account);
            hintStore.Delete(hintKey);
        }

        /// <summary>
        /// Reads the non-secret hint without unlocking the secret store. Returns
        /// null when no sign-in is recorded for this auth base URL.
        /// </summary>
        public NeoComposeTokenHint? PeekHint()
        {
            var raw = hintStore.Read(hintKey);
            if (string.IsNullOrWhiteSpace(raw)) return null;

            try
            {
                return JsonConvert.DeserializeObject<NeoComposeTokenHint>(raw!);
            }
            catch (JsonException)
            {
                // Corrupt hint; treat as signed out.
                return null;
            }
        }
    }
}
