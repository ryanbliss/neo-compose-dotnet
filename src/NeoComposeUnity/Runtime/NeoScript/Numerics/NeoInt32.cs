// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Runtime.NeoScript.Numerics
{
    /// <summary>
    /// NeoScript's int32 operators and library rules (P127 §3.1), the twin of
    /// the compose runtime's <c>numerics/int32.ts</c>. Every result is exact:
    /// arithmetic runs in <c>long</c> and wraps with <c>unchecked((int)x)</c>,
    /// never through <c>double</c>. <c>&amp;</c>, <c>|</c>, <c>^</c> and
    /// <c>~</c> are C#'s own operators.
    /// </summary>
    internal static class NeoInt32
    {
        private const double TwoPow32 = 4294967296d;
        private const double TwoPow31 = 2147483648d;

        internal static int Add(int left, int right, bool isChecked) => Fit((long)left + right, isChecked);

        internal static int Subtract(int left, int right, bool isChecked) => Fit((long)left - right, isChecked);

        internal static int Multiply(int left, int right, bool isChecked) => Fit((long)left * right, isChecked);

        /// <summary><c>/</c> truncates. <c>int.MinValue / -1</c> overflows in any context.</summary>
        internal static int Divide(int left, int right)
        {
            if (right == 0)
                throw new NeoRuntimeError(NeoErrorClass.NeoDivideByZeroError, "Division by zero");
            if (left == int.MinValue && right == -1)
                throw Overflow();
            return left / right;
        }

        /// <summary><c>%</c> takes the dividend's sign. <c>int.MinValue % -1</c> overflows in any context.</summary>
        internal static int Remainder(int left, int right)
        {
            if (right == 0)
                throw new NeoRuntimeError(NeoErrorClass.NeoDivideByZeroError, "Modulo by zero");
            if (left == int.MinValue && right == -1)
                throw Overflow();
            return left % right;
        }

        /// <summary>C# masks an int shift count with <c>&amp; 31</c>.</summary>
        internal static int ShiftLeft(int left, int right) => left << right;

        internal static int ShiftRight(int left, int right) => left >> right;

        /// <summary><c>&gt;&gt;&gt;</c>, which Unity's C# lacks.</summary>
        internal static int UnsignedShiftRight(int left, int right) => unchecked((int)((uint)left >> right));

        internal static int Negate(int value, bool isChecked) => Fit(-(long)value, isChecked);

        /// <summary>The new value of <c>++</c>.</summary>
        internal static int Increment(int value, bool isChecked) => Fit((long)value + 1, isChecked);

        /// <summary>The new value of <c>--</c>.</summary>
        internal static int Decrement(int value, bool isChecked) => Fit((long)value - 1, isChecked);

        /// <summary><c>Math.Abs(int)</c> and <c>int.Abs</c>.</summary>
        internal static int Abs(int value)
        {
            if (value == int.MinValue)
            {
                throw new NeoRuntimeError(
                    NeoErrorClass.NeoOverflowError,
                    "Negating the minimum value of a twos complement number is invalid.");
            }
            return value < 0 ? -value : value;
        }

        /// <summary><c>Enumerable.Sum</c> over ints: checked, in order.</summary>
        internal static int Sum(IEnumerable<int> values)
        {
            int sum = 0;
            foreach (int value in values)
                sum = Add(sum, value, true);
            return sum;
        }

        /// <summary>
        /// <c>Enumerable.Average</c> over ints as a NeoScript float: the exact
        /// 64-bit total divided as a double, then rounded to float. Null for no values.
        /// </summary>
        internal static float? Average(IEnumerable<int> values)
        {
            long total = 0;
            long count = 0;
            foreach (int value in values)
            {
                total += value;
                count++;
            }
            if (count == 0)
                return null;
            return (float)((double)total / count);
        }

        /// <summary>
        /// The int32 an evaluator number holds: its integer part modulo 2^32,
        /// as the compose runtime's <c>| 0</c> reads it. NaN and the
        /// infinities read as 0.
        /// </summary>
        internal static int Wrap(double value)
        {
            if (value is >= int.MinValue and <= int.MaxValue)
                return (int)value;
            if (double.IsNaN(value) || double.IsInfinity(value))
                return 0;
            double remainder = System.Math.Truncate(value) % TwoPow32;
            if (remainder < 0)
                remainder += TwoPow32;
            if (remainder >= TwoPow31)
                remainder -= TwoPow32;
            return (int)remainder;
        }

        internal static NeoRuntimeError Overflow() => new(
            NeoErrorClass.NeoOverflowError,
            "Arithmetic operation resulted in an overflow.");

        private static int Fit(long exact, bool isChecked)
        {
            if (isChecked && (exact > int.MaxValue || exact < int.MinValue))
                throw Overflow();
            return unchecked((int)exact);
        }
    }
}
