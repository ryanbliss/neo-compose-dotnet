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
    /// Abstract base for the TS-side <c>IMember&lt;TType, TValue&gt;</c>
    /// discriminated union. One concrete variant per
    /// <see cref="MemberKind"/> — collapse every per-kind field into
    /// its own subclass instead of a bag-of-fields class. Newtonsoft
    /// dispatches on the numeric <see cref="kind"/> via
    /// {@link MemberConverter}.
    ///
    /// Common fields (id, name, timestamps, etc.) live here; per-kind
    /// extras (`classId`, `enumId`, `entryMemberId`, etc.) live
    /// on the concrete subclass that needs them. <c>defaultValue</c>
    /// lives on the typed <see cref="Member{TValue}"/> intermediate
    /// — typed access is per concrete subclass via <c>TValue</c>.
    /// </summary>
    [JsonConverter(typeof(MemberConverter))]
    public abstract class Member
    {
        public string id = null!;
        public string projectId = null!;
        public string name = null!;
        public MemberKind kind;
        public bool locked;
        public bool required;
        /// <summary>
        /// Whether this declaration belongs to its declaring Class
        /// rather than to each Class value instance. Schema-8 exports carry
        /// this receiver classification explicitly for every member.
        /// </summary>
        public bool isStatic;
        /// <summary>
        /// Whether declarations in extending classes may override this
        /// schema member. Optional on override rows; <c>null</c> resolves
        /// through the inherited declaration.
        /// </summary>
        public bool? isVirtual;
        /// <summary>
        /// Whether this declaration is an abstract schema member. Optional on
        /// the wire; only <c>true</c> is abstract. An overriding row is the
        /// implementation unless it explicitly declares this flag again.
        /// </summary>
        public bool? isAbstract;
        /// <summary>
        /// When set, this member is an *override* of the referenced
        /// member. Most other fields may be absent on overrides;
        /// missing fields resolve from the inherited member via the
        /// chain. Optional on the TS side.
        /// </summary>
        public string? extendsMemberId;
        /// <summary>
        /// Optional value id pointing to this member's stored value.
        /// Mirrors TS-side <c>valueId?</c>. Unset for template-only
        /// members (e.g., a List's entryMember is a template, not
        /// itself a stored value).
        /// </summary>
        public string? valueId;
        /// <summary>
        /// Storage class override (specs/member-storage.md): "immutable",
        /// "save", or "session". Absent means the member inherits its
        /// placement parent's effective storage. Resolved through the
        /// <see cref="extendsMemberId"/> chain like other override
        /// fields.
        /// </summary>
        public string? storage;
        /// <summary>
        /// Storage-partition declaration
        /// (specs/list-member-and-tilegrid-scaling.md §6): values created
        /// under this member are stamped with the resolved partition key
        /// (<see cref="MemberValue.mapKey"/>). The declaration may inherit,
        /// name a fixed partition, or use <c>$parentClass</c>; runtime-created
        /// constructor/static rows resolve it at their placement boundary.
        /// </summary>
        public string? storageKey;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;

        /// <summary>
        /// Shallow member-wise copy preserving the concrete subclass.
        /// Used by <c>NeoGenericResolution.SubstituteMember</c> to build
        /// the substituted record (binding's type + config with the generic
        /// slot's identity fields) without a serialize/deserialize round
        /// trip. Reference-typed config (e.g. <c>defaultValue</c>) is shared
        /// with the source — the wire DTOs are read-mostly, matching the
        /// TS-side object-spread semantics.
        /// </summary>
        internal Member ShallowClone() => (Member)MemberwiseClone();
    }

    /// <summary>
    /// Typed member intermediate — mirrors TS-side
    /// <c>IMember&lt;TType, TValue&gt;</c>. Concrete subclasses
    /// extend this with the already-nullable <typeparamref name="TValue"/>
    /// matching the member's stored payload type — e.g.
    /// <c>BoolMember : Member&lt;bool?&gt;</c>. Hosts the typed
    /// <see cref="defaultValue"/> field, which the
    /// {@link MemberValueBaseConverter} resolves to the matching
    /// <see cref="MemberValueBase{TValue}"/> concrete via context
    /// dispatch (so a wire <c>{value: null}</c> on a typed member
    /// produces the typed concrete with <c>value = null</c> rather than
    /// the shape-dispatched <see cref="NullMemberValueBase"/>).
    /// </summary>
    public abstract class Member<TValue> : Member
    {
        /// <summary>
        /// Default value for the member. Optional on the TS side
        /// (<c>defaultValue?: IMemberValueBase&lt;TValue&gt;</c>).
        /// Strongly typed — accessing <c>.value</c> returns
        /// <typeparamref name="TValue"/>.
        /// </summary>
        public MemberValueBase<TValue>? defaultValue;
    }

    /// <summary>
    /// Mirror of TS-side <c>TMemberNull</c>. <c>TValue</c> is
    /// <c>object?</c> — TS uses the literal <c>null</c> type which has
    /// no direct C# analog; <c>object?</c> with the implicit invariant
    /// "always null" is the practical equivalent.
    /// </summary>
    public class NullMember : Member<object?> { }

    /// <summary>Mirror of TS-side <c>TMemberBool</c>.</summary>
    public class BoolMember : Member<bool?> { }

    /// <summary>
    /// Mirror of TS-side <c>TMemberInt</c>. Stored as <c>double?</c>
    /// (parallel to <see cref="NumberMemberValueBase"/>) so Int and
    /// Float share the wire numeric shape. <see cref="minValue"/> and
    /// <see cref="maxValue"/> are <c>number?</c> on the TS side —
    /// nullable here so absence is distinguishable from "explicitly 0".
    /// </summary>
    public class IntMember : Member<double?>
    {
        public float? minValue;
        public float? maxValue;
    }

    /// <summary>
    /// Mirror of TS-side <c>TMemberFloat</c>. All three constraint
    /// fields are <c>number?</c> on the wire — nullable here.
    /// <see cref="decimalPoints"/> as <c>null</c> means "no rounding";
    /// <c>0</c> would be "round to integer".
    /// </summary>
    public class FloatMember : Member<double?>
    {
        public float? minValue;
        public float? maxValue;
        public int? decimalPoints;
    }

    /// <summary>Mirror of TS-side <c>TMemberString</c>.</summary>
    public class StringMember : Member<string?>
    {
        public bool localizable = true;
    }

    /// <summary>Mirror of TS-side <c>TMemberDictionary</c>.</summary>
    public class DictionaryMember : Member<Dictionary<string, string>?>
    {
        public string entryMemberId = null!;

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

    /// <summary>Well-known values for <see cref="DictionaryMember.keyKind"/>.</summary>
    public static class NeoDictionaryKeyKinds
    {
        public const string String = "string";
        public const string Enum = "enum";
    }

    /// <summary>
    /// One derived index declared by a List member. The key is read from
    /// the Class entry field at <see cref="schemaKey"/>; <see cref="unique"/>
    /// selects zero-or-one versus zero-or-many lookup semantics.
    /// </summary>
    public sealed class ListIndexDefinition
    {
        public string schemaKey = null!;
        public bool unique;
    }

    /// <summary>Mirror of TS-side <c>TMemberList</c>.</summary>
    public class ListMember : Member<string[]?>
    {
        public string entryMemberId = null!;

        /// <summary>
        /// List kind (mirrors TS-side <c>TListKind</c>): "ordered"
        /// (or absent — today's behavior: entries stored inline as a
        /// <c>string[]</c> of entry value ids, array order is the list
        /// order) or "unordered" (the stored value is ONLY the
        /// null-vs-present discriminator — <c>null</c> or <c>[]</c>;
        /// membership resolves by join over
        /// <see cref="MemberValue.containerId"/>). Immutable after
        /// creation.
        /// </summary>
        public string? listKind;

        /// <summary>
        /// Optional derived indexes over String or single-select Enum fields
        /// on Class entries. The maps themselves are runtime-only and are
        /// never serialized.
        /// </summary>
        public ListIndexDefinition[]? indexes;
    }

    /// <summary>Well-known values for <see cref="ListMember.listKind"/>.</summary>
    public static class NeoListKinds
    {
        public const string Ordered = "ordered";
        public const string Unordered = "unordered";
    }

    /// <summary>Mirror of TS-side <c>TMemberClass</c>.</summary>
    public class ClassMember : Member<Dictionary<string, string>?>
    {
        public string classId = null!;

        /// <summary>
        /// Present iff <see cref="classId"/> references a class with
        /// unbound generics in scope: the constructed class's arguments,
        /// keyed by target param id (specs/class-generics.md
        /// Decision 7). Complete-or-absent; immutable after creation.
        /// Absent (<c>null</c>) means a plain (non-constructed) reference —
        /// the same semantics as the web model.
        /// </summary>
        public Dictionary<string, GenericBinding>? classArguments;
    }

    /// <summary>
    /// Mirror of TS-side <c>IMemberGenericBase</c>
    /// (specs/class-generics.md Decision 5). A Generic member is a
    /// placeholder slot: it never holds a value un-substituted (every value
    /// exists under a closed context where
    /// <c>NeoGenericResolution.SubstituteMember</c> has already replaced
    /// it with the terminal binding member), so <c>TValue</c> is
    /// <c>object?</c> and <c>defaultValue</c>/<c>required</c> are never set
    /// (both travel with the binding — Decision 10).
    /// </summary>
    public class GenericMember : Member<object?>
    {
        /// <summary>Id of a generic param in this member's placement scope.</summary>
        public string genericParamId = null!;
    }

    /// <summary>Mirror of TS-side <c>TMemberEnum</c>.</summary>
    public class EnumMember : Member<string[]?>
    {
        public string enumId = null!;
        public bool multiselect;
    }

    /// <summary>
    /// Mirror of TS-side <c>TMemberLookup</c>.
    /// <see cref="collectionValueId"/> is <c>string | null | undefined</c>
    /// on the wire (distinct from absent — see the TS-side docs):
    /// <c>null</c> means "use the parent collection's valueId";
    /// a present id means "drill into that specific entry". Nullable
    /// here preserves both cases.
    /// </summary>
    public class LookupMember : Member<string[]?>
    {
        public string collectionMemberId = null!;
        public string? collectionValueId;
        public bool multiselect;
    }

    /// <summary>
    /// Mirror of TS-side <c>TMemberDialogueLookup</c>. The stored value is a
    /// <c>string[]</c> of <c>dialogueId</c>s (like Lookup), but the candidates
    /// are the project's manually-triggerable dialogues rather than a
    /// collection's entries. <see cref="dialogueGroupId"/> optionally scopes the
    /// selectable dialogues to a single (Standard) dialogue group; <c>null</c>
    /// means any manually-triggerable dialogue.
    /// </summary>
    public class DialogueLookupMember : Member<string[]?>
    {
        public bool multiselect;
        public string? dialogueGroupId;
    }

    /// <summary>
    /// Mirror of TS-side <c>TMemberNSProperty</c>. The stored value is
    /// always null (the runtime computes it via <c>getter</c>), so
    /// <c>TValue</c> is <c>object?</c>. <see cref="code"/> is
    /// client-authored NeoScript; <see cref="returnTypeInfo"/> is the
    /// declared return type; <see cref="getter"/> and <see cref="setter"/>
    /// are the server-compiled IR bodies.
    /// </summary>
    public class NSPropertyMember : Member<object?>
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
        public string? classId;
        public string? interfaceId;
        public string? enumId;
        public TypeInfo? entryTypeInfo;
        public string? collectionMemberId;
        public string? collectionValueId;
        public string? keyEnumId;
        public string? listMemberId;
        public string? ownerClassId;
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
            Schema8LegacyFieldGuard.RejectRemovedReferenceFieldsShallow(
                json,
                "Function argument type info");
            Schema8LegacyFieldGuard.RejectRemovedTypeInfoTypeId(json);
            var typeToken = json["type"] ?? throw new JsonSerializationException(
                "Function argument type info is missing 'type'.");
            var type = ReadArgumentType(typeToken);
            if (type == MemberKind.Function
                || type == MemberKind.NSFunction
                || type == MemberKind.Unknown
                || type == MemberKind.Void)
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
                classId = json.Value<string>("classId"),
                interfaceId = json.Value<string>("interfaceId"),
                enumId = json.Value<string>("enumId"),
                entryTypeInfo = json["entryTypeInfo"]?.ToObject<TypeInfo>(serializer),
                collectionMemberId = json.Value<string>("collectionMemberId"),
                collectionValueId = json.Value<string>("collectionValueId"),
                keyEnumId = json.Value<string>("keyEnumId"),
                listMemberId = json.Value<string>("listMemberId"),
                ownerClassId = json.Value<string>("ownerClassId"),
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

        private static MemberKind ReadArgumentType(JToken typeToken)
        {
            if (typeToken.Type == JTokenType.String)
            {
                return typeToken.Value<string>() switch
                {
                    "Unknown" => MemberKind.Unknown,
                    "Void" => MemberKind.Void,
                    string value => throw new JsonSerializationException(
                        $"Unknown Function argument type '{value}'."),
                    _ => throw new JsonSerializationException(
                        "Function argument type is invalid."),
                };
            }
            return (MemberKind)typeToken.Value<int>();
        }
    }

    /// <summary>Mirror of TS-side <c>TMemberFunction</c>.</summary>
    public class FunctionMember : Member<object?>
    {
        [JsonConverter(typeof(FunctionReturnTypeInfoConverter))]
        public TypeInfo returnTypeInfo = null!;
        public FunctionArgumentTypeInfo[] argumentTypes = null!;
        public bool? deferred;
    }

    /// <summary>
    /// NeoScript-backed callable schema member. Signature fields mirror
    /// <see cref="FunctionMember"/> while <see cref="code"/> and
    /// <see cref="action"/> carry the authored body and compiled IR.
    /// Override rows may omit any inherited field.
    /// </summary>
    public sealed class NSFunctionMember : Member<object?>
    {
        public string code = null!;

        [JsonConverter(typeof(FunctionReturnTypeInfoConverter))]
        public TypeInfo returnTypeInfo = null!;

        public FunctionArgumentTypeInfo[] argumentTypes = null!;
        public bool? deferred;
        public FunctionWithReturnType action = null!;
    }

    /// <summary>
    /// File reference payload shared by file-backed members.
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

    /// <summary>Mirror of TS-side <c>TMemberSprite</c>.</summary>
    public class SpriteMember : Member<SpriteValue?>
    {
        public string? templateId;
    }

    /// <summary>Mirror of TS-side <c>TMemberAudio</c>.</summary>
    public class AudioMember : Member<FileValue?>
    {
        public string? templateId;
    }

    /// <summary>Mirror of TS-side <c>TMemberVector2</c>.</summary>
    public class Vector2Member : Member<NeoVector2Value?> { }

    /// <summary>Mirror of TS-side <c>TMemberVector2Int</c>.</summary>
    public class Vector2IntMember : Member<NeoVector2Value?> { }

    /// <summary>Mirror of TS-side <c>TMemberVector3</c>.</summary>
    public class Vector3Member : Member<NeoVector3Value?> { }

    /// <summary>Mirror of TS-side <c>TMemberVector3Int</c>.</summary>
    public class Vector3IntMember : Member<NeoVector3Value?> { }

    /// <summary>Mirror of TS-side <c>TMemberColor</c>.</summary>
    public class ColorMember : Member<NeoColorValue?> { }

    /// <summary>
    /// Mirror of TS-side <c>TMemberDecimal</c>
    /// (specs/decimal-member.md decision 5). The value is a canonical
    /// decimal string (see <see cref="NeoDecimalValues"/>) — on the wire and
    /// in storage a decimal value reuses the String row shape
    /// (<see cref="StringMemberValue"/>), so <c>TValue</c> is
    /// <c>string?</c>. Bounds are canonical decimal STRINGS (decision 3), not
    /// float64 — an exact bound like <c>"0.0000000000000000001"</c> means
    /// exactly that. Bounds are web-UI validation concerns only; the SDK
    /// never enforces them (parity with Float). <see cref="decimalPoints"/>
    /// is <c>number?</c> on the wire — nullable here.
    /// </summary>
    public class DecimalMember : Member<string?>
    {
        public string? minValue;
        public string? maxValue;
        public double? decimalPoints;
    }

    public class MemberConverter : DiscriminatedConverter<Member>
    {
        protected override string DiscriminatorField => "kind";

        protected override void ValidateObjectBeforeDiscriminator(JObject obj)
        {
            Schema8LegacyFieldGuard.RejectRemovedMemberTypeField(obj);
        }

        protected override void ValidateObject(JObject obj, Type concrete)
        {
            if (!obj.TryGetValue("isStatic", out var isStatic))
            {
                throw new JsonSerializationException(
                    $"Missing required field 'isStatic' on {concrete.Name}.");
            }
            if (isStatic.Type != JTokenType.Boolean)
            {
                throw new JsonSerializationException(
                    $"Field 'isStatic' on {concrete.Name} must be a boolean.");
            }
        }

        protected override Type? ResolveSubclass(JToken discriminator)
        {
            // The TS-side `MemberKind` is a numeric enum on the
            // wire. Newtonsoft surfaces the JSON number as a long; cast
            // through int to land on the enum.
            var value = (MemberKind)discriminator.Value<int>();
            switch (value)
            {
                case MemberKind.Null: return typeof(NullMember);
                case MemberKind.Bool: return typeof(BoolMember);
                case MemberKind.Int: return typeof(IntMember);
                case MemberKind.Float: return typeof(FloatMember);
                case MemberKind.String: return typeof(StringMember);
                case MemberKind.Dictionary: return typeof(DictionaryMember);
                case MemberKind.List: return typeof(ListMember);
                case MemberKind.Class: return typeof(ClassMember);
                case MemberKind.Enum: return typeof(EnumMember);
                case MemberKind.Lookup: return typeof(LookupMember);
                case MemberKind.DialogueLookup: return typeof(DialogueLookupMember);
                case MemberKind.NSProperty: return typeof(NSPropertyMember);
                case MemberKind.Sprite: return typeof(SpriteMember);
                case MemberKind.Audio: return typeof(AudioMember);
                case MemberKind.Function: return typeof(FunctionMember);
                case MemberKind.NSFunction: return typeof(NSFunctionMember);
                case MemberKind.Vector2: return typeof(Vector2Member);
                case MemberKind.Vector2Int: return typeof(Vector2IntMember);
                case MemberKind.Vector3: return typeof(Vector3Member);
                case MemberKind.Vector3Int: return typeof(Vector3IntMember);
                case MemberKind.Color: return typeof(ColorMember);
                case MemberKind.Decimal: return typeof(DecimalMember);
                case MemberKind.Generic: return typeof(GenericMember);
                default: return null;
            }
        }
    }
}
