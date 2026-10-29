// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Globalization;
using System.Numerics;

namespace NeoCompose.Runtime.NeoScript.Numerics
{
    /// <summary>The three <c>INumberBase</c> conversion factories.</summary>
    internal enum NeoCreateMode
    {
        Checked,
        Saturating,
        Truncating,
    }

    /// <summary>
    /// NeoScript's explicit numeric conversions and <c>Create*</c> factories
    /// (P127 §3.3, §7.3), the twin of the compose runtime's
    /// <c>numerics/conversions.ts</c>. Decimals are canonical text. Nothing
    /// here relies on a CLR conversion outside the range where every runtime
    /// agrees (§3.3 "The SDK never delegates").
    /// </summary>
    internal static class NeoNumericConversions
    {
        private const string DecimalMaxValue = "79228162514264337593543950335";
        private const string DecimalMinValue = "-79228162514264337593543950335";
        private const int DecimalScaleMax = 28;
        private const double TwoPow32 = 4294967296.0;
        private const double TwoPow64 = 18446744073709551616.0;

        private static readonly BigInteger TwoPow96 = BigInteger.One << 96;

        /// <summary>.NET's <c>DoublePowers10</c>, as far as these conversions index it.</summary>
        private static readonly double[] DoublePowers10 =
        {
            1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9,
            1e10, 1e11, 1e12, 1e13, 1e14, 1e15, 1e16, 1e17, 1e18, 1e19,
            1e20, 1e21, 1e22, 1e23, 1e24, 1e25, 1e26, 1e27, 1e28,
        };

        /// <summary><c>(int)value</c>: truncates; unchecked saturates and maps NaN to 0.</summary>
        internal static int SingleToInt32(float value, bool isChecked)
        {
            double exact = value;
            if (double.IsNaN(exact))
            {
                if (isChecked)
                    throw NeoInt32.Overflow();
                return 0;
            }
            if (exact >= 2147483648.0)
            {
                if (isChecked)
                    throw NeoInt32.Overflow();
                return int.MaxValue;
            }
            if (exact <= -2147483649.0)
            {
                if (isChecked)
                    throw NeoInt32.Overflow();
                return int.MinValue;
            }
            return (int)exact;
        }

        /// <summary><c>(int)value</c> for a decimal: truncates, and throws out of range in any context.</summary>
        internal static int DecimalToInt32(string value)
        {
            BigInteger truncated = Truncate(value);
            if (truncated > int.MaxValue)
                throw Int32Range();
            if (truncated < int.MinValue)
                throw Int32Range();
            return (int)truncated;
        }

        /// <summary>
        /// <c>(decimal)value</c>: .NET's <c>DecCalc.VarDecFromR4</c>. Rounds to
        /// seven significant digits, then drops the trailing zeros the scaling added.
        /// </summary>
        internal static string SingleToDecimal(float value)
        {
            int exp = ((BitConverter.SingleToInt32Bits(value) >> 23) & 0xFF) - 126;
            if (exp < -94)
                return "0";
            if (exp > 96)
                throw NeoDecimalMath.Overflow();

            bool negative = value < 0;
            double dbl = Math.Abs((double)value);
            int power = 6 - ((exp * 19728) >> 16);
            if (power >= 0)
            {
                if (power > DecimalScaleMax)
                    power = DecimalScaleMax;
                dbl *= DoublePowers10[power];
            }
            else if (power != -1 || dbl >= 1e7)
            {
                dbl /= DoublePowers10[-power];
            }
            else
            {
                power = 0;
            }
            if (dbl < 1e6 && power < DecimalScaleMax)
            {
                dbl *= 10;
                power++;
            }

            // Round half to even; dbl is below 1e7, so the fraction is exact.
            uint mant = (uint)(int)dbl;
            double fraction = dbl - (int)mant;
            if (fraction > 0.5 || (fraction == 0.5 && (mant & 1) != 0))
                mant++;
            if (mant == 0)
                return "0";

            BigInteger coefficient;
            int scale;
            if (power < 0)
            {
                coefficient = mant * BigInteger.Pow(10, -power);
                if (coefficient >= TwoPow96)
                    throw NeoDecimalMath.Overflow();
                scale = 0;
            }
            else
            {
                int lmax = Math.Min(power, 6);
                if (lmax >= 4 && mant % 10000 == 0)
                {
                    mant /= 10000;
                    power -= 4;
                    lmax -= 4;
                }
                if (lmax >= 2 && mant % 100 == 0)
                {
                    mant /= 100;
                    power -= 2;
                    lmax -= 2;
                }
                if (lmax >= 1 && mant % 10 == 0)
                {
                    mant /= 10;
                    power--;
                }
                coefficient = mant;
                scale = power;
            }
            return NeoDecimalMath.FormatParts(new NeoDecimalMath.DecimalParts(negative ? -coefficient : coefficient, scale));
        }

        /// <summary>
        /// <c>(float)value</c> for a decimal: .NET's <c>VarR4FromDec</c>, which
        /// rounds <c>VarR8FromDec</c>'s double to float.
        /// </summary>
        internal static float DecimalToSingle(string value)
        {
            NeoDecimalMath.DecimalParts parts = NeoDecimalMath.ParseArg(value, "value");
            BigInteger magnitude = BigInteger.Abs(parts.Coefficient);
            ulong low64 = (ulong)(magnitude & ulong.MaxValue);
            uint high = (uint)(magnitude >> 64);
            // (double)low64, rounded once: the high word times 2^32 is exact.
            double low = (double)(uint)(low64 >> 32) * TwoPow32 + (uint)low64;
            double dbl = (low + high * TwoPow64) / DoublePowers10[parts.Scale];
            return (float)(parts.Negative ? -dbl : dbl);
        }

        internal static float SingleFromInt32(int value) => value;

        internal static string DecimalFromInt32(int value) => value.ToString(CultureInfo.InvariantCulture);

        /// <summary><c>int.Create*</c> from a float: checked throws, the others saturate.</summary>
        internal static int Int32FromSingle(float value, NeoCreateMode mode) => SingleToInt32(value, mode == NeoCreateMode.Checked);

        /// <summary><c>int.Create*</c> from a decimal: checked throws, the others clamp.</summary>
        internal static int Int32FromDecimal(string value, NeoCreateMode mode)
        {
            if (mode == NeoCreateMode.Checked)
                return DecimalToInt32(value);
            BigInteger truncated = Truncate(value);
            if (truncated >= int.MaxValue)
                return int.MaxValue;
            if (truncated <= int.MinValue)
                return int.MinValue;
            return (int)truncated;
        }

        /// <summary>
        /// <c>decimal.Create*</c> from a float: checked throws; the others clamp
        /// at ±2^96 and map NaN to <c>0.0</c>, as .NET 10 does.
        /// </summary>
        internal static string DecimalFromSingle(float value, NeoCreateMode mode)
        {
            if (mode == NeoCreateMode.Checked)
                return SingleToDecimal(value);
            if (value >= 79228162514264337593543950336.0f)
                return DecimalMaxValue;
            if (value <= -79228162514264337593543950336.0f)
                return DecimalMinValue;
            if (float.IsNaN(value))
                return "0.0";
            return SingleToDecimal(value);
        }

        private static BigInteger Truncate(string value)
        {
            NeoDecimalMath.DecimalParts parts = NeoDecimalMath.ParseArg(value, "value");
            return parts.Scale == 0 ? parts.Coefficient : BigInteger.Divide(parts.Coefficient, BigInteger.Pow(10, parts.Scale));
        }

        private static NeoRuntimeError Int32Range() => new(
            NeoErrorClass.NeoOverflowError,
            "Value was either too large or too small for an Int32.");
    }
}
