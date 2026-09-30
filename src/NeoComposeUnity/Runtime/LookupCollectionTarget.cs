// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// A lookup's collection member and declared collection value, resolved
    /// for one client's schema, and the value node of the collection id it
    /// last resolved to.
    /// </summary>
    internal sealed class LookupCollectionTarget
    {
        internal readonly object schemaResolution;
        internal readonly Member? collectionMember;
        internal readonly string? collectionValueId;
        internal string? collectionId;
        internal NeoValueNode? node;

        internal LookupCollectionTarget(object schemaResolution, Member? collectionMember, string? collectionValueId)
        {
            this.schemaResolution = schemaResolution;
            this.collectionMember = collectionMember;
            this.collectionValueId = collectionValueId;
        }
    }
}
