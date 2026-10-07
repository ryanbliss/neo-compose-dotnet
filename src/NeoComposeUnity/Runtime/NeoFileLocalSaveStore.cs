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

        // Content committed but not yet on disk, by file path. Reads see it
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

        // User keys sit beside the saves as {key}.json. ListSaveIdsAsync reads
        // only save-* files, so they never list as saves.
        private string UserPathFor(string key) =>
            Path.Combine(directory, $"{key}{FileExtension}");

        private static string? SaveIdFor(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name.StartsWith(FilePrefix, System.StringComparison.Ordinal)
                ? name.Substring(FilePrefix.Length)
                : null;
        }

        public Awaitable<IReadOnlyList<string>> ListSaveIdsAsync()
        {
            lock (writeGate)
            {
                IEnumerable<string> written = Directory.Exists(directory)
                    ? Directory.GetFiles(directory, $"{FilePrefix}*{FileExtension}")
                    : Enumerable.Empty<string>();
                IReadOnlyList<string> ids = written
                    .Union(pendingWrites.Keys)
                    .Select(SaveIdFor)
                    .OfType<string>()
                    .Distinct()
                    .ToList();
                return NeoAwaitable.FromResult(ids);
            }
        }

        public Awaitable<string?> LoadSaveAsync(string customId) => Load(PathFor(customId));

        /// <summary>
        /// Writes on a background thread and completes on the main thread,
        /// so a large save never stalls a frame. A later commit or delete of
        /// the same save supersedes a write still in flight.
        /// </summary>
        public Awaitable CommitSaveAsync(string customId, string content) =>
            Write(PathFor(customId), content);

        public Awaitable DeleteSaveAsync(string customId)
        {
            string path = PathFor(customId);
            lock (writeGate)
            {
                pendingWrites.Remove(path);
                if (File.Exists(path))
                    File.Delete(path);
            }
            return NeoAwaitable.Completed();
        }

        public Awaitable<string?> LoadUserAsync(string key) => Load(UserPathFor(key));

        public Awaitable CommitUserAsync(string key, string content) =>
            Write(UserPathFor(key), content);

        private Awaitable<string?> Load(string path)
        {
            lock (writeGate)
            {
                if (pendingWrites.TryGetValue(path, out string? pending))
                    return NeoAwaitable.FromResult<string?>(pending);
                return NeoAwaitable.FromResult<string?>(File.Exists(path) ? File.ReadAllText(path) : null);
            }
        }

        private async Awaitable Write(string path, string content)
        {
            lock (writeGate)
                pendingWrites[path] = content;
            await Awaitable.BackgroundThreadAsync();
            try
            {
                lock (writeGate)
                {
                    if (pendingWrites.TryGetValue(path, out string? newest)
                        && ReferenceEquals(newest, content))
                    {
                        pendingWrites.Remove(path);
                        File.WriteAllText(path, content);
                    }
                }
            }
            finally
            {
                await Awaitable.MainThreadAsync();
            }
        }
    }
}
