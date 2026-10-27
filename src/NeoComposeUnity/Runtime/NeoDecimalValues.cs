// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Numerics;
using System.Text.RegularExpressions;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Boundary conversion funnel between a canonical decimal string (the
    /// storage/wire representation, specs/decimal-member.md decision 5)
    /// and the native <see cref="decimal"/> the generated SDK surface
    /// exposes. Every string ↔ <see cref="decimal"/> conversion at an SDK
    /// public boundary flows through here so the invariant-culture rule and
    /// the canonical pre-check live in exactly one place — never inline
    /// <c>Parse</c>/<c>ToString</c> calls sprinkled through generated code.
    ///
    /// <para>The canonical pattern is
    /// <c>^-?(0|[1-9][0-9]*)(\.[0-9]+)?$</c> within <see cref="decimal"/>'s
    /// envelope (P127 §3.4): a coefficient (every digit, leading zeros
    /// stripped) below 2⁹⁶ and a scale of at most 28 — the same contract the
    /// web guard <c>isDecimalString</c> enforces. A zero keeps its sign
    /// (<c>-0</c>, <c>-0.00</c>). Both directions build and read the
    /// decimal's bits directly rather than calling the CLR's parser or
    /// formatter, so the conversion is exact and Mono matches .NET.</para>
    /// </summary>
    public static class NeoDecimalValues
    {
        /// <summary>
        /// The significant digits every coefficient of that many digits fits
        /// in. <c>decimal.Parse</c> still caps at it until P127 §11.5's
        /// rounding lands; storage and arithmetic accept the full envelope
        /// (<see cref="MaxCoefficient"/>).
        /// </summary>
        internal const int MaxSignificantDigits = 28;

        /// <summary>The largest coefficient, 2⁹⁶ − 1, as canonical digits.</summary>
        internal const string MaxCoefficient = "79228162514264337593543950335";

        /// <summary>Scale cap — C# decimal's fractional-digit envelope.</summary>
        internal const int MaxScale = 28;

        /// <summary>Canonical decimal string pattern (mirrors the web guard).</summary>
        internal const string CanonicalPattern = @"^-?(0|[1-9][0-9]*)(\.[0-9]+)?$";

        private static readonly Regex CanonicalRegex = new Regex(
            CanonicalPattern,
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Why a string is not a canonical decimal string. Each value maps to
        /// a distinct, single-condition failure so callers throw pinpointable
        /// errors (specs/decimal-member.md decision 2).
        /// </summary>
        internal enum Violation
        {
            None,
            NonCanonical,
            TooManySignificantDigits,
            ScaleTooLarge,
        }

        /// <summary>
        /// Classifies a non-null string against the canonical decimal
        /// contract. Shared by <see cref="Parse"/> and
        /// <see cref="NeoDecimalMath"/> so both apply the identical
        /// regex + digit-count rules.
        /// </summary>
        internal static Violation GetViolation(string value)
        {
            if (!CanonicalRegex.IsMatch(value))
            {
                return Violation.NonCanonical;
            }
            // Coefficient = every digit with the sign and point removed and
            // leading zeros stripped (mirrors the web's
            // value.replace("-","").replace(".","").replace(/^0+/,"")).
            string digits = value.Replace("-", string.Empty).Replace(".", string.Empty).TrimStart('0');
            if (digits.Length > MaxCoefficient.Length
                || digits.Length == MaxCoefficient.Length && string.CompareOrdinal(digits, MaxCoefficient) > 0)
            {
                return Violation.TooManySignificantDigits;
            }
            int pointIndex = value.IndexOf('.');
            int scale = pointIndex < 0 ? 0 : value.Length - pointIndex - 1;
            if (scale > MaxScale)
            {
                return Violation.ScaleTooLarge;
            }
            return Violation.None;
        }

        /// <summary>
        /// Parses a canonical decimal string to a native <see cref="decimal"/>.
        /// Four distinct single-condition failures: null input, non-canonical
        /// format, too many significant digits, scale too large — each with a
        /// message naming the offending text.
        /// </summary>
        public static decimal Parse(string value)
        {
            if (value is null)
            {
                throw new ArgumentNullException(
                    nameof(value),
                    "Cannot parse a null decimal string.");
            }
            switch (GetViolation(value))
            {
                case Violation.NonCanonical:
                    throw new FormatException(
                        $"Decimal string \"{value}\" must be a canonical decimal string matching " +
                        $"{CanonicalPattern} (no exponent, no leading zeros).");
                case Violation.TooManySignificantDigits:
                    throw new OverflowException(
                        $"Decimal string \"{value}\" has more significant digits than a decimal holds: its coefficient must be at most {MaxCoefficient}.");
                case Violation.ScaleTooLarge:
                    throw new OverflowException(
                        $"Decimal string \"{value}\" must have at most {MaxScale} fractional digits.");
            }
            bool negative = value[0] == '-';
            int pointIndex = value.IndexOf('.');
            int scale = pointIndex < 0 ? 0 : value.Length - pointIndex - 1;
            // The coefficient fits in 96 bits, so three 32-bit words hold it.
            uint lo = 0;
            uint mid = 0;
            uint hi = 0;
            for (int index = negative ? 1 : 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character == '.')
                    continue;
                ulong carry = (uint)(character - '0');
                carry += (ulong)lo * 10;
                lo = (uint)carry;
                carry = (carry >> 32) + (ulong)mid * 10;
                mid = (uint)carry;
                carry = (carry >> 32) + (ulong)hi * 10;
                hi = (uint)carry;
            }
            return Compose(lo, mid, hi, scale, negative);
        }

        /// <summary>
        /// The decimal with this 96-bit coefficient, scale and sign, a zero's
        /// sign included. Every decimal the SDK builds from parts comes from here.
        /// </summary>
        internal static decimal Compose(uint lo, uint mid, uint hi, int scale, bool negative) =>
            new(unchecked((int)lo), unchecked((int)mid), unchecked((int)hi), negative, (byte)scale);

        /// <inheritdoc cref="Compose(uint, uint, uint, int, bool)"/>
        internal static decimal Compose(BigInteger coefficient, int scale, bool negative) => Compose(
            (uint)(coefficient & uint.MaxValue),
            (uint)((coefficient >> 32) & uint.MaxValue),
            (uint)(coefficient >> 64),
            scale,
            negative);

        /// <summary>Null in → null out; otherwise <see cref="Parse"/>.</summary>
        public static decimal? ParseOrNull(string? value)
        {
            return value is null ? (decimal?)null : Parse(value);
        }

        /// <summary>
        /// Formats a <see cref="decimal"/> to its canonical decimal string,
        /// keeping its scale (<c>1.10m</c> formats "1.10") and a zero's sign
        /// (P127 §3.4: <c>-0.00m</c> formats "-0.00"). Reads the decimal's
        /// bits, because .NET Core and Mono format a negative zero
        /// differently.
        /// </summary>
        public static string Format(decimal value)
        {
            int[] bits = decimal.GetBits(value);
            uint lo = (uint)bits[0];
            uint mid = (uint)bits[1];
            uint hi = (uint)bits[2];
            bool negative = bits[3] < 0;
            int scale = (bits[3] >> 16) & 0xFF;
            // At most 29 coefficient digits, written least significant first.
            var digits = new char[MaxCoefficient.Length];
            int count = 0;
            while (lo != 0 || mid != 0 || hi != 0)
            {
                ulong remainder = hi;
                hi = (uint)(remainder / 10);
                remainder = ((remainder % 10) << 32) | mid;
                mid = (uint)(remainder / 10);
                remainder = ((remainder % 10) << 32) | lo;
                lo = (uint)(remainder / 10);
                digits[count++] = (char)('0' + (int)(remainder % 10));
            }
            var builder = new System.Text.StringBuilder(count + scale + 3);
            if (negative)
                builder.Append('-');
            int integerDigits = count - scale;
            if (integerDigits <= 0)
            {
                builder.Append('0');
            }
            else
            {
                for (int index = count - 1; index >= scale; index--)
                    builder.Append(digits[index]);
            }
            if (scale > 0)
            {
                builder.Append('.');
                for (int index = scale - 1; index >= 0; index--)
                    builder.Append(index < count ? digits[index] : '0');
            }
            return builder.ToString();
        }

        /// <summary>Null in → null out; otherwise <see cref="Format"/>.</summary>
        public static string? FormatOrNull(decimal? value)
        {
            return value.HasValue ? Format(value.Value) : null;
        }
    }
}
