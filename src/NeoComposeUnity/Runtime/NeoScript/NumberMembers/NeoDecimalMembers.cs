// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Numerics;
using NeoCompose.Runtime.NeoScript.Globalization;

namespace NeoCompose.Runtime.NeoScript.NumberMembers
{
    /// <summary>
    /// <c>decimal</c>'s number members (P127 §7.3, §8.1 to §8.5), the twin of
    /// the compose runtime's <c>number-members/decimal.ts</c>. Transcribes
    /// <c>Decimal.cs</c>, <c>Math.cs</c> and <c>INumber.cs</c> at dotnet/runtime
    /// 8f4c012be1d11eef2a7c1991cdb6302479f23b74 (release/10.0). Values are
    /// canonical decimal text, which keeps the scale and the sign of zero. A
    /// tie returns whichever operand .NET's body returns, so the result keeps
    /// that operand's scale and sign. <c>MaxNumber</c>, <c>MinNumber</c> and
    /// the <c>*MagnitudeNumber</c> members are their plain twins, <c>Compare</c>
    /// is <c>CompareTo</c>, and the static <c>Equals</c> is <c>Equals</c>.
    /// </summary>
    internal static class NeoDecimalMembers
    {
        internal const string Zero = "0";
        internal const string One = "1";
        internal const string NegativeOne = "-1";
        internal const string MinusOne = "-1";
        internal const string AdditiveIdentity = "0";
        internal const string MultiplicativeIdentity = "1";
        internal const int Radix = 10;
        internal const string MinValue = "-79228162514264337593543950335";
        internal const string MaxValue = "79228162514264337593543950335";

        private static NeoDecimalMath.DecimalParts Parse(string value) => NeoDecimalMath.ParseArg(value, "value");

        /// <summary>The parts with the sign set to <paramref name="negative"/>, zero included.</summary>
        private static string WithSign(NeoDecimalMath.DecimalParts parts, bool negative)
        {
            BigInteger magnitude = BigInteger.Abs(parts.Coefficient);
            return NeoDecimalMath.FormatParts(new NeoDecimalMath.DecimalParts(negative ? -magnitude : magnitude, parts.Scale, negative));
        }

        /// <summary>The magnitude's integral part, and whether the fraction is zero.</summary>
        private static BigInteger Truncated(NeoDecimalMath.DecimalParts parts, out bool exact)
        {
            BigInteger magnitude = BigInteger.Abs(parts.Coefficient);
            if (parts.Scale == 0)
            {
                exact = true;
                return magnitude;
            }
            BigInteger quotient = BigInteger.DivRem(magnitude, BigInteger.Pow(10, parts.Scale), out BigInteger remainder);
            exact = remainder.IsZero;
            return quotient;
        }

        /// <summary><c>decimal.IsNegative(value)</c> and the other classification members.</summary>
        internal static bool Is(NeoNumberPredicate predicate, string value)
        {
            NeoDecimalMath.DecimalParts parts = Parse(value);
            switch (predicate)
            {
                case NeoNumberPredicate.IsCanonical:
                    return parts.Scale == 0 || !(parts.Coefficient % 10).IsZero;
                case NeoNumberPredicate.IsFinite:
                case NeoNumberPredicate.IsRealNumber:
                    return true;
                case NeoNumberPredicate.IsEvenInteger:
                    {
                        BigInteger integral = Truncated(parts, out bool exact);
                        return exact && integral.IsEven;
                    }
                case NeoNumberPredicate.IsOddInteger:
                    {
                        BigInteger integral = Truncated(parts, out bool exact);
                        return exact && !integral.IsEven;
                    }
                case NeoNumberPredicate.IsInteger:
                    Truncated(parts, out bool isInteger);
                    return isInteger;
                case NeoNumberPredicate.IsNegative:
                    return parts.Negative;
                case NeoNumberPredicate.IsPositive:
                    return !parts.Negative;
                case NeoNumberPredicate.IsNormal:
                    return !parts.Coefficient.IsZero;
                case NeoNumberPredicate.IsZero:
                    return parts.Coefficient.IsZero;
                default:
                    return false;
            }
        }

        /// <summary><c>decimal.Abs</c>: clears the sign, keeping the scale (<c>-0</c> is <c>0</c>).</summary>
        internal static string Abs(string value) => WithSign(Parse(value), false);

        /// <summary><c>decimal.Negate</c> and unary <c>-</c>: flips the sign, so <c>-(0m)</c> is <c>-0</c>.</summary>
        internal static string Negate(string value)
        {
            NeoDecimalMath.DecimalParts parts = Parse(value);
            return WithSign(parts, !parts.Negative);
        }

        /// <summary><c>decimal.CopySign</c>: <paramref name="value"/>'s magnitude with <paramref name="sign"/>'s sign flag.</summary>
        internal static string CopySign(string value, string sign) => WithSign(Parse(value), Parse(sign).Negative);

        /// <summary><c>decimal.Sign</c>: 0 for either zero, else -1 or 1.</summary>
        internal static int Sign(string value) => Parse(value).Coefficient.Sign;

        /// <summary><c>decimal.Max</c>, also <c>MaxNumber</c>: a tie returns <paramref name="x"/>.</summary>
        internal static string Max(string x, string y) => NeoDecimalMath.Compare(x, y) >= 0 ? x : y;

        /// <summary><c>decimal.Min</c>, also <c>MinNumber</c>: a tie returns <paramref name="y"/>.</summary>
        internal static string Min(string x, string y) => NeoDecimalMath.Compare(x, y) < 0 ? x : y;

        /// <summary><c>INumber.MaxNative</c>'s default body: a tie returns <paramref name="y"/>.</summary>
        internal static string MaxNative(string x, string y) => NeoDecimalMath.Compare(x, y) > 0 ? x : y;

        /// <summary><c>INumber.MinNative</c>'s default body: a tie returns <paramref name="y"/>.</summary>
        internal static string MinNative(string x, string y) => NeoDecimalMath.Compare(x, y) < 0 ? x : y;

        /// <summary><c>decimal.MaxMagnitude</c>, also <c>MaxMagnitudeNumber</c>.</summary>
        internal static string MaxMagnitude(string x, string y)
        {
            int order = NeoDecimalMath.Compare(Abs(x), Abs(y));
            if (order > 0)
                return x;
            if (order == 0)
                return Parse(x).Negative ? y : x;
            return y;
        }

        /// <summary><c>decimal.MinMagnitude</c>, also <c>MinMagnitudeNumber</c>.</summary>
        internal static string MinMagnitude(string x, string y)
        {
            int order = NeoDecimalMath.Compare(Abs(x), Abs(y));
            if (order < 0)
                return x;
            if (order == 0)
                return Parse(x).Negative ? x : y;
            return y;
        }

        /// <summary><c>decimal.Clamp</c>, which is <c>Math.Clamp</c>: a bound is returned verbatim.</summary>
        internal static string Clamp(string value, string min, string max)
        {
            if (NeoDecimalMath.Compare(min, max) > 0)
                throw MinMaxError(min, max);
            if (NeoDecimalMath.Compare(value, min) < 0)
                return min;
            if (NeoDecimalMath.Compare(value, max) > 0)
                return max;
            return value;
        }

        /// <summary><c>INumber.ClampNative</c>'s default body.</summary>
        internal static string ClampNative(string value, string min, string max)
        {
            if (NeoDecimalMath.Compare(min, max) > 0)
                throw MinMaxError(min, max);
            return MinNative(MaxNative(value, min), max);
        }

        private static NeoRuntimeError MinMaxError(string min, string max) => NeoNumberMemberErrors.MinMax(
            NeoNumberFormatting.FormatDecimal(NeoDecimalValues.Parse(min), null, null),
            NeoNumberFormatting.FormatDecimal(NeoDecimalValues.Parse(max), null, null));

        /// <summary><c>decimal.Equals(decimal)</c> and the static <c>Equals</c>: numeric, so <c>-0</c> equals <c>0.00</c>.</summary>
        internal static bool Equals(string value, string other) => NeoDecimalMath.Compare(value, other) == 0;

        /// <summary><c>decimal.CompareTo(decimal)</c> and the static <c>Compare</c>: -1, 0 or 1.</summary>
        internal static int CompareTo(string value, string other) => NeoDecimalMath.Compare(value, other);

        /// <summary>
        /// <c>decimal.Equals(object)</c>: true only for a boxed decimal that
        /// <c>Equals</c> the value. Decimals and strings are both text, so the
        /// caller says whether a non-null <paramref name="other"/> is a decimal.
        /// </summary>
        internal static bool EqualsObject(string value, object? other, bool otherIsDecimal) =>
            otherIsDecimal && other is string box && Equals(value, box);

        /// <summary>
        /// <c>decimal.CompareTo(object)</c>: 1 for null, and <c>NeoArgumentError</c>
        /// for a box of another type.
        /// </summary>
        internal static int CompareToObject(string value, object? other, bool otherIsDecimal)
        {
            if (other is null)
                return 1;
            if (!otherIsDecimal || other is not string box)
                throw NeoNumberMemberErrors.MustBeType(NeoNumericKind.Decimal);
            return CompareTo(value, box);
        }
    }
}
