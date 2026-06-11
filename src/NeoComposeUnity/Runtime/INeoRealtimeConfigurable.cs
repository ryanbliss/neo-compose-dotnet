// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Everything a deferred-configuration realtime provider needs from the
    /// registering store: where the realtime deployment lives, which Neo
    /// Compose origin mints socket credentials, the project, and the signed-in
    /// session those credentials derive from.
    /// </summary>
    public sealed class NeoRealtimeProviderContext
    {
        public NeoRealtimeProviderContext(
            string convexUrl,
            string apiBaseUrl,
            string projectId,
            INeoComposeAccessTokenProvider sessionTokenProvider)
        {
            if (string.IsNullOrWhiteSpace(convexUrl))
            {
                throw new ArgumentException(
                    "Convex deployment URL cannot be empty. Synchronize the project in the " +
                    "editor to receive it (NeoComposeConfig.convexUrl).",
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
            if (sessionTokenProvider == null)
            {
                throw new ArgumentNullException(nameof(sessionTokenProvider));
            }

            ConvexUrl = convexUrl.Trim();
            ApiBaseUrl = apiBaseUrl.Trim();
            ProjectId = projectId.Trim();
            SessionTokenProvider = sessionTokenProvider;
        }

        public string ConvexUrl { get; }

        public string ApiBaseUrl { get; }

        public string ProjectId { get; }

        /// <summary>
        /// The session the provider derives its own socket credential from —
        /// the store passes its authentication's token provider, so sign-in
        /// state stays single-sourced between REST and realtime.
        /// </summary>
        public INeoComposeAccessTokenProvider SessionTokenProvider { get; }
    }

    /// <summary>
    /// Optional deferred configuration for an <see cref="INeoRealtimeProvider"/>.
    /// A provider implementing this can be constructed with no arguments and
    /// registered as-is: the <c>NeoProjectStore</c> configures it from the
    /// project's <see cref="NeoComposeConfig"/> and its own authentication
    /// before first use. Providers constructed with explicit options report
    /// <see cref="IsConfigured"/> true and are left untouched, so the manual
    /// low-level path keeps working.
    /// </summary>
    public interface INeoRealtimeConfigurable
    {
        /// <summary>True once the provider knows its deployment and session source.</summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Injects the shared context. Called at most once by the registering
        /// store, and only while <see cref="IsConfigured"/> is false.
        /// </summary>
        void Configure(NeoRealtimeProviderContext context);
    }
}
