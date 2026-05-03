// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using NeoCompose.Runtime;
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
            Assert.AreEqual(NeoComposeDefaults.NamespaceForGeneratedTypes, config.namespaceForGeneratedTypes);
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
            config.namespaceForGeneratedTypes = "Game.Generated";

            config.ClearProject();

            Assert.AreEqual("", config.projectId);
            Assert.AreEqual("", config.projectName);
            Assert.AreEqual("Assets/CustomTypes", config.generatedTypesDirectory);
            Assert.AreEqual("Assets/CustomJson", config.projectJsonDirectory);
            Assert.AreEqual("Game.Generated", config.namespaceForGeneratedTypes);
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
        public async Task Synchronizer_UpdatesConfigNamespaceFromExportedProjectJson()
        {
            var config = MakeConfig();
            config.namespaceForGeneratedTypes = NeoComposeDefaults.NamespaceForGeneratedTypes;
            var api = new FakeApiClient();
            api.exportResponse.projectJson =
                "{ \"project\": { \"exportSettings\": { \"unity\": { \"namespaceForGeneratedTypes\": \"HelloWorld.Assets.Scripts.Neo\" } } } }";
            var assets = new FakeAssetService();
            var synchronizer = new NeoComposeSynchronizer(api, new FakeConfirmationService(true), assets);

            var result = await synchronizer.SynchronizeAsync(config);

            Assert.IsTrue(result.success, result.message);
            Assert.AreEqual("HelloWorld.Assets.Scripts.Neo", config.namespaceForGeneratedTypes);
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
        public async Task ProjectSettingsUpdater_SavesOnlyUnityNamespaceExportSettings()
        {
            var config = MakeConfig();
            config.namespaceForGeneratedTypes = "Game.Generated";
            var api = new FakeApiClient();
            api.editResponse.project.id = config.projectId;
            api.editResponse.project.name = config.projectName;
            api.editResponse.project.exportSettings = new NeoComposeProjectExportSettings
            {
                unity = new NeoComposeUnityExportSettings
                {
                    namespaceForGeneratedTypes = "Game.Generated",
                },
            };
            var assets = new FakeAssetService();
            var updater = new NeoComposeProjectSettingsUpdater(api, assets);

            var result = await updater.UpdateUnityNamespaceAsync(config);

            Assert.IsTrue(result.success, result.message);
            Assert.AreEqual("http://localhost:3000", api.lastEditApiBaseUrl);
            Assert.AreEqual("project-1", api.lastEditProjectId);
            Assert.AreEqual("Game.Generated", api.lastEditNamespace);
            Assert.AreEqual("Game.Generated", config.namespaceForGeneratedTypes);
            Assert.IsTrue(assets.savedConfig);
        }

        private static NeoComposeConfig MakeConfig()
        {
            var config = ScriptableObject.CreateInstance<NeoComposeConfig>();
            config.apiBaseUrl = "http://localhost:3000";
            config.SelectProject("project-1", "Project One");
            return config;
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

            public Task<NeoComposeProjectListResponse> ListProjectsAsync(string apiBaseUrl, string? query)
            {
                return Task.FromResult(new NeoComposeProjectListResponse());
            }

            public Task<NeoComposeProjectEditResponse> UpdateProjectExportSettingsAsync(
                string apiBaseUrl,
                string projectId,
                string namespaceForGeneratedTypes)
            {
                lastEditApiBaseUrl = apiBaseUrl;
                lastEditProjectId = projectId;
                lastEditNamespace = namespaceForGeneratedTypes;
                return Task.FromResult(editResponse);
            }

            public Task<NeoComposeUnityExportResponse> ExportProjectAsync(string apiBaseUrl, string projectId)
            {
                return Task.FromResult(exportResponse);
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
            public readonly List<string> createdDirectories = new();
            public bool savedConfig;

            public bool FileExists(string assetPath)
            {
                return files.ContainsKey(assetPath);
            }

            public void EnsureDirectory(string assetDirectory)
            {
                createdDirectories.Add(assetDirectory);
            }

            public void WriteAllText(string assetPath, string content)
            {
                files[assetPath] = content;
            }

            public void RefreshAsset(string assetPath)
            {
            }

            public void SaveConfig(NeoComposeConfig config)
            {
                savedConfig = true;
            }
        }
    }
}
