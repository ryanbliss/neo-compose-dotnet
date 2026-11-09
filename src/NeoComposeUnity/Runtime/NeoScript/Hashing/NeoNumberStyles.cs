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
        private static readonly IReadOnlyDictionary<string, string> OptionNames = new Dictionary<string, string>
        {
            ["system_353e00f4-9487-48d9-9d58-5a4e29af144c"] = "None",
            ["system_0fb63b11-39f0-4d1b-90a3-3f7c4766f23e"] = "AllowLeadingWhite",
            ["system_8aaebbc1-62ff-4687-a8ac-ecdacca93270"] = "AllowTrailingWhite",
            ["system_d504bcba-4d78-49a6-9c6a-33fc886905e6"] = "AllowLeadingSign",
            ["system_8e987bb5-1f0d-428e-a96c-7bceac1b8185"] = "AllowTrailingSign",
            ["system_c1358680-dd6a-4248-b6bf-9cf08c427689"] = "AllowParentheses",
            ["system_8169ea11-38f7-49ae-ac38-1d9dedd5977c"] = "AllowDecimalPoint",
            ["system_f34e8a81-9140-4164-9204-0659ef9a1952"] = "AllowThousands",
            ["system_62f5ecd2-ba94-44b0-9cc0-4387c974d328"] = "AllowExponent",
            ["system_890178e8-f9af-44be-bb12-215e947a5101"] = "AllowCurrencySymbol",
            ["system_6988ebd0-5cad-4a78-8a22-b295c4e6d02f"] = "AllowHexSpecifier",
            ["system_14596028-8fde-4b57-a75d-452f15e4dfb6"] = "AllowBinarySpecifier",
            ["system_a82aef72-4cef-4058-8f4d-d3e9c2757ff9"] = "Integer",
            ["system_cbd2f147-00e1-4d27-9057-eeb7db3e2b7f"] = "HexNumber",
            ["system_a4d592fb-dc19-454a-b5aa-9a63ac7f390e"] = "BinaryNumber",
            ["system_e26f1735-2b1b-4eca-9c65-fff6c0ccb8b2"] = "Number",
            ["system_4b1dbcc8-014f-4877-9d6c-850d11adc9be"] = "Float",
            ["system_6ae18f0a-9cd9-490d-be80-03ed6b8926b8"] = "Currency",
            ["system_d6b40f4a-31da-4863-bb6a-9082f587b527"] = "Any",
        };

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
