// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.ComponentModel;
using UnityEngine;

namespace NeoCompose.Runtime
{
    // Unity requires attachable MonoBehaviours to be public and named after
    // their source file. Keep this renderer implementation detail hidden from
    // authored-code autocomplete and the Add Component menu.
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [ExecuteAlways]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class NeoTileLayerRenderTargetLifetime : MonoBehaviour
    {
        private Action? onDestroyed;

        internal void Initialize(Action callback)
        {
            if (onDestroyed != null)
            {
                throw new InvalidOperationException(
                    "Neo tile-layer render-target lifetime is already initialized.");
            }
            onDestroyed = callback ?? throw new ArgumentNullException(nameof(callback));
        }

        private void OnDestroy()
        {
            var callback = onDestroyed;
            onDestroyed = null;
            callback?.Invoke();
        }
    }
}
