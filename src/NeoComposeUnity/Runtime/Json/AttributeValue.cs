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
    public class StringAttributeValueBase : AttributeValueBase<string?> { }

    /// <summary>Carrier for a List / Enum / Lookup <see cref="Attribute.defaultValue"/>.</summary>
    public class ArrayAttributeValueBase : AttributeValueBase<string[]?> { }

    /// <summary>Carrier for a Dictionary / Custom <see cref="Attribute.defaultValue"/>.</summary>
    public class ObjectAttributeValueBase : AttributeValueBase<Dictionary<string, string>?> { }

    /// <summary>Carrier for an Audio file <see cref="Attribute.defaultValue"/>.</summary>
    public class FileAttributeValueBase : AttributeValueBase<FileValue?> { }

    /// <summary>Carrier for a Sprite <see cref="Attribute.defaultValue"/>.</summary>
    public class SpriteAttributeValueBase : AttributeValueBase<SpriteValue?> { }

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
        [JsonConverter(typeof(TolerantStringConverter))]
        public string createdAt { get; set; } = null!;
        [JsonConverter(typeof(TolerantStringConverter))]
        public string updatedAt { get; set; } = null!;
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
    public class StringAttributeValue : AttributeValue<string?> { }

    /// <summary>Stored value for a List / Enum / Lookup attribute.</summary>
    public class ArrayAttributeValue : AttributeValue<string[]?> { }

    /// <summary>Stored value for a Dictionary / Custom attribute.</summary>
    public class ObjectAttributeValue : AttributeValue<Dictionary<string, string>?> { }

    /// <summary>Stored value for an Audio file attribute.</summary>
    public class FileAttributeValue : AttributeValue<FileValue?> { }

    /// <summary>Stored value for a Sprite attribute.</summary>
    public class SpriteAttributeValue : AttributeValue<SpriteValue?> { }

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
