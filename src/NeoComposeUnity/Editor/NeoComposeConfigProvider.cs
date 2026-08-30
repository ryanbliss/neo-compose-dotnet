// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    public static class NeoComposeConfigProvider
    {
        public static NeoComposeConfig LoadOrCreate(
            string defaultPath = NeoComposeEditorDefaults.ConfigPath,
            string[]? searchFolders = null)
        {
            var paths = FindConfigPaths(searchFolders);
            if (paths.Length > 0)
            {
                if (paths.Length > 1)
                {
                    Debug.LogWarning(
                        "Multiple Neo Compose config assets were found; using " +
                        paths[0] +
                        ". Move or delete duplicate config assets if this is not the intended project config.");
                }

                var existingConfig = AssetDatabase.LoadAssetAtPath<NeoComposeConfig>(paths[0]);
                if (existingConfig == null)
                {
                    throw new InvalidOperationException($"Neo Compose config could not be loaded at {paths[0]}.");
                }

                return existingConfig;
            }

            EnsureAssetDirectory(Path.GetDirectoryName(defaultPath) ?? "Assets");
            var config = ScriptableObject.CreateInstance<NeoComposeConfig>();
            AssetDatabase.CreateAsset(config, defaultPath);
            AssetDatabase.SaveAssets();
            return config;
        }

        public static string[] FindConfigPaths(string[]? searchFolders = null)
        {
            string[] guids;
            if (searchFolders == null)
            {
                guids = AssetDatabase.FindAssets("t:NeoComposeConfig");
            }
            else
            {
                var existingFolders = searchFolders.Where(AssetDatabase.IsValidFolder).ToArray();
                if (existingFolders.Length == 0) return Array.Empty<string>();
                guids = AssetDatabase.FindAssets("t:NeoComposeConfig", existingFolders);
            }

            return guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Persists the committed config asset. An ephemeral rig overlay
        /// (<see cref="NeoComposeResolvedConfig"/>) is refused instead: its values
        /// belong to a disposable deployment and must never reach the checked-in
        /// asset, so this is a logged no-op rather than a write (P53 §6).
        /// </summary>
        public static void Save(NeoComposeConfig config)
        {
            if (NeoComposeResolvedConfig.IsRigOverlay(config))
            {
                NeoComposeResolvedConfig.ReportSaveRefused(config);
                return;
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
        }

        private static void EnsureAssetDirectory(string assetDirectory)
        {
            var normalized = NeoComposePathUtility.NormalizeSeparators(assetDirectory).TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalized)) return;

            var current = "Assets";
            foreach (var segment in normalized.Split('/').Skip(1))
            {
                var next = $"{current}/{segment}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, segment);
                }

                current = next;
            }
        }
    }
}
