// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime.NeoScript.Globalization
{
    /// <summary>
    /// The <c>string</c> and <c>bool</c> members P127 §11 pins to .NET 10:
    /// ordinal comparison with .NET's exact integers, <c>IParsable</c>,
    /// <c>bool</c> text and interpolation alignment (§4.6).
    /// </summary>
    internal static class NeoStrings
    {
        /// <summary><c>string.Parse</c> through <c>IParsable&lt;string&gt;</c>.</summary>
        internal static string ParseString(string? s) => s ?? throw NeoRuntimeError.ArgumentNull("s");

        /// <summary><c>string.TryParse</c> through <c>IParsable&lt;string&gt;</c>.</summary>
        internal static bool TryParseString(string? s, out string? result)
        {
            result = s;
            return s is not null;
        }

        /// <summary>
        /// <c>string.Compare(a, b, StringComparison.Ordinal)</c> and
        /// <c>CompareTo</c>: the first differing code units' difference,
        /// else the length difference.
        /// </summary>
        internal static int Compare(string? a, string? b)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a is null)
                return -1;
            if (b is null)
                return 1;
            int length = a.Length < b.Length ? a.Length : b.Length;
            for (int index = 0; index < length; index++)
            {
                int difference = a[index] - b[index];
                if (difference != 0)
                    return difference;
            }
            return a.Length - b.Length;
        }

        /// <summary>
        /// <c>string.CompareOrdinal(a, b)</c>: the first two code units, NUL
        /// terminator included, then pairs, then the length difference.
        /// </summary>
        internal static int CompareOrdinal(string? a, string? b)
        {
            if (ReferenceEquals(a, b))
                return 0;
            if (a is null)
                return -1;
            if (b is null)
                return 1;
            int difference = TerminatedAt(a, 0) - TerminatedAt(b, 0);
            if (difference != 0)
                return difference;
            difference = TerminatedAt(a, 1) - TerminatedAt(b, 1);
            if (difference != 0)
                return difference;
            int length = a.Length < b.Length ? a.Length : b.Length;
            for (int index = 2; index < length; index += 2)
            {
                difference = a[index] - b[index];
                if (difference != 0)
                    return difference;
                difference = TerminatedAt(a, index + 1) - TerminatedAt(b, index + 1);
                if (difference != 0)
                    return difference;
            }
            return a.Length - b.Length;
        }

        /// <summary>Interpolation alignment: positive pads left, negative pads right.</summary>
        internal static string Align(string text, int alignment) =>
            alignment >= 0 ? text.PadLeft(alignment) : text.PadRight(-alignment);

        /// <summary><c>bool.ToString()</c>.</summary>
        internal static string FormatBoolean(bool value) => value ? "True" : "False";

        /// <summary><c>bool.Parse</c>.</summary>
        internal static bool ParseBoolean(string? s)
        {
            if (s is null)
                throw NeoRuntimeError.ArgumentNull("value");
            if (!TryParseBoolean(s, out bool result))
            {
                throw new NeoRuntimeError(
                    NeoErrorClass.NeoFormatError,
                    $"String '{s}' was not recognized as a valid Boolean.");
            }
            return result;
        }

        /// <summary><c>bool.TryParse</c>.</summary>
        internal static bool TryParseBoolean(string? s, out bool result)
        {
            result = false;
            if (s is null)
                return false;
            if (Match(s, 0, s.Length, out result))
                return true;
            if (s.Length < 5)
                return false;
            int start = 0;
            int end = s.Length;
            while (start < end && IsWhiteSpaceOrNull(s[start]))
                start++;
            while (end > start && IsWhiteSpaceOrNull(s[end - 1]))
                end--;
            if (end - start == s.Length)
                return false;
            return Match(s, start, end - start, out result);
        }

        private static int TerminatedAt(string s, int index) => index < s.Length ? s[index] : 0;

        private static bool IsWhiteSpaceOrNull(char c) => c == 0 || NeoUnicode.IsWhiteSpace(c);

        private static bool Match(string s, int start, int length, out bool result)
        {
            result = false;
            if (length == 4
                && (s[start] | 0x20) == 't'
                && (s[start + 1] | 0x20) == 'r'
                && (s[start + 2] | 0x20) == 'u'
                && (s[start + 3] | 0x20) == 'e')
            {
                result = true;
                return true;
            }
            return length == 5
                && (s[start] | 0x20) == 'f'
                && (s[start + 1] | 0x20) == 'a'
                && (s[start + 2] | 0x20) == 'l'
                && (s[start + 3] | 0x20) == 's'
                && (s[start + 4] | 0x20) == 'e';
        }
    }
}
