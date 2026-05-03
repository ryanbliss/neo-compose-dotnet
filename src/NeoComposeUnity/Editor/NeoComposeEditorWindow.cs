// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
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

            BeginSection("Export Settings");
            using (new EditorGUIUtilityLabelWidthScope(LabelWidth))
            {
                EditorGUI.BeginChangeCheck();
                config.namespaceForGeneratedTypes = EditorGUILayout.TextField(
                    "Unity Namespace",
                    config.namespaceForGeneratedTypes);
                if (EditorGUI.EndChangeCheck())
                {
                    NeoComposeConfigProvider.Save(config);
                }
            }

            EditorGUILayout.Space(3);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(loading))
            {
                if (GUILayout.Button("Save to web", GUILayout.Width(120), GUILayout.Height(24)))
                {
                    _ = SaveUnityNamespaceAsync();
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
            }

            EndSection();

            BeginSection("Project Assets");
            EditorGUILayout.LabelField(
                "Writes NeoGeneratedTypes.cs and project.json to the configured folders.",
                MutedStyle());
            EditorGUILayout.Space(3);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(loading))
            {
                if (GUILayout.Button(loading ? "Synchronizing..." : "Synchronize", GUILayout.Width(160), GUILayout.Height(26)))
                {
                    _ = SynchronizeAsync();
                }
            }

            if (GUILayout.Button("Edit in web", GUILayout.Width(120), GUILayout.Height(26)))
            {
                Application.OpenURL(BuildProjectSchemaUrl(config.apiBaseUrl, config.projectId));
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EndSection();
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

        private static void DrawProjectSearchResult(NeoComposeConfig config, NeoComposeProjectSummary project)
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
                config.SelectProject(project.id, project.name, project.UnityNamespaceOrDefault());
                NeoComposeConfigProvider.Save(config);
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

        private async Task SynchronizeAsync()
        {
            if (config == null || synchronizer == null) return;
            loading = true;
            status = "Synchronizing...";
            Repaint();

            var result = await synchronizer.SynchronizeAsync(config);
            RefreshConfigForDisplay();
            status = result.message;
            loading = false;
            clearKeyboardFocusNextGui = true;
            Repaint();
        }

        private async Task SaveUnityNamespaceAsync()
        {
            if (config == null || projectSettingsUpdater == null) return;
            loading = true;
            status = "Saving Unity export settings...";
            Repaint();

            var result = await projectSettingsUpdater.UpdateUnityNamespaceAsync(config);
            RefreshConfigForDisplay();
            status = result.message;
            loading = false;
            clearKeyboardFocusNextGui = true;
            Repaint();
        }

        private void RefreshConfigForDisplay()
        {
            config = NeoComposeConfigProvider.LoadOrCreate();
        }

        private void ClearKeyboardFocusIfRequested()
        {
            if (!clearKeyboardFocusNextGui) return;

            clearKeyboardFocusNextGui = false;
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
        }

        private static string BuildProjectSchemaUrl(string apiBaseUrl, string projectId)
        {
            return apiBaseUrl.Trim().TrimEnd('/') + "/projects/" + Uri.EscapeDataString(projectId);
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
