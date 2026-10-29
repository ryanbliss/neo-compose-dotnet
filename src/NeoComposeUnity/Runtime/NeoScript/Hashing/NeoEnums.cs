// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Runtime.NeoScript.Hashing
{
    /// <summary>An enum option: its id and identifier name.</summary>
    internal readonly struct NeoEnumOption
    {
        internal NeoEnumOption(string id, string name)
        {
            Id = id;
            Name = name;
        }

        internal string Id
        {
            get;
        }

        internal string Name
        {
            get;
        }
    }

    /// <summary><c>HasFlag</c> and <c>CompareTo(object?)</c>'s argument: its type and, for an enum, its number.</summary>
    internal readonly struct NeoEnumArgument
    {
        internal NeoEnumArgument(bool sameType, string typeName, int value)
        {
            SameType = sameType;
            TypeName = typeName;
            Value = value;
        }

        /// <summary>The receiver's own enum type.</summary>
        internal bool SameType
        {
            get;
        }

        /// <summary>The argument's type name, for the type-mismatch message.</summary>
        internal string TypeName
        {
            get;
        }

        /// <summary>The argument's number when <see cref="SameType"/>.</summary>
        internal int Value
        {
            get;
        }
    }

    /// <summary>An enum's options, indexed once; the evaluator keeps one per enum type.</summary>
    internal sealed class NeoEnumOptions
    {
        private readonly Dictionary<string, int> indexById = new Dictionary<string, int>();
        private readonly string[] names;

        internal NeoEnumOptions(IReadOnlyList<NeoEnumOption> options)
        {
            names = new string[options.Count];
            for (int i = 0; i < options.Count; i++)
            {
                indexById[options[i].Id] = i;
                names[i] = options[i].Name;
            }
        }

        /// <summary>The declaration index, or the orphan number of an undeclared id.</summary>
        internal int NumericValue(string optionId) =>
            indexById.TryGetValue(optionId, out int index) ? index : NeoEnums.OrphanValue(optionId);

        /// <summary>The identifier name, or an orphan's id.</summary>
        internal string Name(string optionId) =>
            indexById.TryGetValue(optionId, out int index) ? names[index] : optionId;

        /// <summary><c>ToString(format)</c>.</summary>
        internal string Format(string optionId, string? format) =>
            NeoEnums.Format(NumericValue(optionId), Name(optionId), format);
    }

    /// <summary>
    /// P127 §8.5 and §8.6: enum numbers, formats, <c>HasFlag</c> and
    /// <c>CompareTo</c>, and <c>bool.CompareTo</c>, from .NET 10's Enum.cs and
    /// Boolean.cs (dotnet/runtime release/10.0 at
    /// 8f4c012be1d11eef2a7c1991cdb6302479f23b74). The compose twin is
    /// <c>hashing/enums.ts</c>.
    /// </summary>
    internal static class NeoEnums
    {
        private const string Hex = "0123456789ABCDEF";

        /// <summary>
        /// An undeclared option id's number: <c>-1 - (FNV-1a(id) mod 2147483647)</c>.
        /// FNV-1a is the 32-bit variant over UTF-16 code units (offset basis
        /// 0x811C9DC5, prime 0x01000193), as P103's member numbers hash.
        /// </summary>
        internal static int OrphanValue(string optionId)
        {
            uint hash = 0x811C9DC5;
            foreach (char c in optionId)
                hash = unchecked((hash ^ c) * 0x01000193);
            return -1 - (int)(hash % 0x7FFFFFFFu);
        }

        /// <summary>
        /// <c>Enum.ToString(format)</c> for a single-option enum: <c>G</c> and
        /// <c>F</c> print <paramref name="name"/>, <c>D</c> the number,
        /// <c>X</c> eight hex digits; null or <c>""</c> is <c>G</c>,
        /// case-insensitive, and anything else throws.
        /// </summary>
        internal static string Format(int value, string name, string? format)
        {
            char letter = FormatLetter(format);
            if (letter == 'd')
                return Int32Text(value);
            if (letter == 'x')
                return Hex8(value);
            return name;
        }

        /// <summary><c>Enum.HasFlag(flag)</c>: <c>(value &amp; flag) == flag</c>; null and another enum type throw.</summary>
        internal static bool HasFlag(int value, string enumTypeName, NeoEnumArgument? flag)
        {
            if (flag is null)
                throw NeoRuntimeError.ArgumentNull("flag");
            NeoEnumArgument argument = flag.Value;
            if (!argument.SameType)
                throw NeoHashingErrors.EnumFlagType(argument.TypeName, enumTypeName);
            return (value & argument.Value) == argument.Value;
        }

        /// <summary><c>Enum.CompareTo</c> between two values of one enum type, by number.</summary>
        internal static int CompareTo(int value, int other) => value < other ? -1 : value > other ? 1 : 0;

        /// <summary><c>Enum.CompareTo(object?)</c>: by number; 1 for null, and another type throws.</summary>
        internal static int CompareToObject(int value, string enumTypeName, NeoEnumArgument? target)
        {
            if (target is null)
                return 1;
            NeoEnumArgument argument = target.Value;
            if (!argument.SameType)
                throw NeoHashingErrors.EnumCompareType(argument.TypeName, enumTypeName);
            return CompareTo(value, argument.Value);
        }

        /// <summary><c>bool.CompareTo(bool)</c>: <c>false &lt; true</c>.</summary>
        internal static int BoolCompareTo(bool value, bool other)
        {
            if (value == other)
                return 0;
            return value ? 1 : -1;
        }

        /// <summary><c>bool.CompareTo(object?)</c>: <paramref name="target"/> is the argument when it is a bool; 1 for null, else throws.</summary>
        internal static int BoolCompareToObject(bool value, bool? target, bool argumentIsNull)
        {
            if (argumentIsNull)
                return 1;
            if (target is null)
                throw NeoHashingErrors.MustBeBoolean();
            return BoolCompareTo(value, target.Value);
        }

        /// <summary>A format's letter, lowercased as .NET's <c>format | 0x20</c> does; <c>'g'</c> for null or <c>""</c>.</summary>
        internal static char FormatLetter(string? format)
        {
            if (format is null || format.Length == 0)
                return 'g';
            if (format.Length != 1)
                throw NeoHashingErrors.InvalidEnumFormat();
            char letter = (char)(format[0] | 0x20);
            if (letter == 'g' || letter == 'd' || letter == 'x' || letter == 'f')
                return letter;
            throw NeoHashingErrors.InvalidEnumFormat();
        }

        /// <summary>The int32's decimal digits with a leading <c>-</c> when negative, as invariant <c>D</c> prints them.</summary>
        internal static string Int32Text(int value)
        {
            var chars = new char[11];
            int position = chars.Length;
            uint magnitude = value < 0 ? unchecked(0u - (uint)value) : (uint)value;
            do
            {
                chars[--position] = (char)('0' + (magnitude % 10));
                magnitude /= 10;
            }
            while (magnitude != 0);
            if (value < 0)
                chars[--position] = '-';
            return new string(chars, position, chars.Length - position);
        }

        /// <summary>The int32's eight uppercase hex digits, two's complement.</summary>
        internal static string Hex8(int value)
        {
            var chars = new char[8];
            uint bits = unchecked((uint)value);
            for (int i = 7; i >= 0; i--)
            {
                chars[i] = Hex[(int)(bits & 0xF)];
                bits >>= 4;
            }
            return new string(chars);
        }
    }
}
