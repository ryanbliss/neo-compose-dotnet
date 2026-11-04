// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace NeoCompose.Runtime.NeoScript.Globalization
{
    /// <summary>
    /// .NET 10's <c>Number.Parsing</c> for <c>int</c>, <c>float</c> and
    /// <c>decimal</c> (P127 §11.5). A null <c>style</c> is the overload
    /// without one. It never calls the CLR's number parsing, whose results
    /// differ between Mono and .NET 10.
    /// </summary>
    internal static class NeoNumberParsing
    {
        private const int AllowLeadingWhite = 1;
        private const int AllowTrailingWhite = 2;
        private const int AllowLeadingSign = 4;
        private const int AllowTrailingSign = 8;
        private const int AllowParentheses = 16;
        private const int AllowDecimalPoint = 32;
        private const int AllowThousands = 64;
        private const int AllowExponent = 128;
        private const int AllowCurrencySymbol = 256;
        private const int AllowHexSpecifier = 512;
        private const int AllowBinarySpecifier = 1024;
        private const int IntegerStyle = 7;
        private const int HexNumberStyle = 515;
        private const int BinaryNumberStyle = 1027;
        private const int InvalidNumberStyles = ~0x7ff;
        private const int HexOrBinary = AllowHexSpecifier | AllowBinarySpecifier;

        /// <summary><c>NumberStyles.Integer</c>.</summary>
        internal const int DefaultInt32Style = IntegerStyle;

        /// <summary><c>NumberStyles.Float | NumberStyles.AllowThousands</c>.</summary>
        internal const int DefaultSingleStyle = 167 | AllowThousands;

        /// <summary><c>NumberStyles.Number</c>.</summary>
        internal const int DefaultDecimalStyle = 111;

        // .NET's NumberBuffer digit capacities, less the NUL.
        private const int Int32MaxDigits = 10;
        private const int DecimalMaxDigits = 30;
        private const int SingleMaxDigits = 113;
        private const long MaxValueDiv10 = 214748364;

        private static readonly BigInteger mask64 = (BigInteger.One << 64) - 1;
        private static readonly BigInteger ulongMaxDiv10 = mask64 / 10;
        private static readonly BigInteger uintMaxDiv10 = 429496729;
        private static readonly BigInteger low64Limit = BigInteger.Parse("11068046444225730969", CultureInfo.InvariantCulture);
        private static readonly BigInteger low64Wrapped = BigInteger.Parse("11068046444225730970", CultureInfo.InvariantCulture);

        // Exact float32 powers of ten for the fast path.
        private static readonly double[] smallPowersOf10 = { 1, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10 };

        private static readonly List<BigInteger> powersOf10 = new() { BigInteger.One };

        /// <summary>Why a parse produced no value.</summary>
        private enum Failure
        {
            None,
            Format,
            Overflow,
        }

        /// <summary><c>TryParseNumber</c>'s state flags.</summary>
        [Flags]
        private enum ParseState
        {
            Sign = 0x1,
            Parens = 0x2,
            Digits = 0x4,
            NonZero = 0x8,
            Decimal = 0x10,
            Currency = 0x20,
        }

        /// <summary><c>int.Parse(s, style, provider)</c>.</summary>
        internal static int ParseInt32(string? s, int? style, object? provider)
        {
            if (s is null)
                throw NeoRuntimeError.ArgumentNull("s");
            int styles = style ?? DefaultInt32Style;
            ValidateIntegerStyle(styles);
            Failure failure = ParseInt32Value(s, styles, NeoNumberFormatInfos.GetInstance(provider), out int result);
            if (failure == Failure.Format)
                throw NeoRuntimeError.InvalidNumber(s);
            if (failure == Failure.Overflow)
            {
                throw new NeoRuntimeError(
                    NeoErrorClass.NeoOverflowError,
                    "Value was either too large or too small for an Int32.");
            }
            return result;
        }

        /// <summary><c>int.TryParse</c>: the style is checked before the null text.</summary>
        internal static bool TryParseInt32(string? s, int? style, object? provider, out int result)
        {
            int styles = style ?? DefaultInt32Style;
            ValidateIntegerStyle(styles);
            result = 0;
            if (s is null)
                return false;
            Failure failure = ParseInt32Value(s, styles, NeoNumberFormatInfos.GetInstance(provider), out int value);
            if (failure != Failure.None)
                return false;
            result = value;
            return true;
        }

        /// <summary><c>decimal.Parse(s, style, provider)</c>, keeping the scale and a zero's sign.</summary>
        internal static decimal ParseDecimal(string? s, int? style, object? provider)
        {
            if (s is null)
                throw NeoRuntimeError.ArgumentNull("s");
            int styles = style ?? DefaultDecimalStyle;
            ValidateFloatingPointStyle(styles);
            Failure failure = ParseDecimalValue(s, styles, NeoNumberFormatInfos.GetInstance(provider), out decimal result);
            if (failure == Failure.Format)
                throw NeoRuntimeError.InvalidNumber(s);
            if (failure == Failure.Overflow)
                throw NeoDecimalMath.Overflow();
            return result;
        }

        /// <summary><c>decimal.TryParse</c>: the style is checked before the null text.</summary>
        internal static bool TryParseDecimal(string? s, int? style, object? provider, out decimal result)
        {
            int styles = style ?? DefaultDecimalStyle;
            ValidateFloatingPointStyle(styles);
            result = 0m;
            if (s is null)
                return false;
            Failure failure = ParseDecimalValue(s, styles, NeoNumberFormatInfos.GetInstance(provider), out decimal value);
            if (failure != Failure.None)
                return false;
            result = value;
            return true;
        }

        /// <summary><c>float.Parse(s, style, provider)</c>.</summary>
        internal static float ParseSingle(string? s, int? style, object? provider)
        {
            if (s is null)
                throw NeoRuntimeError.ArgumentNull("s");
            int styles = style ?? DefaultSingleStyle;
            ValidateFloatingPointStyle(styles);
            if (!ParseSingleValue(s, styles, NeoNumberFormatInfos.GetInstance(provider), out float result))
                throw NeoRuntimeError.InvalidNumber(s);
            return result;
        }

        /// <summary><c>float.TryParse</c>: the style is checked before the null text.</summary>
        internal static bool TryParseSingle(string? s, int? style, object? provider, out float result)
        {
            int styles = style ?? DefaultSingleStyle;
            ValidateFloatingPointStyle(styles);
            result = 0f;
            if (s is null)
                return false;
            return ParseSingleValue(s, styles, NeoNumberFormatInfos.GetInstance(provider), out result);
        }

        private static NeoRuntimeError InvalidStyles() => new(
            NeoErrorClass.NeoArgumentError,
            "An undefined NumberStyles value is being used. (Parameter 'style')");

        private static void ValidateIntegerStyle(int style)
        {
            if ((style & (InvalidNumberStyles | HexOrBinary)) != 0
                && (style & ~HexNumberStyle) != 0
                && (style & ~BinaryNumberStyle) != 0)
            {
                if ((style & InvalidNumberStyles) != 0)
                    throw InvalidStyles();
                throw new NeoRuntimeError(
                    NeoErrorClass.NeoArgumentError,
                    "With the AllowHexSpecifier or AllowBinarySpecifier bit set in the enum bit field, the only other valid bits that can be combined into the enum value must be AllowLeadingWhite and AllowTrailingWhite. (Parameter 'style')");
            }
        }

        private static void ValidateFloatingPointStyle(int style)
        {
            if ((style & InvalidNumberStyles) != 0)
                throw InvalidStyles();
            if ((style & HexOrBinary) != 0)
            {
                throw new NeoRuntimeError(
                    NeoErrorClass.NeoArgumentError,
                    "The number styles AllowHexSpecifier and AllowBinarySpecifier are not supported on floating point data types. (Parameter 'style')");
            }
        }

        private static bool IsWhite(int c) => c == 0x20 || (c >= 0x09 && c <= 0x0d);

        private static bool IsDigit(int c) => c >= '0' && c <= '9';

        private static int CodeAt(string s, int index) => index < s.Length ? s[index] : 0;

        /// <summary>Only NULs remain from <paramref name="index"/>.</summary>
        private static bool TrailingZeros(string s, int index)
        {
            for (int at = index; at < s.Length; at++)
            {
                if (s[at] != 0)
                    return false;
            }
            return true;
        }

        /// <summary>.NET's <c>MatchChars</c>: <paramref name="value"/> up to its first NUL; U+00A0 and U+202F also match a space.</summary>
        private static int MatchChars(string s, int p, string value)
        {
            if (value.Length == 0 || value[0] == 0)
                return -1;
            int j = 0;
            while (true)
            {
                int cp = CodeAt(s, p);
                int val = value[j];
                if (cp != val && !((val == 0xa0 || val == 0x202f) && cp == 0x20))
                    return -1;
                p++;
                j++;
                if (j >= value.Length || value[j] == 0)
                    return p;
            }
        }

        private static int MatchNegativeSign(string s, int p, NumberFormatInfo info)
        {
            int next = MatchChars(s, p, info.NegativeSign);
            if (next < 0 && NeoNumberFormatInfos.AllowHyphenDuringParsing(info) && CodeAt(s, p) == '-')
                return p + 1;
            return next;
        }

        /// <summary>.NET's <c>TryStringToNumber</c> over <c>TryParseNumber</c>, or false when the text is malformed.</summary>
        private static bool TryStringToNumber(
            string s,
            int styles,
            NumberFormatInfo info,
            NeoNumberBufferKind kind,
            int maxDigCount,
            out NeoNumberBuffer number,
            out bool hasNonZeroTail)
        {
            number = new NeoNumberBuffer { Kind = kind };
            hasNonZeroTail = false;
            int length = s.Length;
            bool parsingCurrency = (styles & AllowCurrencySymbol) != 0;
            string currSymbol = parsingCurrency ? info.CurrencySymbol : "";
            string decSep = parsingCurrency ? info.CurrencyDecimalSeparator : info.NumberDecimalSeparator;
            string groupSep = parsingCurrency ? info.CurrencyGroupSeparator : info.NumberGroupSeparator;

            bool isNegative = false;
            int scale = 0;
            ParseState state = 0;
            int p = 0;
            int ch = CodeAt(s, p);
            int next;

            while (true)
            {
                // Whitespace after a sign is only legal before a currency symbol ("-Kr 1", not "- 1").
                if (!IsWhite(ch)
                    || (styles & AllowLeadingWhite) == 0
                    || ((state & ParseState.Sign) != 0
                        && (state & ParseState.Currency) == 0
                        && info.NumberNegativePattern != 2))
                {
                    next = -1;
                    if ((styles & AllowLeadingSign) != 0 && (state & ParseState.Sign) == 0)
                    {
                        next = MatchChars(s, p, info.PositiveSign);
                        if (next < 0)
                        {
                            next = MatchNegativeSign(s, p, info);
                            if (next >= 0)
                                isNegative = true;
                        }
                    }
                    if (next >= 0)
                    {
                        state |= ParseState.Sign;
                        p = next - 1;
                    }
                    else if (ch == '(' && (styles & AllowParentheses) != 0 && (state & ParseState.Sign) == 0)
                    {
                        state |= ParseState.Sign | ParseState.Parens;
                        isNegative = true;
                    }
                    else if (currSymbol.Length != 0 && (next = MatchChars(s, p, currSymbol)) >= 0)
                    {
                        state |= ParseState.Currency;
                        currSymbol = "";
                        p = next - 1;
                    }
                    else
                    {
                        break;
                    }
                }
                ch = CodeAt(s, ++p);
            }

            int digCount = 0;
            int digEnd = 0;
            int numberOfTrailingZeros = 0;
            var stored = new StringBuilder();
            while (true)
            {
                if (IsDigit(ch))
                {
                    state |= ParseState.Digits;
                    if (ch != '0' || (state & ParseState.NonZero) != 0)
                    {
                        if (digCount < maxDigCount)
                        {
                            stored.Append((char)ch);
                            if (ch != '0' || kind != NeoNumberBufferKind.Integer)
                                digEnd = digCount + 1;
                            numberOfTrailingZeros = ch == '0' ? numberOfTrailingZeros + 1 : 0;
                        }
                        else if (ch != '0')
                        {
                            hasNonZeroTail = true;
                        }
                        if ((state & ParseState.Decimal) == 0)
                            scale++;
                        digCount++;
                        state |= ParseState.NonZero;
                    }
                    else if ((state & ParseState.Decimal) != 0)
                    {
                        scale--;
                    }
                }
                else if ((styles & AllowDecimalPoint) != 0
                    && (state & ParseState.Decimal) == 0
                    && ((next = MatchChars(s, p, decSep)) >= 0
                        || (parsingCurrency
                            && (state & ParseState.Currency) == 0
                            && (next = MatchChars(s, p, info.NumberDecimalSeparator)) >= 0)))
                {
                    state |= ParseState.Decimal;
                    p = next - 1;
                }
                else if ((styles & AllowThousands) != 0
                    && (state & ParseState.Digits) != 0
                    && (state & ParseState.Decimal) == 0
                    && ((next = MatchChars(s, p, groupSep)) >= 0
                        || (parsingCurrency
                            && (state & ParseState.Currency) == 0
                            && (next = MatchChars(s, p, info.NumberGroupSeparator)) >= 0)))
                {
                    p = next - 1;
                }
                else
                {
                    break;
                }
                ch = CodeAt(s, ++p);
            }

            if ((state & ParseState.Digits) == 0)
                return false;
            int digitsCount = digEnd;

            if ((ch == 'E' || ch == 'e') && (styles & AllowExponent) != 0)
            {
                int temp = p;
                ch = CodeAt(s, ++p);
                bool negExp = false;
                if ((next = MatchChars(s, p, info.PositiveSign)) >= 0)
                {
                    p = next;
                    ch = CodeAt(s, p);
                }
                else if ((next = MatchNegativeSign(s, p, info)) >= 0)
                {
                    p = next;
                    ch = CodeAt(s, p);
                    negExp = true;
                }
                if (IsDigit(ch))
                {
                    int exp = 0;
                    do
                    {
                        // A tenth exponent digit means overflow; a format error can still follow.
                        if (exp >= 100_000_000)
                        {
                            exp = 0x7fffffff;
                            scale = 0;
                            while (IsDigit(ch))
                                ch = CodeAt(s, ++p);
                            break;
                        }
                        exp = exp * 10 + ch - '0';
                        ch = CodeAt(s, ++p);
                    }
                    while (IsDigit(ch));
                    scale += negExp ? -exp : exp;
                }
                else
                {
                    p = temp;
                    ch = CodeAt(s, p);
                }
            }

            if (kind == NeoNumberBufferKind.FloatingPoint && !hasNonZeroTail)
            {
                int numberOfFractionalDigits = digEnd - scale;
                if (numberOfFractionalDigits > 0)
                    digitsCount = digEnd - Math.Min(numberOfTrailingZeros, numberOfFractionalDigits);
            }

            while (true)
            {
                if (!IsWhite(ch) || (styles & AllowTrailingWhite) == 0)
                {
                    next = -1;
                    if ((styles & AllowTrailingSign) != 0 && (state & ParseState.Sign) == 0)
                    {
                        next = MatchChars(s, p, info.PositiveSign);
                        if (next < 0)
                        {
                            next = MatchNegativeSign(s, p, info);
                            if (next >= 0)
                                isNegative = true;
                        }
                    }
                    if (next >= 0)
                    {
                        state |= ParseState.Sign;
                        p = next - 1;
                    }
                    else if (ch == ')' && (state & ParseState.Parens) != 0)
                    {
                        state &= ~ParseState.Parens;
                    }
                    else if (currSymbol.Length != 0 && (next = MatchChars(s, p, currSymbol)) >= 0)
                    {
                        currSymbol = "";
                        p = next - 1;
                    }
                    else
                    {
                        break;
                    }
                }
                ch = CodeAt(s, ++p);
            }

            if ((state & ParseState.Parens) != 0)
                return false;
            if ((state & ParseState.NonZero) == 0)
            {
                if (kind != NeoNumberBufferKind.Decimal)
                    scale = 0;
                if (kind == NeoNumberBufferKind.Integer && (state & ParseState.Decimal) == 0)
                    isNegative = false;
            }
            if (p < length && !TrailingZeros(s, p))
                return false;

            if (digitsCount < stored.Length)
                stored.Length = digitsCount;
            number.Digits = stored.ToString();
            number.Scale = scale;
            number.IsNegative = isNegative;
            return true;
        }

        /// <summary>.NET's <c>HasTrailingChars</c> tail: trailing white, then only NULs.</summary>
        private static bool AcceptsTrailing(string value, int index, int num, int styles)
        {
            if (IsWhite(num))
            {
                if ((styles & AllowTrailingWhite) == 0)
                    return false;
                for (index++; index < value.Length; index++)
                {
                    if (!IsWhite(value[index]))
                        break;
                }
                if (index >= value.Length)
                    return true;
            }
            return TrailingZeros(value, index);
        }

        /// <summary>.NET's <c>TryParseBinaryIntegerStyle</c> for the <c>Integer</c> subset of styles.</summary>
        private static Failure ParseInt32IntegerStyle(string value, int styles, NumberFormatInfo info, out int result)
        {
            result = 0;
            if (value.Length == 0)
                return Failure.Format;
            int index = 0;
            int num = value[0];

            if ((styles & AllowLeadingWhite) != 0 && IsWhite(num))
            {
                do
                {
                    index++;
                    if (index >= value.Length)
                        return Failure.Format;
                    num = value[index];
                }
                while (IsWhite(num));
            }

            bool isNegative = false;
            if ((styles & AllowLeadingSign) != 0)
            {
                int signLength = 0;
                if (NeoNumberFormatInfos.HasInvariantNumberSigns(info))
                {
                    if (num == '-')
                    {
                        isNegative = true;
                        signLength = 1;
                    }
                    else if (num == '+')
                    {
                        signLength = 1;
                    }
                }
                else if (NeoNumberFormatInfos.AllowHyphenDuringParsing(info) && num == '-')
                {
                    isNegative = true;
                    signLength = 1;
                }
                else
                {
                    value = value.Substring(index);
                    index = 0;
                    string positiveSign = info.PositiveSign;
                    string negativeSign = info.NegativeSign;
                    if (positiveSign.Length != 0 && value.StartsWith(positiveSign, StringComparison.Ordinal))
                    {
                        signLength = positiveSign.Length;
                    }
                    else if (negativeSign.Length != 0 && value.StartsWith(negativeSign, StringComparison.Ordinal))
                    {
                        isNegative = true;
                        signLength = negativeSign.Length;
                    }
                }
                if (signLength != 0)
                {
                    index += signLength;
                    if (index >= value.Length)
                        return Failure.Format;
                    num = value[index];
                }
            }

            if (!IsDigit(num))
                return Failure.Format;
            long answer = 0;
            bool overflow = false;
            bool trailing = false;
            bool mayOverflow = false;
            if (ScanInt32Digits(value, ref index, ref num, ref answer, ref overflow, ref trailing, ref mayOverflow, isNegative))
                return Failure.Overflow;
            if (trailing)
            {
                if (!AcceptsTrailing(value, index, num, styles))
                    return Failure.Format;
                mayOverflow = true;
            }
            if (mayOverflow && overflow)
                return Failure.Overflow;
            result = unchecked((int)(isNegative ? -answer : answer));
            return Failure.None;
        }

        /// <summary>The digit loop of <c>TryParseBinaryIntegerStyle</c>; true when the text ends in an overflowing digit run.</summary>
        private static bool ScanInt32Digits(
            string value,
            ref int index,
            ref int num,
            ref long answer,
            ref bool overflow,
            ref bool trailing,
            ref bool mayOverflow,
            bool isNegative)
        {
            if (num == '0')
            {
                do
                {
                    index++;
                    if (index >= value.Length)
                        return false;
                    num = value[index];
                }
                while (num == '0');
                if (!IsDigit(num))
                {
                    trailing = true;
                    return false;
                }
            }
            answer = num - '0';
            index++;
            for (int count = 0; count < Int32MaxDigits - 2; count++)
            {
                if (index >= value.Length)
                    return false;
                num = value[index];
                if (!IsDigit(num))
                {
                    trailing = true;
                    return false;
                }
                index++;
                answer = answer * 10 + num - '0';
            }
            if (index >= value.Length)
                return false;
            num = value[index];
            if (!IsDigit(num))
            {
                trailing = true;
                return false;
            }
            index++;
            overflow = answer > MaxValueDiv10;
            answer = answer * 10 + num - '0';
            overflow |= answer > (isNegative ? 0x80000000L : 0x7fffffffL);
            mayOverflow = true;
            if (index >= value.Length)
                return false;
            // Format errors take precedence over overflow.
            num = value[index];
            while (IsDigit(num))
            {
                overflow = true;
                index++;
                if (index >= value.Length)
                    return true;
                num = value[index];
            }
            trailing = true;
            return false;
        }

        private static bool IsHexChar(int c) => IsDigit(c) || ((c | 0x20) >= 'a' && (c | 0x20) <= 'f');

        private static int HexValue(int c) => IsDigit(c) ? c - '0' : (c | 0x20) - 0x57;

        private static bool IsBinaryChar(int c) => c == '0' || c == '1';

        /// <summary>.NET's <c>TryParseBinaryIntegerHexOrBinaryNumberStyle</c> for int32.</summary>
        private static Failure ParseInt32HexOrBinary(string value, int styles, bool hex, out int result)
        {
            result = 0;
            if (value.Length == 0)
                return Failure.Format;
            int maxDigitCount = hex ? 8 : 32;
            int radix = hex ? 16 : 2;
            int index = 0;
            int num = value[0];

            if ((styles & AllowLeadingWhite) != 0 && IsWhite(num))
            {
                do
                {
                    index++;
                    if (index >= value.Length)
                        return Failure.Format;
                    num = value[index];
                }
                while (IsWhite(num));
            }

            if (!IsValid(num, hex))
                return Failure.Format;
            long answer = 0;
            bool overflow = false;
            bool trailing = false;
            switch (ScanRadixDigits(value, ref index, ref num, ref answer, hex, maxDigitCount, radix))
            {
                case RadixScan.Trailing:
                    trailing = true;
                    break;
                case RadixScan.Overflow:
                    return Failure.Overflow;
                case RadixScan.TrailingOverflow:
                    trailing = true;
                    overflow = true;
                    break;
            }
            if (trailing)
            {
                if (!AcceptsTrailing(value, index, num, styles))
                    return Failure.Format;
                if (overflow)
                    return Failure.Overflow;
            }
            result = unchecked((int)(uint)answer);
            return Failure.None;
        }

        private enum RadixScan
        {
            End,
            Trailing,
            Overflow,
            TrailingOverflow,
        }

        private static bool IsValid(int c, bool hex) => hex ? IsHexChar(c) : IsBinaryChar(c);

        private static RadixScan ScanRadixDigits(
            string value,
            ref int index,
            ref int num,
            ref long answer,
            bool hex,
            int maxDigitCount,
            int radix)
        {
            if (num == '0')
            {
                do
                {
                    index++;
                    if (index >= value.Length)
                        return RadixScan.End;
                    num = value[index];
                }
                while (num == '0');
                if (!IsValid(num, hex))
                    return RadixScan.Trailing;
            }
            answer = hex ? HexValue(num) : num - '0';
            index++;
            for (int count = 0; count < maxDigitCount - 1; count++)
            {
                if (index >= value.Length)
                    return RadixScan.End;
                num = value[index];
                if (!IsValid(num, hex))
                    return RadixScan.Trailing;
                index++;
                answer = answer * radix + (hex ? HexValue(num) : num - '0');
            }
            if (index >= value.Length)
                return RadixScan.End;
            num = value[index];
            if (!IsValid(num, hex))
                return RadixScan.Trailing;
            // Format errors take precedence over overflow.
            do
            {
                index++;
                if (index >= value.Length)
                    return RadixScan.Overflow;
                num = value[index];
            }
            while (IsValid(num, hex));
            return RadixScan.TrailingOverflow;
        }

        /// <summary>.NET's <c>TryNumberBufferToBinaryInteger</c> for int32.</summary>
        private static Failure NumberToInt32(NeoNumberBuffer parsed, bool hasNonZeroTail, out int result)
        {
            result = 0;
            int i = parsed.Scale;
            if (i > Int32MaxDigits)
                return Failure.Overflow;
            if (i < parsed.Digits.Length)
                return Failure.Overflow;
            if (hasNonZeroTail)
                return Failure.Overflow;
            long n = 0;
            int p = 0;
            while (--i >= 0)
            {
                if (n > MaxValueDiv10)
                    return Failure.Overflow;
                n *= 10;
                if (p < parsed.Digits.Length)
                    n += parsed.Digits[p++] - '0';
            }
            if (n > (parsed.IsNegative ? 0x80000000L : 0x7fffffffL))
                return Failure.Overflow;
            result = unchecked((int)(parsed.IsNegative ? -n : n));
            return Failure.None;
        }

        private static Failure ParseInt32Value(string s, int styles, NumberFormatInfo info, out int result)
        {
            if ((styles & ~IntegerStyle) == 0)
                return ParseInt32IntegerStyle(s, styles, info, out result);
            if ((styles & AllowHexSpecifier) != 0)
                return ParseInt32HexOrBinary(s, styles, true, out result);
            if ((styles & AllowBinarySpecifier) != 0)
                return ParseInt32HexOrBinary(s, styles, false, out result);
            if (!TryStringToNumber(s, styles, info, NeoNumberBufferKind.Integer, Int32MaxDigits, out NeoNumberBuffer parsed, out bool hasNonZeroTail))
            {
                result = 0;
                return Failure.Format;
            }
            return NumberToInt32(parsed, hasNonZeroTail, out result);
        }

        /// <summary>.NET's <c>TryNumberToDecimal</c>.</summary>
        private static Failure NumberToDecimal(NeoNumberBuffer parsed, bool hasNonZeroTail, out decimal result)
        {
            result = 0m;
            string digits = parsed.Digits;
            int e = parsed.Scale;
            bool sign = parsed.IsNegative;
            int p = 0;
            int c = CodeAt(digits, p);
            if (c == 0)
            {
                result = NeoDecimalValues.Compose(BigInteger.Zero, Math.Min(Math.Max(-e, 0), 28), sign);
                return Failure.None;
            }
            if (e > 29)
                return Failure.Overflow;

            BigInteger low64 = BigInteger.Zero;
            while (e > -28)
            {
                e--;
                low64 = low64 * 10 + (c - '0');
                c = CodeAt(digits, ++p);
                if (low64 >= ulongMaxDiv10)
                    break;
                if (c == 0)
                {
                    while (e > 0)
                    {
                        e--;
                        low64 *= 10;
                        if (low64 >= ulongMaxDiv10)
                            break;
                    }
                    break;
                }
            }

            BigInteger high = BigInteger.Zero;
            while ((e > 0 || (c != 0 && e > -28))
                && (high < uintMaxDiv10
                    || (high == uintMaxDiv10 && (low64 < low64Limit || (low64 == low64Limit && c <= '5')))))
            {
                BigInteger product = ((high << 64) | low64) * 10;
                low64 = product & mask64;
                high = product >> 64;
                if (c != 0)
                {
                    low64 += c - '0';
                    if (low64 > mask64)
                    {
                        low64 &= mask64;
                        high++;
                    }
                    c = CodeAt(digits, ++p);
                }
                e--;
            }

            if (c >= '5')
            {
                bool roundUp = true;
                if (c == '5' && low64.IsEven)
                {
                    c = CodeAt(digits, ++p);
                    bool hasZeroTail = !hasNonZeroTail;
                    while (c != 0 && hasZeroTail)
                    {
                        hasZeroTail = c == '0';
                        c = CodeAt(digits, ++p);
                    }
                    // An even coefficient followed by exactly 5 stays.
                    roundUp = !hasZeroTail;
                }
                if (roundUp)
                {
                    low64 = (low64 + 1) & mask64;
                    if (low64.IsZero)
                    {
                        high = (high + 1) & uint.MaxValue;
                        if (high.IsZero)
                        {
                            low64 = low64Wrapped;
                            high = uintMaxDiv10;
                            e++;
                        }
                    }
                }
            }

            if (e > 0)
                return Failure.Overflow;
            if (e <= -29)
            {
                result = NeoDecimalValues.Compose(BigInteger.Zero, 28, sign);
                return Failure.None;
            }
            result = NeoDecimalValues.Compose((high << 64) | low64, -e, sign);
            return Failure.None;
        }

        private static Failure ParseDecimalValue(string s, int styles, NumberFormatInfo info, out decimal result)
        {
            if (!TryStringToNumber(s, styles, info, NeoNumberBufferKind.Decimal, DecimalMaxDigits, out NeoNumberBuffer parsed, out bool hasNonZeroTail))
            {
                result = 0m;
                return Failure.Format;
            }
            return NumberToDecimal(parsed, hasNonZeroTail, out result);
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

        /// <summary>The bit length of a positive value (netstandard2.1 lacks <c>GetBitLength</c>).</summary>
        private static int BitLength(BigInteger value)
        {
            byte[] bytes = value.ToByteArray();
            int top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0)
                top--;
            int bits = top * 8;
            for (int b = bytes[top]; b != 0; b >>= 1)
                bits++;
            return bits;
        }

        /// <summary>An exact power of two as a double, for 2^-1074 through 2^1023.</summary>
        private static double PowerOf2(int exponent)
        {
            if (exponent >= -1022)
                return BitConverter.Int64BitsToDouble((long)(exponent + 1023) << 52);
            return BitConverter.Int64BitsToDouble(1L << (exponent + 1074));
        }

        /// <summary>The float32 nearest <c>digits × 10^(scale − digits.Length)</c>, ties to even; a nonzero tail breaks ties upward.</summary>
        private static float DigitsToSingle(string digits, int scale, bool hasNonZeroTail)
        {
            int exponent10 = scale - digits.Length;
            if (!hasNonZeroTail && digits.Length <= 8 && exponent10 >= -10 && exponent10 <= 10)
            {
                long fast = 0;
                foreach (char digit in digits)
                    fast = fast * 10 + digit - '0';
                // Both operands are exact float32 values, so rounding through float64 is exact (Figueroa).
                if (fast <= 0x1000000)
                {
                    return (float)(exponent10 >= 0
                        ? fast * smallPowersOf10[exponent10]
                        : fast / smallPowersOf10[-exponent10]);
                }
            }

            BigInteger numerator = BigInteger.Zero;
            foreach (char digit in digits)
                numerator = numerator * 10 + (digit - '0');
            BigInteger denominator = BigInteger.One;
            if (exponent10 >= 0)
                numerator *= PowerOf10(exponent10);
            else
                denominator = PowerOf10(-exponent10);

            int k = BitLength(numerator) - BitLength(denominator);
            bool atLeast2PowK = k >= 0 ? numerator >= denominator << k : numerator << -k >= denominator;
            int floorLog2 = atLeast2PowK ? k : k - 1;
            int exponent2 = Math.Max(floorLog2 - 23, -149);
            if (exponent2 >= 0)
                denominator <<= exponent2;
            else
                numerator <<= -exponent2;

            BigInteger quotient = BigInteger.DivRem(numerator, denominator, out BigInteger remainder);
            BigInteger twiceRemainder = remainder * 2;
            long mantissa = (long)quotient;
            if (twiceRemainder > denominator
                || (twiceRemainder == denominator && (hasNonZeroTail || (mantissa & 1) == 1)))
            {
                mantissa++;
            }
            return (float)(mantissa * PowerOf2(exponent2));
        }

        /// <summary>.NET's <c>NumberToFloat</c> for float32.</summary>
        private static float NumberToSingle(NeoNumberBuffer parsed, bool hasNonZeroTail)
        {
            float result;
            if (parsed.Digits.Length == 0 || parsed.Scale < -45)
                result = 0f;
            else if (parsed.Scale > 39)
                result = float.PositiveInfinity;
            else
                result = DigitsToSingle(parsed.Digits, parsed.Scale, hasNonZeroTail);
            return parsed.IsNegative ? -result : result;
        }

        /// <summary>.NET's <c>TryParseFloat</c>, with its symbol fallbacks.</summary>
        private static bool ParseSingleValue(string s, int styles, NumberFormatInfo info, out float result)
        {
            if (TryStringToNumber(s, styles, info, NeoNumberBufferKind.FloatingPoint, SingleMaxDigits, out NeoNumberBuffer parsed, out bool hasNonZeroTail))
            {
                result = NumberToSingle(parsed, hasNonZeroTail);
                return true;
            }
            string trimmed = NeoUnicode.TrimWhiteSpace(s);
            string positiveInfinity = info.PositiveInfinitySymbol;
            string nan = info.NaNSymbol;
            result = 0f;
            if (NeoUnicode.EqualsOrdinalIgnoreCase(trimmed, positiveInfinity))
            {
                result = float.PositiveInfinity;
                return true;
            }
            if (NeoUnicode.EqualsOrdinalIgnoreCase(trimmed, info.NegativeInfinitySymbol))
            {
                result = float.NegativeInfinity;
                return true;
            }
            if (NeoUnicode.EqualsOrdinalIgnoreCase(trimmed, nan))
            {
                result = float.NaN;
                return true;
            }

            string positiveSign = info.PositiveSign;
            if (StartsWithOrdinalIgnoreCase(trimmed, positiveSign))
            {
                string rest = trimmed.Substring(positiveSign.Length);
                if (NeoUnicode.EqualsOrdinalIgnoreCase(rest, positiveInfinity))
                {
                    result = float.PositiveInfinity;
                    return true;
                }
                if (NeoUnicode.EqualsOrdinalIgnoreCase(rest, nan))
                {
                    result = float.NaN;
                    return true;
                }
                return false;
            }
            string negativeSign = info.NegativeSign;
            if (StartsWithOrdinalIgnoreCase(trimmed, negativeSign))
            {
                if (NeoUnicode.EqualsOrdinalIgnoreCase(trimmed.Substring(negativeSign.Length), nan)
                    || (NeoNumberFormatInfos.AllowHyphenDuringParsing(info)
                        && trimmed.Length > 0
                        && trimmed[0] == '-'
                        && NeoUnicode.EqualsOrdinalIgnoreCase(trimmed.Substring(1), nan)))
                {
                    result = float.NaN;
                    return true;
                }
            }
            return false;
        }

        private static bool StartsWithOrdinalIgnoreCase(string s, string prefix) =>
            prefix.Length <= s.Length && NeoUnicode.EqualsOrdinalIgnoreCase(s.Substring(0, prefix.Length), prefix);
    }
}
