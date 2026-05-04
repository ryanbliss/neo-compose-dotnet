// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NeoCompose.Runtime;
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
        void RefreshAsset(string assetPath);
        void SaveConfig(NeoComposeConfig config);
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

        public async Task<NeoComposeSyncResult> SynchronizeAsync(NeoComposeConfig config)
        {
            var validation = ValidateConfig(config);
            if (!validation.success) return validation;

            try
            {
                var exportResponse = await apiClient.ExportProjectAsync(config.apiBaseUrl, config.projectId);
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
                assets.WriteAllText(generatedTypesPath, exportResponse.generatedTypes);
                assets.WriteAllText(projectJsonPath, exportResponse.projectJson);
                assets.RefreshAsset(generatedTypesPath);
                assets.RefreshAsset(projectJsonPath);
                config.namespaceForGeneratedTypes = ReadUnityNamespaceOrDefault(exportResponse.projectJson);
                config.singleton = ReadUnitySingletonOrDefault(exportResponse.projectJson);
                assets.SaveConfig(config);

                return NeoComposeSyncResult.Success("Neo Compose files synchronized.", exportResponse);
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                return NeoComposeSyncResult.Failure(exception.Message);
            }
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

            config.generatedTypesDirectory = generatedTypesDirectory;
            config.projectJsonDirectory = projectJsonDirectory;
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
            return $"{diagnostic.severity.ToUpperInvariant()}: {path}{diagnostic.message}";
        }

        private static string ReadUnityNamespaceOrDefault(string projectJson)
        {
            try
            {
                var root = JObject.Parse(projectJson);
                var namespaceForGeneratedTypes = root["project"]?["exportSettings"]?["unity"]?["namespaceForGeneratedTypes"]?.Value<string>();
                return string.IsNullOrWhiteSpace(namespaceForGeneratedTypes)
                    ? NeoComposeDefaults.NamespaceForGeneratedTypes
                    : namespaceForGeneratedTypes;
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

        public void RefreshAsset(string assetPath)
        {
            AssetDatabase.ImportAsset(assetPath);
        }

        public void SaveConfig(NeoComposeConfig config)
        {
            NeoComposeConfigProvider.Save(config);
        }
    }
}
