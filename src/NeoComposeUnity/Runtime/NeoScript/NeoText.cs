// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// NeoScript's string library and number text (P115). The C# mirror of
    /// the web's <c>packages/models/src/text/neo-text.ts</c> and
    /// <c>packages/neoscript-language/src/text-formats.ts</c>, locked together
    /// by <c>text-parity-fixture.json</c>. Every member is ordinal and
    /// invariant, counts UTF-16 code units, and never asks the host for
    /// whitespace or number formatting: Mono and .NET disagree on both.
    /// </summary>
    internal static class NeoText
    {
        /// <summary>A number op's primitive, the wire <c>number</c> tag.</summary>
        internal const string Int = "int";
        internal const string Float = "float";
        internal const string Decimal = "decimal";

        /// <summary>The largest format precision, the range every digits argument uses.</summary>
        private const int MaxFormatPrecision = 28;

        private static readonly Regex IntGrammar = new Regex(
            @"^[+-]?[0-9]+\z",
            RegexOptions.CultureInvariant);

        private static readonly Regex FloatGrammar = new Regex(
            @"^[+-]?([0-9]+(\.[0-9]*)?|\.[0-9]+)([eE][+-]?[0-9]+)?\z",
            RegexOptions.CultureInvariant);

        private static readonly Regex DecimalGrammar = new Regex(
            @"^([+-]?)([0-9]*)(?:\.([0-9]*))?\z",
            RegexOptions.CultureInvariant);

        /// <summary>C#'s <c>char.IsWhiteSpace</c> set, written out so no runtime asks its host.</summary>
        internal static bool IsWhiteSpace(char code)
        {
            if (code >= 0x09 && code <= 0x0d)
                return true;
            if (code >= 0x2000 && code <= 0x200a)
                return true;
            switch ((int)code)
            {
                case 0x20:
                case 0x85:
                case 0xa0:
                case 0x1680:
                case 0x2028:
                case 0x2029:
                case 0x202f:
                case 0x205f:
                case 0x3000:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary><c>NumberStyles</c> leading/trailing whitespace: what the parsers skip.</summary>
        private static bool IsNumberWhiteSpace(char code)
        {
            return code == 0x20 || (code >= 0x09 && code <= 0x0d);
        }

        private static int FirstKept(string value, Func<char, bool> skip)
        {
            int start = 0;
            while (start < value.Length && skip(value[start]))
                start++;
            return start;
        }

        private static int LastKeptEnd(string value, int start, Func<char, bool> skip)
        {
            int end = value.Length;
            while (end > start && skip(value[end - 1]))
                end--;
            return end;
        }

        internal static string Trim(string value)
        {
            int start = FirstKept(value, IsWhiteSpace);
            return value.Substring(start, LastKeptEnd(value, start, IsWhiteSpace) - start);
        }

        internal static string TrimStart(string value)
        {
            return value.Substring(FirstKept(value, IsWhiteSpace));
        }

        internal static string TrimEnd(string value)
        {
            return value.Substring(0, LastKeptEnd(value, 0, IsWhiteSpace));
        }

        internal static bool IsNullOrWhiteSpace(string? value)
        {
            if (value is null)
                return true;
            return FirstKept(value, IsWhiteSpace) == value.Length;
        }

        /// <summary><paramref name="length"/> null means "to the end".</summary>
        internal static string Substring(string value, int startIndex, int? length)
        {
            if (startIndex < 0)
                throw new NSGetterRuntimeError("string.Substring startIndex must not be negative.", NeoErrorContract.NeoArgumentError);
            if (startIndex > value.Length)
                throw new NSGetterRuntimeError("string.Substring startIndex must not exceed the string length.", NeoErrorContract.NeoArgumentError);
            if (length is not int count)
                return value.Substring(startIndex);
            if (count < 0)
                throw new NSGetterRuntimeError("string.Substring length must not be negative.", NeoErrorContract.NeoArgumentError);
            if ((long)startIndex + count > value.Length)
            {
                throw new NSGetterRuntimeError(
                    "string.Substring startIndex plus length must not exceed the string length.", NeoErrorContract.NeoArgumentError);
            }
            return value.Substring(startIndex, count);
        }

        internal static int IndexOf(string value, string search)
        {
            return value.IndexOf(search, StringComparison.Ordinal);
        }

        /// <summary>An empty search matches at the end, as on the web (older Mono answers Length - 1).</summary>
        internal static int LastIndexOf(string value, string search)
        {
            if (search.Length == 0)
                return value.Length;
            return value.LastIndexOf(search, StringComparison.Ordinal);
        }

        /// <summary>Ordinal split that keeps empty entries; an empty separator never splits.</summary>
        internal static object?[] Split(string value, string separator)
        {
            if (separator.Length == 0)
                return new object?[] { value };
            string[] parts = value.Split(new[] { separator }, StringSplitOptions.None);
            var entries = new object?[parts.Length];
            Array.Copy(parts, entries, parts.Length);
            return entries;
        }

        /// <summary><paramref name="side"/> is <c>PadLeft</c> or <c>PadRight</c>.</summary>
        internal static string Pad(string side, string value, int totalWidth, string paddingChar)
        {
            if (totalWidth < 0)
                throw new NSGetterRuntimeError($"string.{side} totalWidth must not be negative.", NeoErrorContract.NeoArgumentError);
            if (paddingChar.Length != 1)
                throw new NSGetterRuntimeError($"string.{side} paddingChar must be exactly one character.", NeoErrorContract.NeoArgumentError);
            return side == "PadLeft"
                ? value.PadLeft(totalWidth, paddingChar[0])
                : value.PadRight(totalWidth, paddingChar[0]);
        }

        /// <summary>Null entries join as "", matching interpolation.</summary>
        internal static string Join(string separator, string?[] entries)
        {
            var result = new StringBuilder();
            for (int index = 0; index < entries.Length; index++)
            {
                if (index > 0)
                    result.Append(separator);
                result.Append(entries[index]);
            }
            return result.ToString();
        }

        /// <summary>
        /// Parses a <c>ToString</c> format, or throws the first §3.4 failure.
        /// Validation order is fixed so both runtimes report the same message
        /// for an input that fails more than one check.
        /// </summary>
        private static void ReadFormat(string number, string format, out char specifier, out int precision)
        {
            if (format.Length == 0)
                throw new NSGetterRuntimeError($"{number}.ToString format must not be empty.", NeoErrorContract.NeoArgumentError);
            specifier = FormatSpecifier(format[0]);
            if (specifier == '\0' || (specifier == 'D' && number != Int))
            {
                throw new NSGetterRuntimeError(number == Int
                    ? "int.ToString format must start with D, F, or N."
                    : $"{number}.ToString format must start with F or N.", NeoErrorContract.NeoArgumentError);
            }
            for (int index = 1; index < format.Length; index++)
            {
                if (format[index] < '0' || format[index] > '9')
                {
                    throw new NSGetterRuntimeError(
                        $"{number}.ToString format precision must contain only digits.", NeoErrorContract.NeoArgumentError);
                }
            }
            if (format.Length == 1)
            {
                precision = specifier == 'D' ? 1 : 2;
                return;
            }
            // Leading zeros are fine; any value past 28 fails, however long.
            string digits = format.Substring(1).TrimStart('0');
            precision = digits.Length > 2 ? int.MaxValue : int.Parse("0" + digits, CultureInfo.InvariantCulture);
            if (precision > MaxFormatPrecision)
            {
                throw new NSGetterRuntimeError(
                    $"{number}.ToString format precision must be 0 to {MaxFormatPrecision}.", NeoErrorContract.NeoArgumentError);
            }
        }

        /// <returns>The canonical specifier, or <c>'\0'</c> for an unknown letter.</returns>
        private static char FormatSpecifier(char letter)
        {
            switch (letter)
            {
                case 'D':
                case 'd':
                    return 'D';
                case 'F':
                case 'f':
                    return 'F';
                case 'N':
                case 'n':
                    return 'N';
                default:
                    return '\0';
            }
        }

        private static string GroupThousands(string digits)
        {
            int head = digits.Length % 3 == 0 ? 3 : digits.Length % 3;
            var result = new StringBuilder(digits, 0, head, digits.Length + digits.Length / 3);
            for (int index = head; index < digits.Length; index += 3)
            {
                result.Append(',');
                result.Append(digits, index, 3);
            }
            return result.ToString();
        }

        private static NeoDecimalMath.DecimalParts ExactParts(string number, object value)
        {
            if (number == Decimal)
                return NeoDecimalMath.ParseArg((string)value, "value");
            if (number == Int)
                return new NeoDecimalMath.DecimalParts(new BigInteger((double)value), 0);
            return NeoDecimalMath.ExactPartsFromFloat((double)value);
        }

        /// <summary>
        /// <c>&lt;number&gt;.ToString(format)</c>: <c>int</c> and <c>float</c>
        /// values are doubles, <c>decimal</c> values canonical strings. Digits
        /// come from the exact expansion, never from a host number formatter.
        /// </summary>
        internal static string Format(string number, object value, string format)
        {
            ReadFormat(number, format, out char specifier, out int precision);
            if (value is double floating && (double.IsNaN(floating) || double.IsInfinity(floating)))
            {
                if (double.IsNaN(floating))
                    return "NaN";
                return floating > 0 ? "Infinity" : "-Infinity";
            }
            MidpointRounding midpoint = number == Float
                ? MidpointRounding.ToEven
                : MidpointRounding.AwayFromZero;
            NeoDecimalMath.DecimalParts rounded = specifier == 'D'
                ? ExactParts(number, value)
                : NeoDecimalMath.RoundParts(ExactParts(number, value), precision, midpoint);
            bool negative = rounded.Coefficient.Sign < 0;
            BigInteger magnitude = BigInteger.Abs(rounded.Coefficient);
            string sign = negative ? "-" : "";
            if (specifier == 'D')
                return sign + magnitude.ToString(CultureInfo.InvariantCulture).PadLeft(precision, '0');
            // Pad the scale up to the precision, then split at the decimal point.
            string digits = (magnitude * BigInteger.Pow(10, precision - rounded.Scale))
                .ToString(CultureInfo.InvariantCulture)
                .PadLeft(precision + 1, '0');
            int pointIndex = digits.Length - precision;
            string integral = digits.Substring(0, pointIndex);
            string head = specifier == 'N' ? GroupThousands(integral) : integral;
            string tail = precision == 0 ? "" : "." + digits.Substring(pointIndex);
            return sign + head + tail;
        }

        private static string TrimNumberWhiteSpace(string text)
        {
            int start = FirstKept(text, IsNumberWhiteSpace);
            return text.Substring(start, LastKeptEnd(text, start, IsNumberWhiteSpace) - start);
        }

        /// <returns>The parsed value, or null with the §4.4 refusal in <paramref name="error"/>.</returns>
        private static object? ParseInt(string text, out string? error)
        {
            error = null;
            if (!IntGrammar.IsMatch(text))
            {
                error = "int.Parse input is not an integer.";
                return null;
            }
            // Leading zeros are fine, so range-check the exact value, not the length.
            BigInteger value = BigInteger.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            if (value < int.MinValue || value > int.MaxValue)
            {
                error = "int.Parse input is outside the int range.";
                return null;
            }
            return NSGetterEvaluator.Box((double)(int)value);
        }

        private static object? ParseFloat(string text, out string? error)
        {
            error = null;
            if (!FloatGrammar.IsMatch(text))
            {
                error = "float.Parse input is not a number.";
                return null;
            }
            // The web's Math.fround(Number(text)): round to double, then to
            // float. Older Mono throws on overflow where .NET returns Infinity.
            float value;
            try
            {
                value = (float)double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                value = float.PositiveInfinity;
            }
            if (float.IsInfinity(value))
            {
                error = "float.Parse input is outside the float range.";
                return null;
            }
            // `-0` and negative underflow parse to positive zero.
            return NSGetterEvaluator.Box(value == 0 ? 0d : value);
        }

        private static object? ParseDecimal(string text, out string? error)
        {
            error = null;
            Match match = DecimalGrammar.Match(text);
            string integral = match.Success ? match.Groups[2].Value : "";
            string fraction = match.Success ? match.Groups[3].Value : "";
            if (!match.Success || integral.Length + fraction.Length == 0)
            {
                error = "decimal.Parse input is not a decimal number.";
                return null;
            }
            BigInteger magnitude = BigInteger.Parse(integral + fraction, CultureInfo.InvariantCulture);
            if (magnitude.ToString(CultureInfo.InvariantCulture).Length > NeoDecimalValues.MaxSignificantDigits)
            {
                error = $"decimal.Parse input has more than {NeoDecimalValues.MaxSignificantDigits} significant digits.";
                return null;
            }
            if (fraction.Length > NeoDecimalValues.MaxScale)
            {
                error = $"decimal.Parse input has more than {NeoDecimalValues.MaxScale} fractional digits.";
                return null;
            }
            bool negative = match.Groups[1].Value == "-";
            return NeoDecimalMath.FormatParts(new NeoDecimalMath.DecimalParts(negative ? -magnitude : magnitude, fraction.Length, negative));
        }

        private static object? ParseText(string number, string text, out string? error)
        {
            string trimmed = TrimNumberWhiteSpace(text);
            switch (number)
            {
                case Int:
                    return ParseInt(trimmed, out error);
                case Float:
                    return ParseFloat(trimmed, out error);
                case Decimal:
                    return ParseDecimal(trimmed, out error);
                default:
                    throw new NSGetterRuntimeError($"Unknown text number '{number}'.", NeoErrorContract.NeoArgumentError);
            }
        }

        /// <summary><c>&lt;number&gt;.Parse(s)</c>: <c>int</c>/<c>float</c> as doubles, <c>decimal</c> canonical.</summary>
        internal static object Parse(string number, string text)
        {
            object? value = ParseText(number, text, out string? error);
            if (error is not null)
                throw new NSGetterRuntimeError(error, NeoErrorContract.NeoArgumentError);
            return value!;
        }

        /// <summary><c>&lt;number&gt;.TryParse(s)</c>: null for every input <see cref="Parse"/> would refuse.</summary>
        internal static object? TryParse(string number, string text)
        {
            return ParseText(number, text, out _);
        }
    }
}
