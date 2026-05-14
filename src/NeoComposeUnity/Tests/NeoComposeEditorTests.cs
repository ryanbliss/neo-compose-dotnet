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
        public void ConfigProvider_CreatesDefaultConfigInResourcesFolder()
        {
            var path = $"{TempRoot}/Resources/Neo/NeoComposeConfig.asset";

            var config = NeoComposeConfigProvider.LoadOrCreate(path, new[] { TempRoot });

            Assert.IsNotNull(config);
            Assert.AreEqual(path, AssetDatabase.GetAssetPath(config));
            Assert.AreEqual(NeoComposeDefaults.ApiBaseUrl, config.apiBaseUrl);
            Assert.AreEqual(NeoComposeDefaults.GeneratedTypesDirectory, config.generatedTypesDirectory);
            Assert.AreEqual(NeoComposeDefaults.ProjectJsonDirectory, config.projectJsonDirectory);
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
            config.generatedTypesDirectory = "Assets/CustomTypes";
            config.projectJsonDirectory = "Assets/CustomJson";
            config.spriteDirectory = "Assets/CustomSprites";
            config.audioClipDirectory = "Assets/CustomAudio";
            config.namespaceForGeneratedTypes = "Game.Generated";
            config.singleton = false;

            config.ClearProject();

            Assert.AreEqual("", config.projectId);
            Assert.AreEqual("", config.projectName);
            Assert.AreEqual("Assets/CustomTypes", config.generatedTypesDirectory);
            Assert.AreEqual("Assets/CustomJson", config.projectJsonDirectory);
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
            var synchronizer = new NeoComposeSynchronizer(api, new FakeConfirmationService(true), assets);
            var progress = new List<string>();

            var result = await synchronizer.SynchronizeAsync(config, progress.Add);

            Assert.IsTrue(result.success, result.message);
            Assert.Contains("Requesting download URLs for 1 file asset(s)...", progress);
            Assert.Contains("Downloading file 1/1: hero.png", progress);
            Assert.Contains("Applying import settings 1/1: hero.png", progress);
            CollectionAssert.AreEqual(new[] { "file-1" }, api.lastFileDownloadIds);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, assets.binaryFiles["Assets/Resources/Neo/Files/Sprites/hero.png"]);
            Assert.Contains("Assets/Resources/Neo/Files/Sprites/hero.png", assets.appliedImportSettings);
            var entry = assets.assetDatabase.TryGetEntry("file-1");
            Assert.IsNotNull(entry);
            Assert.AreEqual("Assets/Resources/Neo/Files/Sprites/hero.png", entry!.AssetPath);
            Assert.AreEqual("1970-01-02T00:00:00.000Z", entry.FileUpdatedAt);
            Assert.AreEqual("texture-template-1", entry.TemplateId);
            Assert.AreEqual("1970-01-03T00:00:00.000Z", entry.TemplateUpdatedAt);
            Assert.AreEqual("2026-05-13.2", entry.ImportSettingsVersion);
            Assert.IsTrue(assets.savedAsset);
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
            assets.assetDatabase.SetFile(
                "file-1",
                "Assets/Resources/Neo/Files/Sprites/hero.png",
                "1970-01-02T00:00:00.000Z",
                "1970-01-04T00:00:00.000Z",
                null,
                null,
                null,
                "2026-05-13.2");
            var synchronizer = new NeoComposeSynchronizer(api, new FakeConfirmationService(true), assets);
            var progress = new List<string>();

            var result = await synchronizer.SynchronizeAsync(config, progress.Add);

            Assert.IsTrue(result.success, result.message);
            Assert.Contains("File assets are current.", progress);
            Assert.AreEqual(0, api.lastFileDownloadIds.Length);
            Assert.AreEqual(0, assets.binaryFiles.Count);
            Assert.IsTrue(assets.savedAsset);
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
            return config;
        }

        private static UnityTexture2DImportSettingsTemplate MakeSpriteTemplate()
        {
            return new UnityTexture2DImportSettingsTemplate
            {
                id = "texture-template-1",
                _id = "texture-template-1",
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
            public string? lastEditNamespace;
            public bool? lastEditSingleton;
            public NeoComposeUnityExportFileDownloadResponse fileDownloadResponse = new();
            public readonly Dictionary<string, byte[]> downloads = new();
            public string[] lastFileDownloadIds = System.Array.Empty<string>();

            public Task<NeoComposeProjectListResponse> ListProjectsAsync(string apiBaseUrl, string? query)
            {
                return Task.FromResult(new NeoComposeProjectListResponse());
            }

            public Task<NeoComposeProjectEditResponse> UpdateProjectExportSettingsAsync(
                string apiBaseUrl,
                string projectId,
                string namespaceForGeneratedTypes,
                bool singleton)
            {
                lastEditApiBaseUrl = apiBaseUrl;
                lastEditProjectId = projectId;
                lastEditNamespace = namespaceForGeneratedTypes;
                lastEditSingleton = singleton;
                return Task.FromResult(editResponse);
            }

            public Task<NeoComposeUnityExportResponse> ExportProjectAsync(string apiBaseUrl, string projectId)
            {
                return Task.FromResult(exportResponse);
            }

            public Task<NeoComposeUnityExportFileDownloadResponse> ExportProjectFileDownloadsAsync(
                string apiBaseUrl,
                string projectId,
                string[] fileIds)
            {
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
            public readonly List<string> createdDirectories = new();
            public readonly List<string> deletedAssets = new();
            public readonly List<string> appliedImportSettings = new();
            public NeoAssetDatabase assetDatabase = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            public bool savedConfig;
            public bool savedAsset;

            public bool FileExists(string assetPath)
            {
                return files.ContainsKey(assetPath) || binaryFiles.ContainsKey(assetPath);
            }

            public void EnsureDirectory(string assetDirectory)
            {
                createdDirectories.Add(assetDirectory);
            }

            public void WriteAllText(string assetPath, string content)
            {
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

            public NeoAssetDatabase LoadOrCreateAssetDatabase(string assetPath)
            {
                return assetDatabase;
            }

            public void ApplyUnityImportSettings(string assetPath, ProjectFile file, ProjectData projectData)
            {
                appliedImportSettings.Add(assetPath);
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
