// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public sealed class NeoValueWritePayload
    {
        internal bool isValueReference
        {
            get;
        }
        internal string? valueId
        {
            get;
        }
        internal INeoValueReference? valueReference
        {
            get;
        }
        internal object? value
        {
            get;
        }
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

        // Value payloads are immutable, so the common scalars share one each
        // and a generated setter's write allocates no payload or box.
        private const int MinSharedInt = -128;
        private const int MaxSharedInt = 1023;
        private static readonly NeoValueWritePayload?[] sharedInts =
            new NeoValueWritePayload?[MaxSharedInt - MinSharedInt + 1];
        private static readonly NeoValueWritePayload sharedNull = FromValue(null);
        private static readonly NeoValueWritePayload sharedTrue = FromValue(true);
        private static readonly NeoValueWritePayload sharedFalse = FromValue(false);

        internal static NeoValueWritePayload FromInt(int value)
        {
            if (value < MinSharedInt || value > MaxSharedInt)
                return FromValue(value);
            int index = value - MinSharedInt;
            return sharedInts[index] ??= FromValue(value);
        }

        internal static NeoValueWritePayload FromInt(int? value) =>
            value is int number ? FromInt(number) : sharedNull;

        internal static NeoValueWritePayload FromBool(bool value) =>
            value ? sharedTrue : sharedFalse;

        internal static NeoValueWritePayload FromBool(bool? value) =>
            value is bool flag ? FromBool(flag) : sharedNull;

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
            if (isValueReference)
                RetargetMovedView(client, valueReference as NeoGeneratedClassValue, member, valueId, ownership);
        }

        /// <summary>
        /// Points <paramref name="generated"/>, a view of a value a write
        /// moved onto <paramref name="member"/>'s row, at that row.
        /// </summary>
        internal static void RetargetMovedView(
            NeoClient client,
            NeoGeneratedClassValue? generated,
            Member member,
            string valueId,
            NeoValueOwnership ownership)
        {
            if (generated is null || generated.IsReadOnly)
                return;
            if (member is not ClassMember classMember)
                return;
            if (client.DeferVariantAliasRetarget(generated, classMember, valueId, ownership))
                return;
            generated.RetargetWritableReference(classMember, valueId, ownership);
        }
    }
}
