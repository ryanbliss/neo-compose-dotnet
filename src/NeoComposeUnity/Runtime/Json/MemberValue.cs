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
    //   * MemberValueBase / MemberValueBase<TValue> — mirrors TS-side
    //     IMemberValueBase. Used ONLY as the carrier for
    //     Member.defaultValue. No id / timestamps / projectId — those
    //     belong to stored rows, not embedded carriers.
    //
    //   * MemberValue / MemberValue<TValue> — mirrors TS-side
    //     IMemberValue (= IMemberValueProps + IWithMongoId). Used for
    //     entries in the `values` map. Carries id / timestamps / projectId,
    //     all REQUIRED (the export only ships fully-stored rows).
    //
    //   * IMemberValueBase<TValue> — generic interface implemented by
    //     both the embedded-carrier and stored-row forms for the same
    //     payload type. Lets consumer code work generically against either
    //     form: `void SetValue<T>(IMemberValueBase<T> v, T x)`.
    //
    // Layered structure:
    //
    //   IMemberValueBase                  ← non-generic (just classId)
    //   IMemberValueBase<TValue>          ← adds typed value
    //
    //   MemberValueBase : IMemberValueBase                      (classId only)
    //   MemberValueBase<TValue> : MemberValueBase, IMemberValueBase<TValue>
    //
    //   MemberValue : MemberValueBase                           (+ id/timestamps)
    //   MemberValue<TValue> : MemberValue, IMemberValueBase<TValue>
    //
    //   <Shape>MemberValueBase : MemberValueBase<concrete-type>  (× 6)
    //   <Shape>MemberValue     : MemberValue<concrete-type>      (× 6)
    //
    // Both hierarchies are dispatched by the JSON shape of the `value` field.
    // The TS wire has no `type` discriminator on values; the type is implied
    // by the parent member. Six subclasses on each side cover the
    // possible JSON shapes (Null / Bool / Number / String / Array / Object).
    // For member kinds that share a JSON shape (Int+Float → number;
    // List+Enum+Lookup → array; Class+Dictionary → object), callers
    // disambiguate via the parent Member's `kind`.
    //
    // Per-subclass `value` is nullable: the parent member may have
    // `required: false`, in which case the stored payload can legitimately
    // be null. Nullability is encoded into the type argument itself —
    // concretes pass `bool?` / `double?` / `string?` / `string[]?` /
    // `Dictionary<string,string>?` — rather than a `TValue?` syntax that
    // requires a struct/class constraint to resolve correctly. The
    // generic just stores `TValue` directly. Reading site:
    //   ((NumberMemberValueBase)x).value      // double?
    //   ((StringMemberValueBase)x).value      // string?
    //
    // Fields are auto-properties (not public fields) because C# interface
    // members must be properties. Newtonsoft serializes properties identically
    // to fields — wire shape unchanged.
    // =========================================================================

    // -------------------------------------------------------------------------
    // Interfaces.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Common surface across every <see cref="MemberValueBase"/> and
    /// <see cref="MemberValue"/> subclass — the shared <c>classId</c>
    /// override (mirrors TS-side <c>classId?: string | null</c>). Consumers
    /// that need typed access to the polymorphic <c>value</c> field
    /// should use <see cref="IMemberValueBase{TValue}"/> instead.
    /// </summary>
    public interface IMemberValueBase
    {
        string? classId { get; set; }
    }

    /// <summary>
    /// Generic bridge across the embedded-carrier and stored-row forms
    /// for the same payload shape. Implemented by
    /// <see cref="MemberValueBase{TValue}"/> and
    /// <see cref="MemberValue{TValue}"/>, so consumer code can write
    /// one path that works on either form:
    /// <code>
    /// void Bump(IMemberValueBase&lt;double?&gt; v) =&gt; v.value = (v.value ?? 0) + 1;
    /// </code>
    /// <para>The <c>?</c> on the type argument is intentional — pass the
    /// already-nullable form (<c>double?</c>, <c>string?</c>, etc.). The
    /// interface stores <c>TValue</c> directly; nullability lives in the
    /// type argument rather than in a <c>TValue?</c> syntax that would
    /// require a struct/class constraint to resolve.</para>
    /// </summary>
    public interface IMemberValueBase<TValue> : IMemberValueBase
    {
        TValue? value { get; set; }
    }

    // -------------------------------------------------------------------------
    // MemberValueBase — embedded carrier (Member.defaultValue).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Non-generic base for <see cref="MemberValueBase{TValue}"/> —
    /// holds the polymorphism-anchor [JsonConverter] member and the
    /// classId field shared by every shape. Six concrete variants (one per
    /// JSON shape) extend either this directly (Null) or the typed
    /// <see cref="MemberValueBase{TValue}"/> intermediate.
    /// </summary>
    [JsonConverter(typeof(MemberValueBaseConverter))]
    public abstract class MemberValueBase : IMemberValueBase
    {
        public string? classId { get; set; }
    }

    /// <summary>
    /// Typed embedded-carrier intermediate. Concrete shapes extend this
    /// with the already-nullable <typeparamref name="TValue"/> — e.g.
    /// <c>NumberMemberValueBase : MemberValueBase&lt;double?&gt;</c>.
    /// </summary>
    public abstract class MemberValueBase<TValue>
        : MemberValueBase, IMemberValueBase<TValue>
    {
        public TValue? value { get; set; } = default!;
    }

    /// <summary>
    /// Carrier for a Null / NSProperty default-value. Typed
    /// <c>object?</c> so it slots into <see cref="Member{TValue}"/>
    /// for <see cref="NullMember"/> and <see cref="NSPropertyMember"/>
    /// (whose stored value is conceptually always null but still needs
    /// to fit the typed <c>defaultValue</c> field).
    /// </summary>
    public class NullMemberValueBase : MemberValueBase<object?> { }

    /// <summary>Carrier for a Bool <see cref="Member.defaultValue"/>.</summary>
    public class BoolMemberValueBase : MemberValueBase<bool?> { }

    /// <summary>
    /// Carrier for an Int / Float <see cref="Member.defaultValue"/>.
    /// Stored as <c>double?</c> to round-trip both Int and Float without
    /// loss; cast via the parent member's <c>kind</c>.
    /// </summary>
    public class NumberMemberValueBase : MemberValueBase<double?> { }

    /// <summary>Carrier for a String <see cref="Member.defaultValue"/>.</summary>
    public class StringMemberValueBase : MemberValueBase<string?>
    {
        public NeoStringLocalizationMode? neoLocalizationMode;
    }

    /// <summary>Carrier for a List / Enum / Lookup <see cref="Member.defaultValue"/>.</summary>
    public class ArrayMemberValueBase : MemberValueBase<string[]?> { }

    /// <summary>Carrier for a Dictionary / Class <see cref="Member.defaultValue"/>.</summary>
    public class ObjectMemberValueBase : MemberValueBase<Dictionary<string, string>?> { }

    /// <summary>Carrier for an Audio file <see cref="Member.defaultValue"/>.</summary>
    public class FileMemberValueBase : MemberValueBase<FileValue?> { }

    /// <summary>Carrier for a Sprite <see cref="Member.defaultValue"/>.</summary>
    public class SpriteMemberValueBase : MemberValueBase<SpriteValue?> { }

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

    /// <summary>
    /// RGBA color payload — four floats, each a finite number in
    /// <c>[0, 1]</c>. Maps 1:1 onto <c>UnityEngine.Color</c> with no
    /// scaling (specs/color-member.md decision 1).
    /// </summary>
    [JsonConverter(typeof(NeoColorValueConverter))]
    public class NeoColorValue
    {
        public float r { get; set; }
        public float g { get; set; }
        public float b { get; set; }
        public float a { get; set; }
    }

    /// <summary>Carrier for Vector2 / Vector2Int defaults.</summary>
    public class Vector2MemberValueBase : MemberValueBase<NeoVector2Value?> { }

    /// <summary>Carrier for Vector3 / Vector3Int defaults.</summary>
    public class Vector3MemberValueBase : MemberValueBase<NeoVector3Value?> { }

    /// <summary>Carrier for Color defaults.</summary>
    public class ColorMemberValueBase : MemberValueBase<NeoColorValue?> { }

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
    /// Strict read converter for <see cref="NeoColorValue"/> (mirrors
    /// <see cref="NeoVector2ValueConverter"/>): the wire value must be an
    /// object with <b>exactly</b> the keys <c>r</c>/<c>g</c>/<c>b</c>/<c>a</c>,
    /// each a finite number in <c>[0, 1]</c>. Each failure throws its own
    /// distinct message. Read-only — default serialization handles writes.
    /// </summary>
    public class NeoColorValueConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(NeoColorValue);
        }

        public override bool CanWrite => false;

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            var obj = JObject.Load(reader);
            if (obj.Count != 4)
            {
                throw new JsonSerializationException(
                    "Color value must have exactly the numeric fields 'r', 'g', 'b', and 'a'.");
            }
            return new NeoColorValue
            {
                r = ReadColorComponent(obj, "r"),
                g = ReadColorComponent(obj, "g"),
                b = ReadColorComponent(obj, "b"),
                a = ReadColorComponent(obj, "a"),
            };
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotImplementedException(
                "NeoColorValueConverter is read-only; default serialization handles writes.");
        }

        internal static bool LooksLikeColorValue(JToken token)
        {
            if (token.Type != JTokenType.Object) return false;
            var obj = (JObject)token;
            return obj.Count == 4
                && IsColorComponent(obj["r"])
                && IsColorComponent(obj["g"])
                && IsColorComponent(obj["b"])
                && IsColorComponent(obj["a"]);
        }

        internal static float ReadColorComponent(JObject obj, string key)
        {
            var token = obj[key];
            if (token is null)
            {
                throw new JsonSerializationException(
                    $"Color value is missing '{key}'.");
            }
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                throw new JsonSerializationException(
                    $"Color component '{key}' must be a number.");
            }
            var value = token.Value<float>();
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new JsonSerializationException(
                    $"Color component '{key}' must be a finite number.");
            }
            if (value < 0f)
            {
                throw new JsonSerializationException(
                    $"Color component '{key}' must not be less than 0.");
            }
            if (value > 1f)
            {
                throw new JsonSerializationException(
                    $"Color component '{key}' must not be greater than 1.");
            }
            return value;
        }

        private static bool IsColorComponent(JToken? token)
        {
            if (token == null) return false;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                return false;
            }
            var value = token.Value<float>();
            if (float.IsNaN(value) || float.IsInfinity(value)) return false;
            return value >= 0f && value <= 1f;
        }
    }

    /// <summary>
    /// Two-mode dispatch converter for <see cref="MemberValueBase"/>.
    ///
    /// <para><b>Context-aware (TValue) dispatch</b> when the field is
    /// the typed intermediate <see cref="MemberValueBase{TValue}"/>
    /// — typically <see cref="Member{TValue}.defaultValue"/>. The
    /// converter resolves the concrete subclass from the closed generic
    /// parameter (e.g. <c>MemberValueBase&lt;bool?&gt;</c> →
    /// <see cref="BoolMemberValueBase"/>), independent of the wire's
    /// <c>value</c> shape. This means a typed
    /// <see cref="StringMember.defaultValue"/> with wire
    /// <c>{"value": null}</c> produces a
    /// <see cref="StringMemberValueBase"/> with <c>value = null</c>
    /// — not a <see cref="NullMemberValueBase"/> — preserving the
    /// typed identity even when the payload is missing.</para>
    ///
    /// <para><b>Shape dispatch</b> when the field is the non-generic
    /// <see cref="MemberValueBase"/> (or <see cref="MemberValue"/>
    /// for the stored-row converter). The wire <c>value</c> token's
    /// JSON shape selects the concrete; null routes to the Null*
    /// concrete. This is the only available signal when no typing
    /// context flows from the field.</para>
    ///
    /// <para>Read-only — default Newtonsoft serialization handles
    /// writes.</para>
    /// </summary>
    public class MemberValueBaseConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            // Anchor on MemberValueBase but explicitly NOT on the
            // MemberValue subhierarchy. MemberValue inherits from
            // MemberValueBase, so without this guard Newtonsoft would
            // route stored-row instances through this converter and lose
            // their id/timestamps. The MemberValueConverter declared
            // on MemberValue takes precedence for those.
            if (typeof(MemberValue).IsAssignableFrom(objectType)) return false;
            return typeof(MemberValueBase).IsAssignableFrom(objectType);
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
            RejectRemovedClassIdentityField(obj);
            var concrete =
                TypedHierarchyMap.ResolveByContext(objectType, typeof(MemberValueBase<>))
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

        internal static void RejectRemovedClassIdentityField(JObject obj)
        {
            Schema8LegacyFieldGuard.RejectRemovedMemberValueTypeId(obj);
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotImplementedException(
                "MemberValueBaseConverter is read-only; default serialization handles writes.");
        }

        private static Type ResolveByShape(JToken? token)
        {
            if (token == null) return typeof(NullMemberValueBase);
            switch (token.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return typeof(NullMemberValueBase);
                case JTokenType.Boolean:
                    return typeof(BoolMemberValueBase);
                case JTokenType.Integer:
                case JTokenType.Float:
                    return typeof(NumberMemberValueBase);
                case JTokenType.String:
                    return typeof(StringMemberValueBase);
                case JTokenType.Array:
                    return typeof(ArrayMemberValueBase);
                case JTokenType.Object:
                    if (NeoVector3ValueConverter.LooksLikeVector3Value(token)) return typeof(Vector3MemberValueBase);
                    if (NeoVector2ValueConverter.LooksLikeVector2Value(token)) return typeof(Vector2MemberValueBase);
                    if (NeoColorValueConverter.LooksLikeColorValue(token)) return typeof(ColorMemberValueBase);
                    if (LooksLikeSpriteValue(token)) return typeof(SpriteMemberValueBase);
                    if (LooksLikeFileValue(token)) return typeof(FileMemberValueBase);
                    return typeof(ObjectMemberValueBase);
                default:
                    return typeof(NullMemberValueBase);
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
    // MemberValue — stored row (entries in the `values` map).
    // Mirrors TS-side IMemberValue (= IMemberValueProps + IWithMongoId).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Non-generic base for <see cref="MemberValue{TValue}"/>. Inherits
    /// the classId from <see cref="MemberValueBase"/>; adds the
    /// stored-row metadata (id, _id, projectId, timestamps) — all required.
    /// </summary>
    [JsonConverter(typeof(MemberValueConverter))]
    public abstract class MemberValue : MemberValueBase
    {
        public string id { get; set; } = null!;
        public NeoTimestamp createdAt { get; set; }
        public NeoTimestamp updatedAt { get; set; }

        /// <summary>
        /// Set iff this row is an entry of an <b>unordered</b> List value
        /// (<see cref="ListMember.listKind"/> == "unordered"): the list
        /// VALUE id the row belongs to. Stamped at creation and immutable
        /// thereafter — membership of an unordered list is the set of live
        /// rows carrying its id here; the list value itself stores only the
        /// null-vs-present discriminator (<c>null</c> or <c>[]</c>).
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? containerId { get; set; }

        /// <summary>
        /// Storage partition stamp (specs/list-member-and-tilegrid-scaling.md
        /// §6): the partition this row is serialized/loaded/committed with.
        /// Absent (<c>null</c>) means the "main" partition. Stamped at creation
        /// and immutable thereafter; world grids stamp their <c>Children</c>
        /// placement subtree with <c>world:&lt;gridClassId&gt;</c> (the grid root
        /// and its light metadata stay in main). Purely a lifecycle/serialization
        /// concern — the in-memory value graph stays one dictionary per
        /// ownership regardless of partition.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? mapKey { get; set; }

        /// <summary>
        /// Stable identity of the authored value row from which this
        /// instance-owned row was materialized. Object placements use this
        /// provenance to address authored Children entries exactly after
        /// each placement receives fresh value ids. It is immutable across
        /// Save/Session shadows and is never inferred from a name or list
        /// position.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? sourceValueId { get; set; }

        /// <summary>
        /// Generic-bindings stamp (specs/class-generics.md
        /// Decision 9): present on List/Dictionary value rows whose entry
        /// member subtree references generic params — the resolved
        /// terminal binding member id per referenced param. Stamped at
        /// creation from the enclosing context's environment (the enclosing
        /// class value's effective classId, an enclosing stamped collection,
        /// or the SDK's in-memory document) and immutable thereafter — the
        /// same creation-time-immutable row context as
        /// <see cref="containerId"/>/<see cref="mapKey"/>. Entry reads and
        /// writes substitute the entry member through this stamp instead
        /// of requiring container context.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, string>? genericBindings { get; set; }

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

    /// <summary>Well-known values for <see cref="MemberValue.mark"/>.</summary>
    public static class NeoValueMarks
    {
        /// <summary>An optional value that the save explicitly emptied (resolves as unset, not default).</summary>
        public const string Removed = "removed";
    }

    /// <summary>
    /// Typed stored-row intermediate. Concrete shapes extend this with
    /// the already-nullable <typeparamref name="TValue"/> — e.g.
    /// <c>NumberMemberValue : MemberValue&lt;double?&gt;</c>.
    /// Implements <see cref="IMemberValueBase{TValue}"/> in parallel
    /// with <see cref="MemberValueBase{TValue}"/>, so a
    /// <c>NumberMemberValue</c> and <c>NumberMemberValueBase</c>
    /// share a common interface for typed value access.
    /// </summary>
    public abstract class MemberValue<TValue>
        : MemberValue, IMemberValueBase<TValue>
    {
        public TValue? value { get; set; } = default!;
    }

    /// <summary>
    /// Stored value for a Null / NSProperty member. Typed
    /// <c>object?</c> in parallel with
    /// <see cref="NullMemberValueBase"/>.
    /// </summary>
    public class NullMemberValue : MemberValue<object?> { }

    /// <summary>Stored value for a Bool member.</summary>
    public class BoolMemberValue : MemberValue<bool?> { }

    /// <summary>Stored value for an Int / Float member.</summary>
    public class NumberMemberValue : MemberValue<double?> { }

    /// <summary>Stored value for a String member.</summary>
    public class StringMemberValue : MemberValue<string?>
    {
        public NeoStringLocalizationMode? neoLocalizationMode;
    }

    /// <summary>Stored value for a List / Enum / Lookup member.</summary>
    public class ArrayMemberValue : MemberValue<string[]?> { }

    /// <summary>Stored value for a Dictionary / Class member.</summary>
    public class ObjectMemberValue : MemberValue<Dictionary<string, string>?> { }

    /// <summary>Stored value for an Audio file member.</summary>
    public class FileMemberValue : MemberValue<FileValue?> { }

    /// <summary>Stored value for a Sprite member.</summary>
    public class SpriteMemberValue : MemberValue<SpriteValue?> { }

    /// <summary>Stored value for a Vector2 / Vector2Int member.</summary>
    public class Vector2MemberValue : MemberValue<NeoVector2Value?> { }

    /// <summary>Stored value for a Vector3 / Vector3Int member.</summary>
    public class Vector3MemberValue : MemberValue<NeoVector3Value?> { }

    /// <summary>Stored value for a Color member.</summary>
    public class ColorMemberValue : MemberValue<NeoColorValue?> { }

    /// <summary>
    /// Two-mode dispatch converter for <see cref="MemberValue"/>.
    /// Same dual logic as <see cref="MemberValueBaseConverter"/>,
    /// scoped to the stored-row hierarchy: TValue dispatch when the
    /// field is the typed intermediate <see cref="MemberValue{TValue}"/>;
    /// shape dispatch when the field is the non-generic
    /// <see cref="MemberValue"/> (e.g., as the value type of the
    /// <c>values</c> map). Read-only — default Newtonsoft serialization
    /// handles writes.
    /// </summary>
    public class MemberValueConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            typeof(MemberValue).IsAssignableFrom(objectType);

        public override bool CanWrite => false;

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var obj = JObject.Load(reader);
            MemberValueBaseConverter.RejectRemovedClassIdentityField(obj);
            var concrete =
                TypedHierarchyMap.ResolveByContext(objectType, typeof(MemberValue<>))
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
                "MemberValueConverter is read-only; default serialization handles writes.");
        }

        private static Type ResolveByShape(JToken? token)
        {
            if (token == null) return typeof(NullMemberValue);
            switch (token.Type)
            {
                case JTokenType.Null:
                case JTokenType.Undefined:
                    return typeof(NullMemberValue);
                case JTokenType.Boolean:
                    return typeof(BoolMemberValue);
                case JTokenType.Integer:
                case JTokenType.Float:
                    return typeof(NumberMemberValue);
                case JTokenType.String:
                    return typeof(StringMemberValue);
                case JTokenType.Array:
                    return typeof(ArrayMemberValue);
                case JTokenType.Object:
                    if (NeoVector3ValueConverter.LooksLikeVector3Value(token)) return typeof(Vector3MemberValue);
                    if (NeoVector2ValueConverter.LooksLikeVector2Value(token)) return typeof(Vector2MemberValue);
                    if (NeoColorValueConverter.LooksLikeColorValue(token)) return typeof(ColorMemberValue);
                    if (LooksLikeSpriteValue(token)) return typeof(SpriteMemberValue);
                    if (LooksLikeFileValue(token)) return typeof(FileMemberValue);
                    return typeof(ObjectMemberValue);
                default:
                    return typeof(NullMemberValue);
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
    // resolve a typed-intermediate field type (e.g. MemberValueBase<bool?>)
    // to the matching closed concrete (BoolMemberValueBase). Reflected
    // once at first use and cached.
    // -------------------------------------------------------------------------

    internal static class TypedHierarchyMap
    {
        private static readonly Dictionary<Type, Type> _cache = new();
        private static readonly object _lock = new();

        /// <summary>
        /// If <paramref name="objectType"/> is a closed generic of
        /// <paramref name="genericIntermediate"/> (e.g.
        /// <c>MemberValueBase&lt;bool?&gt;</c> for the
        /// <c>MemberValueBase&lt;&gt;</c> intermediate), returns the
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
