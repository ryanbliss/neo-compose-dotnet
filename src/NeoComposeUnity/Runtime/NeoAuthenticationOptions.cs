// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Configuration for the runtime <see cref="NeoAuthentication"/> device flow:
    /// the origin to authenticate against, the project the credentials are
    /// namespaced under, and the per-project runtime OAuth client + scopes the
    /// device flow requests.
    /// </summary>
    /// <remarks>
    /// At runtime these are sourced from <c>NeoComposeConfig</c>
    /// (<c>runtimeOAuthClientId</c> / <c>runtimeOAuthScopes</c>, wired in a later
    /// phase); the controller itself stays config-agnostic so it can be unit
    /// tested with explicit options.
    /// </remarks>
    public sealed class NeoAuthenticationOptions
    {
        public NeoAuthenticationOptions(
            string apiBaseUrl,
            string projectId,
            string clientId,
            string scopes,
            string? sharedCredentialNamespace = null,
            int overallTimeoutSeconds = DefaultOverallTimeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                throw new ArgumentException("API base URL cannot be empty.", nameof(apiBaseUrl));
            }
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
            }
            if (string.IsNullOrWhiteSpace(clientId))
            {
                throw new ArgumentException(
                    "Runtime OAuth client id cannot be empty. Enable runtime OAuth for the " +
                    "project and sync its config.",
                    nameof(clientId));
            }
            if (string.IsNullOrWhiteSpace(scopes))
            {
                throw new ArgumentException("Runtime OAuth scopes cannot be empty.", nameof(scopes));
            }

            this.apiBaseUrl = apiBaseUrl.Trim();
            this.projectId = projectId.Trim();
            this.clientId = clientId.Trim();
            this.scopes = scopes.Trim();
            this.sharedCredentialNamespace = sharedCredentialNamespace;
            this.overallTimeoutSeconds =
                overallTimeoutSeconds > 0 ? overallTimeoutSeconds : DefaultOverallTimeoutSeconds;
        }

        /// <summary>Default overall sign-in deadline (seconds) — caps the device flow.</summary>
        public const int DefaultOverallTimeoutSeconds = 600;

        public string apiBaseUrl { get; }
        public string projectId { get; }
        public string clientId { get; }
        public string scopes { get; }

        /// <summary>
        /// Optional override of the credential-sharing namespace seed (see
        /// <see cref="NeoAuthCredentialNamespace"/>); leave null for per-game
        /// isolation.
        /// </summary>
        public string? sharedCredentialNamespace { get; }

        /// <summary>
        /// Overall sign-in deadline in seconds. The device flow also honors the
        /// server's device-code expiry; this is an additional client-side cap.
        /// </summary>
        public int overallTimeoutSeconds { get; }
    }
}
