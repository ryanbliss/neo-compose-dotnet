// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Text;

namespace NeoCompose.Runtime.NeoScript.Globalization
{
    /// <summary>
    /// .NET 10's invariant-mode character data (P127 §11.2, §11.3), read
    /// from <see cref="NeoUnicodeTables"/> so Mono's tables never decide a
    /// result.
    /// </summary>
    internal static class NeoUnicode
    {
        /// <summary>Simple uppercase mapping per code point; lone surrogates stay.</summary>
        internal static string ToUpperInvariant(string s) => Map(s, NeoUnicodeTables.UpperCaseRuns);

        /// <summary>Simple lowercase mapping per code point; lone surrogates stay.</summary>
        internal static string ToLowerInvariant(string s) => Map(s, NeoUnicodeTables.LowerCaseRuns);

        /// <summary><c>string.Equals(a, b, StringComparison.OrdinalIgnoreCase)</c> in invariant mode.</summary>
        internal static bool EqualsOrdinalIgnoreCase(string a, string b) =>
            a.Length == b.Length
            && (string.Equals(a, b, System.StringComparison.Ordinal)
                || string.Equals(ToUpperInvariant(a), ToUpperInvariant(b), System.StringComparison.Ordinal));

        /// <summary><c>CharUnicodeInfo.GetDecimalDigitValue</c>, or -1.</summary>
        internal static int DecimalDigitValue(int codePoint)
        {
            int[] runs = NeoUnicodeTables.DecimalDigitRuns;
            int low = 0;
            int high = runs.Length / 3 - 1;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                int start = runs[mid * 3];
                if (codePoint < start)
                    high = mid - 1;
                else if (codePoint >= start + runs[mid * 3 + 1])
                    low = mid + 1;
                else
                    return runs[mid * 3 + 2] + codePoint - start;
            }
            return -1;
        }

        internal static bool IsPrivateUse(int codePoint)
        {
            int[] ranges = NeoUnicodeTables.PrivateUseRanges;
            for (int index = 0; index < ranges.Length; index += 2)
            {
                if (codePoint >= ranges[index] && codePoint <= ranges[index + 1])
                    return true;
            }
            return false;
        }

        /// <summary><c>char.IsWhiteSpace</c>.</summary>
        internal static bool IsWhiteSpace(int c)
        {
            if (c <= 0xff)
                return c == 0x20 || (c >= 0x09 && c <= 0x0d) || c == 0x85 || c == 0xa0;
            return c == 0x1680
                || (c >= 0x2000 && c <= 0x200a)
                || c == 0x2028
                || c == 0x2029
                || c == 0x202f
                || c == 0x205f
                || c == 0x3000;
        }

        /// <summary><c>ReadOnlySpan&lt;char&gt;.Trim()</c>.</summary>
        internal static string TrimWhiteSpace(string s)
        {
            int start = 0;
            int end = s.Length;
            while (start < end && IsWhiteSpace(s[start]))
                start++;
            while (end > start && IsWhiteSpace(s[end - 1]))
                end--;
            return start == 0 && end == s.Length ? s : s.Substring(start, end - start);
        }

        /// <summary>Maps one code point through sorted <c>[start, length, delta, stride]</c> runs.</summary>
        private static int MapCodePoint(int[] runs, int codePoint)
        {
            int low = 0;
            int high = runs.Length / 4 - 1;
            while (low <= high)
            {
                int mid = (low + high) >> 1;
                int start = runs[mid * 4];
                if (codePoint < start)
                {
                    high = mid - 1;
                    continue;
                }
                int offset = codePoint - start;
                int stride = runs[mid * 4 + 3];
                if (offset >= runs[mid * 4 + 1] * stride)
                {
                    low = mid + 1;
                    continue;
                }
                return offset % stride == 0 ? codePoint + runs[mid * 4 + 2] : codePoint;
            }
            return codePoint;
        }

        private static string Map(string s, int[] runs)
        {
            StringBuilder? result = null;
            int copied = 0;
            for (int index = 0; index < s.Length; index++)
            {
                int codePoint = s[index];
                int width = 1;
                if (codePoint >= 0xd800 && codePoint <= 0xdbff && index + 1 < s.Length)
                {
                    int low = s[index + 1];
                    if (low >= 0xdc00 && low <= 0xdfff)
                    {
                        codePoint = ((codePoint - 0xd800) << 10) + (low - 0xdc00) + 0x10000;
                        width = 2;
                    }
                }
                int mapped = MapCodePoint(runs, codePoint);
                if (mapped != codePoint)
                {
                    result ??= new StringBuilder(s.Length);
                    result.Append(s, copied, index - copied);
                    AppendCodePoint(result, mapped);
                    copied = index + width;
                }
                index += width - 1;
            }
            if (result is null)
                return s;
            return result.Append(s, copied, s.Length - copied).ToString();
        }

        private static void AppendCodePoint(StringBuilder builder, int codePoint)
        {
            if (codePoint < 0x10000)
            {
                builder.Append((char)codePoint);
                return;
            }
            codePoint -= 0x10000;
            builder.Append((char)(0xd800 + (codePoint >> 10)));
            builder.Append((char)(0xdc00 + (codePoint & 0x3ff)));
        }
    }
}
