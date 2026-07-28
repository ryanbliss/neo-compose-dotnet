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

    /// <summary>
    /// Carrier for a P42 <c>$partial</c> structured-leaf envelope in a
    /// <see cref="Member.defaultValue"/> position. Declared for parity with
    /// <see cref="PartialLeafMemberValue"/> so an envelope reaching the
    /// embedded-carrier converter resolves to a row that can hold it and
    /// report a precise error, rather than being force-fed into
    /// <see cref="ObjectMemberValueBase"/>'s
    /// <c>Dictionary&lt;string, string&gt;</c>. Authored defaults are never
    /// legitimately partial — a partial is only legal inside an animation
    /// override graph.
    /// </summary>
    public class PartialLeafMemberValueBase : MemberValueBase<NeoPartialLeafValue?> { }

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
            // P42: a `$partial` envelope is never a whole value, whatever
            // else it carries. The exact-count rule below already excludes
            // the canonical one-key envelope; this makes the exclusion
            // explicit rather than incidental.
            if (NeoPartialLeafValue.IsEnvelope(token)) return false;
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
            // P42: see NeoVector2ValueConverter.LooksLikeVector2Value.
            if (NeoPartialLeafValue.IsEnvelope(token)) return false;
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
            // P42: see NeoVector2ValueConverter.LooksLikeVector2Value.
            if (NeoPartialLeafValue.IsEnvelope(token)) return false;
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

    // -------------------------------------------------------------------------
    // P42 — partial structured-leaf values.
    // -------------------------------------------------------------------------

    /// <summary>
    /// The payload of a <b>partial</b> structured-leaf value row (P42
    /// decision D1). On the wire it is an explicit envelope:
    ///
    /// <code>
    /// { "$partial": { "sliceIndex": 1 } }
    /// </code>
    ///
    /// <para>A row holding a <b>full</b> value is unchanged —
    /// <c>{"fileId":…,"sliceIndex":…}</c>, <c>{"x":…,"y":…,"z":…}</c>,
    /// <c>{"r":…,"g":…,"b":…,"a":…}</c>. The envelope exists because
    /// <c>ResolveByShape</c> picks a row type from JSON shape alone, with no
    /// member kind in hand: a bare <c>{"fileId":"…"}</c> sprite partial is
    /// byte-identical to a whole Audio/File value, and <c>{"sliceIndex":1}</c>
    /// or <c>{"y":0.25}</c> matches no probe at all. One discriminating key
    /// makes the signal unambiguous without changing any existing row's
    /// bytes.</para>
    ///
    /// <para>An empty envelope (<c>{"$partial":{}}</c>) is legal and means
    /// "no change".</para>
    ///
    /// <para>This is a plain data row and performs <b>no</b> resolution: it
    /// answers exactly two questions cheaply — <see cref="FieldKeys"/> ("which
    /// fields do you write", in wire order) and the <c>TryGet*</c> family
    /// ("what is the value for field K"). It deliberately does not know which
    /// field names are legal for which member kind, because the kind is not
    /// available at this layer; per-kind key validation (Sprite =
    /// <c>fileId</c>/<c>sliceIndex</c>; Vector2(Int) = <c>x</c>/<c>y</c>;
    /// Vector3(Int) = <c>x</c>/<c>y</c>/<c>z</c>; Color =
    /// <c>r</c>/<c>g</c>/<c>b</c>/<c>a</c>) and the colour channel
    /// <c>[0, 1]</c> range rule (P42 decision D2 — <b>rejected</b>, never
    /// clamped) belong to the kind-aware consumer.</para>
    ///
    /// <para>Field values are held as their original JSON tokens so a row
    /// that is read and re-written is byte-stable: an integer stays an
    /// integer, key order is preserved.</para>
    /// </summary>
    [JsonConverter(typeof(NeoPartialLeafValueConverter))]
    public sealed class NeoPartialLeafValue
    {
        /// <summary>The single discriminating wire key, <c>"$partial"</c>.</summary>
        public const string EnvelopeKey = "$partial";

        /// <summary>
        /// The field tokens in wire order. Only scalars live here (string,
        /// finite number, or null) — enforced on read by
        /// <see cref="FromEnvelope"/> and by the setters.
        /// </summary>
        private readonly JObject fields;

        private string[]? keyCache;

        /// <summary>Creates an empty envelope — the "no change" form.</summary>
        public NeoPartialLeafValue()
        {
            fields = new JObject();
        }

        private NeoPartialLeafValue(JObject fields)
        {
            this.fields = fields;
        }

        /// <summary>Number of fields this partial writes.</summary>
        public int FieldCount => fields.Count;

        /// <summary>True for <c>{"$partial":{}}</c> — writes nothing.</summary>
        public bool IsEmpty => fields.Count == 0;

        /// <summary>
        /// The field names this partial writes, in wire order. Cached; the
        /// cache is dropped by any mutation.
        /// </summary>
        public IReadOnlyList<string> FieldKeys
        {
            get
            {
                if (keyCache is null)
                {
                    var keys = new string[fields.Count];
                    var index = 0;
                    foreach (var property in fields.Properties())
                    {
                        keys[index++] = property.Name;
                    }
                    keyCache = keys;
                }
                return keyCache;
            }
        }

        /// <summary>True when <paramref name="key"/> is written by this partial.</summary>
        public bool HasField(string key) => fields.Property(key) is not null;

        /// <summary>
        /// True when <paramref name="key"/> is written and its value is JSON
        /// null. Distinct from "absent" — an absent field is left alone, an
        /// explicit null is a write.
        /// </summary>
        public bool IsNullField(string key)
        {
            return fields.Property(key)?.Value.Type == JTokenType.Null;
        }

        /// <summary>
        /// Reads <paramref name="key"/> as a string. False when the field is
        /// absent, null, or not a string.
        /// </summary>
        public bool TryGetString(string key, out string? value)
        {
            value = null;
            var token = fields.Property(key)?.Value;
            if (token is null || token.Type != JTokenType.String) return false;
            value = token.Value<string>();
            return true;
        }

        /// <summary>
        /// Reads <paramref name="key"/> as a double. False when the field is
        /// absent or not a number.
        /// </summary>
        public bool TryGetDouble(string key, out double value)
        {
            value = 0d;
            var token = fields.Property(key)?.Value;
            if (token is null) return false;
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            {
                return false;
            }
            value = token.Value<double>();
            return true;
        }

        /// <summary>
        /// Reads <paramref name="key"/> as a float — the component type of
        /// every vector and colour DTO. False when the field is absent or not
        /// a number.
        /// </summary>
        public bool TryGetSingle(string key, out float value)
        {
            value = 0f;
            if (!TryGetDouble(key, out double raw)) return false;
            value = (float)raw;
            return true;
        }

        /// <summary>
        /// Reads <paramref name="key"/> as an int — the type of
        /// <see cref="SpriteValue.sliceIndex"/>. False when the field is
        /// absent, not a number, fractional, or outside the int range.
        /// </summary>
        public bool TryGetInt32(string key, out int value)
        {
            value = 0;
            if (!TryGetDouble(key, out double raw)) return false;
            if (raw < int.MinValue || raw > int.MaxValue) return false;
            if (raw != System.Math.Floor(raw)) return false;
            value = (int)raw;
            return true;
        }

        /// <summary>Writes (or overwrites) a string field. A null value writes JSON null.</summary>
        public void SetString(string key, string? value)
        {
            SetToken(key, value is null ? JValue.CreateNull() : new JValue(value));
        }

        /// <summary>Writes (or overwrites) a fractional number field.</summary>
        public void SetDouble(string key, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new System.ArgumentException(
                    $"Partial structured-leaf field '{key}' must be a finite number.",
                    nameof(value));
            }
            SetToken(key, new JValue(value));
        }

        /// <summary>
        /// Writes (or overwrites) an integral number field. Kept separate
        /// from <see cref="SetDouble"/> so <c>sliceIndex</c> re-serializes as
        /// <c>1</c> rather than <c>1.0</c>.
        /// </summary>
        public void SetInt32(string key, int value)
        {
            SetToken(key, new JValue((long)value));
        }

        /// <summary>Removes a field. True when one was present.</summary>
        public bool RemoveField(string key)
        {
            var removed = fields.Remove(key);
            if (removed) keyCache = null;
            return removed;
        }

        /// <summary>Deep copy — the row layer hands out no shared mutable state.</summary>
        public NeoPartialLeafValue Clone()
        {
            return new NeoPartialLeafValue((JObject)fields.DeepClone());
        }

        private void SetToken(string key, JValue token)
        {
            if (fields.Property(key) is JProperty existing)
            {
                existing.Value = token;
                return;
            }
            fields.Add(key, token);
            keyCache = null;
        }

        /// <summary>
        /// Shape probe used by both <c>ResolveByShape</c> implementations and
        /// as a negative guard on every other object probe.
        ///
        /// <para>Deliberately looser than <see cref="FromEnvelope"/>: it
        /// claims anything that is recognisably an <i>attempted</i> envelope —
        /// a <c>$partial</c> key whose value is an object, or a <c>$partial</c>
        /// key standing alone — so a malformed envelope lands on the partial
        /// row and is rejected there by name, instead of falling through to
        /// <c>ObjectMemberValue</c> and failing later with a Newtonsoft
        /// message about dictionaries.</para>
        ///
        /// <para>The "or standing alone" half is what keeps a Dictionary value
        /// row that happens to contain a <c>$partial</c> <b>string</b> entry
        /// alongside others resolving exactly as it did before P42 — dictionary
        /// and class rows are <c>Dictionary&lt;string, string&gt;</c>, so their
        /// entries are never objects.</para>
        /// </summary>
        internal static bool IsEnvelope(JToken? token)
        {
            if (token is not JObject obj) return false;
            var property = obj.Property(EnvelopeKey);
            if (property is null) return false;
            return property.Value.Type == JTokenType.Object || obj.Count == 1;
        }

        /// <summary>
        /// Validates and materializes an envelope. The member kind is not
        /// available here, so this checks the <b>shape</b> only: exactly one
        /// <c>$partial</c> key whose value is an object of scalars.
        /// </summary>
        internal static NeoPartialLeafValue FromEnvelope(JObject envelope)
        {
            if (envelope.Count != 1 || envelope.Property(EnvelopeKey) is null)
            {
                throw new JsonSerializationException(
                    "Partial structured-leaf value must be an object with exactly one "
                    + $"'{EnvelopeKey}' key; found {DescribeKeys(envelope)}.");
            }
            var inner = envelope.Property(EnvelopeKey)!.Value;
            if (inner.Type != JTokenType.Object)
            {
                throw new JsonSerializationException(
                    $"Partial structured-leaf value '{EnvelopeKey}' must be an object of "
                    + "scalar field values.");
            }
            var copied = new JObject();
            foreach (var property in ((JObject)inner).Properties())
            {
                ValidateScalarField(property);
                copied.Add(property.Name, property.Value.DeepClone());
            }
            return new NeoPartialLeafValue(copied);
        }

        /// <summary>
        /// Writes the envelope back exactly as read — same key order, same
        /// numeric token types — so a row that round-trips is byte-stable.
        /// </summary>
        internal void WriteEnvelope(JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(EnvelopeKey);
            fields.WriteTo(writer);
            writer.WriteEndObject();
        }

        private static void ValidateScalarField(JProperty property)
        {
            switch (property.Value.Type)
            {
                case JTokenType.String:
                case JTokenType.Null:
                    return;
                case JTokenType.Integer:
                case JTokenType.Float:
                    var number = property.Value.Value<double>();
                    if (double.IsNaN(number) || double.IsInfinity(number))
                    {
                        throw new JsonSerializationException(
                            $"Partial structured-leaf field '{property.Name}' must be a "
                            + "finite number.");
                    }
                    return;
                default:
                    throw new JsonSerializationException(
                        $"Partial structured-leaf field '{property.Name}' must be a string "
                        + "or a number.");
            }
        }

        private static string DescribeKeys(JObject envelope)
        {
            if (envelope.Count == 0) return "no keys";
            var names = new List<string>(envelope.Count);
            foreach (var property in envelope.Properties())
            {
                names.Add($"'{property.Name}'");
            }
            return string.Join(", ", names.ToArray());
        }
    }

    /// <summary>
    /// Read/write converter for <see cref="NeoPartialLeafValue"/>. Unlike the
    /// vector and colour converters this one <b>does</b> write — the envelope
    /// shape is not what default Newtonsoft serialization would emit, and the
    /// round-trip has to be byte-stable.
    /// </summary>
    public class NeoPartialLeafValueConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(NeoPartialLeafValue);
        }

        public override bool CanWrite => true;

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            if (reader.TokenType != JsonToken.StartObject)
            {
                throw new JsonSerializationException(
                    "Partial structured-leaf value must be an object with exactly one "
                    + $"'{NeoPartialLeafValue.EnvelopeKey}' key.");
            }
            return NeoPartialLeafValue.FromEnvelope(JObject.Load(reader));
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            if (value is not NeoPartialLeafValue partial)
            {
                writer.WriteNull();
                return;
            }
            partial.WriteEnvelope(writer);
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
                    // P42: the `$partial` envelope probe MUST come first. A
                    // bare {"fileId":"…"} sprite partial is byte-identical to
                    // a whole File value, so the envelope is the only signal
                    // that disambiguates — and a malformed envelope has to
                    // land on the partial row so its converter can reject it
                    // by name.
                    if (NeoPartialLeafValue.IsEnvelope(token)) return typeof(PartialLeafMemberValueBase);
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
            if (token.Type != JTokenType.Object) return false;
            // P42: an envelope is never a whole File/Sprite value, even if a
            // future envelope grew a sibling key.
            if (NeoPartialLeafValue.IsEnvelope(token)) return false;
            return token["fileId"]?.Type == JTokenType.String;
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
    /// Stored row holding a <b>partial</b> structured-leaf value — the P42
    /// <c>$partial</c> envelope (decision D1). Legal only for a structured
    /// leaf (Sprite / Vector2(Int) / Vector3(Int) / Color) inside an
    /// animation override graph; everywhere else it is invalid data and the
    /// kind-aware consumer must say so by name.
    ///
    /// <para>Because <c>NeoMember.Create</c> picks the node CLR type from the
    /// member <i>declaration</i> kind, a row of this type never satisfies the
    /// <c>TValue</c> of the node it lands under (a <c>NeoMemberSprite</c>
    /// wants <see cref="SpriteMemberValue"/>) — so it resolves as null
    /// through the typed accessor and is reached instead via
    /// <c>NeoMember.partialLeafValue</c>, which is untyped by construction
    /// and therefore cannot fail a cast.</para>
    /// </summary>
    public class PartialLeafMemberValue : MemberValue<NeoPartialLeafValue?> { }

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
                    // P42: the `$partial` envelope probe MUST come first —
                    // see MemberValueBaseConverter.ResolveByShape.
                    if (NeoPartialLeafValue.IsEnvelope(token)) return typeof(PartialLeafMemberValue);
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
            if (token.Type != JTokenType.Object) return false;
            // P42: see MemberValueBaseConverter.LooksLikeFileValue.
            if (NeoPartialLeafValue.IsEnvelope(token)) return false;
            return token["fileId"]?.Type == JTokenType.String;
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
