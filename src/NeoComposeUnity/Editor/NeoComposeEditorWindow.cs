// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    public sealed class NeoComposeEditorWindow : EditorWindow
    {
        private NeoComposeConfig? config;
        private INeoComposeEditorApiClient apiClient = new NeoComposeEditorApiClient();
        private NeoComposeSynchronizer? synchronizer;
        private NeoComposeProjectSettingsUpdater? projectSettingsUpdater;
        private readonly List<NeoComposeProjectSummary> projects = new();
        private readonly List<NeoComposeProjectReleaseChannel> releaseChannels = new();
        private readonly List<NeoComposeProjectVersion> versions = new();
        private readonly List<NeoComposeProjectVersionStatus> versionStatuses = new();
        private Vector2 scroll;
        private string searchText = "";
        private string status = "";
        private bool loading;
        private bool clearKeyboardFocusNextGui;
        private const float ContentPadding = 12f;
        private const float LabelWidth = 132f;
        private const float ProjectLabelWidth = 84f;
        private const float BrowseButtonWidth = 72f;
        private const float RemoveButtonWidth = 76f;

        [MenuItem("Tools/Neo Compose")]
        [MenuItem("Window/Neo Compose")]
        public static void Open()
        {
            GetWindow<NeoComposeEditorWindow>("Neo Compose");
        }

        private void OnEnable()
        {
            config = NeoComposeConfigProvider.LoadOrCreate();
            synchronizer = new NeoComposeSynchronizer(
                apiClient,
                new NeoComposeEditorDialogConfirmationService(),
                new NeoComposeEditorAssetService());
            projectSettingsUpdater = new NeoComposeProjectSettingsUpdater(
                apiClient,
                new NeoComposeEditorAssetService());
            if (config.HasProject)
            {
                _ = RefreshVersionMetadataAsync(false);
            }
        }

        private void OnGUI()
        {
            if (config == null)
            {
                EditorGUILayout.HelpBox("Neo Compose config could not be loaded.", MessageType.Error);
                if (GUILayout.Button("Retry")) OnEnable();
                return;
            }

            ClearKeyboardFocusIfRequested();

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(ContentPadding);
            EditorGUILayout.BeginVertical();

            EditorGUILayout.LabelField("Neo Compose", TitleStyle());
            EditorGUILayout.LabelField(
                "Synchronize generated Unity files from the Neo Compose web app.",
                MutedStyle());
            EditorGUILayout.Space(2);
            RenderConnectionSection(config);

            if (config.HasProject)
            {
                RenderSelectedProject(config);
            }
            else
            {
                RenderProjectSearch(config);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(status, MessageType.Info);
            }

            EditorGUILayout.EndVertical();
            GUILayout.Space(ContentPadding);
            EditorGUILayout.EndHorizontal();
        }

        private void RenderConnectionSection(NeoComposeConfig config)
        {
            BeginSection("Connection");
            using (new EditorGUIUtilityLabelWidthScope(LabelWidth))
            {
                EditorGUI.BeginChangeCheck();
                config.apiBaseUrl = EditorGUILayout.TextField("API Base URL", config.apiBaseUrl);
                if (EditorGUI.EndChangeCheck())
                {
                    NeoComposeConfigProvider.Save(config);
                    if (config.HasProject)
                    {
                        _ = RefreshVersionMetadataAsync(false);
                    }
                }
            }

            EndSection();
        }

        private void RenderProjectSearch(NeoComposeConfig config)
        {
            BeginSection("Project");
            using (new EditorGUIUtilityLabelWidthScope(LabelWidth))
            {
                searchText = EditorGUILayout.TextField("Search", searchText);
            }

            using (new EditorGUI.DisabledScope(loading))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button(loading ? "Searching..." : "Refresh Projects", GUILayout.Width(140)))
                {
                    _ = SearchProjectsAsync();
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(5);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (var project in projects)
            {
                DrawProjectSearchResult(config, project);
            }

            EditorGUILayout.EndScrollView();
            EndSection();
        }

        private void RenderSelectedProject(NeoComposeConfig config)
        {
            BeginSection("Selected Project");
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Project", FormLabelStyle(), GUILayout.Width(ProjectLabelWidth));
            EditorGUILayout.LabelField(config.projectName, ProjectNameStyle());
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Remove", GUILayout.Width(RemoveButtonWidth)))
            {
                config.ClearProject();
                releaseChannels.Clear();
                versions.Clear();
                versionStatuses.Clear();
                NeoComposeConfigProvider.Save(config);
                status = "Project unlinked. Synchronized files were left untouched.";
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Project ID", FormLabelStyle(), GUILayout.Width(ProjectLabelWidth));
            EditorGUILayout.SelectableLabel(
                config.projectId,
                ProjectIdStyle(),
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
            EndSection();

            RenderVersionSelection(config);

            BeginSection("Export Settings");
            using (new EditorGUIUtilityLabelWidthScope(LabelWidth))
            {
                EditorGUI.BeginChangeCheck();
                config.namespaceForGeneratedTypes = EditorGUILayout.TextField(
                    "Unity Namespace",
                    config.namespaceForGeneratedTypes);
                config.singleton = EditorGUILayout.Toggle("Singleton", config.singleton);
                if (EditorGUI.EndChangeCheck())
                {
                    NeoComposeConfigProvider.Save(config);
                }
            }

            EditorGUILayout.Space(3);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(loading || !CanSaveUnityExportSettings(config)))
            {
                if (GUILayout.Button("Save to web", GUILayout.Width(120), GUILayout.Height(24)))
                {
                    _ = SaveUnityExportSettingsAsync();
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EndSection();

            BeginSection("Output");
            using (new EditorGUIUtilityLabelWidthScope(LabelWidth))
            {
                DrawDirectoryField("Generated Types", ref config.generatedTypesDirectory);
                DrawDirectoryField("Project JSON", ref config.projectJsonDirectory);
                DrawDirectoryField("Localization Resources", ref config.localizationResourcesDirectory);
                DrawDirectoryField("Localization StreamingAssets", ref config.localizationStreamingAssetsDirectory);
                EditorGUI.BeginChangeCheck();
                config.useStreamingAssetsForNonMainLocales = EditorGUILayout.Toggle(
                    "Stream Non-Main Locales",
                    config.useStreamingAssetsForNonMainLocales);
                if (config.useStreamingAssetsForNonMainLocales)
                {
                    EditorGUILayout.HelpBox(
                        "Non-main locale files synced to StreamingAssets require an explicit async preload before synchronous localized text getters can use them. Until preload completes, localized text falls back to loaded locales and the main locale.",
                        MessageType.Warning);
                }
                config.preloadSystemLocale = EditorGUILayout.Toggle(
                    "Preload System Locale",
                    config.preloadSystemLocale);
                config.localeOverride = EditorGUILayout.TextField("Locale Override", config.localeOverride);
                if (EditorGUI.EndChangeCheck())
                {
                    NeoComposeConfigProvider.Save(config);
                }
                DrawDirectoryField("Sprites", ref config.spriteDirectory);
                DrawDirectoryField("Audio Clips", ref config.audioClipDirectory);
            }

            EndSection();

            BeginSection("Project Assets");
            EditorGUILayout.LabelField(
                "Writes NeoGeneratedTypes.cs and project.json to the configured folders.",
                MutedStyle());
            EditorGUILayout.Space(3);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(loading || !CanSynchronize(config)))
            {
                if (GUILayout.Button(loading ? "Synchronizing..." : "Synchronize", GUILayout.Width(160), GUILayout.Height(26)))
                {
                    _ = SynchronizeAsync();
                }
            }

            if (GUILayout.Button("Edit in web", GUILayout.Width(120), GUILayout.Height(26)))
            {
                Application.OpenURL(BuildProjectSchemaUrl(config.apiBaseUrl, config.projectId, config.versionId));
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EndSection();
        }

        private void RenderVersionSelection(NeoComposeConfig config)
        {
            BeginSection("Version");
            if (releaseChannels.Count == 0 || versions.Count == 0 || versionStatuses.Count == 0)
            {
                EditorGUILayout.LabelField(
                    loading ? "Loading release channels and versions..." : "Release channel and version metadata has not been loaded.",
                    MutedStyle());
                using (new EditorGUI.DisabledScope(loading))
                {
                    if (GUILayout.Button("Refresh Versions", GUILayout.Width(128)))
                    {
                        _ = RefreshVersionMetadataAsync(true);
                    }
                }

                EndSection();
                return;
            }

            var orderedChannels = NeoComposeVersionSelectionUtility.OrderChannels(releaseChannels).ToArray();
            var channelIndex = Math.Max(0, Array.FindIndex(orderedChannels, channel => channel.id == config.targetReleaseChannelId));
            if (channelIndex >= orderedChannels.Length) channelIndex = 0;

            using (new EditorGUIUtilityLabelWidthScope(LabelWidth))
            {
                EditorGUI.BeginChangeCheck();
                var nextChannelIndex = EditorGUILayout.Popup(
                    "Release Channel",
                    channelIndex,
                    orderedChannels.Select(channel => channel.name).ToArray());
                if (EditorGUI.EndChangeCheck() && nextChannelIndex >= 0 && nextChannelIndex < orderedChannels.Length)
                {
                    SelectReleaseChannel(config, orderedChannels[nextChannelIndex].id);
                }

                var options = NeoComposeVersionSelectionUtility.BuildVersionDropdownOptions(
                    versions,
                    versionStatuses,
                    config.targetReleaseChannelId,
                    config.versionId);
                if (options.Length == 0)
                {
                    EditorGUILayout.LabelField("Version", "No versions available");
                }
                else
                {
                    var versionIndex = Math.Max(0, Array.FindIndex(options, version => version.id == config.versionId));
                    if (versionIndex >= options.Length) versionIndex = 0;
                    EditorGUI.BeginChangeCheck();
                    var nextVersionIndex = EditorGUILayout.Popup(
                        "Version",
                        versionIndex,
                        options.Select(FormatVersionOption).ToArray());
                    if (EditorGUI.EndChangeCheck() && nextVersionIndex >= 0 && nextVersionIndex < options.Length)
                    {
                        config.versionId = options[nextVersionIndex].id;
                        NeoComposeConfigProvider.Save(config);
                    }
                }
            }

            RenderVersionWarnings(config);
            RenderUpdateAvailable(config);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(loading))
            {
                if (GUILayout.Button("Refresh Versions", GUILayout.Width(128)))
                {
                    _ = RefreshVersionMetadataAsync(true);
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EndSection();
        }

        private void SelectReleaseChannel(NeoComposeConfig config, string channelId)
        {
            config.targetReleaseChannelId = channelId;
            if (!string.IsNullOrWhiteSpace(config.versionId))
            {
                var current = versions.FirstOrDefault(version => version.id == config.versionId);
                if (current != null &&
                    NeoComposeVersionSelectionUtility.IsVersionInChannel(current, versionStatuses, channelId))
                {
                    NeoComposeConfigProvider.Save(config);
                    return;
                }
            }

            var latest = NeoComposeVersionSelectionUtility.SelectLatestVersionForChannel(
                versions,
                versionStatuses,
                channelId);
            config.versionId = latest?.id ?? "";
            NeoComposeConfigProvider.Save(config);
        }

        private void RenderVersionWarnings(NeoComposeConfig config)
        {
            var current = versions.FirstOrDefault(version => version.id == config.versionId);
            if (current == null)
            {
                EditorGUILayout.HelpBox("The selected project version could not be found.", MessageType.Warning);
                return;
            }

            var statusForVersion = NeoComposeVersionSelectionUtility.FindStatus(current, versionStatuses);
            if (statusForVersion != null)
            {
                using (new EditorGUIUtilityLabelWidthScope(LabelWidth))
                {
                    EditorGUILayout.LabelField("Status", statusForVersion.name);
                }
            }

            if (!NeoComposeVersionSelectionUtility.IsVersionInChannel(
                    current,
                    versionStatuses,
                    config.targetReleaseChannelId))
            {
                var targetedChannels = NeoComposeVersionSelectionUtility.GetTargetReleaseChannelNames(
                    current,
                    versionStatuses,
                    releaseChannels);
                var targets = targetedChannels.Length == 0
                    ? "It is not exposed by any release channel."
                    : "It currently targets: " + string.Join(", ", targetedChannels) + ".";
                EditorGUILayout.HelpBox(
                    "The pinned version is not in the selected release channel. " + targets,
                    MessageType.Warning);
            }

            if (NeoComposeVersionSelectionUtility.IsArchived(current))
            {
                EditorGUILayout.HelpBox("The selected project version is archived.", MessageType.Warning);
            }

            if (NeoComposeVersionSelectionUtility.IsDeprecated(current, versionStatuses))
            {
                EditorGUILayout.HelpBox(
                    "The selected project version is deprecated or no longer channel-targeted.",
                    MessageType.Warning);
            }
        }

        private void RenderUpdateAvailable(NeoComposeConfig config)
        {
            var current = versions.FirstOrDefault(version => version.id == config.versionId);
            var latest = NeoComposeVersionSelectionUtility.SelectLatestVersionForChannel(
                versions,
                versionStatuses,
                config.targetReleaseChannelId);
            if (current == null || latest == null) return;
            if (NeoComposeVersionSelectionUtility.CompareSemver(latest, current) <= 0) return;

            EditorGUILayout.HelpBox("A newer update is available.", MessageType.Info);
            using (new EditorGUI.DisabledScope(loading))
            {
                if (GUILayout.Button("Update to latest version", GUILayout.Width(180)))
                {
                    config.versionId = latest.id;
                    NeoComposeConfigProvider.Save(config);
                    if (EditorUtility.DisplayDialog(
                            "Synchronize latest version?",
                            $"Version {latest.semver.label} is now selected. Synchronize generated files now?",
                            "Synchronize",
                            "Not Now"))
                    {
                        _ = SynchronizeAsync();
                    }
                }
            }
        }

        private bool CanSynchronize(NeoComposeConfig config)
        {
            return config.HasProject &&
                !string.IsNullOrWhiteSpace(config.targetReleaseChannelId) &&
                !string.IsNullOrWhiteSpace(config.versionId);
        }

        private bool CanSaveUnityExportSettings(NeoComposeConfig config)
        {
            return CanSynchronize(config) &&
                NeoComposeVersionSelectionUtility.IsCurrentVersionWritable(
                    config.versionId,
                    versions,
                    versionStatuses);
        }

        private static string FormatVersionOption(NeoComposeProjectVersion version)
        {
            var label = string.IsNullOrWhiteSpace(version.semver.label)
                ? version.id
                : version.semver.label;
            return string.IsNullOrWhiteSpace(version.archivedAt) ? label : label + " (archived)";
        }

        private void DrawDirectoryField(string label, ref string assetDirectory)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            assetDirectory = EditorGUILayout.TextField(label, assetDirectory);
            if (GUILayout.Button("Browse", GUILayout.Width(BrowseButtonWidth)))
            {
                var selected = EditorUtility.OpenFolderPanel(label, Application.dataPath, "");
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    if (TryConvertAbsoluteFolderToAssetPath(selected, out var assetPath))
                    {
                        assetDirectory = assetPath;
                    }
                    else
                    {
                        EditorUtility.DisplayDialog(
                            "Folder must be under Assets",
                            "Choose a folder inside this Unity project's Assets directory.",
                            "OK");
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
            if (EditorGUI.EndChangeCheck() && config != null)
            {
                NeoComposeConfigProvider.Save(config);
            }
        }

        private void DrawProjectSearchResult(NeoComposeConfig config, NeoComposeProjectSummary project)
        {
            EditorGUILayout.BeginVertical(SearchResultStyle());
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.BeginVertical();
            EditorGUILayout.LabelField(project.name, ProjectNameStyle());
            EditorGUILayout.SelectableLabel(
                project.id,
                MutedStyle(),
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndVertical();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Select", GUILayout.Width(72)))
            {
                config.SelectProject(
                    project.id,
                    project.name,
                    project.UnityNamespaceOrDefault(),
                    project.UnitySingletonOrDefault());
                NeoComposeConfigProvider.Save(config);
                _ = RefreshVersionMetadataAsync(true);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static void BeginSection(string title)
        {
            EditorGUILayout.BeginVertical(SectionBoxStyle());
            EditorGUILayout.LabelField(title, SectionTitleStyle());
            EditorGUILayout.Space(1);
        }

        private static void EndSection()
        {
            EditorGUILayout.EndVertical();
        }

        private static GUIStyle TitleStyle()
        {
            return new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
            };
        }

        private static GUIStyle SectionTitleStyle()
        {
            return new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                margin = new RectOffset(0, 0, 0, 1),
            };
        }

        private static GUIStyle ProjectNameStyle()
        {
            return new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
            };
        }

        private static GUIStyle ProjectIdStyle()
        {
            return new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 10,
                wordWrap = false,
                clipping = TextClipping.Clip,
            };
        }

        private static GUIStyle FormLabelStyle()
        {
            return new GUIStyle(EditorStyles.label)
            {
                fontSize = 11,
                normal = { textColor = EditorStyles.miniLabel.normal.textColor },
            };
        }

        private static GUIStyle MutedStyle()
        {
            return new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = true,
            };
        }

        private static GUIStyle SectionBoxStyle()
        {
            return new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 5, 6),
                margin = new RectOffset(0, 0, 2, 3),
            };
        }

        private static GUIStyle SearchResultStyle()
        {
            return new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(7, 7, 5, 5),
                margin = new RectOffset(0, 0, 0, 4),
            };
        }

        private sealed class EditorGUIUtilityLabelWidthScope : IDisposable
        {
            private readonly float previous;

            public EditorGUIUtilityLabelWidthScope(float width)
            {
                previous = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = width;
            }

            public void Dispose()
            {
                EditorGUIUtility.labelWidth = previous;
            }
        }

        private async Task SearchProjectsAsync()
        {
            if (config == null) return;
            loading = true;
            status = "";
            Repaint();

            try
            {
                var query = searchText.Trim().Length > 1 ? searchText.Trim() : null;
                var response = await apiClient.ListProjectsAsync(config.apiBaseUrl, query);
                projects.Clear();
                projects.AddRange(response.projects);
                status = $"Loaded {projects.Count} project(s).";
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                status = exception.Message;
            }
            finally
            {
                loading = false;
                Repaint();
            }
        }

        private async Task RefreshVersionMetadataAsync(bool showStatus)
        {
            if (config == null || !config.HasProject) return;
            loading = true;
            if (showStatus) status = "Loading release channels and versions...";
            Repaint();

            try
            {
                var channelResponse = await apiClient.ListReleaseChannelsAsync(config.apiBaseUrl, config.projectId);
                var versionResponse = await apiClient.ListVersionsAsync(config.apiBaseUrl, config.projectId);
                var statusResponse = await apiClient.ListVersionStatusesAsync(config.apiBaseUrl, config.projectId);

                releaseChannels.Clear();
                releaseChannels.AddRange(channelResponse.channels);
                versions.Clear();
                versions.AddRange(versionResponse.versions);
                versionStatuses.Clear();
                versionStatuses.AddRange(statusResponse.statuses);

                var changed = false;
                if (string.IsNullOrWhiteSpace(config.targetReleaseChannelId))
                {
                    config.targetReleaseChannelId =
                        NeoComposeVersionSelectionUtility.SelectDefaultReleaseChannelId(releaseChannels);
                    changed = !string.IsNullOrWhiteSpace(config.targetReleaseChannelId);
                }

                if (string.IsNullOrWhiteSpace(config.versionId) &&
                    !string.IsNullOrWhiteSpace(config.targetReleaseChannelId))
                {
                    var latest = NeoComposeVersionSelectionUtility.SelectLatestVersionForChannel(
                        versions,
                        versionStatuses,
                        config.targetReleaseChannelId);
                    if (latest != null)
                    {
                        config.versionId = latest.id;
                        changed = true;
                    }
                }

                if (changed)
                {
                    NeoComposeConfigProvider.Save(config);
                }

                if (showStatus)
                {
                    status = $"Loaded {releaseChannels.Count} channel(s) and {versions.Count} version(s).";
                }
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                status = exception.Message;
            }
            finally
            {
                loading = false;
                Repaint();
            }
        }

        private async Task SynchronizeAsync()
        {
            if (config == null || synchronizer == null) return;
            loading = true;
            status = "Synchronizing...";
            Repaint();

            try
            {
                var result = await synchronizer.SynchronizeAsync(config, UpdateProgressStatus);
                RefreshConfigForDisplay();
                if (config != null && config.HasProject)
                {
                    await RefreshVersionMetadataAsync(false);
                }
                status = result.message;
                ScheduleAssetRefresh();
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                status = exception.Message;
            }
            finally
            {
                loading = false;
                clearKeyboardFocusNextGui = true;
                Repaint();
            }
        }

        private async Task SaveUnityExportSettingsAsync()
        {
            if (config == null || projectSettingsUpdater == null) return;
            loading = true;
            status = "Saving Unity export settings...";
            Repaint();

            try
            {
                var result = await projectSettingsUpdater.UpdateUnityExportSettingsAsync(config);
                RefreshConfigForDisplay();
                status = result.message;
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
                status = exception.Message;
            }
            finally
            {
                loading = false;
                clearKeyboardFocusNextGui = true;
                Repaint();
            }
        }

        private static void ScheduleAssetRefresh()
        {
            EditorApplication.delayCall += AssetDatabase.Refresh;
        }

        private void RefreshConfigForDisplay()
        {
            config = NeoComposeConfigProvider.LoadOrCreate();
        }

        private void UpdateProgressStatus(string message)
        {
            status = message;
            Repaint();
        }

        private void ClearKeyboardFocusIfRequested()
        {
            if (!clearKeyboardFocusNextGui) return;

            clearKeyboardFocusNextGui = false;
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
        }

        private static string BuildProjectSchemaUrl(string apiBaseUrl, string projectId, string versionId)
        {
            var root = apiBaseUrl.Trim().TrimEnd('/') + "/projects/" + Uri.EscapeDataString(projectId);
            if (string.IsNullOrWhiteSpace(versionId)) return root;
            return root + "/versions/" + Uri.EscapeDataString(versionId);
        }

        private static bool TryConvertAbsoluteFolderToAssetPath(string absolutePath, out string assetPath)
        {
            var normalized = NeoComposePathUtility.NormalizeSeparators(absolutePath).TrimEnd('/');
            var dataPath = NeoComposePathUtility.NormalizeSeparators(Application.dataPath).TrimEnd('/');
            if (normalized == dataPath)
            {
                assetPath = "Assets";
                return true;
            }

            if (normalized.StartsWith(dataPath + "/", StringComparison.Ordinal))
            {
                assetPath = "Assets" + normalized.Substring(dataPath.Length);
                return true;
            }

            assetPath = "";
            return false;
        }
    }
}
