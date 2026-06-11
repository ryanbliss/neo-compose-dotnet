// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Unity.Editor;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoComposeEditorHotReloadTests
    {
        private sealed class ScriptedConfirmationService : INeoComposeConfirmationService
        {
            public bool Answer = true;
            public int Prompts;

            public bool Confirm(string title, string message, string ok, string cancel)
            {
                Prompts++;
                return Answer;
            }
        }

        private ScriptedConfirmationService confirmation = null!;
        private int syncRuns;
        private bool autoSync;
        private NeoComposeEditorHotReloadController controller = null!;

        [SetUp]
        public void SetUp()
        {
            confirmation = new ScriptedConfirmationService();
            syncRuns = 0;
            autoSync = false;
            controller = new NeoComposeEditorHotReloadController(
                confirmation,
                () =>
                {
                    syncRuns++;
                    return Task.CompletedTask;
                },
                () => autoSync);
        }

        private static NeoComposeExportSignal Signal(string transactionId) =>
            new NeoComposeExportSignal
            {
                versionId = "version-1",
                transactionId = transactionId,
                transactionHash = "hash-" + transactionId,
                transactionAt = 100,
            };

        [Test]
        public void FirstSignalIsTheBaselineAndNeverPrompts()
        {
            controller.HandleSignal(Signal("transaction-1"));

            Assert.That(confirmation.Prompts, Is.Zero);
            Assert.That(syncRuns, Is.Zero);
        }

        [Test]
        public void RepeatedBaselineSignalDoesNothing()
        {
            controller.HandleSignal(Signal("transaction-1"));
            controller.HandleSignal(Signal("transaction-1"));

            Assert.That(confirmation.Prompts, Is.Zero);
            Assert.That(syncRuns, Is.Zero);
        }

        [Test]
        public void RemoteChangePromptsAndSynchronizesOnApproval()
        {
            controller.HandleSignal(Signal("transaction-1"));
            controller.HandleSignal(Signal("transaction-2"));

            Assert.That(confirmation.Prompts, Is.EqualTo(1));
            Assert.That(syncRuns, Is.EqualTo(1));
        }

        [Test]
        public void DeclinedPromptSkipsTheSyncAndIsNotReAskedUntilTheNextChange()
        {
            confirmation.Answer = false;
            controller.HandleSignal(Signal("transaction-1"));
            controller.HandleSignal(Signal("transaction-2"));
            controller.HandleSignal(Signal("transaction-2"));

            Assert.That(confirmation.Prompts, Is.EqualTo(1), "a declined head is not re-asked");
            Assert.That(syncRuns, Is.Zero);

            controller.HandleSignal(Signal("transaction-3"));
            Assert.That(confirmation.Prompts, Is.EqualTo(2), "the next remote change asks again");
        }

        [Test]
        public void AutoSyncSkipsThePrompt()
        {
            autoSync = true;
            controller.HandleSignal(Signal("transaction-1"));
            controller.HandleSignal(Signal("transaction-2"));

            Assert.That(confirmation.Prompts, Is.Zero);
            Assert.That(syncRuns, Is.EqualTo(1));
        }

        [Test]
        public void NullSignalIsIgnored()
        {
            controller.HandleSignal(null);
            controller.HandleSignal(Signal("transaction-1"));
            controller.HandleSignal(null);
            controller.HandleSignal(Signal("transaction-2"));

            Assert.That(confirmation.Prompts, Is.EqualTo(1));
            Assert.That(syncRuns, Is.EqualTo(1));
        }
    }

    public class NeoComposeConvexUrlSyncTests
    {
        [Test]
        public void NullLeavesAHandEnteredUrlAlone()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<NeoComposeConfig>();
            config.convexUrl = "https://hand-entered.convex.cloud";

            NeoComposeSynchronizer.ApplyConvexUrl(config, null);

            Assert.That(config.convexUrl, Is.EqualTo("https://hand-entered.convex.cloud"));
        }

        [Test]
        public void APresentUrlOverwrites()
        {
            var config = UnityEngine.ScriptableObject.CreateInstance<NeoComposeConfig>();
            config.convexUrl = "https://old.convex.cloud";

            NeoComposeSynchronizer.ApplyConvexUrl(config, " https://new.convex.cloud ");

            Assert.That(config.convexUrl, Is.EqualTo("https://new.convex.cloud"));
        }
    }
}
