// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Null-typed attribute. The stored value is always
    /// null; there's nothing to set, so no Saved variant exists.
    /// </summary>
    public class NeoAttributeNull
        : NeoAttribute<NullAttribute, NullAttributeValue>
    {
        public NeoAttributeNull(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeNull(NeoClient client, NullAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }
    }
}
