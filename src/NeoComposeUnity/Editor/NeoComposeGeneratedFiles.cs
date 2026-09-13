// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace NeoCompose.Unity.Editor
{
    internal sealed class NeoComposeGeneratedFiles
    {
        internal const string ManifestFileName = "NeoGeneratedFiles.json";
        private const int SchemaVersion = 1;
        private readonly INeoComposeEditorAssetService assets;
        private readonly string directory;
        private readonly List<NeoComposeGeneratedFile> writes = new();
        private readonly List<string> deletes = new();
        private readonly string manifestContent;
        private string ManifestPath => PathFor(ManifestFileName);

        private sealed class Manifest
        {
            public int schemaVersion;
            public string projectId = "";
            public List<Entry> files = new();
        }

        private sealed class Entry
        {
            public string path = "";
            public string hash = "";
        }

        public NeoComposeGeneratedFiles(INeoComposeEditorAssetService assets,
            string directory, string projectId, IReadOnlyList<NeoComposeGeneratedFile> files)
        {
            this.assets = assets;
            this.directory = directory;
            if (files.Count == 0)
                throw new InvalidOperationException("The full export returned empty generated C#. Update the Neo Compose server and synchronize again.");
            var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var manifest = new Manifest { schemaVersion = SchemaVersion, projectId = projectId };
            foreach (var file in files.OrderBy(file => file.path, StringComparer.Ordinal))
            {
                ValidatePath(file.path);
                if (!expected.Add(file.path))
                    throw new InvalidOperationException($"Duplicate generated C# path: {file.path}");
                if (string.IsNullOrWhiteSpace(file.content))
                    throw new InvalidOperationException($"The export returned empty generated C#: {file.path}");
                manifest.files.Add(new Entry { path = file.path, hash = Hash(file.content) });
                var path = PathFor(file.path);
                if (!assets.FileExists(path) || assets.ReadAllText(path) != file.content)
                    writes.Add(new NeoComposeGeneratedFile { path = path, content = file.content });
            }
            var previous = ReadManifest(assets, ManifestPath);
            if (previous != null)
            {
                foreach (var file in previous.files)
                    if (!expected.Contains(file.path) && assets.FileExists(PathFor(file.path)))
                        deletes.Add(PathFor(file.path));
            }
            var monolith = PathFor(NeoComposeEditorDefaults.GeneratedTypesFileName);
            if (assets.FileExists(monolith)) deletes.Add(monolith);
            manifestContent = JsonConvert.SerializeObject(manifest, Formatting.Indented) + "\n";
        }

        public IEnumerable<string> ReplacedPaths => writes.Select(file => file.path).Concat(deletes);

        // The manifest is local integrity evidence, independent of runtime compatibility hashes.
        public static bool IsCurrent(INeoComposeEditorAssetService assets, string directory, string projectId)
        {
            try
            {
                if (assets.FileExists(NeoComposePathUtility.CombineAssetPath(directory,
                    NeoComposeEditorDefaults.GeneratedTypesFileName))) return false;
                var manifest = ReadManifest(assets,
                    NeoComposePathUtility.CombineAssetPath(directory, ManifestFileName));
                if (manifest == null || manifest.projectId != projectId) return false;
                return manifest.files.All(file =>
                {
                    var path = NeoComposePathUtility.CombineAssetPath(directory, file.path);
                    return assets.FileExists(path) && Hash(assets.ReadAllText(path)) == file.hash;
                });
            }
            catch (JsonException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (IOException) { return false; }
        }

        public IReadOnlyList<string> Apply()
        {
            var changes = new List<string>();
            var backups = new Dictionary<string, string?>();
            // Snapshot the entire plan before any mutation, including Unity GUIDs for deletions.
            foreach (var path in ReplacedPaths.Append(ManifestPath).Distinct(StringComparer.Ordinal))
            {
                backups[path] = assets.FileExists(path) ? assets.ReadAllText(path) : null;
                backups[path + ".meta"] = assets.FileExists(path + ".meta") ? assets.ReadAllText(path + ".meta") : null;
            }
            assets.BeginAssetEditing();
            try
            {
                foreach (var file in writes)
                {
                    assets.EnsureDirectory(Path.GetDirectoryName(file.path)!.Replace('\\', '/'));
                    changes.Add(file.path + ".meta");
                    changes.Add(file.path);
                    assets.WriteAllText(file.path, file.content);
                }
                foreach (var path in deletes)
                {
                    changes.Add(path + ".meta");
                    changes.Add(path);
                    assets.DeleteAsset(path);
                    if (assets.FileExists(path) || assets.FileExists(path + ".meta"))
                        throw new IOException($"Generated asset deletion left files behind: {path}");
                }
                if (backups[ManifestPath] != manifestContent)
                {
                    assets.EnsureDirectory(directory);
                    changes.Add(ManifestPath + ".meta");
                    changes.Add(ManifestPath);
                    assets.WriteAllText(ManifestPath, manifestContent);
                }
            }
            catch
            {
                foreach (var path in changes.AsEnumerable().Reverse())
                {
                    var content = backups[path];
                    if (content == null)
                    {
                        if (assets.FileExists(path)) assets.DeleteAsset(path);
                    }
                    else if (!assets.FileExists(path) || assets.ReadAllText(path) != content)
                        assets.WriteAllText(path, content);
                }
                throw;
            }
            finally { assets.EndAssetEditing(); }
            return writes.Select(file => file.path).ToArray();
        }

        private string PathFor(string path) => NeoComposePathUtility.CombineAssetPath(directory, path);

        private static Manifest? ReadManifest(INeoComposeEditorAssetService assets, string path)
        {
            if (!assets.FileExists(path)) return null;
            var manifest = JsonConvert.DeserializeObject<Manifest>(assets.ReadAllText(path));
            if (manifest == null || manifest.schemaVersion != SchemaVersion || manifest.files == null || manifest.files.Count == 0)
                throw new InvalidOperationException($"Invalid generated-file manifest: {path}");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in manifest.files)
            {
                if (file == null) throw new InvalidOperationException($"Invalid generated-file manifest entry: {path}");
                ValidatePath(file.path);
                if (!paths.Add(file.path)) throw new InvalidOperationException($"Duplicate generated-file manifest path: {file.path}");
            }
            return manifest;
        }

        private static void ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Generated/", StringComparison.Ordinal) || !path.EndsWith(".g.cs", StringComparison.Ordinal))
                throw new InvalidOperationException($"Invalid generated C# path: {path}");
            foreach (var segment in path.Split('/'))
            {
                if (segment.Length == 0 || segment == "." || segment == "..")
                    throw new InvalidOperationException($"Invalid generated C# path segment: {path}");
                if (segment.Any(character => !(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z')
                    && !(character >= '0' && character <= '9') && character != '-' && character != '_' && character != '.' && character != '%'))
                    throw new InvalidOperationException($"Invalid generated C# path character: {path}");
            }
        }

        private static string Hash(string content)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(new UTF8Encoding(false).GetBytes(content))).Replace("-", "").ToLowerInvariant();
        }
    }
}
