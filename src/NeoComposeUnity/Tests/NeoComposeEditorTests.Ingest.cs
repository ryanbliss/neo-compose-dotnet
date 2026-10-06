// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Tests
{
    /// <summary>P100 §7: ingesting a <c>neo export</c> sidecar.</summary>
    public partial class NeoComposeEditorTests
    {
        private const string HeroAssetPath = "Assets/Resources/Neo/Files/Sprites/file-1-hero.png";

        [Test]
        public void Ingest_AppliesTheExportConfigAndSchedulesPostSynchronize()
        {
            using var fixture = new IngestFixture("");
            fixture.config.namespaceForGeneratedTypes = "Old.Namespace";
            fixture.sidecar.config.namespaceForGeneratedTypes = "Game.Generated";
            fixture.sidecar.config.singleton = false;
            fixture.sidecar.config.convexUrl = " https://rig.convex.cloud ";
            fixture.sidecar.config.runtimeOAuth = new NeoComposeUnityRuntimeOAuthConfig
            {
                configuredForVersion = true,
                runtimeOAuthClientId = "client-1",
                scopes = new[] { "project:project-1:save:read" },
            };

            var errors = fixture.Ingest();

            CollectionAssert.IsEmpty(errors);
            Assert.AreEqual("Game.Generated", fixture.config.namespaceForGeneratedTypes);
            Assert.IsFalse(fixture.config.singleton);
            Assert.AreEqual("https://rig.convex.cloud", fixture.config.convexUrl);
            Assert.AreEqual("client-1", fixture.config.runtimeOAuthClientId);
            CollectionAssert.AreEqual(new[] { "project:project-1:save:read" }, fixture.config.runtimeOAuthScopes);
            Assert.IsTrue(fixture.assets.savedConfig);
            Assert.IsTrue(fixture.assets.refreshed);
            Assert.AreEqual("Assets/Resources/Neo/project.json", fixture.assets.postSynchronizeProjectJsonPath);
            Assert.AreEqual("export-1", fixture.Marker);
            Assert.IsTrue(NeoComposeExportIngest.IsIngested(fixture.projectRoot, fixture.sidecar));
        }

        [TestCase("other-project", "version-1", "wrote project 'other-project', but this Unity project's Neo Compose config is on project 'project-1'")]
        [TestCase("project-1", "other-version", "wrote version 'other-version', but this Unity project's Neo Compose config is on version 'version-1'. Run `neo branch switch version-1`")]
        public void Ingest_RejectsAnExportOfAnotherProjectOrVersion(string projectId, string versionId, string expected)
        {
            using var fixture = new IngestFixture("");
            fixture.sidecar.projectId = projectId;
            fixture.sidecar.versionId = versionId;

            var error = Assert.Throws<InvalidOperationException>(() => fixture.Ingest());

            StringAssert.Contains(expected, error!.Message);
            Assert.IsFalse(fixture.assets.savedConfig);
            Assert.IsNull(fixture.Marker);
        }

        [Test]
        public void Ingest_WritesTheMarkerLast()
        {
            using var fixture = new IngestFixture(HeroFileJson());
            fixture.AddWorkspaceFile("file-1", "Files/Sprites/hero.png", new byte[] { 1, 2, 3 });
            fixture.assets.throwOnSchedulePostSynchronize = new IOException("Domain reload.");

            Assert.Throws<IOException>(() => fixture.Ingest());

            Assert.IsNull(fixture.Marker, "An interrupted ingest must run again.");
            Assert.IsFalse(NeoComposeExportIngest.IsIngested(fixture.projectRoot, fixture.sidecar));

            fixture.assets.throwOnSchedulePostSynchronize = null;
            CollectionAssert.IsEmpty(fixture.Ingest());
            Assert.AreEqual("export-1", fixture.Marker);
        }

        [Test]
        public void Ingest_RerunningAnExportWritesNoFileAgain()
        {
            using var fixture = new IngestFixture(HeroFileJson());
            fixture.AddWorkspaceFile("file-1", "Files/Sprites/hero.png", new byte[] { 1, 2, 3 });
            CollectionAssert.IsEmpty(fixture.Ingest());
            var entry = fixture.assets.assetDatabase.TryGetEntry("file-1")!;
            var recordHash = entry.FileRecordHash;
            fixture.assets.writtenPaths.Clear();

            CollectionAssert.IsEmpty(fixture.Ingest());

            CollectionAssert.IsEmpty(fixture.assets.writtenPaths);
            Assert.AreEqual(recordHash, fixture.assets.assetDatabase.TryGetEntry("file-1")!.FileRecordHash);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, fixture.assets.binaryFiles[HeroAssetPath]);
            Assert.AreEqual("export-1", fixture.Marker);
        }

        [TestCase("changed", "changed since export, run `neo export` again.")]
        [TestCase("removed", "was removed since export, run `neo export` again.")]
        [TestCase("unlisted", "the export lists no workspace bytes for file 'file-1', run `neo export` again.")]
        public void Ingest_FileNotAsExportedFailsOnlyThatFile(string defect, string expected)
        {
            using var fixture = new IngestFixture(HeroFileJson() + "," + HeroFileJson("file-2", "villain.png"));
            fixture.AddWorkspaceFile("file-1", "Files/Sprites/hero.png", new byte[] { 1, 2, 3 });
            fixture.AddWorkspaceFile("file-2", "Files/Sprites/villain.png", new byte[] { 4, 5, 6 });
            var heroPath = Path.Combine(fixture.workspaceRoot, "Files/Sprites/hero.png");
            switch (defect)
            {
                case "changed":
                    File.WriteAllBytes(heroPath, new byte[] { 9 });
                    break;
                case "removed":
                    File.Delete(heroPath);
                    break;
                default:
                    fixture.sidecar.files.Remove("file-1");
                    break;
            }

            var errors = fixture.Ingest();

            Assert.AreEqual(1, errors.Length);
            StringAssert.StartsWith("hero.png: ", errors[0]);
            StringAssert.EndsWith(expected, errors[0]);
            Assert.IsFalse(fixture.assets.binaryFiles.ContainsKey(HeroAssetPath));
            Assert.IsNull(fixture.assets.assetDatabase.TryGetEntry("file-1"));
            CollectionAssert.AreEqual(
                new byte[] { 4, 5, 6 },
                fixture.assets.binaryFiles["Assets/Resources/Neo/Files/Sprites/file-2-villain.png"]);
            Assert.IsNull(fixture.Marker, "The next `neo export` must rewrite the sidecar so ingest retries the file.");
        }

        [Test]
        public void Ingest_UnderARigOverlayNeverSavesTheCommittedAsset()
        {
            NeoComposeResolvedConfig.ResetForTests();
            EnsureTempRoot();
            NeoComposeConfig? overlay = null;
            try
            {
                var assetPath = $"{TempRoot}/NeoComposeConfig.asset";
                var committed = NeoComposeConfigProvider.LoadOrCreate(assetPath, new[] { TempRoot });
                committed.SelectProject("committed-project", "Committed Project");
                committed.versionId = "committed-version";
                committed.namespaceForGeneratedTypes = "Committed.Namespace";
                NeoComposeConfigProvider.Save(committed);
                var serializedBefore = File.ReadAllText(assetPath);
                overlay = NeoComposeResolvedConfig.Apply(
                    committed,
                    NeoComposeRigManifestReader.Parse(
                        File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/rig-manifest-seeded.json"),
                        "rig-manifest-seeded.json"));
                using var fixture = new IngestFixture("", config: overlay);
                fixture.assets.saveConfigThroughProvider = true;
                fixture.sidecar.projectId = overlay.projectId;
                fixture.sidecar.versionId = overlay.versionId;
                fixture.sidecar.config.namespaceForGeneratedTypes = "Rig.Namespace";

                CollectionAssert.IsEmpty(fixture.Ingest());
                AssetDatabase.SaveAssets();

                Assert.AreEqual("Rig.Namespace", overlay.namespaceForGeneratedTypes);
                Assert.AreEqual(serializedBefore, File.ReadAllText(assetPath));
                Assert.AreEqual("Committed.Namespace", committed.namespaceForGeneratedTypes);
                Assert.AreEqual("committed-project", committed.projectId);
            }
            finally
            {
                if (overlay != null)
                    UnityEngine.Object.DestroyImmediate(overlay);
                NeoComposeResolvedConfig.ResetForTests();
            }
        }

        [Test]
        public void ReadSidecar_IsNullWithoutAnExportAndRejectsAnotherSchema()
        {
            using var fixture = new IngestFixture("");
            var sidecarPath = Path.Combine(fixture.projectRoot, NeoComposeExportIngest.SidecarPath);

            Assert.IsNull(NeoComposeExportIngest.ReadSidecar(fixture.projectRoot));

            File.WriteAllText(sidecarPath, "{\"schemaVersion\":1,\"exportId\":\"export-2\",\"files\":{\"file-1\":{\"path\":\"Files/a.png\",\"sha256\":\"ab\"}}}");
            var sidecar = NeoComposeExportIngest.ReadSidecar(fixture.projectRoot)!;
            Assert.AreEqual("export-2", sidecar.exportId);
            Assert.AreEqual("Files/a.png", sidecar.files["file-1"].path);

            File.WriteAllText(sidecarPath, "{\"schemaVersion\":2}");
            var error = Assert.Throws<InvalidOperationException>(
                () => NeoComposeExportIngest.ReadSidecar(fixture.projectRoot));
            StringAssert.Contains("is schema 2, but this SDK reads schema 1", error!.Message);
        }

        [Test]
        public void Ingest_CopiesChangedFilesFromTheWorkspaceAndStoresAssetDatabase()
        {
            using var fixture = new IngestFixture(@"
    ""file-1"": {
      ""_id"": ""file-1"",
      ""id"": ""file-1"",
      ""projectId"": ""project-1"",
      ""status"": ""uploaded"",
      ""name"": ""hero.png"",
      ""fileType"": ""image"",
      ""mimeType"": ""image/png"",
      ""byteLength"": 3,
      ""storageKey"": ""projects/project-1/files/file-1"",
      ""storageETag"": ""etag-1"",
      ""unityTextureSettings"": { ""templateId"": ""texture-template-1"", ""type"": ""texture-2d"", ""values"": {}, ""overridePaths"": [] },
      ""createdAt"": ""1970-01-01T00:00:00.000Z"",
      ""updatedAt"": ""1970-01-02T00:00:00.000Z""
    },
    ""file-2"": {
      ""_id"": ""file-2"",
      ""id"": ""file-2"",
      ""projectId"": ""project-1"",
      ""status"": ""pending-upload"",
      ""name"": ""draft.png"",
      ""fileType"": ""image"",
      ""mimeType"": ""image/png"",
      ""byteLength"": 3,
      ""storageKey"": ""projects/project-1/files/file-2"",
      ""storageETag"": null,
      ""createdAt"": ""1970-01-01T00:00:00.000Z"",
      ""updatedAt"": ""1970-01-02T00:00:00.000Z""
    }", SpriteTemplateJson("1970-01-03T00:00:00.000Z"));
            fixture.AddWorkspaceFile("file-1", "Files/Sprites/hero.png", new byte[] { 1, 2, 3 });
            var expectedSprite = Sprite.Create(new Texture2D(1, 1), new Rect(0, 0, 1, 1), Vector2.zero);
            fixture.assets.loadedSprites[HeroAssetPath] = new[] { expectedSprite };

            var errors = fixture.Ingest();

            CollectionAssert.IsEmpty(errors);
            CollectionAssert.AreEqual(new[] { HeroAssetPath }, fixture.assets.writtenPaths);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, fixture.assets.binaryFiles[HeroAssetPath]);
            Assert.Contains(HeroAssetPath, fixture.assets.appliedImportSettings);
            var entry = fixture.assets.assetDatabase.TryGetEntry("file-1");
            Assert.IsNotNull(entry);
            Assert.AreEqual("hero.png", entry!.FileName);
            Assert.AreEqual(HeroAssetPath, entry.AssetPath);
            Assert.AreEqual("86400000", entry.FileUpdatedAt);
            Assert.AreEqual("texture-template-1", entry.TemplateId);
            Assert.IsNotEmpty(entry.FileRecordHash);
            Assert.IsNotEmpty(entry.TemplateRecordHash);
            Assert.AreEqual("2026-05-13.2", entry.ImportSettingsVersion);
            Assert.AreSame(expectedSprite, entry.Sprites[0]);
            Assert.IsTrue(fixture.assets.savedAsset);
            UnityEngine.Object.DestroyImmediate(expectedSprite.texture);
            UnityEngine.Object.DestroyImmediate(expectedSprite);
        }

        [Test]
        public void Ingest_SkipsFilesWhenAssetDatabaseIsCurrent()
        {
            using var fixture = new IngestFixture(HeroFileJson());
            fixture.AddWorkspaceFile("file-1", "Files/Sprites/hero.png", new byte[] { 1, 2, 3 });
            fixture.assets.assetDatabase.SetFile(
                "file-1",
                "hero.png",
                HeroAssetPath,
                "1970-01-02T00:00:00.000Z",
                "1970-01-04T00:00:00.000Z",
                NeoComposeFileSynchronizer.ComputeRecordHash(fixture.ReadProjectData().files["file-1"]),
                null,
                "",
                "2026-05-13.2");
            // Unity serializes null strings as "" across domain reloads; a
            // template-less file must still count as current afterwards.
            fixture.assets.assetDatabase.TryGetEntry("file-1")!.TemplateId = "";
            fixture.assets.binaryFiles[HeroAssetPath] = new byte[] { 7 };

            CollectionAssert.IsEmpty(fixture.Ingest());

            CollectionAssert.IsEmpty(fixture.assets.writtenPaths);
            CollectionAssert.AreEqual(new byte[] { 7 }, fixture.assets.binaryFiles[HeroAssetPath]);
            Assert.IsTrue(fixture.assets.savedAsset);
        }

        [Test]
        public void Ingest_RecopiesFileWhenFileRecordChanges()
        {
            using var fixture = new IngestFixture(HeroFileJson(updatedAt: "1970-01-05T00:00:00.000Z"));
            fixture.AddWorkspaceFile("file-1", "Files/Sprites/hero.png", new byte[] { 4, 5, 6 });
            fixture.assets.assetDatabase.SetFile(
                "file-1",
                "hero.png",
                HeroAssetPath,
                "1970-01-02T00:00:00.000Z",
                "1970-01-04T00:00:00.000Z",
                "hash-of-previous-record",
                null,
                "",
                "2026-05-13.2");
            fixture.assets.binaryFiles[HeroAssetPath] = new byte[] { 1, 2, 3 };

            CollectionAssert.IsEmpty(fixture.Ingest());

            CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, fixture.assets.binaryFiles[HeroAssetPath]);
            Assert.AreEqual(
                NeoComposeFileSynchronizer.ComputeRecordHash(fixture.ReadProjectData().files["file-1"]),
                fixture.assets.assetDatabase.TryGetEntry("file-1")!.FileRecordHash);
        }

        [Test]
        public void Ingest_RecopiesFileWhenTemplateRecordChanges()
        {
            using var fixture = new IngestFixture(
                HeroFileJson(templateId: "texture-template-1"),
                SpriteTemplateJson("1970-01-06T00:00:00.000Z"));
            fixture.AddWorkspaceFile("file-1", "Files/Sprites/hero.png", new byte[] { 1, 2, 3 });
            var exportedData = fixture.ReadProjectData();
            fixture.assets.assetDatabase.SetFile(
                "file-1",
                "hero.png",
                HeroAssetPath,
                "86400000",
                "1970-01-04T00:00:00.000Z",
                NeoComposeFileSynchronizer.ComputeRecordHash(exportedData.files["file-1"]),
                "texture-template-1",
                "hash-of-previous-template-record",
                "2026-05-13.2");
            fixture.assets.binaryFiles[HeroAssetPath] = new byte[] { 1, 2, 3 };

            CollectionAssert.IsEmpty(fixture.Ingest());

            CollectionAssert.AreEqual(new[] { HeroAssetPath }, fixture.assets.writtenPaths);
            Assert.Contains(HeroAssetPath, fixture.assets.appliedImportSettings);
            Assert.AreEqual(
                NeoComposeFileSynchronizer.ComputeRecordHash(exportedData.textureTemplates["texture-template-1"]),
                fixture.assets.assetDatabase.TryGetEntry("file-1")!.TemplateRecordHash);
        }

        [Test]
        public void Ingest_RecopiesFileWhenDatabaseEntryExistsButAssetIsMissing()
        {
            using var fixture = new IngestFixture(HeroFileJson());
            fixture.AddWorkspaceFile("file-1", "Files/Sprites/hero.png", new byte[] { 1, 2, 3 });
            fixture.assets.assetDatabase.SetFile(
                "file-1",
                "hero.png",
                HeroAssetPath,
                "1970-01-02T00:00:00.000Z",
                "1970-01-04T00:00:00.000Z",
                "",
                null,
                "",
                "2026-05-13.2");

            CollectionAssert.IsEmpty(fixture.Ingest());

            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, fixture.assets.binaryFiles[HeroAssetPath]);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Ingest_DeletesStaleDatabaseEntriesOnlyAfterConfirmation(bool confirmed)
        {
            using var fixture = new IngestFixture("");
            fixture.assets.binaryFiles[HeroAssetPath] = new byte[] { 1 };
            fixture.assets.assetDatabase.SetFile(
                "file-1",
                "hero.png",
                HeroAssetPath,
                "1970-01-02T00:00:00.000Z",
                "1970-01-04T00:00:00.000Z",
                "",
                null,
                "",
                "2026-05-13.2");

            CollectionAssert.IsEmpty(fixture.Ingest(confirmed));

            Assert.AreEqual(confirmed, fixture.assets.deletedAssets.Contains(HeroAssetPath));
            Assert.AreEqual(confirmed, fixture.assets.assetDatabase.TryGetEntry("file-1") == null);
        }

        [Test]
        public void NormalizeOutputDirectories_NormalizesLocalizationDirectories()
        {
            var config = MakeConfig();
            config.localizationResourcesDirectory = "Assets\\Resources\\Neo\\Localization\\";
            config.localizationStreamingAssetsDirectory = "Assets\\StreamingAssets\\Neo\\Localization\\";

            NeoComposeExportIngest.NormalizeOutputDirectories(config);

            Assert.AreEqual("Assets/Resources/Neo/Localization", config.localizationResourcesDirectory);
            Assert.AreEqual("Assets/StreamingAssets/Neo/Localization", config.localizationStreamingAssetsDirectory);
        }

        [Test]
        public void NormalizeOutputDirectories_RejectsLocalizationResourcesOutsideResources()
        {
            var config = MakeConfig();
            config.localizationResourcesDirectory = "Assets/Neo/Localization";

            var error = Assert.Throws<InvalidOperationException>(
                () => NeoComposeExportIngest.NormalizeOutputDirectories(config));

            StringAssert.Contains("Assets/Resources/", error!.Message);
        }

        [Test]
        public void NormalizeOutputDirectories_RejectsLocalizationStreamingOutsideStreamingAssets()
        {
            var config = MakeConfig();
            config.localizationStreamingAssetsDirectory = "Assets/Resources/Neo/Localization";

            var error = Assert.Throws<InvalidOperationException>(
                () => NeoComposeExportIngest.NormalizeOutputDirectories(config));

            StringAssert.Contains("Assets/StreamingAssets/", error!.Message);
        }

        private static string HeroFileJson(
            string id = "file-1",
            string name = "hero.png",
            string updatedAt = "1970-01-02T00:00:00.000Z",
            string? templateId = null)
        {
            var settings = templateId == null
                ? ""
                : $@"""unityTextureSettings"": {{ ""templateId"": ""{templateId}"", ""type"": ""texture-2d"", ""values"": {{}}, ""overridePaths"": [] }},";
            return $@"
    ""{id}"": {{
      ""_id"": ""{id}"",
      ""id"": ""{id}"",
      ""projectId"": ""project-1"",
      ""status"": ""uploaded"",
      ""name"": ""{name}"",
      ""fileType"": ""image"",
      ""mimeType"": ""image/png"",
      ""byteLength"": 3,
      ""storageKey"": ""projects/project-1/files/{id}"",
      ""storageETag"": ""etag-1"",
      {settings}
      ""createdAt"": ""1970-01-01T00:00:00.000Z"",
      ""updatedAt"": ""{updatedAt}""
    }}";
        }

        private static string SpriteTemplateJson(string updatedAt) => $@"
    ""texture-template-1"": {{
      ""_id"": ""texture-template-1"",
      ""id"": ""texture-template-1"",
      ""projectId"": ""project-1"",
      ""name"": ""Sprites"",
      ""type"": ""texture-2d"",
      ""textureType"": ""sprite"",
      ""filterMode"": ""point"",
      ""createdAt"": ""1970-01-01T00:00:00.000Z"",
      ""updatedAt"": ""{updatedAt}""
    }}";

        /// <summary>
        /// A Unity project root holding a split export, plus a workspace the
        /// sidecar's file paths point into. Both live in a temp directory.
        /// </summary>
        private sealed class IngestFixture : IDisposable
        {
            private readonly string root = Path.Combine(Path.GetTempPath(), "neo-ingest-" + Guid.NewGuid().ToString("N"));
            public readonly string projectRoot;
            public readonly string workspaceRoot;
            public readonly NeoComposeConfig config;
            public readonly FakeAssetService assets = new();
            public readonly NeoComposeExportSidecar sidecar;

            public IngestFixture(string filesJson, string textureTemplatesJson = "", NeoComposeConfig? config = null)
            {
                projectRoot = Path.Combine(root, "UnityProject");
                workspaceRoot = Path.Combine(root, "neo");
                this.config = config ?? MakeConfig();
                Directory.CreateDirectory(Path.Combine(projectRoot, "Library", "NeoCompose"));
                Directory.CreateDirectory(workspaceRoot);
                NeoTestExport.Write(
                    ProjectJsonDirectory,
                    ProjectJsonWithFiles(filesJson)
                        .Replace(@"""textureTemplates"": {}", @"""textureTemplates"": {" + textureTemplatesJson + "}"));
                sidecar = new NeoComposeExportSidecar
                {
                    schemaVersion = NeoComposeExportIngest.SidecarSchemaVersion,
                    exportId = "export-1",
                    workspaceRoot = workspaceRoot,
                    projectId = "project-1",
                    versionId = "version-1",
                };
            }

            private string ProjectJsonDirectory => Path.Combine(projectRoot, config.projectJsonDirectory);

            public string? Marker
            {
                get
                {
                    var path = Path.Combine(projectRoot, NeoComposeExportIngest.IngestedMarkerPath);
                    return File.Exists(path) ? File.ReadAllText(path) : null;
                }
            }

            public void AddWorkspaceFile(string fileId, string path, byte[] bytes)
            {
                var fullPath = Path.Combine(workspaceRoot, path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                File.WriteAllBytes(fullPath, bytes);
                using var sha256 = SHA256.Create();
                var hex = new StringBuilder();
                foreach (var b in sha256.ComputeHash(bytes))
                {
                    hex.Append(b.ToString("x2"));
                }
                sidecar.files[fileId] = new NeoComposeExportSidecarFile
                {
                    path = path,
                    sha256 = hex.ToString(),
                };
            }

            public ProjectData ReadProjectData() =>
                NeoJsonProjectDataSource.FromFile(Path.Combine(ProjectJsonDirectory, "project.json")).ReadProjectData();

            public string[] Ingest(params bool[] confirmations) =>
                NeoComposeExportIngest.Ingest(
                    projectRoot,
                    sidecar,
                    config,
                    new FakeConfirmationService(confirmations),
                    assets);

            public void Dispose()
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, true);
            }
        }
    }
}
