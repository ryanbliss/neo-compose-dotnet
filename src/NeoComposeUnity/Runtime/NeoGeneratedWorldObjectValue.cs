// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Base of the generated root world object class (world kind
    /// <c>objectBase</c>, e.g. <c>NeoObjectBase</c>), so every object,
    /// composition child, and layer link inherits the world object APIs the
    /// SDK adds here without a codegen change.
    /// </summary>
    public abstract class NeoGeneratedWorldObjectValue : NeoGeneratedClassValue
    {
        protected NeoGeneratedWorldObjectValue(
            NeoClient client,
            NeoMemberClass node,
            string fallbackClassId,
            bool isReadOnly = true,
            NeoValueOwnership inheritedStorageOwnership = NeoValueOwnership.Asset)
            : base(client, node, fallbackClassId, isReadOnly, inheritedStorageOwnership)
        {
        }

        /// <summary>
        /// The GameObject a grid renderer drew this value into. An object gets
        /// its own GameObject, created before its spawn hooks run and live
        /// until its despawn hooks return. A tile layer link flattens into
        /// its target layer's Tilemap and an object layer link into the
        /// layer's root, so they answer with those. False when no renderer
        /// currently draws the value.
        /// </summary>
        public bool TryGetGameObject([NotNullWhen(true)] out GameObject? gameObject)
        {
            if (valueId is string id)
                return Client.TryGetRenderedGameObject(
                    id,
                    isLink: this is INeoTileLayerLinkValue or INeoObjectLayerLinkValue,
                    out gameObject);
            gameObject = null;
            return false;
        }
    }
}
