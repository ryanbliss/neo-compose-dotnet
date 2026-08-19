// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Globalization;
using System.Numerics;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Exact result exceeded 28 significant digits or scale 28
    /// (specs/decimal-member.md decision 7). Never thrown by an explicit
    /// rounding entry point (<see cref="NeoDecimalMath.Round"/> /
    /// <see cref="NeoDecimalMath.Divide"/> reduce precision on purpose).
    /// </summary>
    public class DecimalOverflowException : Exception
    {
        public DecimalOverflowException(string message) : base(message) { }
    }

    /// <summary><see cref="NeoDecimalMath.Divide"/> with a zero divisor.</summary>
    public class DecimalDivisionByZeroException : Exception
    {
        public DecimalDivisionByZeroException(string message) : base(message) { }
    }

    /// <summary>A rounding-digits argument outside 0..28.</summary>
    public class DecimalDigitsRangeException : Exception
    {
        public DecimalDigitsRangeException(string message) : base(message) { }
    }

    /// <summary><see cref="NeoDecimalMath.FromFloat"/> received NaN or ±Infinity.</summary>
    public class DecimalNonFiniteException : Exception
    {
        public DecimalNonFiniteException(string message) : base(message) { }
    }

    /// <summary>
    /// Exact decimal math over canonical decimal strings — the C# mirror of
    /// the web's <c>src/models/decimal/decimal-math.ts</c>
    /// (specs/decimal-member.md decision 7). Both runtimes represent a
    /// decimal as a <c>(coefficient, scale)</c> integer pair
    /// (<see cref="BigInteger"/> here, <c>bigint</c> on the web) so neither
    /// depends on the other's rounding quirks; native
    /// <see cref="decimal"/> is never used for arithmetic. The two are
    /// locked together by <c>decimal-parity-fixture.json</c>.
    ///
    /// <para>Semantics are exact-or-error: add/subtract/multiply never round —
    /// a result past the C# decimal envelope throws
    /// <see cref="DecimalOverflowException"/>. Rounding happens only through
    /// the explicit <see cref="Round"/> / <see cref="Divide"/> /
    /// <see cref="FromFloat"/> entry points, always round-half-even.</para>
    /// </summary>
    public static class NeoDecimalMath
    {
        private const int MaxSignificantDigits = NeoDecimalValues.MaxSignificantDigits;
        private const int MaxScale = NeoDecimalValues.MaxScale;

        /// <summary>A decimal as an exact scaled integer: coefficient * 10^-scale.</summary>
        private readonly struct DecimalParts
        {
            public readonly BigInteger Coefficient;
            public readonly int Scale;

            public DecimalParts(BigInteger coefficient, int scale)
            {
                Coefficient = coefficient;
                Scale = scale;
            }
        }

        private static BigInteger Pow10(int exponent)
        {
            return BigInteger.Pow(10, exponent);
        }

        private static void AssertCanonicalInput(string value, string argName)
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    argName,
                    $"Decimal argument \"{argName}\" must not be null.");
            }
            NeoDecimalValues.Violation violation = NeoDecimalValues.GetViolation(value);
            if (violation != NeoDecimalValues.Violation.None)
            {
                throw new ArgumentException(
                    $"Decimal argument \"{argName}\" (\"{value}\") is not a canonical decimal string.",
                    argName);
            }
        }

        private static DecimalParts ParseArg(string value, string argName)
        {
            AssertCanonicalInput(value, argName);
            bool negative = value.StartsWith("-", StringComparison.Ordinal);
            string unsigned = negative ? value.Substring(1) : value;
            int pointIndex = unsigned.IndexOf('.');
            string digits = pointIndex < 0
                ? unsigned
                : unsigned.Substring(0, pointIndex) + unsigned.Substring(pointIndex + 1);
            int scale = pointIndex < 0 ? 0 : unsigned.Length - pointIndex - 1;
            BigInteger magnitude = BigInteger.Parse(digits, CultureInfo.InvariantCulture);
            return new DecimalParts(negative ? -magnitude : magnitude, scale);
        }

        /// <summary>
        /// Formats parts to the canonical string. Scale is preserved verbatim
        /// (coefficient 110, scale 2 → "1.10"). A zero coefficient never
        /// carries a sign ("-0" is representable as input but never produced).
        /// </summary>
        private static string FormatParts(DecimalParts parts)
        {
            bool negative = parts.Coefficient.Sign < 0;
            string digits = BigInteger.Abs(parts.Coefficient).ToString(CultureInfo.InvariantCulture);
            string unsigned;
            if (parts.Scale == 0)
            {
                unsigned = digits;
            }
            else
            {
                string padded = digits.PadLeft(parts.Scale + 1, '0');
                int pointIndex = padded.Length - parts.Scale;
                unsigned = padded.Substring(0, pointIndex) + "." + padded.Substring(pointIndex);
            }
            if (parts.Coefficient.IsZero)
            {
                return unsigned;
            }
            return negative ? "-" + unsigned : unsigned;
        }

        private static int SignificantDigitCount(BigInteger coefficient)
        {
            return BigInteger.Abs(coefficient).ToString(CultureInfo.InvariantCulture).Length;
        }

        private static DecimalParts AssertWithinEnvelope(DecimalParts parts, string operation)
        {
            if (SignificantDigitCount(parts.Coefficient) > MaxSignificantDigits)
            {
                throw new DecimalOverflowException(
                    $"Decimal overflow in {operation}: the exact result exceeds {MaxSignificantDigits} " +
                    "significant digits. Round explicitly (Round/Divide) to reduce precision.");
            }
            if (parts.Scale > MaxScale)
            {
                throw new DecimalOverflowException(
                    $"Decimal overflow in {operation}: the exact result exceeds scale {MaxScale}. " +
                    "Round explicitly (Round/Divide) to reduce precision.");
            }
            return parts;
        }

        private readonly struct AlignedPair
        {
            public readonly BigInteger A;
            public readonly BigInteger B;
            public readonly int Scale;

            public AlignedPair(BigInteger a, BigInteger b, int scale)
            {
                A = a;
                B = b;
                Scale = scale;
            }
        }

        private static AlignedPair AlignScales(DecimalParts a, DecimalParts b)
        {
            int scale = Math.Max(a.Scale, b.Scale);
            return new AlignedPair(
                a.Coefficient * Pow10(scale - a.Scale),
                b.Coefficient * Pow10(scale - b.Scale),
                scale);
        }

        /// <summary>Exact numeric comparison; scale is ignored ("1.10" == "1.1").</summary>
        public static int Compare(string a, string b)
        {
            AlignedPair aligned = AlignScales(ParseArg(a, "a"), ParseArg(b, "b"));
            if (aligned.A < aligned.B) return -1;
            if (aligned.A > aligned.B) return 1;
            return 0;
        }

        /// <summary>Exact addition; result scale is max(scale(a), scale(b)).</summary>
        public static string Add(string a, string b)
        {
            AlignedPair aligned = AlignScales(ParseArg(a, "a"), ParseArg(b, "b"));
            return FormatParts(
                AssertWithinEnvelope(
                    new DecimalParts(aligned.A + aligned.B, aligned.Scale),
                    "addition"));
        }

        /// <summary>Exact subtraction; result scale is max(scale(a), scale(b)).</summary>
        public static string Subtract(string a, string b)
        {
            AlignedPair aligned = AlignScales(ParseArg(a, "a"), ParseArg(b, "b"));
            return FormatParts(
                AssertWithinEnvelope(
                    new DecimalParts(aligned.A - aligned.B, aligned.Scale),
                    "subtraction"));
        }

        /// <summary>Exact multiplication; result scale is scale(a) + scale(b).</summary>
        public static string Multiply(string a, string b)
        {
            DecimalParts left = ParseArg(a, "a");
            DecimalParts right = ParseArg(b, "b");
            return FormatParts(
                AssertWithinEnvelope(
                    new DecimalParts(
                        left.Coefficient * right.Coefficient,
                        left.Scale + right.Scale),
                    "multiplication"));
        }

        /// <summary>
        /// Exact minimum (P69 §2.2). The winning argument is returned
        /// verbatim, so its scale survives ("1.10" stays "1.10"), and a tie
        /// returns <paramref name="a"/> — the pinned cross-runtime rule, since
        /// the two arguments can be equal yet spelled differently.
        /// </summary>
        public static string Min(string a, string b)
        {
            return Compare(a, b) <= 0 ? a : b;
        }

        /// <summary>Exact maximum; ties return <paramref name="a"/> (see <see cref="Min"/>).</summary>
        public static string Max(string a, string b)
        {
            return Compare(a, b) >= 0 ? a : b;
        }

        /// <summary>
        /// Exact magnitude (P69 §2.6); scale is preserved ("-1.50" → "1.50").
        /// </summary>
        public static string Abs(string value)
        {
            DecimalParts parts = ParseArg(value, "value");
            return FormatParts(
                new DecimalParts(BigInteger.Abs(parts.Coefficient), parts.Scale));
        }

        /// <summary>
        /// Truncation toward zero to scale 0, reporting whether any non-zero
        /// fraction was dropped — the shared core of <see cref="Floor"/>,
        /// <see cref="Ceiling"/> and <see cref="Truncate"/>, which differ only
        /// in how they adjust that dropped fraction. Integer division on
        /// <see cref="BigInteger"/> already truncates toward zero.
        /// </summary>
        private static DecimalParts TruncateParts(DecimalParts parts, out bool droppedFraction)
        {
            if (parts.Scale == 0)
            {
                droppedFraction = false;
                return parts;
            }
            BigInteger quotient = BigInteger.DivRem(
                parts.Coefficient,
                Pow10(parts.Scale),
                out BigInteger remainder);
            droppedFraction = !remainder.IsZero;
            return new DecimalParts(quotient, 0);
        }

        /// <summary>
        /// Largest integer &lt;= value, as a canonical scale-0 decimal
        /// (P69 §2.5). Directional, never half-even: "-4.25" → "-5".
        /// </summary>
        public static string Floor(string value)
        {
            DecimalParts parts = ParseArg(value, "value");
            DecimalParts truncated = TruncateParts(parts, out bool droppedFraction);
            if (droppedFraction && parts.Coefficient.Sign < 0)
            {
                truncated = new DecimalParts(truncated.Coefficient - BigInteger.One, 0);
            }
            return FormatParts(AssertWithinEnvelope(truncated, "floor"));
        }

        /// <summary>
        /// Smallest integer &gt;= value, as a canonical scale-0 decimal
        /// (P69 §2.5): "-4.25" → "-4", "4.25" → "5".
        /// </summary>
        public static string Ceiling(string value)
        {
            DecimalParts parts = ParseArg(value, "value");
            DecimalParts truncated = TruncateParts(parts, out bool droppedFraction);
            if (droppedFraction && parts.Coefficient.Sign > 0)
            {
                truncated = new DecimalParts(truncated.Coefficient + BigInteger.One, 0);
            }
            return FormatParts(AssertWithinEnvelope(truncated, "ceiling"));
        }

        /// <summary>
        /// The integer part, as a canonical scale-0 decimal (P69 §2.5):
        /// toward zero, so "-4.25" → "-4".
        /// </summary>
        public static string Truncate(string value)
        {
            DecimalParts truncated = TruncateParts(ParseArg(value, "value"), out _);
            return FormatParts(AssertWithinEnvelope(truncated, "truncate"));
        }

        private static void AssertDigitsInRange(int digits, string operation)
        {
            if (digits < 0 || digits > MaxScale)
            {
                throw new DecimalDigitsRangeException(
                    $"Decimal {operation} digits must be between 0 and {MaxScale} (received {digits}).");
            }
        }

        /// <summary>
        /// Round-half-even of coefficient * 10^-scale to <paramref name="digits"/>
        /// fractional digits. When digits &gt;= scale the value is already exact
        /// and returned unchanged (no zero-padding).
        /// </summary>
        private static DecimalParts RoundParts(DecimalParts parts, int digits)
        {
            if (digits >= parts.Scale)
            {
                return parts;
            }
            int drop = parts.Scale - digits;
            BigInteger divisor = Pow10(drop);
            bool negative = parts.Coefficient.Sign < 0;
            BigInteger magnitude = BigInteger.Abs(parts.Coefficient);
            BigInteger quotient = magnitude / divisor;
            BigInteger remainder = magnitude % divisor;
            BigInteger doubled = remainder * 2;
            if (doubled > divisor || (doubled == divisor && !(quotient % 2).IsZero))
            {
                quotient += 1;
            }
            return new DecimalParts(negative ? -quotient : quotient, digits);
        }

        /// <summary>Round-half-even to <paramref name="digits"/> (0..28). Never pads scale upward.</summary>
        public static string Round(string value, int digits)
        {
            AssertDigitsInRange(digits, "round");
            DecimalParts rounded = RoundParts(ParseArg(value, "value"), digits);
            return FormatParts(AssertWithinEnvelope(rounded, "round"));
        }

        /// <summary>
        /// a / b with exactly <paramref name="digits"/> fractional digits
        /// (round-half-even). The dividend is scaled so the rounding decision
        /// sees the remainder; the quotient is computed on exact integers.
        /// </summary>
        public static string Divide(string a, string b, int digits)
        {
            AssertDigitsInRange(digits, "divide");
            DecimalParts dividend = ParseArg(a, "a");
            DecimalParts divisor = ParseArg(b, "b");
            if (divisor.Coefficient.IsZero)
            {
                throw new DecimalDivisionByZeroException(
                    $"Decimal division by zero: \"{a}\" / \"{b}\".");
            }
            int exponent = divisor.Scale - dividend.Scale + digits;
            BigInteger numerator = dividend.Coefficient;
            BigInteger denominator = divisor.Coefficient;
            if (exponent >= 0)
            {
                numerator *= Pow10(exponent);
            }
            else
            {
                denominator *= Pow10(-exponent);
            }
            bool negative = (numerator.Sign < 0) != (denominator.Sign < 0);
            BigInteger absNumerator = BigInteger.Abs(numerator);
            BigInteger absDenominator = BigInteger.Abs(denominator);
            BigInteger quotient = absNumerator / absDenominator;
            BigInteger remainder = absNumerator % absDenominator;
            BigInteger doubled = remainder * 2;
            if (doubled > absDenominator || (doubled == absDenominator && !(quotient % 2).IsZero))
            {
                quotient += 1;
            }
            DecimalParts parts = new DecimalParts(negative ? -quotient : quotient, digits);
            return FormatParts(AssertWithinEnvelope(parts, "division"));
        }

        /// <summary>
        /// Float64 → canonical decimal string via the double's EXACT binary
        /// expansion (mantissa/exponent bits through
        /// <see cref="BitConverter.DoubleToInt64Bits"/>), reduced to its
        /// minimal exact scale, then rounded half-even to
        /// <paramref name="digits"/>. Never consults
        /// <c>double.ToString</c>, so the result is identical across runtimes
        /// (Unity's Mono predates .NET Core's shortest-round-trip formatting).
        /// Distinct errors for NaN and ±Infinity.
        /// </summary>
        public static string FromFloat(double value, int digits)
        {
            AssertDigitsInRange(digits, "float conversion");
            if (double.IsNaN(value))
            {
                throw new DecimalNonFiniteException("Cannot convert NaN to a decimal.");
            }
            if (double.IsInfinity(value))
            {
                throw new DecimalNonFiniteException(
                    $"Cannot convert {(value > 0 ? "Infinity" : "-Infinity")} to a decimal.");
            }
            ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
            bool negative = (bits >> 63) == 1UL;
            int exponentBits = (int)((bits >> 52) & 0x7ffUL);
            long mantissaBits = (long)(bits & 0xfffffffffffffUL);
            // value = mantissa * 2^exponent, exactly.
            BigInteger mantissa;
            int exponent;
            if (exponentBits == 0)
            {
                // Subnormal (or zero): no implicit leading bit.
                mantissa = mantissaBits;
                exponent = -1074;
            }
            else
            {
                mantissa = (BigInteger)mantissaBits | (BigInteger.One << 52);
                exponent = exponentBits - 1075;
            }
            if (mantissa.IsZero)
            {
                // ±0 short-circuits to plain "0" — carrying the subnormal
                // scale (1074) through rounding would emit padding and a
                // cross-runtime mismatch with the web mirror.
                return "0";
            }
            if (negative)
            {
                mantissa = -mantissa;
            }
            // Exact decimal expansion: 2^e = 10^e / 5^e for e >= 0;
            // 2^-e = 5^e * 10^-e for e > 0.
            DecimalParts parts;
            if (exponent >= 0)
            {
                parts = new DecimalParts(mantissa * (BigInteger.One << exponent), 0);
            }
            else
            {
                parts = new DecimalParts(mantissa * BigInteger.Pow(5, -exponent), -exponent);
            }
            // Reduce to the minimal exact expansion before rounding: the raw
            // mantissa-times-5^k form carries trailing zeros for dyadic values
            // (0.5 → coefficient 5×10^52, scale 53), and the cross-runtime
            // contract is the SHORTEST exact form — "0.5", not "0.500".
            while (parts.Scale > 0 && (parts.Coefficient % 10).IsZero)
            {
                parts = new DecimalParts(parts.Coefficient / 10, parts.Scale - 1);
            }
            DecimalParts rounded = RoundParts(parts, digits);
            return FormatParts(AssertWithinEnvelope(rounded, "float conversion"));
        }

        /// <summary>
        /// Canonical decimal string → nearest double. Used only by NeoScript's
        /// explicit <c>ToFloat</c> — never by the editor UI (which edits the
        /// string directly, specs/decimal-member.md decision 8).
        /// </summary>
        public static double ToFloat(string value)
        {
            AssertCanonicalInput(value, "value");
            return double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }
}
