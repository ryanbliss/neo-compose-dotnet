// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    public interface INeoComposeEditorExportCache
    {
        NeoComposeUnityExportSyncState? Load(string projectId, string versionId);
        void Save(string projectId, string versionId, NeoComposeUnityExportSyncState state);
        void Delete(string projectId, string versionId);
    }

    /// <summary>
    /// Gitignored editor cache for immutable project snapshots and the last
    /// applied transaction cursor. Runtime assets remain under Assets; cache
    /// state belongs under Library and is rebuilt by a full synchronize when
    /// absent or malformed.
    /// </summary>
    public sealed class NeoComposeEditorExportCache : INeoComposeEditorExportCache
    {
        private readonly Func<string> projectRoot;

        public NeoComposeEditorExportCache()
            : this(() => Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Could not resolve the Unity project root."))
        {
        }

        internal NeoComposeEditorExportCache(Func<string> projectRoot)
        {
            this.projectRoot = projectRoot;
        }

        public NeoComposeUnityExportSyncState? Load(string projectId, string versionId)
        {
            var path = CachePath(projectId, versionId);
            if (!File.Exists(path)) return null;
            try
            {
                var state = JsonConvert.DeserializeObject<NeoComposeUnityExportSyncState>(
                    File.ReadAllText(path));
                if (state == null || state.schemaVersion != 1) return null;
                return state;
            }
            catch (JsonException)
            {
                return null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        public void Save(string projectId, string versionId, NeoComposeUnityExportSyncState state)
        {
            var path = CachePath(projectId, versionId);
            var directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("Could not resolve the export cache directory.");
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonConvert.SerializeObject(state, Formatting.None));
        }

        public void Delete(string projectId, string versionId)
        {
            var path = CachePath(projectId, versionId);
            if (File.Exists(path)) File.Delete(path);
        }

        private string CachePath(string projectId, string versionId)
        {
            return Path.Combine(
                projectRoot(),
                "Library",
                "NeoCompose",
                "ExportCache",
                StableFileName(projectId + "\n" + versionId) + ".json");
        }

        private static string StableFileName(string value)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            var builder = new StringBuilder(hash.Length * 2);
            foreach (var item in hash) builder.Append(item.ToString("x2"));
            return builder.ToString();
        }
    }
}
