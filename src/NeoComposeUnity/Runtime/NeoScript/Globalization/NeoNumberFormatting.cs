// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NeoCompose.Runtime.NeoScript.Globalization
{
    /// <summary>
    /// .NET 10's <c>Number.Formatting</c> (standard and custom numeric format
    /// strings, P127 §11.4) for <c>int</c>, <c>float</c> and <c>decimal</c>.
    /// It never calls the CLR's number formatting, whose output differs
    /// between Mono and .NET 10.
    /// </summary>
    internal static class NeoNumberFormatting
    {
        private const int SingleMaxRoundTripDigits = 9;
        private const int SingleMaxPrecisionCustomFormat = 7;

        private static readonly string[] positiveCurrencyFormats = { "$#", "#$", "$ #", "# $" };

        private static readonly string[] negativeCurrencyFormats =
        {
            "($#)",
            "-$#",
            "$-#",
            "$#-",
            "(#$)",
            "-#$",
            "#-$",
            "#$-",
            "-# $",
            "-$ #",
            "# $-",
            "$ #-",
            "$ -#",
            "#- $",
            "($ #)",
            "(# $)",
            "$- #",
        };

        private static readonly string[] positivePercentFormats = { "# %", "#%", "%#", "% #" };

        private static readonly string[] negativePercentFormats =
        {
            "-# %",
            "-#%",
            "-%#",
            "%-#",
            "%#-",
            "#-%",
            "#%-",
            "-% #",
            "# %-",
            "% #-",
            "% -#",
            "#- %",
        };

        private static readonly string[] negativeNumberFormats = { "(#)", "-#", "- #", "#-", "# -" };

        /// <summary>An int's decimal digits with a leading <c>-</c>, written without the CLR.</summary>
        internal static string Int32Text(int value)
        {
            if (value >= 0)
                return Digits((uint)value);
            return "-" + Digits((uint)-(long)value);
        }

        /// <summary><c>int.ToString(format, provider)</c>.</summary>
        internal static string FormatInt32(int value, string? format, object? provider)
        {
            uint magnitude = value < 0 ? (uint)-(long)value : (uint)value;
            if (string.IsNullOrEmpty(format))
            {
                return value >= 0
                    ? Digits(magnitude)
                    : NeoNumberFormatInfos.GetInstance(provider).NegativeSign + Digits(magnitude);
            }
            ParseFormatSpecifier(format!, out int fmt, out int digits);
            int fmtUpper = fmt & 0xffdf;
            if (fmtUpper == 'G' ? digits < 1 : fmtUpper == 'D')
            {
                string text = Digits(magnitude).PadLeft(Math.Max(digits, 0), '0');
                return value >= 0 ? text : NeoNumberFormatInfos.GetInstance(provider).NegativeSign + text;
            }
            if (fmtUpper == 'X')
            {
                string hex = Radix((uint)value, 4, fmt == 'X' ? "0123456789ABCDEF" : "0123456789abcdef");
                return hex.PadLeft(Math.Max(digits, 0), '0');
            }
            if (fmtUpper == 'B')
                return Radix((uint)value, 1, "01").PadLeft(Math.Max(digits, 0), '0');

            NumberFormatInfo info = NeoNumberFormatInfos.GetInstance(provider);
            string coefficient = value == 0 ? "" : Digits(magnitude);
            var number = new NeoNumberBuffer
            {
                Kind = NeoNumberBufferKind.Integer,
                Digits = coefficient,
                Scale = coefficient.Length,
                IsNegative = value < 0,
            };
            return FormatNumber(number, format!, digits, fmt, info);
        }

        /// <summary><c>float.ToString(format, provider)</c>.</summary>
        internal static string FormatSingle(float value, string? format, object? provider)
        {
            NumberFormatInfo info = NeoNumberFormatInfos.GetInstance(provider);
            if (float.IsNaN(value))
                return info.NaNSymbol;
            if (float.IsPositiveInfinity(value))
                return info.PositiveInfinitySymbol;
            if (float.IsNegativeInfinity(value))
                return info.NegativeInfinitySymbol;

            ParseFormatSpecifier(format ?? "", out int fmt, out int specifierDigits);
            int precision = fmt == 0 ? SingleMaxPrecisionCustomFormat : specifierDigits;
            int nMaxDigitsRequested = precision;
            bool isSignificantDigits = true;
            switch (fmt | 0x20)
            {
                case 0x20:
                    break;
                case 'c':
                    if (precision == -1)
                        precision = info.CurrencyDecimalDigits;
                    isSignificantDigits = false;
                    break;
                case 'e':
                    if (precision == -1)
                        precision = 6;
                    precision++;
                    break;
                case 'f':
                case 'n':
                    if (precision == -1)
                        precision = info.NumberDecimalDigits;
                    isSignificantDigits = false;
                    break;
                case 'g':
                    if (precision == 0)
                        precision = -1;
                    break;
                case 'p':
                    if (precision == -1)
                        precision = info.PercentDecimalDigits;
                    precision += 2;
                    isSignificantDigits = false;
                    break;
                case 'r':
                    precision = -1;
                    break;
                default:
                    throw NeoRuntimeError.BadFormatSpecifier();
            }

            var number = new NeoNumberBuffer
            {
                Kind = NeoNumberBufferKind.FloatingPoint,
                IsNegative = BitConverter.SingleToInt32Bits(value) < 0,
            };
            if (value != 0)
                NeoDragon4.Run(Math.Abs(value), precision, isSignificantDigits, number);

            if (fmt == 0)
                return NumberToStringFormat(number, format ?? "", info);
            int nMaxDigits = precision == -1
                ? Math.Max(number.Digits.Length, SingleMaxRoundTripDigits)
                : nMaxDigitsRequested;
            return NumberToString(number, fmt, nMaxDigits, info);
        }

        /// <summary><c>decimal.ToString(format, provider)</c>, keeping the scale and a zero's sign.</summary>
        internal static string FormatDecimal(decimal value, string? format, object? provider)
        {
            NumberFormatInfo info = NeoNumberFormatInfos.GetInstance(provider);
            ParseFormatSpecifier(format ?? "", out int fmt, out int digits);
            string text = NeoDecimalValues.Format(value);
            bool negative = text[0] == '-';
            int point = text.IndexOf('.');
            int integerStart = negative ? 1 : 0;
            int integerEnd = point < 0 ? text.Length : point;
            int scale = point < 0 ? 0 : text.Length - point - 1;
            string coefficient = point < 0
                ? text.Substring(integerStart)
                : text.Substring(integerStart, integerEnd - integerStart) + text.Substring(point + 1);
            int start = 0;
            while (start < coefficient.Length && coefficient[start] == '0')
                start++;
            if (start != 0)
                coefficient = coefficient.Substring(start);

            var number = new NeoNumberBuffer
            {
                Kind = NeoNumberBufferKind.Decimal,
                Digits = coefficient,
                Scale = coefficient.Length - scale,
                IsNegative = negative,
            };
            return FormatNumber(number, format ?? "", digits, fmt, info);
        }

        private static string Digits(uint value)
        {
            if (value == 0)
                return "0";
            Span<char> buffer = stackalloc char[10];
            int index = buffer.Length;
            while (value != 0)
            {
                buffer[--index] = (char)('0' + (int)(value % 10));
                value /= 10;
            }
            return buffer.Slice(index).ToString();
        }

        private static string Radix(uint value, int bitsPerDigit, string alphabet)
        {
            if (value == 0)
                return "0";
            Span<char> buffer = stackalloc char[32];
            int index = buffer.Length;
            uint mask = (1u << bitsPerDigit) - 1;
            while (value != 0)
            {
                buffer[--index] = alphabet[(int)(value & mask)];
                value >>= bitsPerDigit;
            }
            return buffer.Slice(index).ToString();
        }

        private static bool IsAsciiLetter(int c) => (c | 0x20) >= 'a' && (c | 0x20) <= 'z';

        /// <summary>
        /// The standard specifier's letter code, or 0 for a custom format, and
        /// its precision, or -1.
        /// </summary>
        private static void ParseFormatSpecifier(string format, out int symbol, out int digits)
        {
            int c = format.Length > 0 ? format[0] : 0;
            if (IsAsciiLetter(c))
            {
                int n = 0;
                int index = 1;
                while (index < format.Length)
                {
                    int d = format[index];
                    if (d < '0' || d > '9')
                        break;
                    if (n >= 100_000_000)
                        throw NeoRuntimeError.BadFormatSpecifier();
                    n = n * 10 + d - '0';
                    index++;
                }
                if (index >= format.Length || format[index] == 0)
                {
                    symbol = c;
                    digits = format.Length == 1 ? -1 : n;
                    return;
                }
            }
            // A leading NUL ends the specifier, as an empty one does.
            symbol = format.Length == 0 || c == 0 ? 'G' : 0;
            digits = -1;
        }

        private static char DigitAt(NeoNumberBuffer n, int index) => index < n.Digits.Length ? n.Digits[index] : '0';

        private static void RoundNumber(NeoNumberBuffer n, int pos, bool isCorrectlyRounded)
        {
            string digits = n.Digits;
            int index = pos <= 0 ? 0 : Math.Min(pos, digits.Length);
            bool roundUp = index == pos
                && index < digits.Length
                && !isCorrectlyRounded
                && digits[index] >= '5';
            if (roundUp)
            {
                while (index > 0 && digits[index - 1] == '9')
                    index--;
                if (index > 0)
                {
                    n.Digits = digits.Substring(0, index - 1) + (char)(digits[index - 1] + 1);
                }
                else
                {
                    n.Scale++;
                    n.Digits = "1";
                    index = 1;
                }
            }
            else
            {
                while (index > 0 && digits[index - 1] == '0')
                    index--;
                n.Digits = index == digits.Length ? digits : digits.Substring(0, index);
            }
            if (index == 0)
            {
                if (n.Kind != NeoNumberBufferKind.FloatingPoint)
                    n.IsNegative = false;
                n.Scale = 0;
            }
        }

        private static void FormatFixed(
            StringBuilder output,
            NeoNumberBuffer n,
            int nMaxDigits,
            int[]? groupDigits,
            string decimalSeparator,
            string groupSeparator)
        {
            string digits = n.Digits;
            int digPos = n.Scale;
            int d = 0;
            if (digPos > 0)
            {
                int digStart = Math.Min(digPos, digits.Length);
                int groupSize = groupDigits is { Length: > 0 } ? groupDigits[0] : 0;
                if (groupDigits is not null && groupSize > 0)
                {
                    var parts = new List<string>();
                    int groupSizeIndex = 0;
                    int digitCount = 0;
                    for (int index = digPos - 1; index >= 0; index--)
                    {
                        parts.Add(index < digStart ? digits[index].ToString() : "0");
                        if (groupSize > 0)
                        {
                            digitCount++;
                            if (digitCount == groupSize && index != 0)
                            {
                                parts.Add(groupSeparator);
                                if (groupSizeIndex < groupDigits.Length - 1)
                                {
                                    groupSizeIndex++;
                                    groupSize = groupDigits[groupSizeIndex];
                                }
                                digitCount = 0;
                            }
                        }
                    }
                    for (int index = parts.Count - 1; index >= 0; index--)
                        output.Append(parts[index]);
                }
                else
                {
                    output.Append(digits, 0, digStart);
                    output.Append('0', digPos - digStart);
                }
                d = digStart;
            }
            else
            {
                output.Append('0');
            }

            if (nMaxDigits > 0)
            {
                output.Append(decimalSeparator);
                if (digPos < 0)
                {
                    int zeroes = Math.Min(-digPos, nMaxDigits);
                    output.Append('0', zeroes);
                    nMaxDigits -= zeroes;
                }
                int take = Math.Min(nMaxDigits, digits.Length - d);
                output.Append(digits, d, take);
                output.Append('0', nMaxDigits - take);
            }
        }

        private static void FormatExponent(
            StringBuilder output,
            NumberFormatInfo info,
            int value,
            char expChar,
            int minDigits,
            bool positiveSign)
        {
            output.Append(expChar);
            if (value < 0)
                output.Append(info.NegativeSign);
            else if (positiveSign)
                output.Append(info.PositiveSign);
            output.Append(Digits((uint)Math.Abs(value)).PadLeft(minDigits, '0'));
        }

        private static void FormatScientific(StringBuilder output, NeoNumberBuffer n, int nMaxDigits, NumberFormatInfo info, char expChar)
        {
            output.Append(DigitAt(n, 0));
            if (nMaxDigits != 1)
                output.Append(info.NumberDecimalSeparator);
            int take = Math.Max(0, Math.Min(nMaxDigits - 1, n.Digits.Length - 1));
            if (take > 0)
                output.Append(n.Digits, 1, take);
            output.Append('0', Math.Max(0, nMaxDigits - 1 - take));
            int exponent = n.Digits.Length == 0 ? 0 : n.Scale - 1;
            FormatExponent(output, info, exponent, expChar, 3, true);
        }

        private static void FormatGeneral(
            StringBuilder output,
            NeoNumberBuffer n,
            int nMaxDigits,
            NumberFormatInfo info,
            char expChar,
            bool suppressScientific)
        {
            string digits = n.Digits;
            int digPos = n.Scale;
            bool scientific = false;
            if (!suppressScientific && (digPos > nMaxDigits || digPos < -3))
            {
                digPos = 1;
                scientific = true;
            }
            int d = 0;
            if (digPos > 0)
            {
                d = Math.Min(digPos, digits.Length);
                output.Append(digits, 0, d);
                output.Append('0', digPos - d);
                digPos = 0;
            }
            else
            {
                output.Append('0');
            }
            if (d < digits.Length || digPos < 0)
            {
                output.Append(info.NumberDecimalSeparator);
                output.Append('0', -digPos);
                output.Append(digits, d, digits.Length - d);
            }
            if (scientific)
                FormatExponent(output, info, n.Scale - 1, expChar, 2, true);
        }

        private static string FormatPattern(string pattern, string fixedText, NumberFormatInfo info, string symbol)
        {
            var output = new StringBuilder();
            foreach (char ch in pattern)
            {
                if (ch == '#')
                    output.Append(fixedText);
                else if (ch == '-')
                    output.Append(info.NegativeSign);
                else if (ch == '$' || ch == '%')
                    output.Append(symbol);
                else
                    output.Append(ch);
            }
            return output.ToString();
        }

        private static string Fixed(NeoNumberBuffer n, int nMaxDigits, int[]? groupDigits, string decimalSeparator, string groupSeparator)
        {
            var output = new StringBuilder();
            FormatFixed(output, n, nMaxDigits, groupDigits, decimalSeparator, groupSeparator);
            return output.ToString();
        }

        private static string NumberToString(NeoNumberBuffer n, int format, int nMaxDigits, NumberFormatInfo info)
        {
            bool isCorrectlyRounded = n.Kind == NeoNumberBufferKind.FloatingPoint;
            switch (format)
            {
                case 'C':
                case 'c':
                    {
                        if (nMaxDigits < 0)
                            nMaxDigits = info.CurrencyDecimalDigits;
                        RoundNumber(n, n.Scale + nMaxDigits, isCorrectlyRounded);
                        string fixedText = Fixed(n, nMaxDigits, info.CurrencyGroupSizes, info.CurrencyDecimalSeparator, info.CurrencyGroupSeparator);
                        string pattern = n.IsNegative
                            ? negativeCurrencyFormats[info.CurrencyNegativePattern]
                            : positiveCurrencyFormats[info.CurrencyPositivePattern];
                        return FormatPattern(pattern, fixedText, info, info.CurrencySymbol);
                    }
                case 'F':
                case 'f':
                    {
                        if (nMaxDigits < 0)
                            nMaxDigits = info.NumberDecimalDigits;
                        RoundNumber(n, n.Scale + nMaxDigits, isCorrectlyRounded);
                        var output = new StringBuilder();
                        if (n.IsNegative)
                            output.Append(info.NegativeSign);
                        FormatFixed(output, n, nMaxDigits, null, info.NumberDecimalSeparator, "");
                        return output.ToString();
                    }
                case 'N':
                case 'n':
                    {
                        if (nMaxDigits < 0)
                            nMaxDigits = info.NumberDecimalDigits;
                        RoundNumber(n, n.Scale + nMaxDigits, isCorrectlyRounded);
                        string fixedText = Fixed(n, nMaxDigits, info.NumberGroupSizes, info.NumberDecimalSeparator, info.NumberGroupSeparator);
                        string pattern = n.IsNegative ? negativeNumberFormats[info.NumberNegativePattern] : "#";
                        return FormatPattern(pattern, fixedText, info, "");
                    }
                case 'E':
                case 'e':
                    {
                        if (nMaxDigits < 0)
                            nMaxDigits = 6;
                        nMaxDigits++;
                        RoundNumber(n, nMaxDigits, isCorrectlyRounded);
                        var output = new StringBuilder();
                        if (n.IsNegative)
                            output.Append(info.NegativeSign);
                        FormatScientific(output, n, nMaxDigits, info, (char)format);
                        return output.ToString();
                    }
                case 'R':
                case 'r':
                case 'G':
                case 'g':
                    {
                        // R formats as G.
                        char exponentChar = format == 'R' || format == 'G' ? 'E' : 'e';
                        var output = new StringBuilder();
                        if (nMaxDigits < 1)
                        {
                            if (n.Kind == NeoNumberBufferKind.Decimal && nMaxDigits == -1)
                            {
                                // Decimal G keeps trailing zeros; a zero formats without its sign.
                                if (n.IsNegative && n.Digits.Length != 0)
                                    output.Append(info.NegativeSign);
                                FormatGeneral(output, n, nMaxDigits, info, exponentChar, true);
                                return output.ToString();
                            }
                            nMaxDigits = n.Digits.Length;
                        }
                        RoundNumber(n, nMaxDigits, isCorrectlyRounded);
                        if (n.IsNegative)
                            output.Append(info.NegativeSign);
                        FormatGeneral(output, n, nMaxDigits, info, exponentChar, false);
                        return output.ToString();
                    }
                case 'P':
                case 'p':
                    {
                        if (nMaxDigits < 0)
                            nMaxDigits = info.PercentDecimalDigits;
                        n.Scale += 2;
                        RoundNumber(n, n.Scale + nMaxDigits, isCorrectlyRounded);
                        string fixedText = Fixed(n, nMaxDigits, info.PercentGroupSizes, info.PercentDecimalSeparator, info.PercentGroupSeparator);
                        string pattern = n.IsNegative
                            ? negativePercentFormats[info.PercentNegativePattern]
                            : positivePercentFormats[info.PercentPositivePattern];
                        return FormatPattern(pattern, fixedText, info, info.PercentSymbol);
                    }
            }
            throw NeoRuntimeError.BadFormatSpecifier();
        }

        private static int CharAt(string s, int index) => index < s.Length ? s[index] : -1;

        /// <summary>.NET's <c>FindSection</c>: the start of <c>;</c>-separated section <paramref name="section"/>, or 0.</summary>
        private static int FindSection(string format, int section)
        {
            if (section == 0)
                return 0;
            int src = 0;
            while (true)
            {
                if (src >= format.Length)
                    return 0;
                char ch = format[src++];
                switch (ch)
                {
                    case '\'':
                    case '"':
                        while (src < format.Length && format[src] != 0)
                        {
                            if (format[src++] == ch)
                                break;
                        }
                        break;
                    case '\\':
                        if (src < format.Length && format[src] != 0)
                            src++;
                        break;
                    case ';':
                        if (--section != 0)
                            break;
                        if (src < format.Length && format[src] != 0 && format[src] != ';')
                            return src;
                        return 0;
                    case '\0':
                        return 0;
                }
            }
        }

        /// <summary>.NET's <c>NumberToStringFormat</c>: custom numeric format strings.</summary>
        private static string NumberToStringFormat(NeoNumberBuffer n, string format, NumberFormatInfo info)
        {
            int length = format.Length;
            int digitCount;
            int decimalPos;
            int firstDigit;
            int lastDigit;
            bool scientific;
            int thousandPos;
            int thousandCount = 0;
            bool thousandSeps;
            int scaleAdjust;
            int src;

            int section = FindSection(format, n.Digits.Length == 0 ? 2 : n.IsNegative ? 1 : 0);

            while (true)
            {
                digitCount = 0;
                decimalPos = -1;
                firstDigit = 0x7fffffff;
                lastDigit = 0;
                scientific = false;
                thousandPos = -1;
                thousandSeps = false;
                scaleAdjust = 0;
                src = section;

                while (src < length)
                {
                    char ch = format[src++];
                    if (ch == 0 || ch == ';')
                        break;
                    switch (ch)
                    {
                        case '#':
                            digitCount++;
                            break;
                        case '0':
                            if (firstDigit == 0x7fffffff)
                                firstDigit = digitCount;
                            digitCount++;
                            lastDigit = digitCount;
                            break;
                        case '.':
                            if (decimalPos < 0)
                                decimalPos = digitCount;
                            break;
                        case ',':
                            if (digitCount > 0 && decimalPos < 0)
                            {
                                if (thousandPos >= 0)
                                {
                                    if (thousandPos == digitCount)
                                    {
                                        thousandCount++;
                                        break;
                                    }
                                    thousandSeps = true;
                                }
                                thousandPos = digitCount;
                                thousandCount = 1;
                            }
                            break;
                        case '%':
                            scaleAdjust += 2;
                            break;
                        case '‰':
                            scaleAdjust += 3;
                            break;
                        case '\'':
                        case '"':
                            while (src < length && format[src] != 0)
                            {
                                if (format[src++] == ch)
                                    break;
                            }
                            break;
                        case '\\':
                            if (src < length && format[src] != 0)
                                src++;
                            break;
                        case 'E':
                        case 'e':
                            {
                                int next = CharAt(format, src);
                                int after = CharAt(format, src + 1);
                                if (next == '0' || ((next == '+' || next == '-') && after == '0'))
                                {
                                    while (++src < length && format[src] == '0')
                                    {
                                    }
                                    scientific = true;
                                }
                                break;
                            }
                    }
                }

                if (decimalPos < 0)
                    decimalPos = digitCount;
                if (thousandPos >= 0)
                {
                    if (thousandPos == decimalPos)
                        scaleAdjust -= thousandCount * 3;
                    else
                        thousandSeps = true;
                }

                if (n.Digits.Length != 0)
                {
                    n.Scale += scaleAdjust;
                    int pos = scientific ? digitCount : n.Scale + digitCount - decimalPos;
                    RoundNumber(n, pos, false);
                    if (n.Digits.Length == 0)
                    {
                        src = FindSection(format, 2);
                        if (src != section)
                        {
                            section = src;
                            continue;
                        }
                    }
                }
                else
                {
                    if (n.Kind != NeoNumberBufferKind.FloatingPoint)
                        n.IsNegative = false;
                    n.Scale = 0;
                }
                break;
            }

            firstDigit = firstDigit < decimalPos ? decimalPos - firstDigit : 0;
            lastDigit = lastDigit > decimalPos ? decimalPos - lastDigit : 0;
            int digPos;
            int adjust;
            if (scientific)
            {
                digPos = decimalPos;
                adjust = 0;
            }
            else
            {
                digPos = n.Scale > decimalPos ? n.Scale : decimalPos;
                adjust = n.Scale - decimalPos;
            }
            src = section;

            var thousandsSepPos = new List<int>();
            int thousandsSepCtr = -1;
            string groupSeparator = info.NumberGroupSeparator;
            if (thousandSeps && groupSeparator.Length > 0)
            {
                int[] groupDigits = info.NumberGroupSizes;
                int groupSizeIndex = 0;
                int groupTotalSizeCount = groupDigits.Length != 0 ? groupDigits[0] : 0;
                int groupSize = groupTotalSizeCount;
                int totalDigits = digPos + (adjust < 0 ? adjust : 0);
                int numDigits = firstDigit > totalDigits ? firstDigit : totalDigits;
                while (numDigits > groupTotalSizeCount)
                {
                    if (groupSize == 0)
                        break;
                    thousandsSepPos.Add(groupTotalSizeCount);
                    thousandsSepCtr++;
                    if (groupSizeIndex < groupDigits.Length - 1)
                    {
                        groupSizeIndex++;
                        groupSize = groupDigits[groupSizeIndex];
                    }
                    groupTotalSizeCount += groupSize;
                }
            }

            string digits = n.Digits;
            var output = new StringBuilder();
            if (n.IsNegative && section == 0 && n.Scale != 0)
                output.Append(info.NegativeSign);

            bool decimalWritten = false;
            int cur = 0;
            while (src < length)
            {
                char ch = format[src++];
                if (ch == 0 || ch == ';')
                    break;
                if (adjust > 0 && (ch == '#' || ch == '0' || ch == '.'))
                {
                    while (adjust > 0)
                    {
                        output.Append(cur < digits.Length ? digits[cur++] : '0');
                        if (thousandSeps && digPos > 1 && thousandsSepCtr >= 0)
                        {
                            if (digPos == thousandsSepPos[thousandsSepCtr] + 1)
                            {
                                output.Append(groupSeparator);
                                thousandsSepCtr--;
                            }
                        }
                        digPos--;
                        adjust--;
                    }
                }

                switch (ch)
                {
                    case '#':
                    case '0':
                        {
                            char digit;
                            if (adjust < 0)
                            {
                                adjust++;
                                digit = digPos <= firstDigit ? '0' : '\0';
                            }
                            else if (cur < digits.Length)
                            {
                                digit = digits[cur++];
                            }
                            else
                            {
                                digit = digPos > lastDigit ? '0' : '\0';
                            }
                            if (digit != '\0')
                            {
                                output.Append(digit);
                                if (thousandSeps && digPos > 1 && thousandsSepCtr >= 0)
                                {
                                    if (digPos == thousandsSepPos[thousandsSepCtr] + 1)
                                    {
                                        output.Append(groupSeparator);
                                        thousandsSepCtr--;
                                    }
                                }
                            }
                            digPos--;
                            break;
                        }
                    case '.':
                        // Repeated decimal points are not echoed.
                        if (digPos != 0 || decimalWritten)
                            break;
                        if (lastDigit < 0 || (decimalPos < digitCount && cur < digits.Length))
                        {
                            output.Append(info.NumberDecimalSeparator);
                            decimalWritten = true;
                        }
                        break;
                    case '‰':
                        output.Append(info.PerMilleSymbol);
                        break;
                    case '%':
                        output.Append(info.PercentSymbol);
                        break;
                    case ',':
                        break;
                    case '\'':
                    case '"':
                        while (src < length && format[src] != 0 && format[src] != ch)
                            output.Append(format[src++]);
                        if (src < length && format[src] != 0)
                            src++;
                        break;
                    case '\\':
                        if (src < length && format[src] != 0)
                            output.Append(format[src++]);
                        break;
                    case 'E':
                    case 'e':
                        {
                            bool positiveSign = false;
                            int count = 0;
                            if (scientific)
                            {
                                int next = CharAt(format, src);
                                int after = CharAt(format, src + 1);
                                if (next == '0')
                                {
                                    count++;
                                }
                                else if (next == '+' && after == '0')
                                {
                                    positiveSign = true;
                                }
                                else if (!(next == '-' && after == '0'))
                                {
                                    output.Append(ch);
                                    break;
                                }
                                while (++src < length && format[src] == '0')
                                    count++;
                                if (count > 10)
                                    count = 10;
                                int exponent = digits.Length == 0 ? 0 : n.Scale - decimalPos;
                                FormatExponent(output, info, exponent, ch, count, positiveSign);
                                scientific = false;
                            }
                            else
                            {
                                output.Append(ch);
                                if (src < length)
                                {
                                    char sign = format[src];
                                    if (sign == '+' || sign == '-')
                                        output.Append(format[src++]);
                                    while (src < length && format[src] == '0')
                                        output.Append(format[src++]);
                                }
                            }
                            break;
                        }
                    default:
                        output.Append(ch);
                        break;
                }
            }

            if (n.IsNegative && section == 0 && n.Scale == 0 && output.Length > 0)
                output.Insert(0, info.NegativeSign);
            return output.ToString();
        }

        private static string FormatNumber(NeoNumberBuffer n, string format, int digits, int fmt, NumberFormatInfo info) =>
            fmt != 0 ? NumberToString(n, fmt, digits, info) : NumberToStringFormat(n, format, info);
    }
}
