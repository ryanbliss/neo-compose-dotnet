// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// An export's named value partitions (P100 §9): <c>project.json</c>'s
    /// partition index plus a reader for the files it names. A partition's
    /// file is read, expanded and typed once, on its first load, and the
    /// typed rows are cached here, so every client built over the same
    /// <see cref="ProjectData"/> (one per opened save) reuses them.
    /// </summary>
    internal sealed class NeoValuePartitions
    {
        /// <summary>No named partitions: the default for a schema built in code.</summary>
        internal static readonly NeoValuePartitions Empty = new(
            new Dictionary<string, string>(StringComparer.Ordinal),
            file => throw new FileNotFoundException("The export has no value partitions.", file));

        private readonly IReadOnlyDictionary<string, string> fileByKey;
        private readonly Func<string, string> readPartitionJson;
        private readonly Dictionary<string, NeoLoadedValuePartition> loaded = new(StringComparer.Ordinal);

        /// <param name="fileByKey">Each named partition's file, relative to project.json's directory.</param>
        /// <param name="readPartitionJson">Reads one partition file. Called on the main thread.</param>
        internal NeoValuePartitions(
            IReadOnlyDictionary<string, string> fileByKey,
            Func<string, string> readPartitionJson)
        {
            this.fileByKey = fileByKey;
            this.readPartitionJson = readPartitionJson;
        }

        /// <summary>The named partition keys. Main has no key.</summary>
        internal IEnumerable<string> Keys => fileByKey.Keys;

        /// <summary>The number of named partitions, main excluded.</summary>
        internal int Count => fileByKey.Count;

        internal bool Contains(string mapKey) => fileByKey.ContainsKey(mapKey);

        /// <summary>The partition's cached rows, when some client already loaded it.</summary>
        internal bool TryGetLoaded(
            string mapKey,
            [MaybeNullWhen(false)] out NeoLoadedValuePartition partition) =>
            loaded.TryGetValue(mapKey, out partition);

        /// <summary>
        /// The partition's typed rows, reading its file on the first call.
        /// <paramref name="mapKey"/> must be in the index.
        /// </summary>
        internal NeoLoadedValuePartition Load(string mapKey)
        {
            if (loaded.TryGetValue(mapKey, out NeoLoadedValuePartition partition))
                return partition;

            string file = fileByKey[mapKey];
            JObject physical = NeoInterningJsonReader.Deserialize<JObject>(readPartitionJson(file))
                ?? throw new InvalidOperationException(
                    $"Value partition '{mapKey}' file '{file}' is empty. {NeoProjectExportContract.RegenerateAction}");
            // P76 §5: a partition's rows expand on their way in, as main's do.
            JObject logical = NeoPackedValue.Expand(physical, $"value partition '{mapKey}'");
            Dictionary<string, MemberValue> rows = logical.ToObject<Dictionary<string, MemberValue>>()!;
            foreach (var pair in rows)
            {
                MemberValue row = pair.Value;
                if (row.id != pair.Key)
                {
                    throw new InvalidOperationException(
                        $"Value partition '{mapKey}' row keyed '{pair.Key}' carries mismatched id '{row.id}'.");
                }
                if (!string.IsNullOrEmpty(row.mapKey) && row.mapKey != mapKey)
                {
                    throw new InvalidOperationException(
                        $"Value partition '{mapKey}' row '{row.id}' is stamped with a different partition '{row.mapKey}'.");
                }
                // Partition residency is the stamp's source of truth —
                // self-heal a missing per-row stamp.
                row.mapKey = mapKey;
            }

            partition = new NeoLoadedValuePartition(rows);
            loaded.Add(mapKey, partition);
            return partition;
        }

        internal static NeoValuePartitions FromIndex(JToken? index, Func<string, string> readPartitionJson)
        {
            var fileByKey = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach ((string? key, string file) in ReadIndex(index))
            {
                if (key is null)
                    continue;
                if (fileByKey.ContainsKey(key))
                {
                    throw new JsonSerializationException(
                        $"Project export lists value partition '{key}' twice in its 'partitions' index. {NeoProjectExportContract.RegenerateAction}");
                }
                fileByKey.Add(key, file);
            }
            return new NeoValuePartitions(fileByKey, readPartitionJson);
        }

        /// <summary>
        /// Every file <paramref name="projectJson"/>'s partition index names,
        /// main included, without building the rest of the document. Empty
        /// when there is no index, so the schema gate reports an older export.
        /// </summary>
        internal static IEnumerable<string> ReadIndexFiles(string projectJson)
        {
            using var reader = new JsonTextReader(new StringReader(projectJson));
            if (!reader.Read() || reader.TokenType != JsonToken.StartObject)
                yield break;
            while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
            {
                bool isIndex = (string?)reader.Value == "partitions";
                reader.Read();
                if (!isIndex)
                {
                    reader.Skip();
                    continue;
                }
                foreach (var entry in ReadIndex(JToken.ReadFrom(reader)))
                    yield return entry.file;
                yield break;
            }
        }

        private static IEnumerable<(string? key, string file)> ReadIndex(JToken? index)
        {
            if (index is not JArray entries)
            {
                throw new JsonSerializationException(
                    $"Project export has no 'partitions' index. {NeoProjectExportContract.RegenerateAction}");
            }
            foreach (JToken token in entries)
            {
                if (token is not JObject entry)
                {
                    throw new JsonSerializationException(
                        $"Project export partition index entry {token.ToString(Formatting.None)} is not an object.");
                }
                if (entry["file"] is not { Type: JTokenType.String } file)
                {
                    throw new JsonSerializationException(
                        $"Project export partition index entry {token.ToString(Formatting.None)} needs a string 'file'.");
                }
                if (entry["key"] is not { Type: JTokenType.String or JTokenType.Null } key)
                {
                    throw new JsonSerializationException(
                        $"Project export partition index entry {token.ToString(Formatting.None)} needs a string or null 'key'.");
                }
                string path = file.Value<string>()!;
                if (key.Type == JTokenType.Null && path != NeoProjectExportContract.MainPartitionFile)
                {
                    throw new JsonSerializationException(
                        $"Project export names its main partition file '{path}', but this SDK reads '{NeoProjectExportContract.MainPartitionFile}'. Update the NeoCompose SDK.");
                }
                yield return (key.Value<string>(), path);
            }
        }
    }

    /// <summary>One named partition's typed rows, shared by every client over its <see cref="ProjectData"/>.</summary>
    internal sealed class NeoLoadedValuePartition
    {
        internal NeoLoadedValuePartition(IReadOnlyDictionary<string, MemberValue> rows)
        {
            Rows = rows;
            RowIds = new HashSet<string>(rows.Keys, StringComparer.Ordinal);
        }

        internal IReadOnlyDictionary<string, MemberValue> Rows
        {
            get;
        }

        /// <summary>The partition's row ids. Shared across clients, so never mutated.</summary>
        internal HashSet<string> RowIds
        {
            get;
        }

        /// <summary>
        /// Set once the rows pass read-only validation, which then runs once
        /// per <see cref="ProjectData"/> however many clients load them.
        /// </summary>
        internal bool Validated;
    }
}
