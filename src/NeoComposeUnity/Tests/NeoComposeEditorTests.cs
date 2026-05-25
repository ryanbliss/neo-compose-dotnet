// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Unity.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Tests
{
    public class NeoComposeEditorTests
    {
        private const string TempRoot = "Assets/NeoComposeEditorTestsTemp";

        [SetUp]
        public void SetUp()
        {
            CleanupTempRoot();
            AssetDatabase.CreateFolder("Assets", "NeoComposeEditorTestsTemp");
        }

        [TearDown]
        public void TearDown()
        {
            CleanupTempRoot();
        }

        [Test]
        public void PathUtility_AcceptsExpectedAssetDirectories()
        {
            Assert.IsTrue(NeoComposePathUtility.TryNormalizeAssetDirectory(
                "Assets/Scripts/Neo/",
                out var scripts,
                out var scriptsError));
            Assert.AreEqual("", scriptsError);
            Assert.AreEqual("Assets/Scripts/Neo", scripts);

            Assert.IsTrue(NeoComposePathUtility.TryNormalizeAssetDirectory(
                "Assets\\Resources\\Neo",
                out var resources,
                out var resourcesError));
            Assert.AreEqual("", resourcesError);
            Assert.AreEqual("Assets/Resources/Neo", resources);
        }

        [Test]
        public void PathUtility_RejectsPathsOutsideAssets()
        {
            Assert.IsFalse(NeoComposePathUtility.TryNormalizeAssetDirectory(
                "/tmp/Neo",
                out _,
                out var absoluteError));
            Assert.IsTrue(absoluteError.Contains("project-relative"));

            Assert.IsFalse(NeoComposePathUtility.TryNormalizeAssetDirectory(
                "Packages/com.example",
                out _,
                out var packageError));
            Assert.IsTrue(packageError.Contains("Assets/"));

            Assert.IsFalse(NeoComposePathUtility.TryNormalizeAssetDirectory(
                "Assets/../ProjectSettings",
                out _,
                out var parentError));
            Assert.IsTrue(parentError.Contains(".."));
        }

        [Test]
        public void PathUtility_ValidatesLocalizationDirectories()
        {
            Assert.IsTrue(NeoComposePathUtility.TryNormalizeResourcesDirectory(
                "Assets\\Resources\\Neo\\Localization\\",
                out var resources,
                out var resourcesError));
            Assert.AreEqual("", resourcesError);
            Assert.AreEqual("Assets/Resources/Neo/Localization", resources);

            Assert.IsFalse(NeoComposePathUtility.TryNormalizeResourcesDirectory(
                "Assets/StreamingAssets/Neo/Localization",
                out _,
                out var invalidResourcesError));
            Assert.IsTrue(invalidResourcesError.Contains("Assets/Resources/"));

            Assert.IsTrue(NeoComposePathUtility.TryNormalizeStreamingAssetsDirectory(
                "Assets\\StreamingAssets\\Neo\\Localization\\",
                out var streaming,
                out var streamingError));
            Assert.AreEqual("", streamingError);
            Assert.AreEqual("Assets/StreamingAssets/Neo/Localization", streaming);

            Assert.IsFalse(NeoComposePathUtility.TryNormalizeStreamingAssetsDirectory(
                "Assets/Resources/Neo/Localization",
                out _,
                out var invalidStreamingError));
            Assert.IsTrue(invalidStreamingError.Contains("Assets/StreamingAssets/"));
        }

        [Test]
        public void ConfigProvider_CreatesDefaultConfigInResourcesFolder()
        {
            var path = $"{TempRoot}/Resources/Neo/NeoComposeConfig.asset";

            var config = NeoComposeConfigProvider.LoadOrCreate(path, new[] { TempRoot });

            Assert.IsNotNull(config);
            Assert.AreEqual(path, AssetDatabase.GetAssetPath(config));
            Assert.AreEqual(NeoComposeDefaults.ApiBaseUrl, config.apiBaseUrl);
            Assert.AreEqual(NeoComposeDefaults.GeneratedTypesDirectory, config.generatedTypesDirectory);
            Assert.AreEqual(NeoComposeDefaults.ProjectJsonDirectory, config.projectJsonDirectory);
            Assert.AreEqual(NeoComposeDefaults.LocalizationResourcesDirectory, config.localizationResourcesDirectory);
            Assert.AreEqual(NeoComposeDefaults.LocalizationStreamingAssetsDirectory, config.localizationStreamingAssetsDirectory);
            Assert.IsFalse(config.useStreamingAssetsForNonMainLocales);
            Assert.IsTrue(config.preloadSystemLocale);
            Assert.AreEqual("", config.localeOverride);
            Assert.AreEqual(NeoComposeDefaults.SpriteDirectory, config.spriteDirectory);
            Assert.AreEqual(NeoComposeDefaults.AudioClipDirectory, config.audioClipDirectory);
            Assert.AreEqual(NeoComposeDefaults.NamespaceForGeneratedTypes, config.namespaceForGeneratedTypes);
            Assert.AreEqual(NeoComposeDefaults.Singleton, config.singleton);
        }

        [Test]
        public void ConfigProvider_FindsMovedConfigByType()
        {
            AssetDatabase.CreateFolder(TempRoot, "Moved");
            var path = $"{TempRoot}/Moved/NeoComposeConfig.asset";
            var moved = ScriptableObject.CreateInstance<NeoComposeConfig>();
            moved.apiBaseUrl = "http://localhost:4000";
            AssetDatabase.CreateAsset(moved, path);
            AssetDatabase.SaveAssets();

            var config = NeoComposeConfigProvider.LoadOrCreate(
                $"{TempRoot}/Resources/Neo/NeoComposeConfig.asset",
                new[] { TempRoot });

            Assert.AreSame(moved, config);
            Assert.AreEqual("http://localhost:4000", config.apiBaseUrl);
        }

        [Test]
        public void Config_ClearProject_OnlyUnlinksProjectFields()
        {
            var config = ScriptableObject.CreateInstance<NeoComposeConfig>();
            config.SelectProject("project-1", "Project One");
            config.targetReleaseChannelId = "development";
            config.versionId = "version-1";
            config.generatedTypesDirectory = "Assets/CustomTypes";
            config.projectJsonDirectory = "Assets/CustomJson";
            config.localizationResourcesDirectory = "Assets/Resources/CustomLocalization";
            config.localizationStreamingAssetsDirectory = "Assets/StreamingAssets/CustomLocalization";
            config.useStreamingAssetsForNonMainLocales = true;
            config.preloadSystemLocale = false;
            config.localeOverride = "es-ES";
            config.spriteDirectory = "Assets/CustomSprites";
            config.audioClipDirectory = "Assets/CustomAudio";
            config.namespaceForGeneratedTypes = "Game.Generated";
            config.singleton = false;

            config.ClearProject();

            Assert.AreEqual("", config.projectId);
            Assert.AreEqual("", config.projectName);
            Assert.AreEqual("", config.targetReleaseChannelId);
            Assert.AreEqual("", config.versionId);
            Assert.AreEqual("Assets/CustomTypes", config.generatedTypesDirectory);
            Assert.AreEqual("Assets/CustomJson", config.projectJsonDirectory);
            Assert.AreEqual("Assets/Resources/CustomLocalization", config.localizationResourcesDirectory);
            Assert.AreEqual("Assets/StreamingAssets/CustomLocalization", config.localizationStreamingAssetsDirectory);
            Assert.IsTrue(config.useStreamingAssetsForNonMainLocales);
            Assert.IsFalse(config.preloadSystemLocale);
            Assert.AreEqual("es-ES", config.localeOverride);
            Assert.AreEqual("Assets/CustomSprites", config.spriteDirectory);
            Assert.AreEqual("Assets/CustomAudio", config.audioClipDirectory);
            Assert.AreEqual("Game.Generated", config.namespaceForGeneratedTypes);
            Assert.IsFalse(config.singleton);
        }

        [Test]
        public void GeneratedTypesSupport_LookupSelectionId_ReturnsBoundValueId()
        {
            Assert.AreEqual("value-1", NeoGeneratedTypesSupport.LookupSelectionId("value-1"));
        }

        [Test]
        public void VersionSelection_DefaultsToDevelopmentLatestSemver()
        {
            var channels = new[]
            {
                new NeoComposeProjectReleaseChannel { id = "production", name = "Production", slug = "production", sortOrder = 1 },
                new NeoComposeProjectReleaseChannel { id = "development", name = "Development", slug = "development", sortOrder = 0 },
            };
            var statuses = new[]
            {
                new NeoComposeProjectVersionStatus
                {
                    id = "draft",
                    name = "Draft",
                    isWritable = true,
                    releaseChannelIds = new[] { "development" },
                },
                new NeoComposeProjectVersionStatus
                {
                    id = "published",
                    name = "Published",
                    releaseChannelIds = new[] { "production" },
                },
            };
            var versions = new[]
            {
                Version("v-0-1-1", "draft", 0, 1, 1),
                Version("v-0-2-0", "draft", 0, 2, 0),
                Version("v-1-0-0", "published", 1, 0, 0),
            };

            var channelId = NeoComposeVersionSelectionUtility.SelectDefaultReleaseChannelId(channels);
            var latest = NeoComposeVersionSelectionUtility.SelectLatestVersionForChannel(versions, statuses, channelId);

            Assert.AreEqual("development", channelId);
            Assert.IsNotNull(latest);
            Assert.AreEqual("v-0-2-0", latest!.id);
        }

        [Test]
        public void VersionSelection_KeepsPinnedArchivedVersionInDropdown()
        {
            var statuses = new[]
            {
                new NeoComposeProjectVersionStatus
                {
                    id = "draft",
                    name = "Draft",
                    releaseChannelIds = new[] { "development" },
                },
            };
            var current = Version("v-archived", "draft", 0, 1, 0);
            current.archivedAt = "1970-01-01T00:00:00.000Z";
            var versions = new[]
            {
                Version("v-latest", "draft", 0, 2, 0),
                current,
            };

            var options = NeoComposeVersionSelectionUtility.BuildVersionDropdownOptions(
                versions,
                statuses,
                "development",
                "v-archived");

            CollectionAssert.AreEqual(new[] { "v-archived", "v-latest" }, options.Select(version => version.id).ToArray());
        }

        [Test]
        public void GeneratedTypesSupport_LookupSelectionId_RejectsMissingValueId()
        {
            Assert.Throws<System.InvalidOperationException>(
                () => NeoGeneratedTypesSupport.LookupSelectionId(null));
        }

        [Test]
        public void GeneratedTypesSupport_ValuePayload_ReadsProvider()
        {
            var payload = new NeoValuePayload("value", "type-id");

            Assert.AreSame(
                payload,
                NeoGeneratedTypesSupport.ValuePayload(
                    new TestPayloadProvider(payload)));
            Assert.IsNull(NeoGeneratedTypesSupport.ValuePayload(null));
        }

        [Test]
        public async Task Synchronizer_WritesGeneratedTypesAndProjectJson()
        {
            var config = MakeConfig();
            var api = new FakeApiClient();
            api.exportResponse.generatedTypes = "// generated";
            api.exportResponse.projectJson = "{ \"project\": true }";
            var assets = new FakeAssetService();
            var confirmations = new FakeConfirmationService(true);
            var synchronizer = new NeoComposeSynchronizer(api, confirmations, assets);

            var result = await synchronizer.SynchronizeAsync(config);

            Assert.IsTrue(result.success, result.message);
            Assert.AreEqual("// generated", assets.files["Assets/Scripts/Neo/NeoGeneratedTypes.cs"]);
            Assert.AreEqual("{ \"project\": true }", assets.files["Assets/Resources/Neo/project.json"]);
            Assert.Contains("Assets/Scripts/Neo", assets.createdDirectories);
            Assert.Contains("Assets/Resources/Neo", assets.createdDirectories);
            Assert.IsTrue(assets.savedConfig);
            Assert.AreEqual("version-1", api.lastExportVersionId);
            Assert.AreEqual("Assets/Resources/Neo/project.json", assets.postSynchronizeProjectJsonPath);
        }

        [Test]
        public async Task Synchronizer_WritesLocalizationFilesToResourcesByDefault()
        {
            var config = MakeConfig();
            var api = new FakeApiClient();
            api.exportResponse.projectJson = ProjectJsonWithLocalization("en-US");
            api.exportResponse.localizationFiles.Add(new NeoComposeUnityLocalizationFile
            {
                locale = "en-US",
                fileName = "en-US.json",
                content = "{ \"locale\": \"en-US\" }",
            });
            api.exportResponse.localizationFiles.Add(new NeoComposeUnityLocalizationFile
            {
                locale = "es-MX",
                fileName = "es-MX.json",
                content = "{ \"locale\": \"es-MX\" }",
            });
            var assets = new FakeAssetService();
            assets.files["Assets/Resources/Neo/Localization/fr-FR.json"] = "{}";
            var synchronizer = new NeoComposeSynchronizer(
                api,
                new FakeConfirmationService(true),
                assets);

            var result = await synchronizer.SynchronizeAsync(config);

            Assert.IsTrue(result.success, result.message);
            Assert.AreEqual(
                "{ \"locale\": \"en-US\" }",
                assets.files["Assets/Resources/Neo/Localization/en-US.json"]);
            Assert.AreEqual(
                "{ \"locale\": \"es-MX\" }",
                assets.files["Assets/Resources/Neo/Localization/es-MX.json"]);
            Assert.Contains("Assets/Resources/Neo/Localization", assets.createdDirectories);
            Assert.Contains("Assets/Resources/Neo/Localization/fr-FR.json", assets.deletedAssets);
        }

        [Test]
        public async Task Synchronizer_WritesNonMainLocalizationFilesToStreamingAssetsWhenEnabled()
        {
            var config = MakeConfig();
            config.useStreamingAssetsForNonMainLocales = true;
            var api = new FakeApiClient();
            api.exportResponse.projectJson = ProjectJsonWithLocalization("en-US");
            api.exportResponse.localizationFiles.Add(new NeoComposeUnityLocalizationFile
            {
                locale = "en-US",
                fileName = "en-US.json",
                content = "{ \"locale\": \"en-US\" }",
            });
            api.exportResponse.localizationFiles.Add(new NeoComposeUnityLocalizationFile
            {
                locale = "es-MX",
                fileName = "es-MX.json",
                content = "{ \"locale\": \"es-MX\" }",
            });
            var assets = new FakeAssetService();
            var synchronizer = new NeoComposeSynchronizer(
                api,
                new FakeConfirmationService(true),
                assets);

            var result = await synchronizer.SynchronizeAsync(config);

            Assert.IsTrue(result.success, result.message);
            Assert.AreEqual(
                "{ \"locale\": \"en-US\" }",
                assets.files["Assets/Resources/Neo/Localization/en-US.json"]);
            Assert.AreEqual(
                "{ \"locale\": \"es-MX\" }",
                assets.files["Assets/StreamingAssets/Neo/Localization/es-MX.json"]);
            Assert.Contains("Assets/Resources/Neo/Localization", assets.createdDirectories);
            Assert.Contains("Assets/StreamingAssets/Neo/Localization", assets.createdDirectories);
        }

        [Test]
        public async Task Synchronizer_ReportsLocalizationWriteFailuresAfterWritingProjectFiles()
        {
            var config = MakeConfig();
            var api = new FakeApiClient();
            api.exportResponse.generatedTypes = "// generated";
            api.exportResponse.projectJson = ProjectJsonWithLocalization("en-US");
            api.exportResponse.localizationFiles.Add(new NeoComposeUnityLocalizationFile
            {
                locale = "en-US",
                fileName = "en-US.json",
                content = "{ \"locale\": \"en-US\" }",
            });
            var assets = new FakeAssetService();
            assets.throwOnWriteText.Add("Assets/Resources/Neo/Localization/en-US.json");
            var synchronizer = new NeoComposeSynchronizer(
                api,
                new FakeConfirmationService(true),
                assets);

            var result = await synchronizer.SynchronizeAsync(config);

            Assert.IsFalse(result.success);
            Assert.IsTrue(result.message.Contains("en-US"));
            Assert.AreEqual("// generated", assets.files["Assets/Scripts/Neo/NeoGeneratedTypes.cs"]);
            Assert.AreEqual(ProjectJsonWithLocalization("en-US"), assets.files["Assets/Resources/Neo/project.json"]);
        }

        [Test]
        public void Synchronizer_ValidateConfig_NormalizesLocalizationDirectories()
        {
            var config = MakeConfig();
            config.localizationResourcesDirectory = "Assets\\Resources\\Neo\\Localization\\";
            config.localizationStreamingAssetsDirectory = "Assets\\StreamingAssets\\Neo\\Localization\\";

            var result = NeoComposeSynchronizer.ValidateConfig(config);

            Assert.IsTrue(result.success, result.message);
            Assert.AreEqual("Assets/Resources/Neo/Localization", config.localizationResourcesDirectory);
            Assert.AreEqual("Assets/StreamingAssets/Neo/Localization", config.localizationStreamingAssetsDirectory);
        }

        [Test]
        public void Synchronizer_ValidateConfig_RejectsLocalizationResourcesOutsideResources()
        {
            var config = MakeConfig();
            config.localizationResourcesDirectory = "Assets/Neo/Localization";

            var result = NeoComposeSynchronizer.ValidateConfig(config);

            Assert.IsFalse(result.success);
            Assert.IsTrue(result.message.Contains("Assets/Resources/"));
        }

        [Test]
        public void Synchronizer_ValidateConfig_RejectsLocalizationStreamingOutsideStreamingAssets()
        {
            var config = MakeConfig();
            config.localizationStreamingAssetsDirectory = "Assets/Resources/Neo/Localization";

            var result = NeoComposeSynchronizer.ValidateConfig(config);

            Assert.IsFalse(result.success);
            Assert.IsTrue(result.message.Contains("Assets/StreamingAssets/"));
        }

        [Test]
        public async Task Synchronizer_DownloadsChangedUnityFilesAndStoresAssetDatabase()
        {
            var config = MakeConfig();
            var api = new FakeApiClient();
            api.exportResponse.projectJson = @"
{
  ""project"": {
    ""_id"": ""project-1"",
    ""id"": ""project-1"",
    ""name"": ""Project One"",
    ""rootAssetsAttributeId"": ""assets-root"",
    ""rootSaveFileAttributeId"": ""save-root"",
    ""createdAt"": ""1970-01-01T00:00:00.000Z"",
    ""updatedAt"": ""1970-01-01T00:00:00.000Z""
  },
  ""attributes"": {},
  ""values"": {},
  ""types"": {},
  ""enums"": {},
  ""files"": {
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
    }
  },
  ""textureTemplates"": {
    ""texture-template-1"": {
      ""_id"": ""texture-template-1"",
      ""id"": ""texture-template-1"",
      ""projectId"": ""project-1"",
      ""name"": ""Sprites"",
      ""type"": ""texture-2d"",
      ""textureType"": ""sprite"",
      ""textureShape"": ""2d"",
      ""sRGBTexture"": true,
      ""alphaSource"": ""input-texture-alpha"",
      ""alphaIsTransparency"": true,
      ""nonPowerOfTwoScale"": ""none"",
      ""ignorePngGamma"": false,
      ""readWriteEnabled"": false,
      ""virtualTextureOnly"": false,
      ""generateMipMaps"": false,
      ""borderMipMaps"": false,
      ""mipMapFiltering"": ""box"",
      ""mipMapsPreserveCoverage"": false,
      ""alphaCutoffValue"": 0.5,
      ""fadeOutMipMaps"": false,
      ""mipMapFadeDistanceStart"": 1,
      ""mipMapFadeDistanceEnd"": 3,
      ""anisoLevel"": 1,
      ""wrapMode"": ""clamp"",
      ""filterMode"": ""point"",
      ""maxTextureSize"": 2048,
      ""resizeAlgorithm"": ""mitchell"",
      ""textureCompression"": ""none"",
      ""compressionQuality"": 50,
      ""crunchedCompression"": false,
      ""createdAt"": ""1970-01-01T00:00:00.000Z"",
      ""updatedAt"": ""1970-01-03T00:00:00.000Z""
    }
  },
  ""audioClipTemplates"": {}
}";
            api.fileDownloadResponse.files["file-1"] = new NeoComposeUnityExportFileDownload
            {
                fileId = "file-1",
                downloadUrl = "signed-url",
                expiresAt = "1970-01-01T00:05:00.000Z",
            };
            api.downloads["signed-url"] = new byte[] { 1, 2, 3 };
            var assets = new FakeAssetService();
            var expectedPath = "Assets/Resources/Neo/Files/Sprites/file-1-hero.png";
            var expectedSprite = Sprite.Create(new Texture2D(1, 1), new Rect(0, 0, 1, 1), Vector2.zero);
            assets.loadedSprites[expectedPath] = new[] { expectedSprite };
            var synchronizer = new NeoComposeSynchronizer(api, new FakeConfirmationService(true), assets);
            var progress = new List<string>();

            var result = await synchronizer.SynchronizeAsync(config, progress.Add);

            Assert.IsTrue(result.success, result.message);
            Assert.Contains("Requesting download URLs for 1 file asset(s)...", progress);
            Assert.Contains("Downloading file 1/1: hero.png", progress);
            Assert.Contains("Applying import settings 1/1: hero.png", progress);
            CollectionAssert.AreEqual(new[] { "file-1" }, api.lastFileDownloadIds);
            Assert.AreEqual("version-1", api.lastFileDownloadVersionId);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, assets.binaryFiles[expectedPath]);
            Assert.Contains(expectedPath, assets.appliedImportSettings);
            var entry = assets.assetDatabase.TryGetEntry("file-1");
            Assert.IsNotNull(entry);
            Assert.AreEqual("hero.png", entry!.FileName);
            Assert.AreEqual(expectedPath, entry.AssetPath);
            Assert.AreEqual("1970-01-02T00:00:00.000Z", entry.FileUpdatedAt);
            Assert.AreEqual("texture-template-1", entry.TemplateId);
            Assert.AreEqual("1970-01-03T00:00:00.000Z", entry.TemplateUpdatedAt);
            Assert.AreEqual("2026-05-13.2", entry.ImportSettingsVersion);
            Assert.AreSame(expectedSprite, entry.Sprites[0]);
            Assert.IsTrue(assets.savedAsset);
            Object.DestroyImmediate(expectedSprite.texture);
            Object.DestroyImmediate(expectedSprite);
        }

        [Test]
        public async Task Synchronizer_SkipsUnityFilesWhenAssetDatabaseIsCurrent()
        {
            var config = MakeConfig();
            var api = new FakeApiClient();
            api.exportResponse.projectJson = @"
{
  ""project"": {
    ""_id"": ""project-1"",
    ""id"": ""project-1"",
    ""name"": ""Project One"",
    ""rootAssetsAttributeId"": ""assets-root"",
    ""rootSaveFileAttributeId"": ""save-root"",
    ""createdAt"": ""1970-01-01T00:00:00.000Z"",
    ""updatedAt"": ""1970-01-01T00:00:00.000Z""
  },
  ""attributes"": {},
  ""values"": {},
  ""types"": {},
  ""enums"": {},
  ""files"": {
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
      ""createdAt"": ""1970-01-01T00:00:00.000Z"",
      ""updatedAt"": ""1970-01-02T00:00:00.000Z""
    }
  },
  ""textureTemplates"": {},
  ""audioClipTemplates"": {}
}";
            var assets = new FakeAssetService();
            var expectedPath = "Assets/Resources/Neo/Files/Sprites/file-1-hero.png";
            assets.assetDatabase.SetFile(
                "file-1",
                "hero.png",
                expectedPath,
                "1970-01-02T00:00:00.000Z",
                "1970-01-04T00:00:00.000Z",
                null,
                null,
                null,
                "2026-05-13.2");
            assets.binaryFiles[expectedPath] = new byte[] { 1, 2, 3 };
            var synchronizer = new NeoComposeSynchronizer(api, new FakeConfirmationService(true), assets);
            var progress = new List<string>();

            var result = await synchronizer.SynchronizeAsync(config, progress.Add);

            Assert.IsTrue(result.success, result.message);
            Assert.Contains("File assets are current.", progress);
            Assert.AreEqual(0, api.lastFileDownloadIds.Length);
            Assert.AreEqual(1, assets.binaryFiles.Count);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, assets.binaryFiles[expectedPath]);
            Assert.IsTrue(assets.savedAsset);
        }

        [Test]
        public async Task Synchronizer_RedownloadsUnityFileWhenDatabaseEntryExistsButAssetIsMissing()
        {
            var config = MakeConfig();
            var api = new FakeApiClient();
            api.exportResponse.projectJson = ProjectJsonWithFiles(@"
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
      ""createdAt"": ""1970-01-01T00:00:00.000Z"",
      ""updatedAt"": ""1970-01-02T00:00:00.000Z""
    }");
            api.fileDownloadResponse.files["file-1"] = new NeoComposeUnityExportFileDownload
            {
                fileId = "file-1",
                downloadUrl = "signed-url",
                expiresAt = "1970-01-01T00:05:00.000Z",
            };
            api.downloads["signed-url"] = new byte[] { 1, 2, 3 };
            var assets = new FakeAssetService();
            var expectedPath = "Assets/Resources/Neo/Files/Sprites/file-1-hero.png";
            assets.assetDatabase.SetFile(
                "file-1",
                "hero.png",
                expectedPath,
                "1970-01-02T00:00:00.000Z",
                "1970-01-04T00:00:00.000Z",
                null,
                null,
                null,
                "2026-05-13.2");
            var synchronizer = new NeoComposeSynchronizer(api, new FakeConfirmationService(true), assets);

            var result = await synchronizer.SynchronizeAsync(config);

            Assert.IsTrue(result.success, result.message);
            CollectionAssert.AreEqual(new[] { "file-1" }, api.lastFileDownloadIds);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, assets.binaryFiles[expectedPath]);
        }

        [Test]
        public async Task Synchronizer_DeletesStaleDatabaseEntriesAfterConfirmation()
        {
            var config = MakeConfig();
            var api = new FakeApiClient();
            api.exportResponse.projectJson = ProjectJsonWithFiles("");
            var assets = new FakeAssetService();
            assets.binaryFiles["Assets/Resources/Neo/Files/Sprites/file-1-hero.png"] = new byte[] { 1 };
            assets.assetDatabase.SetFile(
                "file-1",
                "hero.png",
                "Assets/Resources/Neo/Files/Sprites/file-1-hero.png",
                "1970-01-02T00:00:00.000Z",
                "1970-01-04T00:00:00.000Z",
                null,
                null,
                null,
                "2026-05-13.2");
            var synchronizer = new NeoComposeSynchronizer(api, new FakeConfirmationService(true), assets);

            var result = await synchronizer.SynchronizeAsync(config);

            Assert.IsTrue(result.success, result.message);
            Assert.Contains("Assets/Resources/Neo/Files/Sprites/file-1-hero.png", assets.deletedAssets);
            Assert.IsNull(assets.assetDatabase.TryGetEntry("file-1"));
        }

        [Test]
        public async Task Synchronizer_KeepsStaleDatabaseEntriesWhenDeletionIsDeclined()
        {
            var config = MakeConfig();
            var api = new FakeApiClient();
            api.exportResponse.projectJson = ProjectJsonWithFiles("");
            var assets = new FakeAssetService();
            assets.binaryFiles["Assets/Resources/Neo/Files/Sprites/file-1-hero.png"] = new byte[] { 1 };
            assets.assetDatabase.SetFile(
                "file-1",
                "hero.png",
                "Assets/Resources/Neo/Files/Sprites/file-1-hero.png",
                "1970-01-02T00:00:00.000Z",
                "1970-01-04T00:00:00.000Z",
                null,
                null,
                null,
                "2026-05-13.2");
            var synchronizer = new NeoComposeSynchronizer(api, new FakeConfirmationService(false), assets);

            var result = await synchronizer.SynchronizeAsync(config);

            Assert.IsTrue(result.success, result.message);
            Assert.AreEqual(0, assets.deletedAssets.Count);
            Assert.IsNotNull(assets.assetDatabase.TryGetEntry("file-1"));
        }

        [Test]
        public void ImportSettingsApplier_AppliesGridSpriteSlices()
        {
            var assetPath = $"{TempRoot}/sheet.png";
            var texture = new Texture2D(32, 16, TextureFormat.RGBA32, false);
            for (var y = 0; y < texture.height; y++)
            {
                for (var x = 0; x < texture.width; x++)
                {
                    texture.SetPixel(x, y, x < 16 ? Color.red : Color.blue);
                }
            }
            texture.Apply();
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(assetPath);

            var projectData = new ProjectData
            {
                textureTemplates = new Dictionary<string, UnityTexture2DImportSettingsTemplate>
                {
                    ["texture-template-1"] = MakeSpriteTemplate(),
                },
            };
            var file = new ProjectFile
            {
                id = "file-1",
                name = "sheet.png",
                fileType = "image",
                unityTextureSettings = new FileUnityTextureImportSettings
                {
                    templateId = "texture-template-1",
                    type = "texture-2d",
                    overridePaths = System.Array.Empty<string>(),
                },
            };

            new NeoComposeEditorAssetService().ApplyUnityImportSettings(assetPath, file, projectData);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            Assert.AreEqual(SpriteImportMode.Multiple, importer.spriteImportMode);
            Assert.AreEqual(16, importer.spritePixelsPerUnit);
            var sprites = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath)
                .OfType<Sprite>()
                .ToArray();
            Assert.AreEqual(2, sprites.Length);
            CollectionAssert.AreEqual(new[] { "sheet_0_0", "sheet_0_1" }, sprites.Select(sprite => sprite.name).ToArray());
            Assert.AreEqual(new Rect(0, 0, 16, 16), sprites[0].rect);
            Assert.AreEqual(new Rect(16, 0, 16, 16), sprites[1].rect);
        }

        [Test]
        public void AssetDatabase_ResolvesDirectReferencesAndReverseLookup()
        {
            var texture = new Texture2D(1, 1);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
            var audio = AudioClip.Create("voice", 1, 1, 44100, false);
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            try
            {
                database.SetFile(
                    "sprite-file",
                    "hero.png",
                    "Assets/Resources/Neo/Files/Sprites/sprite-file-hero.png",
                    "1970-01-02T00:00:00.000Z",
                    "1970-01-04T00:00:00.000Z",
                    "texture-template-1",
                    "1970-01-03T00:00:00.000Z",
                    "1970-01-04T00:00:00.000Z",
                    "2026-05-13.2",
                    new[] { sprite },
                    null);
                database.SetFile(
                    "audio-file",
                    "voice.wav",
                    "Assets/Resources/Neo/Files/Audio/audio-file-voice.wav",
                    "1970-01-02T00:00:00.000Z",
                    "1970-01-04T00:00:00.000Z",
                    "audio-template-1",
                    "1970-01-03T00:00:00.000Z",
                    "1970-01-04T00:00:00.000Z",
                    "2026-05-13.2",
                    null,
                    audio);

                Assert.AreSame(sprite, database.TryGetSprite("sprite-file", 0));
                Assert.AreSame(audio, database.TryGetAudioClip("audio-file"));

                var spriteValue = database.TryGetValueForSprite(sprite);
                Assert.IsNotNull(spriteValue);
                Assert.AreEqual("sprite-file", spriteValue!.fileId);
                Assert.AreEqual(0, spriteValue.sliceIndex);

                var audioValue = database.TryGetValueForAudioClip(audio);
                Assert.IsNotNull(audioValue);
                Assert.AreEqual("audio-file", audioValue!.fileId);

                Assert.DoesNotThrow(() =>
                    NeoAssetResolver.ValueForSprite(database, sprite, "texture-template-1", "Portrait"));
                Assert.DoesNotThrow(() =>
                    NeoAssetResolver.ValueForAudioClip(database, audio, "audio-template-1", "Voice"));
                Assert.Throws<System.InvalidOperationException>(() =>
                    NeoAssetResolver.ValueForSprite(database, sprite, "other-template", "Portrait"));
                Assert.Throws<System.InvalidOperationException>(() =>
                    NeoAssetResolver.ValueForAudioClip(database, audio, "other-template", "Voice"));
            }
            finally
            {
                Object.DestroyImmediate(audio);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(database);
            }
        }

        private sealed class TestPayloadProvider : INeoValuePayloadProvider
        {
            private readonly NeoValuePayload payload;

            public TestPayloadProvider(NeoValuePayload payload)
            {
                this.payload = payload;
            }

            public NeoValuePayload ToNeoValuePayload()
            {
                return payload;
            }
        }

        [Test]
        public async Task Synchronizer_UpdatesConfigUnitySettingsFromExportedProjectJson()
        {
            var config = MakeConfig();
            config.namespaceForGeneratedTypes = NeoComposeDefaults.NamespaceForGeneratedTypes;
            config.singleton = true;
            var api = new FakeApiClient();
            api.exportResponse.projectJson =
                "{ \"project\": { \"exportSettings\": { \"unity\": { \"namespaceForGeneratedTypes\": \"HelloWorld.Assets.Scripts.Neo\", \"singleton\": false } } } }";
            var assets = new FakeAssetService();
            var synchronizer = new NeoComposeSynchronizer(api, new FakeConfirmationService(true), assets);

            var result = await synchronizer.SynchronizeAsync(config);

            Assert.IsTrue(result.success, result.message);
            Assert.AreEqual("HelloWorld.Assets.Scripts.Neo", config.namespaceForGeneratedTypes);
            Assert.IsFalse(config.singleton);
            Assert.IsTrue(assets.savedConfig);
        }

        [Test]
        public async Task Synchronizer_RequiresConfirmationBeforeOverwritingFiles()
        {
            var config = MakeConfig();
            var api = new FakeApiClient();
            api.exportResponse.generatedTypes = "new";
            var assets = new FakeAssetService();
            assets.files["Assets/Scripts/Neo/NeoGeneratedTypes.cs"] = "existing";
            var confirmations = new FakeConfirmationService(false);
            var synchronizer = new NeoComposeSynchronizer(api, confirmations, assets);

            var result = await synchronizer.SynchronizeAsync(config);

            Assert.IsFalse(result.success);
            Assert.AreEqual("existing", assets.files["Assets/Scripts/Neo/NeoGeneratedTypes.cs"]);
            Assert.AreEqual(1, confirmations.calls.Count);
            Assert.IsTrue(confirmations.calls[0].Contains("Replace"));
        }

        [Test]
        public async Task Synchronizer_RequiresConfirmationBeforeWritingErroredGeneratedCode()
        {
            var config = MakeConfig();
            var api = new FakeApiClient();
            api.exportResponse.diagnostics.Add(new NeoComposeCodegenDiagnostic
            {
                severity = "error",
                path = "types.bad",
                message = "Bad generated type.",
            });
            var assets = new FakeAssetService();
            var confirmations = new FakeConfirmationService(false);
            var synchronizer = new NeoComposeSynchronizer(api, confirmations, assets);

            var result = await synchronizer.SynchronizeAsync(config);

            Assert.IsFalse(result.success);
            Assert.AreEqual(0, assets.files.Count);
            Assert.AreEqual(1, confirmations.calls.Count);
            Assert.IsTrue(confirmations.calls[0].Contains("generated C#"));
        }

        [Test]
        public async Task Synchronizer_CanContinueAndWriteFilesAfterExportDiagnostics()
        {
            var config = MakeConfig();
            var api = new FakeApiClient();
            api.exportResponse.generatedTypes = "// generated";
            api.exportResponse.projectJson = "{ \"project\": true }";
            api.exportResponse.diagnostics.Add(new NeoComposeCodegenDiagnostic
            {
                severity = "error",
                path = "localizedTexts.text-1.localeValues.en-US.value",
                message = "Localized text could not be converted.",
            });
            var assets = new FakeAssetService();
            var confirmations = new FakeConfirmationService(true);
            var synchronizer = new NeoComposeSynchronizer(api, confirmations, assets);

            var result = await synchronizer.SynchronizeAsync(config);

            Assert.IsTrue(result.success, result.message);
            Assert.AreEqual("// generated", assets.files["Assets/Scripts/Neo/NeoGeneratedTypes.cs"]);
            Assert.AreEqual("{ \"project\": true }", assets.files["Assets/Resources/Neo/project.json"]);
            Assert.AreEqual(1, confirmations.calls.Count);
            Assert.IsTrue(confirmations.calls[0].Contains("generated C#"));
        }

        [Test]
        public async Task ProjectSettingsUpdater_SavesUnityExportSettings()
        {
            var config = MakeConfig();
            config.namespaceForGeneratedTypes = "Game.Generated";
            config.singleton = false;
            var api = new FakeApiClient();
            api.editResponse.project.id = config.projectId;
            api.editResponse.project.name = config.projectName;
            api.editResponse.project.exportSettings = new NeoComposeProjectExportSettings
            {
                unity = new NeoComposeUnityExportSettings
                {
                    namespaceForGeneratedTypes = "Game.Generated",
                    singleton = false,
                },
            };
            var assets = new FakeAssetService();
            var updater = new NeoComposeProjectSettingsUpdater(api, assets);

            var result = await updater.UpdateUnityExportSettingsAsync(config);

            Assert.IsTrue(result.success, result.message);
            Assert.AreEqual("http://localhost:3000", api.lastEditApiBaseUrl);
            Assert.AreEqual("project-1", api.lastEditProjectId);
            Assert.AreEqual("version-1", api.lastEditVersionId);
            Assert.AreEqual("Game.Generated", api.lastEditNamespace);
            Assert.AreEqual(false, api.lastEditSingleton);
            Assert.AreEqual("Game.Generated", config.namespaceForGeneratedTypes);
            Assert.IsFalse(config.singleton);
            Assert.IsTrue(assets.savedConfig);
        }

        private static NeoComposeConfig MakeConfig()
        {
            var config = ScriptableObject.CreateInstance<NeoComposeConfig>();
            config.apiBaseUrl = "http://localhost:3000";
            config.SelectProject("project-1", "Project One");
            config.targetReleaseChannelId = "development";
            config.versionId = "version-1";
            return config;
        }

        private static NeoComposeProjectVersion Version(
            string id,
            string statusId,
            int major,
            int minor,
            int patch)
        {
            return new NeoComposeProjectVersion
            {
                id = id,
                statusId = statusId,
                semver = new NeoComposeProjectVersionSemver
                {
                    major = major,
                    minor = minor,
                    patch = patch,
                    label = $"{major}.{minor}.{patch}",
                },
            };
        }

        private static string ProjectJsonWithFiles(string filesJson)
        {
            return @"
{
  ""project"": {
    ""_id"": ""project-1"",
    ""id"": ""project-1"",
    ""name"": ""Project One"",
    ""rootAssetsAttributeId"": ""assets-root"",
    ""rootSaveFileAttributeId"": ""save-root"",
    ""createdAt"": ""1970-01-01T00:00:00.000Z"",
    ""updatedAt"": ""1970-01-01T00:00:00.000Z""
  },
  ""attributes"": {},
  ""values"": {},
  ""types"": {},
  ""enums"": {},
  ""files"": {
" + filesJson + @"
  },
  ""textureTemplates"": {},
  ""audioClipTemplates"": {}
}";
        }

        private static string ProjectJsonWithLocalization(string mainLocale)
        {
            return @"{
  ""project"": {
    ""exportSettings"": {
      ""unity"": {
        ""namespaceForGeneratedTypes"": ""Assets.Scripts.Neo"",
        ""singleton"": true
      }
    }
  },
  ""localization"": {
    ""mainLocale"": """ + mainLocale + @"""
  }
}";
        }

        private static UnityTexture2DImportSettingsTemplate MakeSpriteTemplate()
        {
            return new UnityTexture2DImportSettingsTemplate
            {
                id = "texture-template-1",
                projectId = "project-1",
                name = "Sprites",
                type = "texture-2d",
                textureType = "sprite",
                textureShape = "2d",
                sRGBTexture = true,
                alphaSource = "input-texture-alpha",
                alphaIsTransparency = true,
                nonPowerOfTwoScale = "none",
                ignorePngGamma = false,
                readWriteEnabled = true,
                virtualTextureOnly = false,
                generateMipMaps = false,
                borderMipMaps = false,
                mipMapFiltering = "box",
                mipMapsPreserveCoverage = false,
                alphaCutoffValue = 0.5,
                fadeOutMipMaps = false,
                mipMapFadeDistanceStart = 1,
                mipMapFadeDistanceEnd = 3,
                anisoLevel = 1,
                wrapMode = "clamp",
                filterMode = "point",
                textureCompression = "none",
                compressionQuality = 50,
                crunchedCompression = false,
                createdAt = "1970-01-01T00:00:00.000Z",
                updatedAt = "1970-01-02T00:00:00.000Z",
                spriteSettings = new UnitySpriteTextureSettingsTemplate
                {
                    spriteMode = "multiple",
                    pixelsPerUnit = 16,
                    meshType = "tight",
                    extrudeEdges = 1,
                    pivotAlignment = "center",
                    pivot = new UnityVector2 { x = 0.5, y = 0.5 },
                    generatePhysicsShape = true,
                    spriteEditor = new UnitySpriteEditorSettingsTemplate
                    {
                        slice = new UnitySpriteGridByCellSizeSliceTemplate
                        {
                            type = "grid-by-cell-size",
                            pixelSize = new UnityVector2 { x = 16, y = 16 },
                            offset = new UnityVector2 { x = 0, y = 0 },
                            padding = new UnityVector2 { x = 0, y = 0 },
                            keepEmptyRects = false,
                            pivotAlignment = "center",
                            pivot = new UnityVector2 { x = 0.5, y = 0.5 },
                            border = new UnityVector4 { x = 0, y = 0, z = 0, w = 0 },
                            naming = new UnitySpriteGridNamingConvention
                            {
                                pattern = "{fileName}_{row}_{column}",
                                startIndex = 0,
                                order = "row-major",
                            },
                        },
                    },
                },
            };
        }

        private static void CleanupTempRoot()
        {
            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }
        }

        private sealed class FakeApiClient : INeoComposeEditorApiClient
        {
            public readonly NeoComposeUnityExportResponse exportResponse = new()
            {
                projectId = "project-1",
                projectName = "Project One",
                projectJson = "{}",
                generatedTypes = "",
            };
            public readonly NeoComposeProjectEditResponse editResponse = new();
            public string? lastEditApiBaseUrl;
            public string? lastEditProjectId;
            public string? lastEditVersionId;
            public string? lastEditNamespace;
            public bool? lastEditSingleton;
            public string? lastExportVersionId;
            public NeoComposeUnityExportFileDownloadResponse fileDownloadResponse = new();
            public readonly Dictionary<string, byte[]> downloads = new();
            public string[] lastFileDownloadIds = System.Array.Empty<string>();
            public string? lastFileDownloadVersionId;

            public Task<NeoComposeProjectListResponse> ListProjectsAsync(string apiBaseUrl, string? query)
            {
                return Task.FromResult(new NeoComposeProjectListResponse());
            }

            public Task<NeoComposeProjectReleaseChannelListResponse> ListReleaseChannelsAsync(string apiBaseUrl, string projectId)
            {
                return Task.FromResult(new NeoComposeProjectReleaseChannelListResponse());
            }

            public Task<NeoComposeProjectVersionListResponse> ListVersionsAsync(string apiBaseUrl, string projectId)
            {
                return Task.FromResult(new NeoComposeProjectVersionListResponse());
            }

            public Task<NeoComposeProjectVersionStatusListResponse> ListVersionStatusesAsync(string apiBaseUrl, string projectId)
            {
                return Task.FromResult(new NeoComposeProjectVersionStatusListResponse());
            }

            public Task<NeoComposeProjectVersionMetadataResponse> GetVersionMetadataAsync(
                string apiBaseUrl,
                string projectId,
                string versionId)
            {
                return Task.FromResult(new NeoComposeProjectVersionMetadataResponse());
            }

            public Task<NeoComposeProjectEditResponse> UpdateProjectExportSettingsAsync(
                string apiBaseUrl,
                string projectId,
                string versionId,
                string namespaceForGeneratedTypes,
                bool singleton)
            {
                lastEditApiBaseUrl = apiBaseUrl;
                lastEditProjectId = projectId;
                lastEditVersionId = versionId;
                lastEditNamespace = namespaceForGeneratedTypes;
                lastEditSingleton = singleton;
                return Task.FromResult(editResponse);
            }

            public Task<NeoComposeUnityExportResponse> ExportProjectAsync(string apiBaseUrl, string projectId, string versionId)
            {
                lastExportVersionId = versionId;
                return Task.FromResult(exportResponse);
            }

            public Task<NeoComposeUnityExportFileDownloadResponse> ExportProjectFileDownloadsAsync(
                string apiBaseUrl,
                string projectId,
                string versionId,
                string[] fileIds)
            {
                lastFileDownloadVersionId = versionId;
                lastFileDownloadIds = fileIds;
                return Task.FromResult(fileDownloadResponse);
            }

            public Task<byte[]> DownloadFileAsync(string downloadUrl)
            {
                return Task.FromResult(downloads[downloadUrl]);
            }
        }

        private sealed class FakeConfirmationService : INeoComposeConfirmationService
        {
            private readonly Queue<bool> responses = new();
            public readonly List<string> calls = new();

            public FakeConfirmationService(params bool[] responses)
            {
                foreach (var response in responses)
                {
                    this.responses.Enqueue(response);
                }
            }

            public bool Confirm(string title, string message, string ok, string cancel)
            {
                calls.Add(title);
                if (responses.Count == 0) return true;
                return responses.Dequeue();
            }
        }

        private sealed class FakeAssetService : INeoComposeEditorAssetService
        {
            public readonly Dictionary<string, string> files = new();
            public readonly Dictionary<string, byte[]> binaryFiles = new();
            public readonly Dictionary<string, Sprite[]> loadedSprites = new();
            public readonly Dictionary<string, AudioClip> loadedAudioClips = new();
            public readonly List<string> createdDirectories = new();
            public readonly List<string> deletedAssets = new();
            public readonly List<string> appliedImportSettings = new();
            public readonly HashSet<string> throwOnWriteText = new();
            public NeoAssetDatabase assetDatabase = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            public bool savedConfig;
            public bool savedAsset;
            public string? postSynchronizeProjectJsonPath;

            public bool FileExists(string assetPath)
            {
                return files.ContainsKey(assetPath) || binaryFiles.ContainsKey(assetPath);
            }

            public string[] FindFiles(string assetDirectory, string searchPattern)
            {
                return files.Keys
                    .Concat(binaryFiles.Keys)
                    .Where(path => path.StartsWith(assetDirectory.TrimEnd('/') + "/"))
                    .Where(path => searchPattern != "*.json" || path.EndsWith(".json"))
                    .ToArray();
            }

            public void EnsureDirectory(string assetDirectory)
            {
                createdDirectories.Add(assetDirectory);
            }

            public void WriteAllText(string assetPath, string content)
            {
                if (throwOnWriteText.Contains(assetPath))
                {
                    throw new IOException("Injected write failure.");
                }
                files[assetPath] = content;
            }

            public void WriteAllBytes(string assetPath, byte[] content)
            {
                binaryFiles[assetPath] = content;
            }

            public void RefreshAsset(string assetPath)
            {
            }

            public void SaveConfig(NeoComposeConfig config)
            {
                savedConfig = true;
            }

            public void SchedulePostSynchronize(NeoComposeConfig config, string projectJsonPath)
            {
                postSynchronizeProjectJsonPath = projectJsonPath;
            }

            public NeoAssetDatabase LoadOrCreateAssetDatabase(string assetPath)
            {
                return assetDatabase;
            }

            public void ApplyUnityImportSettings(string assetPath, ProjectFile file, ProjectData projectData)
            {
                appliedImportSettings.Add(assetPath);
            }

            public Sprite[] LoadSprites(string assetPath)
            {
                return loadedSprites.TryGetValue(assetPath, out var sprites)
                    ? sprites
                    : System.Array.Empty<Sprite>();
            }

            public AudioClip? LoadAudioClip(string assetPath)
            {
                return loadedAudioClips.TryGetValue(assetPath, out var audioClip)
                    ? audioClip
                    : null;
            }

            public void SaveAsset(Object asset)
            {
                savedAsset = true;
            }

            public void DeleteAsset(string assetPath)
            {
                deletedAssets.Add(assetPath);
                files.Remove(assetPath);
                binaryFiles.Remove(assetPath);
            }
        }
    }
}
