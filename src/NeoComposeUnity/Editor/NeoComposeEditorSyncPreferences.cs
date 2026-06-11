// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using UnityEditor;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Per-user (EditorPrefs-backed) synchronization preferences. These are
    /// workflow choices — how chatty the editor is — not project configuration,
    /// so they live beside <c>NeoComposeEditorWindow.AutoSyncPrefKey</c> rather
    /// than on the committed <c>NeoComposeConfig</c> asset.
    /// </summary>
    public static class NeoComposeEditorSyncPreferences
    {
        internal const string AskBeforeOverwritingFilesPrefKey =
            "NeoCompose.EditorWindow.AskBeforeOverwritingFiles";

        /// <summary>
        /// When true, Synchronize asks before replacing existing generated files
        /// and synchronized assets. Off by default: replaced files are fully
        /// regenerable from the web project, so the prompt is opt-in friction.
        /// Applies to both manual Synchronize and auto-sync.
        /// </summary>
        public static bool AskBeforeOverwritingFiles
        {
            get => EditorPrefs.GetBool(AskBeforeOverwritingFilesPrefKey, false);
            set => EditorPrefs.SetBool(AskBeforeOverwritingFilesPrefKey, value);
        }
    }
}
