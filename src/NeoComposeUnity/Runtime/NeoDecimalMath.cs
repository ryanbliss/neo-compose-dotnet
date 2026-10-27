// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Globalization;
using System.Numerics;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// An explicit rounding entry point (<see cref="NeoDecimalMath.Round"/>,
    /// <see cref="NeoDecimalMath.Divide(string, string, int)"/>,
    /// <see cref="NeoDecimalMath.FromFloat"/>) produced a result outside the
    /// decimal envelope: a coefficient above 2⁹⁶ − 1 at the requested scale.
    /// It carries .NET's overflow message, and the evaluator raises it as
    /// <c>NeoOverflowError</c>, as the operators throw directly (P127 §3.4).
    /// </summary>
    public class DecimalOverflowException : Exception
    {
        public DecimalOverflowException(string message) : base(message) { }
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
    /// <para>The operators <c>+</c>, <c>-</c>, <c>*</c>, <c>/</c> and
    /// <c>%</c> (<see cref="Add"/>, <see cref="Subtract"/>,
    /// <see cref="Multiply"/>, <see cref="Divide(string, string)"/>,
    /// <see cref="Remainder"/>) follow <see cref="decimal"/> exactly
    /// (P127 §3.4): when the exact result needs more digits than the 96-bit
    /// coefficient holds, they round half-to-even by lowering the scale, and
    /// throw <c>NeoOverflowError</c> only when the integer part does not fit.
    /// Their control flow transcribes <c>Decimal.DecCalc</c> at dotnet/runtime
    /// 8f4c012be1d11eef2a7c1991cdb6302479f23b74 (release/10.0):
    /// <c>DecAddSub</c>, <c>VarDecMul</c>, <c>VarDecDiv</c> and
    /// <c>VarDecMod</c>, with <see cref="BigInteger"/> standing in for its
    /// 96-, 128- and 192-bit word buffers. The explicit
    /// <see cref="Round"/> / <see cref="Divide(string, string, int)"/> /
    /// <see cref="FromFloat"/> entry points round half-even to the digits they
    /// are given, and number formatting picks its midpoint rule per
    /// receiver.</para>
    /// </summary>
    public static class NeoDecimalMath
    {
        private static readonly BigInteger MaxCoefficient = (BigInteger.One << 96) - 1;
        private const int MaxScale = NeoDecimalValues.MaxScale;

        /// <summary>DecCalc's <c>MaxInt32Scale</c>: the largest power of 10 one 32-bit step scales by.</summary>
        private const int MaxInt32Scale = 9;

        /// <summary>DecCalc's <c>MaxInt64Scale</c>.</summary>
        private const int MaxInt64Scale = 19;

        /// <summary>10⁰ to 10⁵⁶: every power the operators align, scale or round by.</summary>
        private static readonly BigInteger[] PowersOf10 = BuildPowersOf10(2 * MaxScale);

        private static BigInteger[] BuildPowersOf10(int maxExponent)
        {
            var powers = new BigInteger[maxExponent + 1];
            powers[0] = BigInteger.One;
            for (int i = 1; i <= maxExponent; i++)
                powers[i] = powers[i - 1] * 10;
            return powers;
        }

        /// <summary>.NET's <c>SR.Overflow_Decimal</c>.</summary>
        internal const string OverflowMessage = "Value was either too large or too small for a Decimal.";

        internal static NeoRuntimeError Overflow() => new(NeoErrorClass.NeoOverflowError, OverflowMessage);

        /// <summary>.NET's <c>SR.Arg_DivideByZero</c>.</summary>
        private static NeoRuntimeError DivideByZero() => new(
            NeoErrorClass.NeoDivideByZeroError,
            "Attempted to divide by zero.");

        /// <summary>
        /// A decimal as an exact scaled integer: coefficient * 10^-scale. A
        /// zero keeps its sign, as <see cref="decimal"/> does (P127 §3.4).
        /// </summary>
        internal readonly struct DecimalParts
        {
            public readonly BigInteger Coefficient;
            public readonly int Scale;

            /// <summary>The sign: the coefficient's, or a zero's own.</summary>
            public readonly bool Negative;

            public DecimalParts(BigInteger coefficient, int scale)
                : this(coefficient, scale, coefficient.Sign < 0)
            {
            }

            /// <summary>Parts whose zero takes <paramref name="negativeZero"/>'s sign.</summary>
            public DecimalParts(BigInteger coefficient, int scale, bool negativeZero)
            {
                Coefficient = coefficient;
                Scale = scale;
                Negative = coefficient.IsZero ? negativeZero : coefficient.Sign < 0;
            }

            internal DecimalParts Negated() => new(-Coefficient, Scale, !Negative);
        }

        private static BigInteger Pow10(int exponent)
        {
            return exponent < PowersOf10.Length ? PowersOf10[exponent] : BigInteger.Pow(10, exponent);
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

        internal static DecimalParts ParseArg(string value, string argName)
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
            return new DecimalParts(negative ? -magnitude : magnitude, scale, negative);
        }

        /// <summary>
        /// Formats parts to the canonical string. Scale is preserved verbatim
        /// (coefficient 110, scale 2 → "1.10"), and so is a zero's sign
        /// ("-0.00").
        /// </summary>
        internal static string FormatParts(DecimalParts parts)
        {
            bool negative = parts.Negative;
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
            return negative ? "-" + unsigned : unsigned;
        }

        /// <summary>
        /// The explicit rounding entry points' overflow check. Their scale is
        /// at most the 0..28 digits they are given, so only the coefficient can
        /// leave the envelope; it throws .NET's <c>OverflowException</c>
        /// message, as the equivalent C# conversion does (P127 §3.4).
        /// </summary>
        private static DecimalParts AssertWithinEnvelope(DecimalParts parts)
        {
            if (BigInteger.Abs(parts.Coefficient) > MaxCoefficient)
            {
                throw new DecimalOverflowException(OverflowMessage);
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
            if (aligned.A < aligned.B)
                return -1;
            if (aligned.A > aligned.B)
                return 1;
            return 0;
        }

        /// <summary><c>a + b</c>, as <see cref="decimal"/>'s <c>+</c>.</summary>
        public static string Add(string a, string b) =>
            FormatParts(AddParts(ParseArg(a, "a"), ParseArg(b, "b")));

        /// <summary><c>a - b</c>, as <see cref="decimal"/>'s <c>-</c>.</summary>
        public static string Subtract(string a, string b) =>
            FormatParts(AddParts(ParseArg(a, "a"), ParseArg(b, "b").Negated()));

        /// <summary>
        /// <c>DecAddSub</c>: the sum at the larger scale, through
        /// <see cref="ScaleResult"/> when it needs more than 96 bits. When the
        /// lower-scale operand is zero, the other operand is the sum, scale
        /// and sign unchanged. A zero sum takes, at equal scales,
        /// <paramref name="a"/>'s sign, and at unequal ones the lower-scale
        /// operand's. (<c>DecAddSub</c>'s carry-past-96-bits branch for equal
        /// scales divides by 10 once, which is what <see cref="ScaleResult"/>
        /// does for a sum below 2⁹⁷.)
        /// </summary>
        private static DecimalParts AddParts(DecimalParts a, DecimalParts b)
        {
            if (a.Scale == b.Scale)
            {
                return ScaleResult(a.Coefficient + b.Coefficient, a.Scale, a.Negative);
            }
            DecimalParts lower = a.Scale < b.Scale ? a : b;
            DecimalParts higher = a.Scale < b.Scale ? b : a;
            if (lower.Coefficient.IsZero)
            {
                return higher;
            }
            BigInteger aligned = lower.Coefficient * Pow10(higher.Scale - lower.Scale);
            return ScaleResult(aligned + higher.Coefficient, higher.Scale, lower.Negative);
        }

        /// <summary><c>a * b</c>, as <see cref="decimal"/>'s <c>*</c>.</summary>
        public static string Multiply(string a, string b) =>
            FormatParts(MultiplyParts(ParseArg(a, "a"), ParseArg(b, "b")));

        /// <summary>
        /// <c>INumberBase&lt;decimal&gt;.MultiplyAddEstimate</c>: unfused,
        /// <c>(left * right) + addend</c>, so the product rounds first
        /// (P127 §7.3).
        /// </summary>
        public static string MultiplyAddEstimate(string left, string right, string addend) =>
            FormatParts(AddParts(MultiplyParts(ParseArg(left, "left"), ParseArg(right, "right")), ParseArg(addend, "addend")));

        /// <summary>
        /// <c>VarDecMul</c>: the exact product at the summed scale, through
        /// <see cref="ScaleResult"/>, with the signs' XOR. Its branches show in
        /// zeros: when both coefficients fit in 32 bits, a scale past 47 gives
        /// <c>0</c> (positive, scale 0) and otherwise the product rounds to
        /// scale 28 keeping the XOR sign; when either is wider, a zero product
        /// is <c>0</c> (positive, scale 0).
        /// </summary>
        private static DecimalParts MultiplyParts(DecimalParts left, DecimalParts right)
        {
            int scale = left.Scale + right.Scale;
            bool negative = left.Negative != right.Negative;
            BigInteger leftMagnitude = BigInteger.Abs(left.Coefficient);
            BigInteger rightMagnitude = BigInteger.Abs(right.Coefficient);
            BigInteger product = leftMagnitude * rightMagnitude;
            if (leftMagnitude <= uint.MaxValue && rightMagnitude <= uint.MaxValue)
            {
                // The product is below 2^64, so past scale 28 + 19 it rounds
                // to zero: ReturnZero.
                if (scale > MaxScale + MaxInt64Scale)
                {
                    return new DecimalParts(BigInteger.Zero, 0);
                }
            }
            else if (product.IsZero)
            {
                return new DecimalParts(BigInteger.Zero, 0);
            }
            return ScaleResult(negative ? -product : product, scale, negative);
        }

        /// <summary>
        /// <c>ScaleResult</c>: lowers the scale by the fewest digits that
        /// bring it to 28 or below and the coefficient, rounded half-even on
        /// the exact value, below 2⁹⁶. Throws <c>NeoOverflowError</c> when
        /// even scale 0 doesn't fit. <paramref name="negativeZero"/> is the
        /// sign of a zero result.
        /// </summary>
        private static DecimalParts ScaleResult(BigInteger coefficient, int scale, bool negativeZero)
        {
            BigInteger magnitude = BigInteger.Abs(coefficient);
            int reduce = Math.Max(0, scale - MaxScale);
            if (reduce == 0 && magnitude <= MaxCoefficient)
            {
                return new DecimalParts(coefficient, scale, negativeZero);
            }
            while (true)
            {
                if (reduce > scale)
                {
                    throw Overflow();
                }
                BigInteger rounded = RoundHalfEven(magnitude, reduce);
                if (rounded <= MaxCoefficient)
                {
                    return new DecimalParts(coefficient.Sign < 0 ? -rounded : rounded, scale - reduce, negativeZero);
                }
                reduce++;
            }
        }

        /// <summary><paramref name="magnitude"/> / 10^<paramref name="digits"/>, rounded half-even.</summary>
        private static BigInteger RoundHalfEven(BigInteger magnitude, int digits)
        {
            if (digits == 0)
            {
                return magnitude;
            }
            BigInteger divisor = Pow10(digits);
            BigInteger quotient = BigInteger.DivRem(magnitude, divisor, out BigInteger remainder);
            BigInteger doubled = remainder << 1;
            if (doubled > divisor || (doubled == divisor && !quotient.IsEven))
            {
                quotient += 1;
            }
            return quotient;
        }

        /// <summary><c>a / b</c>, as <see cref="decimal"/>'s <c>/</c> (P127 §3.4).</summary>
        public static string Divide(string a, string b) =>
            FormatParts(DivideParts(ParseArg(a, "a"), ParseArg(b, "b")));

        /// <summary>
        /// <c>VarDecDiv</c>. The quotient starts at the natural scale
        /// (dividend's minus divisor's). While a remainder is left, it takes
        /// as many more digits as fit (<see cref="SearchScale"/>, at most 9 at
        /// a time, up to scale 28), then rounds half-even; a negative scale
        /// scales up to 0. A quotient that had a remainder drops its trailing
        /// zeros (<c>Unscale</c>), so <c>1 / 4</c> is <c>0.25</c> while
        /// <c>1.000 / 4</c> stays <c>0.250</c>. The sign is the XOR of the
        /// operands'.
        /// </summary>
        private static DecimalParts DivideParts(DecimalParts dividend, DecimalParts divisor)
        {
            BigInteger den = BigInteger.Abs(divisor.Coefficient);
            if (den.IsZero)
            {
                throw DivideByZero();
            }
            int scale = dividend.Scale - divisor.Scale;
            bool unscale = false;
            BigInteger quotient = BigInteger.DivRem(BigInteger.Abs(dividend.Coefficient), den, out BigInteger remainder);
            while (true)
            {
                int curScale;
                if (remainder.IsZero)
                {
                    if (scale >= 0)
                    {
                        break;
                    }
                    curScale = Math.Min(MaxInt32Scale, -scale);
                }
                else
                {
                    // We need to unscale if and only if we have a non-zero remainder.
                    unscale = true;
                    if (scale == MaxScale || (curScale = SearchScale(quotient, scale)) == 0)
                    {
                        // No more scaling to be done, but remainder is non-zero. Round quotient.
                        BigInteger doubled = remainder << 1;
                        if (doubled > den || (doubled == den && !quotient.IsEven))
                        {
                            quotient += 1;
                            if (quotient > MaxCoefficient)
                            {
                                quotient = OverflowUnscale(quotient, ref scale, sticky: true);
                            }
                        }
                        break;
                    }
                }

                // HaveScale
                scale += curScale;
                quotient *= Pow10(curScale);
                if (quotient > MaxCoefficient)
                {
                    throw Overflow();
                }
                quotient += BigInteger.DivRem(remainder * Pow10(curScale), den, out remainder);
                if (quotient > MaxCoefficient)
                {
                    quotient = OverflowUnscale(quotient, ref scale, sticky: !remainder.IsZero);
                    break;
                }
            }

            if (unscale)
            {
                // Unscale: drop trailing zeros while the scale is positive.
                while (scale > 0 && (quotient % 10).IsZero)
                {
                    quotient /= 10;
                    scale--;
                }
            }
            bool negative = dividend.Negative != divisor.Negative;
            return new DecimalParts(negative ? -quotient : quotient, scale, negative);
        }

        /// <summary>
        /// <c>SearchScale</c>: the largest power of 10, at most 9 and at most
        /// what keeps the scale within 28, that <paramref name="quotient"/>
        /// can be scaled up by and still fit in 96 bits. Like .NET, it throws
        /// <c>NeoOverflowError</c> when that power is below 9 and can't bring
        /// a negative <paramref name="scale"/> to 0.
        /// </summary>
        private static int SearchScale(BigInteger quotient, int scale)
        {
            int curScale = scale > MaxScale - MaxInt32Scale ? MaxScale - scale : MaxInt32Scale;
            while (curScale > 0 && quotient * Pow10(curScale) > MaxCoefficient)
            {
                curScale--;
            }
            if (curScale == MaxInt32Scale)
            {
                return curScale;
            }
            if (curScale + scale < 0)
            {
                throw Overflow();
            }
            return curScale;
        }

        /// <summary>
        /// <c>OverflowUnscale</c>: a quotient that passed 96 bits drops one
        /// digit, rounding half-even with <paramref name="sticky"/> standing
        /// for the remainder below it.
        /// </summary>
        private static BigInteger OverflowUnscale(BigInteger quotient, ref int scale, bool sticky)
        {
            if (--scale < 0)
            {
                throw Overflow();
            }
            BigInteger result = BigInteger.DivRem(quotient, 10, out BigInteger digit);
            if (digit > 5 || (digit == 5 && (sticky || !result.IsEven)))
            {
                result += 1;
            }
            return result;
        }

        /// <summary><c>a % b</c>, as <see cref="decimal"/>'s <c>%</c> (P127 §3.4).</summary>
        public static string Remainder(string a, string b) =>
            FormatParts(RemainderParts(ParseArg(a, "a"), ParseArg(b, "b")));

        /// <summary>
        /// <c>VarDecMod</c>: the exact remainder at the larger scale, with the
        /// dividend's sign, computed without the quotient, so
        /// <c>decimal.MaxValue % 0.1m</c> works. A zero dividend, or one
        /// smaller in magnitude than the divisor, comes back unchanged; equal
        /// magnitudes give zero at the larger scale.
        /// </summary>
        private static DecimalParts RemainderParts(DecimalParts dividend, DecimalParts divisor)
        {
            if (divisor.Coefficient.IsZero)
            {
                throw DivideByZero();
            }
            if (dividend.Coefficient.IsZero)
            {
                return dividend;
            }
            int scale = Math.Max(dividend.Scale, divisor.Scale);
            BigInteger left = BigInteger.Abs(dividend.Coefficient) * Pow10(scale - dividend.Scale);
            BigInteger right = BigInteger.Abs(divisor.Coefficient) * Pow10(scale - divisor.Scale);
            int comparison = left.CompareTo(right);
            if (comparison < 0)
            {
                return dividend;
            }
            BigInteger remainder = comparison == 0 ? BigInteger.Zero : left % right;
            return new DecimalParts(dividend.Negative ? -remainder : remainder, scale, dividend.Negative);
        }

        /// <summary>
        /// Exact minimum (P69 §2.2). The winning argument is returned
        /// verbatim, so its scale survives ("1.10" stays "1.10"). A tie
        /// returns <paramref name="b"/>, as <c>decimal.Min</c> does, since the
        /// two arguments can be equal yet spelled differently.
        /// </summary>
        public static string Min(string a, string b)
        {
            return Compare(a, b) < 0 ? a : b;
        }

        /// <summary>Exact maximum; a tie returns <paramref name="a"/>, as <c>decimal.Max</c> does.</summary>
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
                new DecimalParts(BigInteger.Abs(parts.Coefficient), parts.Scale, negativeZero: false));
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
            return new DecimalParts(quotient, 0, parts.Negative);
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
            return FormatParts(AssertWithinEnvelope(truncated));
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
            return FormatParts(AssertWithinEnvelope(truncated));
        }

        /// <summary>
        /// The integer part, as a canonical scale-0 decimal (P69 §2.5):
        /// toward zero, so "-4.25" → "-4".
        /// </summary>
        public static string Truncate(string value)
        {
            DecimalParts truncated = TruncateParts(ParseArg(value, "value"), out _);
            return FormatParts(AssertWithinEnvelope(truncated));
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
        /// Rounds coefficient * 10^-scale to <paramref name="digits"/>
        /// fractional digits, breaking ties by <paramref name="midpoint"/>.
        /// When digits &gt;= scale the value is already exact and returned
        /// unchanged (no zero-padding).
        /// </summary>
        internal static DecimalParts RoundParts(DecimalParts parts, int digits, MidpointRounding midpoint)
        {
            if (digits >= parts.Scale)
            {
                return parts;
            }
            BigInteger divisor = Pow10(parts.Scale - digits);
            bool negative = parts.Coefficient.Sign < 0;
            BigInteger magnitude = BigInteger.Abs(parts.Coefficient);
            BigInteger quotient = magnitude / divisor;
            BigInteger doubled = magnitude % divisor * 2;
            bool tieRoundsUp = midpoint == MidpointRounding.AwayFromZero || !(quotient % 2).IsZero;
            if (doubled > divisor || (doubled == divisor && tieRoundsUp))
            {
                quotient += 1;
            }
            return new DecimalParts(negative ? -quotient : quotient, digits, parts.Negative);
        }

        /// <summary>Round-half-even to <paramref name="digits"/> (0..28). Never pads scale upward.</summary>
        public static string Round(string value, int digits)
        {
            AssertDigitsInRange(digits, "round");
            DecimalParts rounded = RoundParts(ParseArg(value, "value"), digits, MidpointRounding.ToEven);
            return FormatParts(AssertWithinEnvelope(rounded));
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
                throw new NeoRuntimeError(
                    NeoErrorClass.NeoDivideByZeroError,
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
            bool negative = dividend.Negative != divisor.Negative;
            BigInteger absNumerator = BigInteger.Abs(numerator);
            BigInteger absDenominator = BigInteger.Abs(denominator);
            BigInteger quotient = absNumerator / absDenominator;
            BigInteger remainder = absNumerator % absDenominator;
            BigInteger doubled = remainder * 2;
            if (doubled > absDenominator || (doubled == absDenominator && !(quotient % 2).IsZero))
            {
                quotient += 1;
            }
            DecimalParts parts = new DecimalParts(negative ? -quotient : quotient, digits, negative);
            return FormatParts(AssertWithinEnvelope(parts));
        }

        /// <summary>
        /// A finite double's EXACT binary expansion (mantissa/exponent bits
        /// through <see cref="BitConverter.DoubleToInt64Bits"/>) as the
        /// shortest exact parts (0.5 → coefficient 5, scale 1). No envelope
        /// applies: the scale reaches 1074 for subnormals. ±0 is plain
        /// (0, 0). Distinct errors for NaN and ±Infinity.
        /// </summary>
        internal static DecimalParts ExactPartsFromFloat(double value)
        {
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
                // ±0 short-circuits to plain zero: carrying the subnormal
                // scale (1074) would emit "0.00000…" padding.
                return new DecimalParts(BigInteger.Zero, 0);
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
            // The raw mantissa-times-5^k form carries trailing zeros for
            // dyadic values (0.5 → coefficient 5×10^52, scale 53); the
            // cross-runtime contract is the SHORTEST exact form, "0.5", not
            // "0.500".
            while (parts.Scale > 0 && (parts.Coefficient % 10).IsZero)
            {
                parts = new DecimalParts(parts.Coefficient / 10, parts.Scale - 1);
            }
            return parts;
        }

        /// <summary>
        /// Float64 → canonical decimal string via the double's exact binary
        /// expansion, rounded half-even to <paramref name="digits"/>. Never
        /// consults <c>double.ToString</c>, so the result is identical across
        /// runtimes (Unity's Mono predates .NET Core's shortest-round-trip
        /// formatting).
        /// </summary>
        public static string FromFloat(double value, int digits)
        {
            AssertDigitsInRange(digits, "float conversion");
            DecimalParts rounded = RoundParts(ExactPartsFromFloat(value), digits, MidpointRounding.ToEven);
            return FormatParts(AssertWithinEnvelope(rounded));
        }

        /// <summary>
        /// Canonical decimal string → nearest double. Used only by NeoScript's
        /// explicit <c>ToFloat</c> — never by the editor UI (which edits the
        /// string directly, specs/decimal-member.md decision 8).
        /// </summary>
        public static double ToFloat(string value)
        {
            AssertCanonicalInput(value, "value");
            double result = double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
            // Mono parses "-0" as +0; a decimal -0 converts to -0.0.
            return result == 0 && value[0] == '-' ? -0d : result;
        }
    }
}
