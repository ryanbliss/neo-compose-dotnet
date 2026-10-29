// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// P127 §1.2. A value converted to <c>object</c>, <c>ValueType</c>,
    /// <c>Enum</c> or an interface: the closed runtime type it was converted
    /// at and its payload, immutable. The compose twin is the TS runtime's
    /// <c>NeoBox</c>.
    /// </summary>
    internal sealed class NeoBox
    {
        private static readonly TypeBinding ObjectType = TypeBinding.Closed(new TypeHead(MemberKind.Object, true));
        private static readonly object Zero = 0d;

        // Reference-like payloads keep their identity: converting one list
        // twice gives one box (§1.2).
        private static readonly ConditionalWeakTable<object, NeoBox> referenceBoxes = new();

        // A row an evaluator write stored a box in reads back as that box.
        private static readonly ConditionalWeakTable<MemberValue, NeoBox> writtenBoxes = new();

        private NeoBox(TypeBinding valueType, object? payload)
        {
            ValueType = valueType;
            Payload = payload;
            IdentityKey = valueType.Head!.Type == MemberKind.String
                ? (MemberKind.String, (string)payload!)
                : AssetKey(valueType.Head.Type, payload) ?? this;
        }

        internal object IdentityKey
        {
            get;
        }

        /// <summary>The held type: closed, with nullable references erased and never nullable at the top.</summary>
        internal TypeBinding ValueType
        {
            get;
        }

        /// <summary>The evaluator value; null only for <c>new object()</c>.</summary>
        internal object? Payload
        {
            get;
        }

        /// <summary><c>new object()</c>: a fresh identity with no payload.</summary>
        internal static NeoBox NewObject() => new(ObjectType, null);

        /// <summary>
        /// The box of <paramref name="payload"/> held as <paramref name="valueType"/>:
        /// fresh for value kinds and strings, the payload's one box otherwise.
        /// </summary>
        internal static NeoBox Of(TypeBinding valueType, object payload)
        {
            switch (valueType.Head!.Type)
            {
                case MemberKind.Int:
                    // An int never carries -0.
                    return new NeoBox(valueType, payload is double number && number == 0 ? Zero : payload);
                case MemberKind.Float:
                case MemberKind.Decimal:
                case MemberKind.Bool:
                case MemberKind.Enum:
                case MemberKind.Tuple:
                case MemberKind.String:
                    return new NeoBox(valueType, payload);
                default:
                    return referenceBoxes.GetValue(payload, held => new NeoBox(valueType, held));
            }
        }

        /// <summary>Records <paramref name="box"/> as what <paramref name="row"/> reads as.</summary>
        internal static void Remember(MemberValue row, NeoBox box)
        {
            writtenBoxes.Remove(row);
            writtenBoxes.Add(row, box);
        }

        /// <summary>The box an evaluator write stored in <paramref name="row"/>, if any.</summary>
        internal static NeoBox? WrittenTo(MemberValue row) => writtenBoxes.TryGetValue(row, out NeoBox? box) ? box : null;

        /// <summary>
        /// <c>object.ReferenceEquals</c> (§1.2): the same row or box, both
        /// null, or two string boxes of equal strings.
        /// </summary>
        internal static bool SameIdentity(object? left, object? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            return left is NeoBox leftBox && right is NeoBox rightBox
                && leftBox.IdentityKey.Equals(rightBox.IdentityKey);
        }

        private static object? AssetKey(MemberKind kind, object? payload)
        {
            if (kind is not (MemberKind.Sprite or MemberKind.Audio)
                || payload is not IDictionary<string, object?> asset
                || !asset.TryGetValue("fileId", out object? rawId) || rawId is not string id)
                return null;
            asset.TryGetValue("sliceIndex", out object? slice);
            int? index = kind == MemberKind.Audio || slice is null ? null : Convert.ToInt32(slice, CultureInfo.InvariantCulture);
            return (kind, id, index);
        }

    }

    /// <summary>
    /// P127 §2. A NeoScript <c>Type</c> value, one per runtime type, so
    /// <c>typeof(int) == typeof(int)</c> compares identities.
    /// </summary>
    internal sealed class NeoTypeValue
    {
        private static readonly ConcurrentDictionary<TypeBinding, NeoTypeValue> interned = new();

        private NeoTypeValue(TypeBinding binding)
        {
            Binding = binding;
        }

        /// <summary>The runtime type: tuple labels and nullable references erased, a nullable value type's <c>?</c> kept.</summary>
        internal TypeBinding Binding
        {
            get;
        }

        /// <summary>The interned value of <paramref name="binding"/>, which must already be a runtime type.</summary>
        internal static NeoTypeValue Of(TypeBinding binding) => interned.GetOrAdd(binding, key => new NeoTypeValue(key));
    }
}
