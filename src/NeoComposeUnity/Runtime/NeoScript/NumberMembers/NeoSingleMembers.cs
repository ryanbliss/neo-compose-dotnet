// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.NeoScript.Globalization;

namespace NeoCompose.Runtime.NeoScript.NumberMembers
{
    /// <summary>
    /// <c>float</c>'s number members (P127 §7.3, §8.1 to §8.5), the twin of the
    /// compose runtime's <c>number-members/float32.ts</c>. Transcribes
    /// <c>Single.cs</c>, <c>MathF.cs</c>, <c>Math.cs</c> and <c>INumber.cs</c> at
    /// dotnet/runtime 8f4c012be1d11eef2a7c1991cdb6302479f23b74 (release/10.0).
    /// NeoScript has one NaN, whose bits are 0xFFC00000 (§8.1): every member
    /// that reads a float's bits reads a NaN's as those.
    /// </summary>
    internal static class NeoSingleMembers
    {
        /// <summary>The bits of NeoScript's one NaN.</summary>
        internal const uint NaNBits = 0xFFC00000;

        internal const float Zero = 0f;
        internal const float One = 1f;
        internal const float NegativeOne = -1f;
        internal const float AdditiveIdentity = 0f;
        internal const float MultiplicativeIdentity = 1f;
        internal const int Radix = 2;
        internal const float MinValue = float.MinValue;
        internal const float MaxValue = float.MaxValue;
        internal const float Epsilon = float.Epsilon;
        internal const float NaN = float.NaN;
        internal const float PositiveInfinity = float.PositiveInfinity;
        internal const float NegativeInfinity = float.NegativeInfinity;

        private const float SmallestNormal = 1.17549435E-38f;

        /// <summary>The value just past <c>float.MaxValue</c>, where rounding reaches infinity.</summary>
        private const double TwoPow128 = 340282366920938463463374607431768211456.0;

        /// <summary>A float's IEEE 754 bits; NaN is 0xFFC00000.</summary>
        internal static uint ToBits(float value) =>
            float.IsNaN(value) ? NaNBits : unchecked((uint)BitConverter.SingleToInt32Bits(value));

        /// <summary>The float with <paramref name="bits"/>; every NaN pattern reads as the one NaN.</summary>
        internal static float FromBits(uint bits)
        {
            float value = BitConverter.Int32BitsToSingle(unchecked((int)bits));
            return float.IsNaN(value) ? float.NaN : value;
        }

        /// <summary><c>float.IsNegative</c>: the sign bit, so -0 and NaN are negative.</summary>
        private static bool IsNegative(float value) => (ToBits(value) & 0x80000000) != 0;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsInteger(float value) => IsFinite(value) && value == MathF.Truncate(value);

        /// <summary><c>float.IsNaN(value)</c> and the other classification members.</summary>
        internal static bool Is(NeoNumberPredicate predicate, float value) => predicate switch
        {
            NeoNumberPredicate.IsCanonical => true,
            NeoNumberPredicate.IsComplexNumber => false,
            NeoNumberPredicate.IsImaginaryNumber => false,
            NeoNumberPredicate.IsEvenInteger => IsInteger(value) && value % 2f == 0f,
            NeoNumberPredicate.IsFinite => IsFinite(value),
            NeoNumberPredicate.IsInfinity => float.IsInfinity(value),
            NeoNumberPredicate.IsInteger => IsInteger(value),
            NeoNumberPredicate.IsNaN => float.IsNaN(value),
            NeoNumberPredicate.IsNegative => IsNegative(value),
            NeoNumberPredicate.IsNegativeInfinity => float.IsNegativeInfinity(value),
            NeoNumberPredicate.IsNormal => IsFinite(value) && MathF.Abs(value) >= SmallestNormal,
            NeoNumberPredicate.IsOddInteger => IsInteger(value) && MathF.Abs(value % 2f) == 1f,
            NeoNumberPredicate.IsPositive => !IsNegative(value),
            NeoNumberPredicate.IsPositiveInfinity => float.IsPositiveInfinity(value),
            NeoNumberPredicate.IsRealNumber => !float.IsNaN(value),
            NeoNumberPredicate.IsSubnormal => value != 0f && MathF.Abs(value) < SmallestNormal,
            NeoNumberPredicate.IsZero => value == 0f,
            _ => throw new ArgumentOutOfRangeException(nameof(predicate)),
        };

        /// <summary><c>float.Abs</c>. A NaN stays the one NaN.</summary>
        internal static float Abs(float value) => float.IsNaN(value) ? value : MathF.Abs(value);

        /// <summary><c>float.Sign</c>: throws <c>NeoArithmeticError</c> for NaN.</summary>
        internal static int Sign(float value)
        {
            if (float.IsNaN(value))
                throw NeoNumberMemberErrors.NotANumber();
            return value < 0f ? -1 : value > 0f ? 1 : 0;
        }

        /// <summary>
        /// <c>float.CopySign</c>. A NaN <paramref name="value"/> stays the one NaN;
        /// a NaN <paramref name="sign"/> makes the result negative.
        /// </summary>
        internal static float CopySign(float value, float sign)
        {
            if (float.IsNaN(value))
                return value;
            float magnitude = MathF.Abs(value);
            return IsNegative(sign) ? -magnitude : magnitude;
        }

        /// <summary><c>float.Max</c>, IEEE 754 <c>maximum</c>: propagates NaN, and +0 beats -0.</summary>
        internal static float Max(float x, float y)
        {
            if (x != y)
            {
                if (!float.IsNaN(x))
                    return y < x ? x : y;
                return x;
            }
            return IsNegative(y) ? x : y;
        }

        /// <summary><c>float.Min</c>, IEEE 754 <c>minimum</c>.</summary>
        internal static float Min(float x, float y)
        {
            if (x != y)
            {
                if (!float.IsNaN(x))
                    return x < y ? x : y;
                return x;
            }
            return IsNegative(x) ? x : y;
        }

        /// <summary><c>float.MaxNumber</c>, IEEE 754 <c>maximumNumber</c>: a NaN loses to a number.</summary>
        internal static float MaxNumber(float x, float y)
        {
            if (x != y)
            {
                if (!float.IsNaN(y))
                    return y < x ? x : y;
                return x;
            }
            return IsNegative(y) ? x : y;
        }

        /// <summary><c>float.MinNumber</c>, IEEE 754 <c>minimumNumber</c>.</summary>
        internal static float MinNumber(float x, float y)
        {
            if (x != y)
            {
                if (!float.IsNaN(y))
                    return x < y ? x : y;
                return x;
            }
            return IsNegative(x) ? x : y;
        }

        internal static float MaxNative(float x, float y) => x > y ? x : y;

        internal static float MinNative(float x, float y) => x < y ? x : y;

        /// <summary><c>float.MaxMagnitude</c>, IEEE 754 <c>maximumMagnitude</c>.</summary>
        internal static float MaxMagnitude(float x, float y)
        {
            float ax = MathF.Abs(x);
            float ay = MathF.Abs(y);
            if (ax > ay || float.IsNaN(ax))
                return x;
            if (ax == ay)
                return IsNegative(x) ? y : x;
            return y;
        }

        /// <summary><c>float.MaxMagnitudeNumber</c>, IEEE 754 <c>maximumMagnitudeNumber</c>.</summary>
        internal static float MaxMagnitudeNumber(float x, float y)
        {
            float ax = MathF.Abs(x);
            float ay = MathF.Abs(y);
            if (ax > ay || float.IsNaN(ay))
                return x;
            if (ax == ay)
                return IsNegative(x) ? y : x;
            return y;
        }

        /// <summary><c>float.MinMagnitude</c>, IEEE 754 <c>minimumMagnitude</c>.</summary>
        internal static float MinMagnitude(float x, float y)
        {
            float ax = MathF.Abs(x);
            float ay = MathF.Abs(y);
            if (ax < ay || float.IsNaN(ax))
                return x;
            if (ax == ay)
                return IsNegative(x) ? x : y;
            return y;
        }

        /// <summary><c>float.MinMagnitudeNumber</c>, IEEE 754 <c>minimumMagnitudeNumber</c>.</summary>
        internal static float MinMagnitudeNumber(float x, float y)
        {
            float ax = MathF.Abs(x);
            float ay = MathF.Abs(y);
            if (ax < ay || float.IsNaN(ay))
                return x;
            if (ax == ay)
                return IsNegative(x) ? x : y;
            return y;
        }

        /// <summary><c>float.Clamp</c>: <c>Min(Max(value, min), max)</c>.</summary>
        internal static float Clamp(float value, float min, float max)
        {
            if (min > max)
                throw MinMaxError(min, max);
            return Min(Max(value, min), max);
        }

        /// <summary><c>float.ClampNative</c>: <c>MinNative(MaxNative(value, min), max)</c>.</summary>
        internal static float ClampNative(float value, float min, float max)
        {
            if (min > max)
                throw MinMaxError(min, max);
            return MinNative(MaxNative(value, min), max);
        }

        private static NeoRuntimeError MinMaxError(float min, float max) => NeoNumberMemberErrors.MinMax(
            NeoNumberFormatting.FormatSingle(min, null, null),
            NeoNumberFormatting.FormatSingle(max, null, null));

        /// <summary>
        /// <c>float.MultiplyAddEstimate</c>, NeoScript's exactly rounded fused
        /// multiply-add, computed without <c>MathF.FusedMultiplyAdd</c>, which Mono
        /// doesn't promise to round once. The double product of two floats is
        /// exact, and TwoSum recovers the double sum's rounding error. Rounding
        /// that sum to float again is wrong only when the sum lands exactly
        /// between two floats; then the error picks the side. One operation per
        /// statement keeps a compiler from contracting any of them.
        /// </summary>
        internal static float MultiplyAddEstimate(float left, float right, float addend)
        {
            double product = (double)left * right;
            double sum = product + addend;
            float rounded = (float)sum;
            if (double.IsNaN(sum) || double.IsInfinity(sum))
                return rounded;
            if (rounded == sum)
                return rounded;
            double addendPart = sum - product;
            double productPart = sum - addendPart;
            double productError = product - productPart;
            double addendError = addend - addendPart;
            double error = productError + addendError;
            if (error == 0)
                return rounded;

            int roundedBits = BitConverter.SingleToInt32Bits(rounded);
            bool awayFromZero = Math.Abs(sum) > Math.Abs((double)rounded);
            float neighbor = BitConverter.Int32BitsToSingle(unchecked(awayFromZero ? roundedBits + 1 : roundedBits - 1));
            double roundedFinite = float.IsInfinity(rounded) ? Math.Sign(rounded) * TwoPow128 : rounded;
            double pairSum = roundedFinite + neighbor;
            double midpoint = pairSum / 2;
            if (sum != midpoint)
                return rounded;
            bool roundedIsAbove = rounded > neighbor;
            if (error > 0)
                return roundedIsAbove ? rounded : neighbor;
            return roundedIsAbove ? neighbor : rounded;
        }

        /// <summary><c>float.Equals(float)</c>: NaN equals NaN, and -0 equals 0.</summary>
        internal static bool Equals(float value, float other) => value == other || (float.IsNaN(value) && float.IsNaN(other));

        /// <summary><c>float.CompareTo(float)</c>: -1, 0 or 1, with NaN below every number.</summary>
        internal static int CompareTo(float value, float other)
        {
            if (value < other)
                return -1;
            if (value > other)
                return 1;
            if (value == other)
                return 0;
            if (float.IsNaN(value))
                return float.IsNaN(other) ? 0 : -1;
            return 1;
        }

        /// <summary><c>float.Equals(object)</c>: true only for a boxed float that <c>Equals</c> the value.</summary>
        internal static bool EqualsObject(float value, object? other) => other is float box && Equals(value, box);

        /// <summary><c>float.CompareTo(object)</c>: 1 for null, and <c>NeoArgumentError</c> for another type.</summary>
        internal static int CompareToObject(float value, object? other)
        {
            if (other is null)
                return 1;
            if (other is not float box)
                throw NeoNumberMemberErrors.MustBeType(NeoNumericKind.Float);
            return CompareTo(value, box);
        }

        /// <summary><c>float &amp; float</c> on the bits.</summary>
        internal static float And(float left, float right) => FromBits(ToBits(left) & ToBits(right));

        /// <summary><c>float | float</c> on the bits.</summary>
        internal static float Or(float left, float right) => FromBits(ToBits(left) | ToBits(right));

        /// <summary><c>float ^ float</c> on the bits.</summary>
        internal static float Xor(float left, float right) => FromBits(ToBits(left) ^ ToBits(right));

        /// <summary><c>~float</c> on the bits.</summary>
        internal static float Not(float value) => FromBits(~ToBits(value));
    }
}
