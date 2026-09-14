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
        private readonly Dictionary<string, string?> metadata = new();
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
            public string id = "";
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
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var manifest = new Manifest { schemaVersion = SchemaVersion, projectId = projectId };
            var previous = ReadManifest(assets, ManifestPath);
            var previousFiles = previous?.files ?? new List<Entry>();
            var previousById = previousFiles.ToDictionary(file => file.id);
            var previousPaths = new HashSet<string>(previousFiles.Select(file => file.path), StringComparer.OrdinalIgnoreCase);
            foreach (var file in files.OrderBy(file => file.path, StringComparer.Ordinal))
            {
                ValidatePath(file.path);
                if (!expected.Add(file.path))
                    throw new InvalidOperationException($"Duplicate generated C# path: {file.path}");
                if (string.IsNullOrWhiteSpace(file.id))
                    throw new InvalidOperationException($"Missing generated C# identity: {file.path}");
                if (!identities.Add(file.id))
                    throw new InvalidOperationException($"Duplicate generated C# identity: {file.id}");
                if (string.IsNullOrWhiteSpace(file.content))
                    throw new InvalidOperationException($"The export returned empty generated C#: {file.path}");
                manifest.files.Add(new Entry { id = file.id, path = file.path, hash = Hash(file.content) });
            }
            var incomingById = manifest.files.ToDictionary(file => file.id);
            foreach (var old in previousFiles)
            {
                if (previous?.projectId == projectId && incomingById.TryGetValue(old.id, out var next) && next.path == old.path) continue;
                deletes.Add(PathFor(old.path));
            }
            var deletedPaths = new HashSet<string>(deletes, StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var path = PathFor(file.path);
                if (previous?.projectId == projectId && previousById.TryGetValue(file.id, out var old) && old.path != file.path)
                {
                    if (!previousPaths.Contains(file.path) && (assets.FileExists(path) || assets.FileExists(path + ".meta")))
                        throw new InvalidOperationException($"Generated rename destination is not owned by the manifest: {file.path}");
                    var oldMeta = PathFor(old.path) + ".meta";
                    metadata[path] = assets.FileExists(oldMeta) ? assets.ReadAllText(oldMeta) : null;
                }
                if (deletedPaths.Contains(path) || !assets.FileExists(path) || assets.ReadAllText(path) != file.content)
                    writes.Add(new NeoComposeGeneratedFile { id = file.id, path = path, content = file.content });
            }
            var monolith = PathFor(NeoComposeEditorDefaults.GeneratedTypesFileName);
            if (assets.FileExists(monolith) || assets.FileExists(monolith + ".meta")) deletes.Add(monolith);
            manifestContent = JsonConvert.SerializeObject(manifest, Formatting.Indented) + "\n";
        }

        public IEnumerable<string> ReplacedPaths => deletes.Concat(writes.Select(file => file.path)).Concat(metadata.Keys);

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
            // Prefer the original spelling for case-only renames on every filesystem.
            var backups = new Dictionary<string, (string path, string? content, string? meta)>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in ReplacedPaths.Append(ManifestPath))
            {
                if (!backups.ContainsKey(path)) backups[path] = (path, Read(path), Read(path + ".meta"));
            }
            assets.BeginAssetEditing();
            try
            {
                // Clear all old names before assigning destinations, including name swaps.
                foreach (var path in deletes)
                {
                    changes.Add(path);
                    assets.DeleteAsset(path);
                    if (assets.FileExists(path) || assets.FileExists(path + ".meta"))
                        throw new IOException($"Generated asset deletion left files behind: {path}");
                }
                foreach (var file in writes)
                {
                    changes.Add(file.path);
                    assets.EnsureDirectory(Path.GetDirectoryName(file.path)!.Replace('\\', '/'));
                    if (metadata.TryGetValue(file.path, out var meta)) RestoreText(file.path + ".meta", meta);
                    assets.WriteAllText(file.path, file.content);
                }
                if (backups[ManifestPath].content != manifestContent)
                {
                    changes.Add(ManifestPath);
                    assets.EnsureDirectory(directory);
                    assets.WriteAllText(ManifestPath, manifestContent);
                }
            }
            catch
            {
                var restored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in changes.AsEnumerable().Reverse())
                {
                    if (!restored.Add(path)) continue;
                    var original = backups[path];
                    if (original.path != path || original.content == null)
                    {
                        if (assets.FileExists(path) || assets.FileExists(path + ".meta")) assets.DeleteAsset(path);
                    }
                    RestoreText(original.path, original.content);
                    RestoreText(original.path + ".meta", original.meta);
                }
                throw;
            }
            finally { assets.EndAssetEditing(); }
            return writes.Select(file => file.path).ToArray();
        }

        private string? Read(string path) => assets.FileExists(path) ? assets.ReadAllText(path) : null;

        private void RestoreText(string path, string? content)
        {
            if (Read(path) == content) return;
            if (content == null) assets.DeleteAsset(path);
            else assets.WriteAllText(path, content);
        }

        private string PathFor(string path) => NeoComposePathUtility.CombineAssetPath(directory, path);

        private static Manifest? ReadManifest(INeoComposeEditorAssetService assets, string path)
        {
            if (!assets.FileExists(path)) return null;
            var manifest = JsonConvert.DeserializeObject<Manifest>(assets.ReadAllText(path));
            if (manifest == null || manifest.schemaVersion != SchemaVersion || manifest.files == null || manifest.files.Count == 0)
                throw new InvalidOperationException($"Invalid generated-file manifest: {path}");
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var file in manifest.files)
            {
                if (file == null) throw new InvalidOperationException($"Invalid generated-file manifest entry: {path}");
                if (string.IsNullOrWhiteSpace(file.id)) throw new InvalidOperationException($"Invalid generated-file manifest identity: {path}");
                if (!identities.Add(file.id)) throw new InvalidOperationException($"Duplicate generated-file manifest identity: {file.id}");
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
