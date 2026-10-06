// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using NeoCompose.Runtime;
using Newtonsoft.Json.Linq;

namespace HelloWorld.Assets.Tests
{
    internal static class SampleProjectFixture
    {
        internal const string ProjectJsonPath = "Assets/Resources/Neo/project.json";
        internal static readonly string Json = File.ReadAllText(ProjectJsonPath);
        // The source caches project-scoped schema. Tests still own separate stores,
        // saves, clients, and subscriptions; custom-schema tests use their own source.
        internal static readonly NeoJsonProjectDataSource Source = NeoJsonProjectDataSource.FromFile(ProjectJsonPath);

        /// <summary>The export's partition files by index path, as <see cref="NeoJsonProjectDataSource"/> takes them.</summary>
        internal static Dictionary<string, string> PartitionFiles()
        {
            var files = new Dictionary<string, string>();
            foreach ((_, string file) in PartitionIndex())
                files[file] = File.ReadAllText(Path.Combine(Path.GetDirectoryName(ProjectJsonPath)!, file));
            return files;
        }

        /// <summary>Each value partition's rows by partition key; main's key is null.</summary>
        internal static IEnumerable<(string? key, JObject rows)> Partitions()
        {
            Dictionary<string, string> files = PartitionFiles();
            foreach ((string? key, string file) in PartitionIndex())
                yield return (key, JObject.Parse(files[file]));
        }

        private static IEnumerable<(string? key, string file)> PartitionIndex()
        {
            foreach (JToken entry in JObject.Parse(Json)["partitions"]!)
                yield return (entry["key"]!.Value<string?>(), entry["file"]!.Value<string>()!);
        }
    }
}
