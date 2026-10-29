// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Globalization;
using System.Text;

namespace NeoCompose.Runtime.NeoScript.Globalization
{
    /// <summary>
    /// NeoScript's <c>CultureInfo</c> in invariant mode (P127 §11.1, §14
    /// "Culture types"). A NeoScript value is the CLR instance itself; this
    /// class holds the members whose NeoScript checks run before the CLR's.
    /// </summary>
    internal static class NeoCultures
    {
        private static CultureInfo? cached;

        /// <summary><c>InvariantCulture</c>, <c>CurrentCulture</c> and <c>CurrentUICulture</c>.</summary>
        internal static CultureInfo InvariantCulture => CultureInfo.InvariantCulture;

        internal static CultureInfo CurrentCulture => InvariantCulture;

        internal static CultureInfo CurrentUICulture => InvariantCulture;

        /// <summary><c>new CultureInfo(name)</c>: only the invariant culture exists.</summary>
        internal static CultureInfo New(string? name)
        {
            if (name is null)
                throw NeoRuntimeError.ArgumentNull("name");
            if (name.Length != 0)
                throw CultureNotFound(name);
            return new CultureInfo("");
        }

        /// <summary><c>CultureInfo.GetCultureInfo(name)</c>: one cached read-only invariant culture.</summary>
        internal static CultureInfo GetCultureInfo(string? name)
        {
            if (name is null)
                throw NeoRuntimeError.ArgumentNull("name");
            if (name.Length != 0)
                throw CultureNotFound(AsciiLower(name));
            return cached ??= CultureInfo.ReadOnly(new CultureInfo(""));
        }

        /// <summary><c>CultureInfo.ReadOnly</c>.</summary>
        internal static CultureInfo ReadOnly(CultureInfo? culture)
        {
            if (culture is null)
                throw NeoRuntimeError.ArgumentNull("ci");
            if (culture.IsReadOnly)
                return culture;
            NumberFormatInfo source = culture.NumberFormat;
            CultureInfo copy = CultureInfo.ReadOnly(culture);
            NeoNumberFormatInfos.ShareRetainedDigits(source, copy.NumberFormat);
            return copy;
        }

        /// <summary><c>Clone()</c>: a writable copy with a writable <c>NumberFormat</c>.</summary>
        internal static CultureInfo Clone(CultureInfo culture)
        {
            NumberFormatInfo source = culture.NumberFormat;
            var copy = (CultureInfo)culture.Clone();
            NeoNumberFormatInfos.ShareRetainedDigits(source, copy.NumberFormat);
            return copy;
        }

        /// <summary>
        /// <c>Parent</c>: the <c>InvariantCulture</c> singleton for every
        /// invariant culture, as on .NET 10, where Mono returns a distinct
        /// invariant culture for <c>new CultureInfo("")</c> and its copies.
        /// </summary>
        internal static CultureInfo Parent(CultureInfo culture) =>
            culture.Name.Length == 0 ? InvariantCulture : culture.Parent;

        /// <summary>The <c>NumberFormat</c> setter.</summary>
        internal static void SetNumberFormat(CultureInfo culture, NumberFormatInfo? value)
        {
            if (value is null)
                throw NeoRuntimeError.ArgumentNull("value");
            if (culture.IsReadOnly)
                throw NeoRuntimeError.ReadOnly();
            culture.NumberFormat = value;
        }

        private static NeoRuntimeError CultureNotFound(string name) => new(
            NeoErrorClass.NeoCultureNotFoundError,
            "Only the invariant culture is supported in globalization-invariant mode. See https://aka.ms/GlobalizationInvariantMode for more information. (Parameter 'name')\n"
                + name
                + " is an invalid culture identifier.");

        private static string AsciiLower(string s)
        {
            var result = new StringBuilder(s.Length);
            foreach (char c in s)
                result.Append(c >= 'A' && c <= 'Z' ? (char)(c + 0x20) : c);
            return result.ToString();
        }
    }
}
