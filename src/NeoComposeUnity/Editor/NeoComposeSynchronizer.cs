// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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

        /// <summary>
        /// Confirmation for replacing already-synchronized files (generated C#,
        /// project.json, localization, file assets). Split from
        /// <see cref="Confirm"/> so the "Ask me before overwriting files"
        /// preference can auto-approve exactly these prompts — destructive
        /// one-offs (stale-asset deletion, error overrides) always ask.
        /// </summary>
        bool ConfirmReplaceFiles(string title, string message, string ok, string cancel);
    }

    public interface INeoComposeEditorAssetService
    {
        bool FileExists(string assetPath);
        string ReadAllText(string assetPath);
        string[] FindFiles(string assetDirectory, string searchPattern);
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
        private readonly INeoComposeEditorExportCache exportCache;

        public NeoComposeSynchronizer(
            INeoComposeEditorApiClient apiClient,
            INeoComposeConfirmationService confirmations,
            INeoComposeEditorAssetService assets,
            INeoComposeEditorExportCache? exportCache = null)
        {
            this.apiClient = apiClient;
            this.confirmations = confirmations;
            this.assets = assets;
            this.exportCache = exportCache ?? new NeoComposeEditorExportCache();
        }

        public async Task<NeoComposeSyncResult> SynchronizeAsync(
            NeoComposeConfig config,
            Action<string>? onProgress = null)
        {
            var validation = ValidateConfig(config);
            if (!validation.success) return validation;

            try
            {
                var generatedTypesPath = NeoComposePathUtility.CombineAssetPath(
                    config.generatedTypesDirectory,
                    NeoComposeEditorDefaults.GeneratedTypesFileName);
                var projectJsonPath = NeoComposePathUtility.CombineAssetPath(
                    config.projectJsonDirectory,
                    NeoComposeEditorDefaults.ProjectJsonFileName);
                onProgress?.Invoke("Exporting project...");
                var incremental = await TryBuildIncrementalExportAsync(
                    config,
                    projectJsonPath,
                    onProgress);
                if (incremental?.unchanged == true)
                {
                    return NeoComposeSyncResult.Success(
                        "Neo Compose project is already synchronized.",
                        incremental.response);
                }
                var isIncremental = incremental != null;
                var exportResponse = incremental?.response
                    ?? await apiClient.ExportProjectAsync(
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

                var localizationPaths = BuildLocalizationFilePaths(
                    config,
                    exportResponse.localizationFiles,
                    ReadLocalizationMainLocaleOrDefault(exportResponse.projectJson));
                var replacementRoots = isIncremental
                    ? new[] { projectJsonPath }
                    : new[] { generatedTypesPath, projectJsonPath };
                var existingReplacementPaths = replacementRoots
                    .Concat(localizationPaths.Values)
                    .Where(assets.FileExists)
                    .ToArray();

                if (existingReplacementPaths.Length > 0 &&
                    !confirmations.ConfirmReplaceFiles(
                        "Replace Neo Compose files?",
                        "Existing synchronized files will be replaced:\n\n" +
                        string.Join("\n", existingReplacementPaths),
                        "Replace",
                        "Cancel"))
                {
                    return NeoComposeSyncResult.Failure("Synchronization cancelled before replacing existing files.");
                }

                assets.EnsureDirectory(config.generatedTypesDirectory);
                assets.EnsureDirectory(config.projectJsonDirectory);
                var localizationSyncErrors = isIncremental
                    ? Array.Empty<string>()
                    : SynchronizeLocalizationFiles(
                        config,
                        exportResponse.localizationFiles,
                        localizationPaths,
                        onProgress);
                var assetSyncErrors = await SynchronizeFilesAsync(config, exportResponse.projectJson, onProgress);

                onProgress?.Invoke("Writing generated files...");
                if (!isIncremental)
                {
                    assets.WriteAllText(generatedTypesPath, exportResponse.generatedTypes);
                }
                assets.WriteAllText(projectJsonPath, exportResponse.projectJson);
                if (exportResponse.version != null && !string.IsNullOrWhiteSpace(exportResponse.version.id))
                {
                    config.versionId = exportResponse.version.id;
                }

                config.namespaceForGeneratedTypes = ReadUnityNamespaceOrDefault(exportResponse.projectJson);
                config.singleton = ReadUnitySingletonOrDefault(exportResponse.projectJson);
                ApplyRuntimeOAuthConfig(config, exportResponse.runtimeOAuth);
                ApplyConvexUrl(config, exportResponse.convexUrl);
                if (config.TryGetCloudSaveSyncWarning(
                        NeoComposeRuntimeSecretProvider.LoadRuntimeApiKey(),
                        out var cloudSyncWarning))
                {
                    Debug.LogWarning(cloudSyncWarning);
                }
                assets.SaveConfig(config);
                assets.SchedulePostSynchronize(config, projectJsonPath);
                if (exportResponse.syncState != null)
                {
                    exportCache.Save(
                        config.projectId,
                        config.versionId,
                        PrepareSyncStateForCache(exportResponse.syncState));
                }

                var syncErrors = localizationSyncErrors.Concat(assetSyncErrors).ToArray();
                if (syncErrors.Length > 0)
                {
                    return NeoComposeSyncResult.Failure(
                        "Neo Compose files synchronized, but some assets failed:\n" +
                        string.Join("\n", syncErrors));
                }

                return NeoComposeSyncResult.Success("Neo Compose files synchronized.", exportResponse);
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                return NeoComposeSyncResult.Failure(exception.Message);
            }
        }

        private sealed class IncrementalExportAttempt
        {
            public NeoComposeUnityExportResponse response = new();
            public bool unchanged;
        }

        private async Task<IncrementalExportAttempt?> TryBuildIncrementalExportAsync(
            NeoComposeConfig config,
            string projectJsonPath,
            Action<string>? onProgress)
        {
            var state = exportCache.Load(config.projectId, config.versionId);
            if (state == null || state.schemaVersion != 1) return null;
            if (!assets.FileExists(projectJsonPath)) return null;

            var delta = await apiClient.ExportProjectDeltaAsync(
                config.apiBaseUrl,
                config.projectId,
                config.versionId,
                state.cursor);
            if (delta.fullResync || delta.cursor == null) return null;
            // Value records are the high-volume content path and map directly
            // onto the exported values/valuePartitions dictionaries. Every
            // other record kind can affect global materialization (dialogue
            // aggregation, localization files, file reachability, or codegen)
            // and deliberately falls back to the exact full export.
            if (delta.codegenAffected
                || delta.runtimeContractAffected
                || delta.records.Any(record => record.recordKind != "value"))
            {
                return null;
            }
            if (delta.records.Count == 0)
            {
                state.cursor = delta.cursor;
                exportCache.Save(config.projectId, config.versionId, state);
                return new IncrementalExportAttempt
                {
                    unchanged = true,
                    response = new NeoComposeUnityExportResponse
                    {
                        mode = "incremental",
                        projectId = config.projectId,
                        projectJson = assets.ReadAllText(projectJsonPath),
                        syncState = state,
                    },
                };
            }

            onProgress?.Invoke("Fetching changed project snapshots...");
            var snapshotsById = state.snapshots.ToDictionary(snapshot => snapshot.id);
            var missingIds = delta.records
                .Where(record => !record.deleted && record.snapshotId != null)
                .Select(record => record.snapshotId!)
                .Where(snapshotId => !snapshotsById.ContainsKey(snapshotId))
                .Distinct()
                .ToArray();
            if (missingIds.Length > 0)
            {
                var fetched = await apiClient.ExportProjectSnapshotsAsync(
                    config.apiBaseUrl,
                    config.projectId,
                    config.versionId,
                    missingIds);
                foreach (var snapshot in fetched.snapshots)
                {
                    snapshotsById[snapshot.id] = snapshot;
                }
                if (missingIds.Any(snapshotId => !snapshotsById.ContainsKey(snapshotId)))
                {
                    return null;
                }
            }

            JObject root;
            try
            {
                root = JObject.Parse(assets.ReadAllText(projectJsonPath));
            }
            catch (JsonException)
            {
                return null;
            }
            var headsByKey = state.heads.ToDictionary(HeadKey);
            var projectFileIds = root["files"] is JObject files
                ? files.Properties().Select(property => property.Name).ToArray()
                : Array.Empty<string>();

            foreach (var descriptor in delta.records)
            {
                var oldValue = FindExportedValue(root, descriptor.recordId);
                NeoComposeUnityExportCachedSnapshot? snapshot = null;
                if (!descriptor.deleted)
                {
                    if (descriptor.snapshotId == null
                        || !snapshotsById.TryGetValue(descriptor.snapshotId, out snapshot)
                        || snapshot.recordKind != "value"
                        || snapshot.recordId != descriptor.recordId)
                    {
                        return null;
                    }
                }
                // File inclusion is a global reachability calculation. A value
                // that adds or removes any known file id uses the full export
                // rather than risking a stale asset manifest.
                if (projectFileIds.Any(fileId =>
                        TokenContainsString(oldValue, fileId)
                        || TokenContainsString(snapshot?.data, fileId)))
                {
                    return null;
                }
                ApplyValueDelta(root, descriptor.recordId, snapshot?.data);
                var nextHead = new NeoComposeUnityExportHeadDescriptor
                {
                    recordKind = descriptor.recordKind,
                    recordId = descriptor.recordId,
                    snapshotId = descriptor.deleted ? null : descriptor.snapshotId,
                    contentHash = descriptor.deleted ? null : snapshot!.contentHash,
                    deleted = descriptor.deleted,
                };
                if (descriptor.deleted)
                {
                    headsByKey.Remove(HeadKey(nextHead));
                }
                else
                {
                    headsByKey[HeadKey(nextHead)] = nextHead;
                }
            }

            var contentHash = ComputeProjectDocumentContentHash(headsByKey.Values);
            if (root["metadata"] is not JObject metadata) return null;
            metadata["projectDocumentContentHash"] = contentHash;

            state.cursor = delta.cursor;
            state.heads = headsByKey.Values
                .OrderBy(head => HeadKey(head), StringComparer.Ordinal)
                .ToList();
            state.snapshots = snapshotsById.Values
                .Where(snapshot =>
                    headsByKey.TryGetValue(
                        snapshot.recordKind + ":" + snapshot.recordId,
                        out var head)
                    && head.snapshotId == snapshot.id)
                .ToList();
            var project = root["project"] as JObject;
            return new IncrementalExportAttempt
            {
                response = new NeoComposeUnityExportResponse
                {
                    mode = "incremental",
                    projectId = config.projectId,
                    projectName = project?["name"]?.Value<string>() ?? "",
                    projectJson = root.ToString(Formatting.Indented),
                    generatedTypes = "",
                    projectDocumentContentHash = contentHash,
                    codegenContractHash = metadata["codegenContractHash"]?.Value<string>(),
                    runtimeDataContractHash = metadata["runtimeDataContractHash"]?.Value<string>(),
                    syncState = state,
                },
            };
        }

        private static string HeadKey(NeoComposeUnityExportHeadDescriptor head) =>
            head.recordKind + ":" + head.recordId;

        private static JToken? FindExportedValue(JObject root, string valueId)
        {
            if (root["values"] is JObject values && values.TryGetValue(valueId, out var main))
            {
                return main;
            }
            if (root["valuePartitions"] is not JObject partitions) return null;
            foreach (var partition in partitions.Properties())
            {
                if (partition.Value is JObject rows && rows.TryGetValue(valueId, out var value))
                {
                    return value;
                }
            }
            return null;
        }

        private static void ApplyValueDelta(JObject root, string valueId, JToken? rawData)
        {
            var values = root["values"] as JObject;
            if (values == null)
            {
                values = new JObject();
                root["values"] = values;
            }
            values.Remove(valueId);
            var partitions = root["valuePartitions"] as JObject;
            if (partitions == null)
            {
                partitions = new JObject();
                root["valuePartitions"] = partitions;
            }
            foreach (var partition in partitions.Properties().ToArray())
            {
                if (partition.Value is not JObject rows) continue;
                rows.Remove(valueId);
                if (!rows.Properties().Any()) partition.Remove();
            }
            if (rawData == null) return;
            var record = rawData.DeepClone() as JObject
                ?? throw new JsonSerializationException("A project record snapshot must be an object.");
            var mapKey = record["mapKey"]?.Value<string>();
            if (string.IsNullOrEmpty(mapKey))
            {
                values[valueId] = record;
                return;
            }
            var target = partitions[mapKey] as JObject ?? new JObject();
            partitions[mapKey] = target;
            target[valueId] = record;
        }

        private static NeoComposeUnityExportSyncState PrepareSyncStateForCache(
            NeoComposeUnityExportSyncState state)
        {
            var currentValueSnapshots = state.heads
                .Where(head =>
                    !head.deleted
                    && head.recordKind == "value"
                    && head.snapshotId != null)
                .Select(head => head.snapshotId!)
                .ToHashSet(StringComparer.Ordinal);
            state.snapshots = state.snapshots
                .Where(snapshot =>
                    snapshot.recordKind == "value"
                    && currentValueSnapshots.Contains(snapshot.id))
                .ToList();
            return state;
        }

        private static bool TokenContainsString(JToken? token, string expected)
        {
            if (token == null) return false;
            if (token.Type == JTokenType.String)
            {
                return token.Value<string>() == expected;
            }
            return token.Children().Any(child => TokenContainsString(child, expected));
        }

        private static string ComputeProjectDocumentContentHash(
            IEnumerable<NeoComposeUnityExportHeadDescriptor> heads)
        {
            var array = new JArray(
                heads
                    .Where(head => !head.deleted && head.contentHash != null)
                    .OrderBy(HeadKey, StringComparer.Ordinal)
                    .Select(head => new JObject
                    {
                        ["contentHash"] = head.contentHash,
                        ["deleted"] = false,
                        ["recordId"] = head.recordId,
                        ["recordKind"] = head.recordKind,
                    }));
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(
                Encoding.UTF8.GetBytes(array.ToString(Formatting.None)));
            var result = new StringBuilder(bytes.Length * 2);
            foreach (var item in bytes) result.Append(item.ToString("x2"));
            return result.ToString();
        }

        private string[] SynchronizeLocalizationFiles(
            NeoComposeConfig config,
            List<NeoComposeUnityLocalizationFile> localizationFiles,
            IReadOnlyDictionary<string, string> localizationPaths,
            Action<string>? onProgress)
        {
            var errors = new List<string>();
            if (localizationFiles.Count == 0) return errors.ToArray();

            onProgress?.Invoke("Writing localization files...");
            assets.EnsureDirectory(config.localizationResourcesDirectory);
            if (config.useStreamingAssetsForNonMainLocales)
            {
                assets.EnsureDirectory(config.localizationStreamingAssetsDirectory);
            }

            var expectedPaths = new HashSet<string>(localizationPaths.Values);
            DeleteStaleLocalizationFiles(config.localizationResourcesDirectory, expectedPaths);
            if (config.useStreamingAssetsForNonMainLocales &&
                config.localizationStreamingAssetsDirectory != config.localizationResourcesDirectory)
            {
                DeleteStaleLocalizationFiles(config.localizationStreamingAssetsDirectory, expectedPaths);
            }

            foreach (var file in localizationFiles)
            {
                if (!localizationPaths.TryGetValue(file.locale, out var assetPath)) continue;
                try
                {
                    assets.WriteAllText(assetPath, file.content ?? "");
                    assets.RefreshAsset(assetPath);
                }
                catch (Exception exception)
                {
                    errors.Add($"{file.locale}: {exception.Message}");
                }
            }

            return errors.ToArray();
        }

        private void DeleteStaleLocalizationFiles(
            string assetDirectory,
            HashSet<string> expectedPaths)
        {
            foreach (var existingPath in assets.FindFiles(assetDirectory, "*.json"))
            {
                if (expectedPaths.Contains(existingPath)) continue;
                assets.DeleteAsset(existingPath);
            }
        }

        private static Dictionary<string, string> BuildLocalizationFilePaths(
            NeoComposeConfig config,
            List<NeoComposeUnityLocalizationFile> localizationFiles,
            string mainLocale)
        {
            var paths = new Dictionary<string, string>();
            foreach (var file in localizationFiles)
            {
                if (!IsSafeLocalizationFileName(file.fileName)) continue;
                var directory =
                    config.useStreamingAssetsForNonMainLocales &&
                    !string.Equals(file.locale, mainLocale, StringComparison.Ordinal)
                        ? config.localizationStreamingAssetsDirectory
                        : config.localizationResourcesDirectory;
                paths[file.locale] = NeoComposePathUtility.CombineAssetPath(directory, file.fileName);
            }
            return paths;
        }

        private static string ReadLocalizationMainLocaleOrDefault(string projectJson)
        {
            try
            {
                var main = JObject.Parse(projectJson)["localization"]?["mainLocale"]?.Value<string>();
                return string.IsNullOrWhiteSpace(main) ? "en-US" : main!;
            }
            catch
            {
                return "en-US";
            }
        }

        private static bool IsSafeLocalizationFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            if (fileName.Contains('/')) return false;
            if (fileName.Contains('\\')) return false;
            if (fileName.Contains("..")) return false;
            return string.Equals(Path.GetExtension(fileName), ".json", StringComparison.OrdinalIgnoreCase);
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

        /// <summary>
        /// Writes the synced runtime OAuth fields (<c>runtimeOAuthClientId</c> /
        /// <c>runtimeOAuthScopes</c>) from the export bundle. These are always
        /// overwritten — the bundle is the source of truth for the selected version,
        /// so a version predating the introduction marker (or a disabled client)
        /// clears them. <c>enableOAuthCloudSync</c> is developer-owned and is seeded
        /// <c>true</c> only the first time a client becomes available; it is never
        /// force-overwritten thereafter.
        /// </summary>
        public static void ApplyRuntimeOAuthConfig(
            NeoComposeConfig config,
            NeoComposeUnityRuntimeOAuthConfig? runtimeOAuth)
        {
            bool hadClientBefore = config.HasRuntimeOAuthClient;

            // A developer override sticks: leave the hand-edited client id / scopes
            // alone instead of overwriting them from the export bundle.
            if (!config.runtimeOAuthOverridden)
            {
                if (runtimeOAuth == null || !runtimeOAuth.configuredForVersion)
                {
                    config.runtimeOAuthClientId = "";
                    config.runtimeOAuthScopes = Array.Empty<string>();
                }
                else
                {
                    config.runtimeOAuthClientId = runtimeOAuth.runtimeOAuthClientId ?? "";
                    config.runtimeOAuthScopes = runtimeOAuth.scopes ?? Array.Empty<string>();
                }
            }

            // Convenience seed: turn the developer-owned toggle on the first time a
            // client becomes available. Guarded on "no client before" so it never
            // re-enables a toggle the developer deliberately turned off while a client
            // was already present.
            if (!hadClientBefore && config.HasRuntimeOAuthClient)
            {
                config.enableOAuthCloudSync = true;
            }
        }

        /// <summary>
        /// Writes the synced Convex deployment URL from the export bundle. Null
        /// means the server has none configured and the field is left alone (a
        /// hand-entered URL survives syncing against such a server); a present
        /// value is the source of truth and overwrites.
        /// </summary>
        public static void ApplyConvexUrl(NeoComposeConfig config, string? convexUrl)
        {
            if (convexUrl == null) return;
            config.convexUrl = convexUrl.Trim();
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
        private readonly Func<bool> askBeforeOverwritingFiles;
        private readonly Func<string, string, string, string, bool> displayDialog;

        public NeoComposeEditorDialogConfirmationService()
            : this(
                () => NeoComposeEditorSyncPreferences.AskBeforeOverwritingFiles,
                EditorUtility.DisplayDialog)
        {
        }

        /// <summary>Seam constructor so the preference gate is unit-testable
        /// without popping modal dialogs.</summary>
        public NeoComposeEditorDialogConfirmationService(
            Func<bool> askBeforeOverwritingFiles,
            Func<string, string, string, string, bool> displayDialog)
        {
            this.askBeforeOverwritingFiles = askBeforeOverwritingFiles;
            this.displayDialog = displayDialog;
        }

        public bool Confirm(string title, string message, string ok, string cancel)
        {
            return displayDialog(title, message, ok, cancel);
        }

        public bool ConfirmReplaceFiles(string title, string message, string ok, string cancel)
        {
            // "Ask me before overwriting files" is opt-in: the default
            // auto-approves replacement of regenerable synchronized files.
            if (!askBeforeOverwritingFiles()) return true;
            return displayDialog(title, message, ok, cancel);
        }
    }

    public sealed class NeoComposeEditorAssetService : INeoComposeEditorAssetService
    {
        public bool FileExists(string assetPath)
        {
            return File.Exists(assetPath);
        }

        public string ReadAllText(string assetPath)
        {
            return File.ReadAllText(assetPath, Encoding.UTF8);
        }

        public string[] FindFiles(string assetDirectory, string searchPattern)
        {
            if (!Directory.Exists(assetDirectory)) return Array.Empty<string>();
            return Directory.GetFiles(assetDirectory, searchPattern)
                .Select(NeoComposePathUtility.NormalizeSeparators)
                .ToArray();
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
