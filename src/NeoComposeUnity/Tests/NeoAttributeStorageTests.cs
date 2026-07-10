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
    /// specs/attribute-storage.md — per-placement storage in the runtime.
    /// Mirrors the TS resolution vectors in
    /// <c>src/models/attributes/effective-storage.test.ts</c>: a static
    /// Outpost record under the Assets root with a Save-stamped Health field
    /// (the headline case), plus the lifetime/refusal edges.
    /// </summary>
    public class NeoAttributeStorageTests
    {
        private const string ProjectId = "project-storage";

        [Test]
        public void Parse_AcceptsWireVocabularyAndRejectsUnknown()
        {
            Assert.AreEqual(NeoAttributeStorage.Inherit, NeoAttributeStorageResolution.Parse(null));
            Assert.AreEqual(NeoAttributeStorage.Inherit, NeoAttributeStorageResolution.Parse("inherit"));
            Assert.AreEqual(NeoAttributeStorage.Static, NeoAttributeStorageResolution.Parse("static"));
            Assert.AreEqual(NeoAttributeStorage.Save, NeoAttributeStorageResolution.Parse("save"));
            Assert.AreEqual(NeoAttributeStorage.Session, NeoAttributeStorageResolution.Parse("session"));
            Assert.Throws<System.InvalidOperationException>(
                () => NeoAttributeStorageResolution.Parse("persistent"));
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

            // Static-stamped leaf under the save root stays asset-owned.
            Assert.IsTrue(client.TryGetValueOwnership("v-build", out NeoValueOwnership buildOwnership));
            Assert.AreEqual(NeoValueOwnership.Asset, buildOwnership);
        }

        [Test]
        public void DeclaredStorage_ResolvesThroughExtendsChain()
        {
            var client = LoadStorageClient();
            Assert.IsTrue(client.TryGetAttribute("attr-health-override", out IntAttribute? overrideAttribute));
            Assert.AreEqual(NeoAttributeStorage.Save, client.DeclaredStorage(overrideAttribute!));
        }

        [Test]
        public void ReadOnlyAssetsTree_ExposesWritableStampedChildAndPersistsToSave()
        {
            var client = LoadStorageClient();
            var outpost = client.AssetsRoot.Get<NeoAttributeCustom>("Outpost");
            Assert.IsFalse(outpost is NeoAttributeCustomWritable);

            // The Save-stamped child is a writable node under a read-only parent.
            var health = outpost.Get<NeoAttributeIntWritable>("Health");
            Assert.AreEqual(NeoValueOwnership.Save, health.ownership);

            health.Set(25);
            Assert.AreEqual(25, (int)health.value!.value!.Value);
            StringAssert.Contains("v-health", client.SerializeSaveData());

            // The unstamped sibling stays read-only.
            var label = outpost.Get<NeoAttribute>("Label");
            Assert.IsFalse(label is NeoAttributeStringWritable);
        }

        [Test]
        public void AsWritableView_AllowsStampedKeyWritesAndRefusesStaticKeys()
        {
            var client = LoadStorageClient();
            var outpost = client.AssetsRoot.Get<NeoAttributeCustom>("Outpost");
            var view = NeoGeneratedTypesSupport.AsWritable(outpost);

            NeoGeneratedTypesSupport.SetValue(
                view, "Health", NeoValueWritePayload.FromValue(75d));
            var health = outpost.Get<NeoAttributeIntWritable>("Health");
            Assert.AreEqual(75, (int)health.value!.value!.Value);

            var staticWrite = Assert.Throws<System.InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.SetValue(
                    view, "Label", NeoValueWritePayload.FromValue("hacked")));
            StringAssert.Contains("effective storage is static", staticWrite!.Message);
        }

        [Test]
        public void WritableSaveTree_RefusesStaticStampedLeaf()
        {
            var client = LoadStorageClient();
            var saveRoot = client.SaveRoot;
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.SetValue(
                    saveRoot, "BuildLabel", NeoValueWritePayload.FromValue("nope")));
            StringAssert.Contains("effective storage is static", error!.Message);

            // The static-stamped child node under the writable root is the
            // read-only kind.
            var buildLabel = saveRoot.Get<NeoAttribute>("BuildLabel");
            Assert.IsFalse(buildLabel is NeoAttributeStringWritable);
        }

        [Test]
        public void ExportSchemaVersion_NewerThanSupportedThrows()
        {
            var data = BuildStorageProjectData();
            data.metadata = new ProjectExportMetadata
            {
                schemaVersion = 5,
                projectId = ProjectId,
                versionId = "version-1",
            };
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(data));
            StringAssert.Contains("schema version 5", error!.Message);
            StringAssert.Contains("newer", error.Message);
        }

        [Test]
        public void ExportSchemaVersion_OlderThanInterfacesThrows()
        {
            var legacy = BuildStorageProjectData();
            legacy.metadata = new ProjectExportMetadata
            {
                schemaVersion = 3,
                projectId = ProjectId,
                versionId = "version-1",
            };
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                NeoTestSaveStack.ClientFromSchema(legacy));
            StringAssert.Contains("schema version 3", error!.Message);
            StringAssert.Contains("Re-export", error.Message);
        }

        [Test]
        public void ExportSchemaVersion_CurrentIsAccepted()
        {
            var current = BuildStorageProjectData();
            current.metadata = new ProjectExportMetadata
            {
                schemaVersion = 4,
                projectId = ProjectId,
                versionId = "version-1",
            };
            Assert.DoesNotThrow(() => NeoTestSaveStack.ClientFromSchema(current));
        }

        private static NeoClient LoadStorageClient()
        {
            return NeoTestSaveStack.ClientFromSchema(BuildStorageProjectData());
        }

        /// <summary>
        /// Assets root: { Outpost: Outpost { Health(int, storage=save),
        /// Label(string), Mood(string, storage=session) } } with authored
        /// values. Save root: { BuildLabel(string, storage=static) }.
        /// Session root: empty. Plus an unplaced override attribute whose
        /// extends chain reaches the Save-stamped Health.
        /// </summary>
        private static ProjectData BuildStorageProjectData()
        {
            var healthAttribute = new IntAttribute
            {
                id = "attr-health",
                projectId = ProjectId,
                name = "Health",
                type = AttributeType.Int,
                required = true,
                storage = "save",
                createdAt = "x",
                updatedAt = "x",
            };
            var healthOverrideAttribute = new IntAttribute
            {
                id = "attr-health-override",
                projectId = ProjectId,
                name = "Health",
                type = AttributeType.Int,
                extendsAttributeId = healthAttribute.id,
                createdAt = "x",
                updatedAt = "x",
            };
            var labelAttribute = new StringAttribute
            {
                id = "attr-label",
                projectId = ProjectId,
                name = "Label",
                type = AttributeType.String,
                localizable = false,
                createdAt = "x",
                updatedAt = "x",
            };
            var moodAttribute = new StringAttribute
            {
                id = "attr-mood",
                projectId = ProjectId,
                name = "Mood",
                type = AttributeType.String,
                localizable = false,
                storage = "session",
                createdAt = "x",
                updatedAt = "x",
            };
            var buildLabelAttribute = new StringAttribute
            {
                id = "attr-build-label",
                projectId = ProjectId,
                name = "BuildLabel",
                type = AttributeType.String,
                localizable = false,
                storage = "static",
                createdAt = "x",
                updatedAt = "x",
            };
            var outpostAttribute = new CustomAttribute
            {
                id = "attr-outpost",
                projectId = ProjectId,
                name = "Outpost",
                type = AttributeType.Custom,
                customTypeId = "type-outpost",
                createdAt = "x",
                updatedAt = "x",
            };
            var rootAssets = RootAttribute("attr-root-assets", "Assets", "type-root-assets", "v-root-assets", "static");
            var rootSave = RootAttribute("attr-root-save", "Save", "type-root-save", "v-root-save", "save");
            var rootSession = RootAttribute("attr-root-session", "Session", "type-root-session", "v-root-session", "session");

            return new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "Storage",
                    rootAssetsAttributeId = rootAssets.id,
                    rootSaveFileAttributeId = rootSave.id,
                    rootSessionAttributeId = rootSession.id,
                    createdAt = "x",
                    updatedAt = "x",
                },
                attributes = new Dictionary<string, NeoCompose.Runtime.Json.Attribute>
                {
                    [healthAttribute.id] = healthAttribute,
                    [healthOverrideAttribute.id] = healthOverrideAttribute,
                    [labelAttribute.id] = labelAttribute,
                    [moodAttribute.id] = moodAttribute,
                    [buildLabelAttribute.id] = buildLabelAttribute,
                    [outpostAttribute.id] = outpostAttribute,
                    [rootAssets.id] = rootAssets,
                    [rootSave.id] = rootSave,
                    [rootSession.id] = rootSession,
                },
                types = new Dictionary<string, CustomType>
                {
                    ["type-outpost"] = TypeOf("type-outpost", "Outpost", new Dictionary<string, string>
                    {
                        ["Health"] = healthAttribute.id,
                        ["Label"] = labelAttribute.id,
                        ["Mood"] = moodAttribute.id,
                    }),
                    ["type-root-assets"] = TypeOf("type-root-assets", "AssetsRoot", new Dictionary<string, string>
                    {
                        ["Outpost"] = outpostAttribute.id,
                    }),
                    ["type-root-save"] = TypeOf("type-root-save", "SaveRoot", new Dictionary<string, string>
                    {
                        ["BuildLabel"] = buildLabelAttribute.id,
                    }),
                    ["type-root-session"] = TypeOf("type-root-session", "SessionRoot", new Dictionary<string, string>()),
                },
                values = new Dictionary<string, AttributeValue>
                {
                    ["v-root-assets"] = RecordValue("v-root-assets", "type-root-assets", new Dictionary<string, string>
                    {
                        ["Outpost"] = "v-outpost",
                    }),
                    ["v-outpost"] = RecordValue("v-outpost", "type-outpost", new Dictionary<string, string>
                    {
                        ["Health"] = "v-health",
                        ["Label"] = "v-label",
                        ["Mood"] = "v-mood",
                    }),
                    ["v-health"] = new NumberAttributeValue
                    {
                        id = "v-health",
                        createdAt = "x",
                        updatedAt = "x",
                        value = 100,
                    },
                    ["v-label"] = new StringAttributeValue
                    {
                        id = "v-label",
                        createdAt = "x",
                        updatedAt = "x",
                        value = "Alpha",
                    },
                    ["v-mood"] = new StringAttributeValue
                    {
                        id = "v-mood",
                        createdAt = "x",
                        updatedAt = "x",
                        value = "calm",
                    },
                    ["v-root-save"] = RecordValue("v-root-save", "type-root-save", new Dictionary<string, string>
                    {
                        ["BuildLabel"] = "v-build",
                    }),
                    ["v-build"] = new StringAttributeValue
                    {
                        id = "v-build",
                        createdAt = "x",
                        updatedAt = "x",
                        value = "1.0.0",
                    },
                    ["v-root-session"] = RecordValue("v-root-session", "type-root-session", new Dictionary<string, string>()),
                },
            };
        }

        private static CustomAttribute RootAttribute(
            string id,
            string name,
            string typeId,
            string valueId,
            string storage)
        {
            return new CustomAttribute
            {
                id = id,
                projectId = ProjectId,
                name = name,
                type = AttributeType.Custom,
                customTypeId = typeId,
                valueId = valueId,
                storage = storage,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static CustomType TypeOf(string id, string name, Dictionary<string, string> schema)
        {
            return new CustomType
            {
                id = id,
                projectId = ProjectId,
                name = name,
                schema = schema,
                createdAt = "x",
                updatedAt = "x",
            };
        }

        private static ObjectAttributeValue RecordValue(
            string id,
            string typeId,
            Dictionary<string, string> value)
        {
            return new ObjectAttributeValue
            {
                id = id,
                typeId = typeId,
                createdAt = "x",
                updatedAt = "x",
                value = value,
            };
        }
    }
}
