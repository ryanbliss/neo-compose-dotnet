// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public sealed class NeoValueWritePayload
    {
        internal bool isValueReference { get; }
        internal string? valueId { get; }
        internal INeoValueReference? valueReference { get; }
        internal object? value { get; }
        internal bool isNull => !isValueReference && value is null;

        private NeoValueWritePayload(
            object? value,
            string? valueId,
            INeoValueReference? valueReference,
            bool isValueReference)
        {
            this.value = value;
            this.valueId = valueId;
            this.valueReference = valueReference;
            this.isValueReference = isValueReference;
        }

        internal static NeoValueWritePayload FromValue(object? value)
        {
            return new NeoValueWritePayload(value, null, null, false);
        }

        internal static NeoValueWritePayload FromValueReference(
            string valueId,
            INeoValueReference? valueReference = null)
        {
            return new NeoValueWritePayload(null, valueId, valueReference, true);
        }

        internal void RetargetMovedReference(
            NeoClient client,
            Member member,
            string valueId,
            NeoValueOwnership ownership)
        {
            if (!isValueReference) return;
            if (valueReference is not NeoGeneratedClassValue generated) return;
            if (generated.IsReadOnly) return;
            if (member is not ClassMember classMember) return;
            generated.RetargetWritableReference(classMember, valueId, ownership);
        }
    }
}
