// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
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
    /// A project data source backed by an in-hand JSON string. Useful for tests,
    /// custom pipelines, and callers that already hold the export. Reusing one
    /// instance across project stores also reuses its parsed, project-scoped schema.
    /// </summary>
    public sealed class NeoJsonProjectDataSource : IProjectDataSource, IParsedProjectDataSource
    {
        private readonly string projectJson;
        private readonly object parseLock = new object();
        private ProjectData? parsedProjectData;

        public NeoJsonProjectDataSource(string projectJson)
        {
            if (string.IsNullOrWhiteSpace(projectJson))
            {
                throw new ArgumentException("Project JSON cannot be empty.", nameof(projectJson));
            }

            this.projectJson = projectJson;
        }

        public Awaitable<string> ReadProjectJsonAsync() => NeoAwaitable.FromResult(projectJson);

        Awaitable<ProjectData> IParsedProjectDataSource.ReadProjectDataAsync()
        {
            lock (parseLock)
            {
                parsedProjectData ??= JsonConvert.DeserializeObject<ProjectData>(projectJson)
                    ?? throw new InvalidOperationException(
                        "Neo Compose project JSON could not be deserialized.");
                return NeoAwaitable.FromResult(parsedProjectData);
            }
        }
    }

    /// <summary>
    /// The config-driven default: loads the project JSON from a
    /// <see cref="TextAsset"/> under <c>Resources</c> at the path configured in
    /// <see cref="NeoComposeConfig.projectJsonDirectory"/>. Asynchronous so it is
    /// shape-compatible with a future cloud fetch.
    /// </summary>
    public sealed class NeoResourcesProjectDataSource : IProjectDataSource
    {
        private readonly string resourcePath;

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
            if (config == null) throw new ArgumentNullException(nameof(config));
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
                    "Synchronize the project so its export is bundled, or pass an explicit " +
                    "IProjectDataSource / project JSON.");
            }

            return NeoAwaitable.FromResult(asset.text);
        }
    }
}
