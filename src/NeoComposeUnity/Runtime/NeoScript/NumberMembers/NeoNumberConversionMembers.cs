// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.NeoScript.Numerics;

namespace NeoCompose.Runtime.NeoScript.NumberMembers
{
    /// <summary>
    /// <c>INumberBase.TryConvertFrom*</c>, <c>TryConvertTo*</c> and <c>Create*</c>
    /// between the primitives, the twin of the compose runtime's
    /// <c>number-members/convert.ts</c>, as <c>Int32.cs</c>, <c>Single.cs</c> and
    /// <c>Decimal.cs</c> implement them at dotnet/runtime
    /// 8f4c012be1d11eef2a7c1991cdb6302479f23b74 (release/10.0). .NET splits each
    /// pair of primitives between the two hooks: <c>int</c> and <c>float</c>
    /// convert from each other, and <c>decimal</c> converts to and from both
    /// through its own <c>TryConvertTo</c>. Every other pair, including a type
    /// with itself, refuses. Values are a boxed <c>int</c>, <c>float</c>, or
    /// decimal text.
    /// </summary>
    internal static class NeoNumberConversionMembers
    {
        /// <summary>
        /// <c>self.TryConvertFromChecked&lt;other&gt;(value, out result)</c> and its
        /// siblings. An authored <paramref name="other"/> passes null: no
        /// primitive converts from an authored type.
        /// </summary>
        internal static bool TryConvertFrom(NeoCreateMode mode, NeoNumericKind self, NeoNumericKind? other, object value, out object? result)
        {
            bool converts = (self == NeoNumericKind.Int && other == NeoNumericKind.Float)
                || (self == NeoNumericKind.Float && other == NeoNumericKind.Int);
            result = converts ? Convert(mode, self, other!.Value, value) : null;
            return converts;
        }

        /// <summary>
        /// <c>self.TryConvertToChecked&lt;other&gt;(value, out result)</c> and its
        /// siblings. An authored <paramref name="other"/> passes null: no
        /// primitive converts to an authored type.
        /// </summary>
        internal static bool TryConvertTo(NeoCreateMode mode, NeoNumericKind self, NeoNumericKind? other, object value, out object? result)
        {
            bool converts = other is { } target
                && target != self
                && (self == NeoNumericKind.Decimal || target == NeoNumericKind.Decimal);
            result = converts ? Convert(mode, other!.Value, self, value) : null;
            return converts;
        }

        /// <summary>
        /// <c>self.CreateChecked&lt;other&gt;(value)</c> and its siblings between
        /// primitives: the value itself for the same type, else
        /// <paramref name="self"/>'s <c>TryConvertFrom</c>, else
        /// <paramref name="other"/>'s <c>TryConvertTo</c>.
        /// </summary>
        internal static object CreateFrom(NeoCreateMode mode, NeoNumericKind self, NeoNumericKind other, object value)
        {
            if (self == other)
                return value;
            if (TryConvertFrom(mode, self, other, value, out object? from))
                return from!;
            if (TryConvertTo(mode, other, self, value, out object? to))
                return to!;
            throw NeoNumberMemberErrors.NotSupported();
        }

        /// <summary>
        /// <c>self.CreateChecked&lt;TOther&gt;(value)</c> for an authored
        /// <c>TOther</c>: <c>self</c> refuses, so the result is <c>TOther</c>'s own
        /// <c>TryConvertTo*&lt;self&gt;</c> hook, which the caller has run. Its
        /// false throws <c>NeoNotSupportedError</c>.
        /// </summary>
        internal static object CreateFromAuthored(bool converted, object? result)
        {
            if (!converted)
                throw NeoNumberMemberErrors.NotSupported();
            return result!;
        }

        /// <summary>F1's conversion from <paramref name="source"/> to a different <paramref name="target"/>.</summary>
        private static object Convert(NeoCreateMode mode, NeoNumericKind target, NeoNumericKind source, object value)
        {
            switch (source)
            {
                case NeoNumericKind.Int:
                    int i = (int)value;
                    if (target == NeoNumericKind.Float)
                        return NeoNumericConversions.SingleFromInt32(i);
                    return NeoNumericConversions.DecimalFromInt32(i);
                case NeoNumericKind.Float:
                    float f = (float)value;
                    if (target == NeoNumericKind.Int)
                        return NeoNumericConversions.Int32FromSingle(f, mode);
                    return NeoNumericConversions.DecimalFromSingle(f, mode);
                default:
                    string m = (string)value;
                    if (target == NeoNumericKind.Int)
                        return NeoNumericConversions.Int32FromDecimal(m, mode);
                    return NeoNumericConversions.DecimalToSingle(m);
            }
        }
    }
}
