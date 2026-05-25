// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

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
        /// Serializes the current save state to JSON without invoking the
        /// configured save handler.
        /// </summary>
        /// <returns>The current save file JSON.</returns>
        string SerializeSaveData();

        /// <summary>
        /// Commits the current save state through the configured save handler.
        /// Logs a warning when generated factory values exist in the save file
        /// but are not linked from the save tree.
        /// </summary>
        void Commit();

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
