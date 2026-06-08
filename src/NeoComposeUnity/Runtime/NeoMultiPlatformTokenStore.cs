// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Stores the single secret value (the access token) for an account key.
    /// Platform packages implement this over Keychain / Keystore / etc.; the
    /// default is the obfuscated-<c>PlayerPrefs</c> fallback.
    /// </summary>
    public interface INeoAuthSecretStore
    {
        string? Read(string accountKey);
        void Write(string accountKey, string secret);
        void Delete(string accountKey);
    }

    /// <summary>
    /// Runtime <see cref="INeoComposeTokenStore"/> that selects a
    /// platform-appropriate secret backend, so the shared device-flow / refresh
    /// core can persist a player's token without an editor-only concrete.
    /// </summary>
    /// <remarks>
    /// <see cref="ResolveSecretStore"/> is <c>virtual</c>-by-override-point: a
    /// platform package supplies an <see cref="INeoAuthSecretStore"/> backed by
    /// the OS keychain / keystore. The base returns the obfuscated-PlayerPrefs
    /// fallback and logs a one-time warning that the credential is not
    /// hardware-protected on this platform. Non-secret hints always live in
    /// <c>PlayerPrefs</c>.
    ///
    /// <para>The account key is derived via <see cref="NeoAuthCredentialNamespace"/>
    /// from <c>(sharedNamespace, applicationIdentifier, authHost, projectId)</c>,
    /// so different games on one device never share credentials unless they opt
    /// into the same shared namespace.</para>
    /// </remarks>
    public class NeoMultiPlatformTokenStore : INeoComposeTokenStore
    {
        private const string SecretKeyPrefix = "NeoCompose.Auth.Secret.";
        private const string HintKeyPrefix = "NeoCompose.Auth.Hint.";

        private readonly string accountKey;
        private readonly INeoAuthSecretStore secretStore;
        private readonly INeoComposeTokenHintStore hintStore;

        /// <summary>
        /// Construct over explicit backends — used by platform overrides and
        /// tests. Most callers should use <see cref="Create"/>.
        /// </summary>
        public NeoMultiPlatformTokenStore(
            string accountKey,
            INeoAuthSecretStore secretStore,
            INeoComposeTokenHintStore hintStore)
        {
            if (string.IsNullOrWhiteSpace(accountKey))
            {
                throw new ArgumentException("Account key cannot be empty.", nameof(accountKey));
            }
            this.accountKey = accountKey;
            this.secretStore = secretStore;
            this.hintStore = hintStore;
        }

        /// <summary>
        /// Construct for an auth base URL + project, deriving the namespaced
        /// account key and resolving the platform secret backend (warning-backed
        /// <c>PlayerPrefs</c> fallback by default).
        /// </summary>
        public static NeoMultiPlatformTokenStore Create(
            string authBaseUrl,
            string projectId,
            NeoAuthCredentialNamespace? credentialNamespace = null)
        {
            var resolvedNamespace = credentialNamespace ?? NeoAuthCredentialNamespace.ForApplication();
            var account = resolvedNamespace.BuildAccountKey(authBaseUrl, projectId);
            var secretStore = ResolveSecretStore(Application.platform, account);
            return new NeoMultiPlatformTokenStore(
                account,
                secretStore,
                new NeoPlayerPrefsHintStore());
        }

        /// <summary>
        /// Resolve the secret backend for a platform. The base returns the
        /// obfuscated-PlayerPrefs fallback and warns once; platform packages
        /// provide a hardware-backed store (passed to the ctor, or by replacing
        /// this resolver).
        /// </summary>
        protected static INeoAuthSecretStore ResolveSecretStore(
            RuntimePlatform platform,
            string accountKey)
        {
            switch (platform)
            {
                // Platform packages back these with the OS keychain / keystore by
                // constructing the store with their own INeoAuthSecretStore. Until
                // then every platform uses the obfuscated fallback.
                default:
                    Debug.LogWarning(
                        "[NeoCompose] No hardware-backed credential store is configured for " +
                        $"platform '{platform}'. Falling back to obfuscated PlayerPrefs; the " +
                        "access token is not protected by the OS secret store on this device.");
                    return new NeoObfuscatedPlayerPrefsSecretStore();
            }
        }

        public NeoComposeStoredToken? Load()
        {
            var hint = PeekHint();
            if (hint == null) return null;
            var secret = secretStore.Read(SecretKeyPrefix + accountKey);
            if (string.IsNullOrWhiteSpace(secret)) return null;
            return new NeoComposeStoredToken(
                secret!,
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
            secretStore.Write(SecretKeyPrefix + accountKey, token.accessToken);
            hintStore.Write(HintKeyPrefix + accountKey, JsonConvert.SerializeObject(token.ToHint()));
        }

        public void Clear()
        {
            secretStore.Delete(SecretKeyPrefix + accountKey);
            hintStore.Delete(HintKeyPrefix + accountKey);
        }

        public NeoComposeTokenHint? PeekHint()
        {
            var raw = hintStore.Read(HintKeyPrefix + accountKey);
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

    /// <summary>Non-secret hint store backed by <c>PlayerPrefs</c>.</summary>
    public sealed class NeoPlayerPrefsHintStore : INeoComposeTokenHintStore
    {
        public string? Read(string key) =>
            PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;

        public void Write(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// Fallback secret store: AES-encrypts the access token with a device-derived
    /// key and persists the ciphertext in <c>PlayerPrefs</c>. This is obfuscation,
    /// not hardware-backed security — callers are warned at construction. Used
    /// only when no platform keychain/keystore backend is configured.
    /// </summary>
    public sealed class NeoObfuscatedPlayerPrefsSecretStore : INeoAuthSecretStore
    {
        public string? Read(string accountKey)
        {
            if (!PlayerPrefs.HasKey(accountKey)) return null;
            var cipher = PlayerPrefs.GetString(accountKey);
            if (string.IsNullOrWhiteSpace(cipher)) return null;
            try
            {
                return Decrypt(cipher);
            }
            catch (Exception)
            {
                // Corrupt / undecryptable; treat as no secret.
                return null;
            }
        }

        public void Write(string accountKey, string secret)
        {
            PlayerPrefs.SetString(accountKey, Encrypt(secret));
            PlayerPrefs.Save();
        }

        public void Delete(string accountKey)
        {
            PlayerPrefs.DeleteKey(accountKey);
            PlayerPrefs.Save();
        }

        private static byte[] DeriveKey()
        {
            // Device + app derived seed. Obfuscation only; documented as such.
            var seed = SafeDeviceId() + "|" + SafeAppId() + "|neo-compose-secret";
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
        }

        private static string Encrypt(string plaintext)
        {
            using var aes = Aes.Create();
            aes.Key = DeriveKey();
            aes.GenerateIV();
            using var encryptor = aes.CreateEncryptor();
            var plainBytes = Encoding.UTF8.GetBytes(plaintext);
            var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
            // Prefix the IV so decryption can recover it.
            var combined = new byte[aes.IV.Length + cipherBytes.Length];
            Buffer.BlockCopy(aes.IV, 0, combined, 0, aes.IV.Length);
            Buffer.BlockCopy(cipherBytes, 0, combined, aes.IV.Length, cipherBytes.Length);
            return Convert.ToBase64String(combined);
        }

        private static string Decrypt(string cipher)
        {
            var combined = Convert.FromBase64String(cipher);
            using var aes = Aes.Create();
            aes.Key = DeriveKey();
            var iv = new byte[16];
            Buffer.BlockCopy(combined, 0, iv, 0, iv.Length);
            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            var cipherBytes = decryptor.TransformFinalBlock(
                combined, iv.Length, combined.Length - iv.Length);
            return Encoding.UTF8.GetString(cipherBytes);
        }

        private static string SafeDeviceId()
        {
            try
            {
                return SystemInfo.deviceUniqueIdentifier;
            }
            catch (Exception)
            {
                return "unknown-device";
            }
        }

        private static string SafeAppId()
        {
            try
            {
                var id = Application.identifier;
                return string.IsNullOrWhiteSpace(id) ? "unknown-app" : id;
            }
            catch (Exception)
            {
                return "unknown-app";
            }
        }
    }
}
