// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Test corpora stay one readable document: the export's
    /// <c>project.json</c> fields plus <c>values</c> (main's rows) and an
    /// optional <c>valuePartitions</c> object of named partitions' rows. This
    /// splits one into the files <c>neo export</c> writes (P100 §4).
    /// </summary>
    internal static class NeoTestExport
    {
        /// <summary>The corpus's <c>project.json</c> and partition files, keyed by index path.</summary>
        internal static (string projectJson, Dictionary<string, string> partitionJsonByFile) Split(string corpusJson)
        {
            JObject root = JObject.Parse(corpusJson);
            var partitionJsonByFile = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [NeoProjectExportContract.MainPartitionFile] = (root["values"] as JObject ?? new JObject()).ToString(Formatting.None),
            };
            var index = new JArray
            {
                new JObject
                {
                    ["key"] = null,
                    ["file"] = NeoProjectExportContract.MainPartitionFile,
                },
            };
            if (root["valuePartitions"] is JObject partitions)
            {
                foreach (JProperty partition in partitions.Properties())
                {
                    string file = $"Partitions/partition-{index.Count}.json";
                    index.Add(new JObject
                    {
                        ["key"] = partition.Name,
                        ["file"] = file,
                    });
                    partitionJsonByFile[file] = partition.Value.ToString(Formatting.None);
                }
            }
            root.Remove("values");
            root.Remove("valuePartitions");
            root["partitions"] = index;
            return (root.ToString(Formatting.None), partitionJsonByFile);
        }

        internal static NeoJsonProjectDataSource Source(string corpusJson)
        {
            var (projectJson, partitionJsonByFile) = Split(corpusJson);
            return new NeoJsonProjectDataSource(projectJson, partitionJsonByFile);
        }

        internal static ProjectData Read(string corpusJson) => Source(corpusJson).ReadProjectData();

        /// <summary>
        /// Named partitions over in-memory rows keyed by partition key, for
        /// schemas built in code. Each file read is recorded in <paramref name="reads"/>.
        /// </summary>
        internal static NeoValuePartitions Partitions(
            IReadOnlyDictionary<string, JToken> rowsByKey,
            List<string>? reads = null)
        {
            var fileByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in rowsByKey.Keys)
            {
                fileByKey.Add(key, key);
            }
            return new NeoValuePartitions(fileByKey, file =>
            {
                reads?.Add(file);
                return rowsByKey[file].ToString(Formatting.None);
            });
        }

        /// <summary>Writes the split export into <paramref name="directory"/> as <c>neo export</c> lays it out.</summary>
        internal static void Write(string directory, string corpusJson)
        {
            var (projectJson, partitionJsonByFile) = Split(corpusJson);
            Directory.CreateDirectory(Path.Combine(directory, "Partitions"));
            File.WriteAllText(Path.Combine(directory, "project.json"), projectJson);
            foreach (var pair in partitionJsonByFile)
            {
                File.WriteAllText(Path.Combine(directory, pair.Key), pair.Value);
            }
        }
    }
}
