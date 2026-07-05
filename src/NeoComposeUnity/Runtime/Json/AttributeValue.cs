// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    // =========================================================================
    // Two parallel polymorphic hierarchies bridged by a generic interface.
    //
    //   * AttributeValueBase / AttributeValueBase<TValue> — mirrors TS-side
    //     IAttributeValueBase. Used ONLY as the carrier for
    //     Attribute.defaultValue. No id / timestamps / projectId — those
    //     belong to stored rows, not embedded carriers.
    //
    //   * AttributeValue / AttributeValue<TValue> — mirrors TS-side
    //     IAttributeValue (= IAttributeValueProps + IWithMongoId). Used for
    //     entries in the `values` map. Carries id / timestamps / projectId,
    //     all REQUIRED (the export only ships fully-stored rows).
    //
    //   * IAttributeValueBase<TValue> — generic interface implemented by
    //     both the embedded-carrier and stored-row forms for the same
    //     payload type. Lets consumer code work generically against either
    //     form: `void SetValue<T>(IAttributeValueBase<T> v, T x)`.
    //
    // Layered structure:
    //
    //   IAttributeValueBase                  ← non-generic (just typeId)
    //   IAttributeValueBase<TValue>          ← adds typed value
    //
    //   AttributeValueBase : IAttributeValueBase                      (typeId only)
    //   AttributeValueBase<TValue> : AttributeValueBase, IAttributeValueBase<TValue>
    //
    //   AttributeValue : AttributeValueBase                           (+ id/timestamps)
    //   AttributeValue<TValue> : AttributeValue, IAttributeValueBase<TValue>
    //
    //   <Shape>AttributeValueBase : AttributeValueBase<concrete-type>  (× 6)
    //   <Shape>AttributeValue     : AttributeValue<concrete-type>      (× 6)
    //
    // Both hierarchies are dispatched by the JSON shape of the `value` field.
    // The TS wire has no `type` discriminator on values; the type is implied
    // by the parent attribute. Six subclasses on each side cover the
    // possible JSON shapes (Null / Bool / Number / String / Array / Object).
    // For attribute types that share a JSON shape (Int+Float → number;
    // List+Enum+Lookup → array; Custom+Dictionary → object), callers
    // disambiguate via the parent Attribute's `type`.
    //
    // Per-subclass `value` is nullable: the parent attribute may have
    // `required: false`, in which case the stored payload can legitimately
    // be null. Nullability is encoded into the type argument itself —
    // concretes pass `bool?` / `double?` / `string?` / `string[]?` /
    // `Dictionary<string,string>?` — rather than a `TValue?` syntax that
    // requires a struct/class constraint to resolve correctly. The
    // generic just stores `TValue` directly. Reading site:
    //   ((NumberAttributeValueBase)x).value      // double?
    //   ((StringAttributeValueBase)x).value      // string?
    //
    // Fields are auto-properties (not public fields) because C# interface
    // members must be properties. Newtonsoft serializes properties identically
    // to fields — wire shape unchanged.
    // =========================================================================

    // -------------------------------------------------------------------------
    // Interfaces.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Common surface across every <see cref="AttributeValueBase"/> and
    /// <see cref="AttributeValue"/> subclass — the shared <c>typeId</c>
    /// override (mirrors TS-side <c>typeId?: string | null</c>). Consumers
    /// that need typed access to the polymorphic <c>value</c> field
    /// should use <see cref="IAttributeValueBase{TValue}"/> instead.
    /// </summary>
    public interface IAttributeValueBase
    {
        string? typeId { get; set; }
    }

    /// <summary>
    /// Generic bridge across the embedded-carrier and stored-row forms
    /// for the same payload shape. Implemented by
    /// <see cref="AttributeValueBase{TValue}"/> and
    /// <see cref="AttributeValue{TValue}"/>, so consumer code can write
    /// one path that works on either form:
    /// <code>
    /// void Bump(IAttributeValueBase&lt;double?&gt; v) =&gt; v.value = (v.value ?? 0) + 1;
    /// </code>
    /// <para>The <c>?</c> on the type argument is intentional — pass the
    /// already-nullable form (<c>double?</c>, <c>string?</c>, etc.). The
    /// interface stores <c>TValue</c> directly; nullability lives in the
    /// type argument rather than in a <c>TValue?</c> syntax that would
    /// require a struct/class constraint to resolve.</para>
    /// </summary>
    public interface IAttributeValueBase<TValue> : IAttributeValueBase
    {
        TValue? value { get; set; }
    }

    // -------------------------------------------------------------------------
    // AttributeValueBase — embedded carrier (Attribute.defaultValue).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Non-generic base for <see cref="AttributeValueBase{TValue}"/> —
    /// holds the polymorphism-anchor [JsonConverter] attribute and the
    /// typeId field shared by every shape. Six concrete variants (one per
    /// JSON shape) extend either this directly (Null) or the typed
    /// <see cref="AttributeValueBase{TValue}"/> intermediate.
    /// </summary>
    [JsonConverter(typeof(AttributeValueBaseConverter))]
    public abstract class AttributeValueBase : IAttributeValueBase
    {
        public string? typeId { get; set; }
    }

    /// <summary>
    /// Typed embedded-carrier intermediate. Concrete shapes extend this
    /// with the already-nullable <typeparamref name="TValue"/> — e.g.
    /// <c>NumberAttributeValueBase : AttributeValueBase&lt;double?&gt;</c>.
    /// </summary>
    public abstract class AttributeValueBase<TValue>
        : AttributeValueBase, IAttributeValueBase<TValue>
    {
        public TValue? value { get; set; } = default!;
    }

    /// <summary>
    /// Carrier for a Null / NSGetter default-value. Typed
    /// <c>object?</c> so it slots into <see cref="Attribute{TValue}"/>
    /// for <see cref="NullAttribute"/> and <see cref="NSGetterAttribute"/>
    /// (whose stored value is conceptually always null but still needs
    /// to fit the typed <c>defaultValue</c> field).
    /// </summary>
    public class NullAttributeValueBase : AttributeValueBase<object?> { }

    /// <summary>Carrier for a Bool <see cref="Attribute.defaultValue"/>.</summary>
    public class BoolAttributeValueBase : AttributeValueBase<bool?> { }

    /// <summary>
    /// Carrier for an Int / Float <see cref="Attribute.defaultValue"/>.
    /// Stored as <c>double?</c> to round-trip both Int and Float without
    /// loss; cast via the parent attribute's <c>type</c>.
    /// </summary>
    public class NumberAttributeValueBase : AttributeValueBase<double?> { }

    /// <summary>Carrier for a String <see cref="Attribute.defaultValue"/>.</summary>
    public class StringAttributeValueBase : AttributeValueBase<string?>
    {
        public NeoStringLocalizationMode? neoLocalizationMode;
    }

    /// <summary>Carrier for a List / Enum / Lookup <see cref="Attribute.defaultValue"/>.</summary>
    public class ArrayAttributeValueBase : AttributeValueBase<string[]?> { }

    /// <summary>Carrier for a Dictionary / Custom <see cref="Attribute.defaultValue"/>.</summary>
    public class ObjectAttributeValueBase : AttributeValueBase<Dictionary<string, string>?> { }

    /// <summary>Carrier for an Audio file <see cref="Attribute.defaultValue"/>.</summary>
    public class FileAttributeValueBase : AttributeValueBase<FileValue?> { }

    /// <summary>Carrier for a Sprite <see cref="Attribute.defaultValue"/>.</summary>
    public class SpriteAttributeValueBase : AttributeValueBase<SpriteValue?> { }

    [JsonConverter(typeof(NeoVector2ValueConverter))]
    public class NeoVector2Value
    {
        public float x { get; set; }
        public float y { get; set; }
    }

    [JsonConverter(typeof(NeoVector3ValueConverter))]
    public class NeoVector3Value : NeoVector2Value
    {
        public float z { get; set; }
    }

    /// <summary>Carrier for Vector2 / Vector2Int defaults.</summary>
    public class Vector2AttributeValueBase : AttributeValueBase<NeoVector2Value?> { }

    /// <summary>Carrier for Vector3 / Vector3Int defaults.</summary>
    public class Vector3AttributeValueBase : AttributeValueBase<NeoVector3Value?> { }

    public class NeoVector2ValueConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(NeoVector2Value);
        }

        public override bool CanWrite => false;

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);
            if (!LooksLikeVector2Value(obj))
            {
                throw new JsonSerializationException(
                    "Vector2 value must have exactly numeric 'x' and 'y' fields.");
            }
            return new NeoVector2Value
            {
                x = ReadFiniteFloat(obj, "x"),
                y = ReadFiniteFloat(obj, "y"),
            };
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotImplementedException(
                "NeoVector2ValueConverter is read-only; default serialization handles writes.");
        }

        internal static bool LooksLikeVector2Value(JToken token)
        {
            if (token.Type != JTokenType.Object) return false;
            var obj = (JObject)token;
            return obj.Count == 2 && IsFiniteNumber(obj["x"]) && IsFiniteNumber(obj["y"]);
        }

        internal static float ReadFiniteFloat(JObject obj, string key)
        {
            var token = obj[key] ?? throw new JsonSerializationException(
                $"Vector value is missing '{key}'.");
            if (!IsFiniteNumber(token))
            {
                throw new JsonSerializationException(
                    $"Vector component '{key}' must be a finite number.");
            }
            var value = token.Value<float>();
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new JsonSerializationException(
                    $"Vector component '{key}' must be a finite number.");
            }
            return value;
        }

        private static bool IsFiniteNumber(JToken? token)
        {
            if (token == null) return false;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                return false;
            }
            var value = token.Value<float>();
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public class NeoVector3ValueConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(NeoVector3Value);
        }

        public override bool CanWrite => false;

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);
            if (!LooksLikeVector3Value(obj))
            {
                throw new JsonSerializationException(
                    "Vector3 value must have exactly numeric 'x', 'y', and 'z' fields.");
            }
            return new NeoVector3Value
            {
                x = NeoVector2ValueConverter.ReadFiniteFloat(obj, "x"),
                y = NeoVector2ValueConverter.ReadFiniteFloat(obj, "y"),
                z = NeoVector2ValueConverter.ReadFiniteFloat(obj, "z"),
            };
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotImplementedException(
                "NeoVector3ValueConverter is read-only; default serialization handles writes.");
        }

        internal static bool LooksLikeVector3Value(JToken token)
        {
            if (token.Type != JTokenType.Object) return false;
            var obj = (JObject)token;
            return obj.Count == 3
                && IsFiniteNumber(obj["x"])
                && IsFiniteNumber(obj["y"])
                && IsFiniteNumber(obj["z"]);
        }

        private static bool IsFiniteNumber(JToken? token)
        {
            if (token == null) return false;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                return false;
            }
            var value = token.Value<float>();
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Two-mode dispatch converter for <see cref="AttributeValueBase"/>.
    ///
    /// <para><b>Context-aware (TValue) dispatch</b> when the field is
    /// the typed intermediate <see cref="AttributeValueBase{TValue}"/>
    /// — typically <see cref="Attribute{TValue}.defaultValue"/>. The
    /// converter resolves the concrete subclass from the closed generic
    /// parameter (e.g. <c>AttributeValueBase&lt;bool?&gt;</c> →
    /// <see cref="BoolAttributeValueBase"/>), independent of the wire's
    /// <c>value</c> shape. This means a typed
    /// <see cref="StringAttribute.defaultValue"/> with wire
    /// <c>{"value": null}</c> produces a
    /// <see cref="StringAttributeValueBase"/> with <c>value = null</c>
    /// — not a <see cref="NullAttributeValueBase"/> — preserving the
    /// typed identity even when the payload is missing.</para>
    ///
    /// <para><b>Shape dispatch</b> when the field is the non-generic
    /// <see cref="AttributeValueBase"/> (or <see cref="AttributeValue"/>
    /// for the stored-row converter). The wire <c>value</c> token's
    /// JSON shape selects the concrete; null routes to the Null*
    /// concrete. This is the only available signal when no typing
    /// context flows from the field.</para>
    ///
    /// <para>Read-only — default Newtonsoft serialization handles
    /// writes.</para>
    /// </summary>
    public class AttributeValueBaseConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            // Anchor on AttributeValueBase but explicitly NOT on the
            // AttributeValue subhierarchy. AttributeValue inherits from
            // AttributeValueBase, so without this guard Newtonsoft would
            // route stored-row instances through this converter and lose
            // their id/timestamps. The AttributeValueConverter declared
            // on AttributeValue takes precedence for those.
            if (typeof(AttributeValue).IsAssignableFrom(objectType)) return false;
            return typeof(AttributeValueBase).IsAssignableFrom(objectType);
        }

        public override bool CanWrite => false;

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var obj = JObject.Load(reader);
            var concrete =
                TypedHierarchyMap.ResolveByContext(objectType, typeof(AttributeValueBase<>))
                ?? ResolveByShape(obj["value"]);
            // Use Populate (not ToObject) to avoid converter recursion.
            // Same trick as DiscriminatedConverter — see its docstring.
            var instance = Activator.CreateInstance(concrete);
            using (var subReader = obj.CreateReader())
            {
                serializer.Populate(subReader, instance);
            }
            return instance;
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotImplementedException(
                "AttributeValueBaseConverter is read-only; default serialization handles writes.");
        }

        private static Type ResolveByShape(JToken? token)
        {
            if (token == null) return typeof(NullAttributeValueBase);
            switch (token.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return typeof(NullAttributeValueBase);
                case JTokenType.Boolean:
                    return typeof(BoolAttributeValueBase);
                case JTokenType.Integer:
                case JTokenType.Float:
                    return typeof(NumberAttributeValueBase);
                case JTokenType.String:
                    return typeof(StringAttributeValueBase);
                case JTokenType.Array:
                    return typeof(ArrayAttributeValueBase);
                case JTokenType.Object:
                    if (NeoVector3ValueConverter.LooksLikeVector3Value(token)) return typeof(Vector3AttributeValueBase);
                    if (NeoVector2ValueConverter.LooksLikeVector2Value(token)) return typeof(Vector2AttributeValueBase);
                    if (LooksLikeSpriteValue(token)) return typeof(SpriteAttributeValueBase);
                    if (LooksLikeFileValue(token)) return typeof(FileAttributeValueBase);
                    return typeof(ObjectAttributeValueBase);
                default:
                    return typeof(NullAttributeValueBase);
            }
        }

        private static bool LooksLikeFileValue(JToken token)
        {
            return token.Type == JTokenType.Object &&
                token["fileId"]?.Type == JTokenType.String;
        }

        private static bool LooksLikeSpriteValue(JToken token)
        {
            return LooksLikeFileValue(token) &&
                token["sliceIndex"] != null &&
                (token["sliceIndex"]!.Type == JTokenType.Integer ||
                    token["sliceIndex"]!.Type == JTokenType.Float);
        }
    }

    // -------------------------------------------------------------------------
    // AttributeValue — stored row (entries in the `values` map).
    // Mirrors TS-side IAttributeValue (= IAttributeValueProps + IWithMongoId).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Non-generic base for <see cref="AttributeValue{TValue}"/>. Inherits
    /// the typeId from <see cref="AttributeValueBase"/>; adds the
    /// stored-row metadata (id, _id, projectId, timestamps) — all required.
    /// </summary>
    [JsonConverter(typeof(AttributeValueConverter))]
    public abstract class AttributeValue : AttributeValueBase
    {
        public string id { get; set; } = null!;
        public NeoTimestamp createdAt { get; set; }
        public NeoTimestamp updatedAt { get; set; }

        /// <summary>
        /// Set iff this row is an entry of an <b>unordered</b> List value
        /// (<see cref="ListAttribute.listKind"/> == "unordered"): the list
        /// VALUE id the row belongs to. Stamped at creation and immutable
        /// thereafter — membership of an unordered list is the set of live
        /// rows carrying its id here; the list value itself stores only the
        /// null-vs-present discriminator (<c>null</c> or <c>[]</c>).
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? containerId { get; set; }

        /// <summary>
        /// Storage partition stamp (specs/list-attribute-and-tilegrid-scaling.md
        /// §6): the partition this row is serialized/loaded/committed with.
        /// Absent (<c>null</c>) means the "main" partition. Stamped at creation
        /// and immutable thereafter; world grids stamp their <c>Children</c>
        /// placement subtree with <c>world:&lt;gridTypeId&gt;</c> (the grid root
        /// and its light metadata stay in main). Purely a lifecycle/serialization
        /// concern — the in-memory value graph stays one dictionary per
        /// ownership regardless of partition.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? mapKey { get; set; }

        /// <summary>
        /// Save-overlay tombstone marker. When set to
        /// <see cref="NeoValueMarks.Removed"/>, this row represents an
        /// <b>explicitly removed/emptied</b> optional value and resolves as
        /// unset/null — distinct from the row being absent (which falls through
        /// to the authored default). Null for ordinary values. Only meaningful in
        /// the Save/Session overlay stores; authored asset rows never carry it.
        /// </summary>
        public string? mark { get; set; }

        /// <summary>True when this row is a removal tombstone.</summary>
        public bool IsRemoved => mark == NeoValueMarks.Removed;
    }

    /// <summary>Well-known values for <see cref="AttributeValue.mark"/>.</summary>
    public static class NeoValueMarks
    {
        /// <summary>An optional value that the save explicitly emptied (resolves as unset, not default).</summary>
        public const string Removed = "removed";
    }

    /// <summary>
    /// Typed stored-row intermediate. Concrete shapes extend this with
    /// the already-nullable <typeparamref name="TValue"/> — e.g.
    /// <c>NumberAttributeValue : AttributeValue&lt;double?&gt;</c>.
    /// Implements <see cref="IAttributeValueBase{TValue}"/> in parallel
    /// with <see cref="AttributeValueBase{TValue}"/>, so a
    /// <c>NumberAttributeValue</c> and <c>NumberAttributeValueBase</c>
    /// share a common interface for typed value access.
    /// </summary>
    public abstract class AttributeValue<TValue>
        : AttributeValue, IAttributeValueBase<TValue>
    {
        public TValue? value { get; set; } = default!;
    }

    /// <summary>
    /// Stored value for a Null / NSGetter attribute. Typed
    /// <c>object?</c> in parallel with
    /// <see cref="NullAttributeValueBase"/>.
    /// </summary>
    public class NullAttributeValue : AttributeValue<object?> { }

    /// <summary>Stored value for a Bool attribute.</summary>
    public class BoolAttributeValue : AttributeValue<bool?> { }

    /// <summary>Stored value for an Int / Float attribute.</summary>
    public class NumberAttributeValue : AttributeValue<double?> { }

    /// <summary>Stored value for a String attribute.</summary>
    public class StringAttributeValue : AttributeValue<string?>
    {
        public NeoStringLocalizationMode? neoLocalizationMode;
    }

    /// <summary>Stored value for a List / Enum / Lookup attribute.</summary>
    public class ArrayAttributeValue : AttributeValue<string[]?> { }

    /// <summary>Stored value for a Dictionary / Custom attribute.</summary>
    public class ObjectAttributeValue : AttributeValue<Dictionary<string, string>?> { }

    /// <summary>Stored value for an Audio file attribute.</summary>
    public class FileAttributeValue : AttributeValue<FileValue?> { }

    /// <summary>Stored value for a Sprite attribute.</summary>
    public class SpriteAttributeValue : AttributeValue<SpriteValue?> { }

    /// <summary>Stored value for a Vector2 / Vector2Int attribute.</summary>
    public class Vector2AttributeValue : AttributeValue<NeoVector2Value?> { }

    /// <summary>Stored value for a Vector3 / Vector3Int attribute.</summary>
    public class Vector3AttributeValue : AttributeValue<NeoVector3Value?> { }

    /// <summary>
    /// Two-mode dispatch converter for <see cref="AttributeValue"/>.
    /// Same dual logic as <see cref="AttributeValueBaseConverter"/>,
    /// scoped to the stored-row hierarchy: TValue dispatch when the
    /// field is the typed intermediate <see cref="AttributeValue{TValue}"/>;
    /// shape dispatch when the field is the non-generic
    /// <see cref="AttributeValue"/> (e.g., as the value type of the
    /// <c>values</c> map). Read-only — default Newtonsoft serialization
    /// handles writes.
    /// </summary>
    public class AttributeValueConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            typeof(AttributeValue).IsAssignableFrom(objectType);

        public override bool CanWrite => false;

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var obj = JObject.Load(reader);
            var concrete =
                TypedHierarchyMap.ResolveByContext(objectType, typeof(AttributeValue<>))
                ?? ResolveByShape(obj["value"]);
            var instance = Activator.CreateInstance(concrete);
            using (var subReader = obj.CreateReader())
            {
                serializer.Populate(subReader, instance);
            }
            return instance;
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotImplementedException(
                "AttributeValueConverter is read-only; default serialization handles writes.");
        }

        private static Type ResolveByShape(JToken? token)
        {
            if (token == null) return typeof(NullAttributeValue);
            switch (token.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return typeof(NullAttributeValue);
                case JTokenType.Boolean:
                    return typeof(BoolAttributeValue);
                case JTokenType.Integer:
                case JTokenType.Float:
                    return typeof(NumberAttributeValue);
                case JTokenType.String:
                    return typeof(StringAttributeValue);
                case JTokenType.Array:
                    return typeof(ArrayAttributeValue);
                case JTokenType.Object:
                    if (NeoVector3ValueConverter.LooksLikeVector3Value(token)) return typeof(Vector3AttributeValue);
                    if (NeoVector2ValueConverter.LooksLikeVector2Value(token)) return typeof(Vector2AttributeValue);
                    if (LooksLikeSpriteValue(token)) return typeof(SpriteAttributeValue);
                    if (LooksLikeFileValue(token)) return typeof(FileAttributeValue);
                    return typeof(ObjectAttributeValue);
                default:
                    return typeof(NullAttributeValue);
            }
        }

        private static bool LooksLikeFileValue(JToken token)
        {
            return token.Type == JTokenType.Object &&
                token["fileId"]?.Type == JTokenType.String;
        }

        private static bool LooksLikeSpriteValue(JToken token)
        {
            return LooksLikeFileValue(token) &&
                token["sliceIndex"] != null &&
                (token["sliceIndex"]!.Type == JTokenType.Integer ||
                    token["sliceIndex"]!.Type == JTokenType.Float);
        }
    }

    // -------------------------------------------------------------------------
    // Shared TValue → concrete-type lookup. Both converters use this to
    // resolve a typed-intermediate field type (e.g. AttributeValueBase<bool?>)
    // to the matching closed concrete (BoolAttributeValueBase). Reflected
    // once at first use and cached.
    // -------------------------------------------------------------------------

    internal static class TypedHierarchyMap
    {
        private static readonly Dictionary<Type, Type> _cache = new();
        private static readonly object _lock = new();

        /// <summary>
        /// If <paramref name="objectType"/> is a closed generic of
        /// <paramref name="genericIntermediate"/> (e.g.
        /// <c>AttributeValueBase&lt;bool?&gt;</c> for the
        /// <c>AttributeValueBase&lt;&gt;</c> intermediate), returns the
        /// concrete subclass that extends that closed generic. Returns
        /// <c>null</c> if <paramref name="objectType"/> isn't a closed
        /// generic of the intermediate (the caller falls back to shape
        /// dispatch). Walks the assembly once and caches results.
        /// </summary>
        public static Type? ResolveByContext(Type objectType, Type genericIntermediate)
        {
            if (!objectType.IsGenericType) return null;
            if (objectType.GetGenericTypeDefinition() != genericIntermediate) return null;

            if (_cache.TryGetValue(objectType, out var cached)) return cached;

            lock (_lock)
            {
                if (_cache.TryGetValue(objectType, out cached)) return cached;
                foreach (var t in genericIntermediate.Assembly.GetTypes())
                {
                    if (!t.IsClass || t.IsAbstract) continue;
                    if (t.BaseType == objectType)
                    {
                        _cache[objectType] = t;
                        return t;
                    }
                }
                return null;
            }
        }
    }
}
