// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;

namespace NeoCompose.Unity.Editor
{
    internal static class NeoComposeFileSynchronizer
    {
        private const string ImportSettingsVersion = "2026-05-13.2";

        /// <summary>
        /// Brings each uploaded project file's asset up to date from the bytes
        /// <paramref name="sidecar"/> names in the workspace. Returns one error
        /// per file that failed; the rest still sync.
        /// </summary>
        internal static string[] Synchronize(
            INeoComposeConfirmationService confirmations,
            INeoComposeEditorAssetService assets,
            NeoComposeConfig config,
            ProjectData projectData,
            NeoComposeExportSidecar sidecar)
        {
            var errors = new List<string>();
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

            foreach (var file in files.Where(file => NeedsSync(config, assets, assetDatabase, projectData, file)))
            {
                try
                {
                    var bytes = ReadExportedBytes(sidecar, file);
                    var assetPath = BuildAssetPath(config, file);
                    var existing = assetDatabase.TryGetEntry(file.id);
                    if (existing != null &&
                        !string.IsNullOrWhiteSpace(existing.AssetPath) &&
                        existing.AssetPath != assetPath)
                    {
                        assets.DeleteAsset(existing.AssetPath);
                    }

                    assets.EnsureDirectory(GetAssetDirectory(assetPath));
                    assets.WriteAllBytes(assetPath, bytes);
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
                        ComputeRecordHash(file),
                        template.templateId,
                        ComputeRecordHash(template.record),
                        ImportSettingsVersion,
                        sprites,
                        audioClip);
                }
                catch (Exception exception)
                {
                    errors.Add($"{file.name}: {exception.Message}");
                }
            }

            assets.SaveAsset(assetDatabase);
            return errors.ToArray();
        }

        /// <summary>
        /// The file's bytes from the workspace, which must still hash to what
        /// the export recorded: <c>neo export</c> doesn't copy them, so a
        /// workspace edit after the export would otherwise slip in unexported.
        /// </summary>
        private static byte[] ReadExportedBytes(NeoComposeExportSidecar sidecar, ProjectFile file)
        {
            if (!sidecar.files.TryGetValue(file.id, out var exported))
            {
                throw new InvalidOperationException(
                    $"the export lists no workspace bytes for file '{file.id}', run `neo export` again.");
            }

            var path = Path.Combine(sidecar.workspaceRoot, exported.path);
            if (!File.Exists(path))
                throw new InvalidOperationException($"{path} was removed since export, run `neo export` again.");
            var bytes = File.ReadAllBytes(path);
            if (!string.Equals(Sha256Hex(bytes), exported.sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{path} changed since export, run `neo export` again.");
            return bytes;
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
            if (entry == null)
                return true;
            if (entry.AssetPath != assetPath)
                return true;
            if (!assets.FileExists(assetPath))
                return true;
            if (entry.ImportSettingsVersion != ImportSettingsVersion)
                return true;
            if (entry.FileRecordHash != ComputeRecordHash(file))
                return true;

            var template = ResolveTemplate(projectData, file);
            // Unity serializes null strings as "" on domain reload, so a
            // null-vs-empty template id difference is not a real change.
            if ((entry.TemplateId ?? "") != (template.templateId ?? ""))
                return true;
            if (entry.TemplateRecordHash != ComputeRecordHash(template.record))
                return true;
            return false;
        }

        /// <summary>
        /// Deterministic content hash of an exported record. The export ships
        /// the file/template record head snapshot's data verbatim, so hashing
        /// the deserialized record identifies the snapshot the bytes were
        /// copied against: an unchanged record hashes identically across
        /// synchronizations, while any record change (replacement upload,
        /// import-settings edit, rename) also bumps `updatedAt` and therefore
        /// the hash. Null records (e.g. "file uses no template") hash to "".
        /// </summary>
        internal static string ComputeRecordHash(object? record)
        {
            if (record == null)
                return "";
            // JsonSerializer.Create ignores JsonConvert.DefaultSettings so a
            // host project's global serializer configuration cannot change
            // how records hash between synchronizations.
            var serializer = JsonSerializer.Create(new JsonSerializerSettings());
            var buffer = new StringBuilder(256);
            using (var writer = new StringWriter(buffer))
            {
                serializer.Serialize(writer, record);
            }

            return Sha256Hex(Encoding.UTF8.GetBytes(buffer.ToString()));
        }

        private static string Sha256Hex(byte[] content)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(content);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                builder.Append(b.ToString("x2"));
            }

            return builder.ToString();
        }

        internal static string BuildAssetPath(NeoComposeConfig config, ProjectFile file)
        {
            var directory = string.Equals(file.fileType, "audio", StringComparison.OrdinalIgnoreCase)
                ? config.audioClipDirectory
                : config.spriteDirectory;
            return NeoComposePathUtility.CombineAssetPath(directory, $"{file.id}-{SanitizeFileName(file.name)}");
        }

        private static (string? templateId, object? record) ResolveTemplate(
            ProjectData projectData,
            ProjectFile file)
        {
            if (string.Equals(file.fileType, "audio", StringComparison.OrdinalIgnoreCase))
            {
                var templateId = file.unityAudioClipSettings?.templateId;
                if (templateId != null &&
                    projectData.audioClipTemplates.TryGetValue(templateId, out var audioTemplate))
                {
                    return (templateId, audioTemplate);
                }

                return (templateId, null);
            }

            var textureTemplateId = file.unityTextureSettings?.templateId;
            if (textureTemplateId != null &&
                projectData.textureTemplates.TryGetValue(textureTemplateId, out var textureTemplate))
            {
                return (textureTemplateId, textureTemplate);
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
