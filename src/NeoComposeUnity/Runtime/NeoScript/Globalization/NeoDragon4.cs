// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace NeoCompose.Runtime.NeoScript.Globalization
{
    /// <summary>.NET's <c>NumberBuffer.Kind</c>.</summary>
    internal enum NeoNumberBufferKind
    {
        Integer,
        Decimal,
        FloatingPoint,
    }

    /// <summary>.NET's <c>NumberBuffer</c>, with <see cref="Digits"/> holding exactly <c>DigitsCount</c> ASCII digits.</summary>
    internal sealed class NeoNumberBuffer
    {
        internal NeoNumberBufferKind Kind;
        internal string Digits = "";
        internal int Scale;
        internal bool IsNegative;
    }

    /// <summary>
    /// .NET 10's <c>Number.Dragon4</c> for float32, with exact
    /// <see cref="BigInteger"/> division in place of
    /// <c>BigInteger.HeuristicDivide</c> (which is exact, so the normalizing
    /// shift is unneeded).
    /// </summary>
    internal static class NeoDragon4
    {
        private const double Log10Of2 = 0.30102999566398119521373889472449;

        // Number.SingleNumberBufferLength: the digit buffer that bounds the cutoff.
        private const int SingleBufferLength = 114;

        private static readonly List<BigInteger> powersOf10 = new() { BigInteger.One };

        /// <summary>
        /// Writes the digits and scale of a positive, finite, nonzero float32
        /// into <paramref name="number"/>. <paramref name="cutoffNumber"/> -1
        /// asks for the shortest round-trip digits; otherwise it counts
        /// significant or fractional digits.
        /// </summary>
        internal static void Run(float value, int cutoffNumber, bool isSignificantDigits, NeoNumberBuffer number)
        {
            int bits = BitConverter.SingleToInt32Bits(value);
            int biasedExponent = (bits >> 23) & 0xff;
            int fraction = bits & 0x7fffff;

            // Integers below 2^24 are exact; Dragon4 prints them digit for digit.
            if (biasedExponent != 0
                && value < 0x1000000
                && (float)(int)value == value
                && (cutoffNumber == -1 || !isSignificantDigits || cutoffNumber >= 8))
            {
                string text = NeoNumberFormatting.Int32Text((int)value);
                int end = text.Length;
                while (text[end - 1] == '0')
                    end--;
                number.Digits = end == text.Length ? text : text.Substring(0, end);
                number.Scale = text.Length;
                return;
            }

            int mantissa = biasedExponent != 0 ? fraction | 0x800000 : fraction;
            int exponent = biasedExponent != 0 ? biasedExponent - 150 : -149;
            int mantissaHighBitIdx = HighBitIndex(mantissa);
            bool hasUnequalMargins = mantissa == 0x800000;

            var m = new BigInteger(mantissa);
            BigInteger scale;
            BigInteger scaledValue;
            BigInteger scaledMarginLow;
            if (exponent > 0)
            {
                scaledValue = ((hasUnequalMargins ? 4 : 2) * m) << exponent;
                scale = hasUnequalMargins ? 4 : 2;
                scaledMarginLow = BigInteger.One << exponent;
            }
            else
            {
                scaledValue = (hasUnequalMargins ? 4 : 2) * m;
                scale = BigInteger.One << (-exponent + (hasUnequalMargins ? 2 : 1));
                scaledMarginLow = BigInteger.One;
            }
            BigInteger scaledMarginHigh = hasUnequalMargins ? scaledMarginLow * 2 : scaledMarginLow;

            int digitExponent = (int)Math.Ceiling((mantissaHighBitIdx + exponent) * Log10Of2 - 0.69);
            if (digitExponent > 0)
            {
                scale *= PowerOf10(digitExponent);
            }
            else if (digitExponent < 0)
            {
                BigInteger power = PowerOf10(-digitExponent);
                scaledValue *= power;
                scaledMarginLow *= power;
                scaledMarginHigh = hasUnequalMargins ? scaledMarginLow * 2 : scaledMarginLow;
            }

            bool isEven = mantissa % 2 == 0;
            bool estimateTooLow;
            if (cutoffNumber == -1)
            {
                BigInteger upper = scaledValue + scaledMarginHigh;
                estimateTooLow = isEven ? upper >= scale : upper > scale;
            }
            else
            {
                estimateTooLow = scaledValue >= scale;
            }

            if (estimateTooLow)
            {
                digitExponent++;
            }
            else
            {
                scaledValue *= 10;
                scaledMarginLow *= 10;
                scaledMarginHigh = hasUnequalMargins ? scaledMarginLow * 2 : scaledMarginLow;
            }

            int cutoffExponent = digitExponent - SingleBufferLength;
            if (cutoffNumber != -1)
            {
                int desiredCutoffExponent = isSignificantDigits ? digitExponent - cutoffNumber : -cutoffNumber;
                if (desiredCutoffExponent > cutoffExponent)
                    cutoffExponent = desiredCutoffExponent;
            }

            int decimalExponent = --digitExponent;
            var digits = new StringBuilder();
            bool low;
            bool high;
            int outputDigit;

            if (cutoffNumber == -1)
            {
                while (true)
                {
                    outputDigit = (int)BigInteger.DivRem(scaledValue, scale, out scaledValue);
                    BigInteger compareLow = scaledValue - scaledMarginLow;
                    BigInteger compareHigh = scaledValue + scaledMarginHigh - scale;
                    low = isEven ? compareLow.Sign <= 0 : compareLow.Sign < 0;
                    high = isEven ? compareHigh.Sign >= 0 : compareHigh.Sign > 0;
                    if (low || high || digitExponent == cutoffExponent)
                        break;
                    digits.Append((char)('0' + outputDigit));
                    scaledValue *= 10;
                    scaledMarginLow *= 10;
                    scaledMarginHigh = hasUnequalMargins ? scaledMarginLow * 2 : scaledMarginLow;
                    digitExponent--;
                }
            }
            else if (digitExponent >= cutoffExponent)
            {
                low = false;
                high = false;
                while (true)
                {
                    outputDigit = (int)BigInteger.DivRem(scaledValue, scale, out scaledValue);
                    if (scaledValue.IsZero || digitExponent <= cutoffExponent)
                        break;
                    digits.Append((char)('0' + outputDigit));
                    scaledValue *= 10;
                    digitExponent--;
                }
            }
            else
            {
                // The first significant digit lies past the cutoff: it becomes the rounding digit.
                outputDigit = (int)BigInteger.DivRem(scaledValue, scale, out scaledValue);
                if (outputDigit > 5 || (outputDigit == 5 && !scaledValue.IsZero))
                {
                    decimalExponent++;
                    outputDigit = 1;
                }
                number.Digits = ((char)('0' + outputDigit)).ToString();
                number.Scale = decimalExponent + 1;
                return;
            }

            bool roundDown = low;
            if (low == high)
            {
                BigInteger compare = scaledValue * 2 - scale;
                roundDown = compare.Sign < 0;
                if (compare.IsZero)
                    roundDown = (outputDigit & 1) == 0;
            }

            if (roundDown)
            {
                digits.Append((char)('0' + outputDigit));
            }
            else if (outputDigit == 9)
            {
                int end = digits.Length;
                while (end > 0 && digits[end - 1] == '9')
                    end--;
                if (end == 0)
                {
                    digits.Clear();
                    digits.Append('1');
                    decimalExponent++;
                }
                else
                {
                    digits.Length = end;
                    digits[end - 1] = (char)(digits[end - 1] + 1);
                }
            }
            else
            {
                digits.Append((char)('1' + outputDigit));
            }

            number.Digits = digits.ToString();
            number.Scale = decimalExponent + 1;
        }

        private static int HighBitIndex(int mantissa)
        {
            int index = 31;
            while (index > 0 && (mantissa & (1 << index)) == 0)
                index--;
            return index;
        }

        private static BigInteger PowerOf10(int exponent)
        {
            lock (powersOf10)
            {
                for (int index = powersOf10.Count; index <= exponent; index++)
                    powersOf10.Add(powersOf10[index - 1] * 10);
                return powersOf10[exponent];
            }
        }
    }
}
