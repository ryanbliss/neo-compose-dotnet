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
        internal static string Of(Member entryMember, MemberValue row, NeoClient client, Func<string, MemberValue?>? readRow = null)
        {
            if (entryMember is null)
                throw new ArgumentNullException(nameof(entryMember));
            if (row is null)
                throw new ArgumentNullException(nameof(row));
            return EntryKey(entryMember, row, client, readRow);
        }

        private static string EntryKey(
            Member entryMember,
            MemberValue row,
            NeoClient client,
            Func<string, MemberValue?>? readRow)
        {
            // P127 §9.5: every null at a reference-type position, boxed or
            // not, is one key, so a Set holds at most one null, as HashSet<T> does.
            if (IsNullEntry(row))
                return NullEntryKey;
            switch (entryMember)
            {
                case TupleMember tuple:
                    return TupleFragment(tuple, row, client, readRow).ToString(Formatting.None);
                // Both Class arms key a row by id (P113 §2.1); an interface
                // slot keys a box by what it holds (P127 §9.5).
                case ClassMember:
                    return row.valueType is TypeBinding classSlotType
                        ? BoxKey(row, classSlotType, client, readRow)
                        : row.id;
                case ObjectMember objectMember:
                    return ObjectEntryKey(objectMember, row, client, readRow);
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
                    return Float32Key(RequireFloat(row, entryMember));
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
        /// <param name="contextRow">The collection row a tuple entry would join, with its member: the entry reads its element types through the typeSource it would take there.</param>
        internal static string OfPayload(
            Member entryMember,
            object? payload,
            NeoClient client,
            Func<string, MemberValue?>? readRow = null,
            (MemberValue Row, Member Member)? contextRow = null)
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
                readRow = id => rows.TryGetValue(id, out MemberValue row) ? row : fallback is null ? client.ResolveValueRow(id) : fallback(id);
                // A box keeps its envelope: its held type decides its key (P127 §9.5).
                if (entryMember is not ObjectMember)
                    payload = wrapped.value;
            }
            var temporaryRow = MemberValueFactory.Create(entryMember, payload, string.Empty, default, default);
            if (entryMember is TupleMember && contextRow is { } context)
                temporaryRow.typeSource = NeoTypeSources.EntryTypeSource(client, context.Row, context.Member, temporaryRow);
            return Of(entryMember, temporaryRow, client, readRow);
        }

        /// <summary>Row ids and box keys (which hold <c>|</c>) never take this form.</summary>
        internal const string NullEntryKey = "null";

        /// <summary>A row an <c>object</c> or interface slot holds: it keys by its id.</summary>
        private static bool IsHeldRow(MemberValue row) => row is ObjectMemberValue { classId: string };

        private static bool IsNullEntry(MemberValue row) =>
            !IsHeldRow(row) && NeoGeneratedTypesSupport.IsNullStoredValue(row);

        /// <summary>
        /// P127 §9.5. An <c>object</c> slot's entry: a held row by its id, a
        /// box by its type key and payload key, a declared built-in value by
        /// §12.1's key, and null as one key, as <c>HashSet&lt;object&gt;</c>
        /// holds one null.
        /// </summary>
        private static string ObjectEntryKey(
            ObjectMember entryMember,
            MemberValue row,
            NeoClient client,
            Func<string, MemberValue?>? readRow)
        {
            if (IsHeldRow(row))
                return row.id;
            if (row.valueType is TypeBinding valueType)
                return BoxKey(row, valueType, client, readRow);
            return BuiltInValueKey(entryMember.objectType, row, client) ?? row.id;
        }

        /// <summary>
        /// P127 §9.5. A box keys by its held type's canonical binding key,
        /// a <c>|</c>, and P108's key for that type's payload. A payload whose
        /// equality is identity (a collection, a <c>NumberFormatInfo</c>,
        /// <c>new object()</c>) keys by its row, as a held class row does.
        /// </summary>
        private static string BoxKey(
            MemberValue row,
            TypeBinding valueType,
            NeoClient client,
            Func<string, MemberValue?>? readRow)
        {
            NeoBoxForm form = client.TypeRules.Box(valueType);
            string? key = form.Kind switch
            {
                NeoBoxFormKind.Invalid => throw new InvalidOperationException(
                    $"NeoSetEntryKey: entry '{row.id}' holds no value: {form.Invalid}."),
                NeoBoxFormKind.Comparer => ComparerKey(row, client),
                // `new object()`'s equality is identity (§1.1).
                NeoBoxFormKind.Identity => null,
                _ => PayloadKey(form.Member!, row, client, readRow),
            };
            return key is null ? row.id : client.TypeRules.CanonicalKeyOf(valueType) + "|" + key;
        }

        /// <summary>A box payload's P108 key, or null when its equality is identity.</summary>
        private static string? PayloadKey(
            Member member,
            MemberValue row,
            NeoClient client,
            Func<string, MemberValue?>? readRow)
        {
            switch (member)
            {
                // A boxed enum keys by its option id.
                case EnumMember:
                    return row is StringMemberValue { value: string option }
                        ? option
                        : string.Join(",", Require(row as ArrayMemberValue, member).value ?? Array.Empty<string>());
                // A held reference is its target, as a slot reference keys; a
                // multi-select one is a Set, whose equality is identity.
                case LookupMember lookup:
                    return lookup.Selection == NeoMemberSelectionKind.Multi ? null : ReferenceTargetKey(row, member);
                case DialogueLookupMember dialogue:
                    return dialogue.Selection == NeoMemberSelectionKind.Multi ? null : ReferenceTargetKey(row, member);
                case ObjectMember objectMember:
                    return BuiltInValueKey(objectMember.objectType, row, client);
                // Delegate equality is P127 §9.7's, which a key can't decide.
                case DelegateMember:
                    return null;
                case TupleMember:
                case BoolMember:
                case IntMember:
                case FloatMember:
                case DecimalMember:
                case StringMember:
                case Vector2Member:
                case Vector2IntMember:
                case Vector3Member:
                case Vector3IntMember:
                case ColorMember:
                case SpriteMember:
                case AudioMember:
                    return EntryKey(member, row, client, readRow);
                // A collection is a reference type: its equality is identity.
                default:
                    return null;
            }
        }

        /// <summary>
        /// P127 §12.1. A <c>Type</c> keys by its canonical binding key and a
        /// <c>CultureInfo</c> by its <c>Name</c> (every storable culture is
        /// invariant, so <c>""</c>, §11.1). Null for a <c>NumberFormatInfo</c>,
        /// whose equality is identity.
        /// </summary>
        private static string? BuiltInValueKey(NeoObjectTypeKind? objectType, MemberValue row, NeoClient client)
        {
            switch (objectType)
            {
                case NeoObjectTypeKind.Type:
                    if (row is not PayloadMemberValue { value: JObject { } payload } || payload["type"] is not JObject type)
                    {
                        throw new InvalidOperationException(
                            $"NeoSetEntryKey: Type entry '{row.id}' must hold a {{ type }} payload.");
                    }
                    return client.TypeRules.CanonicalKeyOf(TypeBindings.Read(type, $"Type entry '{row.id}' type"));
                case NeoObjectTypeKind.CultureInfo:
                    return "";
                case NeoObjectTypeKind.BuiltInComparer:
                    return ComparerKey(row, client);
                default:
                    return null;
            }
        }

        /// <summary>
        /// P127 §12.1. A <c>Default</c>, <c>ReferenceEquality</c> or
        /// <c>SetEquality</c> comparer keys by its payload, as two for one
        /// <c>T</c> are equal. Null for a <c>Create</c> comparer: a
        /// <c>Comparer&lt;T&gt;.Create</c> one's equality is identity, and an
        /// <c>EqualityComparer&lt;T&gt;.Create</c> one compares delegates, so
        /// only a comparer can decide.
        /// </summary>
        private static string? ComparerKey(MemberValue row, NeoClient client)
        {
            if (row is not PayloadMemberValue { value: JObject payload })
            {
                throw new InvalidOperationException(
                    $"NeoSetEntryKey: comparer entry '{row.id}' must hold a BuiltInComparer payload.");
            }
            int comparer = payload.Value<int>("comparer");
            int family = payload.Value<int>("family");
            if (comparer == (int)NeoBuiltInComparerKind.Create)
                return null;
            string typeKey = payload["type"] is JObject type
                ? client.TypeRules.CanonicalKeyOf(TypeBindings.Read(type, $"comparer entry '{row.id}' type"))
                : "";
            return comparer.ToString(CultureInfo.InvariantCulture) + ":" + family.ToString(CultureInfo.InvariantCulture) + ":" + typeKey;
        }

        /// <summary>
        /// A single reference's key: its target id, stored as the id or as a
        /// one-id array (§9.5, "a reference payload keys by its target id
        /// however it is stored").
        /// </summary>
        private static string ReferenceTargetKey(MemberValue row, Member member) => row switch
        {
            StringMemberValue { value: string target } => target,
            ArrayMemberValue { value: { Length: 1 } targets } when targets[0] is string target => target,
            _ => throw new InvalidOperationException(
                $"NeoSetEntryKey: '{member.name}' entry '{row.id}' must be a target id."),
        };

        private static bool IsHandle(Member member) => member is ClassMember or ListMember or DictionaryMember
            || member is LookupMember { Selection: NeoMemberSelectionKind.Multi }
            || member is DialogueLookupMember { Selection: NeoMemberSelectionKind.Multi }
            || member is EnumMember { Selection: NeoMemberSelectionKind.Multi };

        private static JToken TupleFragment(
            TupleMember tuple,
            MemberValue row,
            NeoClient client,
            Func<string, MemberValue?>? readRow)
        {
            // A nested tuple's elements read in the outer tuple's bindings,
            // as a probed runtime tuple's do (P127 §12.1).
            return TupleFragment(tuple, row, client, readRow, NeoTypeSources.EntryStamp(client, row, tuple));
        }

        private static JToken TupleFragment(
            TupleMember tuple,
            MemberValue row,
            NeoClient client,
            Func<string, MemberValue?>? readRow,
            IReadOnlyDictionary<string, TypeBinding>? stamp)
        {
            if (Require(row as ArrayMemberValue, tuple).value is not { } ids)
                return JValue.CreateNull();
            if (ids.Length != tuple.elementMemberIds.Length)
                throw new InvalidOperationException($"Tuple '{tuple.name}' has the wrong arity.");
            var parts = new JArray();
            for (int index = 0; index < ids.Length; index++)
            {
                Member element = client.TryResolveTupleElementMember(tuple, stamp, readsDeclarationDefault: false, index)
                    ?? throw new InvalidOperationException($"Tuple '{tuple.name}' has no element {index + 1}.");
                MemberValue child = (readRow is null ? client.ResolveValueRow(ids[index]) : readRow(ids[index]))
                    ?? throw new InvalidOperationException($"Tuple '{tuple.name}' has no child '{ids[index]}'.");
                JToken key = NeoGeneratedTypesSupport.IsNullStoredValue(child) ? JValue.CreateNull()
                    : element is TupleMember nested ? TupleFragment(nested, child, client, readRow, stamp)
                    // A class slot holding a box keys by what it holds (P127 §9.5).
                    : element is ClassMember && child.valueType is TypeBinding boxed ? new JValue(BoxKey(child, boxed, client, readRow))
                    : IsHandle(element) ? new JValue(child.id)
                    : element is LookupMember or DialogueLookupMember ? new JValue(ReferenceTargetKey(child, element))
                    : element is StringMember text ? new JValue(TupleStringKey(client, text, child))
                    : new JValue(EntryKey(element, child, client, readRow));
                parts.Add(new JArray((int)element.kind, key));
            }
            return parts;
        }

        /// <summary>
        /// Tuple strings compare the text a read produces, including localized
        /// elements; a minted <c>type:string</c> element is Plain.
        /// </summary>
        private static string? TupleStringKey(NeoClient client, StringMember element, MemberValue child) =>
            NeoMemberString.ResolveText(client, element, Require(child as StringMemberValue, element));

        internal static string OfRuntimeTuple(TupleMember member, RuntimeTuple tuple, NSGetterEvaluator.Context context, MemberValue? collectionRow = null)
        {
            // The probed tuple has no row yet; its elements read in the
            // bindings the collection's own row holds (P127 §12.1).
            IReadOnlyDictionary<string, TypeBinding>? stamp = null;
            if (collectionRow is not null)
            {
                if (!context.client.TryInferMemberForValueId(collectionRow.id, out Member? collection))
                    throw new InvalidOperationException($"Collection \"{collectionRow.id}\" is placed under no member.");
                stamp = NeoTypeSources.EntryStamp(context.client, collectionRow, collection);
            }
            return RuntimeTupleFragment(member, tuple, context, stamp, collectionRow is null).ToString(Formatting.None);
        }

        private static JArray RuntimeTupleFragment(
            TupleMember member,
            RuntimeTuple tuple,
            NSGetterEvaluator.Context context,
            IReadOnlyDictionary<string, TypeBinding>? stamp,
            bool readsDeclarationDefault)
        {
            if (tuple.Count != member.elementMemberIds.Length)
                throw new InvalidOperationException($"Tuple '{member.name}' has the wrong arity.");
            var parts = new JArray();
            for (int index = 0; index < tuple.Count; index++)
            {
                Member element = context.client.TryResolveTupleElementMember(member, stamp, readsDeclarationDefault, index)
                    ?? throw new InvalidOperationException($"Tuple '{member.name}' has no element {index + 1}.");
                object? value = tuple[index];
                JToken key;
                if (value is null)
                    key = JValue.CreateNull();
                else if (element is TupleMember nested && value is RuntimeTuple nestedValue)
                    key = RuntimeTupleFragment(nested, nestedValue, context, stamp, readsDeclarationDefault);
                else if (IsHandle(element))
                    key = new JValue(NSGetterEvaluator.ReferenceKeyOf(value, context) as string ?? "<unattached>");
                else if (element is LookupMember or DialogueLookupMember)
                {
                    string? id = value is NSSlotReference slot ? slot.TargetId : NSGetterEvaluator.ReferenceKeyOf(value, context) as string;
                    key = id is null ? JValue.CreateNull() : new JValue(id);
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

        /// <summary>
        /// A stored float, including its symbol forms (P127 §12.1): a row typed
        /// by shape reads <c>"NaN"</c>, <c>"-0"</c> and the infinities as strings.
        /// </summary>
        private static double RequireFloat(MemberValue row, Member entryMember) =>
            row is StringMemberValue { value: string symbol } && NeoStoredFloat.TryDecode(new JValue(symbol), out double decoded)
                ? decoded
                : RequireNumber(row, entryMember);

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
            // Every NaN keys as NeoScript's one NaN (§9.5, as float.Equals).
            if (double.IsNaN(value))
                return NaNKey;
            float single = (float)value;
            if (single == 0f)
                single = 0f;
            return BitConverter.SingleToInt32Bits(single).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>NeoScript's one NaN, bits <c>0xFFC00000</c> (P127 §8.1), as a signed integer.</summary>
        private const string NaNKey = "-4194304";

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
