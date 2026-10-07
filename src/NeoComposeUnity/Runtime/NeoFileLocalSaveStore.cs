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
    /// All operations are async to match the save stack; commits write on a
    /// background thread and the rest complete synchronously. Developers on
    /// platforms without file IO can supply their own
    /// <see cref="INeoLocalSaveStore"/> instead.
    /// </remarks>
    public sealed class NeoFileLocalSaveStore : INeoLocalSaveStore
    {
        private const string FilePrefix = "save-";
        private const string FileExtension = ".json";

        private readonly string directory;
        private readonly object writeGate = new();

        /// <summary>The directory containing this store's save files.</summary>
        public string DirectoryPath => directory;

        // Content committed but not yet on disk, by customId. Reads see it
        // immediately, and a background write lands only while its content is
        // still the newest, so writes and deletes keep their call order.
        private readonly Dictionary<string, string> pendingWrites = new();

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
            lock (writeGate)
            {
                IEnumerable<string> written = Directory.Exists(directory)
                    ? Directory
                        .GetFiles(directory, $"{FilePrefix}*{FileExtension}")
                        .Select(path => Path.GetFileNameWithoutExtension(path).Substring(FilePrefix.Length))
                    : Enumerable.Empty<string>();
                IReadOnlyList<string> ids = written.Union(pendingWrites.Keys).ToList();
                return NeoAwaitable.FromResult(ids);
            }
        }

        public Awaitable<string?> LoadSaveAsync(string customId)
        {
            string path = PathFor(customId);
            lock (writeGate)
            {
                if (pendingWrites.TryGetValue(customId, out string? pending))
                    return NeoAwaitable.FromResult<string?>(pending);
                return NeoAwaitable.FromResult<string?>(File.Exists(path) ? File.ReadAllText(path) : null);
            }
        }

        /// <summary>
        /// Writes on a background thread and completes on the main thread,
        /// so a large save never stalls a frame. A later commit or delete of
        /// the same save supersedes a write still in flight.
        /// </summary>
        public async Awaitable CommitSaveAsync(string customId, string content)
        {
            string path = PathFor(customId);
            lock (writeGate)
                pendingWrites[customId] = content;
            await Awaitable.BackgroundThreadAsync();
            try
            {
                lock (writeGate)
                {
                    if (pendingWrites.TryGetValue(customId, out string? newest)
                        && ReferenceEquals(newest, content))
                    {
                        pendingWrites.Remove(customId);
                        File.WriteAllText(path, content);
                    }
                }
            }
            finally
            {
                await Awaitable.MainThreadAsync();
            }
        }

        public Awaitable DeleteSaveAsync(string customId)
        {
            string path = PathFor(customId);
            lock (writeGate)
            {
                pendingWrites.Remove(customId);
                if (File.Exists(path))
                    File.Delete(path);
            }
            return NeoAwaitable.Completed();
        }
    }
}
