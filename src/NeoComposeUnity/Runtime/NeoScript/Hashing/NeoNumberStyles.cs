// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NeoCompose.Runtime.NeoScript.Hashing
{
    /// <summary>
    /// P127 §7.4's <c>NumberStyles</c>: the fixed option table and .NET 10's
    /// flags <c>ToString</c> (Enum.cs <c>FormatFlagNames</c>). A
    /// <c>NumberStyles</c> value is its number at runtime. The compose twin is
    /// in <c>hashing/enums.ts</c>.
    /// </summary>
    internal static class NeoNumberStyles
    {
        /// <summary><c>System.Globalization.NumberStyles</c>' members and .NET 10 values, in declaration order.</summary>
        internal static readonly IReadOnlyList<KeyValuePair<string, int>> Members = new[]
        {
            new KeyValuePair<string, int>("None", 0),
            new KeyValuePair<string, int>("AllowLeadingWhite", 1),
            new KeyValuePair<string, int>("AllowTrailingWhite", 2),
            new KeyValuePair<string, int>("AllowLeadingSign", 4),
            new KeyValuePair<string, int>("AllowTrailingSign", 8),
            new KeyValuePair<string, int>("AllowParentheses", 16),
            new KeyValuePair<string, int>("AllowDecimalPoint", 32),
            new KeyValuePair<string, int>("AllowThousands", 64),
            new KeyValuePair<string, int>("AllowExponent", 128),
            new KeyValuePair<string, int>("AllowCurrencySymbol", 256),
            new KeyValuePair<string, int>("AllowHexSpecifier", 512),
            new KeyValuePair<string, int>("AllowBinarySpecifier", 1024),
            new KeyValuePair<string, int>("Integer", 7),
            new KeyValuePair<string, int>("HexNumber", 515),
            new KeyValuePair<string, int>("BinaryNumber", 1027),
            new KeyValuePair<string, int>("Number", 111),
            new KeyValuePair<string, int>("Float", 167),
            new KeyValuePair<string, int>("Currency", 383),
            new KeyValuePair<string, int>("Any", 511),
        };

        /// <summary>
        /// Each <c>NumberStyles</c> option id's member name. The system-schema
        /// phase mints the ids; this is the only place they appear.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, string> OptionNames = new Dictionary<string, string>();

        private static readonly Dictionary<string, int> ValuesByName = Members.ToDictionary(member => member.Key, member => member.Value);

        /// <summary>The members sorted by unsigned value, as <c>EnumInfo</c> holds them.</summary>
        private static readonly KeyValuePair<string, int>[] Sorted = Members.OrderBy(member => (uint)member.Value).ToArray();

        /// <summary>The number a <c>NumberStyles</c> option literal evaluates to, or null for another id.</summary>
        internal static int? OptionValue(string optionId)
        {
            if (!OptionNames.TryGetValue(optionId, out string? name))
                return null;
            return ValuesByName[name];
        }

        /// <summary><c>NumberStyles.ToString(format)</c>, .NET's flags format for <c>G</c> and <c>F</c>.</summary>
        internal static string Format(int value, string? format)
        {
            char letter = NeoEnums.FormatLetter(format);
            if (letter == 'x')
                return NeoEnums.Hex8(value);
            if (letter != 'd')
            {
                string? names = FormatFlagNames(value);
                if (names is not null)
                    return names;
            }
            return NeoEnums.Int32Text(value);
        }

        /// <summary>
        /// <c>FormatFlagNames</c>: the one member equal to <paramref name="value"/>,
        /// else the members that cover it exactly, from the largest, printed
        /// smallest first; null when no combination does.
        /// </summary>
        private static string? FormatFlagNames(int value)
        {
            uint resultValue = unchecked((uint)value);
            if (resultValue == 0)
                return Sorted[0].Key;
            int index = Sorted.Length - 1;
            for (; index >= 0; index--)
            {
                uint current = (uint)Sorted[index].Value;
                if (current <= resultValue)
                {
                    if (current == resultValue)
                        return Sorted[index].Key;
                    break;
                }
            }
            uint remaining = resultValue;
            var found = new List<string>();
            for (; index >= 0; index--)
            {
                uint current = (uint)Sorted[index].Value;
                if (index == 0 && current == 0)
                    break;
                if ((remaining & current) == current)
                {
                    remaining &= ~current;
                    found.Add(Sorted[index].Key);
                    if (remaining == 0)
                        break;
                }
            }
            if (remaining != 0)
                return null;
            var text = new StringBuilder();
            for (int i = found.Count - 1; i >= 0; i--)
            {
                if (text.Length > 0)
                    text.Append(", ");
                text.Append(found[i]);
            }
            return text.ToString();
        }
    }
}
