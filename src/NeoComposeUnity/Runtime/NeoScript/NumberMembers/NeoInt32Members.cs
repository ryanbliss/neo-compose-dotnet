// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.NeoScript.Globalization;

namespace NeoCompose.Runtime.NeoScript.NumberMembers
{
    /// <summary>
    /// <c>int</c>'s number members (P127 §7.3, §8.1 to §8.5), the twin of the
    /// compose runtime's <c>number-members/int32.ts</c>. Transcribes
    /// <c>Int32.cs</c>, <c>Math.cs</c> and <c>INumber.cs</c> at dotnet/runtime
    /// 8f4c012be1d11eef2a7c1991cdb6302479f23b74 (release/10.0). <c>int.Abs</c>
    /// is <c>NeoInt32.Abs</c>; <c>MaxNumber</c>, <c>MinNumber</c> and the
    /// <c>*MagnitudeNumber</c> members are their plain twins.
    /// </summary>
    internal static class NeoInt32Members
    {
        internal const int Zero = 0;
        internal const int One = 1;
        internal const int NegativeOne = -1;
        internal const int AdditiveIdentity = 0;
        internal const int MultiplicativeIdentity = 1;
        internal const int Radix = 2;
        internal const int MinValue = int.MinValue;
        internal const int MaxValue = int.MaxValue;

        /// <summary><c>int.IsNaN(value)</c> and the other classification members.</summary>
        internal static bool Is(NeoNumberPredicate predicate, int value) => predicate switch
        {
            NeoNumberPredicate.IsCanonical => true,
            NeoNumberPredicate.IsFinite => true,
            NeoNumberPredicate.IsInteger => true,
            NeoNumberPredicate.IsRealNumber => true,
            NeoNumberPredicate.IsEvenInteger => (value & 1) == 0,
            NeoNumberPredicate.IsOddInteger => (value & 1) != 0,
            NeoNumberPredicate.IsNegative => value < 0,
            NeoNumberPredicate.IsPositive => value >= 0,
            NeoNumberPredicate.IsNormal => value != 0,
            NeoNumberPredicate.IsZero => value == 0,
            _ => false,
        };

        internal static int Sign(int value) => value < 0 ? -1 : value > 0 ? 1 : 0;

        /// <summary><c>int.CopySign</c>: throws when <paramref name="value"/> is <c>int.MinValue</c> and <paramref name="sign"/> isn't negative.</summary>
        internal static int CopySign(int value, int sign)
        {
            int magnitude = value < 0 ? unchecked(-value) : value;
            if (sign >= 0)
            {
                if (magnitude < 0)
                    throw NeoNumberMemberErrors.NegateMinValue();
                return magnitude;
            }
            return unchecked(-magnitude);
        }

        internal static int Max(int x, int y) => x >= y ? x : y;

        internal static int Min(int x, int y) => x <= y ? x : y;

        /// <summary><c>INumber.MaxNative</c>'s default body.</summary>
        internal static int MaxNative(int x, int y) => x > y ? x : y;

        /// <summary><c>INumber.MinNative</c>'s default body.</summary>
        internal static int MinNative(int x, int y) => x < y ? x : y;

        /// <summary><c>int.Clamp</c>, which is <c>Math.Clamp</c>.</summary>
        internal static int Clamp(int value, int min, int max)
        {
            if (min > max)
                throw MinMaxError(min, max);
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        /// <summary><c>INumber.ClampNative</c>'s default body.</summary>
        internal static int ClampNative(int value, int min, int max)
        {
            if (min > max)
                throw MinMaxError(min, max);
            return MinNative(MaxNative(value, min), max);
        }

        private static NeoRuntimeError MinMaxError(int min, int max) => NeoNumberMemberErrors.MinMax(
            NeoNumberFormatting.FormatInt32(min, null, null),
            NeoNumberFormatting.FormatInt32(max, null, null));

        /// <summary>
        /// <c>int.MaxMagnitude</c>, also <c>MaxMagnitudeNumber</c>. <c>int.MinValue</c>
        /// has the largest magnitude, since its negation wraps to itself.
        /// </summary>
        internal static int MaxMagnitude(int x, int y)
        {
            int absX = x;
            if (absX < 0)
            {
                absX = unchecked(-absX);
                if (absX < 0)
                    return x;
            }
            int absY = y;
            if (absY < 0)
            {
                absY = unchecked(-absY);
                if (absY < 0)
                    return y;
            }
            if (absX > absY)
                return x;
            if (absX == absY)
                return x < 0 ? y : x;
            return y;
        }

        /// <summary><c>int.MinMagnitude</c>, also <c>MinMagnitudeNumber</c>.</summary>
        internal static int MinMagnitude(int x, int y)
        {
            int absX = x;
            if (absX < 0)
            {
                absX = unchecked(-absX);
                if (absX < 0)
                    return y;
            }
            int absY = y;
            if (absY < 0)
            {
                absY = unchecked(-absY);
                if (absY < 0)
                    return x;
            }
            if (absX < absY)
                return x;
            if (absX == absY)
                return x < 0 ? x : y;
            return y;
        }

        /// <summary><c>int.MultiplyAddEstimate</c>: <c>(left * right) + addend</c>, unchecked.</summary>
        internal static int MultiplyAddEstimate(int left, int right, int addend) => unchecked((left * right) + addend);

        internal static bool Equals(int value, int other) => value == other;

        /// <summary><c>int.CompareTo(int)</c>: -1, 0 or 1.</summary>
        internal static int CompareTo(int value, int other)
        {
            if (value < other)
                return -1;
            if (value > other)
                return 1;
            return 0;
        }

        /// <summary><c>int.Equals(object)</c>: true only for a boxed int of the same value.</summary>
        internal static bool EqualsObject(int value, object? other) => other is int box && value == box;

        /// <summary><c>int.CompareTo(object)</c>: 1 for null, and <c>NeoArgumentError</c> for another type.</summary>
        internal static int CompareToObject(int value, object? other)
        {
            if (other is null)
                return 1;
            if (other is not int box)
                throw NeoNumberMemberErrors.MustBeType(NeoNumericKind.Int);
            return CompareTo(value, box);
        }
    }
}
