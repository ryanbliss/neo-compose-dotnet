// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// P127 §12.1's storable built-in payloads, validated as data. Each
    /// validator throws a <see cref="JsonSerializationException"/> naming
    /// the payload and the offending field. Reloading a payload as a runtime
    /// value belongs to the evaluator.
    /// <list type="bullet">
    /// <item><description><c>Type</c>: <c>{ type }</c>, a type binding,
    /// closed, open or a definition, including an opaque head.</description></item>
    /// <item><description><c>CultureInfo</c>: <c>{ invariant: true }</c>, or
    /// <c>{ readOnly, numberFormat }</c>.</description></item>
    /// <item><description><c>NumberFormatInfo</c>: <c>{ invariant: true }</c>,
    /// or <c>readOnly</c> and every §11.2 property, keyed by its camelCase
    /// name (<see cref="NumberFormatKeys"/>).</description></item>
    /// <item><description><c>BuiltInComparer</c>:
    /// <c>{ comparer, family, type?, delegates? }</c>.</description></item>
    /// </list>
    /// </summary>
    internal static class NeoStorablePayloads
    {
        internal const string InvariantKey = "invariant";
        internal const string ReadOnlyKey = "readOnly";

        /// <summary>
        /// Whether <paramref name="payload"/> reads as a writable
        /// <c>CultureInfo</c> or <c>NumberFormatInfo</c>, whose C# writes
        /// persist through its row (§12.1).
        /// </summary>
        internal static bool IsWritable(JObject payload) => payload.Value<bool?>(ReadOnlyKey) == false;

        /// <summary>
        /// §11.2's properties in .NET's declaration groups, as payload keys:
        /// the .NET name with its first letter lowered, and <c>nanSymbol</c>
        /// for <c>NaNSymbol</c>.
        /// </summary>
        internal static readonly string[] NumberFormatKeys =
        {
            "currencyDecimalDigits",
            "currencyDecimalSeparator",
            "currencyGroupSeparator",
            "currencyGroupSizes",
            "currencyNegativePattern",
            "currencyPositivePattern",
            "currencySymbol",
            "nanSymbol",
            "negativeInfinitySymbol",
            "positiveInfinitySymbol",
            "negativeSign",
            "positiveSign",
            "perMilleSymbol",
            "percentSymbol",
            "numberDecimalDigits",
            "numberDecimalSeparator",
            "numberGroupSeparator",
            "numberGroupSizes",
            "numberNegativePattern",
            "percentDecimalDigits",
            "percentDecimalSeparator",
            "percentGroupSeparator",
            "percentGroupSizes",
            "percentNegativePattern",
            "percentPositivePattern",
            "digitSubstitution",
            "nativeDigits",
        };

        /// <summary>The payload of an invariant <c>CultureInfo</c> or a <c>NumberFormatInfo</c>.</summary>
        internal static JObject Encode(object value) => value switch
        {
            CultureInfo culture when ReferenceEquals(culture, CultureInfo.InvariantCulture) => new JObject { [InvariantKey] = true },
            CultureInfo culture => new JObject
            {
                [ReadOnlyKey] = culture.IsReadOnly,
                ["numberFormat"] = Encode(culture.NumberFormat),
            },
            NumberFormatInfo format => EncodeNumberFormat(format),
            _ => throw new ArgumentException($"{value.GetType().Name} has no storable payload.", nameof(value)),
        };

        private static JObject EncodeNumberFormat(NumberFormatInfo format)
        {
            if (ReferenceEquals(format, NumberFormatInfo.InvariantInfo))
                return new JObject { [InvariantKey] = true };
            var payload = new JObject { [ReadOnlyKey] = format.IsReadOnly };
            foreach (string key in NumberFormatKeys)
            {
                object stored = NumberFormatProperty(key).GetValue(format)!;
                payload[key] = stored is DigitShapes shapes ? (int)shapes : JToken.FromObject(stored);
            }
            return payload;
        }

        /// <summary>The <c>NumberFormatInfo</c> property a payload key names.</summary>
        internal static PropertyInfo NumberFormatProperty(string key)
        {
            string name = key == "nanSymbol" ? "NaNSymbol" : char.ToUpperInvariant(key[0]) + key.Substring(1);
            return typeof(NumberFormatInfo).GetProperty(name)!;
        }

        private static readonly Dictionary<string, int> PatternMaximums = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["currencyNegativePattern"] = 16,
            ["currencyPositivePattern"] = 3,
            ["numberNegativePattern"] = 4,
            ["percentNegativePattern"] = 11,
            ["percentPositivePattern"] = 3,
        };

        private const int DigitShapesNativeNational = 2;

        /// <summary>Validates a <c>Type</c> payload.</summary>
        internal static void ValidateType(JToken? payload, string context)
        {
            if (payload is not JObject record)
                throw Error(context, "must be an object");
            if (record.Count != 1 || record["type"] is not JObject stored)
                throw Error(context, "must be exactly { type }, with 'type' a type binding object");
            TypeBinding type = TypeBindings.Read(stored, $"{context} type");
            string? violation = TypeBindings.PositionViolation(type, TypeBindingPosition.TypePayload);
            if (violation is not null)
                throw Error(context, $"type {violation}");
        }


        /// <summary>Validates a <c>CultureInfo</c> payload.</summary>
        internal static void ValidateCultureInfo(JToken? payload, string context)
        {
            if (payload is not JObject culture)
                throw Error(context, "must be an object");
            if (IsInvariant(culture, context))
                return;
            if (culture.Count != 2 || culture.Property(ReadOnlyKey) is null || culture.Property("numberFormat") is null)
                throw Error(context, "must be exactly { invariant: true } or { readOnly, numberFormat }");
            RequireBoolean(culture, ReadOnlyKey, context);
            ValidateNumberFormatInfo(culture["numberFormat"], $"{context} numberFormat");
        }

        /// <summary>Validates a <c>NumberFormatInfo</c> payload.</summary>
        internal static void ValidateNumberFormatInfo(JToken? payload, string context)
        {
            if (payload is not JObject info)
                throw Error(context, "must be an object");
            if (IsInvariant(info, context))
                return;
            if (info.Count != NumberFormatKeys.Length + 1)
                throw Error(context, $"must hold exactly 'readOnly' and the {NumberFormatKeys.Length} NumberFormatInfo properties");
            RequireBoolean(info, ReadOnlyKey, context);
            foreach (string key in NumberFormatKeys)
            {
                JToken? value = info[key];
                if (value is null)
                    throw Error(context, $"is missing '{key}'");
                ValidateNumberFormatProperty(key, value, context);
            }
        }

        /// <summary>
        /// Validates a <c>BuiltInComparer</c> payload. A
        /// <c>Default</c> or <c>SetEquality</c> comparer stores its closed
        /// <c>type</c>. Create delegates use the ordinary stored delegate shape.
        /// </summary>
        internal static (NeoBuiltInComparerKind kind, NeoComparerFamilyKind family) ValidateBuiltInComparer(JToken? payload, string context)
        {
            if (payload is not JObject comparer)
                throw Error(context, "must be an object");
            NeoBuiltInComparerKind kind = ReadOrdinal<NeoBuiltInComparerKind>(comparer, "comparer", context);
            NeoComparerFamilyKind family = ReadOrdinal<NeoComparerFamilyKind>(comparer, "family", context);
            bool hasType = comparer.Property("type") is not null;
            bool hasDelegates = comparer.Property("delegates") is not null;
            if (comparer.Count != 2 + (hasType ? 1 : 0) + (hasDelegates ? 1 : 0))
                throw Error(context, "must contain exactly 'comparer', 'family' and optional 'type'/'delegates'");
            if (kind == NeoBuiltInComparerKind.ReferenceEquality)
            {
                if (family != NeoComparerFamilyKind.Equality)
                    throw Error(context, "ReferenceEquality is an equality comparer");
                if (hasType)
                    throw Error(context, "ReferenceEquality has no 'type'");
            }
            else
            {
                if (!hasType)
                    throw Error(context, $"{kind} needs its closed 'type'");
                if (comparer["type"] is not JObject stored)
                    throw Error(context, "'type' must be a type binding object");
                string? violation = TypeBindings.PositionViolation(TypeBindings.Read(stored, $"{context} type"), TypeBindingPosition.Identity);
                if (violation is not null)
                    throw Error(context, $"'type' {violation}");
            }
            if (kind == NeoBuiltInComparerKind.SetEquality && family != NeoComparerFamilyKind.Equality)
                throw Error(context, "SetEquality is an equality comparer");
            if (kind != NeoBuiltInComparerKind.Create)
            {
                if (hasDelegates)
                    throw Error(context, $"{kind} has no 'delegates'");
                return (kind, family);
            }
            if (comparer["delegates"] is not JArray delegates)
                throw Error(context, "Create needs its 'delegates' array");
            int maximum = family == NeoComparerFamilyKind.Equality ? 2 : 1;
            if (delegates.Count < 1 || delegates.Count > maximum)
            {
                throw Error(context, family == NeoComparerFamilyKind.Equality
                    ? "EqualityComparer<T>.Create stores [equals, getHashCode?]"
                    : "Comparer<T>.Create stores [comparison]");
            }
            for (int i = 0; i < delegates.Count; i++)
            {
                if (delegates[i] is not JObject storedDelegate)
                    throw Error(context, $"Create delegate {i} must be an object");
                NeoDelegateValue value = storedDelegate.ToObject<NeoDelegateValue>()!;
                if (value.IsClosure && string.IsNullOrWhiteSpace(value.code))
                    throw Error(context, $"stores a Create comparer whose delegate {i} has no source to compile");
                if (value.captures is { Length: > 0 })
                    throw Error(context, $"comparer delegate {i} cannot capture values");
            }
            return (kind, family);
        }

        /// <summary>
        /// P127 §1.3. A <c>BuiltInComparer</c> payload held through
        /// <c>valueType</c>, which names the public system class the
        /// comparer came from: <c>EqualityComparer&lt;T&gt;</c> for an
        /// equality <c>Default</c> or <c>Create</c> comparer and for
        /// <c>CreateSetComparer()</c>, with <c>T</c> the set type;
        /// <c>Comparer&lt;T&gt;</c> for a comparison <c>Default</c> or
        /// <c>Create</c> comparer; and <c>ReferenceEqualityComparer</c>.
        /// </summary>
        internal static void ValidateHeldComparer(TypeBinding valueType, JToken payload, string context)
        {
            (NeoBuiltInComparerKind kind, NeoComparerFamilyKind family) = ValidateBuiltInComparer(payload, context);
            string expected = kind == NeoBuiltInComparerKind.ReferenceEquality
                ? NeoSystemComparerClasses.ReferenceEqualityComparer
                : family == NeoComparerFamilyKind.Equality
                    ? NeoSystemComparerClasses.EqualityComparer
                    : NeoSystemComparerClasses.Comparer;
            string classId = valueType.Head!.ClassId!;
            string? held = NeoSystemComparerClasses.NameOf(classId);
            if (held is null)
                throw Error(context, $"'valueType' class '{classId}' is no built-in comparer class");
            if (held != expected)
            {
                string source = kind == NeoBuiltInComparerKind.ReferenceEquality ? expected : expected + "<T>";
                throw Error(context, $"is a {kind} {family} comparer, which comes from {source}, but its 'valueType' names {held}");
            }
            if (kind == NeoBuiltInComparerKind.ReferenceEquality)
            {
                if (valueType.Bindings is not null)
                    throw Error(context, $"'valueType' must be {expected}, which has no type arguments");
                return;
            }
            TypeBinding argument = TypeBindings.Read((JObject)((JObject)payload)["type"]!, $"{context} type");
            if (kind == NeoBuiltInComparerKind.SetEquality)
            {
                argument = TypeBinding.Closed(
                    new TypeHead(MemberKind.Set, true),
                    new Dictionary<string, TypeBinding>(StringComparer.Ordinal) { ["0"] = argument });
            }
            if (valueType.Bindings is not { Count: 1 } bindings || !bindings.Values.First().Equals(argument))
            {
                string set = kind == NeoBuiltInComparerKind.SetEquality ? "Set<T>, with T the comparer's 'type'" : "the comparer's 'type'";
                throw Error(context, $"'valueType' must be {expected} bound to {set}");
            }
        }

        private static bool IsInvariant(JObject payload, string context)
        {
            if (payload.Property(InvariantKey) is null)
                return false;
            if (payload.Count != 1 || payload[InvariantKey]?.Type != JTokenType.Boolean || !payload.Value<bool>(InvariantKey))
                throw Error(context, "with 'invariant' must be exactly { invariant: true }");
            return true;
        }

        private static void ValidateNumberFormatProperty(string key, JToken value, string context)
        {
            if (key.EndsWith("DecimalDigits", StringComparison.Ordinal))
            {
                RequireIntInRange(key, value, 99, context);
                return;
            }
            if (PatternMaximums.TryGetValue(key, out int maximum))
            {
                RequireIntInRange(key, value, maximum, context);
                return;
            }
            if (key.EndsWith("GroupSizes", StringComparison.Ordinal))
            {
                ValidateGroupSizes(key, value, context);
                return;
            }
            if (key == "digitSubstitution")
            {
                RequireIntInRange(key, value, DigitShapesNativeNational, context);
                return;
            }
            if (key == "nativeDigits")
            {
                if (value is not JArray digits || digits.Count != 10)
                    throw Error(context, "'nativeDigits' must be a list of ten strings");
                for (int index = 0; index < digits.Count; index++)
                {
                    if (digits[index].Type != JTokenType.String)
                        throw Error(context, "'nativeDigits' must be a list of ten strings");
                    if (!IsNativeDigitText(digits[index].Value<string>()!))
                        throw Error(context, $"'nativeDigits' entry {index} must be one UTF-16 unit or one surrogate pair");
                }
                return;
            }
            if (value.Type != JTokenType.String)
                throw Error(context, $"'{key}' must be a string");
            if (key.EndsWith("DecimalSeparator", StringComparison.Ordinal) && value.Value<string>()!.Length == 0)
                throw Error(context, $"'{key}' must not be empty");
        }

        /// <summary>
        /// One UTF-16 unit or one surrogate pair. The Unicode decimal-digit
        /// check of §11.2 runs at the runtime write.
        /// </summary>
        private static bool IsNativeDigitText(string text) =>
            text.Length == 1 || (text.Length == 2 && char.IsHighSurrogate(text[0]) && char.IsLowSurrogate(text[1]));

        private static void ValidateGroupSizes(string key, JToken value, string context)
        {
            if (value is not JArray sizes)
                throw Error(context, $"'{key}' must be a list of ints");
            for (int index = 0; index < sizes.Count; index++)
            {
                JToken size = sizes[index];
                if (size.Type != JTokenType.Integer)
                    throw Error(context, $"'{key}' must be a list of ints");
                long entry = size.Value<long>();
                bool finalZero = entry == 0 && index == sizes.Count - 1;
                if (!finalZero && (entry < 1 || entry > 9))
                    throw Error(context, $"'{key}' entry {index} must be between 1 and 9, or a final 0");
            }
        }

        private static void RequireIntInRange(string key, JToken value, int maximum, string context)
        {
            if (value.Type != JTokenType.Integer)
                throw Error(context, $"'{key}' must be an int");
            long number = value.Value<long>();
            if (number < 0 || number > maximum)
                throw Error(context, $"'{key}' must be between 0 and {maximum}");
        }

        private static void RequireBoolean(JObject payload, string key, string context)
        {
            if (payload[key]?.Type != JTokenType.Boolean)
                throw Error(context, $"'{key}' must be a bool");
        }

        private static TEnum ReadOrdinal<TEnum>(JObject payload, string key, string context)
            where TEnum : struct
        {
            JToken? token = payload[key];
            if (token?.Type != JTokenType.Integer)
                throw Error(context, $"'{key}' must be a {typeof(TEnum).Name} ordinal");
            long ordinal = token.Value<long>();
            if (ordinal < int.MinValue || ordinal > int.MaxValue || !System.Enum.IsDefined(typeof(TEnum), (int)ordinal))
                throw Error(context, $"'{key}' {ordinal} is not a {typeof(TEnum).Name} ordinal");
            return (TEnum)System.Enum.ToObject(typeof(TEnum), (int)ordinal);
        }

        private static JsonSerializationException Error(string context, string message) =>
            new JsonSerializationException($"{context} {message}.");
    }

    /// <summary>
    /// P127 §1.3 and §7.2. The system classes a held built-in comparer
    /// comes from, by class id. System ids are contract ids, the same in
    /// every project (P39 §1.1a), so one minted table serves every project.
    /// </summary>
    internal static class NeoSystemComparerClasses
    {
        internal const string EqualityComparer = "EqualityComparer";
        internal const string Comparer = "Comparer";
        internal const string ReferenceEqualityComparer = "ReferenceEqualityComparer";

        internal const string ReferenceEqualityComparerId = "system_07f7d2cd-daab-411d-9637-cbf7594893f1";
        internal const string ComparerId = "system_2848aabe-772e-43a0-b230-c63a36d19a1b";
        internal const string EqualityComparerId = "system_624b009f-dadc-469f-83be-db6ba3294971";

        internal static string? NameOf(string classId) => classId switch
        {
            EqualityComparerId => EqualityComparer,
            ComparerId => Comparer,
            ReferenceEqualityComparerId => ReferenceEqualityComparer,
            _ => null,
        };

        // Member ids are pinned by the system schema; overloaded names are
        // not sufficient to distinguish the non-generic entry points.
        internal static readonly IReadOnlyDictionary<string, (string Owner, string Operation)> Operations =
            new Dictionary<string, (string, string)>(StringComparer.Ordinal)
            {
                ["system_2b1546e0-95ee-4529-bc4f-09507d4cda27"] = (ReferenceEqualityComparer, "GetInstance"),
                ["system_875b9ee4-ee02-400f-addf-95f1a0af4b7e"] = (ReferenceEqualityComparer, "Equals"),
                ["system_e44698d7-5fd4-4a70-b0d1-90ea065024e2"] = (ReferenceEqualityComparer, "GetHashCode"),
                ["system_4abc87b5-da88-4feb-aa49-2f2bcc40ef5f"] = (ReferenceEqualityComparer, "GetHashCode"),
                ["system_aa5cb4c4-1492-48f4-a58b-3bd2e2539ca8"] = (ReferenceEqualityComparer, "GetHashCode"),
                ["system_53041aa0-ad45-4bf7-a995-367394b660cb"] = (Comparer, "GetDefault"),
                ["system_66941395-a39c-420b-b3bc-13d01141f4d1"] = (Comparer, "Create"),
                ["system_b45b77cd-ba29-45c8-85bb-e0c293f0edc2"] = (Comparer, "Compare"),
                ["system_37a5ce29-bc05-4fbf-b752-82caebe71e89"] = (Comparer, "CompareObject"),
                ["system_b5d0bc7f-8f01-48c2-a683-98c3f75dac87"] = (EqualityComparer, "GetDefault"),
                ["system_fedbd8cd-50e9-44a4-8a79-6ac3da8f8fc5"] = (EqualityComparer, "Create"),
                ["system_1199e1ec-4232-42ae-a67f-bb0b889c32c3"] = (EqualityComparer, "Equals"),
                ["system_8b820172-3b60-40af-aa63-4163cb3e57f3"] = (EqualityComparer, "GetHashCode"),
                ["system_5df2ea47-f04b-4e44-b6ba-30d40e938483"] = (EqualityComparer, "EqualsObject"),
                ["system_954562aa-03c7-46bf-be0e-bd0a58e88337"] = (EqualityComparer, "GetHashCodeObject"),
            };

    }

    /// <summary>
    /// P127 §9.5. A stored Set's or Dictionary's comparer, absent for the
    /// collection's own default: <c>{ kind: BuiltIn, comparer }</c> with a
    /// <c>BuiltInComparer</c> payload of the <c>Equality</c> family, or
    /// <c>{ kind: Value, valueId }</c> naming the owned value row of an
    /// authored comparer.
    /// </summary>
    [JsonConverter(typeof(NeoCollectionComparerConverter))]
    public sealed class NeoCollectionComparer
    {
        [JsonIgnore] internal object? runtimeIdentity;
        public NeoCollectionComparerKind kind;

        /// <summary>The <c>BuiltInComparer</c> payload of a <c>BuiltIn</c> comparer.</summary>
        public JObject? comparer;

        /// <summary>The owned comparer row of a <c>Value</c> comparer.</summary>
        public string? valueId;

        internal static bool Same(NeoCollectionComparer? left, NeoCollectionComparer? right) =>
            ReferenceEquals(left, right)
            || left is not null
                && right is not null
                && left.kind == right.kind
                && left.valueId == right.valueId
                && JToken.DeepEquals(left.comparer, right.comparer);
    }

    /// <summary>Strict reader and writer for <see cref="NeoCollectionComparer"/>.</summary>
    public sealed class NeoCollectionComparerConverter : JsonConverter
    {
        private const string Context = "Collection comparer";

        public override bool CanConvert(Type objectType) => objectType == typeof(NeoCollectionComparer);

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            JObject obj = NeoJsonObjectReader.Read(reader);
            var kind = StrictRecordShapeEnums.ReadRequired<NeoCollectionComparerKind>(obj, "kind", Context);
            if (obj.Count != 2)
                throw new JsonSerializationException($"{Context} must contain exactly 'kind' and its {(kind == NeoCollectionComparerKind.BuiltIn ? "'comparer'" : "'valueId'")}.");
            if (kind == NeoCollectionComparerKind.BuiltIn)
            {
                NeoStorablePayloads.ValidateBuiltInComparer(obj["comparer"], $"{Context} 'comparer'");
                if (obj["comparer"]!.Value<int>("family") != (int)NeoComparerFamilyKind.Equality)
                    throw new JsonSerializationException($"{Context} must be an equality comparer; a Comparison comparer can't key a Set or Dictionary.");
                return new NeoCollectionComparer { kind = kind, comparer = (JObject)obj["comparer"]!.DeepClone() };
            }
            JToken? valueId = obj["valueId"];
            if (valueId?.Type != JTokenType.String || string.IsNullOrEmpty(valueId.Value<string>()))
                throw new JsonSerializationException($"{Context} of kind Value must name its owned comparer row in a non-empty 'valueId'.");
            return new NeoCollectionComparer { kind = kind, valueId = valueId.Value<string>() };
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is not NeoCollectionComparer comparer)
            {
                writer.WriteNull();
                return;
            }
            writer.WriteStartObject();
            writer.WritePropertyName("kind");
            writer.WriteValue((int)comparer.kind);
            if (comparer.kind == NeoCollectionComparerKind.BuiltIn)
            {
                writer.WritePropertyName("comparer");
                (comparer.comparer ?? throw new JsonSerializationException($"{Context} of kind BuiltIn has no 'comparer'.")).WriteTo(writer);
            }
            else
            {
                writer.WritePropertyName("valueId");
                writer.WriteValue(comparer.valueId ?? throw new JsonSerializationException($"{Context} of kind Value has no 'valueId'."));
            }
            writer.WriteEndObject();
        }
    }
}
