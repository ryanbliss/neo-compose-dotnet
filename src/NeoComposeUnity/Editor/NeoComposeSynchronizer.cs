// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    public interface INeoComposeConfirmationService
    {
        bool Confirm(string title, string message, string ok, string cancel);
    }

    public interface INeoComposeEditorAssetService
    {
        bool FileExists(string assetPath);
        void EnsureDirectory(string assetDirectory);
        void WriteAllText(string assetPath, string content);
        void WriteAllBytes(string assetPath, byte[] content);
        void RefreshAsset(string assetPath);
        void SaveConfig(NeoComposeConfig config);
        void SchedulePostSynchronize(NeoComposeConfig config, string projectJsonPath);
        NeoAssetDatabase LoadOrCreateAssetDatabase(string assetPath);
        void ApplyUnityImportSettings(string assetPath, ProjectFile file, ProjectData projectData);
        Sprite[] LoadSprites(string assetPath);
        AudioClip? LoadAudioClip(string assetPath);
        void SaveAsset(UnityEngine.Object asset);
        void DeleteAsset(string assetPath);
    }

    public sealed class NeoComposeSyncResult
    {
        public bool success;
        public string message = "";
        public NeoComposeUnityExportResponse? exportResponse;

        public static NeoComposeSyncResult Success(string message, NeoComposeUnityExportResponse exportResponse)
        {
            return new NeoComposeSyncResult
            {
                success = true,
                message = message,
                exportResponse = exportResponse,
            };
        }

        public static NeoComposeSyncResult Failure(string message)
        {
            return new NeoComposeSyncResult
            {
                success = false,
                message = message,
            };
        }
    }

    public sealed class NeoComposeSynchronizer
    {
        private readonly INeoComposeEditorApiClient apiClient;
        private readonly INeoComposeConfirmationService confirmations;
        private readonly INeoComposeEditorAssetService assets;

        public NeoComposeSynchronizer(
            INeoComposeEditorApiClient apiClient,
            INeoComposeConfirmationService confirmations,
            INeoComposeEditorAssetService assets)
        {
            this.apiClient = apiClient;
            this.confirmations = confirmations;
            this.assets = assets;
        }

        public async Task<NeoComposeSyncResult> SynchronizeAsync(
            NeoComposeConfig config,
            Action<string>? onProgress = null)
        {
            var validation = ValidateConfig(config);
            if (!validation.success) return validation;

            try
            {
                onProgress?.Invoke("Exporting project...");
                var exportResponse = await apiClient.ExportProjectAsync(
                    config.apiBaseUrl,
                    config.projectId,
                    config.versionId);
                var diagnosticErrors = exportResponse.diagnostics
                    .Where(d => string.Equals(d.severity, "error", StringComparison.OrdinalIgnoreCase))
                    .ToArray();

                if (diagnosticErrors.Length > 0)
                {
                    LogDiagnostics(exportResponse.diagnostics);
                    if (!confirmations.Confirm(
                            "Neo Compose generated C# has errors",
                            FormatDiagnosticsForDialog(diagnosticErrors) +
                            "\n\nContinue and write the files anyway?",
                            "Continue",
                            "Cancel"))
                    {
                        return NeoComposeSyncResult.Failure("Synchronization cancelled because generated C# had errors.");
                    }
                }

                var generatedTypesPath = NeoComposePathUtility.CombineAssetPath(
                    config.generatedTypesDirectory,
                    NeoComposeEditorDefaults.GeneratedTypesFileName);
                var projectJsonPath = NeoComposePathUtility.CombineAssetPath(
                    config.projectJsonDirectory,
                    NeoComposeEditorDefaults.ProjectJsonFileName);

                if ((assets.FileExists(generatedTypesPath) || assets.FileExists(projectJsonPath)) &&
                    !confirmations.Confirm(
                        "Replace Neo Compose files?",
                        "Existing synchronized files will be replaced:\n\n" +
                        generatedTypesPath +
                        "\n" +
                        projectJsonPath,
                        "Replace",
                        "Cancel"))
                {
                    return NeoComposeSyncResult.Failure("Synchronization cancelled before replacing existing files.");
                }

                assets.EnsureDirectory(config.generatedTypesDirectory);
                assets.EnsureDirectory(config.projectJsonDirectory);
                var assetSyncErrors = await SynchronizeFilesAsync(config, exportResponse.projectJson, onProgress);

                onProgress?.Invoke("Writing generated files...");
                assets.WriteAllText(generatedTypesPath, exportResponse.generatedTypes);
                assets.WriteAllText(projectJsonPath, exportResponse.projectJson);
                if (exportResponse.version != null && !string.IsNullOrWhiteSpace(exportResponse.version.id))
                {
                    config.versionId = exportResponse.version.id;
                }

                config.namespaceForGeneratedTypes = ReadUnityNamespaceOrDefault(exportResponse.projectJson);
                config.singleton = ReadUnitySingletonOrDefault(exportResponse.projectJson);
                assets.SaveConfig(config);
                assets.SchedulePostSynchronize(config, projectJsonPath);

                if (assetSyncErrors.Length > 0)
                {
                    return NeoComposeSyncResult.Failure(
                        "Neo Compose files synchronized, but some assets failed:\n" +
                        string.Join("\n", assetSyncErrors));
                }

                return NeoComposeSyncResult.Success("Neo Compose files synchronized.", exportResponse);
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                return NeoComposeSyncResult.Failure(exception.Message);
            }
        }

        private async Task<string[]> SynchronizeFilesAsync(
            NeoComposeConfig config,
            string projectJson,
            Action<string>? onProgress)
        {
            try
            {
                var root = JObject.Parse(projectJson);
                if (root["files"] is not JObject)
                {
                    return Array.Empty<string>();
                }
            }
            catch
            {
                return Array.Empty<string>();
            }

            ProjectData? projectData;
            try
            {
                projectData = JsonConvert.DeserializeObject<ProjectData>(projectJson);
            }
            catch (Exception exception)
            {
                return new[] { "Could not read file metadata from project.json: " + exception.Message };
            }

            if (projectData?.files == null)
            {
                return Array.Empty<string>();
            }

            onProgress?.Invoke("Checking synchronized assets...");
            return await NeoComposeFileSynchronizer.SynchronizeAsync(
                apiClient,
                confirmations,
                assets,
                config,
                projectData,
                onProgress);
        }

        public static NeoComposeSyncResult ValidateConfig(NeoComposeConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.apiBaseUrl))
            {
                return NeoComposeSyncResult.Failure("API base URL cannot be empty.");
            }

            if (!Uri.TryCreate(config.apiBaseUrl, UriKind.Absolute, out var uri))
            {
                return NeoComposeSyncResult.Failure("API base URL must be an absolute URL.");
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                return NeoComposeSyncResult.Failure("API base URL must use http or https.");
            }

            if (string.IsNullOrWhiteSpace(config.projectId))
            {
                return NeoComposeSyncResult.Failure("No Neo Compose project is selected.");
            }

            if (string.IsNullOrWhiteSpace(config.targetReleaseChannelId))
            {
                return NeoComposeSyncResult.Failure("No Neo Compose release channel is selected.");
            }

            if (string.IsNullOrWhiteSpace(config.versionId))
            {
                return NeoComposeSyncResult.Failure("No Neo Compose project version is selected.");
            }

            if (!NeoComposePathUtility.TryNormalizeAssetDirectory(
                    config.generatedTypesDirectory,
                    out var generatedTypesDirectory,
                    out var generatedTypesError))
            {
                return NeoComposeSyncResult.Failure(generatedTypesError);
            }

            if (!NeoComposePathUtility.TryNormalizeAssetDirectory(
                    config.projectJsonDirectory,
                    out var projectJsonDirectory,
                    out var projectJsonError))
            {
                return NeoComposeSyncResult.Failure(projectJsonError);
            }

            if (!NeoComposePathUtility.TryNormalizeAssetDirectory(
                    config.spriteDirectory,
                    out var spriteDirectory,
                    out var spriteError))
            {
                return NeoComposeSyncResult.Failure(spriteError);
            }

            if (!NeoComposePathUtility.TryNormalizeResourcesDirectory(
                    config.localizationResourcesDirectory,
                    out var localizationResourcesDirectory,
                    out var localizationResourcesError))
            {
                return NeoComposeSyncResult.Failure(localizationResourcesError);
            }

            if (!NeoComposePathUtility.TryNormalizeStreamingAssetsDirectory(
                    config.localizationStreamingAssetsDirectory,
                    out var localizationStreamingAssetsDirectory,
                    out var localizationStreamingAssetsError))
            {
                return NeoComposeSyncResult.Failure(localizationStreamingAssetsError);
            }

            if (!NeoComposePathUtility.TryNormalizeAssetDirectory(
                    config.audioClipDirectory,
                    out var audioClipDirectory,
                    out var audioClipError))
            {
                return NeoComposeSyncResult.Failure(audioClipError);
            }

            config.generatedTypesDirectory = generatedTypesDirectory;
            config.projectJsonDirectory = projectJsonDirectory;
            config.spriteDirectory = spriteDirectory;
            config.localizationResourcesDirectory = localizationResourcesDirectory;
            config.localizationStreamingAssetsDirectory = localizationStreamingAssetsDirectory;
            config.audioClipDirectory = audioClipDirectory;
            return NeoComposeSyncResult.Success("Config is valid.", new NeoComposeUnityExportResponse());
        }

        private static void LogDiagnostics(System.Collections.Generic.IEnumerable<NeoComposeCodegenDiagnostic> diagnostics)
        {
            foreach (var diagnostic in diagnostics)
            {
                Debug.LogWarning(FormatDiagnostic(diagnostic));
            }
        }

        private static string FormatDiagnosticsForDialog(NeoComposeCodegenDiagnostic[] diagnostics)
        {
            var shown = diagnostics.Take(8).Select(FormatDiagnostic);
            var text = string.Join("\n", shown);
            if (diagnostics.Length > 8)
            {
                text += $"\n...and {diagnostics.Length - 8} more. See the Unity Console for the full list.";
            }

            return text;
        }

        private static string FormatDiagnostic(NeoComposeCodegenDiagnostic diagnostic)
        {
            var path = string.IsNullOrWhiteSpace(diagnostic.path) ? "" : $"{diagnostic.path}: ";
            var severity = diagnostic.severity?.ToUpperInvariant() ?? "UNKNOWN";
            return $"{severity}: {path}{diagnostic.message}";
        }

        private static string ReadUnityNamespaceOrDefault(string projectJson)
        {
            try
            {
                var root = JObject.Parse(projectJson);
                var namespaceForGeneratedTypes = root["project"]?["exportSettings"]?["unity"]?["namespaceForGeneratedTypes"]?.Value<string>();
                if (string.IsNullOrWhiteSpace(namespaceForGeneratedTypes))
                {
                    return NeoComposeDefaults.NamespaceForGeneratedTypes;
                }

                return namespaceForGeneratedTypes!;
            }
            catch
            {
                return NeoComposeDefaults.NamespaceForGeneratedTypes;
            }
        }

        private static bool ReadUnitySingletonOrDefault(string projectJson)
        {
            try
            {
                var root = JObject.Parse(projectJson);
                return root["project"]?["exportSettings"]?["unity"]?["singleton"]?.Value<bool?>()
                    ?? NeoComposeDefaults.Singleton;
            }
            catch
            {
                return NeoComposeDefaults.Singleton;
            }
        }
    }

    public sealed class NeoComposeEditorDialogConfirmationService : INeoComposeConfirmationService
    {
        public bool Confirm(string title, string message, string ok, string cancel)
        {
            return EditorUtility.DisplayDialog(title, message, ok, cancel);
        }
    }

    public sealed class NeoComposeEditorAssetService : INeoComposeEditorAssetService
    {
        public bool FileExists(string assetPath)
        {
            return File.Exists(assetPath);
        }

        public void EnsureDirectory(string assetDirectory)
        {
            Directory.CreateDirectory(assetDirectory);
        }

        public void WriteAllText(string assetPath, string content)
        {
            File.WriteAllText(assetPath, content, new UTF8Encoding(false));
        }

        public void WriteAllBytes(string assetPath, byte[] content)
        {
            File.WriteAllBytes(assetPath, content);
        }

        public void RefreshAsset(string assetPath)
        {
            AssetDatabase.ImportAsset(assetPath);
        }

        public void SaveConfig(NeoComposeConfig config)
        {
            NeoComposeConfigProvider.Save(config);
        }

        public void SchedulePostSynchronize(NeoComposeConfig config, string projectJsonPath)
        {
            NeoComposePostSynchronizeProcessor.Schedule(config, projectJsonPath);
        }

        public NeoAssetDatabase LoadOrCreateAssetDatabase(string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<NeoAssetDatabase>(assetPath);
            if (existing != null) return existing;

            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            var directory = Path.GetDirectoryName(assetPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            AssetDatabase.CreateAsset(database, assetPath);
            AssetDatabase.SaveAssets();
            return database;
        }

        public void ApplyUnityImportSettings(string assetPath, ProjectFile file, ProjectData projectData)
        {
            NeoComposeUnityImportSettingsApplier.Apply(assetPath, file, projectData);
        }

        public Sprite[] LoadSprites(string assetPath)
        {
            var sprites = AssetDatabase
                .LoadAllAssetRepresentationsAtPath(assetPath)
                .OfType<Sprite>()
                .ToArray();
            if (sprites.Length > 0) return sprites;

            var mainSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            return mainSprite == null ? Array.Empty<Sprite>() : new[] { mainSprite };
        }

        public AudioClip? LoadAudioClip(string assetPath)
        {
            return AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
        }

        public void SaveAsset(UnityEngine.Object asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        public void DeleteAsset(string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
            }
            else if (File.Exists(assetPath))
            {
                File.Delete(assetPath);
            }
        }
    }
}
