// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading;
using NeoCompose.Runtime;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// The editor-side realtime seam (see <c>specs/convex-realtime-sync.md</c>):
    /// live version-metadata change notifications and the export "sync signal"
    /// the hot-reload flow subscribes to. Core stays transport-agnostic — a
    /// realtime plugin's editor assembly supplies the implementation through
    /// <see cref="NeoComposeEditorRealtime.ProviderFactory"/>. All callbacks
    /// arrive on the editor main thread.
    /// </summary>
    public interface INeoComposeEditorRealtimeProvider : IDisposable
    {
        NeoRealtimeConnectionState State { get; }

        event Action<NeoRealtimeConnectionState>? OnConnectionStateChanged;

        Awaitable ConnectAsync(CancellationToken cancellationToken = default);

        Awaitable DisconnectAsync();

        /// <summary>
        /// Fires when the project's version/channel metadata changes; the window
        /// re-runs its existing REST metadata load (signal-then-pull — the
        /// subscription never carries the payload the editor renders).
        /// </summary>
        IDisposable SubscribeVersionMetadata(string projectId, Action onChanged);

        /// <summary>
        /// Fires with the version's head transaction whenever an edit is
        /// committed remotely; null while the version has no transactions.
        /// </summary>
        IDisposable SubscribeExportSignal(
            string projectId, string versionId, Action<NeoComposeExportSignal?> onChanged);
    }

    /// <summary>The export sync signal: a version's head transaction.</summary>
    public sealed class NeoComposeExportSignal
    {
        public string versionId = "";
        public string transactionId = "";
        public string transactionHash = "";
        public long transactionAt;
    }

    /// <summary>Everything a plugin needs to build an editor realtime provider.</summary>
    public sealed class NeoComposeEditorRealtimeContext
    {
        public NeoComposeEditorRealtimeContext(
            string apiBaseUrl,
            string convexUrl,
            string projectId,
            INeoComposeAccessTokenProvider sessionTokenProvider)
        {
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                throw new ArgumentException("API base URL cannot be empty.", nameof(apiBaseUrl));
            }
            if (string.IsNullOrWhiteSpace(convexUrl))
            {
                throw new ArgumentException("Convex deployment URL cannot be empty.", nameof(convexUrl));
            }
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException("Project id cannot be empty.", nameof(projectId));
            }

            this.apiBaseUrl = apiBaseUrl;
            this.convexUrl = convexUrl;
            this.projectId = projectId;
            this.sessionTokenProvider = sessionTokenProvider
                ?? throw new ArgumentNullException(nameof(sessionTokenProvider));
        }

        public string apiBaseUrl { get; }
        public string convexUrl { get; }
        public string projectId { get; }
        public INeoComposeAccessTokenProvider sessionTokenProvider { get; }
    }

    /// <summary>
    /// Registration point. A realtime plugin's editor assembly assigns
    /// <see cref="ProviderFactory"/> (typically from <c>[InitializeOnLoad]</c>)
    /// so the Neo Compose window can bring realtime up with zero project setup;
    /// null means no plugin is installed and the editor stays REST-only.
    /// </summary>
    public static class NeoComposeEditorRealtime
    {
        public static Func<NeoComposeEditorRealtimeContext, INeoComposeEditorRealtimeProvider>?
            ProviderFactory
        { get; set; }
    }
}
