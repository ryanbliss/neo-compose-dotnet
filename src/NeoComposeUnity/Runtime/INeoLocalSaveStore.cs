// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// The byte-persistence layer beneath the project store: enumerate, read,
    /// write, and delete serialized saves keyed by <c>customId</c>. This is the
    /// async replacement for the old single-file <c>LoadSave</c>/<c>HandleSave</c>
    /// delegates — keying by id lets one device hold many local saves. A folder
    /// implementation over <c>save-{customId}.json</c> ships with the Hello World
    /// sample; the SDK itself only depends on this seam.
    /// </summary>
    /// <remarks>
    /// All operations are asynchronous (<see cref="Awaitable"/>) — there is no
    /// synchronous persistence path anywhere in the save stack.
    /// </remarks>
    public interface INeoLocalSaveStore
    {
        /// <summary>The <c>customId</c>s of every save currently persisted locally.</summary>
        Awaitable<IReadOnlyList<string>> ListSaveIdsAsync();

        /// <summary>The serialized save content for <paramref name="customId"/>, or null when absent.</summary>
        Awaitable<string?> LoadSaveAsync(string customId);

        /// <summary>Writes (creating or overwriting) the serialized save content.</summary>
        Awaitable CommitSaveAsync(string customId, string content);

        /// <summary>Removes the locally-persisted save, if present.</summary>
        Awaitable DeleteSaveAsync(string customId);
    }

    /// <summary>
    /// An in-memory <see cref="INeoLocalSaveStore"/>. Useful as a default for
    /// transient/local-only play and as a deterministic test double. Not durable
    /// across process restarts — durable persistence is the Hello World folder
    /// store's job.
    /// </summary>
    public sealed class NeoInMemoryLocalSaveStore : INeoLocalSaveStore
    {
        private readonly Dictionary<string, string> saves = new();

        public Awaitable<IReadOnlyList<string>> ListSaveIdsAsync()
        {
            IReadOnlyList<string> ids = new List<string>(saves.Keys);
            return NeoAwaitable.FromResult(ids);
        }

        public Awaitable<string?> LoadSaveAsync(string customId)
        {
            saves.TryGetValue(customId, out var content);
            return NeoAwaitable.FromResult(content);
        }

        public Awaitable CommitSaveAsync(string customId, string content)
        {
            saves[customId] = content;
            return NeoAwaitable.Completed();
        }

        public Awaitable DeleteSaveAsync(string customId)
        {
            saves.Remove(customId);
            return NeoAwaitable.Completed();
        }
    }
}
