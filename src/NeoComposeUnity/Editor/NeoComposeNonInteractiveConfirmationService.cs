// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Confirmation service for headless synchronization: it approves every
    /// prompt and logs what it approved instead of opening a modal dialog.
    /// </summary>
    /// <remarks>
    /// A modal dialog in <c>-batchmode</c> either throws or hangs the run, so the
    /// headless entry point must never reach
    /// <see cref="UnityEditor.EditorUtility.DisplayDialog(string, string, string, string)"/>.
    /// Approving is safe because every prompt the synchronize pipeline raises is
    /// about regenerable synchronized output (generated C#, <c>project.json</c>,
    /// localization, file assets) or about continuing past diagnostics that the
    /// log already carries — never about developer-authored files.
    /// </remarks>
    public sealed class NeoComposeNonInteractiveConfirmationService : INeoComposeConfirmationService
    {
        /// <summary>Prefix shared with the headless entry point's log markers.</summary>
        private const string LogPrefix = NeoComposeBatchSync.LogPrefix;

        public bool Confirm(string title, string message, string ok, string cancel)
        {
            Debug.Log($"{LogPrefix} auto-confirmed \"{title}\": {message}");
            return true;
        }

        public bool ConfirmReplaceFiles(string title, string message, string ok, string cancel)
        {
            Debug.Log($"{LogPrefix} auto-confirmed file replacement \"{title}\": {message}");
            return true;
        }
    }
}
