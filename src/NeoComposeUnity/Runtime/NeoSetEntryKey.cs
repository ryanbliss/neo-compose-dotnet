// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Globalization;
using System.Collections.Generic;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// First entry per key, retaining collisions caused by translated tuple text.
    /// Only colliding keys allocate an ordered bucket; removal stays constant-time.
    /// </summary>
    internal sealed class NeoSetEntryIndex
    {
        private readonly Dictionary<string, string> firstByKey;
        private Dictionary<string, OrderedMap<string, string>>? collisions;

        internal NeoSetEntryIndex(int capacity) => firstByKey = new(capacity, StringComparer.Ordinal);

        internal bool TryGetValue(string key, out string value) => firstByKey.TryGetValue(key, out value);

        internal void Add(string key, string id)
        {
            if (firstByKey.TryAdd(key, id) || firstByKey[key] == id)
                return;
            collisions ??= new(StringComparer.Ordinal);
            if (!collisions.TryGetValue(key, out var entries))
            {
                entries = new OrderedMap<string, string>(StringComparer.Ordinal);
                entries.Add(firstByKey[key], firstByKey[key]);
                collisions.Add(key, entries);
            }
            entries.Add(id, id);
        }

        internal void Remove(string key, string id)
        {
            if (collisions is not null && collisions.TryGetValue(key, out var entries))
            {
                entries.Remove(id);
                // A collision bucket starts with at least two entries.
                foreach (string remaining in entries.Values)
                {
                    firstByKey[key] = remaining;
                    break;
                }
                if (entries.Count == 1)
                    collisions.Remove(key);
            }
            else if (firstByKey.TryGetValue(key, out string holder) && holder == id)
                firstByKey.Remove(key);
        }

        internal void Clear()
        {
            firstByKey.Clear();
            collisions?.Clear();
        }
    }

    /// <summary>
    /// The P108 §3 key of one Set entry: the value id for Class entries and a
    /// canonical encoding of the stored value for every value kind. Two
    /// entries of one Set are equal iff their keys are equal. Mirrors the
    /// TypeScript <c>setEntryKey</c>; <c>NeoSetEntryKeyParityFixture</c> pins
    /// both.
    /// </summary>
    internal static class NeoSetEntryKey
    {
        /// <summary>
        /// The key of a stored entry. <paramref name="entryMember"/> must be
        /// the resolved entry member: a generic entry is substituted with its
        /// closed binding first.
        /// </summary>
        internal static string Of(Member entryMember, MemberValue row, NeoClient? client = null, Func<string, MemberValue?>? readRow = null)
        {
            if (entryMember is null)
                throw new ArgumentNullException(nameof(entryMember));
            if (row is null)
                throw new ArgumentNullException(nameof(row));
            switch (entryMember)
            {
                case TupleMember tuple:
                    return TupleFragment(tuple, row, client, readRow).ToString(Formatting.None);
                case ClassMember:
                    return row.id;
                case BoolMember:
                    return Require(row as BoolMemberValue, entryMember).value switch
                    {
                        true => "true",
                        false => "false",
                        null => throw new InvalidOperationException(
                            $"NeoSetEntryKey: '{entryMember.name}' entry '{row.id}' has no stored boolean."),
                    };
                case IntMember:
                    return IntKey(RequireNumber(row, entryMember));
                case FloatMember:
                    return Float32Key(RequireNumber(row, entryMember));
                case DecimalMember:
                    return DecimalKey(RequireString(row, entryMember));
                case StringMember text:
                    if (text.Format == NeoStringFormatKind.Localized)
                    {
                        throw new InvalidOperationException(
                            $"NeoSetEntryKey: '{entryMember.name}' is a localized string; Set<string> elements must be @settings(format: .Plain).");
                    }
                    return RequireString(row, entryMember);
                case EnumMember:
                    return string.Join(",", Require(row as ArrayMemberValue, entryMember).value ?? Array.Empty<string>());
                case Vector2Member:
                    {
                        NeoVector2Value vector = RequireValue(Require(row as Vector2MemberValue, entryMember).value, entryMember);
                        return Float32Key(vector.x) + "," + Float32Key(vector.y);
                    }
                case Vector2IntMember:
                    {
                        NeoVector2Value vector = RequireValue(Require(row as Vector2MemberValue, entryMember).value, entryMember);
                        return IntKey(vector.x) + "," + IntKey(vector.y);
                    }
                case Vector3Member:
                    {
                        NeoVector3Value vector = RequireValue(Require(row as Vector3MemberValue, entryMember).value, entryMember);
                        return Float32Key(vector.x) + "," + Float32Key(vector.y) + "," + Float32Key(vector.z);
                    }
                case Vector3IntMember:
                    {
                        NeoVector3Value vector = RequireValue(Require(row as Vector3MemberValue, entryMember).value, entryMember);
                        return IntKey(vector.x) + "," + IntKey(vector.y) + "," + IntKey(vector.z);
                    }
                case ColorMember:
                    {
                        NeoColorValue color = RequireValue(Require(row as ColorMemberValue, entryMember).value, entryMember);
                        return Float32Key(color.r) + "," + Float32Key(color.g) + "," + Float32Key(color.b) + "," + Float32Key(color.a);
                    }
                case SpriteMember:
                    {
                        SpriteValue sprite = RequireValue(Require(row as SpriteMemberValue, entryMember).value, entryMember);
                        return sprite.fileId + ":" + sprite.sliceIndex.ToString(CultureInfo.InvariantCulture);
                    }
                case AudioMember:
                    return RequireValue(Require(row as FileMemberValue, entryMember).value, entryMember).fileId;
                default:
                    throw new InvalidOperationException(
                        $"NeoSetEntryKey: '{entryMember.name}' is a {entryMember.GetType().Name}, which is not a Set element kind.");
            }
        }

        /// <summary>
        /// The key a value-kind entry built from <paramref name="payload"/>
        /// would have. Class entries key by value id, so callers resolve them
        /// before reaching here.
        /// </summary>
        internal static string OfPayload(Member entryMember, object? payload, NeoClient? client = null, Func<string, MemberValue?>? readRow = null)
        {
            if (entryMember is ClassMember)
            {
                throw new InvalidOperationException(
                    $"NeoSetEntryKey: Class entry '{entryMember.name}' keys by value id, not by payload.");
            }
            if (payload is NeoValuePayload wrapped)
            {
                var rows = new Dictionary<string, MemberValue>(StringComparer.Ordinal);
                foreach (MemberValue row in wrapped.valueRows)
                    rows[row.id] = row;
                Func<string, MemberValue?>? fallback = readRow;
                readRow = id => rows.TryGetValue(id, out MemberValue row) ? row : fallback is null ? client?.ResolveValueRow(id) : fallback(id);
                payload = wrapped.value;
            }
            return Of(entryMember, MemberValueFactory.Create(entryMember, payload, string.Empty, default, default), client, readRow);
        }

        private static bool IsHandle(Member member) => member is ClassMember or ListMember or DictionaryMember
            || member is LookupMember { Selection: NeoMemberSelectionKind.Multi }
            || member is DialogueLookupMember { Selection: NeoMemberSelectionKind.Multi }
            || member is EnumMember { Selection: NeoMemberSelectionKind.Multi };

        private static JToken TupleFragment(TupleMember tuple, MemberValue row, NeoClient? client, Func<string, MemberValue?>? readRow)
        {
            if (client is null)
                throw new InvalidOperationException("Tuple set keys require a client graph.");
            if (Require(row as ArrayMemberValue, tuple).value is not { } ids)
                return JValue.CreateNull();
            if (ids.Length != tuple.elementMemberIds.Length)
                throw new InvalidOperationException($"Tuple '{tuple.name}' has the wrong arity.");
            var parts = new JArray();
            for (int index = 0; index < ids.Length; index++)
            {
                Member element = client.TryResolveTupleElementMember(tuple, row, index)
                    ?? throw new InvalidOperationException($"Tuple '{tuple.name}' has no element {index + 1}.");
                MemberValue child = (readRow is null ? client.ResolveValueRow(ids[index]) : readRow(ids[index]))
                    ?? throw new InvalidOperationException($"Tuple '{tuple.name}' has no child '{ids[index]}'.");
                JToken key = NeoGeneratedTypesSupport.IsNullStoredValue(child) ? JValue.CreateNull()
                    : element is TupleMember nested ? TupleFragment(nested, child, client, readRow)
                    : IsHandle(element) ? new JValue(child.id)
                    : element is LookupMember or DialogueLookupMember ? JToken.FromObject(((ArrayMemberValue)child).value!)
                    : element is StringMember text ? new JValue(NeoMemberString.ResolveText(client, text, Require(child as StringMemberValue, element)))
                    : new JValue(Of(element, child, client, readRow));
                parts.Add(new JArray((int)element.kind, key));
            }
            return parts;
        }

        internal static string OfRuntimeTuple(TupleMember member, RuntimeTuple tuple, NSGetterEvaluator.Context context, MemberValue? collectionRow = null)
            => RuntimeTupleFragment(member, tuple, context, collectionRow).ToString(Formatting.None);

        private static JArray RuntimeTupleFragment(TupleMember member, RuntimeTuple tuple, NSGetterEvaluator.Context context, MemberValue? collectionRow)
        {
            if (tuple.Count != member.elementMemberIds.Length)
                throw new InvalidOperationException($"Tuple '{member.name}' has the wrong arity.");
            var parts = new JArray();
            for (int index = 0; index < tuple.Count; index++)
            {
                Member element = context.client.TryResolveTupleElementMember(member, collectionRow, index)
                    ?? throw new InvalidOperationException($"Tuple '{member.name}' has no element {index + 1}.");
                object? value = tuple[index];
                JToken key;
                if (value is null)
                    key = JValue.CreateNull();
                else if (element is TupleMember nested && value is RuntimeTuple nestedValue)
                    key = RuntimeTupleFragment(nested, nestedValue, context, collectionRow);
                else if (IsHandle(element))
                    key = new JValue(NSGetterEvaluator.ReferenceKeyOf(value, context) as string ?? "<unattached>");
                else if (element is LookupMember or DialogueLookupMember)
                {
                    string? id = value is NSSlotReference slot ? slot.TargetId : NSGetterEvaluator.ReferenceKeyOf(value, context) as string;
                    key = id is null ? new JArray() : new JArray(id);
                }
                // Tuple strings compare the text a read produces, including
                // localized elements. Direct Set<string> keeps its Plain rule.
                else if (element is StringMember)
                    key = value is string text ? new JValue(text)
                        : throw new InvalidOperationException($"Tuple '{member.name}' element {index + 1} requires a string.");
                else
                    key = new JValue(OfPayload(element, value, context.client));
                parts.Add(new JArray((int)element.kind, key));
            }
            return parts;
        }

        private static double RequireNumber(MemberValue row, Member entryMember) =>
            Require(row as NumberMemberValue, entryMember).value
                ?? throw new InvalidOperationException(
                    $"NeoSetEntryKey: '{entryMember.name}' entry '{row.id}' has no stored number.");

        private static string RequireString(MemberValue row, Member entryMember) =>
            Require(row as StringMemberValue, entryMember).value
                ?? throw new InvalidOperationException(
                    $"NeoSetEntryKey: '{entryMember.name}' entry '{row.id}' has no stored string.");

        private static TRow Require<TRow>(TRow? row, Member entryMember)
            where TRow : MemberValue =>
            row ?? throw new InvalidOperationException(
                $"NeoSetEntryKey: '{entryMember.name}' entry is not a {typeof(TRow).Name}.");

        private static TValue RequireValue<TValue>(TValue? value, Member entryMember)
            where TValue : class =>
            value ?? throw new InvalidOperationException(
                $"NeoSetEntryKey: '{entryMember.name}' entry has no stored {typeof(TValue).Name}.");

        private static string IntKey(double value) =>
            ((long)value).ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// The stored float32's bit pattern as a signed integer, so the
        /// TypeScript mirror produces the same text without sharing a float
        /// formatter. <c>0</c> and <c>-0</c> share a key.
        /// </summary>
        private static string Float32Key(double value)
        {
            float single = (float)value;
            if (single == 0f)
                single = 0f;
            return BitConverter.SingleToInt32Bits(single).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Canonical decimal string: <c>1.0</c>, <c>1.00</c>, and <c>1</c> share a key.</summary>
        internal static string DecimalKey(string value)
        {
            string text = value;
            if (text.IndexOf('.') >= 0 && text.EndsWith("0", StringComparison.Ordinal))
            {
                text = text.TrimEnd('0');
                if (text.EndsWith(".", StringComparison.Ordinal))
                    text = text.Substring(0, text.Length - 1);
            }
            return text == "-0" ? "0" : text;
        }
    }
}
