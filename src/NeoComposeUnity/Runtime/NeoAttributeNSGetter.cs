// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for an NSGetter-typed attribute. The stored value is
    /// always null — the runtime computes the value at evaluation
    /// time by walking the IR exposed via
    /// <see cref="NSGetterAttribute.getter"/>.
    ///
    /// <para>No Saved variant — NSGetter values are derived, not set.
    /// <see cref="Compute"/> is the future hook for the IR evaluator;
    /// stub for now (throws).</para>
    /// </summary>
    public class NeoAttributeNSGetter
        : NeoAttribute<NSGetterAttribute, NullAttributeValue>
    {
        public NeoAttributeNSGetter(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeNSGetter(NeoClient client, NSGetterAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }

        /// <summary>
        /// Walks the compiled IR (<see cref="NSGetterAttribute.getter"/>)
        /// to compute the current value. <b>Not implemented yet</b> —
        /// the IR evaluator is future scope. The class shape exists
        /// so consumer code can write against it today and the
        /// evaluator slot in later without an API change.
        /// </summary>
        public object? Compute()
        {
            throw new System.NotImplementedException(
                "NSGetter evaluation isn't implemented yet — see specs/neo-attributes.md.");
        }
    }
}
