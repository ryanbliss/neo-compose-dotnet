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
        /// P65 §3.3 — 18 is the first export whose parameters may carry a
        /// constant <c>defaultValue</c>. The SDK accepts exactly this version,
        /// so an older export is regenerated rather than partially understood:
        /// a pre-P65 SDK would accept calls the project no longer requires
        /// arguments for, then throw arity errors at evaluation.
        /// </summary>
        public const int CurrentSchemaVersion = 18;
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
