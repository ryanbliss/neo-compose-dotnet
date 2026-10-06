// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using NeoCompose.Runtime;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Reads the <c>metadata.localExport</c> stamp a local <c>neo export</c>
    /// writes into <c>project.json</c> (P101 §4).
    /// </summary>
    internal static class NeoComposeLocalExport
    {
        private static string cachedPath = "";
        private static DateTime cachedWriteTime;
        private static bool cachedLocal;
        private static string cachedWorkspaceRoot = "";

        /// <summary>
        /// Whether the <c>project.json</c> in <paramref name="config"/>'s
        /// directory is a local export. Rereads only when the file changes,
        /// so the window can ask on every repaint.
        /// </summary>
        internal static bool IsLocal(string projectRoot, NeoComposeConfig config) =>
            TryGetLocalWorkspace(projectRoot, config, out _);

        /// <summary>
        /// <see cref="IsLocal"/>, plus the workspace the local export came
        /// from, as its sidecar names it.
        /// </summary>
        internal static bool TryGetLocalWorkspace(
            string projectRoot,
            NeoComposeConfig config,
            out string workspaceRoot)
        {
            string path = Path.Combine(
                projectRoot,
                NeoComposePathUtility.CombineAssetPath(
                    config.projectJsonDirectory,
                    NeoComposeEditorDefaults.ProjectJsonFileName));
            workspaceRoot = "";
            if (!File.Exists(path))
                return false;
            DateTime writeTime = File.GetLastWriteTimeUtc(path);
            if (path != cachedPath || writeTime != cachedWriteTime)
            {
                cachedLocal = ReadLocalExport(path);
                cachedWorkspaceRoot = cachedLocal
                    ? NeoComposeExportIngest.ReadSidecar(projectRoot)?.workspaceRoot ?? ""
                    : "";
                cachedPath = path;
                cachedWriteTime = writeTime;
            }

            workspaceRoot = cachedWorkspaceRoot;
            return cachedLocal;
        }

        /// <summary>Reads only <c>metadata</c>, which the CLI writes first.</summary>
        private static bool ReadLocalExport(string path)
        {
            using var reader = new JsonTextReader(File.OpenText(path));
            if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                return false;
            while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
            {
                if ((string?)reader.Value != "metadata")
                {
                    reader.Skip();
                    continue;
                }

                reader.Read();
                return JObject.Load(reader).Value<bool?>("localExport") == true;
            }

            return false;
        }
    }
}
