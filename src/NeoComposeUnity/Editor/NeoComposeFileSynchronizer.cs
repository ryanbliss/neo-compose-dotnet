// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Unity.Editor
{
    internal static class NeoComposeFileSynchronizer
    {
        private const string ImportSettingsVersion = "2026-05-13.2";

        public static async Task<string[]> SynchronizeAsync(
            INeoComposeEditorApiClient apiClient,
            INeoComposeConfirmationService confirmations,
            INeoComposeEditorAssetService assets,
            NeoComposeConfig config,
            ProjectData projectData,
            Action<string>? onProgress = null)
        {
            var errors = new List<string>();
            onProgress?.Invoke("Reading synchronized asset database...");
            var assetDatabasePath = NeoComposePathUtility.CombineAssetPath(
                config.projectJsonDirectory,
                NeoComposeEditorDefaults.AssetDatabaseFileName);
            var assetDatabase = assets.LoadOrCreateAssetDatabase(assetDatabasePath);
            var files = projectData.files.Values
                .Where(file => string.Equals(file.status, "uploaded", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var knownFileIds = new HashSet<string>(files.Select(file => file.id));
            var missingFiles = assetDatabase.FindMissingFiles(knownFileIds);
            if (missingFiles.Length > 0 &&
                confirmations.Confirm(
                    "Delete stale Neo Compose assets?",
                    $"{missingFiles.Length} synchronized asset file(s) are no longer present in the project export.",
                    "Delete",
                    "Keep"))
            {
                foreach (var missingFile in missingFiles)
                {
                    assets.DeleteAsset(missingFile.AssetPath);
                    assetDatabase.RemoveFile(missingFile.FileId);
                }
            }

            onProgress?.Invoke($"Checking {files.Length} file asset(s)...");
            var changedFiles = files
                .Where(file => NeedsSync(config, assets, assetDatabase, projectData, file))
                .ToArray();
            if (changedFiles.Length == 0)
            {
                onProgress?.Invoke("File assets are current.");
                assets.SaveAsset(assetDatabase);
                return errors.ToArray();
            }

            var replacePaths = changedFiles
                .Select(file => BuildAssetPath(config, file))
                .Where(assets.FileExists)
                .ToArray();
            if (replacePaths.Length > 0 &&
                !confirmations.Confirm(
                    "Replace Neo Compose assets?",
                    $"{replacePaths.Length} synchronized asset file(s) will be replaced.",
                    "Replace",
                    "Skip"))
            {
                assets.SaveAsset(assetDatabase);
                return errors.ToArray();
            }

            NeoComposeUnityExportFileDownloadResponse downloads;
            try
            {
                onProgress?.Invoke($"Requesting download URLs for {changedFiles.Length} file asset(s)...");
                downloads = await apiClient.ExportProjectFileDownloadsAsync(
                    config.apiBaseUrl,
                    config.projectId,
                    config.versionId,
                    changedFiles.Select(file => file.id).ToArray());
            }
            catch (Exception exception)
            {
                return new[] { "Could not request file download URLs: " + exception.Message };
            }

            for (var index = 0; index < changedFiles.Length; index++)
            {
                var file = changedFiles[index];
                try
                {
                    if (!downloads.files.TryGetValue(file.id, out var download) ||
                        string.IsNullOrWhiteSpace(download.downloadUrl))
                    {
                        errors.Add($"{file.name}: no download URL was returned.");
                        continue;
                    }

                    var assetPath = BuildAssetPath(config, file);
                    var existing = assetDatabase.TryGetEntry(file.id);
                    if (existing != null &&
                        !string.IsNullOrWhiteSpace(existing.AssetPath) &&
                        existing.AssetPath != assetPath)
                    {
                        assets.DeleteAsset(existing.AssetPath);
                    }

                    assets.EnsureDirectory(GetAssetDirectory(assetPath));
                    onProgress?.Invoke($"Downloading file {index + 1}/{changedFiles.Length}: {file.name}");
                    var bytes = await apiClient.DownloadFileAsync(download.downloadUrl);
                    onProgress?.Invoke($"Writing file {index + 1}/{changedFiles.Length}: {file.name}");
                    assets.WriteAllBytes(assetPath, bytes);
                    onProgress?.Invoke($"Applying import settings {index + 1}/{changedFiles.Length}: {file.name}");
                    assets.ApplyUnityImportSettings(assetPath, file, projectData);
                    var sprites = string.Equals(file.fileType, "image", StringComparison.OrdinalIgnoreCase)
                        ? assets.LoadSprites(assetPath)
                        : Array.Empty<UnityEngine.Sprite>();
                    var audioClip = string.Equals(file.fileType, "audio", StringComparison.OrdinalIgnoreCase)
                        ? assets.LoadAudioClip(assetPath)
                        : null;

                    var template = ResolveTemplate(projectData, file);
                    var now = DateTime.UtcNow.ToString("o");
                    assetDatabase.SetFile(
                        file.id,
                        file.name,
                        assetPath,
                        file.updatedAt,
                        now,
                        template.templateId,
                        template.templateUpdatedAt,
                        template.templateUpdatedAt == null ? null : now,
                        ImportSettingsVersion,
                        sprites,
                        audioClip);
                }
                catch (Exception exception)
                {
                    errors.Add($"{file.name}: {exception.Message}");
                }
            }

            onProgress?.Invoke("Saving synchronized asset database...");
            assets.SaveAsset(assetDatabase);
            return errors.ToArray();
        }

        internal static bool NeedsSync(
            NeoComposeConfig config,
            INeoComposeEditorAssetService assets,
            NeoAssetDatabase assetDatabase,
            ProjectData projectData,
            ProjectFile file)
        {
            var assetPath = BuildAssetPath(config, file);
            var entry = assetDatabase.TryGetEntry(file.id);
            if (entry == null) return true;
            if (entry.AssetPath != assetPath) return true;
            if (!assets.FileExists(assetPath)) return true;
            if (IsAfter(file.updatedAt, entry.LastDownloadedAt)) return true;
            if (entry.ImportSettingsVersion != ImportSettingsVersion) return true;

            var template = ResolveTemplate(projectData, file);
            if (entry.TemplateId != template.templateId) return true;
            if (template.templateUpdatedAt != null &&
                IsAfter(template.templateUpdatedAt, entry.LastAppliedTemplateEditsAt))
            {
                return true;
            }
            return false;
        }

        internal static string BuildAssetPath(NeoComposeConfig config, ProjectFile file)
        {
            var directory = string.Equals(file.fileType, "audio", StringComparison.OrdinalIgnoreCase)
                ? config.audioClipDirectory
                : config.spriteDirectory;
            return NeoComposePathUtility.CombineAssetPath(directory, $"{file.id}-{SanitizeFileName(file.name)}");
        }

        private static bool IsAfter(string? lhs, string? rhs)
        {
            if (string.IsNullOrWhiteSpace(lhs)) return false;
            if (string.IsNullOrWhiteSpace(rhs)) return true;
            if (NeoTimestamp.TryParse(lhs, out var lhsTimestamp) &&
                NeoTimestamp.TryParse(rhs, out var rhsTimestamp))
            {
                return lhsTimestamp.CompareTo(rhsTimestamp) > 0;
            }
            if (DateTime.TryParse(lhs, out var lhsDate) && DateTime.TryParse(rhs, out var rhsDate))
            {
                return lhsDate.ToUniversalTime() > rhsDate.ToUniversalTime();
            }

            return string.CompareOrdinal(lhs, rhs) > 0;
        }

        private static (string? templateId, string? templateUpdatedAt) ResolveTemplate(
            ProjectData projectData,
            ProjectFile file)
        {
            if (string.Equals(file.fileType, "audio", StringComparison.OrdinalIgnoreCase))
            {
                var templateId = file.unityAudioClipSettings?.templateId;
                if (templateId != null &&
                    projectData.audioClipTemplates.TryGetValue(templateId, out var audioTemplate))
                {
                    return (templateId, audioTemplate.updatedAt);
                }

                return (templateId, null);
            }

            var textureTemplateId = file.unityTextureSettings?.templateId;
            if (textureTemplateId != null &&
                projectData.textureTemplates.TryGetValue(textureTemplateId, out var textureTemplate))
            {
                return (textureTemplateId, textureTemplate.updatedAt);
            }

            return (textureTemplateId, null);
        }

        private static string GetAssetDirectory(string assetPath)
        {
            return assetPath.LastIndexOf('/') is var index && index > 0
                ? assetPath.Substring(0, index)
                : "Assets";
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = fileName
                .Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' ? '_' : ch)
                .ToArray();
            var sanitized = new string(chars).Trim();
            return string.IsNullOrWhiteSpace(sanitized) ? "neo-file" : sanitized;
        }
    }
}
