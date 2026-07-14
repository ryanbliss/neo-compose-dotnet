// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Abstract base for the TS-side <c>IAttribute&lt;TType, TValue&gt;</c>
    /// discriminated union. One concrete variant per
    /// <see cref="AttributeType"/> — collapse every per-type field into
    /// its own subclass instead of a bag-of-fields class. Newtonsoft
    /// dispatches on the numeric <see cref="type"/> via
    /// {@link AttributeConverter}.
    ///
    /// Common fields (id, name, timestamps, etc.) live here; per-type
    /// extras (`customTypeId`, `enumId`, `entryAttributeId`, etc.) live
    /// on the concrete subclass that needs them. <c>defaultValue</c>
    /// lives on the typed <see cref="Attribute{TValue}"/> intermediate
    /// — typed access is per concrete subclass via <c>TValue</c>.
    /// </summary>
    [JsonConverter(typeof(AttributeConverter))]
    public abstract class Attribute
    {
        public string id = null!;
        public string projectId = null!;
        public string name = null!;
        public AttributeType type;
        public bool locked;
        public bool required;
        /// <summary>
        /// Whether declarations in extending custom types may override this
        /// schema member. Optional on the wire; <c>null</c> has the permanent
        /// backwards-compatible meaning <c>true</c>.
        /// </summary>
        public bool? isVirtual;
        /// <summary>
        /// Whether this declaration is an abstract schema member. Optional on
        /// the wire; only <c>true</c> is abstract. An overriding row is the
        /// implementation unless it explicitly declares this flag again.
        /// </summary>
        public bool? isAbstract;
        /// <summary>
        /// When set, this attribute is an *override* of the referenced
        /// attribute. Most other fields may be absent on overrides;
        /// missing fields resolve from the inherited attribute via the
        /// chain. Optional on the TS side.
        /// </summary>
        public string? extendsAttributeId;
        /// <summary>
        /// Optional value id pointing to this attribute's stored value.
        /// Mirrors TS-side <c>valueId?</c>. Unset for template-only
        /// attributes (e.g., a List's entryAttribute is a template, not
        /// itself a stored value).
        /// </summary>
        public string? valueId;
        /// <summary>
        /// Storage class override (specs/attribute-storage.md): "static",
        /// "save", or "session". Absent means the attribute inherits its
        /// placement parent's effective storage. Resolved through the
        /// <see cref="extendsAttributeId"/> chain like other override
        /// fields.
        /// </summary>
        public string? storage;
        /// <summary>
        /// Storage-partition declaration
        /// (specs/list-attribute-and-tilegrid-scaling.md §6): values created
        /// under this attribute are stamped with this partition key
        /// (<see cref="AttributeValue.mapKey"/>), e.g.
        /// <c>world:&lt;gridTypeId&gt;</c>. Informational on the C# side —
        /// the web app resolves and stamps partitions at creation; the
        /// runtime only reads the stamps already on value rows.
        /// </summary>
        public string? storageMap;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;

        /// <summary>
        /// Shallow member-wise copy preserving the concrete subclass.
        /// Used by <c>NeoGenericResolution.SubstituteAttribute</c> to build
        /// the substituted record (binding's type + config with the generic
        /// slot's identity fields) without a serialize/deserialize round
        /// trip. Reference-typed config (e.g. <c>defaultValue</c>) is shared
        /// with the source — the wire DTOs are read-mostly, matching the
        /// TS-side object-spread semantics.
        /// </summary>
        internal Attribute ShallowClone() => (Attribute)MemberwiseClone();
    }

    /// <summary>
    /// Typed attribute intermediate — mirrors TS-side
    /// <c>IAttribute&lt;TType, TValue&gt;</c>. Concrete subclasses
    /// extend this with the already-nullable <typeparamref name="TValue"/>
    /// matching the attribute's stored payload type — e.g.
    /// <c>BoolAttribute : Attribute&lt;bool?&gt;</c>. Hosts the typed
    /// <see cref="defaultValue"/> field, which the
    /// {@link AttributeValueBaseConverter} resolves to the matching
    /// <see cref="AttributeValueBase{TValue}"/> concrete via context
    /// dispatch (so a wire <c>{value: null}</c> on a typed attribute
    /// produces the typed concrete with <c>value = null</c> rather than
    /// the shape-dispatched <see cref="NullAttributeValueBase"/>).
    /// </summary>
    public abstract class Attribute<TValue> : Attribute
    {
        /// <summary>
        /// Default value for the attribute. Optional on the TS side
        /// (<c>defaultValue?: IAttributeValueBase&lt;TValue&gt;</c>).
        /// Strongly typed — accessing <c>.value</c> returns
        /// <typeparamref name="TValue"/>.
        /// </summary>
        public AttributeValueBase<TValue>? defaultValue;
    }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeNull</c>. <c>TValue</c> is
    /// <c>object?</c> — TS uses the literal <c>null</c> type which has
    /// no direct C# analog; <c>object?</c> with the implicit invariant
    /// "always null" is the practical equivalent.
    /// </summary>
    public class NullAttribute : Attribute<object?> { }

    /// <summary>Mirror of TS-side <c>TAttributeBool</c>.</summary>
    public class BoolAttribute : Attribute<bool?> { }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeInt</c>. Stored as <c>double?</c>
    /// (parallel to <see cref="NumberAttributeValueBase"/>) so Int and
    /// Float share the wire numeric shape. <see cref="minValue"/> and
    /// <see cref="maxValue"/> are <c>number?</c> on the TS side —
    /// nullable here so absence is distinguishable from "explicitly 0".
    /// </summary>
    public class IntAttribute : Attribute<double?>
    {
        public float? minValue;
        public float? maxValue;
    }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeFloat</c>. All three constraint
    /// fields are <c>number?</c> on the wire — nullable here.
    /// <see cref="decimalPoints"/> as <c>null</c> means "no rounding";
    /// <c>0</c> would be "round to integer".
    /// </summary>
    public class FloatAttribute : Attribute<double?>
    {
        public float? minValue;
        public float? maxValue;
        public int? decimalPoints;
    }

    /// <summary>Mirror of TS-side <c>TAttributeString</c>.</summary>
    public class StringAttribute : Attribute<string?>
    {
        public bool localizable = true;
    }

    /// <summary>Mirror of TS-side <c>TAttributeDictionary</c>.</summary>
    public class DictionaryAttribute : Attribute<Dictionary<string, string>?>
    {
        public string entryAttributeId = null!;

        /// <summary>
        /// Dictionary key kind (mirrors TS-side <c>TDictionaryKeyKind</c>):
        /// "string" (free-text keys) or "enum" (keys are option ids of the
        /// enum referenced by <see cref="keyEnumId"/>). Nullable strictly
        /// for read-compat with stale local exports: <c>null</c> means
        /// string-keyed — already-synced <c>project.json</c> files predate
        /// the web-side backfill migration; every post-migration export
        /// carries the field. Immutable after creation.
        /// </summary>
        public string? keyKind;

        /// <summary>
        /// Present iff <see cref="keyKind"/> is "enum": the enum whose
        /// option ids are the only valid keys. The value wire shape is
        /// unchanged — entry keys are still strings that happen to be
        /// option ids of this enum.
        /// </summary>
        public string? keyEnumId;
    }

    /// <summary>Well-known values for <see cref="DictionaryAttribute.keyKind"/>.</summary>
    public static class NeoDictionaryKeyKinds
    {
        public const string String = "string";
        public const string Enum = "enum";
    }

    /// <summary>
    /// One derived index declared by a List attribute. The key is read from
    /// the Custom entry field at <see cref="schemaKey"/>; <see cref="unique"/>
    /// selects zero-or-one versus zero-or-many lookup semantics.
    /// </summary>
    public sealed class ListIndexDefinition
    {
        public string schemaKey = null!;
        public bool unique;
    }

    /// <summary>Mirror of TS-side <c>TAttributeList</c>.</summary>
    public class ListAttribute : Attribute<string[]?>
    {
        public string entryAttributeId = null!;

        /// <summary>
        /// List kind (mirrors TS-side <c>TListKind</c>): "ordered"
        /// (or absent — today's behavior: entries stored inline as a
        /// <c>string[]</c> of entry value ids, array order is the list
        /// order) or "unordered" (the stored value is ONLY the
        /// null-vs-present discriminator — <c>null</c> or <c>[]</c>;
        /// membership resolves by join over
        /// <see cref="AttributeValue.containerId"/>). Immutable after
        /// creation.
        /// </summary>
        public string? listKind;

        /// <summary>
        /// Optional derived indexes over String or single-select Enum fields
        /// on Custom entries. The maps themselves are runtime-only and are
        /// never serialized.
        /// </summary>
        public ListIndexDefinition[]? indexes;
    }

    /// <summary>Well-known values for <see cref="ListAttribute.listKind"/>.</summary>
    public static class NeoListKinds
    {
        public const string Ordered = "ordered";
        public const string Unordered = "unordered";
    }

    /// <summary>Mirror of TS-side <c>TAttributeCustom</c>.</summary>
    public class CustomAttribute : Attribute<Dictionary<string, string>?>
    {
        public string customTypeId = null!;

        /// <summary>
        /// Present iff <see cref="customTypeId"/> references a type with
        /// unbound generics in scope: the constructed type's arguments,
        /// keyed by target param id (specs/custom-type-generics.md
        /// Decision 7). Complete-or-absent; immutable after creation.
        /// Absent (<c>null</c>) means a plain (non-constructed) reference —
        /// the same semantics as the web model.
        /// </summary>
        public Dictionary<string, GenericBinding>? customTypeArguments;
    }

    /// <summary>
    /// Mirror of TS-side <c>IAttributeGenericBase</c>
    /// (specs/custom-type-generics.md Decision 5). A Generic attribute is a
    /// placeholder slot: it never holds a value un-substituted (every value
    /// exists under a closed context where
    /// <c>NeoGenericResolution.SubstituteAttribute</c> has already replaced
    /// it with the terminal binding attribute), so <c>TValue</c> is
    /// <c>object?</c> and <c>defaultValue</c>/<c>required</c> are never set
    /// (both travel with the binding — Decision 10).
    /// </summary>
    public class GenericAttribute : Attribute<object?>
    {
        /// <summary>Id of a generic param in this attribute's placement scope.</summary>
        public string genericParamId = null!;
    }

    /// <summary>Mirror of TS-side <c>TAttributeEnum</c>.</summary>
    public class EnumAttribute : Attribute<string[]?>
    {
        public string enumId = null!;
        public bool multiselect;
    }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeLookup</c>.
    /// <see cref="collectionValueId"/> is <c>string | null | undefined</c>
    /// on the wire (distinct from absent — see the TS-side docs):
    /// <c>null</c> means "use the parent collection's valueId";
    /// a present id means "drill into that specific entry". Nullable
    /// here preserves both cases.
    /// </summary>
    public class LookupAttribute : Attribute<string[]?>
    {
        public string collectionAttributeId = null!;
        public string? collectionValueId;
        public bool multiselect;
    }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeDialogueLookup</c>. The stored value is a
    /// <c>string[]</c> of <c>dialogueId</c>s (like Lookup), but the candidates
    /// are the project's manually-triggerable dialogues rather than a
    /// collection's entries. <see cref="dialogueGroupId"/> optionally scopes the
    /// selectable dialogues to a single (Standard) dialogue group; <c>null</c>
    /// means any manually-triggerable dialogue.
    /// </summary>
    public class DialogueLookupAttribute : Attribute<string[]?>
    {
        public bool multiselect;
        public string? dialogueGroupId;
    }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeNSProperty</c>. The stored value is
    /// always null (the runtime computes it via <c>getter</c>), so
    /// <c>TValue</c> is <c>object?</c>. <see cref="code"/> is
    /// client-authored NeoScript; <see cref="returnTypeInfo"/> is the
    /// declared return type; <see cref="getter"/> and <see cref="setter"/>
    /// are the server-compiled IR bodies.
    /// </summary>
    public class NSPropertyAttribute : Attribute<object?>
    {
        public string code = null!;
        public TypeInfo returnTypeInfo = null!;
        public FunctionWithReturnType getter = null!;
        public string? setterCode;
        public FunctionWithReturnType? setter;
    }

    [JsonConverter(typeof(FunctionArgumentTypeInfoConverter))]
    public class FunctionArgumentTypeInfo : TypeInfo
    {
        public string name = null!;
        public string? typeId;
        public string? interfaceId;
        public string? enumId;
        public TypeInfo? entryTypeInfo;
        public string? collectionAttributeId;
        public string? collectionValueId;
        public string? keyEnumId;
        public string? listAttributeId;
        public string? ownerTypeId;
        public string? genericParamId;
        public Dictionary<string, TypeInfo>? typeArguments;
    }

    public class FunctionArgumentTypeInfoConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(FunctionArgumentTypeInfo);
        }

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            var json = JObject.Load(reader);
            var typeToken = json["type"] ?? throw new JsonSerializationException(
                "Function argument type info is missing 'type'.");
            var type = ReadArgumentType(typeToken);
            if (type == AttributeType.Function
                || type == AttributeType.NSFunction
                || type == AttributeType.Unknown
                || type == AttributeType.Void)
            {
                throw new JsonSerializationException(
                    $"Type '{typeToken}' is not valid for a Function argument.");
            }

            return new FunctionArgumentTypeInfo
            {
                name = json.Value<string>("name") ?? throw new JsonSerializationException(
                    "Function argument type info is missing 'name'."),
                type = type,
                required = json.Value<bool?>("required") ?? false,
                typeId = json.Value<string>("typeId"),
                interfaceId = json.Value<string>("interfaceId"),
                enumId = json.Value<string>("enumId"),
                entryTypeInfo = json["entryTypeInfo"]?.ToObject<TypeInfo>(serializer),
                collectionAttributeId = json.Value<string>("collectionAttributeId"),
                collectionValueId = json.Value<string>("collectionValueId"),
                keyEnumId = json.Value<string>("keyEnumId"),
                listAttributeId = json.Value<string>("listAttributeId"),
                ownerTypeId = json.Value<string>("ownerTypeId"),
                genericParamId = json.Value<string>("genericParamId"),
                typeArguments = json["typeArguments"]?.ToObject<Dictionary<string, TypeInfo>>(serializer),
            };
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }

        private static AttributeType ReadArgumentType(JToken typeToken)
        {
            if (typeToken.Type == JTokenType.String)
            {
                return typeToken.Value<string>() switch
                {
                    "Unknown" => AttributeType.Unknown,
                    "Void" => AttributeType.Void,
                    string value => throw new JsonSerializationException(
                        $"Unknown Function argument type '{value}'."),
                    _ => throw new JsonSerializationException(
                        "Function argument type is invalid."),
                };
            }
            return (AttributeType)typeToken.Value<int>();
        }
    }

    /// <summary>Mirror of TS-side <c>TAttributeFunction</c>.</summary>
    public class FunctionAttribute : Attribute<object?>
    {
        [JsonConverter(typeof(FunctionReturnTypeInfoConverter))]
        public TypeInfo returnTypeInfo = null!;
        public FunctionArgumentTypeInfo[] argumentTypes = null!;
        public bool? deferred;
    }

    /// <summary>
    /// NeoScript-backed callable schema member. Signature fields mirror
    /// <see cref="FunctionAttribute"/> while <see cref="code"/> and
    /// <see cref="action"/> carry the authored body and compiled IR.
    /// Override rows may omit any inherited field.
    /// </summary>
    public sealed class NSFunctionAttribute : Attribute<object?>
    {
        public string code = null!;

        [JsonConverter(typeof(FunctionReturnTypeInfoConverter))]
        public TypeInfo returnTypeInfo = null!;

        public FunctionArgumentTypeInfo[] argumentTypes = null!;
        public bool? deferred;
        public FunctionWithReturnType action = null!;
    }

    /// <summary>
    /// File reference payload shared by file-backed attributes.
    /// </summary>
    public class FileValue
    {
        public string fileId = null!;
    }

    /// <summary>
    /// Sprite reference payload. <see cref="sliceIndex"/> addresses the
    /// imported sprite slice for multi-sprite textures; single-sprite
    /// textures use index 0.
    /// </summary>
    public class SpriteValue : FileValue
    {
        public int sliceIndex;
    }

    /// <summary>Mirror of TS-side <c>TAttributeSprite</c>.</summary>
    public class SpriteAttribute : Attribute<SpriteValue?>
    {
        public string? templateId;
    }

    /// <summary>Mirror of TS-side <c>TAttributeAudio</c>.</summary>
    public class AudioAttribute : Attribute<FileValue?>
    {
        public string? templateId;
    }

    /// <summary>Mirror of TS-side <c>TAttributeVector2</c>.</summary>
    public class Vector2Attribute : Attribute<NeoVector2Value?> { }

    /// <summary>Mirror of TS-side <c>TAttributeVector2Int</c>.</summary>
    public class Vector2IntAttribute : Attribute<NeoVector2Value?> { }

    /// <summary>Mirror of TS-side <c>TAttributeVector3</c>.</summary>
    public class Vector3Attribute : Attribute<NeoVector3Value?> { }

    /// <summary>Mirror of TS-side <c>TAttributeVector3Int</c>.</summary>
    public class Vector3IntAttribute : Attribute<NeoVector3Value?> { }

    /// <summary>Mirror of TS-side <c>TAttributeColor</c>.</summary>
    public class ColorAttribute : Attribute<NeoColorValue?> { }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeDecimal</c>
    /// (specs/decimal-attribute.md decision 5). The value is a canonical
    /// decimal string (see <see cref="NeoDecimalValues"/>) — on the wire and
    /// in storage a decimal value reuses the String row shape
    /// (<see cref="StringAttributeValue"/>), so <c>TValue</c> is
    /// <c>string?</c>. Bounds are canonical decimal STRINGS (decision 3), not
    /// float64 — an exact bound like <c>"0.0000000000000000001"</c> means
    /// exactly that. Bounds are web-UI validation concerns only; the SDK
    /// never enforces them (parity with Float). <see cref="decimalPoints"/>
    /// is <c>number?</c> on the wire — nullable here.
    /// </summary>
    public class DecimalAttribute : Attribute<string?>
    {
        public string? minValue;
        public string? maxValue;
        public double? decimalPoints;
    }

    public class AttributeConverter : DiscriminatedConverter<Attribute>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
        {
            // The TS-side `AttributeType` is a numeric enum on the
            // wire. Newtonsoft surfaces the JSON number as a long; cast
            // through int to land on the enum.
            var value = (AttributeType)discriminator.Value<int>();
            switch (value)
            {
                case AttributeType.Null: return typeof(NullAttribute);
                case AttributeType.Bool: return typeof(BoolAttribute);
                case AttributeType.Int: return typeof(IntAttribute);
                case AttributeType.Float: return typeof(FloatAttribute);
                case AttributeType.String: return typeof(StringAttribute);
                case AttributeType.Dictionary: return typeof(DictionaryAttribute);
                case AttributeType.List: return typeof(ListAttribute);
                case AttributeType.Custom: return typeof(CustomAttribute);
                case AttributeType.Enum: return typeof(EnumAttribute);
                case AttributeType.Lookup: return typeof(LookupAttribute);
                case AttributeType.DialogueLookup: return typeof(DialogueLookupAttribute);
                case AttributeType.NSProperty: return typeof(NSPropertyAttribute);
                case AttributeType.Sprite: return typeof(SpriteAttribute);
                case AttributeType.Audio: return typeof(AudioAttribute);
                case AttributeType.Function: return typeof(FunctionAttribute);
                case AttributeType.NSFunction: return typeof(NSFunctionAttribute);
                case AttributeType.Vector2: return typeof(Vector2Attribute);
                case AttributeType.Vector2Int: return typeof(Vector2IntAttribute);
                case AttributeType.Vector3: return typeof(Vector3Attribute);
                case AttributeType.Vector3Int: return typeof(Vector3IntAttribute);
                case AttributeType.Color: return typeof(ColorAttribute);
                case AttributeType.Decimal: return typeof(DecimalAttribute);
                case AttributeType.Generic: return typeof(GenericAttribute);
                default: return null;
            }
        }
    }
}
