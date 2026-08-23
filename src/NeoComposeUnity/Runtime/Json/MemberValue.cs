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
    /// <summary>
    /// P43 §1 — a <b>computed</b> default. Mirrors TS-side
    /// <c>INSInitializerBody</c>: the authored NeoScript source plus the
    /// server-compiled IR, following the <c>NSFunction</c>
    /// <c>code</c>+<c>action</c> precedent.
    ///
    /// <para><see cref="compiled"/> is optional on the wire only because a
    /// <b>client write</b> never supplies it (the server compiles). Every
    /// exported initializer carries one, so the SDK treats its absence as a
    /// stale export and says so rather than silently producing no value.</para>
    /// </summary>
    public sealed class InitializerBody
    {
        /// <summary>Authored NeoScript initializer expression source.</summary>
        public string code = null!;

        /// <summary>Server-compiled IR. Never accepted from a client write.</summary>
        public FunctionWithReturnType? compiled;
    }

    [JsonConverter(typeof(MemberValueBaseConverter))]
    public abstract class MemberValueBase : IMemberValueBase
    {
        public string? classId { get; set; }

        /// <summary>
        /// P43 §1 / P61 §3 — set iff this container is an
        /// <b>init-backed declaration</b>: the value is produced by evaluating
        /// <see cref="InitializerBody.compiled"/> when an enclosing instance is
        /// constructed rather than read from <c>value</c>.
        /// Mutually exclusive with <c>value</c>/<c>classId</c> — a computed
        /// default stores no baked value and no concrete class (both come from
        /// evaluation), and the converters reject a container carrying both.
        ///
        /// <para>Declared on the non-generic base rather than on
        /// <see cref="MemberValueBase{TValue}"/> so it is equally available on
        /// a stored <see cref="MemberValue"/> row inside a member-default
        /// declaration graph. P61 forbids <c>init</c> on instance rows: those
        /// rows arrive with their materialized <c>value</c>, concrete
        /// <c>classId</c>, and (when needed) <c>constructorArgs</c>.</para>
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public InitializerBody? init { get; set; }
    }

    /// <summary>
    /// P43 §1 — enforces the value-container discriminated union on read.
    /// Exactly one variant is present: a literal default stores <c>value</c>
    /// (and optionally <c>classId</c>); a computed default stores <c>init</c>
    /// and neither of the other two.
    /// </summary>
    internal static class InitializerVariantGuard
    {
        internal static void RejectConflictingVariant(JObject carrier, string subject)
        {
            if (carrier.Property("init") is null) return;
            if (IsPresent(carrier["value"]))
            {
                throw new JsonSerializationException(
                    $"{subject} carries both 'value' and 'init'. A computed default stores its "
                    + "initializer and no baked value; re-export the project from the current web app.");
            }
            if (IsPresent(carrier["classId"]))
            {
                throw new JsonSerializationException(
                    $"{subject} carries both 'classId' and 'init'. The concrete class of a computed "
                    + "default comes from evaluating it; re-export the project from the current web app.");
            }
            if (IsPresent(carrier["constructorArgs"]))
            {
                throw new JsonSerializationException(
                    $"{subject} carries both 'constructorArgs' and 'init'. Constructor arguments "
                    + "belong only to a materialized instance; re-export the project from the current web app.");
            }
        }

        private static bool IsPresent(JToken? token)
        {
            return token is not null
                && token.Type != JTokenType.Null
                && token.Type != JTokenType.Undefined;
        }
    }

    /// <summary>
    /// P61 §3 — validates the creation-data half of a materialized class
    /// instance. <c>constructorArgs</c> is never an alternative value variant:
    /// it supplements an already-materialized object row with a concrete class.
    /// </summary>
    internal static class ConstructorArgsGuard
    {
        internal static void Validate(JObject carrier, string subject)
        {
            JProperty? property = carrier.Property("constructorArgs");
            if (property is null || property.Value.Type == JTokenType.Null) return;
            if (property.Value is not JObject args)
            {
                throw new JsonSerializationException(
                    $"{subject} has invalid 'constructorArgs'. Evaluated constructor arguments must be an object keyed by parameter id.");
            }
            if (carrier["value"] is not JObject)
            {
                throw new JsonSerializationException(
                    $"{subject} carries 'constructorArgs' without a materialized class 'value' object. Re-export the project from the current web app.");
            }
            if (carrier["classId"]?.Type != JTokenType.String
                || string.IsNullOrWhiteSpace(carrier.Value<string>("classId")))
            {
                throw new JsonSerializationException(
                    $"{subject} carries 'constructorArgs' without a concrete 'classId'. Re-export the project from the current web app.");
            }
            foreach (JProperty argument in args.Properties())
            {
                if (string.IsNullOrWhiteSpace(argument.Name))
                {
                    throw new JsonSerializationException(
                        $"{subject} has an empty constructor parameter id in 'constructorArgs'. Re-export the project from the current web app.");
                }
            }
        }
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

    /// <summary>Carrier for an NSDelegate declaration default.</summary>
    public class DelegateMemberValueBase : MemberValueBase<NeoDelegateValue?> { }

    /// <summary>
    /// Carrier for an NSAction declaration default — the authored listener
    /// set. An absent default means the empty set (P62 §2.1).
    /// </summary>
    public class ActionMemberValueBase : MemberValueBase<NeoActionValue?> { }

    /// <summary>Carrier for an Audio file <see cref="Member.defaultValue"/>.</summary>
    public class FileMemberValueBase : MemberValueBase<FileValue?> { }

    /// <summary>Carrier for a Sprite <see cref="Member.defaultValue"/>.</summary>
    public class SpriteMemberValueBase : MemberValueBase<SpriteValue?> { }

    /// <summary>
    /// Carrier for a Variant member's declaration default — the authored
    /// `{classId, variantId}` selection (P67 §6).
    /// </summary>
    public class VariantMemberValueBase : MemberValueBase<VariantRefValue?> { }

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
    /// P42 decision D10 — a <c>$partial</c> structured-leaf envelope is legal
    /// <b>only</b> inside an animation override graph, and the position is
    /// statically knowable rather than inferred from the value's shape or the
    /// member's kind. A <see cref="Member.defaultValue"/> is never an override
    /// graph, so an envelope there is invalid data and is rejected by name.
    ///
    /// <para>There is deliberately no <c>PartialLeafMemberValueBase</c> carrier
    /// to hold one. An earlier revision declared it "so an envelope reaching
    /// the embedded-carrier converter resolves to a row that can report a
    /// precise error" — but nothing raised that error, so the envelope was
    /// swallowed: a stray <c>$partial</c> under a Sprite declaration
    /// deserialized into a <see cref="SpriteValue"/> with a null
    /// <c>fileId</c>, i.e. silently became "no value". The error is raised
    /// here instead, which leaves the carrier with nothing to carry.</para>
    /// </summary>
    internal static class PartialLeafPositionGuard
    {
        /// <summary>
        /// Rejects a <c>$partial</c> envelope sitting in the <c>value</c> of a
        /// declaration-default carrier. <paramref name="carrier"/> is the
        /// <see cref="MemberValueBase"/> JSON object; <paramref name="subject"/>
        /// names the position for the message.
        /// </summary>
        internal static void RejectDefaultCarrier(JObject? carrier, string subject)
        {
            if (carrier is null) return;
            if (!NeoPartialLeafValue.IsEnvelope(carrier["value"])) return;
            throw new JsonSerializationException(
                $"{subject} holds a '{NeoPartialLeafValue.EnvelopeKey}' structured-leaf "
                + "value. A partial value is legal only inside an animation override graph "
                + "(the Overrides subtree of a frame or a child override), never in a "
                + "member declaration default; declare a whole value instead.");
        }

        /// <summary>
        /// Same rule, reached from <c>MemberConverter</c> where the member's
        /// own JSON is in hand — so the message can name the member and its
        /// kind, which is what decision D10 asks for. Runs before the member's
        /// fields are populated, so it wins over the carrier-level check.
        /// </summary>
        internal static void RejectMemberDeclarationDefault(JObject member, Type concrete)
        {
            if (member["defaultValue"] is not JObject carrier) return;
            RejectDefaultCarrier(carrier, DescribeMember(member, concrete));
        }

        private static string DescribeMember(JObject member, Type concrete)
        {
            string? name = member.Value<string>("name");
            string? id = member.Value<string>("id");
            string named = name is null ? concrete.Name : $"{concrete.Name} '{name}'";
            return id is null
                ? $"The default value of {named}"
                : $"The default value of {named} ({id})";
        }
    }

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
            // P42 decision D10 — a MemberValueBase is only ever a
            // Member.defaultValue, which is never an animation override
            // graph, so a `$partial` envelope here is invalid wherever it
            // came from. Raised before dispatch: the context path would
            // otherwise force-feed the envelope into the declared kind's
            // payload (a Sprite default silently becoming a SpriteValue with
            // a null fileId), and the shape path would need a carrier type
            // that exists only to fail.
            PartialLeafPositionGuard.RejectDefaultCarrier(
                obj,
                "A member declaration default");
            InitializerVariantGuard.RejectConflictingVariant(
                obj,
                "A member declaration default");
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
                    // P42: an envelope never reaches here — ReadJson rejects
                    // it above, because this is a declaration-default
                    // position (decision D10). The negative envelope guard on
                    // each probe below still matters: it keeps a
                    // {"$partial":{"fileId":"…"}} from being mistaken for a
                    // whole File value should any other caller reuse them.
                    if (NeoDelegateValueConverter.LooksLikeValue(token)) return typeof(DelegateMemberValueBase);
                    if (NeoActionValueConverter.LooksLikeValue(token)) return typeof(ActionMemberValueBase);
                    if (NeoVector3ValueConverter.LooksLikeVector3Value(token)) return typeof(Vector3MemberValueBase);
                    if (NeoVector2ValueConverter.LooksLikeVector2Value(token)) return typeof(Vector2MemberValueBase);
                    if (NeoColorValueConverter.LooksLikeColorValue(token)) return typeof(ColorMemberValueBase);
                    if (LooksLikeVariantRefValue(token)) return typeof(VariantMemberValueBase);
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

        /// <summary>
        /// P67 §6. A variant reference is exactly `classId` plus `variantId`,
        /// and `variantId` is either a string or an explicit null — which is
        /// what tells it apart from a placement sidecar that also carries a
        /// `variantId`. Exact-keyed for the same reason every other shape
        /// probe is: this decides how the row is deserialized.
        ///
        /// <para>Residual collision, unavoidable at this seam: a Dictionary
        /// member whose stored map is exactly two entries keyed `classId` and
        /// `variantId` is structurally identical to a variant reference and
        /// deserializes as one. This resolver runs on the JSON path, where the
        /// row arrives keyed by id in `values` with no member in hand, so there
        /// is nothing to discriminate on but shape - the same bound the sprite
        /// and file probes sit inside. Discriminating by member kind would mean
        /// threading the declaring member into row deserialization, a change to
        /// the export reader's contract rather than to this probe.</para>
        /// </summary>
        private static bool LooksLikeVariantRefValue(JToken token)
        {
            if (token.Type != JTokenType.Object) return false;
            var record = (JObject)token;
            if (record.Count != 2) return false;
            JToken? classId = record["classId"];
            if (classId is null || classId.Type != JTokenType.String) return false;
            JToken? variantId = record["variantId"];
            if (variantId is null) return false;
            return variantId.Type == JTokenType.String ||
                variantId.Type == JTokenType.Null;
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
        /// P61 §3 / §5.1 — evaluated arguments used to create a class
        /// instance, keyed by constructor parameter id.
        /// These are creation data, not executable source: literals remain
        /// literals, a constructed argument is the id of its materialized row,
        /// and an NSDelegate argument retains its ordinary delegate-value
        /// object. The row's <c>value</c> remains authoritative for every
        /// P75 replays these arguments to resolve omitted instance rows.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, JToken?>? constructorArgs { get; set; }

        /// <summary>
        /// P75 creation provenance. A string names the exact declared
        /// constructor; null selects the implicit new(). Historical rows omit
        /// both this field and constructorArgs.
        /// </summary>
        private string? storedInstanceConstructorId;

        [JsonIgnore]
        public bool hasInstanceConstructorId { get; private set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public string? instanceConstructorId
        {
            get => storedInstanceConstructorId;
            set
            {
                storedInstanceConstructorId = value;
                hasInstanceConstructorId = true;
            }
        }

        public bool ShouldSerializeinstanceConstructorId() =>
            hasInstanceConstructorId;

        /// <summary>P75 variant layer used to construct this instance.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? instanceVariantId { get; set; }

        /// <summary>P68 lookup row supplied to a lookup-bound variant.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? instanceVariantRowValueId { get; set; }

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

    /// <summary>Stored value for an NSDelegate member.</summary>
    public class DelegateMemberValue : MemberValue<NeoDelegateValue?> { }

    /// <summary>
    /// Stored value for an NSAction member — the live listener set that
    /// <c>+=</c> / <c>-=</c> write through (P62 §3.3).
    /// </summary>
    public class ActionMemberValue : MemberValue<NeoActionValue?> { }

    /// <summary>Stored value for an Audio file member.</summary>
    public class FileMemberValue : MemberValue<FileValue?> { }

    /// <summary>Stored value for a Sprite member.</summary>
    public class SpriteMemberValue : MemberValue<SpriteValue?> { }

    /// <summary>Stored value for a Variant member (P67 §6).</summary>
    public class VariantMemberValue : MemberValue<VariantRefValue?> { }

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
            InitializerVariantGuard.RejectConflictingVariant(
                obj,
                "A member value row");
            ConstructorArgsGuard.Validate(obj, "A member value row");
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
                    if (NeoDelegateValueConverter.LooksLikeValue(token)) return typeof(DelegateMemberValue);
                    if (NeoActionValueConverter.LooksLikeValue(token)) return typeof(ActionMemberValue);
                    if (NeoVector3ValueConverter.LooksLikeVector3Value(token)) return typeof(Vector3MemberValue);
                    if (NeoVector2ValueConverter.LooksLikeVector2Value(token)) return typeof(Vector2MemberValue);
                    if (NeoColorValueConverter.LooksLikeColorValue(token)) return typeof(ColorMemberValue);
                    if (LooksLikeVariantRefValue(token)) return typeof(VariantMemberValue);
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

        /// <summary>
        /// P67 §6. A variant reference is exactly `classId` plus `variantId`,
        /// and `variantId` is either a string or an explicit null — which is
        /// what tells it apart from a placement sidecar that also carries a
        /// `variantId`. Exact-keyed for the same reason every other shape
        /// probe is: this decides how the row is deserialized.
        ///
        /// <para>Residual collision, unavoidable at this seam: a Dictionary
        /// member whose stored map is exactly two entries keyed `classId` and
        /// `variantId` is structurally identical to a variant reference and
        /// deserializes as one. This resolver runs on the JSON path, where the
        /// row arrives keyed by id in `values` with no member in hand, so there
        /// is nothing to discriminate on but shape - the same bound the sprite
        /// and file probes sit inside. Discriminating by member kind would mean
        /// threading the declaring member into row deserialization, a change to
        /// the export reader's contract rather than to this probe.</para>
        /// </summary>
        private static bool LooksLikeVariantRefValue(JToken token)
        {
            if (token.Type != JTokenType.Object) return false;
            var record = (JObject)token;
            if (record.Count != 2) return false;
            JToken? classId = record["classId"];
            if (classId is null || classId.Type != JTokenType.String) return false;
            JToken? variantId = record["variantId"];
            if (variantId is null) return false;
            return variantId.Type == JTokenType.String ||
                variantId.Type == JTokenType.Null;
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
