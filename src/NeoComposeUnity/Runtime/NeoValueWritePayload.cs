// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    public sealed class NeoValueWritePayload
    {
        internal bool isValueReference { get; }
        internal string? valueId { get; }
        internal object? value { get; }
        internal bool isNull => !isValueReference && value is null;

        private NeoValueWritePayload(
            object? value,
            string? valueId,
            bool isValueReference)
        {
            this.value = value;
            this.valueId = valueId;
            this.isValueReference = isValueReference;
        }

        internal static NeoValueWritePayload FromValue(object? value)
        {
            return new NeoValueWritePayload(value, null, false);
        }

        internal static NeoValueWritePayload FromValueReference(string valueId)
        {
            return new NeoValueWritePayload(null, valueId, true);
        }
    }
}
