// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Security.Cryptography;
using System.Text;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Builds the stable, namespaced account key under which a player's Neo
    /// Compose credentials are stored on a device.
    /// </summary>
    /// <remarks>
    /// The key is composed from <c>(sharedNamespace, applicationIdentifier,
    /// authHost, projectId)</c> and hashed (SHA-256, hex) so it is opaque and
    /// safe for any key/value or file-backed store. Two different games on the
    /// same device therefore never read each other's tokens (different
    /// <c>applicationIdentifier</c>), and localhost vs production credentials
    /// never collide (different <c>authHost</c>).
    ///
    /// <para>The <c>sharedNamespace</c> seed is overridable: games that
    /// intentionally want to share a sign-in (e.g. a suite of titles from one
    /// studio) can set the same non-default namespace so the composed key
    /// matches across them.</para>
    /// </remarks>
    public sealed class NeoAuthCredentialNamespace
    {
        /// <summary>The default shared-namespace seed when none is overridden.</summary>
        public const string DefaultSharedNamespace = "neo-compose";

        private readonly string sharedNamespace;
        private readonly string applicationIdentifier;

        public NeoAuthCredentialNamespace(
            string applicationIdentifier,
            string? sharedNamespace = null)
        {
            this.applicationIdentifier = Normalize(applicationIdentifier);
            this.sharedNamespace = string.IsNullOrWhiteSpace(sharedNamespace)
                ? DefaultSharedNamespace
                : sharedNamespace!.Trim();
        }

        /// <summary>
        /// Builds the namespacing helper from the running app, defaulting the
        /// application identifier to <see cref="UnityEngine.Application.identifier"/>.
        /// </summary>
        public static NeoAuthCredentialNamespace ForApplication(
            string? sharedNamespace = null,
            string? applicationIdentifier = null) =>
            new NeoAuthCredentialNamespace(
                applicationIdentifier ?? SafeApplicationIdentifier(),
                sharedNamespace);

        /// <summary>
        /// The hashed account key for a given auth base URL + project. The auth
        /// host (not the full URL) is used so path/trailing-slash variants of the
        /// same origin resolve to one account.
        /// </summary>
        public string BuildAccountKey(string authBaseUrl, string projectId)
        {
            var authHost = ExtractHost(authBaseUrl);
            var composed = string.Join(
                "", // unit separator — cannot appear in the inputs
                sharedNamespace,
                applicationIdentifier,
                authHost,
                Normalize(projectId));
            return Hash(composed);
        }

        private static string ExtractHost(string authBaseUrl)
        {
            if (string.IsNullOrWhiteSpace(authBaseUrl)) return "";
            var trimmed = authBaseUrl.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            {
                // Host + port distinguishes localhost:3000 from localhost:4000.
                return uri.IsDefaultPort ? uri.Host : uri.Host + ":" + uri.Port;
            }
            return Normalize(trimmed).TrimEnd('/');
        }

        private static string Normalize(string value) =>
            string.IsNullOrWhiteSpace(value) ? "" : value.Trim().ToLowerInvariant();

        private static string Hash(string value)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }

        private static string SafeApplicationIdentifier()
        {
            try
            {
                var id = UnityEngine.Application.identifier;
                return string.IsNullOrWhiteSpace(id) ? "unknown-app" : id;
            }
            catch (Exception)
            {
                // Application.identifier can throw outside the player/editor
                // lifecycle (e.g. some test contexts); degrade gracefully.
                return "unknown-app";
            }
        }
    }
}
