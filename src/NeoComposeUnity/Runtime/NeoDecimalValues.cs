// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Boundary conversion funnel between a canonical decimal string (the
    /// storage/wire representation, specs/decimal-attribute.md decision 5)
    /// and the native <see cref="decimal"/> the generated SDK surface
    /// exposes. Every string ↔ <see cref="decimal"/> conversion at an SDK
    /// public boundary flows through here so the invariant-culture rule and
    /// the canonical pre-check live in exactly one place — never inline
    /// <c>Parse</c>/<c>ToString</c> calls sprinkled through generated code.
    ///
    /// <para>The canonical pattern is
    /// <c>^-?(0|[1-9][0-9]*)(\.[0-9]+)?$</c> with at most 28 significant
    /// digits (coefficient, leading zeros stripped) and scale 28 — the same
    /// contract the web guard <c>isDecimalString</c> enforces. Pre-checking
    /// before <see cref="decimal.Parse(string, NumberStyles, IFormatProvider)"/>
    /// makes the parse infallible by construction and keeps the SDK from
    /// being more lenient than the server (<see cref="NumberStyles.Number"/>
    /// alone would tolerate whitespace/thousands the server rejects).</para>
    /// </summary>
    public static class NeoDecimalValues
    {
        /// <summary>Coefficient cap — C# decimal holds 28 safe significant digits.</summary>
        internal const int MaxSignificantDigits = 28;

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
        /// errors (specs/decimal-attribute.md decision 2).
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
            if (digits.Length > MaxSignificantDigits)
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
                        $"Decimal string \"{value}\" must have at most {MaxSignificantDigits} significant digits.");
                case Violation.ScaleTooLarge:
                    throw new OverflowException(
                        $"Decimal string \"{value}\" must have at most {MaxScale} fractional digits.");
            }
            return decimal.Parse(value, NumberStyles.Number, CultureInfo.InvariantCulture);
        }

        /// <summary>Null in → null out; otherwise <see cref="Parse"/>.</summary>
        public static decimal? ParseOrNull(string? value)
        {
            return value is null ? (decimal?)null : Parse(value);
        }

        /// <summary>
        /// Formats a <see cref="decimal"/> to its canonical decimal string.
        /// Uses <see cref="CultureInfo.InvariantCulture"/> and the "G" format
        /// (decimal "G" never emits exponents or leading zeros and preserves
        /// scale — <c>1.10m</c> formats "1.10"). A negative zero (which
        /// .NET Core and Mono format differently) is normalized so the sign
        /// is dropped, guaranteeing the output satisfies the canonical
        /// pattern and "-0" is never produced (decision 1).
        /// </summary>
        public static string Format(decimal value)
        {
            string formatted = value.ToString("G", CultureInfo.InvariantCulture);
            if (value == 0m && formatted.Length > 0 && formatted[0] == '-')
            {
                formatted = formatted.Substring(1);
            }
            return formatted;
        }

        /// <summary>Null in → null out; otherwise <see cref="Format"/>.</summary>
        public static string? FormatOrNull(decimal? value)
        {
            return value.HasValue ? Format(value.Value) : null;
        }
    }
}
