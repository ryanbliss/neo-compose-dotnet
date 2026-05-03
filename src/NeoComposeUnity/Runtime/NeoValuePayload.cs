// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Payload envelope for generated code that needs to create a new
    /// save-side value row while also carrying metadata that does not live
    /// inside the raw value payload, especially Custom value <c>typeId</c>.
    /// </summary>
    public sealed class NeoValuePayload
    {
        public object? value { get; }
        public string? typeId { get; }
        public IReadOnlyList<AttributeValue> valueRows { get; }

        public NeoValuePayload(
            object? value,
            string? typeId = null,
            IReadOnlyList<AttributeValue>? valueRows = null)
        {
            this.value = value;
            this.typeId = typeId;
            this.valueRows = valueRows ?? new List<AttributeValue>();
        }
    }
}
