// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NUnit.Framework;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Tests
{
    /// <summary>
    /// specs/member-storage.md — per-placement storage in the runtime.
    /// Mirrors the TS resolution vectors in
    /// <c>src/models/members/effective-storage.test.ts</c>: an immutable
    /// Outpost record under the Assets root with a Save-stamped Health field
    /// (the headline case), plus the lifetime/refusal edges.
    /// </summary>
    public class NeoMemberStorageTests
    {
        private const string ProjectId = "project-storage";

        [Test]
        public void Parse_AcceptsWireVocabularyAndRejectsUnknown()
        {
            Assert.AreEqual(NeoMemberStorage.Inherit, NeoMemberStorageResolution.Parse(null));
            Assert.AreEqual(NeoMemberStorage.Inherit, NeoMemberStorageResolution.Parse("inherit"));
            Assert.AreEqual(NeoMemberStorage.Immutable, NeoMemberStorageResolution.Parse("immutable"));
            Assert.AreEqual(NeoMemberStorage.Save, NeoMemberStorageResolution.Parse("save"));
            Assert.AreEqual(NeoMemberStorage.Session, NeoMemberStorageResolution.Parse("session"));
            Assert.Throws<System.InvalidOperationException>(
                () => NeoMemberStorageResolution.Parse("static"));
            Assert.Throws<System.InvalidOperationException>(
                () => NeoMemberStorageResolution.Parse("persistent"));
        }

        [Test]
        public void AuthoredOwnership_SaveStampedLeafUnderAssetsIsSaveOwned()
        {
            var client = LoadStorageClient();

            Assert.IsTrue(client.TryGetValueOwnership("v-health", out NeoValueOwnership healthOwnership));
            Assert.AreEqual(NeoValueOwnership.Save, healthOwnership);

            Assert.IsTrue(client.TryGetValueOwnership("v-label", out NeoValueOwnership labelOwnership));
            Assert.AreEqual(NeoValueOwnership.Asset, labelOwnership);

            // Session-stamped subtree under assets resolves session-owned.
            Assert.IsTrue(client.TryGetValueOwnership("v-mood", out NeoValueOwnership moodOwnership));
            Assert.AreEqual(NeoValueOwnership.Session, moodOwnership);

            // Immutable-stamped leaf under the save root stays asset-owned.
            Assert.IsTrue(client.TryGetValueOwnership("v-build", out NeoValueOwnership buildOwnership));
            Assert.AreEqual(NeoValueOwnership.Asset, buildOwnership);
        }

        [Test]
        public void DeclaredStorage_ResolvesThroughExtendsChain()
        {
            var client = LoadStorageClient();
            Assert.IsTrue(client.TryGetMember("member-health-override", out IntMember? overrideMember));
            Assert.AreEqual(NeoMemberStorage.Save, client.DeclaredStorage(overrideMember!));
        }

        [Test]
        public void ReadOnlyAssetsTree_ExposesWritableStampedChildAndPersistsToSave()
        {
            var client = LoadStorageClient();
            var outpost = client.AssetsRoot.Get<NeoMemberClass>("Outpost");
            Assert.IsFalse(outpost is NeoMemberClassWritable);

            // The Save-stamped child is a writable node under a read-only parent.
            var health = outpost.Get<NeoMemberIntWritable>("Health");
            Assert.AreEqual(NeoValueOwnership.Save, health.ownership);

            health.Set(25);
            Assert.AreEqual(25, (int)health.value!.value!.Value);
            StringAssert.Contains("v-health", client.SerializeSaveData());

            // The unstamped sibling stays read-only.
            var label = outpost.Get<NeoMember>("Label");
            Assert.IsFalse(label is NeoMemberStringWritable);
        }

        [Test]
        public void AsWritableView_AllowsStampedKeyWritesAndRefusesImmutableKeys()
        {
            var client = LoadStorageClient();
            var outpost = client.AssetsRoot.Get<NeoMemberClass>("Outpost");
            var view = NeoGeneratedTypesSupport.AsWritable(outpost);

            NeoGeneratedTypesSupport.SetValue(
                view, "Health", NeoValueWritePayload.FromValue(75d));
            var health = outpost.Get<NeoMemberIntWritable>("Health");
            Assert.AreEqual(75, (int)health.value!.value!.Value);

            var immutableWrite = Assert.Throws<System.InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.SetValue(
                    view, "Label", NeoValueWritePayload.FromValue("hacked")));
            StringAssert.Contains("effective storage is immutable", immutableWrite!.Message);
        }

        [Test]
        public void WritableSaveTree_RefusesImmutableStampedLeaf()
        {
            var client = LoadStorageClient();
            var saveRoot = client.SaveRoot;
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.SetValue(
                    saveRoot, "BuildLabel", NeoValueWritePayload.FromValue("nope")));
            StringAssert.Contains("effective storage is immutable", error!.Message);

            // The immutable-stamped child node under the writable root is the
            // read-only kind.
            var buildLabel = saveRoot.Get<NeoMember>("BuildLabel");
            Assert.IsFalse(buildLabel is NeoMemberStringWritable);
        }

        [Test]
        public void ExportSchemaVersion_NewerThanSupportedThrows()
        {
            var data = BuildStorageProjectData();
            data.metadata = new ProjectExportMetadata
            {
                schemaVersion = 12,
                projectId = ProjectId,
                versionId = "version-1",
            };
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(data));
            StringAssert.Contains("schema version 12", error!.Message);
            StringAssert.Contains("schema version 11", error.Message);
            StringAssert.Contains("Update", error.Message);
        }

        [Test]
        public void ExportSchemaVersion_OlderThanCurrentContractThrows()
        {
            var legacy = BuildStorageProjectData();
            legacy.metadata = new ProjectExportMetadata
            {
                schemaVersion = 5,
                projectId = ProjectId,
                versionId = "version-1",
            };
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(legacy));
            StringAssert.Contains("schema version 5", error!.Message);
            StringAssert.Contains("Re-export", error.Message);
        }

        [Test]
        public void ExportSchemaVersion_EightRequiresReleaseMigrationBoundary()
        {
            var current = BuildStorageProjectData();
            current.metadata = new ProjectExportMetadata
            {
                schemaVersion = 8,
                projectId = ProjectId,
                versionId = "version-1",
            };
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(current));
            StringAssert.Contains("schema version 8", error!.Message);
            StringAssert.Contains("release-data migration boundary", error.Message);
        }

        [Test]
        public void ExportSchemaVersion_ElevenWithRelationsIsAccepted()
        {
            var current = BuildStorageProjectData();
            current.metadata = new ProjectExportMetadata
            {
                schemaVersion = 11,
                projectId = ProjectId,
                versionId = "version-1",
            };
            current.internalRecordRelations =
                new Dictionary<string, InternalRecordRelation>();

            Assert.DoesNotThrow(() => NeoTestSaveStack.ClientFromSchema(current));
        }

        [Test]
        public void ExportSchemaVersion_ElevenMissingRelationsThrowsClearly()
        {
            var invalid = BuildStorageProjectData();
            invalid.metadata = new ProjectExportMetadata
            {
                schemaVersion = 11,
                projectId = ProjectId,
                versionId = "version-1",
            };
            invalid.internalRecordRelations = null;

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(invalid));

            StringAssert.Contains("internalRecordRelations", error!.Message);
            StringAssert.Contains("schema version 11", error.Message);
        }

        [Test]
        public void ExportSchemaVersion_ElevenRejectsUnknownRelationKind()
        {
            var invalid = BuildStorageProjectData();
            invalid.metadata = new ProjectExportMetadata
            {
                schemaVersion = 11,
                projectId = ProjectId,
                versionId = "version-1",
            };
            invalid.internalRecordRelations =
                new Dictionary<string, InternalRecordRelation>
                {
                    ["relation-unknown"] = new InternalRecordRelation
                    {
                        id = "relation-unknown",
                        projectId = ProjectId,
                        relationKind = "world.future-kind",
                        sourceRecordKind = "class",
                        sourceRecordId = "class-root-assets",
                        targetRecordKind = "class",
                        targetRecordId = "class-outpost",
                        createdAt = "x",
                        updatedAt = "x",
                    },
                };

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(invalid));

            StringAssert.Contains("unsupported relation kind", error!.Message);
            StringAssert.Contains("Update the NeoCompose SDK", error.Message);
        }

        [Test]
        public void ExportSchemaVersion_PreviousRequiresReExport()
        {
            var previous = BuildStorageProjectData();
            previous.metadata = new ProjectExportMetadata
            {
                schemaVersion = 7,
                projectId = ProjectId,
                versionId = "version-1",
            };
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(previous));
            StringAssert.Contains("schema version 7", error!.Message);
            StringAssert.Contains("schema version 11", error.Message);
            StringAssert.Contains("Re-export", error.Message);
        }

        [Test]
        public void ExportSchemaVersion_MissingMetadataThrows()
        {
            var missing = BuildStorageProjectData();
            missing.metadata = null;

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(
                    missing,
                    assumeCurrentSchema: false));

            StringAssert.Contains("metadata is missing", error!.Message);
            StringAssert.Contains("requires schema version 11", error.Message);
            StringAssert.Contains("Re-export", error.Message);
        }

        [Test]
        public void CurrentExport_MissingClassesCollectionThrowsClearly()
        {
            var invalid = BuildStorageProjectData();
            invalid.classes = null!;

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(invalid));

            StringAssert.Contains("required 'classes' collection", error!.Message);
            StringAssert.Contains("schema-8 Class/Member contract", error.Message);
        }

        [Test]
        public void CurrentExport_MissingMembersCollectionThrowsClearly()
        {
            var invalid = BuildStorageProjectData();
            invalid.members = null!;

            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(invalid));

            StringAssert.Contains("required 'members' collection", error!.Message);
            StringAssert.Contains("schema-8 Class/Member contract", error.Message);
        }

        private static NeoClient LoadStorageClient()
        {
            return NeoTestSaveStack.ClientFromSchema(BuildStorageProjectData());
        }

        /// <summary>
        /// Assets root: { Outpost: Outpost { Health(int, storage=save),
        /// Label(string), Mood(string, storage=session) } } with authored
        /// values. Save root: { BuildLabel(string, storage=immutable) }.
        /// Session root: empty. Plus an unplaced override member whose
        /// extends chain reaches the Save-stamped Health.
        /// </summary>
        private static ProjectData BuildStorageProjectData()
        {
            var healthMember = new IntMember
            {
                id = "member-health",
                projectId = ProjectId,
                name = "Health",
                kind = MemberKind.Int,
                required = true,
                storage = "save",
                createdAt = "x",
                updatedAt = "x",
            };
            var healthOverrideMember = new IntMember
            {
                id = "member-health-override",
                projectId = ProjectId,
                name = "Health",
                kind = MemberKind.Int,
                extendsMemberId = healthMember.id,
                createdAt = "x",
                updatedAt = "x",
            };
            var labelMember = new StringMember
            {
                id = "member-label",
                projectId = ProjectId,
                name = "Label",
                kind = MemberKind.String,
                localizable = false,
                createdAt = "x",
                updatedAt = "x",
            };
            var moodMember = new StringMember
            {
                id = "member-mood",
                projectId = ProjectId,
                name = "Mood",
                kind = MemberKind.String,
                localizable = false,
                storage = "session",
                createdAt = "x",
                updatedAt = "x",
            };
            var buildLabelMember = new StringMember
            {
                id = "member-build-label",
                projectId = ProjectId,
                name = "BuildLabel",
                kind = MemberKind.String,
                localizable = false,
                storage = "immutable",
                createdAt = "x",
                updatedAt = "x",
            };
            var outpostMember = new ClassMember
            {
                id = "member-outpost",
                projectId = ProjectId,
                name = "Outpost",
                kind = MemberKind.Class,
                classId = "class-outpost",
                createdAt = "x",
                updatedAt = "x",
            };
            var rootAssets = RootMember("member-root-assets", "Assets", "class-root-assets", "v-root-assets", "immutable");
            var rootSave = RootMember("member-root-save", "Save", "class-root-save", "v-root-save", "save");
            var rootSession = RootMember("member-root-session", "Session", "class-root-session", "v-root-session", "session");

            return new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "Storage",
                    rootAssetsMemberId = rootAssets.id,
                    rootSaveFileMemberId = rootSave.id,
                    rootSessionMemberId = rootSession.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    [healthMember.id] = healthMember,
                    [healthOverrideMember.id] = healthOverrideMember,
                    [labelMember.id] = labelMember,
                    [moodMember.id] = moodMember,
                    [buildLabelMember.id] = buildLabelMember,
                    [outpostMember.id] = outpostMember,
                    [rootAssets.id] = rootAssets,
                    [rootSave.id] = rootSave,
                    [rootSession.id] = rootSession,
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["class-outpost"] = ClassOf("class-outpost", "Outpost", new Dictionary<string, string>
                    {
                        ["Health"] = healthMember.id,
                        ["Label"] = labelMember.id,
                        ["Mood"] = moodMember.id,
                    }),
                    ["class-root-assets"] = ClassOf("class-root-assets", "AssetsRoot", new Dictionary<string, string>
                    {
                        ["Outpost"] = outpostMember.id,
                    }),
                    ["class-root-save"] = ClassOf("class-root-save", "SaveRoot", new Dictionary<string, string>
                    {
                        ["BuildLabel"] = buildLabelMember.id,
                    }),
                    ["class-root-session"] = ClassOf("class-root-session", "SessionRoot", new Dictionary<string, string>()),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["v-root-assets"] = RecordValue("v-root-assets", "class-root-assets", new Dictionary<string, string>
                    {
                        ["Outpost"] = "v-outpost",
                    }),
                    ["v-outpost"] = RecordValue("v-outpost", "class-outpost", new Dictionary<string, string>
                    {
                        ["Health"] = "v-health",
                        ["Label"] = "v-label",
                        ["Mood"] = "v-mood",
                    }),
                    ["v-health"] = new NumberMemberValue
                    {
                        id = "v-health",
                        createdAt = "x",
                        updatedAt = "x",
                        value = 100,
                    },
                    ["v-label"] = new StringMemberValue
                    {
                        id = "v-label",
                        createdAt = "x",
                        updatedAt = "x",
                        value = "Alpha",
                    },
                    ["v-mood"] = new StringMemberValue
                    {
                        id = "v-mood",
                        createdAt = "x",
                        updatedAt = "x",
                        value = "calm",
                    },
                    ["v-root-save"] = RecordValue("v-root-save", "class-root-save", new Dictionary<string, string>
                    {
                        ["BuildLabel"] = "v-build",
                    }),
                    ["v-build"] = new StringMemberValue
                    {
                        id = "v-build",
                        createdAt = "x",
                        updatedAt = "x",
                        value = "1.0.0",
                    },
                    ["v-root-session"] = RecordValue("v-root-session", "class-root-session", new Dictionary<string, string>()),
                },
            };
        }

        private static ClassMember RootMember(
            string id,
            string name,
            string classId,
            string valueId,
            string storage)
        {
            return new ClassMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Class,
                classId = classId,
                valueId = valueId,
                storage = storage,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static NeoSchemaClass ClassOf(string id, string name, Dictionary<string, string> schema)
        {
            return new NeoSchemaClass
            {
                id = id,
                projectId = ProjectId,
                name = name,
                schema = schema,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static ObjectMemberValue RecordValue(
            string id,
            string classId,
            Dictionary<string, string> value)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                createdAt = "x",
                updatedAt = "x",
                value = value,
            };
        }
    }
}
