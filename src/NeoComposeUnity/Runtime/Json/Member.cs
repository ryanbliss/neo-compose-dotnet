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
        /// <summary>Absent resolves through the override chain, then to Optional.</summary>
        [JsonProperty("requirement", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberRequirementKind? requirement;
        /// <summary>Declaration-local. Absent means Mutable.</summary>
        [JsonProperty("mutability", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberMutabilityKind? mutability;
        /// <summary>Absent resolves through the override chain, then to Virtual.</summary>
        [JsonProperty("modifier", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberModifierKind? modifier;
        /// <summary>
        /// Declaration-local access projection. Absent means Public.
        /// </summary>
        [JsonProperty("access", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberAccessKind? access;
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
        /// Storage class override. Absence inherits through the override
        /// member chain. A present zero stops that chain and selects placement-
        /// parent inheritance.
        /// </summary>
        [JsonProperty("storage", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberStorage? storage;
        /// <summary>
        /// P76 §1 — how this member's materialized value is distributed across
        /// storage rows. Absence is the canonical <i>automatic</i> state: it
        /// inherits through the override chain and, when the whole chain is
        /// absent, resolves from the effective concrete kind and settings
        /// (<see cref="NeoSubtreeDistribution"/>). It is NOT a synonym for
        /// <see cref="NeoSubtreeDistributionKind.Sparse"/>, so absence must
        /// survive the wire: the field serializes only when present, and an
        /// explicit ordinal zero stays distinguishable from having said
        /// nothing.
        /// </summary>
        [JsonProperty("distribution", NullValueHandling = NullValueHandling.Ignore)]
        private NeoSubtreeDistributionKind? distribution;
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
        /// Runtime-only identity of a substituted generic slot. Wire member
        /// ids remain anchored to the slot declaration, but two closed
        /// bindings can carry different kinds/defaults and therefore cannot
        /// share a declaration-default node or synthetic value row.
        /// </summary>
        [JsonIgnore]
        internal string? substitutedDeclarationIdentity;

        [JsonIgnore]
        internal string RuntimeDeclarationIdentity =>
            substitutedDeclarationIdentity ?? id;

        [JsonIgnore]
        internal NeoResolvedMemberShape? resolvedShape;

        [JsonIgnore]
        private HashSet<string>? declaredWireFields;

        [JsonIgnore]
        private Dictionary<string, object?>? originalChainResolvedFields;

        [JsonIgnore]
        private Dictionary<string, object?>? materializedChainResolvedFields;

        internal void RecordDeclaredWireFields(IEnumerable<JProperty> properties)
        {
            declaredWireFields = new HashSet<string>(StringComparer.Ordinal);
            foreach (JProperty property in properties)
            {
                declaredWireFields.Add(property.Name);
            }
        }

        internal void PrepareChainResolvedFields()
        {
            if (originalChainResolvedFields is not null)
            {
                if (materializedChainResolvedFields is not null)
                {
                    foreach (var pair in materializedChainResolvedFields)
                    {
                        if (!MemberChainResolvedFields.TryRead(this, pair.Key, out object? current)
                            || Equals(current, pair.Value))
                        {
                            continue;
                        }
                        originalChainResolvedFields[pair.Key] = current;
                        if (current is not null || !string.IsNullOrEmpty(extendsMemberId))
                        {
                            declaredWireFields!.Add(pair.Key);
                        }
                        else
                        {
                            declaredWireFields!.Remove(pair.Key);
                        }
                    }
                }
                foreach (var pair in originalChainResolvedFields)
                {
                    MemberChainResolvedFields.TryWrite(this, pair.Key, pair.Value);
                }
                materializedChainResolvedFields = null;
                return;
            }

            bool inferDeclarations = declaredWireFields is null;
            declaredWireFields ??= new HashSet<string>(StringComparer.Ordinal);
            originalChainResolvedFields = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (string field in MemberChainResolvedFields.ResolutionFields)
            {
                if (!MemberChainResolvedFields.TryRead(this, field, out object? value))
                {
                    continue;
                }
                originalChainResolvedFields[field] = value;
                if (inferDeclarations && value is not null)
                {
                    declaredWireFields.Add(field);
                }
            }
            if (inferDeclarations
                && this is LookupMember lookup
                && lookup.CollectionValueIdWasAssigned)
            {
                declaredWireFields.Add("collectionValueId");
            }
        }

        internal bool DeclaresWireField(string field)
        {
            if (declaredWireFields is not null)
            {
                return declaredWireFields.Contains(field);
            }
            if (field == "collectionValueId"
                && this is LookupMember lookup
                && lookup.CollectionValueIdWasAssigned)
            {
                return true;
            }
            return MemberChainResolvedFields.TryRead(this, field, out object? value)
                && value is not null;
        }

        internal bool TryReadOriginalChainResolvedField(
            string field,
            out object? value)
        {
            if (originalChainResolvedFields is not null)
            {
                return originalChainResolvedFields.TryGetValue(field, out value);
            }
            return MemberChainResolvedFields.TryRead(this, field, out value);
        }

        internal void RecordMaterializedChainResolvedField(string field, object? value)
        {
            materializedChainResolvedFields ??=
                new Dictionary<string, object?>(StringComparer.Ordinal);
            materializedChainResolvedFields[field] = value;
        }

        private bool ShouldSerializeChainResolvedField(string field)
        {
            if (!DeclaresWireField(field)) return false;
            TryReadOriginalChainResolvedField(field, out object? value);
            return value is not null || !string.IsNullOrEmpty(extendsMemberId);
        }

        public bool ShouldSerializedefaultValue() => ShouldSerializeChainResolvedField("defaultValue");
        public bool ShouldSerializestorageKey() => ShouldSerializeChainResolvedField("storageKey");
        public bool ShouldSerializeminValue() => ShouldSerializeChainResolvedField("minValue");
        public bool ShouldSerializemaxValue() => ShouldSerializeChainResolvedField("maxValue");
        public bool ShouldSerializedecimalPoints() => ShouldSerializeChainResolvedField("decimalPoints");
        public bool ShouldSerializeindexes() => ShouldSerializeChainResolvedField("indexes");
        public bool ShouldSerializecolumnSettings() => ShouldSerializeChainResolvedField("columnSettings");
        public bool ShouldSerializeschemaKeyOrder() => ShouldSerializeChainResolvedField("schemaKeyOrder");
        public bool ShouldSerializeclassArguments() => ShouldSerializeChainResolvedField("classArguments");
        public bool ShouldSerializecollectionValueId() => ShouldSerializeChainResolvedField("collectionValueId");
        public bool ShouldSerializedeclaredTypeInfo() => ShouldSerializeChainResolvedField("declaredTypeInfo");
        public bool ShouldSerializetargetTypeInfo() => ShouldSerializeChainResolvedField("targetTypeInfo");
        public bool ShouldSerializevalueTypeInfo() => ShouldSerializeChainResolvedField("valueTypeInfo");
        public bool ShouldSerializedialogueGroupId() => ShouldSerializeChainResolvedField("dialogueGroupId");
        public bool ShouldSerializecode() => ShouldSerializeChainResolvedField("code");
        public bool ShouldSerializesetterCode() => ShouldSerializeChainResolvedField("setterCode");
        public bool ShouldSerializetemplateId() => ShouldSerializeChainResolvedField("templateId");
        public bool ShouldSerializeuiAction() => ShouldSerializeChainResolvedField("uiAction");
        public bool ShouldSerializegetter() => ShouldSerializeChainResolvedField("getter");
        public bool ShouldSerializesetter() => ShouldSerializeChainResolvedField("setter");
        public bool ShouldSerializeaction() => ShouldSerializeChainResolvedField("action");

        /// <summary>The resolved requirement used by runtime consumers.</summary>
        [JsonIgnore]
        public NeoMemberRequirementKind Requirement
        {
            get => requirement ?? resolvedShape?.Requirement ?? NeoMemberRequirementKind.Optional;
            set => DeclaredRequirement = value;
        }

        /// <summary>The declaration-local mutability used by runtime consumers.</summary>
        [JsonIgnore]
        public NeoMemberMutabilityKind Mutability
        {
            get => mutability ?? resolvedShape?.Mutability ?? NeoMemberMutabilityKind.Mutable;
            set => DeclaredMutability = value;
        }

        /// <summary>The resolved modifier used by runtime consumers.</summary>
        [JsonIgnore]
        public NeoMemberModifierKind Modifier
        {
            get => modifier ?? resolvedShape?.Modifier ?? NeoMemberModifierKind.Virtual;
            set => DeclaredModifier = value;
        }

        /// <summary>The declaration-local access used by runtime consumers.</summary>
        [JsonIgnore]
        public NeoMemberAccessKind Access
        {
            get => access ?? resolvedShape?.Access ?? NeoMemberAccessKind.Public;
            set => DeclaredAccess = value;
        }

        /// <summary>The resolved storage placement used by runtime consumers.</summary>
        [JsonIgnore]
        public NeoMemberStorage Storage
        {
            get => storage ?? resolvedShape?.Storage ?? NeoMemberStorage.Inherit;
            set => DeclaredStorage = value;
        }

        /// <summary>
        /// P76 §1 — the <b>effective</b> subtree distribution: the nearest
        /// explicit setting on the resolved member chain, else the automatic
        /// result for this member's concrete kind and settings.
        ///
        /// <para>Nullable, and never defaulted: <c>null</c> means the kind owns
        /// no stored value at all (NSProperty, Function, Interface,
        /// NSFunction), or the member is an unsubstituted
        /// <see cref="MemberKind.Generic"/> slot — an open slot genuinely has
        /// no distribution until it is closed
        /// (<see cref="NeoGenericResolution.SubstituteMember"/> supplies the
        /// binding). Reading it as <c>?? Sparse</c> would claim every scalar
        /// keeps an independent row, which is the opposite of what the table
        /// says.</para>
        ///
        /// <para>Assigning writes the DECLARED field, exactly like
        /// <see cref="Storage"/>: the automatic result is derived on every
        /// read and is never persisted back onto the record.</para>
        /// </summary>
        [JsonIgnore]
        public NeoSubtreeDistributionKind? Distribution
        {
            get
            {
                NeoSubtreeDistributionKind? declared = ChainDeclaredDistribution;
                if (declared is not null) return declared;
                if (kind == MemberKind.Generic) return null;
                return NeoSubtreeDistribution
                    .Automatic(kind, DistributionFormat, DistributionSelection)
                    .Distribution;
            }
            set => DeclaredDistribution = value;
        }

        /// <summary>
        /// The nearest EXPLICIT <c>distribution</c> on this member's resolved
        /// chain, or null when the whole chain is absent. Distinct from
        /// <see cref="Distribution"/>, which folds absence into the automatic
        /// result — this one is what a writer or a generic substitution must
        /// carry forward, because propagating an automatic result would freeze
        /// a derived answer into a record.
        /// </summary>
        [JsonIgnore]
        internal NeoSubtreeDistributionKind? ChainDeclaredDistribution =>
            distribution ?? resolvedShape?.Distribution;

        /// <summary>
        /// The §1 table's <c>String</c> condition, read through the concrete
        /// member so an inherited format decides exactly as an own declaration
        /// would. Absent means Localized, which is why every non-String kind
        /// answers Localized here — the table only consults it for String.
        /// </summary>
        [JsonIgnore]
        private NeoStringFormatKind DistributionFormat =>
            this is StringMember stringMember
                ? stringMember.Format
                : NeoStringFormatKind.Localized;

        /// <summary>
        /// The §1 table's selection-cardinality condition. Absent means Single;
        /// the table consults it only for Lookup and DialogueLookup.
        /// </summary>
        [JsonIgnore]
        private NeoMemberSelectionKind DistributionSelection => this switch
        {
            EnumMember enumMember => enumMember.Selection,
            LookupMember lookupMember => lookupMember.Selection,
            DialogueLookupMember dialogueMember => dialogueMember.Selection,
            _ => NeoMemberSelectionKind.Single,
        };

        [JsonIgnore]
        internal NeoMemberRequirementKind? DeclaredRequirement
        {
            get => requirement;
            set
            {
                requirement = value;
                resolvedShape = null;
            }
        }

        [JsonIgnore]
        internal NeoMemberMutabilityKind? DeclaredMutability
        {
            get => mutability;
            set
            {
                mutability = value;
                resolvedShape = null;
            }
        }

        [JsonIgnore]
        internal NeoMemberModifierKind? DeclaredModifier
        {
            get => modifier;
            set
            {
                modifier = value;
                resolvedShape = null;
            }
        }

        [JsonIgnore]
        internal NeoMemberAccessKind? DeclaredAccess
        {
            get => access;
            set
            {
                access = value;
                resolvedShape = null;
            }
        }

        [JsonIgnore]
        internal NeoMemberStorage? DeclaredStorage
        {
            get => storage;
            set
            {
                storage = value;
                resolvedShape = null;
            }
        }

        [JsonIgnore]
        internal NeoSubtreeDistributionKind? DeclaredDistribution
        {
            get => distribution;
            set
            {
                distribution = value;
                resolvedShape = null;
            }
        }

        protected void InvalidateResolvedShape()
        {
            resolvedShape = null;
        }

        /// <summary>
        /// Shallow member-wise copy preserving the concrete subclass.
        /// Used by <c>NeoGenericResolution.SubstituteMember</c> to build
        /// the substituted record (binding's type + config with the generic
        /// slot's identity fields) without a serialize/deserialize round
        /// trip. Reference-typed config (e.g. <c>defaultValue</c>) remains
        /// shared, matching TS object-spread semantics. Resolution metadata is
        /// copied so changes to the substituted member cannot alter its source.
        /// </summary>
        internal Member ShallowClone()
        {
            var clone = (Member)MemberwiseClone();
            clone.resolvedShape = resolvedShape?.Clone();
            clone.declaredWireFields = declaredWireFields is null
                ? null
                : new HashSet<string>(declaredWireFields, StringComparer.Ordinal);
            clone.originalChainResolvedFields = originalChainResolvedFields is null
                ? null
                : new Dictionary<string, object?>(
                    originalChainResolvedFields,
                    StringComparer.Ordinal);
            clone.materializedChainResolvedFields = materializedChainResolvedFields is null
                ? null
                : new Dictionary<string, object?>(
                    materializedChainResolvedFields,
                    StringComparer.Ordinal);
            return clone;
        }
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
        [JsonProperty("format", NullValueHandling = NullValueHandling.Ignore)]
        private NeoStringFormatKind? format;
        /// <summary>
        /// Opts this String into the per-instance search projection. A
        /// declaration-backed read-only field cannot enable it because no
        /// per-instance row exists to index.
        /// </summary>
        [JsonProperty("searchBy", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberSearchByKind? searchBy;

        [JsonIgnore]
        public NeoStringFormatKind Format
        {
            get => format ?? resolvedShape?.Format ?? NeoStringFormatKind.Localized;
            set => DeclaredFormat = value;
        }

        [JsonIgnore]
        public NeoMemberSearchByKind SearchBy
        {
            get => searchBy ?? resolvedShape?.SearchBy ?? NeoMemberSearchByKind.None;
            set => DeclaredSearchBy = value;
        }

        [JsonIgnore]
        internal NeoStringFormatKind? DeclaredFormat
        {
            get => format;
            set
            {
                format = value;
                InvalidateResolvedShape();
            }
        }

        [JsonIgnore]
        internal NeoMemberSearchByKind? DeclaredSearchBy
        {
            get => searchBy;
            set
            {
                searchBy = value;
                InvalidateResolvedShape();
            }
        }
    }

    /// <summary>Mirror of TS-side <c>TMemberDictionary</c>.</summary>
    public class DictionaryMember : Member<Dictionary<string, string>?>
    {
        public string entryMemberId = null!;

        /// <summary>
        /// Dictionary key kind. Absent means free-text string keys; Enum uses
        /// option ids from the enum referenced by <see cref="keyEnumId"/>.
        /// </summary>
        [JsonProperty("keyKind", NullValueHandling = NullValueHandling.Ignore)]
        private NeoDictionaryKeyKind? keyKind;

        [JsonIgnore]
        public NeoDictionaryKeyKind KeyKind
        {
            get => keyKind ?? resolvedShape?.DictionaryKeyKind ?? NeoDictionaryKeyKind.String;
            set => DeclaredKeyKind = value;
        }

        [JsonIgnore]
        internal NeoDictionaryKeyKind? DeclaredKeyKind
        {
            get => keyKind;
            set
            {
                keyKind = value;
                InvalidateResolvedShape();
            }
        }

        /// <summary>
        /// Present iff <see cref="keyKind"/> is Enum: the enum whose
        /// option ids are the only valid keys. The value wire shape is
        /// unchanged — entry keys are still strings that happen to be
        /// option ids of this enum.
        /// </summary>
        public string? keyEnumId;
    }

    internal static class NeoDictionaryMemberContract
    {
        internal static string? GetValidationError(
            NeoDictionaryKeyKind? keyKind,
            string? keyEnumId)
        {
            NeoDictionaryKeyKind effectiveKind =
                keyKind ?? NeoDictionaryKeyKind.String;
            if (!System.Enum.IsDefined(typeof(NeoDictionaryKeyKind), effectiveKind))
            {
                return $"has unknown keyKind ordinal '{(int)effectiveKind}'.";
            }
            if (effectiveKind == NeoDictionaryKeyKind.Enum)
            {
                if (keyEnumId is null)
                {
                    return "uses keyKind 'Enum' but is missing required field 'keyEnumId'.";
                }
                if (keyEnumId.Length == 0)
                {
                    return "uses keyKind 'Enum' but field 'keyEnumId' is empty.";
                }
                return null;
            }
            if (keyEnumId is not null)
            {
                return "uses keyKind 'String' but also defines field 'keyEnumId'.";
            }
            return null;
        }
    }

    /// <summary>
    /// One derived index declared by a List member. The key is read from
    /// the Class entry field at <see cref="schemaKey"/>; <see cref="kind"/>
    /// selects zero-or-one versus zero-or-many lookup semantics.
    /// </summary>
    public sealed class ListIndexDefinition
    {
        public string schemaKey = null!;
        [JsonProperty("kind", NullValueHandling = NullValueHandling.Ignore)]
        private NeoListIndexKind? kind;

        [JsonIgnore]
        public NeoListIndexKind Kind
        {
            get => kind ?? NeoListIndexKind.Bucket;
            set => kind = value;
        }

        [JsonIgnore]
        internal NeoListIndexKind? DeclaredKind => kind;
    }

    /// <summary>Authoring layout for one Class field shown in a List.</summary>
    public sealed class ListColumnSetting
    {
        public string memberKey = null!;
        public double? width;
        [JsonProperty("visibility", NullValueHandling = NullValueHandling.Ignore)]
        private NeoColumnVisibilityKind? visibility;
        [JsonProperty("pin", NullValueHandling = NullValueHandling.Ignore)]
        private NeoColumnPinKind? pin;
        [JsonProperty("overflow", NullValueHandling = NullValueHandling.Ignore)]
        private NeoColumnOverflowKind? overflow;

        [JsonIgnore]
        public NeoColumnVisibilityKind Visibility
        {
            get => visibility ?? NeoColumnVisibilityKind.Visible;
            set => visibility = value;
        }

        [JsonIgnore]
        public NeoColumnPinKind Pin
        {
            get => pin ?? NeoColumnPinKind.None;
            set => pin = value;
        }

        [JsonIgnore]
        public NeoColumnOverflowKind Overflow
        {
            get => overflow ?? NeoColumnOverflowKind.Clip;
            set => overflow = value;
        }
    }

    /// <summary>Mirror of TS-side <c>TMemberList</c>.</summary>
    public class ListMember : Member<string[]?>
    {
        public string entryMemberId = null!;

        /// <summary>
        /// Ordered (or absent) stores entries inline as a
        /// <c>string[]</c> of entry value ids, array order is the list
        /// order. Unordered uses the stored value only as the
        /// null-vs-present discriminator — <c>null</c> or <c>[]</c>;
        /// membership resolves by join over
        /// <see cref="MemberValue.containerId"/>). Immutable after
        /// creation.
        /// </summary>
        [JsonProperty("listKind", NullValueHandling = NullValueHandling.Ignore)]
        private NeoListKind? listKind;

        [JsonIgnore]
        public NeoListKind ListKind
        {
            get => listKind ?? resolvedShape?.ListKind ?? NeoListKind.Ordered;
            set => DeclaredListKind = value;
        }

        [JsonIgnore]
        internal NeoListKind? DeclaredListKind
        {
            get => listKind;
            set
            {
                listKind = value;
                InvalidateResolvedShape();
            }
        }

        /// <summary>
        /// Optional derived indexes over String or single-select Enum fields
        /// on Class entries. The maps themselves are runtime-only and are
        /// never serialized.
        /// </summary>
        public ListIndexDefinition[]? indexes;

        /// <summary>Optional authoring layout retained for export round trips.</summary>
        public ListColumnSetting[]? columnSettings;
    }

    /// <summary>Mirror of TS-side <c>TMemberClass</c>.</summary>
    public class ClassMember : Member<Dictionary<string, string>?>
    {
        public string classId = null!;

        /// <summary>
        /// Selects a full Class value or recursive sparse override graph.
        /// </summary>
        [JsonProperty("payload", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberPayloadKind? payload;

        [JsonIgnore]
        public NeoMemberPayloadKind Payload
        {
            get => payload ?? resolvedShape?.Payload ?? NeoMemberPayloadKind.Full;
            set => DeclaredPayload = value;
        }

        [JsonIgnore]
        internal NeoMemberPayloadKind? DeclaredPayload
        {
            get => payload;
            set
            {
                payload = value;
                InvalidateResolvedShape();
            }
        }

        /// <summary>
        /// Present iff <see cref="classId"/> references a class with
        /// unbound generics in scope: the constructed class's arguments,
        /// keyed by target param id (specs/class-generics.md
        /// Decision 7). Complete-or-absent; immutable after creation.
        /// Absent (<c>null</c>) means a plain (non-constructed) reference —
        /// the same semantics as the web model.
        /// </summary>
        public Dictionary<string, GenericBinding>? classArguments;

        /// <summary>Optional authored schema-key ordering metadata.</summary>
        public string[]? schemaKeyOrder;
    }

    /// <summary>
    /// Mirror of TS-side <c>IMemberGenericBase</c>
    /// (specs/class-generics.md Decision 5). A Generic member is a
    /// placeholder slot: it never holds a value un-substituted (every value
    /// exists under a closed context where
    /// <c>NeoGenericResolution.SubstituteMember</c> has already replaced
    /// it with the terminal binding member), so <c>TValue</c> is
    /// <c>object?</c> and <c>defaultValue</c>/<c>requirement</c> are never set
    /// (both travel with the binding — Decision 10).
    /// </summary>
    public class GenericMember : Member<object?>
    {
        /// <summary>Id of a generic param in this member's placement scope.</summary>
        public string genericParamId = null!;

        /// <summary>
        /// Selects a full value or recursive sparse Partial value.
        /// Animation system definitions use this for Partial&lt;TTarget&gt;.
        /// </summary>
        [JsonProperty("payload", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberPayloadKind? payload;

        [JsonIgnore]
        public NeoMemberPayloadKind Payload
        {
            get => payload ?? resolvedShape?.Payload ?? NeoMemberPayloadKind.Full;
            set => DeclaredPayload = value;
        }

        [JsonIgnore]
        internal NeoMemberPayloadKind? DeclaredPayload
        {
            get => payload;
            set
            {
                payload = value;
                InvalidateResolvedShape();
            }
        }
    }

    /// <summary>Mirror of TS-side <c>TMemberEnum</c>.</summary>
    public class EnumMember : Member<string[]?>
    {
        public string enumId = null!;
        [JsonProperty("selection", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberSelectionKind? selection;

        [JsonIgnore]
        public NeoMemberSelectionKind Selection
        {
            get => selection ?? resolvedShape?.Selection ?? NeoMemberSelectionKind.Single;
            set => DeclaredSelection = value;
        }

        [JsonIgnore]
        internal NeoMemberSelectionKind? DeclaredSelection
        {
            get => selection;
            set
            {
                selection = value;
                InvalidateResolvedShape();
            }
        }
    }

    /// <summary>
    /// Mirror of TS-side <c>TMemberLookup</c>.
    /// <see cref="collectionValueId"/> is <c>string | null | undefined</c>
    /// on sparse overrides. Absence inherits the ancestor declaration while a
    /// present <c>null</c> explicitly clears it. On a full declaration, null
    /// and absence both mean that the runtime resolves the collection value
    /// from its placement.
    /// </summary>
    public class LookupMember : Member<string[]?>
    {
        public string collectionMemberId = null!;
        private string? _collectionValueId;
        private bool collectionValueIdWasAssigned;

        [JsonIgnore]
        internal bool CollectionValueIdWasAssigned => collectionValueIdWasAssigned;

        public string? collectionValueId
        {
            get => _collectionValueId;
            set
            {
                _collectionValueId = value;
                collectionValueIdWasAssigned = true;
                resolvedShape?.SetChainResolvedValue("collectionValueId", value);
            }
        }

        [JsonIgnore]
        public string? CollectionValueId =>
            resolvedShape is null
                ? _collectionValueId
                : resolvedShape.GetChainResolvedValue<string>("collectionValueId");

        /// <summary>Optional declared Lookup type metadata.</summary>
        public TypeInfo? declaredTypeInfo;

        [JsonProperty("selection", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberSelectionKind? selection;

        [JsonIgnore]
        public NeoMemberSelectionKind Selection
        {
            get => selection ?? resolvedShape?.Selection ?? NeoMemberSelectionKind.Single;
            set => DeclaredSelection = value;
        }

        [JsonIgnore]
        internal NeoMemberSelectionKind? DeclaredSelection
        {
            get => selection;
            set
            {
                selection = value;
                InvalidateResolvedShape();
            }
        }
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
        [JsonProperty("selection", NullValueHandling = NullValueHandling.Ignore)]
        private NeoMemberSelectionKind? selection;

        [JsonIgnore]
        public NeoMemberSelectionKind Selection
        {
            get => selection ?? resolvedShape?.Selection ?? NeoMemberSelectionKind.Single;
            set => DeclaredSelection = value;
        }

        [JsonIgnore]
        internal NeoMemberSelectionKind? DeclaredSelection
        {
            get => selection;
            set
            {
                selection = value;
                InvalidateResolvedShape();
            }
        }
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

    /// <summary>
    /// Mirror of TS-side <c>INSParameterDefault</c> (P65 §3.1). Present iff
    /// the parameter is defaulted; a present wrapper with a null
    /// <see cref="value"/> is an explicit null default. The payload carries no
    /// kind of its own — it is interpreted by the parameter's own
    /// <c>type</c>/<c>enumId</c>: bool, integer, float, decimal canonical
    /// string, string, or a single enum option id.
    /// </summary>
    public sealed class ParameterDefaultValue
    {
        public object? value;
    }

    [JsonConverter(typeof(FunctionArgumentTypeInfoConverter))]
    public class FunctionArgumentTypeInfo : TypeInfo
    {
        public string name = null!;
        /// <summary>
        /// P65 §3.1 — the parameter's constant default. Absent means no
        /// default; <c>{ value: null }</c> is an explicit null default.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ParameterDefaultValue? defaultValue;
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
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(FunctionReturnTypeInfoConverter))]
        public TypeInfo? returnTypeInfo;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public TypeInfo[]? argumentTypes;
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
            if (type == MemberKind.Function
                || type == MemberKind.NSFunction
                || type == MemberKind.FunctionRef
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
                defaultValue = ReadParameterDefault(json["defaultValue"]),
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
                returnTypeInfo = ReadDelegateReturnType(
                    json["returnTypeInfo"],
                    serializer),
                argumentTypes = json["argumentTypes"]?.ToObject<TypeInfo[]>(serializer),
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

        /// <summary>
        /// P65 §3.1 — reads the <c>{ value }</c> default wrapper. Only the
        /// §1.2 constant scalars are legal payloads, so anything structured is
        /// a malformed export rather than a tolerable unknown.
        /// </summary>
        private static ParameterDefaultValue? ReadParameterDefault(JToken? token)
        {
            if (token is null) return null;
            if (token is not JObject wrapper)
            {
                throw new JsonSerializationException(
                    "Function argument 'defaultValue' must be an object when present.");
            }
            if (!wrapper.TryGetValue("value", out JToken? valueToken))
            {
                throw new JsonSerializationException(
                    "Function argument 'defaultValue' is missing 'value'.");
            }
            object? value = valueToken.Type switch
            {
                JTokenType.Null => null,
                JTokenType.Boolean => valueToken.Value<bool>(),
                JTokenType.Integer => valueToken.Value<long>(),
                JTokenType.Float => valueToken.Value<double>(),
                JTokenType.String => valueToken.Value<string>(),
                _ => throw new JsonSerializationException(
                    $"Function argument 'defaultValue' payload of type {valueToken.Type} is not a P65 §1.2 constant."),
            };
            return new ParameterDefaultValue { value = value };
        }

        private static TypeInfo? ReadDelegateReturnType(
            JToken? token,
            JsonSerializer serializer)
        {
            if (token is null || token.Type == JTokenType.Null) return null;
            if (token is not JObject obj)
            {
                throw new JsonSerializationException(
                    "Delegate returnTypeInfo must be an object when present.");
            }
            if (obj["type"]?.Type == JTokenType.String
                && obj["type"]!.Value<string>() == "Void")
            {
                return new VoidTypeInfo
                {
                    type = MemberKind.Void,
                    required = obj.Value<bool?>("required") ?? true,
                };
            }
            return obj.ToObject<TypeInfo>(serializer);
        }
    }

    /// <summary>Mirror of TS-side <c>TMemberFunction</c>.</summary>
    public class FunctionMember : Member<object?>
    {
        [JsonConverter(typeof(FunctionReturnTypeInfoConverter))]
        public TypeInfo returnTypeInfo = null!;
        public FunctionArgumentTypeInfo[] argumentTypes = null!;
        [JsonProperty("dispatch", NullValueHandling = NullValueHandling.Ignore)]
        private NeoFunctionDispatchKind? dispatch;

        [JsonIgnore]
        public NeoFunctionDispatchKind Dispatch
        {
            get => dispatch ?? resolvedShape?.Dispatch ?? NeoFunctionDispatchKind.Synchronous;
            set => DeclaredDispatch = value;
        }

        [JsonIgnore]
        internal NeoFunctionDispatchKind? DeclaredDispatch
        {
            get => dispatch;
            set
            {
                dispatch = value;
                InvalidateResolvedShape();
            }
        }
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

        /// <summary>
        /// Optional authored body mode. Absent means custom NeoScript;
        /// UI means <see cref="uiAction"/> is the logic-builder IR.
        /// </summary>
        [JsonProperty("bodyMode", NullValueHandling = NullValueHandling.Ignore)]
        private NeoFunctionBodyKind? bodyMode;

        /// <summary>
        /// Single-instruction UI action retained for round trip. The compiled
        /// executable body remains <see cref="action"/>.
        /// </summary>
        public FunctionWithReturnType? uiAction;

        [JsonConverter(typeof(FunctionReturnTypeInfoConverter))]
        public TypeInfo returnTypeInfo = null!;

        public FunctionArgumentTypeInfo[] argumentTypes = null!;
        [JsonProperty("dispatch", NullValueHandling = NullValueHandling.Ignore)]
        private NeoFunctionDispatchKind? dispatch;

        [JsonIgnore]
        public NeoFunctionDispatchKind Dispatch
        {
            get => dispatch ?? resolvedShape?.Dispatch ?? NeoFunctionDispatchKind.Synchronous;
            set => DeclaredDispatch = value;
        }

        [JsonIgnore]
        public NeoFunctionBodyKind BodyMode
        {
            get => bodyMode ?? resolvedShape?.BodyMode ?? NeoFunctionBodyKind.Code;
            set => DeclaredBodyMode = value;
        }

        [JsonIgnore]
        internal NeoFunctionDispatchKind? DeclaredDispatch
        {
            get => dispatch;
            set
            {
                dispatch = value;
                InvalidateResolvedShape();
            }
        }

        [JsonIgnore]
        internal NeoFunctionBodyKind? DeclaredBodyMode
        {
            get => bodyMode;
            set
            {
                bodyMode = value;
                InvalidateResolvedShape();
            }
        }
        public FunctionWithReturnType action = null!;
    }

    /// <summary>
    /// First-class callable value with a persisted positional signature.
    /// Values are either compiled closures or bound callable-member targets.
    /// </summary>
    public sealed class DelegateMember : Member<NeoDelegateValue?>
    {
        [JsonConverter(typeof(FunctionReturnTypeInfoConverter))]
        public TypeInfo returnTypeInfo = null!;
        public FunctionArgumentTypeInfo[] argumentTypes = null!;
    }

    /// <summary>
    /// Multicast void callable with a persisted positional signature (P62
    /// §2.1). There is no return slot — an action is always void — and the
    /// value is an insertion-ordered listener set of member targets. An
    /// omitted <see cref="Member{TValue}.defaultValue"/> means the empty
    /// set, never null.
    /// </summary>
    public sealed class ActionMember : Member<NeoActionValue?>
    {
        public FunctionArgumentTypeInfo[] argumentTypes = null!;
    }

    /// <summary>
    /// Reference to a callable member.
    /// </summary>
    public sealed class FunctionRefMember : Member<Dictionary<string, string>?> { }

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

    /// <summary>
    /// P67 §6. The `{classId, variantId}` pair a `NeoVariant&lt;TTarget&gt;`
    /// member stores. A null <see cref="variantId"/> selects the base entry —
    /// the class itself with no variant applied — and is a real selection, not
    /// an absent one.
    /// </summary>
    public class VariantRefValue
    {
        public string classId = string.Empty;
        public string? variantId;
        /// <summary>
        /// P68 §6 — required for a lookup variant projected through a plain
        /// <c>NeoVariant&lt;T&gt;</c> member; null for plain and unbound handles.
        /// </summary>
        public string? rowValueId;
    }

    /// <summary>Mirror of TS-side <c>TMemberVariant</c>.</summary>
    public class VariantMember : Member<VariantRefValue?>
    {
        public TypeInfo? targetTypeInfo;
        public TypeInfo? valueTypeInfo;
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
            RecordShapeContractGuard.ValidateMemberRecord(obj);
        }

        protected override void ValidateObject(JObject obj, Type concrete)
        {
            // P42 decision D10 — a declaration default is never an animation
            // override graph, so a `~partial` envelope in one is invalid.
            // MemberValueBaseConverter raises the same error, but only this
            // site can name the member and its kind.
            PartialLeafPositionGuard.RejectMemberDeclarationDefault(obj, concrete);
            if (concrete == typeof(DictionaryMember))
            {
                ValidateDictionaryKeyContract(obj);
            }
        }

        private static void ValidateDictionaryKeyContract(JObject obj)
        {
            JToken? keyKindToken = obj["keyKind"];
            if (keyKindToken is not null
                && keyKindToken.Type != JTokenType.Null
                && keyKindToken.Type != JTokenType.Integer)
            {
                throw new JsonSerializationException(
                    "Dictionary member field 'keyKind' must be a numeric enum ordinal.");
            }
            JToken? keyEnumIdToken = obj["keyEnumId"];
            if (keyEnumIdToken is not null
                && keyEnumIdToken.Type != JTokenType.Null
                && keyEnumIdToken.Type != JTokenType.String)
            {
                throw new JsonSerializationException(
                    "Dictionary member field 'keyEnumId' must be a string or null.");
            }

            string? error = NeoDictionaryMemberContract.GetValidationError(
                keyKindToken?.Type == JTokenType.Integer
                    ? (NeoDictionaryKeyKind?)keyKindToken.Value<int>()
                    : null,
                keyEnumIdToken?.Value<string>());
            if (error is not null)
            {
                throw new JsonSerializationException($"Dictionary member {error}");
            }
        }

        protected override void OnPopulated(JObject obj, Member instance)
        {
            instance.RecordDeclaredWireFields(obj.Properties());
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
                case MemberKind.FunctionRef: return typeof(FunctionRefMember);
                case MemberKind.NSDelegate: return typeof(DelegateMember);
                case MemberKind.NSAction: return typeof(ActionMember);
                case MemberKind.Variant: return typeof(VariantMember);
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
