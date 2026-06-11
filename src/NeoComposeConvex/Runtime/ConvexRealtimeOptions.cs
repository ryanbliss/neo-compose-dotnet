// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime;

namespace NeoCompose.Convex
{
    /// <summary>
    /// Configuration for <see cref="ConvexRealtimeProvider"/>: where the Convex
    /// deployment lives, which Neo Compose origin mints the socket JWT, and the
    /// signed-in session the JWT derives from.
    /// </summary>
    /// <remarks>
    /// The session token source comes from the host's authentication —
    /// <c>NeoAuthentication.AccessTokenProvider</c> at runtime,
    /// <c>NeoComposeEditorAuthController.CreateAccessTokenProvider()</c> in the
    /// editor. The transport and clock are injectable for deterministic tests.
    /// </remarks>
    public sealed class ConvexRealtimeOptions
    {
        public ConvexRealtimeOptions(
            string convexUrl,
            string apiBaseUrl,
            string projectId,
            INeoComposeAccessTokenProvider sessionTokenProvider,
            INeoComposeHttpClient? httpClient = null,
            Func<DateTimeOffset>? now = null)
        {
            if (string.IsNullOrWhiteSpace(convexUrl))
            {
                throw new ArgumentException(
                    "Convex deployment URL cannot be empty. Set NeoComposeConfig.convexUrl to the " +
                    "project's deployment URL (https://<deployment>.convex.cloud).",
                    nameof(convexUrl));
            }
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                throw new ArgumentException("API base URL cannot be empty.", nameof(apiBaseUrl));
            }
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
            }

            this.convexUrl = convexUrl.Trim().TrimEnd('/');
            this.apiBaseUrl = apiBaseUrl.Trim().TrimEnd('/');
            this.projectId = projectId.Trim();
            this.sessionTokenProvider = sessionTokenProvider
                ?? throw new ArgumentNullException(nameof(sessionTokenProvider));
            this.httpClient = httpClient;
            this.now = now;
        }

        public string convexUrl { get; }

        public string apiBaseUrl { get; }

        public string projectId { get; }

        public INeoComposeAccessTokenProvider sessionTokenProvider { get; }

        /// <summary>Transport override for tests; null uses the Unity web-request transport.</summary>
        public INeoComposeHttpClient? httpClient { get; }

        /// <summary>Clock override for tests; null uses <see cref="DateTimeOffset.UtcNow"/>.</summary>
        public Func<DateTimeOffset>? now { get; }
    }
}
