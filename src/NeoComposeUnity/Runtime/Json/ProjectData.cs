// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
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
                ["collectionAttributeId"] = "collectionMemberId",
                ["customTypeArguments"] = "classArguments",
                ["customTypeId"] = "classId",
                ["customTypeIds"] = "classIds",
                ["customTypeInfo"] = "classTypeInfo",
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
