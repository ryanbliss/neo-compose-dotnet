// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// The default durable <see cref="INeoLocalSaveStore"/>: a folder of
    /// <c>save-{customId}.json</c> files. This is what <see cref="NeoProjectStore"/>
    /// uses when no local store is supplied, writing under
    /// <see cref="Application.persistentDataPath"/>.
    /// </summary>
    /// <remarks>
    /// One file per save keyed by <c>customId</c>, so the store can manage many saves.
    /// All operations are async to match the save stack, though this file-backed
    /// implementation completes synchronously. Developers on platforms without file
    /// IO can supply their own <see cref="INeoLocalSaveStore"/> instead.
    /// </remarks>
    public sealed class NeoFileLocalSaveStore : INeoLocalSaveStore
    {
        private const string FilePrefix = "save-";
        private const string FileExtension = ".json";

        private readonly string directory;

        /// <param name="directory">
        /// The folder saves are written to; defaults to
        /// <see cref="Application.persistentDataPath"/> when null or empty.
        /// </param>
        public NeoFileLocalSaveStore(string? directory = null)
        {
            this.directory = string.IsNullOrWhiteSpace(directory)
                ? Application.persistentDataPath
                : directory!;
            Directory.CreateDirectory(this.directory);
        }

        private string PathFor(string customId) =>
            Path.Combine(directory, $"{FilePrefix}{customId}{FileExtension}");

        public Awaitable<IReadOnlyList<string>> ListSaveIdsAsync()
        {
            IReadOnlyList<string> ids = Directory.Exists(directory)
                ? Directory
                    .GetFiles(directory, $"{FilePrefix}*{FileExtension}")
                    .Select(path => Path.GetFileNameWithoutExtension(path).Substring(FilePrefix.Length))
                    .ToList()
                : new List<string>();
            return NeoAwaitable.FromResult(ids);
        }

        public Awaitable<string?> LoadSaveAsync(string customId)
        {
            string path = PathFor(customId);
            return NeoAwaitable.FromResult<string?>(File.Exists(path) ? File.ReadAllText(path) : null);
        }

        public Awaitable CommitSaveAsync(string customId, string content)
        {
            File.WriteAllText(PathFor(customId), content);
            return NeoAwaitable.Completed();
        }

        public Awaitable DeleteSaveAsync(string customId)
        {
            string path = PathFor(customId);
            if (File.Exists(path)) File.Delete(path);
            return NeoAwaitable.Completed();
        }
    }
}
