// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading.Tasks;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Hot-reload decision logic, kept UI-free so it is unit-testable: the
    /// first signal after subscribing is the baseline (a Convex subscription
    /// always pushes the current value immediately — that is not a remote
    /// change); each subsequent new head transaction prompts through the same
    /// confirmation seam as the manual sync button, or synchronizes without
    /// asking when auto-sync is enabled.
    /// </summary>
    public sealed class NeoComposeEditorHotReloadController
    {
        private readonly INeoComposeConfirmationService confirmation;
        private readonly Func<Task> synchronize;
        private readonly Func<bool> autoSyncEnabled;
        private string? baselineTransactionId;

        public NeoComposeEditorHotReloadController(
            INeoComposeConfirmationService confirmation,
            Func<Task> synchronize,
            Func<bool> autoSyncEnabled)
        {
            this.confirmation = confirmation
                ?? throw new ArgumentNullException(nameof(confirmation));
            this.synchronize = synchronize
                ?? throw new ArgumentNullException(nameof(synchronize));
            this.autoSyncEnabled = autoSyncEnabled
                ?? throw new ArgumentNullException(nameof(autoSyncEnabled));
        }

        public void HandleSignal(NeoComposeExportSignal? signal)
        {
            // No transactions yet — nothing to baseline or sync.
            if (signal == null) return;

            if (baselineTransactionId == null)
            {
                baselineTransactionId = signal.transactionId;
                return;
            }

            if (signal.transactionId == baselineTransactionId) return;

            // Advance the baseline before prompting so a declined sync is not
            // re-asked until the next remote change.
            baselineTransactionId = signal.transactionId;

            if (!autoSyncEnabled())
            {
                var approved = confirmation.Confirm(
                    "Neo Compose",
                    "Remote changes were saved to this version. Synchronize now?",
                    "Synchronize",
                    "Not now");
                if (!approved) return;
            }

            RunSynchronize();
        }

        private async void RunSynchronize()
        {
            try
            {
                await synchronize();
            }
            catch (Exception exception)
            {
                // The sync path reports its own status; this guard only keeps an
                // unobserved exception out of the async-void boundary.
                Debug.LogError(exception);
            }
        }
    }
}
