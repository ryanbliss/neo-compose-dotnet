// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Writes floats and decimals in their stored forms (P127 §12.1, §3.4): a
    /// float through <see cref="NeoStoredFloat"/>, so NaN, the infinities and
    /// <c>-0</c> write as their symbol strings, and a decimal as its canonical
    /// string, which keeps every digit, its scale and a zero's sign. Reads
    /// stay default: a string is a float or a decimal only where the slot's
    /// type says so.
    /// </summary>
    internal sealed class NeoStoredNumberJsonConverter : JsonConverter
    {
        /// <summary>
        /// Serializes runtime values with every CLR decimal as its canonical
        /// string. A runtime double is a float only at a float position, so
        /// <see cref="WriteTyped"/> writes floats by position and this leaves
        /// other doubles as numbers.
        /// </summary>
        internal static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Converters = new List<JsonConverter> { new NeoStoredNumberJsonConverter(floats: false) },
        });

        private readonly bool floats;

        /// <summary>Writes floats and decimals; the form a float-typed property declares.</summary>
        public NeoStoredNumberJsonConverter()
            : this(floats: true)
        {
        }

        private NeoStoredNumberJsonConverter(bool floats)
        {
            this.floats = floats;
        }

        public override bool CanRead => false;

        public override bool CanConvert(Type objectType) =>
            objectType == typeof(decimal) || floats && (objectType == typeof(double) || objectType == typeof(float));

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            throw new NotSupportedException("NeoStoredNumberJsonConverter only writes.");
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            switch (value)
            {
                case double number:
                    NeoStoredFloat.Write(writer, number);
                    return;
                case float single:
                    NeoStoredFloat.Write(writer, single);
                    return;
                case decimal exact:
                    writer.WriteValue(NeoDecimalValues.Format(exact));
                    return;
                default:
                    serializer.Serialize(writer, value);
                    return;
            }
        }

        /// <summary>
        /// Whether a value of <paramref name="type"/> can hold a float or a
        /// decimal once its type parameters close: such a value's stored form
        /// is read back by its type.
        /// </summary>
        internal static bool MayHoldStoredNumber(TypeInfo? type) => type?.type switch
        {
            MemberKind.Float or MemberKind.Decimal or MemberKind.Generic => true,
            MemberKind.List or MemberKind.Set or MemberKind.Dictionary => MayHoldStoredNumber(EntryOf(type)),
            MemberKind.Tuple => TupleTypeInfo.From(type) is { } tuple
                && Array.Exists(tuple.elements, element => MayHoldStoredNumber(element.typeInfo)),
            _ => false,
        };

        /// <summary>
        /// Writes a runtime value at a position of <paramref name="type"/>:
        /// a float through <see cref="NeoStoredFloat"/>, a decimal as its
        /// canonical string, and list, set and dictionary entries by their
        /// entry type. Tuples write their elements by their own type. Any
        /// other value writes as <paramref name="serializer"/> writes it.
        /// </summary>
        internal static void WriteTyped(JsonWriter writer, object? value, TypeInfo? type, JsonSerializer serializer)
        {
            switch (type?.type)
            {
                case MemberKind.Float when value is double number:
                    NeoStoredFloat.Write(writer, number);
                    return;
                case MemberKind.Float when value is float single:
                    NeoStoredFloat.Write(writer, single);
                    return;
                case MemberKind.Decimal when value is decimal exact:
                    writer.WriteValue(NeoDecimalValues.Format(exact));
                    return;
                case MemberKind.List or MemberKind.Set
                    when value is IEnumerable entries and not string and not IDictionary
                        && MayHoldStoredNumber(EntryOf(type)):
                    writer.WriteStartArray();
                    foreach (object? entry in entries)
                        WriteTyped(writer, entry, EntryOf(type), serializer);
                    writer.WriteEndArray();
                    return;
                case MemberKind.Dictionary
                    when value is IDictionary<string, object?> fields && MayHoldStoredNumber(EntryOf(type)):
                    writer.WriteStartObject();
                    foreach (KeyValuePair<string, object?> field in fields)
                    {
                        writer.WritePropertyName(field.Key);
                        WriteTyped(writer, field.Value, EntryOf(type), serializer);
                    }
                    writer.WriteEndObject();
                    return;
                default:
                    serializer.Serialize(writer, value);
                    return;
            }
        }

        /// <summary>
        /// Reads a value stored at a position of <paramref name="type"/> back
        /// to its runtime form, through list, set and dictionary entries and
        /// tuple elements: a float's symbol string becomes its double, and a
        /// decimal is its canonical string. Returns <paramref name="value"/>
        /// itself when nothing changes, and never writes into it.
        /// </summary>
        internal static object? ReadTyped(object? value, TypeInfo? type)
        {
            switch (type?.type)
            {
                case MemberKind.Float when value is string text && NeoStoredFloat.IsSymbol(text):
                    return NeoStoredFloat.DecodeSymbol(text);
                // An SDK before P127 wrote a CLR decimal as its double.
                case MemberKind.Decimal when value is double or long:
                    return NeoDecimalValues.Format(Convert.ToDecimal(value));
                case MemberKind.List or MemberKind.Set when value is object?[] entries:
                    return ReadEntries(entries, EntryOf(type));
                case MemberKind.Dictionary when value is IDictionary<string, object?> fields:
                    {
                        TypeInfo? entryType = EntryOf(type);
                        Dictionary<string, object?>? read = null;
                        foreach (KeyValuePair<string, object?> field in fields)
                        {
                            object? entry = ReadTyped(field.Value, entryType);
                            if (read is null && !ReferenceEquals(entry, field.Value))
                                read = new Dictionary<string, object?>(fields);
                            if (read is not null)
                                read[field.Key] = entry;
                        }
                        return read ?? value;
                    }
                case MemberKind.Tuple
                    when value is IDictionary<string, object?> record
                        && record.TryGetValue("elements", out object? stored)
                        && stored is object?[] elements
                        && TupleTypeInfo.From(type) is { } tuple
                        && tuple.elements.Length == elements.Length:
                    {
                        object?[]? read = null;
                        for (int index = 0; index < elements.Length; index++)
                        {
                            object? element = ReadTyped(elements[index], tuple.elements[index].typeInfo);
                            if (read is null && !ReferenceEquals(element, elements[index]))
                                read = (object?[])elements.Clone();
                            if (read is not null)
                                read[index] = element;
                        }
                        return read is null ? value : new Dictionary<string, object?> { ["elements"] = read };
                    }
                default:
                    return value;
            }
        }

        private static object?[] ReadEntries(object?[] entries, TypeInfo? entryType)
        {
            object?[]? read = null;
            for (int index = 0; index < entries.Length; index++)
            {
                object? entry = ReadTyped(entries[index], entryType);
                if (read is null && !ReferenceEquals(entry, entries[index]))
                    read = (object?[])entries.Clone();
                if (read is not null)
                    read[index] = entry;
            }
            return read ?? entries;
        }

        private static TypeInfo? EntryOf(TypeInfo? type) => type switch
        {
            CollectionTypeInfo collection => collection.entryTypeInfo,
            FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
            _ => null,
        };
    }
}
