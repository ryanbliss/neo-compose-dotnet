// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Null-typed attribute. The stored value is always
    /// null; there's nothing to set, so no Writable variant exists.
    /// </summary>
    public class NeoAttributeNull
        : NeoAttribute<NullAttribute, NullAttributeValue>
    {
        public NeoAttributeNull(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeNull(NeoClient client, NullAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }
    }
}
