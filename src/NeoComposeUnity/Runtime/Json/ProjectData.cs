// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    public static class NeoProjectExportContract
    {
        /// <summary>
        /// 26 admits the revision-13 <c>indexOf</c> function kind and the
        /// predicate-bearing <c>count</c> payload inside compiled bodies. An
        /// older SDK cannot deserialize or correctly execute those shapes.
        ///
        /// 25 admits the P71 <c>listRepeat</c> function kind inside compiled
        /// bodies, for the same reason 23 admitted <c>mathOp</c>: an older
        /// SDK has no converter arm for it and would fail such a body with a
        /// raw serialization error mid-load rather than the standard clean
        /// refusal.
        ///
        /// 24 admits the revision-12 <c>conditional</c> and
        /// <c>delegateClosure</c> pointers and the generic-Equals
        /// <c>missingMemberFallback</c>. An older SDK cannot correctly
        /// interpret those shapes, so it must reject the export at the exact
        /// version gate.
        ///
        /// 23 admits the P69 <c>mathOp</c> function kind inside compiled
        /// bodies. An older SDK has no converter arm for it, so
        /// <c>DiscriminatedConverter</c> would fail such a body with a raw
        /// serialization error mid-load; the exact-match gate turns that into
        /// the standard clean refusal.
        ///
        /// 22 adds lookup-variant folder bindings and row-bound variant
        /// references (P68 §7). An older SDK would invoke a lookup variant
        /// without its row and construct the wrong object, so it must reject
        /// the export.
        ///
        /// 21 adds the `variants` collection (P67 §9) and the generated
        /// `Variants` static trees that resolve against it. An older SDK has no
        /// collection to resolve a variant id through, so every
        /// <c>Variants.X</c> lookup and every <c>Variant</c>-member read would
        /// fail to find its record and hand back the bare class — an object in
        /// the wrong configuration rather than an error. It must reject the
        /// export.
        /// </summary>
        public const int CurrentSchemaVersion = 26;
    }

    public class ProjectExportMetadataSemver
    {
        public string label = null!;
        public int major;
        public int minor;
        public int patch;
    }

    public class ProjectExportMetadata
    {
        public int schemaVersion;
        public string projectId = null!;
        public string versionId = null!;
        public ProjectExportMetadataSemver? semver;
    }

    /// <summary>
    /// One declared, independently versioned project-record relation. The
    /// runtime resolves source inheritance and target class expansion from
    /// this declared graph plus <see cref="NeoSchemaClass.extendsClassId"/>;
    /// exports never duplicate inherited effective edges.
    /// </summary>
    public sealed class InternalRecordRelation
    {
        public string id = null!;
        public string projectId = null!;
        public string relationKind = null!;
        public string sourceRecordKind = null!;
        public string sourceRecordId = null!;
        public string targetRecordKind = null!;
        public string targetRecordId = null!;
        public string? orderKey;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
    }

    /// <summary>
    /// P43 §6.1 — one named argument of a <c>: base(...)</c> clause. The
    /// authored expression is compiled over the declaring constructor's
    /// parameter scope; the compiled getter sits at the same index in
    /// <see cref="ConstructorRecord.compiledBaseArguments"/>.
    /// </summary>
    public sealed class ConstructorBaseArgument
    {
        /// <summary>Base constructor parameter name this argument binds.</summary>
        public string name = null!;

        /// <summary>Authored expression source.</summary>
        public string code = null!;
    }

    /// <summary>
    /// P49 §1.5 — one entry of a base clause's initializer block
    /// (<c>: Foo { Bar = bar }</c>). Modelled apart from
    /// <see cref="ConstructorBaseArgument"/> even though the wire shape is the
    /// same: <see cref="name"/> is a merged schema key of the base, so the
    /// entry settles a base member directly instead of feeding a base
    /// constructor parameter. The authored expression is compiled over the
    /// declaring constructor's parameter scope; the compiled getter sits at the
    /// same index in
    /// <see cref="ConstructorRecord.compiledBaseInitializerFields"/>.
    /// </summary>
    public sealed class ConstructorBaseInitializerField
    {
        /// <summary>Base member schema key this entry settles.</summary>
        public string name = null!;

        /// <summary>Authored expression source.</summary>
        public string code = null!;
    }

    /// <summary>
    /// P67 §2.1/§9 — one named configuration of a class.
    ///
    /// <para>A variant is a record, not a value: it is a child of its class the
    /// way a constructor is, and it POINTS AT the value graph carrying its
    /// configuration (<see cref="valueId"/>) rather than living inside one.
    /// That graph is an ordinary materialized row — a `NeoVariant&lt;TObject&gt;`
    /// class value whose members are Initialize, Apply, Overrides, and
    /// ChildOverrides — so it reads through the normal node machinery.</para>
    ///
    /// <para><see cref="classId"/> is the authority on the variant's target
    /// class. The root row's generic bindings corroborate it when present, but
    /// a graph pushed from the CLI carries none, so nothing may depend on
    /// them.</para>
    /// </summary>
    public sealed class VariantRecord
    {
        public string id = null!;
        public string projectId = null!;

        /// <summary>Owning class — the `TObject` this variant configures.</summary>
        public string classId = null!;

        /// <summary>
        /// Symbol name, unique per (classId, folder), case-insensitive. `Base`
        /// is reserved for the no-variant selection and never appears here.
        /// </summary>
        public string name = null!;

        /// <summary>
        /// Resolved folder PATH (`"Trees/Oak"`), or null for the class root.
        ///
        /// <para>Deliberately the path and not a folder record id: the export
        /// performs the join so a data-driven consumer can resolve a variant by
        /// (folder, name) without loading a second collection that exists only
        /// to hold this string. P68 also ships the folder collection separately
        /// so bound folders retain their collection identity at runtime.</para>
        /// </summary>
        public string? folder;

        /// <summary>
        /// Root value row of this variant's `NeoVariant&lt;TObject&gt;` graph.
        /// </summary>
        public string valueId = null!;

        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
    }

    /// <summary>
    /// P68 §2/§7 — a variant folder retained in the runtime export so lookup
    /// variants can validate and resolve the collection row they receive.
    /// </summary>
    public sealed class VariantFolderRecord
    {
        public string id = null!;
        public string classId = null!;
        public string path = null!;
        public VariantFolderBinding? binding;
    }

    public sealed class VariantFolderBinding
    {
        public string collectionMemberId = null!;
        public string collectionValueId = null!;
    }

    /// <summary>
    /// P43 §6.2 — a class's declared constructor. Parameters are typed
    /// declarations, not members: not stored, not in the schema, and not part
    /// of the merged schema the constructed value graph is validated against.
    ///
    /// <para><see cref="code"/> and <see cref="action"/> are both required. A
    /// constructor is never abstract, so there is no absent case; an empty
    /// body is <c>""</c>. There is no <c>bodyMode</c> and no
    /// <c>returnTypeInfo</c> — a constructor's product is its owning class by
    /// definition, and its compiled body is void (<c>typeInfo</c> is a
    /// required Null).</para>
    /// </summary>
    public sealed class ConstructorRecord
    {
        public string id = null!;
        public string projectId = null!;

        /// <summary>Owning class.</summary>
        public string classId = null!;

        /// <summary>
        /// Ordered parameter declarations, reusing the shape
        /// <c>Function</c>/<c>NSFunction</c> members already use. The compiled
        /// <see cref="action"/>'s parameters are
        /// <c>__this__, __root__, __arg_0__ … __arg_N__</c>, position-aligned
        /// with this array.
        /// </summary>
        public FunctionArgumentTypeInfo[] argumentTypes = null!;

        /// <summary>
        /// Authored NeoScript body. Absent means the declaration carries no
        /// <c>init</c> block at all (P49 §1.2 — a required constructor without
        /// one); <c>""</c> means a block was declared and left empty. The two
        /// are interchangeable to the SDK, which executes the compiled
        /// <see cref="action"/> and never the source.
        /// </summary>
        public string? code;

        /// <summary>Server-compiled executable IR. Never accepted from a client write.</summary>
        public FunctionWithReturnType action = null!;

        /// <summary>
        /// The <c>: base(...)</c> clause, by name. Absent means no explicit
        /// base call — the base's parameterless constructor runs if it declares
        /// one, and it is an error if the base declares constructors but no
        /// parameterless one.
        /// </summary>
        public ConstructorBaseArgument[]? baseArguments;

        /// <summary>
        /// Server-compiled base-argument getters, position-aligned with
        /// <see cref="baseArguments"/> and evaluated in this constructor's
        /// parameter scope (<c>__this__</c> is null — the instance is not
        /// usable until the base has run).
        /// </summary>
        public FunctionWithReturnType[]? compiledBaseArguments;

        /// <summary>
        /// P49 §1.5 — the base clause's initializer block, keyed by base member
        /// schema key. Absent means the base clause carries no block. It runs
        /// after the base chain and before this constructor's body, so both the
        /// body and the call-site block can still refine what it settled
        /// (§2.5).
        /// </summary>
        public ConstructorBaseInitializerField[]? baseInitializerFields;

        /// <summary>
        /// Server-compiled base-initializer getters, position-aligned with
        /// <see cref="baseInitializerFields"/> and evaluated in this
        /// constructor's parameter scope. Unlike
        /// <see cref="compiledBaseArguments"/> these run against a constructed
        /// <c>__this__</c>: the base has already run by the time the block
        /// applies.
        /// </summary>
        public FunctionWithReturnType[]? compiledBaseInitializerFields;

        /// <summary>
        /// Authoring documentation. Stripped from the export; modelled so a
        /// hydrated record round-trips.
        /// </summary>
        public string? docsText;

        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
    }

    public static class InternalRecordRelationKinds
    {
        public const string WorldGridTileImport = "world.grid.tile-import";
        public const string WorldGridObjectImport = "world.grid.object-import";
        public const string WorldGridTileLayer = "world.grid.tile-layer";
        public const string WorldGridObjectLayer = "world.grid.object-layer";
        public const string WorldTileCompatibleLayer = "world.tile.compatible-layer";
        public const string WorldTileDefaultLayer = "world.tile.default-layer";
        public const string WorldObjectCompatibleLayer = "world.object.compatible-layer";
        public const string WorldObjectDefaultLayer = "world.object.default-layer";
        public const string WorldTileLayerLinkTarget = "world.tile-layer-link.target";
        public const string WorldObjectLayerLinkTarget = "world.object-layer-link.target";
        public const string WorldSmartTileNeighborTile = "world.smart-tile-neighbor.tile";
    }

    /// <summary>
    /// Top-level deserialization target — pass to
    /// <c>JsonConvert.DeserializeObject&lt;ProjectExport&gt;(json)</c>.
    /// Mirrors the TS-side <c>IProjectUnityExport</c> wrapper:
    /// the project record nested under <see cref="project"/>, plus its
    /// keyed-by-id payloads as <see cref="Dictionary{TKey, TValue}"/>.
    ///
    /// JSON shape:
    ///
    /// <code>
    /// {
    ///   "project":    { ... },
    ///   "members": { "&lt;id&gt;": { ... } },
    ///   "enums":      { "&lt;id&gt;": { ... } },
    ///   "classes":      { "&lt;id&gt;": { ... } },
    ///   "interfaces": { "&lt;id&gt;": { ... } },
    ///   "values":     { "&lt;id&gt;": { ... } }
    /// }
    /// </code>
    ///
    /// Tile grid content is NOT a separate payload: painted tiles and placed
    /// objects live in <see cref="values"/> as ordinary member values
    /// (TilePlacement class values joined to their layer link's unordered
    /// "Tiles" list via <see cref="MemberValue.containerId"/>).
    /// </summary>
    [JsonConverter(typeof(ProjectDataConverter))]
    public class ProjectData
    {
        public ProjectExportMetadata? metadata;
        public Project project = null!;
        public Dictionary<string, Member> members = null!;
        public Dictionary<string, MemberValue> values = null!;

        /// <summary>
        /// Storage partitions (specs/list-member-and-tilegrid-scaling.md
        /// §6): every non-main partition of the export, keyed by partition
        /// key (<c>mapKey</c>, e.g. <c>world:&lt;gridClassId&gt;</c>) with the
        /// partition's value rows keyed by value id. Kept as raw
        /// <see cref="JToken"/>s so parsing project.json does NOT materialize
        /// partition rows — a partition's rows are deserialized into typed
        /// <see cref="MemberValue"/>s only when
        /// <c>NeoClient.LoadValuePartition</c> loads it. Null means the export
        /// has no non-main partitions.
        /// </summary>
        public Dictionary<string, JToken>? valuePartitions;
        public Dictionary<string, NeoSchemaClass> classes = null!;

        /// <summary>
        /// P43 §6.2 — declared constructors keyed by constructor record id.
        /// Empty for every project that declares none, which is why the field
        /// defaults to an empty map rather than being required: an export
        /// written before P43 simply omits it and loads unchanged.
        /// </summary>
        public Dictionary<string, ConstructorRecord> constructors = new();

        /// <summary>
        /// P67 §9 — variant records keyed by variant record id. Empty for every
        /// project that declares none, so the field defaults to an empty map
        /// rather than being required; a hand-built <see cref="ProjectData"/>
        /// may also leave it null, which means the same thing.
        /// </summary>
        public Dictionary<string, VariantRecord> variants = new();

        /// <summary>P68 §7 — lookup bindings keyed by folder record id.</summary>
        public Dictionary<string, VariantFolderRecord> variantFolders = new();

        /// <summary>
        /// Declared relation rows keyed by stable relation id. Required by
        /// export schema 9; absent on the explicitly supported schema-8
        /// compatibility boundary.
        /// </summary>
        public Dictionary<string, InternalRecordRelation>? internalRecordRelations;
        public Dictionary<string, Interface> interfaces = new();
        public Dictionary<string, Enum> enums = null!;
        public Dictionary<string, ProjectFile> files = new();
        public Dictionary<string, UnityTexture2DImportSettingsTemplate> textureTemplates = new();
        public Dictionary<string, UnityAudioClipImportSettingsTemplate> audioClipTemplates = new();
        public Dictionary<string, Dialogue> dialogues = new();
        public Dictionary<string, DialogueGroup> dialogueGroups = new();
        public Dictionary<string, PriorityGroup> priorityGroups = new();
        public ProjectLocalizationExport? localization;

        /// <summary>
        /// Legacy-export detector ONLY. Exports at schema version 3+ never
        /// carry a <c>tileGridContents</c> payload (tile data lives in
        /// <see cref="values"/>); this field exists so a parsed legacy export
        /// can be rejected loudly at load time instead of silently dropping
        /// its derived region payloads. Never read for content.
        /// </summary>
        public JObject? tileGridContents;
    }

    /// <summary>
    /// Strict schema-8 project export reader. Newtonsoft normally ignores
    /// unknown properties, which would make removed class/member fields look
    /// successfully loaded while silently dropping their references. This
    /// converter rejects only the retired schema vocabulary and otherwise
    /// preserves Newtonsoft's forward-compatible unknown-field behavior.
    /// </summary>
    public sealed class ProjectDataConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(ProjectData);

        public override bool CanWrite => false;

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var obj = JObject.Load(reader);
            Schema8LegacyFieldGuard.ValidateProjectData(obj);

            var projectData = new ProjectData();
            using (var subReader = obj.CreateReader())
            {
                serializer.Populate(subReader, projectData);
            }
            return projectData;
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotImplementedException(
                "ProjectDataConverter is read-only; default serialization handles writes.");
        }
    }

    /// <summary>
    /// Targeted hard-cutover guard shared by project-export and polymorphic
    /// DTO readers. Deliberately does not enable Json.NET's global
    /// MissingMemberHandling.Error: unrelated future fields remain readable.
    /// </summary>
    internal static class Schema8LegacyFieldGuard
    {
        private static readonly IReadOnlyDictionary<string, string> RemovedReferenceFields =
            new Dictionary<string, string>
            {
                ["analyzerDialogueContextTypeId"] = "analyzerDialogueContextClassId",
                ["analyzerRootTypeId"] = "analyzerRootClassId",
                ["attribute"] = "member",
                ["attributeDefault"] = "memberDefault",
                ["attributeId"] = "memberId",
                ["attributeIds"] = "memberIds",
                ["attributeKey"] = "memberKey",
                ["attributeKind"] = "memberKind",
                ["attributeType"] = "memberKind",
                ["attributeValue"] = "memberValue",
                ["attributes"] = "members",
                ["attributesById"] = "membersById",
                ["classTypeInfo"] = "schemaClassInfo",
                ["collectionAttributeId"] = "collectionMemberId",
                ["customTypeArguments"] = "classArguments",
                ["customTypeId"] = "classId",
                ["customTypeIds"] = "classIds",
                ["customTypeInfo"] = "schemaClassInfo",
                ["customTypesById"] = "classesById",
                ["customField"] = "classField",
                ["declaringAttributeId"] = "declaringMemberId",
                ["declaringTypeId"] = "declaringClassId",
                ["entryAttributeId"] = "entryMemberId",
                ["extendsAttributeId"] = "extendsMemberId",
                ["extendsTypeId"] = "extendsClassId",
                ["hiddenInAttributeSelector"] = "hiddenInMemberSelector",
                ["InheritsFromType"] = "InheritsFromClass",
                ["listAttributeId"] = "listMemberId",
                ["memberAttributeId"] = "memberId",
                ["NotInheritsFromType"] = "NotInheritsFromClass",
                ["ownerAttributeId"] = "ownerMemberId",
                ["ownerTypeId"] = "ownerClassId",
                ["parentAttributeId"] = "parentMemberId",
                ["parentTypeId"] = "parentClassId",
                ["priorityTypeId"] = "priorityOptionId",
                ["receiverTypeId"] = "receiverClassId",
                ["rootAssetsAttributeId"] = "rootAssetsMemberId",
                ["rootAttributeId"] = "rootMemberId",
                ["rootSaveFileAttributeId"] = "rootSaveFileMemberId",
                ["rootSessionAttributeId"] = "rootSessionMemberId",
                ["rootTypeId"] = "rootClassId",
                ["rootValueAttributeId"] = "rootValueMemberId",
                ["schemaClassArguments"] = "classArguments",
                ["schemaClassId"] = "classId",
                ["schemaClassIds"] = "classIds",
                ["schemaClasses"] = "classes",
                ["sourceOwnerAttributeId"] = "sourceOwnerMemberId",
                ["sourceOwnerTypeId"] = "sourceOwnerClassId",
                ["staticOwnerAttributeId"] = "staticOwnerMemberId",
                ["staticOwnerTypeId"] = "staticOwnerClassId",
                ["targetAttributeId"] = "targetMemberId",
                ["targetTypeId"] = "targetClassId",
                ["thisTypeId"] = "thisClassId",
                ["typesById"] = "classesById",
                ["valueAttributeId"] = "valueMemberId",
                ["valueRootAttributeId"] = "valueRootMemberId",
                ["valueSearchTypeName"] = "valueSearchClassName",
                ["valueTypeId"] = "valueClassId",
            };

        internal static void ValidateProjectData(JObject root)
        {
            Reject(root, "attributes", "members", "Project export");
            Reject(root, "types", "classes", "Project export");
            RejectRemovedReferenceFieldsShallow(root, "Project export");

            foreach (var property in root.Properties())
            {
                switch (property.Name)
                {
                    case "project":
                        RejectRemovedReferenceFieldsRecursive(property.Value);
                        break;
                    case "members":
                        ValidateMembers(property.Value);
                        break;
                    case "values":
                        ValidateMemberValueMap(property.Value, "Project member value");
                        break;
                    case "valuePartitions":
                        ValidateValuePartitions(property.Value);
                        break;
                    case "classes":
                    case "interfaces":
                    case "dialogues":
                    case "dialogueGroups":
                    case "priorityGroups":
                        RejectRemovedReferenceFieldsInMapValues(property.Value);
                        break;
                }
            }
        }

        internal static void ValidateSaveEnvelope(JObject root)
        {
            RejectRemovedReferenceFieldsShallow(root, "Save envelope");

            foreach (var property in root.Properties())
            {
                switch (property.Name)
                {
                    case "values":
                        ValidateMemberValueMap(property.Value, "Save member value");
                        break;
                    case "valuePartitions":
                        ValidateValuePartitions(property.Value);
                        break;
                    case "staticBindings":
                        // Keys are stable user/project member ids, not DTO
                        // property names. Their values are value ids or null.
                        break;
                }
            }
        }

        internal static void RejectRemovedReferenceFieldsShallow(
            JObject obj,
            string context)
        {
            foreach (var replacement in RemovedReferenceFields)
            {
                Reject(obj, replacement.Key, replacement.Value, context);
            }
        }

        internal static void RejectRemovedMemberTypeField(JObject obj)
        {
            Reject(obj, "type", "kind", "Member");
        }

        internal static void RejectRemovedMemberValueTypeId(JObject obj)
        {
            Reject(obj, "typeId", "classId", "Member value");
        }

        internal static void RejectRemovedTypeInfoTypeId(JObject obj)
        {
            Reject(obj, "typeId", "classId", "Type info");
        }

        internal static void Reject(
            JObject obj,
            string removedField,
            string replacementField,
            string context)
        {
            if (obj.Property(removedField) is null) return;
            throw new JsonSerializationException(
                $"{context} uses removed field '{removedField}'; schema 8 requires '{replacementField}'.");
        }

        private static void ValidateMembers(JToken token)
        {
            if (token is not JObject members) return;
            foreach (var memberProperty in members.Properties())
            {
                if (memberProperty.Value is not JObject member) continue;

                RejectRemovedMemberTypeField(member);
                RejectRemovedReferenceFieldsShallow(member, "Member");
                foreach (var property in member.Properties())
                {
                    if (property.Name == "defaultValue")
                    {
                        ValidateMemberValue(property.Value, "Member default value");
                    }
                    else
                    {
                        RejectRemovedReferenceFieldsRecursive(property.Value);
                    }
                }
            }
        }

        private static void ValidateMemberValueMap(JToken token, string context)
        {
            if (token is not JObject values) return;
            foreach (var valueProperty in values.Properties())
            {
                ValidateMemberValue(valueProperty.Value, context);
            }
        }

        private static void ValidateValuePartitions(JToken token)
        {
            if (token is not JObject partitions) return;
            foreach (var partitionProperty in partitions.Properties())
            {
                ValidateMemberValueMap(partitionProperty.Value, "Partition member value");
            }
        }

        private static void ValidateMemberValue(JToken token, string context)
        {
            if (token is not JObject value) return;

            RejectRemovedMemberValueTypeId(value);
            RejectRemovedReferenceFieldsShallow(value, context);

            // The payload can be an authored Dictionary/Class value whose
            // schema keys are arbitrary user data. Do not interpret those keys
            // as DTO property names. Row metadata remains checked above.
            foreach (var property in value.Properties())
            {
                if (property.Name != "value")
                {
                    RejectRemovedReferenceFieldsRecursive(property.Value);
                }
            }
        }

        private static void RejectRemovedReferenceFieldsRecursive(JToken token)
        {
            if (token is JObject obj)
            {
                RejectRemovedReferenceFieldsShallow(obj, "Schema object");
                if (obj.Property("typeId") is not null
                    && obj.Property("type") is not null
                    && obj.Property("required") is not null)
                {
                    RejectRemovedTypeInfoTypeId(obj);
                }

                foreach (var property in obj.Properties())
                {
                    switch (property.Name)
                    {
                        // Dictionary keys are stable ids or authored schema
                        // names, not DTO property names. Validate their values
                        // without interpreting the keys as retired fields.
                        case "schema":
                        case "members":
                        case "typeArguments":
                        case "classArguments":
                        case "extendsGenericBindings":
                        case "genericBindings":
                        case "variables":
                        case "nodes":
                            RejectRemovedReferenceFieldsInMapValues(property.Value);
                            break;
                        default:
                            RejectRemovedReferenceFieldsRecursive(property.Value);
                            break;
                    }
                }
                return;
            }

            if (token is not JArray array) return;
            foreach (var child in array)
            {
                RejectRemovedReferenceFieldsRecursive(child);
            }
        }

        private static void RejectRemovedReferenceFieldsInMapValues(JToken token)
        {
            if (token is not JObject map) return;
            foreach (var property in map.Properties())
            {
                RejectRemovedReferenceFieldsRecursive(property.Value);
            }
        }
    }
}
