// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
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
        void WriteAllBytes(string assetPath, byte[] content);
        void Refresh();
        void SaveConfig(NeoComposeConfig config);
        void SchedulePostSynchronize(NeoComposeConfig config, string projectJsonPath);
        NeoAssetDatabase LoadOrCreateAssetDatabase(string assetPath);
        void ApplyUnityImportSettings(string assetPath, ProjectFile file, ProjectData projectData);
        Sprite[] LoadSprites(string assetPath);
        AudioClip? LoadAudioClip(string assetPath);
        void SaveAsset(UnityEngine.Object asset);
        void DeleteAsset(string assetPath);
    }

    /// <summary>
    /// <c>Library/NeoCompose/export.json</c>, which <c>neo export</c> writes last
    /// (P100 §7): the export's identity, the config ingest applies, and where
    /// each kept project file's bytes live in the workspace.
    /// </summary>
    internal sealed class NeoComposeExportSidecar
    {
        public int schemaVersion;
        public string exportId = "";
        public string workspaceRoot = "";
        public string projectId = "";
        public string versionId = "";
        public NeoComposeExportSidecarConfig config = new();
        public Dictionary<string, NeoComposeExportSidecarFile> files = new();
    }

    internal sealed class NeoComposeExportSidecarConfig
    {
        public string? namespaceForGeneratedTypes;
        public bool? singleton;
        public string? convexUrl;
        public NeoComposeUnityRuntimeOAuthConfig? runtimeOAuth;
    }

    internal sealed class NeoComposeExportSidecarFile
    {
        /// <summary>Relative to the sidecar's <c>workspaceRoot</c>.</summary>
        public string path = "";
        public string sha256 = "";
    }

    /// <summary>
    /// Ingests a <c>neo export</c> into this Unity project (P100 §7). The CLI
    /// writes generated code, <c>project.json</c>, partition and localization
    /// files itself; ingest applies the sidecar's config, imports what the CLI
    /// wrote, copies project files from the workspace, and schedules
    /// post-synchronize. A compile and domain reload can interrupt it at any
    /// step, so every step is idempotent and the ingested marker is written
    /// last.
    /// </summary>
    internal static class NeoComposeExportIngest
    {
        internal const string SidecarPath = "Library/NeoCompose/export.json";
        internal const string IngestedMarkerPath = "Library/NeoCompose/export.ingested";
        internal const int SidecarSchemaVersion = 1;

        /// <summary>The sidecar under <paramref name="projectRoot"/>, or null when there is none.</summary>
        internal static NeoComposeExportSidecar? ReadSidecar(string projectRoot)
        {
            string path = Path.Combine(projectRoot, SidecarPath);
            if (!File.Exists(path))
                return null;
            NeoComposeExportSidecar sidecar = JsonConvert.DeserializeObject<NeoComposeExportSidecar>(File.ReadAllText(path, Encoding.UTF8))
                ?? throw new InvalidOperationException($"{SidecarPath} is empty. Run `neo export` again.");
            if (sidecar.schemaVersion != SidecarSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"{SidecarPath} is schema {sidecar.schemaVersion}, but this SDK reads schema {SidecarSchemaVersion}. Update the Neo CLI and the NeoCompose SDK to matching versions.");
            }
            return sidecar;
        }

        internal static bool IsIngested(string projectRoot, NeoComposeExportSidecar sidecar)
        {
            string path = Path.Combine(projectRoot, IngestedMarkerPath);
            return File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == sidecar.exportId;
        }

        /// <summary>
        /// Runs every ingest step against <paramref name="config"/>, the
        /// resolved config (the <c>DontSave</c> overlay in a rig). Throws when
        /// ingest cannot run; returns the project files that failed. A failed
        /// file leaves the export un-ingested, so the next <c>neo export</c>
        /// rewrites the sidecar and ingest retries it.
        /// </summary>
        internal static string[] Ingest(
            string projectRoot,
            NeoComposeExportSidecar sidecar,
            NeoComposeConfig config,
            INeoComposeConfirmationService confirmations,
            INeoComposeEditorAssetService assets)
        {
            // 1. The export must be of the project and version this Unity project is on.
            if (config.projectId != sidecar.projectId)
            {
                throw new InvalidOperationException(
                    $"`neo export` wrote project '{sidecar.projectId}', but this Unity project's Neo Compose config is on project '{config.projectId}'. Export from a workspace on that project.");
            }
            if (config.versionId != sidecar.versionId)
            {
                throw new InvalidOperationException(
                    $"`neo export` wrote version '{sidecar.versionId}', but this Unity project's Neo Compose config is on version '{config.versionId}'. Run `neo branch switch {config.versionId}` in the workspace, then `neo export`.");
            }

            // 2. Apply config.
            NormalizeOutputDirectories(config);
            string namespaceForGeneratedTypes = sidecar.config.namespaceForGeneratedTypes ?? "";
            config.namespaceForGeneratedTypes = string.IsNullOrWhiteSpace(namespaceForGeneratedTypes)
                ? NeoComposeDefaults.NamespaceForGeneratedTypes
                : namespaceForGeneratedTypes;
            config.singleton = sidecar.config.singleton ?? NeoComposeDefaults.Singleton;
            ApplyRuntimeOAuthConfig(config, sidecar.config.runtimeOAuth);
            ApplyConvexUrl(config, sidecar.config.convexUrl);
            if (config.TryGetCloudSaveSyncWarning(
                    NeoComposeRuntimeSecretProvider.LoadRuntimeApiKey(),
                    out var cloudSyncWarning))
            {
                Debug.LogWarning(cloudSyncWarning);
            }
            assets.SaveConfig(config);

            // 3. Import what the CLI wrote.
            assets.Refresh();

            // 4. Project files, from the workspace.
            string projectJsonPath = NeoComposePathUtility.CombineAssetPath(
                config.projectJsonDirectory,
                NeoComposeEditorDefaults.ProjectJsonFileName);
            ProjectData projectData = NeoJsonProjectDataSource
                .FromFile(Path.Combine(projectRoot, projectJsonPath))
                .ReadProjectData();
            string[] fileErrors = NeoComposeFileSynchronizer.Synchronize(
                confirmations,
                assets,
                config,
                projectData,
                sidecar);

            // 5. Post-synchronize survives the domain reload new generated code causes.
            assets.SchedulePostSynchronize(config, projectJsonPath);

            // 6. Last, so an interrupted or failed ingest runs again.
            if (fileErrors.Length == 0)
            {
                string markerPath = Path.Combine(projectRoot, IngestedMarkerPath);
                File.WriteAllText(markerPath, sidecar.exportId, new UTF8Encoding(false));
            }
            return fileErrors;
        }

        /// <summary>
        /// Normalizes the config's output folders in place, throwing for the
        /// first one the editor rejects.
        /// </summary>
        internal static void NormalizeOutputDirectories(NeoComposeConfig config)
        {
            config.generatedTypesDirectory = Normalize(
                NeoComposePathUtility.TryNormalizeAssetDirectory(config.generatedTypesDirectory, out var generatedTypes, out var error),
                generatedTypes,
                error);
            config.projectJsonDirectory = Normalize(
                NeoComposePathUtility.TryNormalizeAssetDirectory(config.projectJsonDirectory, out var projectJson, out error),
                projectJson,
                error);
            config.spriteDirectory = Normalize(
                NeoComposePathUtility.TryNormalizeAssetDirectory(config.spriteDirectory, out var sprites, out error),
                sprites,
                error);
            config.localizationResourcesDirectory = Normalize(
                NeoComposePathUtility.TryNormalizeResourcesDirectory(config.localizationResourcesDirectory, out var localizationResources, out error),
                localizationResources,
                error);
            config.localizationStreamingAssetsDirectory = Normalize(
                NeoComposePathUtility.TryNormalizeStreamingAssetsDirectory(config.localizationStreamingAssetsDirectory, out var localizationStreamingAssets, out error),
                localizationStreamingAssets,
                error);
            config.audioClipDirectory = Normalize(
                NeoComposePathUtility.TryNormalizeAssetDirectory(config.audioClipDirectory, out var audioClips, out error),
                audioClips,
                error);
        }

        private static string Normalize(bool valid, string normalized, string error) =>
            valid ? normalized : throw new InvalidOperationException(error);

        /// <summary>
        /// Writes the runtime OAuth fields (<c>runtimeOAuthClientId</c> /
        /// <c>runtimeOAuthScopes</c>) from the export. These are always
        /// overwritten — the export is the source of truth for the version, so
        /// a version predating the introduction marker (or a disabled client)
        /// clears them. <c>enableOAuthCloudSync</c> is developer-owned and is
        /// seeded <c>true</c> only the first time a client becomes available;
        /// it is never force-overwritten thereafter.
        /// </summary>
        internal static void ApplyRuntimeOAuthConfig(
            NeoComposeConfig config,
            NeoComposeUnityRuntimeOAuthConfig? runtimeOAuth)
        {
            bool hadClientBefore = config.HasRuntimeOAuthClient;

            // A developer override sticks: leave the hand-edited client id / scopes
            // alone instead of overwriting them from the export.
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
        /// Writes the export's Convex deployment URL. Null means the deployment
        /// has none configured and the field is left alone (a hand-entered URL
        /// survives); a present value is the source of truth and overwrites.
        /// </summary>
        internal static void ApplyConvexUrl(NeoComposeConfig config, string? convexUrl)
        {
            if (convexUrl == null)
                return;
            config.convexUrl = convexUrl.Trim();
        }
    }

    /// <summary>
    /// Notices a <c>neo export</c> without the Neo Compose window open: once a
    /// second it stats the sidecar, and when its modification time changes and
    /// its <c>exportId</c> isn't the ingested one, it ingests. Imports of the
    /// written files don't drive ingest, because Unity can import them before
    /// the sidecar lands. Batch mode ingests only through
    /// <see cref="NeoComposeBatchSync.Run"/>.
    /// </summary>
    [InitializeOnLoad]
    internal static class NeoComposeExportWatcher
    {
        private const double IntervalSeconds = 1;

        private static double nextCheckAt;
        private static DateTime lastSidecarWriteUtc;

        static NeoComposeExportWatcher()
        {
            if (Application.isBatchMode)
                return;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextCheckAt)
                return;
            nextCheckAt = EditorApplication.timeSinceStartup + IntervalSeconds;
            if (EditorApplication.isCompiling
                || EditorApplication.isUpdating
                || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            string projectRoot = Directory.GetCurrentDirectory();
            var sidecarFile = new FileInfo(Path.Combine(projectRoot, NeoComposeExportIngest.SidecarPath));
            if (!sidecarFile.Exists || sidecarFile.LastWriteTimeUtc == lastSidecarWriteUtc)
                return;
            lastSidecarWriteUtc = sidecarFile.LastWriteTimeUtc;

            try
            {
                NeoComposeExportSidecar sidecar = NeoComposeExportIngest.ReadSidecar(projectRoot)!;
                if (NeoComposeExportIngest.IsIngested(projectRoot, sidecar))
                    return;
                string[] fileErrors = NeoComposeExportIngest.Ingest(
                    projectRoot,
                    sidecar,
                    NeoComposeResolvedConfig.Resolve(NeoComposeConfigProvider.LoadOrCreate()),
                    new NeoComposeEditorDialogConfirmationService(),
                    new NeoComposeEditorAssetService());
                if (fileErrors.Length > 0)
                {
                    string message = "Neo Compose export ingested, but some project files failed:\n" + string.Join("\n", fileErrors);
                    Debug.LogError(message);
                    NeoComposePostSynchronizeProcessor.SetStatus(message, MessageType.Error);
                    return;
                }
                NeoComposePostSynchronizeProcessor.SetStatus(
                    "Neo Compose export ingested. Post-synchronize work is finishing...",
                    MessageType.Info);
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                NeoComposePostSynchronizeProcessor.SetStatus(exception.Message, MessageType.Error);
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

        public void WriteAllBytes(string assetPath, byte[] content)
        {
            File.WriteAllBytes(assetPath, content);
        }

        public void Refresh()
        {
            AssetDatabase.Refresh();
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
            if (existing != null)
                return existing;

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
            if (sprites.Length > 0)
                return sprites;

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
                if (!AssetDatabase.DeleteAsset(assetPath))
                    throw new IOException($"Unity could not delete asset: {assetPath}");
            }
            else
            {
                if (File.Exists(assetPath))
                    File.Delete(assetPath);
                if (File.Exists(assetPath + ".meta"))
                    File.Delete(assetPath + ".meta");
            }
            if (File.Exists(assetPath) || File.Exists(assetPath + ".meta"))
                throw new IOException($"Asset deletion left files behind: {assetPath}");
        }
    }
}
