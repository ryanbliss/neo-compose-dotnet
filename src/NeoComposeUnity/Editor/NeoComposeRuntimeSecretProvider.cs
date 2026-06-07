// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Editor-side lifecycle for the gitignored <see cref="NeoComposeRuntimeSecret"/>
    /// asset: locate/create it next to the project config, persist edits, and keep a
    /// per-directory <c>.gitignore</c> excluding it so the runtime API key is bundled
    /// into builds but never committed.
    /// </summary>
    public static class NeoComposeRuntimeSecretProvider
    {
        /// <summary>
        /// Ensures the secret asset exists (next to the config) and that a sibling
        /// <c>.gitignore</c> excludes it. Called by the synchronize flow so a freshly
        /// linked project is git-safe without any manual setup.
        /// </summary>
        public static NeoComposeRuntimeSecret EnsureAssetAndGitignore()
        {
            var secret = LoadOrCreate();
            EnsureGitignore(SecretDirectory());
            return secret;
        }

        /// <summary>Loads the secret asset, creating an empty one next to the config when absent.</summary>
        public static NeoComposeRuntimeSecret LoadOrCreate()
        {
            var existing = Find();
            if (existing != null) return existing;

            var directory = SecretDirectory();
            EnsureAssetDirectory(directory);
            var path = $"{directory}/{NeoComposeEditorDefaults.RuntimeSecretFileName}";
            var secret = ScriptableObject.CreateInstance<NeoComposeRuntimeSecret>();
            AssetDatabase.CreateAsset(secret, path);
            AssetDatabase.SaveAssets();
            return secret;
        }

        /// <summary>The existing secret asset, or null when none has been created.</summary>
        public static NeoComposeRuntimeSecret? Find()
        {
            var guids = AssetDatabase.FindAssets("t:NeoComposeRuntimeSecret");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var secret = AssetDatabase.LoadAssetAtPath<NeoComposeRuntimeSecret>(path);
                if (secret != null) return secret;
            }

            return null;
        }

        public static string LoadRuntimeApiKey() => Find()?.RuntimeApiKey ?? "";

        public static void Save(NeoComposeRuntimeSecret secret)
        {
            EditorUtility.SetDirty(secret);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The asset-path directory the secret lives in (mirrors the config's folder).</summary>
        private static string SecretDirectory()
        {
            var configPaths = NeoComposeConfigProvider.FindConfigPaths();
            var configPath = configPaths.Length > 0 ? configPaths[0] : NeoComposeEditorDefaults.ConfigPath;
            var directory = Path.GetDirectoryName(configPath);
            return NeoComposePathUtility.NormalizeSeparators(
                string.IsNullOrEmpty(directory) ? "Assets/Resources/Neo" : directory);
        }

        /// <summary>
        /// Writes (idempotently) a <c>.gitignore</c> in the secret's directory that
        /// excludes the asset and its <c>.meta</c>. Per-directory <c>.gitignore</c>
        /// files are standard git — patterns apply relative to the file's directory.
        /// </summary>
        private static void EnsureGitignore(string assetDirectory)
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (projectRoot == null) return;

            var directoryFullPath = Path.Combine(projectRoot, assetDirectory);
            Directory.CreateDirectory(directoryFullPath);
            var gitignorePath = Path.Combine(directoryFullPath, ".gitignore");

            var assetEntry = NeoComposeEditorDefaults.RuntimeSecretFileName;
            var lines = File.Exists(gitignorePath)
                ? File.ReadAllLines(gitignorePath).ToList()
                : new List<string>();
            if (lines.Any(line => line.Trim() == assetEntry)) return; // already ignored

            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add("# Neo Compose runtime secret — bundled into builds, never committed.");
            lines.Add(assetEntry);
            lines.Add(assetEntry + ".meta");
            File.WriteAllText(gitignorePath, string.Join("\n", lines) + "\n");
            AssetDatabase.ImportAsset(gitignorePath.Substring(projectRoot.Length + 1).Replace('\\', '/'));
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
