// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// User-facing save/runtime operations exposed by generated Neo Compose
    /// project facades.
    /// </summary>
    public interface INeoClient : IDisposable
    {
        NeoAttributeCustom AssetsRoot { get; }

        NeoAttributeCustomWritable SaveRoot { get; }

        NeoAttributeCustomWritable SessionRoot { get; }

        NeoLocalization Localization { get; }

        /// <summary>
        /// The active-save abstraction this client persists through (normally a
        /// <see cref="NeoSaveSynchronizer"/>).
        /// </summary>
        INeoSaveLoader Synchronizer { get; }

        /// <summary>The cloud save transport, or null when local-only.</summary>
        INeoApiClient? ApiClient { get; }

        /// <summary>The runtime authentication backing cloud sync, or null when local-only.</summary>
        NeoAuthentication? Authentication { get; }

        /// <summary>
        /// Serializes the current save state to JSON without persisting it.
        /// </summary>
        /// <returns>The current save file JSON.</returns>
        string SerializeSaveData();

        /// <summary>
        /// Commits the current save state through the active save loader (local, and
        /// cloud when sync is configured). Logs a warning when generated factory
        /// values exist in the save file but are not linked from the save tree.
        /// </summary>
        /// <param name="replaceSnapshot">
        /// When true, overwrites the head snapshot in place instead of appending a new
        /// one (cloud path).
        /// </param>
        Awaitable CommitAsync(bool replaceSnapshot = false);

        /// <summary>
        /// Deletes save-side values that are not reachable from the save tree.
        /// </summary>
        /// <returns>The number of unlinked root values removed.</returns>
        int RunGarbageCollector();

        /// <summary>
        /// Finds save-side values that are not reachable from the save tree.
        /// </summary>
        /// <returns>Unlinked save value ids.</returns>
        IReadOnlyList<string> FindUnlinkedSaveValueIds();
    }
}
