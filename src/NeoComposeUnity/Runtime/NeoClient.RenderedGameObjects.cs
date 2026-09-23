// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // The grid renderers drawing this client's values, most recent last.
        private readonly List<NeoTileGridRenderer> gridRenderers = new();

        internal void AttachRenderer(NeoTileGridRenderer renderer)
        {
            if (!gridRenderers.Contains(renderer)) gridRenderers.Add(renderer);
        }

        internal void DetachRenderer(NeoTileGridRenderer renderer) => gridRenderers.Remove(renderer);

        /// <summary>
        /// The GameObject a grid renderer drew <paramref name="valueId"/>
        /// into, preferring the most recently attached renderer. Only a link
        /// is looked up among the layers it flattens into.
        /// </summary>
        internal bool TryGetRenderedGameObject(
            string valueId, bool isLink, [NotNullWhen(true)] out GameObject? gameObject)
        {
            for (int i = gridRenderers.Count - 1; i >= 0; i--)
            {
                var renderer = gridRenderers[i];
                // A renderer destroyed before it was ever enabled never runs OnDestroy.
                if (renderer == null)
                {
                    gridRenderers.RemoveAt(i);
                    continue;
                }
                if (renderer.TryGetGameObject(valueId, isLink, out gameObject)) return true;
            }
            gameObject = null;
            return false;
        }
    }
}
