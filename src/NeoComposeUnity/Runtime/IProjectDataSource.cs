// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Supplies the authored project schema JSON to a <see cref="NeoProjectStore"/>.
    /// The read is asynchronous even for the local default so the API is identical
    /// when a later pass swaps in a cloud schema fetch — this is what lets the
    /// generated client drop its <c>projectJson</c> argument and read schema from
    /// the store instead.
    /// </summary>
    public interface IProjectDataSource
    {
        Awaitable<string> ReadProjectJsonAsync();

        /// <summary>
        /// Reads one partition file named by project.json's partition index,
        /// relative to project.json's directory. Main thread only:
        /// <c>Resources.Load</c> requires it, and
        /// <see cref="NeoClient.LoadValuePartition"/> runs inside gameplay code.
        /// </summary>
        string ReadPartitionJson(string file);
    }

    /// <summary>
    /// Optional fast path for project data sources that can safely reuse an already
    /// parsed schema. A project schema is shared by every save opened from a project
    /// store, so callers should treat the returned object as project-scoped data.
    /// </summary>
    internal interface IParsedProjectDataSource
    {
        Awaitable<ProjectData> ReadProjectDataAsync();
    }

    /// <summary>
    /// A project data source backed by in-hand JSON: project.json plus the
    /// partition files its index names, keyed by their index path. Useful for
    /// tests, editor tools, and callers that already hold the export. Reusing
    /// one instance across project stores also reuses its parsed,
    /// project-scoped schema.
    /// </summary>
    public sealed class NeoJsonProjectDataSource : IProjectDataSource, IParsedProjectDataSource
    {
        private readonly string projectJson;
        private readonly IReadOnlyDictionary<string, string> partitionJsonByFile;
        private readonly object parseLock = new object();
        private ProjectData? parsedProjectData;

        public NeoJsonProjectDataSource(
            string projectJson,
            IReadOnlyDictionary<string, string> partitionJsonByFile)
        {
            if (string.IsNullOrWhiteSpace(projectJson))
            {
                throw new ArgumentException("Project JSON cannot be empty.", nameof(projectJson));
            }

            this.projectJson = projectJson;
            this.partitionJsonByFile = partitionJsonByFile
                ?? throw new ArgumentNullException(nameof(partitionJsonByFile));
        }

        /// <summary>
        /// Reads project.json at the path and every partition file its index
        /// names, from disk. For editor tools.
        /// </summary>
        public static NeoJsonProjectDataSource FromFile(string projectJsonPath)
        {
            string projectJson = File.ReadAllText(projectJsonPath);
            string directory = Path.GetDirectoryName(Path.GetFullPath(projectJsonPath))!;
            var partitionJsonByFile = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string file in NeoValuePartitions.ReadIndexFiles(projectJson))
            {
                partitionJsonByFile[file] = File.ReadAllText(Path.Combine(directory, file));
            }
            return new NeoJsonProjectDataSource(projectJson, partitionJsonByFile);
        }

        public Awaitable<string> ReadProjectJsonAsync() => NeoAwaitable.FromResult(projectJson);

        public string ReadPartitionJson(string file)
        {
            if (!partitionJsonByFile.TryGetValue(file, out string? json))
            {
                throw new FileNotFoundException(
                    $"Neo Compose partition file '{file}' was not supplied with the project JSON. {NeoProjectExportContract.RegenerateAction}",
                    file);
            }
            return json;
        }

        Awaitable<ProjectData> IParsedProjectDataSource.ReadProjectDataAsync() =>
            NeoAwaitable.FromResult(ReadProjectData());

        internal ProjectData ReadProjectData()
        {
            lock (parseLock)
            {
                parsedProjectData ??= ProjectDataConverter.Read(
                    projectJson,
                    partitionJsonByFile.TryGetValue(NeoProjectExportContract.MainPartitionFile, out string? main)
                        ? main
                        : null,
                    ReadPartitionJson);
                return parsedProjectData;
            }
        }
    }

    /// <summary>
    /// The config-driven default: loads the project JSON from a
    /// <see cref="TextAsset"/> under <c>Resources</c> at the path configured in
    /// <see cref="NeoComposeConfig.projectJsonDirectory"/>. Asynchronous so it is
    /// shape-compatible with a future cloud fetch. In players with thread
    /// support, parsing runs in the background so the loading UI can keep
    /// updating. Reusing a source reuses its parsed project schema.
    /// </summary>
    public sealed class NeoResourcesProjectDataSource : IProjectDataSource, IParsedProjectDataSource
    {
        private readonly string resourcePath;
        private Task<ProjectData>? parsedProjectDataTask;

        async Awaitable<ProjectData> IParsedProjectDataSource.ReadProjectDataAsync()
        {
            if (parsedProjectDataTask == null)
            {
                // Resource access stays on Unity's main thread. Only the managed
                // JSON conversion runs in the pool; awaiting Task returns to the
                // caller's Unity context before the store touches runtime state.
                string projectJson = await ReadProjectJsonAsync();
                string? mainPartitionJson = LoadPartition(NeoProjectExportContract.MainPartitionFile)?.text;
                ProjectData Parse() => ProjectDataConverter.Read(
                    projectJson,
                    mainPartitionJson,
                    ReadPartitionJson);
#if !UNITY_WEBGL || UNITY_EDITOR
                if (Application.isPlaying)
                    parsedProjectDataTask = Task.Run(Parse);
                else
#endif
                    parsedProjectDataTask = Task.FromResult(Parse());
            }
            return await parsedProjectDataTask;
        }

        public NeoResourcesProjectDataSource(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                throw new ArgumentException("Resource path cannot be empty.", nameof(resourcePath));
            }

            this.resourcePath = resourcePath;
        }

        /// <summary>
        /// Builds the default source from config: the project JSON lives under the
        /// configured <see cref="NeoComposeConfig.projectJsonDirectory"/> in
        /// Resources, addressed by the project name file Neo Compose writes.
        /// </summary>
        public static NeoResourcesProjectDataSource FromConfig(NeoComposeConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            var directory = ToResourcesRelativeDirectory(config.projectJsonDirectory);
            var path = string.IsNullOrEmpty(directory)
                ? NeoComposeDefaults.ProjectJsonResourceName
                : $"{directory}/{NeoComposeDefaults.ProjectJsonResourceName}";
            return new NeoResourcesProjectDataSource(path);
        }

        /// <summary>
        /// Converts a project-relative asset directory (e.g.
        /// <c>Assets/Resources/Neo</c>) into a <c>Resources.Load</c> path (e.g.
        /// <c>Neo</c>) by dropping the leading <c>Assets/Resources/</c> segment.
        /// </summary>
        private static string ToResourcesRelativeDirectory(string? directory)
        {
            var normalized = (directory ?? "").Replace('\\', '/').Trim('/');
            const string prefix = "Assets/Resources/";
            if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(prefix.Length).Trim('/');
            }

            return normalized;
        }

        public Awaitable<string> ReadProjectJsonAsync()
        {
            var asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Neo Compose project JSON was not found in Resources at \"{resourcePath}\". " +
                    "Run `neo pull` and `neo export` so its export is bundled, or pass an explicit " +
                    "IProjectDataSource.");
            }

            return NeoAwaitable.FromResult(asset.text);
        }

        public string ReadPartitionJson(string file)
        {
            TextAsset? asset = LoadPartition(file);
            if (asset == null)
            {
                throw new InvalidOperationException(
                    $"Neo Compose partition file '{file}' was not found in Resources beside \"{resourcePath}\". {NeoProjectExportContract.RegenerateAction}");
            }
            return asset.text;
        }

        /// <summary>Loads a partition file beside project.json; Resources paths drop <c>.json</c>.</summary>
        private TextAsset? LoadPartition(string file)
        {
            int slash = resourcePath.LastIndexOf('/');
            string directory = slash < 0 ? "" : resourcePath.Substring(0, slash + 1);
            return Resources.Load<TextAsset>(directory + Path.ChangeExtension(file, null));
        }
    }
}
