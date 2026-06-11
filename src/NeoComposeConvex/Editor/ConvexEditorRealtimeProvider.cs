// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using NeoCompose.Runtime;
using NeoCompose.Unity.Editor;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Convex.Editor
{
    /// <summary>
    /// Registers the Convex implementation of the editor realtime seam the
    /// moment the package is present — the zero-setup half of "auto when
    /// signed in" (the window still gates on sign-in + a synced Convex URL).
    /// </summary>
    [InitializeOnLoad]
    internal static class ConvexEditorRealtimeRegistration
    {
        static ConvexEditorRealtimeRegistration()
        {
            NeoComposeEditorRealtime.ProviderFactory =
                context => new ConvexEditorRealtimeProvider(context);
        }
    }

    /// <summary>
    /// Editor-shaped facade over <see cref="ConvexRealtimeProvider"/>: the same
    /// connection/auth/denial machinery, with the editor's two subscriptions —
    /// the version-metadata change signal and the export sync signal.
    /// </summary>
    internal sealed class ConvexEditorRealtimeProvider : INeoComposeEditorRealtimeProvider
    {
        private readonly ConvexRealtimeProvider inner;

        public ConvexEditorRealtimeProvider(NeoComposeEditorRealtimeContext context)
            : this(BuildInner(context))
        {
        }

        internal ConvexEditorRealtimeProvider(ConvexRealtimeProvider inner)
        {
            this.inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        private static ConvexRealtimeProvider BuildInner(NeoComposeEditorRealtimeContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            return new ConvexRealtimeProvider(new ConvexRealtimeOptions(
                context.convexUrl,
                context.apiBaseUrl,
                context.projectId,
                context.sessionTokenProvider));
        }

        public NeoRealtimeConnectionState State => inner.State;

        public event Action<NeoRealtimeConnectionState>? OnConnectionStateChanged
        {
            add => inner.OnConnectionStateChanged += value;
            remove => inner.OnConnectionStateChanged -= value;
        }

        public Awaitable ConnectAsync(CancellationToken cancellationToken = default) =>
            inner.ConnectAsync(cancellationToken);

        public Awaitable DisconnectAsync() => inner.DisconnectAsync();

        public IDisposable SubscribeVersionMetadata(string projectId, Action onChanged)
        {
            if (onChanged == null)
            {
                throw new ArgumentNullException(nameof(onChanged));
            }

            // Signal-then-pull: the payload is ignored; the window re-runs its
            // existing REST metadata load.
            return inner.SubscribeRaw(
                "projectVersions:listMetadata",
                new Dictionary<string, object?> { ["projectId"] = projectId },
                _ => onChanged());
        }

        public IDisposable SubscribeExportSignal(
            string projectId, string versionId, Action<NeoComposeExportSignal?> onChanged)
        {
            if (onChanged == null)
            {
                throw new ArgumentNullException(nameof(onChanged));
            }

            return inner.SubscribeRaw(
                "projectExportData:exportSignal",
                new Dictionary<string, object?>
                {
                    ["projectId"] = projectId,
                    ["versionId"] = versionId,
                },
                json =>
                {
                    NeoComposeExportSignal? signal;
                    try
                    {
                        signal = ParseSignal(json);
                    }
                    catch (Exception exception)
                    {
                        Debug.LogError(
                            $"[NeoCompose] Export sync signal could not be parsed: " +
                            $"{exception.Message}");
                        return;
                    }

                    onChanged(signal);
                });
        }

        public void Dispose() => inner.Dispose();

        private static NeoComposeExportSignal? ParseSignal(string json)
        {
            var token = JToken.Parse(json);
            if (token.Type == JTokenType.Null) return null;
            if (token is not JObject payload)
            {
                throw new InvalidOperationException(
                    $"Export sync signal payload was a {token.Type}, expected an object or null.");
            }

            var versionId = (string?)payload["versionId"];
            if (versionId == null)
            {
                throw new InvalidOperationException(
                    "Export sync signal payload had no \"versionId\".");
            }

            var transactionId = (string?)payload["transactionId"];
            if (transactionId == null)
            {
                throw new InvalidOperationException(
                    "Export sync signal payload had no \"transactionId\".");
            }

            var transactionHash = (string?)payload["transactionHash"];
            if (transactionHash == null)
            {
                throw new InvalidOperationException(
                    "Export sync signal payload had no \"transactionHash\".");
            }

            var transactionAt = payload["transactionAt"];
            if (transactionAt == null)
            {
                throw new InvalidOperationException(
                    "Export sync signal payload had no \"transactionAt\".");
            }

            return new NeoComposeExportSignal
            {
                versionId = versionId,
                transactionId = transactionId,
                transactionHash = transactionHash,
                transactionAt = (long)transactionAt,
            };
        }
    }
}
