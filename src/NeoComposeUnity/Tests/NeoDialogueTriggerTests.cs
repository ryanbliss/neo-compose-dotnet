// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.IO;
using System;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoDialogueTriggerTests
    {
        private const string ProjectId = "dialogue-project";
        private const string Now = "1970-01-01T00:00:00.000Z";
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        private static string LoadFixture(string fileName)
        {
            return File.ReadAllText(Path.Combine(PackageRoot, fileName));
        }

        [Test]
        public void TryTrigger_WithDialogueId_ReturnsDialogue()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);

            Assert.IsTrue(root.TryTrigger("dialogue-direct", out NeoDialogueTriggerResult result));

            Assert.IsTrue(result.Ok);
            Assert.IsNotNull(result.Dialogue);
            Assert.AreEqual("dialogue-direct", result.Dialogue!.Id);
            Assert.AreEqual("A Direct Dialogue", result.Dialogue.Name);
            Assert.IsNull(result.Dialogue.Description);
            Assert.AreEqual("group-standard", result.Dialogue.GroupId);
            Assert.AreEqual(NeoDialogueState.Created, result.Dialogue.State);
            Assert.IsFalse(result.Dialogue.IsStarted);
        }

        [Test]
        public void NeoDialogueReference_Bound_TryTrigger_ReturnsDialogue()
        {
            var client = CreateClient();
            // Constructing the dialogues API registers it with the client, so a
            // client-bound reference can reach the trigger machinery.
            _ = new TestDialogues(client);

            var reference = new NeoDialogueReference(client, "dialogue-direct");

            Assert.IsTrue(reference.TryTrigger(out NeoDialogue dialogue));
            Assert.AreEqual("dialogue-direct", dialogue.Id);
        }

        [Test]
        public void NeoDialogueReference_Unbound_TryTrigger_Throws()
        {
            // The id-only ctor produces an unbound reference (for assignment).
            var reference = new NeoDialogueReference("dialogue-direct");

            Assert.Throws<InvalidOperationException>(
                () => reference.TryTrigger(out NeoDialogue _));
        }

        [Test]
        public void TryTrigger_LocalizesDialogueDescription()
        {
            var client = CreateClientWithLocalization(
                new Dictionary<string, string?>
                {
                    ["text-dialogue-description"] = "Localized description",
                });
            client.dialogues["dialogue-direct"].description = "text-dialogue-description";
            var root = new TestDialogues(client);

            Assert.IsTrue(root.TryTrigger("dialogue-direct", out NeoDialogueTriggerResult result));

            Assert.AreEqual("text-dialogue-description", result.Dialogue!.DescriptionTextId);
            Assert.AreEqual("Localized description", result.Dialogue.Description);
        }

        [Test]
        public void TextNode_LocalizesTextBeforeNeoVariableInterpolation()
        {
            var client = CreateClientWithLocalization(
                new Dictionary<string, string?>
                {
                    ["text-localized-node"] = "Take {{neo-var:item-name}}.",
                });
            var dialogue = client.dialogues["dialogue-text-variable-primary"];
            ((NeoCompose.Runtime.Json.DialogueTextNode)dialogue.nodes["text-variable-primary"]).text =
                "text-localized-node";
            var root = new TestDialogues(
                client,
                valueResolver: valueId => ResolveClientValue(client, valueId));

            Assert.IsTrue(root.TryTrigger("dialogue-text-variable-primary", out NeoDialogue triggered));
            NeoDialogueTextNode? shown = null;
            triggered.OnShow += node => shown = node;

            triggered.Start();

            Assert.AreEqual("Take Compass.", shown!.Text);
        }

        [Test]
        public void TextOption_LocalizesTextBeforeNeoVariableInterpolation()
        {
            var client = CreateClientWithLocalization(
                new Dictionary<string, string?>
                {
                    ["text-localized-option"] = "Grab {{neo-var:item-name}}",
                });
            var dialogue = client.dialogues["dialogue-option-variable-primary"];
            var node = (NeoCompose.Runtime.Json.DialogueTextNode)dialogue.nodes["text-option-variable"];
            node.optionSettings!.options[0].text = "text-localized-option";
            var root = new TestDialogues(
                client,
                valueResolver: valueId => ResolveClientValue(client, valueId));

            Assert.IsTrue(root.TryTrigger("dialogue-option-variable-primary", out NeoDialogue triggered));
            NeoDialogueTextNode? shown = null;
            triggered.OnShow += node => shown = node;

            triggered.Start();

            Assert.AreEqual("Grab Compass", shown!.Options[0].Text);
        }

        [Test]
        public void StandardGroupTryTrigger_ReturnsFirstDialogueInGroup()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            var group = new TestStandardDialogueGroup(root, "group-standard");

            Assert.IsTrue(group.TryTrigger(out NeoDialogueTriggerResult result));

            Assert.IsTrue(result.Ok);
            Assert.IsNotNull(result.Dialogue);
            Assert.AreEqual("dialogue-direct", result.Dialogue!.Id);
            Assert.AreEqual("group-standard", result.Dialogue.Context.GroupId);
        }

        [Test]
        public void StandardGroupTryTrigger_PrefersHigherPriorityBucket()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            var group = new TestStandardDialogueGroup(root, "group-priority");

            Assert.IsTrue(group.TryTrigger(out NeoDialogueTriggerResult result));

            Assert.IsTrue(result.Ok);
            Assert.IsNotNull(result.Dialogue);
            Assert.AreEqual("dialogue-priority-high", result.Dialogue!.Id);
        }

        [Test]
        public void StandardGroupTryTrigger_PrefersLowerVisitCount()
        {
            var client = CreateClient();
            var memory = new TestMemoryStore();
            memory.GetOrCreateTestDialogueMemory("dialogue-visit-a").VisitCount = 2;
            var root = new TestDialogues(client, memoryStore: memory);
            var group = new TestStandardDialogueGroup(root, "group-visits");

            Assert.IsTrue(group.TryTrigger(out NeoDialogueTriggerResult result));

            Assert.IsTrue(result.Ok);
            Assert.IsNotNull(result.Dialogue);
            Assert.AreEqual("dialogue-visit-b", result.Dialogue!.Id);
        }

        [Test]
        public void DialoguesBase_VisitCountAndHasVisited_ReadMemoryPointers()
        {
            var client = CreateClient();
            var memory = new TestMemoryStore();
            var dialogueMemory = memory.GetOrCreateTestDialogueMemory("dialogue-visited");
            dialogueMemory.VisitCount = 2;
            var textNodeMemory = (TestTextNodeMemory)dialogueMemory
                .GetOrCreateTextNodeMemory("text-visited");
            textNodeMemory.VisitCount = 3;
            textNodeMemory.AddChoice("option-visited", Now);
            var root = new TestDialogues(client, memoryStore: memory);

            Assert.AreEqual(2, root.VisitCount("dialogue-visited"));
            Assert.AreEqual(3, root.VisitCount("dialogue-visited,text-visited"));
            Assert.AreEqual(1, root.VisitCount("dialogue-visited,text-visited,option-visited"));
            Assert.IsTrue(root.HasVisited("dialogue-visited"));
            Assert.IsTrue(root.HasVisited("dialogue-visited,text-visited"));
            Assert.IsTrue(root.HasVisited("dialogue-visited,text-visited,option-visited"));
            Assert.AreEqual(0, root.VisitCount("dialogue-visited,,option-visited"));
            Assert.IsFalse(root.HasVisited("missing"));
        }

        [Test]
        public void LookupGroupTryTrigger_FiltersByLookupValueId()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            var group = new TestLookupDialogueGroup(root, "group-lookup");

            Assert.IsTrue(group.TryTrigger(new TestLookupValue("lookup-value-b"), out NeoDialogueTriggerResult result));

            Assert.IsTrue(result.Ok);
            Assert.IsNotNull(result.Dialogue);
            Assert.AreEqual("dialogue-lookup-b", result.Dialogue!.Id);
            Assert.AreEqual("lookup-value-b", ((TestLookupValue)result.Dialogue.Context.Trigger!).valueId);
            Assert.IsNull(result.Dialogue.Context.Primary);
        }

        [Test]
        public void LookupGroupTryTrigger_AcceptsDerivedLookupValue()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            var group = new TestLookupDialogueGroup(root, "group-lookup");

            Assert.IsTrue(group.TryTrigger(new DerivedTestLookupValue("lookup-value-b"), out NeoDialogueTriggerResult result));

            Assert.IsTrue(result.Ok);
            Assert.IsNotNull(result.Dialogue);
            Assert.AreEqual("dialogue-lookup-b", result.Dialogue!.Id);
            Assert.IsInstanceOf<DerivedTestLookupValue>(result.Dialogue.Context.Trigger);
        }

        [Test]
        public void LookupGroupTryTrigger_RequiresValueReferenceId()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            var group = new TestLookupDialogueGroup(root, "group-lookup");

            Assert.IsFalse(group.TryTrigger(new TestLookupValue(null), out NeoDialogueTriggerResult result));

            Assert.IsFalse(result.Ok);
            Assert.IsNotNull(result.Error);
            StringAssert.Contains("requires a value with a Neo value id", result.Error!.Message);
        }

        [Test]
        public void TryTrigger_WithFalseCondition_ReturnsNotFound()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);

            Assert.IsFalse(root.TryTrigger("dialogue-condition-false", out NeoDialogueTriggerResult result));

            Assert.IsFalse(result.Ok);
            Assert.IsNull(result.Dialogue);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void TryTrigger_WithOccurrenceLimitAtVisitCount_ReturnsNotFound()
        {
            var client = CreateClient();
            var memory = new TestMemoryStore();
            memory.GetOrCreateTestDialogueMemory("dialogue-limited").VisitCount = 1;
            var root = new TestDialogues(client, memoryStore: memory);

            Assert.IsFalse(root.TryTrigger("dialogue-limited", out NeoDialogueTriggerResult result));

            Assert.IsFalse(result.Ok);
            Assert.IsNull(result.Dialogue);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void TryTrigger_EvaluatesInheritedGroupConditions()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);

            Assert.IsFalse(root.TryTrigger("dialogue-parent-condition", out NeoDialogueTriggerResult result));

            Assert.IsFalse(result.Ok);
            Assert.IsNull(result.Dialogue);
            Assert.IsNull(result.Error);
        }

        [Test]
        public void TryTrigger_DirectLookupDialogueRequiresStoredLookupValue()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);

            Assert.IsFalse(root.TryTrigger("dialogue-lookup-a", out NeoDialogueTriggerResult result));

            Assert.IsFalse(result.Ok);
            Assert.IsNotNull(result.Error);
            StringAssert.Contains("references missing lookup value", result.Error!.Message);
        }

        [Test]
        public void TryTrigger_DirectLookupDialogueResolvesStoredLookupValue()
        {
            var client = CreateClient();
            var root = new TestDialogues(
                client,
                valueResolver: valueId => new TestLookupValue(valueId));

            Assert.IsTrue(root.TryTrigger("dialogue-lookup-direct", out NeoDialogueTriggerResult result));

            Assert.IsTrue(result.Ok);
            Assert.IsNotNull(result.Dialogue);
            Assert.IsInstanceOf<TestLookupValue>(result.Dialogue!.Context.Trigger);
            Assert.AreEqual("lookup-value-direct", ((TestLookupValue)result.Dialogue.Context.Trigger!).valueId);
            Assert.IsNull(result.Dialogue.Context.Primary);
        }

        [Test]
        public void TryTrigger_ResolvesLinkedValues()
        {
            var client = CreateClient();
            var root = new TestDialogues(
                client,
                valueResolver: valueId => new TestLookupValue(valueId));

            Assert.IsTrue(root.TryTrigger("dialogue-linked-values", out NeoDialogueTriggerResult result));

            Assert.IsTrue(result.Ok);
            Assert.IsNotNull(result.Dialogue);
            Assert.IsTrue(result.Dialogue!.Context.LinkedValues.TryGetValue(
                "linked-value-a",
                out object? linked));
            Assert.IsInstanceOf<TestLookupValue>(linked);
            Assert.AreEqual("linked-value-a", ((TestLookupValue)linked!).valueId);
        }

        [Test]
        public void TryTrigger_ExposesDialogueMetadataToContextConditions()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);

            Assert.IsTrue(root.TryTrigger("dialogue-context-condition", out NeoDialogueTriggerResult result));

            Assert.IsTrue(result.Ok);
            Assert.IsNotNull(result.Dialogue);
            Assert.AreEqual("dialogue-context-condition", result.Dialogue!.Id);
        }

        [Test]
        public void TryTrigger_EvaluatesGroupConditionsWithDialoguePrimaryInContext()
        {
            var client = CreateClient();
            var root = new TestDialogues(
                client,
                valueResolver: valueId => new TestLookupValue(valueId));

            Assert.IsTrue(root.TryTrigger("dialogue-group-context-primary", out NeoDialogueTriggerResult result));

            Assert.IsTrue(result.Ok);
            Assert.IsNotNull(result.Dialogue);
            Assert.IsInstanceOf<TestLookupValue>(result.Dialogue!.Context.Primary);
            Assert.AreEqual("primary-dialogue", ((TestLookupValue)result.Dialogue.Context.Primary!).valueId);
        }

        [Test]
        public void LookupTriggerConditions_FallBackToRuntimeTriggerAsThis()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            var group = new TestLookupDialogueGroup(root, "group-lookup");

            Assert.IsTrue(group.TryTrigger(
                new TestLookupValue("lookup-value-this-trigger"),
                out NeoDialogueTriggerResult result));

            Assert.IsTrue(result.Ok);
            Assert.IsNotNull(result.Dialogue);
            Assert.AreEqual("dialogue-lookup-this-trigger", result.Dialogue!.Id);
            Assert.IsNull(result.Dialogue.Context.Primary);
        }

        [Test]
        public void TryTrigger_WithNonBoolCondition_ReturnsError()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);

            Assert.IsFalse(root.TryTrigger("dialogue-condition-error", out NeoDialogueTriggerResult result));

            Assert.IsFalse(result.Ok);
            Assert.IsNotNull(result.Error);
            StringAssert.Contains("expected bool", result.Error!.Message);
        }

        [Test]
        public void Start_EmitsTextNode_AndFinishesOnNext()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-direct", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            bool finished = false;
            dialogue.OnShow += node => shown = node;
            dialogue.OnFinish += () => finished = true;

            dialogue.Start();

            Assert.IsTrue(dialogue.IsStarted);
            Assert.AreEqual(NeoDialogueState.Started, dialogue.State);
            Assert.IsNotNull(shown);
            Assert.AreEqual("text-start", shown!.Id);
            Assert.AreEqual("Hello there.", shown.Text);
            Assert.IsFalse(finished);

            shown.Next();

            Assert.IsTrue(finished);
            Assert.AreEqual(NeoDialogueState.Finished, dialogue.State);
            Assert.IsFalse(dialogue.IsDisposed);
            Assert.Throws<System.ObjectDisposedException>(() => shown.Next());
            Assert.Throws<System.InvalidOperationException>(() => dialogue.Start());
        }

        [Test]
        public void Start_WritesDialogueAndTextNodeMemory()
        {
            var client = CreateClient();
            var memory = new TestMemoryStore();
            var root = new TestDialogues(
                client,
                new NeoDialogueRuntimeOptions
                {
                    UtcNow = () => new System.DateTime(
                        2026,
                        5,
                        7,
                        12,
                        34,
                        56,
                        System.DateTimeKind.Utc),
                },
                memory);
            Assert.IsTrue(root.TryTrigger("dialogue-direct", out NeoDialogue dialogue));
            Assert.IsNull(memory.FindDialogueMemory("dialogue-direct"));
            Assert.AreEqual(0, dialogue.VisitCount());
            Assert.IsFalse(dialogue.HasVisited());
            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            var dialogueMemory = memory.FindDialogueMemory("dialogue-direct");
            Assert.IsNotNull(dialogueMemory);
            Assert.AreEqual(1, dialogueMemory!.VisitCount);
            Assert.AreEqual("2026-05-07T12:34:56.0000000Z", dialogueMemory.LastVisitedAt);
            Assert.AreEqual(1, dialogue.VisitCount());
            Assert.IsTrue(dialogue.HasVisited());

            var textMemory = dialogueMemory.FindTextNodeMemory("text-start");
            Assert.IsNotNull(textMemory);
            Assert.AreEqual(1, textMemory!.VisitCount);
            Assert.AreEqual("2026-05-07T12:34:56.0000000Z", textMemory.LastVisitedAt);
            Assert.IsNotNull(shown);
            Assert.AreEqual(1, shown!.VisitCount());
            Assert.IsTrue(shown.HasVisited());
        }

        [Test]
        public void Start_UsesBodyNodePrimaryOverride()
        {
            var client = CreateClient();
            var root = new TestDialogues(
                client,
                valueResolver: valueId => new TestLookupValue(valueId));
            Assert.IsTrue(root.TryTrigger("dialogue-node-primary", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            Assert.IsInstanceOf<TestLookupValue>(dialogue.Context.Primary);
            Assert.AreEqual("primary-dialogue", ((TestLookupValue)dialogue.Context.Primary!).valueId);
            Assert.AreSame(dialogue.Context.Primary, dialogue.Primary);

            dialogue.Start();

            Assert.IsNotNull(shown);
            Assert.AreEqual("primary-dialogue", ((TestLookupValue)dialogue.Context.Primary!).valueId);
            Assert.AreSame(dialogue.Primary, dialogue.Context.Primary);
            Assert.IsInstanceOf<TestLookupValue>(shown!.Primary);
            Assert.AreEqual("primary-text", ((TestLookupValue)shown.Primary!).valueId);
        }

        [Test]
        public void TextNodeVariables_InterpolateBeforeOnShow()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-text-variables-root", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsNotNull(shown);
            Assert.AreEqual("Score 1, asset score 3.", shown!.Text);
        }

        [Test]
        public void TextNodeVariables_UseTextPrimaryWithDialoguePrimaryFallback()
        {
            var client = CreateClient();
            var root = new TestDialogues(
                client,
                valueResolver: valueId => ResolveClientValue(client, valueId));
            Assert.IsTrue(root.TryTrigger("dialogue-text-variable-primary", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsNotNull(shown);
            Assert.AreEqual("Hello Compass.", shown!.Text);
        }

        [Test]
        public void OptionTextVariables_UseParentTextNodeContextBeforeOptionsAreExposed()
        {
            var client = CreateClient();
            var root = new TestDialogues(
                client,
                valueResolver: valueId => ResolveClientValue(client, valueId));
            Assert.IsTrue(root.TryTrigger("dialogue-option-variable-primary", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsNotNull(shown);
            Assert.AreEqual(1, shown!.Options.Count);
            Assert.AreEqual("Take Compass", shown.Options[0].Text);
        }

        [Test]
        public void TextNodeVariables_FailThroughDialogueErrorPath()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-text-variable-missing", out NeoDialogue dialogue));
            System.Exception? error = null;
            bool showed = false;
            dialogue.OnShow += _ => showed = true;
            dialogue.OnError += ex => error = ex;

            dialogue.Start();

            Assert.IsFalse(showed);
            Assert.IsNotNull(error);
            StringAssert.Contains("references missing text variable", error!.Message);
            Assert.AreEqual(NeoDialogueState.Disposed, dialogue.State);
        }

        [Test]
        public void Start_ExposesTextNodeLinkedValues()
        {
            var client = CreateClient();
            var root = new TestDialogues(
                client,
                valueResolver: valueId => new TestLookupValue(valueId));
            Assert.IsTrue(root.TryTrigger("dialogue-text-linked-values", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsNotNull(shown);
            Assert.AreEqual(1, shown!.LinkedValues.Count);
            Assert.IsTrue(shown.LinkedValues.TryGetValue("text-linked-value-a", out object? linked));
            Assert.IsInstanceOf<TestLookupValue>(linked);
            Assert.AreEqual("text-linked-value-a", ((TestLookupValue)linked!).valueId);
        }

        [Test]
        public void TextOption_Select_TransitionsToNextNode()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-options", out NeoDialogue dialogue));

            var shown = new List<NeoDialogueTextNode>();
            bool finished = false;
            dialogue.OnShow += shown.Add;
            dialogue.OnFinish += () => finished = true;

            dialogue.Start();

            Assert.AreEqual(1, shown.Count);
            Assert.AreEqual("text-choice", shown[0].Id);
            Assert.AreEqual(2, shown[0].Options.Count);
            Assert.IsTrue(shown[0].SaveChoice);
            Assert.Throws<System.InvalidOperationException>(() => shown[0].Next());

            shown[0].Options[0].Select();

            Assert.AreEqual(2, shown.Count);
            Assert.AreEqual("text-after-choice", shown[1].Id);
            Assert.AreEqual("option-a", dialogue.Context.OptionId);
            Assert.Throws<System.InvalidOperationException>(() => shown[0].Options[1].Select());

            shown[1].Next();

            Assert.IsTrue(finished);
            Assert.AreEqual(NeoDialogueState.Finished, dialogue.State);
            Assert.IsFalse(dialogue.IsDisposed);
        }

        [Test]
        public void TextOption_Select_SavesChoiceWhenEnabled()
        {
            var client = CreateClient();
            var memory = new TestMemoryStore();
            var root = new TestDialogues(client, memoryStore: memory);
            Assert.IsTrue(root.TryTrigger("dialogue-options", out NeoDialogue dialogue));

            var shown = new List<NeoDialogueTextNode>();
            dialogue.OnShow += shown.Add;

            dialogue.Start();
            Assert.IsFalse(shown[0].Options[0].HasChosen());
            shown[0].Options[0].Select();

            var textMemory = memory
                .FindDialogueMemory("dialogue-options")!
                .FindTextNodeMemory("text-choice");
            Assert.IsNotNull(textMemory);
            Assert.AreEqual("option-a", textMemory!.MostRecentChoiceId);
            Assert.IsTrue(textMemory.HasChoice("option-a"));
            Assert.IsTrue(shown[0].Options[0].HasChosen());
        }

        [Test]
        public void TextOption_HasChosen_ThrowsWhenSaveChoiceDisabled()
        {
            var client = CreateClient();
            var memory = new TestMemoryStore();
            var root = new TestDialogues(client, memoryStore: memory);
            Assert.IsTrue(root.TryTrigger("dialogue-options-no-save", out NeoDialogue dialogue));

            var shown = new List<NeoDialogueTextNode>();
            dialogue.OnShow += shown.Add;

            dialogue.Start();

            Assert.AreEqual(1, shown.Count);
            Assert.IsFalse(shown[0].SaveChoice);
            var ex = Assert.Throws<System.InvalidOperationException>(() => shown[0].Options[0].HasChosen());
            StringAssert.Contains("SaveChoice is disabled", ex!.Message);
            StringAssert.Contains("HasChosen", ex.Message);
        }

        [Test]
        public void TextOption_Settings_FilterHiddenOptionsAndMarkSelectable()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-option-settings", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsNotNull(shown);
            Assert.AreEqual(2, shown!.Options.Count);
            Assert.AreEqual("option-disabled", shown.Options[0].Id);
            Assert.IsFalse(shown.Options[0].Selectable);
            Assert.AreEqual("option-visible", shown.Options[1].Id);
            Assert.IsTrue(shown.Options[1].Selectable);
            Assert.AreEqual(1, shown.HiddenOptions.Count);
            Assert.AreEqual("option-hidden", shown.HiddenOptions[0].Id);
            Assert.IsNull(dialogue.Context.OptionId);

            var ex = Assert.Throws<System.InvalidOperationException>(() => shown.Options[0].Select());
            StringAssert.Contains("option.Selectable", ex!.Message);
            StringAssert.Contains("Button.interactable", ex.Message);
        }

        [Test]
        public void TextOption_Settings_FailureUsesDialogueErrorPath()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-option-condition-error", out NeoDialogue dialogue));

            System.Exception? error = null;
            dialogue.OnError += ex => error = ex;

            dialogue.Start();

            Assert.IsNotNull(error);
            StringAssert.Contains("expected bool", error!.Message);
            Assert.AreEqual(NeoDialogueState.Disposed, dialogue.State);
        }

        [Test]
        public void ConditionsNode_SelectsFirstMatchingOutcome()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-conditions-node", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsNotNull(shown);
            Assert.AreEqual("text-true", shown!.Id);
            Assert.AreEqual("The true branch.", shown.Text);
        }

        [Test]
        public void ActionsNode_Assign_MutatesSaveValueBeforeContinuing()
        {
            var client = CreateClient();
            client.SetSaveValue(new NumberAttributeValue
            {
                id = "score-value",
                createdAt = Now,
                updatedAt = Now,
                value = 1,
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-assign", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsNotNull(shown);
            Assert.AreEqual("text-after-action", shown!.Id);
            Assert.IsTrue(client.TryGetValue("score-value", out NumberAttributeValue? score));
            Assert.AreEqual(5, score!.value);
        }

        [Test]
        public void ActionsNode_Assign_CompoundOperatorMutatesSaveValue()
        {
            var client = CreateClient();
            client.SetSaveValue(new NumberAttributeValue
            {
                id = "score-value",
                createdAt = Now,
                updatedAt = Now,
                value = 1000,
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-assign-compound", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("score-value", out NumberAttributeValue? score));
            Assert.AreEqual(900, score!.value);
        }

        [TestCase("dialogue-ts-compiled-assign-add", 10, 13)]
        [TestCase("dialogue-ts-compiled-assign-subtract", 10, 7)]
        [TestCase("dialogue-ts-compiled-assign-multiply", 10, 30)]
        [TestCase("dialogue-ts-compiled-assign-divide", 12, 4)]
        [TestCase("dialogue-ts-compiled-assign-modulo", 10, 1)]
        [TestCase("dialogue-ts-compiled-assign-increment", 10, 11)]
        [TestCase("dialogue-ts-compiled-assign-decrement", 10, 9)]
        public void ActionsNode_Assign_ExecutesTypescriptCompiledCompoundOperators(
            string dialogueId,
            double initialValue,
            double expectedValue)
        {
            var client = NeoTestSaveStack.LoadClient(LoadFixture("synth-example.json"));
            var scoreNode = client.save.Get<NeoAttributeIntWritable>("Score");
            scoreNode.Set((int)initialValue);
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger(dialogueId, out NeoDialogue dialogue));

            dialogue.Start();

            var score = client.save.Get<NeoAttributeInt>("Score").value;
            Assert.IsNotNull(score);
            Assert.AreEqual(expectedValue, score!.value, $"dialogue {dialogueId}");
        }

        [Test]
        public void ActionsNode_Assign_RejectsAssetOwnedTarget()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-asset-write", out NeoDialogue dialogue));

            System.Exception? error = null;
            dialogue.OnError += ex => error = ex;

            dialogue.Start();

            Assert.IsNotNull(error);
            StringAssert.Contains("not save-owned", error!.Message);
            Assert.IsTrue(client.TryGetValue("asset-score-value", out NumberAttributeValue? assetScore));
            Assert.AreEqual(3, assetScore!.value);
        }

        [Test]
        public void ActionsNode_Assign_MaterializesDefaultSaveRootBeforeMutating()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-default-save-write", out NeoDialogue dialogue));

            dialogue.Start();

            // Stable-id overlay: a save shadows the authored values at their SAME
            // ids (no override-map hop, no eager root clone). The dialogue write
            // shadows the root record + the Score leaf it touched.
            Assert.IsTrue(client.saveValues.TryGetValue("root-save-default-value", out AttributeValue? saveRootUntyped));
            var saveRoot = (ObjectAttributeValue)saveRootUntyped;
            Assert.AreEqual("score-default-value", saveRoot.value!["Score"]);
            Assert.IsTrue(client.saveValues.TryGetValue("score-default-value", out AttributeValue? scoreUntyped));
            var score = (NumberAttributeValue)scoreUntyped;
            Assert.AreEqual(22, score!.value);
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
        }

        [Test]
        public void ActionsNode_Assign_InferSessionOwnershipWhenUiTargetHasNoWritability()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger(
                "dialogue-action-session-bool-write-with-inferred-ownership",
                out NeoDialogue dialogue));

            dialogue.Start();

            // Inferred session ownership: the Foo leaf is shadowed in the SESSION
            // store at its authored id (stable-id overlay), never the save store.
            Assert.IsTrue(client.sessionValues.TryGetValue("session-foo-default-value", out AttributeValue? sessionFooUntyped));
            var sessionFoo = (BoolAttributeValue)sessionFooUntyped;
            Assert.AreEqual(true, sessionFoo.value);
            Assert.IsFalse(client.saveValues.ContainsKey("session-foo-default-value"));
            Assert.IsFalse(client.SerializeSaveData().Contains("session-foo-default-value"));
        }

        [Test]
        public void ActionsNode_CollectionCall_AddsSaveListEntry()
        {
            var client = CreateClient();
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "list-value",
                createdAt = Now,
                updatedAt = Now,
                value = new string[0],
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-list-add", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("list-value", out ArrayAttributeValue? list));
            Assert.AreEqual(1, list!.value!.Length);
            Assert.IsTrue(client.TryGetValue(list.value[0], out StringAttributeValue? entry));
            Assert.AreEqual("Potion", entry!.value);
        }

        [Test]
        public void ActionsNode_CollectionCall_LookupAdd_StoresAssetEntryRef()
        {
            var client = CreateClient();
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "default-inventory-value",
                createdAt = Now,
                updatedAt = Now,
                value = new string[0],
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-lookup-add", out NeoDialogue dialogue));

            System.Exception? error = null;
            dialogue.OnError += ex => error = ex;
            dialogue.Start();

            Assert.IsNull(error, error?.Message);
            Assert.IsTrue(client.TryGetValue("default-inventory-value", out ArrayAttributeValue? lookup));
            CollectionAssert.AreEqual(new[] { "asset-item-value-b" }, lookup!.value);
        }

        [Test]
        public void ActionsNode_CollectionCall_LookupAdd_AppendsWhenLookupAlreadyHasEntry()
        {
            // Field repro: the FIRST grant works into an empty inventory, the
            // SECOND threw "Cannot mutate '<first item>' ... not save-owned"
            // because the resolved lookup read mapped back to the looked-up
            // asset row instead of the save-side ref list.
            var client = CreateClient();
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "default-inventory-value",
                createdAt = Now,
                updatedAt = Now,
                value = new[] { "asset-item-value" },
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-lookup-add", out NeoDialogue dialogue));

            System.Exception? error = null;
            dialogue.OnError += ex => error = ex;
            dialogue.Start();

            Assert.IsNull(error, error?.Message);
            Assert.IsTrue(client.TryGetValue("default-inventory-value", out ArrayAttributeValue? lookup));
            CollectionAssert.AreEqual(
                new[] { "asset-item-value", "asset-item-value-b" },
                lookup!.value);
        }

        [Test]
        public void ActionsNode_CollectionCall_RemovesSaveListEntry()
        {
            var client = CreateClient();
            SeedList(client, "list-value");
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-list-remove", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("list-value", out ArrayAttributeValue? list));
            Assert.AreEqual(1, list!.value!.Length);
            Assert.AreEqual("list-entry-elixir", list.value[0]);
            Assert.IsFalse(client.TryGetValue("list-entry-potion", out AttributeValue? _));
        }

        [Test]
        public void ActionsNode_CollectionCall_RemoveAtDeletesSaveListEntry()
        {
            var client = CreateClient();
            SeedList(client, "list-value");
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-list-remove-at", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("list-value", out ArrayAttributeValue? list));
            Assert.AreEqual(1, list!.value!.Length);
            Assert.AreEqual("list-entry-elixir", list.value[0]);
            Assert.IsFalse(client.TryGetValue("list-entry-potion", out AttributeValue? _));
        }

        [Test]
        public void ActionsNode_CollectionCall_ClearDeletesSaveListEntries()
        {
            var client = CreateClient();
            SeedList(client, "list-value");
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-list-clear", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("list-value", out ArrayAttributeValue? list));
            Assert.AreEqual(0, list!.value!.Length);
            Assert.IsFalse(client.TryGetValue("list-entry-potion", out AttributeValue? _));
            Assert.IsFalse(client.TryGetValue("list-entry-elixir", out AttributeValue? _));
        }

        [Test]
        public void ActionsNode_CollectionCall_AddsDictionaryEntry()
        {
            var client = CreateClient();
            SeedDictionary(client, "dict-value");
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-dict-add", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("dict-value", out ObjectAttributeValue? dict));
            Assert.IsTrue(dict!.value!.TryGetValue("slot", out string valueId));
            Assert.IsTrue(client.TryGetValue(valueId, out StringAttributeValue? entry));
            Assert.AreEqual("Potion", entry!.value);
        }

        [Test]
        public void ActionsNode_CollectionCall_RemovesDictionaryEntry()
        {
            var client = CreateClient();
            SeedDictionary(client, "dict-value");
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-dict-remove", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("dict-value", out ObjectAttributeValue? dict));
            Assert.IsFalse(dict!.value!.ContainsKey("slot"));
            Assert.IsFalse(client.TryGetValue("dict-entry-slot", out AttributeValue? _));
        }

        [Test]
        public void ActionsNode_CollectionCall_ClearsDictionaryEntries()
        {
            var client = CreateClient();
            SeedDictionary(client, "dict-value");
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-dict-clear", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("dict-value", out ObjectAttributeValue? dict));
            Assert.AreEqual(0, dict!.value!.Count);
            Assert.IsFalse(client.TryGetValue("dict-entry-slot", out AttributeValue? _));
        }

        [Test]
        public void ActionsNode_CollectionCall_ClearDictionaryPreservesSharedSaveValue()
        {
            var client = CreateClient();
            // Stable-id overlay: make the shared value reachable from the save
            // root (via the root's Items list) so the GC preserves it when the
            // unrelated dict entry is cleared — there is no override-map rebind.
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "root-save-default-value",
                createdAt = Now,
                updatedAt = Now,
                typeId = "type-root",
                value = new Dictionary<string, string>
                {
                    ["Items"] = "shared-items-list",
                },
            });
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "shared-items-list",
                createdAt = Now,
                updatedAt = Now,
                value = new[] { "shared-item-value" },
            });
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "shared-item-value",
                createdAt = Now,
                updatedAt = Now,
                typeId = "type-item",
                value = new Dictionary<string, string>(),
            });
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "dict-value",
                createdAt = Now,
                updatedAt = Now,
                value = new Dictionary<string, string>
                {
                    ["slot"] = "shared-item-value",
                },
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-dict-clear", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("dict-value", out ObjectAttributeValue? dict));
            Assert.AreEqual(0, dict!.value!.Count);
            Assert.IsTrue(client.TryGetValue("shared-item-value", out ObjectAttributeValue? rootRow));
            Assert.AreEqual("type-item", rootRow!.typeId);
        }

        [Test]
        public void ActionsNode_Assign_CreatesCustomMemberSaveValue()
        {
            var client = CreateClient();
            client.AddSaveValue("root-save", new ObjectAttributeValue
            {
                id = "root-save-value",
                createdAt = Now,
                updatedAt = Now,
                typeId = "type-root",
                value = new Dictionary<string, string>(),
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-custom-set", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("root-save-value", out ObjectAttributeValue? rootRow));
            Assert.IsTrue(rootRow!.value!.TryGetValue("Score", out string scoreValueId));
            Assert.IsTrue(client.TryGetValue(scoreValueId, out NumberAttributeValue? score));
            Assert.AreEqual(12, score!.value);
        }

        [Test]
        public void ActionsNode_Assign_CanMutateGeneratedContextPrimary()
        {
            var client = CreateClient();
            client.AddSaveValue("root-save", new ObjectAttributeValue
            {
                id = "root-save-value",
                createdAt = Now,
                updatedAt = Now,
                typeId = "type-root",
                value = new Dictionary<string, string>(),
            });
            var root = new TestDialogues(
                client,
                valueResolver: valueId => new TestLookupValue(valueId));
            Assert.IsTrue(root.TryTrigger("dialogue-action-primary-set", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("root-save-value", out ObjectAttributeValue? rootRow));
            Assert.IsTrue(rootRow!.value!.TryGetValue("Score", out string scoreValueId));
            Assert.IsTrue(client.TryGetValue(scoreValueId, out NumberAttributeValue? score));
            Assert.AreEqual(15, score!.value);
        }

        [Test]
        public void ActionsNode_CollectionCall_CanLinkGeneratedCustomValue()
        {
            var client = CreateClient();
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "list-value",
                createdAt = Now,
                updatedAt = Now,
                value = new string[0],
            });
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "root-save-value",
                createdAt = Now,
                updatedAt = Now,
                typeId = "type-root",
                value = new Dictionary<string, string>(),
            });
            var root = new TestDialogues(
                client,
                valueResolver: valueId => new TestLookupValue(valueId));
            Assert.IsTrue(root.TryTrigger("dialogue-action-list-add-primary", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("list-value", out ArrayAttributeValue? list));
            CollectionAssert.AreEqual(new[] { "root-save-value" }, list!.value);
        }

        [Test]
        public void ActionsNode_LookupSetAdd_ResolvesNestedCollectionValueWhenUnpinned()
        {
            var client = CreateClient();
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "save-inventory-value",
                createdAt = Now,
                updatedAt = Now,
                value = new string[0],
            });
            var root = new TestDialogues(
                client,
                valueResolver: valueId => new TestLookupValue(valueId));
            Assert.IsTrue(root.TryTrigger("dialogue-action-lookup-add-primary", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("save-inventory-value", out ArrayAttributeValue? inventory));
            CollectionAssert.AreEqual(new[] { "asset-item-value" }, inventory!.value);
        }

        [Test]
        public void ActionsNode_LookupSetAdd_NotifiesExistingLookupSetWrapper()
        {
            var client = CreateClient();
            // The lookup-add action targets the inventory collection by its value
            // id directly (a stable instance id). Shadow that id and bind the
            // wrapper to the same id so the action's write notifies the wrapper —
            // no override-map rebind of the root.
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "save-inventory-value",
                createdAt = Now,
                updatedAt = Now,
                value = new string[0],
            });
            Assert.IsTrue(client.TryGetAttribute("attr-inventory", out LookupAttribute? inventoryAttr));
            var inventoryNode = (NeoAttributeLookupWritable)NeoAttribute.CreateWritable(
                client,
                inventoryAttr!,
                "save-inventory-value",
                NeoValueOwnership.Save);
            var inventory = new NeoLookupSet<TestLookupValue>(
                client,
                inventoryNode,
                child => new TestLookupValue(child.value?.id));
            int changed = 0;
            using var inventorySubscription = inventory.OnChanged((_, _) => changed++);
            var root = new TestDialogues(
                client,
                valueResolver: valueId => new TestLookupValue(valueId));
            Assert.IsTrue(root.TryTrigger("dialogue-action-lookup-add-primary", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.AreEqual(1, changed);
        }

        [Test]
        public void ActionsNode_LookupSetAddThroughRootPath_NotifiesExistingLookupSetWrapper()
        {
            var client = CreateClient();
            client.AddSaveValue("root-save", new ObjectAttributeValue
            {
                id = "root-save-with-inventory-value",
                createdAt = Now,
                updatedAt = Now,
                typeId = "type-root",
                value = new Dictionary<string, string>
                {
                    ["Inventory"] = "save-inventory-value",
                },
            });
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "save-inventory-value",
                createdAt = Now,
                updatedAt = Now,
                value = new string[0],
            });
            var inventory = new NeoLookupSet<TestLookupValue>(
                client,
                client.save.GetOrCreateLookup("Inventory"),
                child => new TestLookupValue(child.value?.id));
            int changed = 0;
            using var inventorySubscription = inventory.OnChanged((_, _) => changed++);
            var root = new TestDialogues(
                client,
                valueResolver: valueId => new TestLookupValue(valueId));
            Assert.IsTrue(root.TryTrigger("dialogue-action-lookup-add-root-path", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.AreEqual(1, changed);
        }

        [Test]
        public void ActionsNode_LookupSetAddThroughRootPath_NotifiesWrapperAcrossMaterialization()
        {
            var client = CreateClient();
            var inventory = new NeoLookupSet<TestLookupValue>(
                client,
                client.save.GetOrCreateLookup("Inventory"),
                child => new TestLookupValue(child.value?.id));
            int changed = 0;
            using var inventorySubscription = inventory.OnChanged((_, _) => changed++);
            var root = new TestDialogues(
                client,
                valueResolver: valueId => new TestLookupValue(valueId));
            Assert.IsTrue(root.TryTrigger("dialogue-action-lookup-add-root-path", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.AreEqual(1, changed);
            CollectionAssert.AreEqual(new[] { "asset-item-value" }, inventory.Ids);
        }

        [Test]
        public void ActionsNode_CollectionCall_CanLinkGeneratedCustomDictionaryValue()
        {
            var client = CreateClient();
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "dict-value",
                createdAt = Now,
                updatedAt = Now,
                value = new Dictionary<string, string>(),
            });
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "root-save-value",
                createdAt = Now,
                updatedAt = Now,
                typeId = "type-root",
                value = new Dictionary<string, string>(),
            });
            var root = new TestDialogues(
                client,
                valueResolver: valueId => new TestLookupValue(valueId));
            Assert.IsTrue(root.TryTrigger("dialogue-action-dict-add-primary", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("dict-value", out ObjectAttributeValue? dict));
            Assert.IsTrue(dict!.value!.TryGetValue("slot", out string valueId));
            Assert.AreEqual("root-save-value", valueId);
        }

        [Test]
        public void ActionsNode_CollectionCall_ClearPreservesSharedSaveValue()
        {
            var client = CreateClient();
            // Stable-id overlay: make the shared value reachable from the save
            // root (via the root's Items list) so the GC preserves it when the
            // unrelated list entry is cleared — there is no override-map rebind.
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "root-save-default-value",
                createdAt = Now,
                updatedAt = Now,
                typeId = "type-root",
                value = new Dictionary<string, string>
                {
                    ["Items"] = "shared-items-list",
                },
            });
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "shared-items-list",
                createdAt = Now,
                updatedAt = Now,
                value = new[] { "shared-item-value" },
            });
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "shared-item-value",
                createdAt = Now,
                updatedAt = Now,
                typeId = "type-item",
                value = new Dictionary<string, string>(),
            });
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "list-value",
                createdAt = Now,
                updatedAt = Now,
                value = new[] { "shared-item-value" },
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-list-clear", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("list-value", out ArrayAttributeValue? list));
            Assert.AreEqual(0, list!.value!.Length);
            Assert.IsTrue(client.TryGetValue("shared-item-value", out ObjectAttributeValue? rootRow));
            Assert.AreEqual("type-item", rootRow!.typeId);
        }

        [Test]
        public void ActionsNode_Error_StopsTraversalAndEmitsError()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-error", out NeoDialogue dialogue));

            bool showedText = false;
            System.Exception? error = null;
            dialogue.OnShow += _ => showedText = true;
            dialogue.OnError += ex => error = ex;

            dialogue.Start();

            Assert.IsFalse(showedText);
            Assert.IsNotNull(error);
            Assert.AreEqual("boom", error!.Message);
            Assert.IsTrue(dialogue.IsDisposed);
        }

        [Test]
        public void ActionsNode_Pause_EmitsPauseAndResumesAtNextAction()
        {
            var client = CreateClient();
            client.SetSaveValue(new NumberAttributeValue
            {
                id = "score-value",
                createdAt = Now,
                updatedAt = Now,
                value = 1,
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-pause-manual", out NeoDialogue dialogue));

            NeoDialoguePauseAction? pause = null;
            NeoDialogueTextNode? shown = null;
            dialogue.OnPause += action => pause = action;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.AreEqual(NeoDialogueState.Paused, dialogue.State);
            Assert.IsNotNull(pause);
            Assert.AreEqual("pause-manual", pause!.Id);
            Assert.AreEqual("dialogue-action-pause-manual", pause.DialogueId);
            Assert.AreEqual("actions-start", pause.NodeId);
            Assert.AreEqual("cutscene", pause.Reason);
            Assert.IsNull(pause.AutoResumeDurationSeconds);
            Assert.IsTrue(pause.Paused);
            Assert.IsNull(shown);

            pause.Resume();

            Assert.IsFalse(pause.Paused);
            Assert.IsNotNull(shown);
            Assert.AreEqual("text-after-action", shown!.Id);
            Assert.AreEqual(NeoDialogueState.Started, dialogue.State);
            Assert.IsTrue(client.TryGetValue("score-value", out NumberAttributeValue? score));
            Assert.AreEqual(5, score!.value);
            Assert.Throws<System.InvalidOperationException>(() => pause.Resume());
        }

        [Test]
        public void ActionsNode_DeferredFunction_PausesAndResumesWithResult()
        {
            var client = CreateClient();
            client.SetSaveValue(new NumberAttributeValue
            {
                id = "score-value",
                createdAt = Now,
                updatedAt = Now,
                value = 1,
            });
            NeoDeferredFunction<int>? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["attr-deferred-score"] = (_, _, _, deferred) =>
                    {
                        pending = NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(
                            deferred,
                            "DeferredScore");
                    },
                });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-deferred-score", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.AreEqual(NeoDialogueState.Paused, dialogue.State);
            Assert.IsNotNull(pending);
            Assert.IsTrue(pending!.Pending);
            Assert.IsNull(shown);

            pending.Complete(42);

            Assert.IsFalse(pending.Pending);
            Assert.AreEqual(NeoDialogueState.Started, dialogue.State);
            Assert.IsNotNull(shown);
            Assert.AreEqual("text-after-action", shown!.Id);
            Assert.IsTrue(client.TryGetValue("score-value", out NumberAttributeValue? score));
            Assert.AreEqual(42, score!.value);
        }

        [Test]
        public void ActionsNode_DeferredFunction_SynchronousCompleteContinuesAfterHandlerReturns()
        {
            var client = CreateClient();
            client.SetSaveValue(new NumberAttributeValue
            {
                id = "score-value",
                createdAt = Now,
                updatedAt = Now,
                value = 1,
            });
            bool handlerReturned = false;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["attr-deferred-score"] = (_, _, _, deferred) =>
                    {
                        var typed = NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(
                            deferred,
                            "DeferredScore");
                        typed.Complete(77);
                        handlerReturned = true;
                    },
                });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-deferred-score", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsTrue(handlerReturned);
            Assert.AreEqual(NeoDialogueState.Started, dialogue.State);
            Assert.IsNotNull(shown);
            Assert.IsTrue(client.TryGetValue("score-value", out NumberAttributeValue? score));
            Assert.AreEqual(77, score!.value);
        }

        [Test]
        public void ActionsNode_DeferredFunction_DisposeInvalidatesPendingHandle()
        {
            var client = CreateClient();
            NeoDeferredFunction<int>? pending = null;
            client.RegisterDeferredNativeFunctionInvokers(
                new Dictionary<string, NeoClient.NeoDeferredNativeFunctionInvoker>
                {
                    ["attr-deferred-score"] = (_, _, _, deferred) =>
                    {
                        pending = NeoGeneratedTypesSupport.ResolveDeferredFunction<NeoDeferredFunction<int>>(
                            deferred,
                            "DeferredScore");
                    },
                });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-deferred-score", out NeoDialogue dialogue));

            dialogue.Start();
            dialogue.Dispose();

            Assert.IsNotNull(pending);
            Assert.IsFalse(pending!.Pending);
            Assert.IsTrue(pending.CancellationToken.IsCancellationRequested);
            Assert.Throws<ObjectDisposedException>(() => pending.Complete(1));
        }

        [Test]
        public void ActionsNode_Pause_ConsecutivePausesRequireCurrentPause()
        {
            var client = CreateClient();
            client.SetSaveValue(new NumberAttributeValue
            {
                id = "score-value",
                createdAt = Now,
                updatedAt = Now,
                value = 1,
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-pause-consecutive", out NeoDialogue dialogue));

            var pauses = new List<NeoDialoguePauseAction>();
            NeoDialogueTextNode? shown = null;
            dialogue.OnPause += pauses.Add;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();
            Assert.AreEqual(1, pauses.Count);

            pauses[0].Resume();

            Assert.AreEqual(2, pauses.Count);
            Assert.IsNull(shown);
            Assert.Throws<System.InvalidOperationException>(() => pauses[0].Resume());

            pauses[1].Resume();

            Assert.IsNotNull(shown);
            Assert.IsTrue(client.TryGetValue("score-value", out NumberAttributeValue? score));
            Assert.AreEqual(7, score!.value);
        }

        [Test]
        public void ActionsNode_Pause_SynchronousResumeContinuesBeforeStartReturns()
        {
            var client = CreateClient();
            client.SetSaveValue(new NumberAttributeValue
            {
                id = "score-value",
                createdAt = Now,
                updatedAt = Now,
                value = 1,
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-pause-manual", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            dialogue.OnPause += action => action.Resume();
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsNotNull(shown);
            Assert.AreEqual("text-after-action", shown!.Id);
            Assert.IsTrue(client.TryGetValue("score-value", out NumberAttributeValue? score));
            Assert.AreEqual(5, score!.value);
        }

        [Test]
        public void ActionsNode_Pause_AutoResumeZeroContinuesAfterHandlersReturn()
        {
            var client = CreateClient();
            client.SetSaveValue(new NumberAttributeValue
            {
                id = "score-value",
                createdAt = Now,
                updatedAt = Now,
                value = 1,
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-pause-auto-zero", out NeoDialogue dialogue));

            bool handlerSawPausedState = false;
            NeoDialogueTextNode? shown = null;
            dialogue.OnPause += action =>
            {
                handlerSawPausedState = action.Paused
                    && dialogue.State == NeoDialogueState.Paused
                    && shown == null;
            };
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsTrue(handlerSawPausedState);
            Assert.IsNotNull(shown);
            Assert.IsTrue(client.TryGetValue("score-value", out NumberAttributeValue? score));
            Assert.AreEqual(9, score!.value);
        }

        [Test]
        public void ActionsNode_Pause_AutoResumePositiveUsesSchedulerAndManualResumeCancels()
        {
            var client = CreateClient();
            client.SetSaveValue(new NumberAttributeValue
            {
                id = "score-value",
                createdAt = Now,
                updatedAt = Now,
                value = 1,
            });
            var scheduler = new ManualPauseScheduler();
            var root = new TestDialogues(
                client,
                new NeoDialogueRuntimeOptions
                {
                    PauseScheduler = scheduler,
                });
            Assert.IsTrue(root.TryTrigger("dialogue-action-pause-auto-delay", out NeoDialogue dialogue));

            NeoDialoguePauseAction? pause = null;
            NeoDialogueTextNode? shown = null;
            dialogue.OnPause += action => pause = action;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsNotNull(pause);
            Assert.AreEqual(1, scheduler.PendingCount);
            Assert.IsNull(shown);

            pause!.Resume();

            Assert.AreEqual(0, scheduler.PendingCount);
            Assert.IsNotNull(shown);
            Assert.IsTrue(client.TryGetValue("score-value", out NumberAttributeValue? score));
            Assert.AreEqual(11, score!.value);
            Assert.DoesNotThrow(() => scheduler.RunAll());
        }

        [Test]
        public void ActionsNode_Pause_AutoResumePositiveContinuesWhenSchedulerFires()
        {
            var client = CreateClient();
            client.SetSaveValue(new NumberAttributeValue
            {
                id = "score-value",
                createdAt = Now,
                updatedAt = Now,
                value = 1,
            });
            var scheduler = new ManualPauseScheduler();
            var root = new TestDialogues(
                client,
                new NeoDialogueRuntimeOptions
                {
                    PauseScheduler = scheduler,
                });
            Assert.IsTrue(root.TryTrigger("dialogue-action-pause-auto-delay", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            dialogue.OnShow += node => shown = node;

            dialogue.Start();

            Assert.IsNull(shown);
            Assert.AreEqual(1, scheduler.PendingCount);

            scheduler.RunNext();

            Assert.IsNotNull(shown);
            Assert.IsTrue(client.TryGetValue("score-value", out NumberAttributeValue? score));
            Assert.AreEqual(11, score!.value);
        }

        [Test]
        public void ActionsNode_Pause_NoListenerLogsWarningAndRemainsPaused()
        {
            var client = CreateClient();
            var logger = new TestDialogueLogger();
            var root = new TestDialogues(
                client,
                new NeoDialogueRuntimeOptions
                {
                    Logger = logger,
                });
            Assert.IsTrue(root.TryTrigger("dialogue-action-pause-only", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.AreEqual(NeoDialogueState.Paused, dialogue.State);
            Assert.AreEqual(1, logger.Warnings.Count);
            StringAssert.Contains("paused", logger.Warnings[0]);
            StringAssert.Contains("pause-only", logger.Warnings[0]);
        }

        [Test]
        public void ActionsNode_Pause_DisposeDialogueInvalidatesPause()
        {
            var client = CreateClient();
            var scheduler = new ManualPauseScheduler();
            var root = new TestDialogues(
                client,
                new NeoDialogueRuntimeOptions
                {
                    PauseScheduler = scheduler,
                });
            Assert.IsTrue(root.TryTrigger("dialogue-action-pause-auto-delay", out NeoDialogue dialogue));

            NeoDialoguePauseAction? pause = null;
            dialogue.OnPause += action => pause = action;

            dialogue.Start();
            dialogue.Dispose();

            Assert.AreEqual(NeoDialogueState.Disposed, dialogue.State);
            Assert.AreEqual(0, scheduler.PendingCount);
            Assert.IsFalse(pause!.Paused);
            Assert.Throws<System.ObjectDisposedException>(() => pause.Resume());
        }

        [Test]
        public void ActionsNode_Pause_DisposeClientDisposesDialogueAndInvalidatesPause()
        {
            var client = CreateClient();
            var scheduler = new ManualPauseScheduler();
            var root = new TestDialogues(
                client,
                new NeoDialogueRuntimeOptions
                {
                    PauseScheduler = scheduler,
                });
            Assert.IsTrue(root.TryTrigger("dialogue-action-pause-auto-delay", out NeoDialogue dialogue));

            NeoDialoguePauseAction? pause = null;
            dialogue.OnPause += action => pause = action;

            dialogue.Start();
            client.Dispose();

            Assert.AreEqual(NeoDialogueState.Disposed, dialogue.State);
            Assert.AreEqual(0, scheduler.PendingCount);
            Assert.IsFalse(pause!.Paused);
            Assert.Throws<System.ObjectDisposedException>(() => pause.Resume());
        }

        [Test]
        public void ActionsNode_Pause_ErrorAfterResumeUsesErrorPath()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-pause-then-error", out NeoDialogue dialogue));

            System.Exception? error = null;
            dialogue.OnPause += action => action.Resume();
            dialogue.OnError += ex => error = ex;

            dialogue.Start();

            Assert.IsNotNull(error);
            Assert.AreEqual("boom-after-pause", error!.Message);
            Assert.AreEqual(NeoDialogueState.Disposed, dialogue.State);
        }

        private static NeoClient CreateClient()
        {
            var data = new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "Dialogue Project",
                    rootAssetsAttributeId = "root-assets",
                    rootSaveFileAttributeId = "root-save",
                    rootSessionAttributeId = "root-session",
                    defaultPriorityGroupId = "priority-default",
                    createdAt = Now,
                    updatedAt = Now,
                },
                localization = new ProjectLocalizationExport
                {
                    schemaVersion = 1,
                    mainLocale = "en-US",
                    supportedLocales = new[]
                    {
                        new ProjectLocalizationLocale { locale = "en-US" },
                    },
                    mainLocaleFileName = "en-US.json",
                    localeFileNames = new Dictionary<string, string>
                    {
                        ["en-US"] = "en-US.json",
                    },
                    formatting = new ProjectLocalizationFormatting
                    {
                        syntax = "smart-format",
                        sourceSyntax = "icu",
                    },
                },
                attributes = new Dictionary<string, NeoCompose.Runtime.Json.Attribute>
                {
                    ["root-assets"] = RootAttribute("root-assets", "Assets"),
                    ["root-save"] = RootAttribute("root-save", "Save"),
                    ["root-session"] = RootAttribute("root-session", "Session"),
                    ["attr-score"] = new IntAttribute
                    {
                        id = "attr-score",
                        projectId = ProjectId,
                        name = "Score",
                        type = AttributeType.Int,
                        required = true,
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["attr-item-name"] = new StringAttribute
                    {
                        id = "attr-item-name",
                        projectId = ProjectId,
                        name = "Name",
                        type = AttributeType.String,
                        required = true,
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["attr-item"] = new CustomAttribute
                    {
                        id = "attr-item",
                        projectId = ProjectId,
                        name = "Item",
                        type = AttributeType.Custom,
                        required = true,
                        customTypeId = "type-item",
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["attr-items"] = new ListAttribute
                    {
                        id = "attr-items",
                        projectId = ProjectId,
                        name = "Items",
                        type = AttributeType.List,
                        required = true,
                        valueId = "default-items-value",
                        entryAttributeId = "attr-item",
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["attr-inventory"] = new LookupAttribute
                    {
                        id = "attr-inventory",
                        projectId = ProjectId,
                        name = "Inventory",
                        type = AttributeType.Lookup,
                        required = true,
                        multiselect = true,
                        collectionAttributeId = "attr-items",
                        collectionValueId = null,
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["attr-session-foo"] = new BoolAttribute
                    {
                        id = "attr-session-foo",
                        projectId = ProjectId,
                        name = "Foo",
                        type = AttributeType.Bool,
                        required = true,
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["attr-deferred-score"] = new FunctionAttribute
                    {
                        id = "attr-deferred-score",
                        projectId = ProjectId,
                        name = "DeferredScore",
                        type = AttributeType.Function,
                        required = false,
                        returnTypeInfo = IntTypeInfo(),
                        argumentTypes = new FunctionArgumentTypeInfo[0],
                        deferred = true,
                        createdAt = Now,
                        updatedAt = Now,
                    },
                },
                values = new Dictionary<string, AttributeValue>
                {
                    ["root-assets-value"] = new ObjectAttributeValue
                    {
                        id = "root-assets-value",
                        createdAt = Now,
                        updatedAt = Now,
                        typeId = "type-root",
                        value = new Dictionary<string, string>
                        {
                            ["Items"] = "assets-items-value",
                            ["Score"] = "asset-score-value",
                        },
                    },
                    ["asset-score-value"] = new NumberAttributeValue
                    {
                        id = "asset-score-value",
                        createdAt = Now,
                        updatedAt = Now,
                        value = 3,
                    },
                    ["root-save-default-value"] = new ObjectAttributeValue
                    {
                        id = "root-save-default-value",
                        createdAt = Now,
                        updatedAt = Now,
                        typeId = "type-root",
                        value = new Dictionary<string, string>
                        {
                            ["Score"] = "score-default-value",
                            ["Inventory"] = "default-inventory-value",
                        },
                    },
                    ["root-session-default-value"] = new ObjectAttributeValue
                    {
                        id = "root-session-default-value",
                        createdAt = Now,
                        updatedAt = Now,
                        typeId = "type-root",
                        value = new Dictionary<string, string>
                        {
                            ["Foo"] = "session-foo-default-value",
                        },
                    },
                    ["session-foo-default-value"] = new BoolAttributeValue
                    {
                        id = "session-foo-default-value",
                        createdAt = Now,
                        updatedAt = Now,
                        value = false,
                    },
                    ["score-default-value"] = new NumberAttributeValue
                    {
                        id = "score-default-value",
                        createdAt = Now,
                        updatedAt = Now,
                        value = 1,
                    },
                    ["lookup-value-direct"] = new ObjectAttributeValue
                    {
                        id = "lookup-value-direct",
                        createdAt = Now,
                        updatedAt = Now,
                        value = new Dictionary<string, string>(),
                    },
                    ["default-items-value"] = new ArrayAttributeValue
                    {
                        id = "default-items-value",
                        createdAt = Now,
                        updatedAt = Now,
                        value = new string[0],
                    },
                    ["default-inventory-value"] = new ArrayAttributeValue
                    {
                        id = "default-inventory-value",
                        createdAt = Now,
                        updatedAt = Now,
                        value = new string[0],
                    },
                    ["assets-items-value"] = new ArrayAttributeValue
                    {
                        id = "assets-items-value",
                        createdAt = Now,
                        updatedAt = Now,
                        value = new[] { "asset-item-value", "asset-item-value-b" },
                    },
                    ["asset-item-value"] = new ObjectAttributeValue
                    {
                        id = "asset-item-value",
                        createdAt = Now,
                        updatedAt = Now,
                        typeId = "type-item",
                        value = new Dictionary<string, string>
                        {
                            ["Name"] = "asset-item-name-value",
                        },
                    },
                    ["asset-item-name-value"] = new StringAttributeValue
                    {
                        id = "asset-item-name-value",
                        createdAt = Now,
                        updatedAt = Now,
                        value = "Compass",
                    },
                    ["asset-item-value-b"] = new ObjectAttributeValue
                    {
                        id = "asset-item-value-b",
                        createdAt = Now,
                        updatedAt = Now,
                        typeId = "type-item",
                        value = new Dictionary<string, string>
                        {
                            ["Name"] = "asset-item-name-value-b",
                        },
                    },
                    ["asset-item-name-value-b"] = new StringAttributeValue
                    {
                        id = "asset-item-name-value-b",
                        createdAt = Now,
                        updatedAt = Now,
                        value = "Parasol",
                    },
                },
                types = new Dictionary<string, CustomType>
                {
                    ["type-root"] = new()
                    {
                        id = "type-root",
                        projectId = ProjectId,
                        name = "Root",
                        schema = new Dictionary<string, string>
                        {
                            ["Score"] = "attr-score",
                            ["Items"] = "attr-items",
                            ["Inventory"] = "attr-inventory",
                            ["Foo"] = "attr-session-foo",
                        },
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["type-item"] = new()
                    {
                        id = "type-item",
                        projectId = ProjectId,
                        name = "Item",
                        schema = new Dictionary<string, string>
                        {
                            ["Name"] = "attr-item-name",
                        },
                        createdAt = Now,
                        updatedAt = Now,
                    },
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
                dialogueGroups = new Dictionary<string, DialogueGroup>
                {
                    ["group-standard"] = new StandardDialogueGroup
                    {
                        id = "group-standard",
                        projectId = ProjectId,
                        name = "Standard",
                        type = DialogueGroupType.Standard,
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["group-lookup"] = new LookupDialogueGroup
                    {
                        id = "group-lookup",
                        projectId = ProjectId,
                        name = "Lookup",
                        type = DialogueGroupType.Lookup,
                        collectionAttributeId = "attr-npcs",
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["group-priority"] = new StandardDialogueGroup
                    {
                        id = "group-priority",
                        projectId = ProjectId,
                        name = "Priority",
                        type = DialogueGroupType.Standard,
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["group-visits"] = new StandardDialogueGroup
                    {
                        id = "group-visits",
                        projectId = ProjectId,
                        name = "Visits",
                        type = DialogueGroupType.Standard,
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["group-folder-false"] = new FolderDialogueGroup
                    {
                        id = "group-folder-false",
                        projectId = ProjectId,
                        name = "False Folder",
                        type = DialogueGroupType.Folder,
                        conditions = new[] { Condition(BoolGetter(false)) },
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["group-child-of-false"] = new StandardDialogueGroup
                    {
                        id = "group-child-of-false",
                        projectId = ProjectId,
                        name = "Child Of False",
                        type = DialogueGroupType.Standard,
                        parentDialogueGroupId = "group-folder-false",
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["group-context-primary"] = new StandardDialogueGroup
                    {
                        id = "group-context-primary",
                        projectId = ProjectId,
                        name = "Context Primary",
                        type = DialogueGroupType.Standard,
                        conditions = new[] { Condition(ContextIsNotNull("primary")) },
                        createdAt = Now,
                        updatedAt = Now,
                    },
                },
                dialogues = new Dictionary<string, Dialogue>
                {
                    ["dialogue-direct"] = Dialogue(
                        "dialogue-direct",
                        "A Direct Dialogue",
                        "group-standard",
                        relativeOrder: 0),
                    ["dialogue-options"] = OptionsDialogue(),
                    ["dialogue-options-no-save"] = OptionsNoSaveChoiceDialogue(),
                    ["dialogue-option-settings"] = OptionSettingsDialogue(),
                    ["dialogue-option-condition-error"] = OptionConditionErrorDialogue(),
                    ["dialogue-condition-false"] = Dialogue(
                        "dialogue-condition-false",
                        "Condition False",
                        "group-standard",
                        conditions: new[] { Condition(BoolGetter(false)) }),
                    ["dialogue-limited"] = Dialogue(
                        "dialogue-limited",
                        "Limited",
                        "group-standard",
                        occurrenceLimit: 1),
                    ["dialogue-condition-error"] = Dialogue(
                        "dialogue-condition-error",
                        "Condition Error",
                        "group-standard",
                        conditions: new[] { Condition(StringGetter("not bool")) }),
                    ["dialogue-conditions-node"] = ConditionsNodeDialogue(),
                    ["dialogue-action-assign"] = ActionDialogue(
                        "dialogue-action-assign",
                        AssignAction(
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "score-value",
                            },
                            IntTypeInfo(),
                            NumberPointer(5))),
                    ["dialogue-action-assign-compound"] = ActionDialogue(
                        "dialogue-action-assign-compound",
                        AssignAction(
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "score-value",
                            },
                            IntTypeInfo(),
                            ArithmeticPointer(
                                ArithmeticOpKind.Subtraction,
                                new ReferencePointer
                                {
                                    type = PointerKind.Reference,
                                    valueId = "score-value",
                                },
                                NumberPointer(100)),
                            "-=")),
                    ["dialogue-action-asset-write"] = ActionDialogue(
                        "dialogue-action-asset-write",
                        AssignAction(
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "asset-score-value",
                            },
                            IntTypeInfo(),
                            NumberPointer(9))),
                    ["dialogue-action-default-save-write"] = ActionDialogue(
                        "dialogue-action-default-save-write",
                        AssignAction(
                            RootKeyPointer("Save", "Score"),
                            IntTypeInfo(),
                            NumberPointer(22))),
                    ["dialogue-action-session-bool-write-with-inferred-ownership"] = ActionDialogue(
                        "dialogue-action-session-bool-write-with-inferred-ownership",
                        AssignAction(
                            RootKeyPointer("Session", "Foo"),
                            BoolTypeInfo(),
                            BoolPointer(true),
                            writability: null)),
                    ["dialogue-action-lookup-add"] = ActionDialogue(
                        "dialogue-action-lookup-add",
                        CollectionAction(
                            RootKeyPointer("Save", "Inventory"),
                            new LookupTypeInfo
                            {
                                type = AttributeType.Lookup,
                                required = true,
                                entryTypeInfo = new CustomTypeInfo
                                {
                                    type = AttributeType.Custom,
                                    required = true,
                                    typeId = "type-item",
                                },
                                collectionAttributeId = "attr-items",
                                collectionValueId = null,
                            },
                            CollectionMutationKind.Add,
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "asset-item-value-b",
                            })),
                    ["dialogue-action-list-add"] = ActionDialogue(
                        "dialogue-action-list-add",
                        CollectionAction(
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "list-value",
                            },
                            new CollectionTypeInfo
                            {
                                type = AttributeType.List,
                                required = true,
                                entryTypeInfo = StringTypeInfo(),
                            },
                            CollectionMutationKind.Add,
                            StringPointer("Potion"))),
                    ["dialogue-action-list-remove"] = ActionDialogue(
                        "dialogue-action-list-remove",
                        CollectionAction(
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "list-value",
                            },
                            ListTypeInfo(StringTypeInfo()),
                            CollectionMutationKind.Remove,
                            StringPointer("Potion"))),
                    ["dialogue-action-list-remove-at"] = ActionDialogue(
                        "dialogue-action-list-remove-at",
                        CollectionAction(
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "list-value",
                            },
                            ListTypeInfo(StringTypeInfo()),
                            CollectionMutationKind.RemoveAt,
                            NumberPointer(0))),
                    ["dialogue-action-list-clear"] = ActionDialogue(
                        "dialogue-action-list-clear",
                        CollectionAction(
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "list-value",
                            },
                            ListTypeInfo(StringTypeInfo()),
                            CollectionMutationKind.Clear)),
                    ["dialogue-action-dict-add"] = ActionDialogue(
                        "dialogue-action-dict-add",
                        CollectionAction(
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "dict-value",
                            },
                            DictionaryTypeInfo(StringTypeInfo()),
                            CollectionMutationKind.Add,
                            StringPointer("slot"),
                            StringPointer("Potion"))),
                    ["dialogue-action-dict-remove"] = ActionDialogue(
                        "dialogue-action-dict-remove",
                        CollectionAction(
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "dict-value",
                            },
                            DictionaryTypeInfo(StringTypeInfo()),
                            CollectionMutationKind.Remove,
                            StringPointer("slot"))),
                    ["dialogue-action-dict-clear"] = ActionDialogue(
                        "dialogue-action-dict-clear",
                        CollectionAction(
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "dict-value",
                            },
                            DictionaryTypeInfo(StringTypeInfo()),
                            CollectionMutationKind.Clear)),
                    ["dialogue-action-custom-set"] = ActionDialogue(
                        "dialogue-action-custom-set",
                        AssignAction(
                            new KeyOfPointer
                            {
                                type = PointerKind.KeyOf,
                                keyOf = new KeyOf
                                {
                                    pointer = new ReferencePointer
                                    {
                                        type = PointerKind.Reference,
                                        valueId = "root-save-value",
                                    },
                                    key = StringPointer("Score"),
                                },
                            },
                            IntTypeInfo(),
                            NumberPointer(12))),
                    ["dialogue-action-primary-set"] = ActionDialogue(
                        "dialogue-action-primary-set",
                        AssignAction(
                            new KeyOfPointer
                            {
                                type = PointerKind.KeyOf,
                                keyOf = new KeyOf
                                {
                                    pointer = ContextKeyPointer("primary"),
                                    key = StringPointer("Score"),
                                },
                            },
                            IntTypeInfo(),
                            NumberPointer(15)),
                        primaryLinkedValueId: "root-save-value"),
                    ["dialogue-action-list-add-primary"] = ActionDialogue(
                        "dialogue-action-list-add-primary",
                        CollectionAction(
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "list-value",
                            },
                            ListTypeInfo(CustomTypeInfo("type-root")),
                            CollectionMutationKind.Add,
                            ContextKeyPointer("primary")),
                        primaryLinkedValueId: "root-save-value"),
                    ["dialogue-action-dict-add-primary"] = ActionDialogue(
                        "dialogue-action-dict-add-primary",
                        CollectionAction(
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "dict-value",
                            },
                            DictionaryTypeInfo(CustomTypeInfo("type-root")),
                            CollectionMutationKind.Add,
                            StringPointer("slot"),
                            ContextKeyPointer("primary")),
                        primaryLinkedValueId: "root-save-value"),
                    ["dialogue-action-lookup-add-primary"] = ActionDialogue(
                        "dialogue-action-lookup-add-primary",
                        CollectionAction(
                            new ReferencePointer
                            {
                                type = PointerKind.Reference,
                                valueId = "save-inventory-value",
                            },
                            LookupTypeInfo("attr-items", CustomTypeInfo("type-item")),
                            CollectionMutationKind.Add,
                            ContextKeyPointer("primary")),
                        primaryLinkedValueId: "asset-item-value"),
                    ["dialogue-action-lookup-add-root-path"] = ActionDialogue(
                        "dialogue-action-lookup-add-root-path",
                        CollectionAction(
                            RootKeyPointer("Save", "Inventory"),
                            LookupTypeInfo("attr-items", CustomTypeInfo("type-item")),
                            CollectionMutationKind.Add,
                            ContextKeyPointer("primary")),
                        primaryLinkedValueId: "asset-item-value"),
                    ["dialogue-action-error"] = ActionDialogue(
                        "dialogue-action-error",
                        ThrowAction("boom")),
                    ["dialogue-action-pause-manual"] = ActionsDialogue(
                        "dialogue-action-pause-manual",
                        PauseAction("pause-manual", "cutscene"),
                        EditAction(
                            "set-score-after-pause",
                            AssignAction(
                                new ReferencePointer
                                {
                                    type = PointerKind.Reference,
                                    valueId = "score-value",
                                },
                                IntTypeInfo(),
                                NumberPointer(5)))),
                    ["dialogue-action-pause-consecutive"] = ActionsDialogue(
                        "dialogue-action-pause-consecutive",
                        PauseAction("pause-one", "first"),
                        PauseAction("pause-two", "second"),
                        EditAction(
                            "set-score-after-second-pause",
                            AssignAction(
                                new ReferencePointer
                                {
                                    type = PointerKind.Reference,
                                    valueId = "score-value",
                                },
                                IntTypeInfo(),
                                NumberPointer(7)))),
                    ["dialogue-action-pause-auto-zero"] = ActionsDialogue(
                        "dialogue-action-pause-auto-zero",
                        PauseAction("pause-auto-zero", "zero", 0),
                        EditAction(
                            "set-score-after-zero-pause",
                            AssignAction(
                                new ReferencePointer
                                {
                                    type = PointerKind.Reference,
                                    valueId = "score-value",
                                },
                                IntTypeInfo(),
                                NumberPointer(9)))),
                    ["dialogue-action-pause-auto-delay"] = ActionsDialogue(
                        "dialogue-action-pause-auto-delay",
                        PauseAction("pause-auto-delay", "delay", 3),
                        EditAction(
                            "set-score-after-delay-pause",
                            AssignAction(
                                new ReferencePointer
                                {
                                    type = PointerKind.Reference,
                                    valueId = "score-value",
                                },
                                IntTypeInfo(),
                                NumberPointer(11)))),
                    ["dialogue-action-pause-only"] = ActionsDialogue(
                        "dialogue-action-pause-only",
                        PauseAction("pause-only", "manual forever")),
                    ["dialogue-action-pause-then-error"] = ActionsDialogue(
                        "dialogue-action-pause-then-error",
                        PauseAction("pause-before-error", "error"),
                        EditAction(
                            "throw-after-pause",
                            ThrowAction("boom-after-pause"))),
                    ["dialogue-action-deferred-score"] = ActionsDialogue(
                        "dialogue-action-deferred-score",
                        EditAction(
                            "set-score-after-deferred",
                            DeferredScoreAction())),
                    ["dialogue-priority-low"] = Dialogue(
                        "dialogue-priority-low",
                        "Priority Low",
                        "group-priority",
                        priorityTypeId: "priority-low"),
                    ["dialogue-priority-high"] = Dialogue(
                        "dialogue-priority-high",
                        "Priority High",
                        "group-priority",
                        priorityTypeId: "priority-high"),
                    ["dialogue-visit-a"] = Dialogue(
                        "dialogue-visit-a",
                        "Visit A",
                        "group-visits"),
                    ["dialogue-visit-b"] = Dialogue(
                        "dialogue-visit-b",
                        "Visit B",
                        "group-visits"),
                    ["dialogue-parent-condition"] = Dialogue(
                        "dialogue-parent-condition",
                        "Parent Condition",
                        "group-child-of-false"),
                    ["dialogue-node-primary"] = Dialogue(
                        "dialogue-node-primary",
                        "Node Primary",
                        "group-standard",
                        primaryLinkedValueId: "primary-dialogue",
                        textPrimaryLinkedValueId: "primary-text"),
                    ["dialogue-linked-values"] = Dialogue(
                        "dialogue-linked-values",
                        "Linked Values",
                        "group-standard",
                        linkedValueIds: new[] { "linked-value-a" }),
                    ["dialogue-context-condition"] = Dialogue(
                        "dialogue-context-condition",
                        "Context Condition",
                        "group-standard",
                        conditions: new[]
                        {
                            Condition(ContextEquals("dialogueId", "dialogue-context-condition")),
                        }),
                    ["dialogue-group-context-primary"] = Dialogue(
                        "dialogue-group-context-primary",
                        "Group Context Primary",
                        "group-context-primary",
                        primaryLinkedValueId: "primary-dialogue"),
                    ["dialogue-text-linked-values"] = Dialogue(
                        "dialogue-text-linked-values",
                        "Text Linked Values",
                        "group-standard",
                        textLinkedValueIds: new[] { "text-linked-value-a" }),
                    ["dialogue-text-variables-root"] = TextVariablesRootDialogue(),
                    ["dialogue-text-variable-primary"] = TextVariablePrimaryDialogue(),
                    ["dialogue-option-variable-primary"] = OptionVariablePrimaryDialogue(),
                    ["dialogue-text-variable-missing"] = TextVariableMissingDialogue(),
                    ["dialogue-lookup-a"] = Dialogue(
                        "dialogue-lookup-a",
                        "Lookup A",
                        "group-lookup",
                        "lookup-value-a"),
                    ["dialogue-lookup-direct"] = Dialogue(
                        "dialogue-lookup-direct",
                        "Lookup Direct",
                        "group-lookup",
                        "lookup-value-direct"),
                    ["dialogue-lookup-b"] = Dialogue(
                        "dialogue-lookup-b",
                        "Lookup B",
                        "group-lookup",
                        "lookup-value-b"),
                    ["dialogue-lookup-this-trigger"] = Dialogue(
                        "dialogue-lookup-this-trigger",
                        "Lookup This Trigger",
                        "group-lookup",
                        "lookup-value-this-trigger",
                        conditions: new[]
                        {
                            Condition(ThisEqualsContextTrigger()),
                        }),
                },
                priorityGroups = new Dictionary<string, PriorityGroup>
                {
                    ["priority-default"] = new PriorityGroup
                    {
                        id = "priority-default",
                        projectId = ProjectId,
                        name = "Default",
                        options = new[]
                        {
                            new PriorityType
                            {
                                id = "priority-high",
                                name = "High",
                            },
                            new PriorityType
                            {
                                id = "priority-low",
                                name = "Low",
                            },
                        },
                        createdAt = Now,
                        updatedAt = Now,
                    },
                },
            };

            return NeoTestSaveStack.ClientFromSchema(data);
        }

        private static NeoClient CreateClientWithLocalization(
            Dictionary<string, string?> values)
        {
            var client = CreateClient();
            client.Localization.TryAddLoadedLocale(new ProjectLocalizationLocaleFile
            {
                schemaVersion = 1,
                projectId = ProjectId,
                versionId = "version-1",
                locale = "en-US",
                formattingSyntax = "smart-format",
                values = values,
            });
            return client;
        }

        private static CustomAttribute RootAttribute(string id, string name)
        {
            return new CustomAttribute
            {
                id = id,
                projectId = ProjectId,
                name = name,
                type = AttributeType.Custom,
                customTypeId = "type-root",
                valueId = name == "Save"
                    ? "root-save-default-value"
                    : name == "Session"
                        ? "root-session-default-value"
                        : "root-assets-value",
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static Dialogue Dialogue(
            string id,
            string name,
            string groupId,
            string? lookupValueId = null,
            LogicCondition[]? conditions = null,
            string? priorityTypeId = null,
            int? relativeOrder = null,
            int? occurrenceLimit = null,
            string? primaryLinkedValueId = null,
            string? textPrimaryLinkedValueId = null,
            string[]? linkedValueIds = null,
            string[]? textLinkedValueIds = null)
        {
            return new Dialogue
            {
                id = id,
                projectId = ProjectId,
                name = name,
                description = null,
                linkedValues = LinkedValues(linkedValueIds),
                settings = new DialogueSettings(),
                primaryLinkedValueId = primaryLinkedValueId,
                triggerNode = new DialogueTriggerNode
                {
                    id = $"{id}-trigger",
                    type = DialogueNodeType.Trigger,
                    layout = new DialogueNodeLayout(),
                    toNodeId = "text-start",
                    linkedValues = new DialogueLinkedValue[0],
                    conditions = conditions ?? new LogicCondition[0],
                    occurrenceLimitSettings = occurrenceLimit == null
                        ? null
                        : new OccurrenceLimitSettings
                        {
                            count = occurrenceLimit.Value,
                        },
                    dialogueGroupSettings = new DialogueGroupSettings
                    {
                        dialogueGroupId = groupId,
                        lookupValueId = lookupValueId,
                        priority = new DialogueGroupPrioritySettings
                        {
                            priorityTypeId = priorityTypeId,
                            relativeOrder = relativeOrder,
                        },
                    },
                },
                nodes = new Dictionary<string, DialogueBodyNode>
                {
                    ["text-start"] = TextNode(
                        "text-start",
                        "Hello there.",
                        primaryLinkedValueId: textPrimaryLinkedValueId,
                        linkedValueIds: textLinkedValueIds),
                },
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static DialogueLinkedValue[] LinkedValues(string[]? valueIds)
        {
            if (valueIds == null) return new DialogueLinkedValue[0];
            var result = new DialogueLinkedValue[valueIds.Length];
            for (int i = 0; i < valueIds.Length; i++)
            {
                result[i] = new DialogueLinkedValue
                {
                    valueId = valueIds[i],
                    source = DialogueLinkedValueSource.Manual,
                };
            }
            return result;
        }

        private static Dialogue ActionDialogue(
            string id,
            FunctionWithReturnType action,
            string? primaryLinkedValueId = null)
        {
            return new Dialogue
            {
                id = id,
                projectId = ProjectId,
                name = id,
                description = null,
                linkedValues = new DialogueLinkedValue[0],
                settings = new DialogueSettings(),
                primaryLinkedValueId = primaryLinkedValueId,
                triggerNode = new DialogueTriggerNode
                {
                    id = $"{id}-trigger",
                    type = DialogueNodeType.Trigger,
                    layout = new DialogueNodeLayout(),
                    toNodeId = "actions-start",
                    linkedValues = new DialogueLinkedValue[0],
                    conditions = new LogicCondition[0],
                    dialogueGroupSettings = new DialogueGroupSettings
                    {
                        dialogueGroupId = "group-standard",
                        priority = new DialogueGroupPrioritySettings(),
                    },
                },
                nodes = new Dictionary<string, DialogueBodyNode>
                {
                    ["actions-start"] = new DialogueActionsNode
                    {
                        id = "actions-start",
                        type = DialogueNodeType.Actions,
                        layout = new DialogueNodeLayout(),
                        linkedValues = new DialogueLinkedValue[0],
                        toNodeId = "text-after-action",
                        actions = new DialogueAction[]
                        {
                            new DialogueLogicEditAttributeAction
                            {
                                id = $"{id}-action",
                                type = DialogueActionType.EditAttribute,
                                logic = new UILogicAction
                                {
                                    type = LogicType.UI,
                                    action = action,
                                },
                            },
                        },
                    },
                    ["text-after-action"] = TextNode("text-after-action", "Action completed."),
                },
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static Dialogue ActionsDialogue(
            string id,
            params DialogueAction[] actions)
        {
            return new Dialogue
            {
                id = id,
                projectId = ProjectId,
                name = id,
                description = null,
                linkedValues = new DialogueLinkedValue[0],
                settings = new DialogueSettings(),
                primaryLinkedValueId = null,
                triggerNode = new DialogueTriggerNode
                {
                    id = $"{id}-trigger",
                    type = DialogueNodeType.Trigger,
                    layout = new DialogueNodeLayout(),
                    toNodeId = "actions-start",
                    linkedValues = new DialogueLinkedValue[0],
                    conditions = new LogicCondition[0],
                    dialogueGroupSettings = new DialogueGroupSettings
                    {
                        dialogueGroupId = "group-standard",
                        priority = new DialogueGroupPrioritySettings(),
                    },
                },
                nodes = new Dictionary<string, DialogueBodyNode>
                {
                    ["actions-start"] = new DialogueActionsNode
                    {
                        id = "actions-start",
                        type = DialogueNodeType.Actions,
                        layout = new DialogueNodeLayout(),
                        linkedValues = new DialogueLinkedValue[0],
                        toNodeId = "text-after-action",
                        actions = actions,
                    },
                    ["text-after-action"] = TextNode("text-after-action", "Action completed."),
                },
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static DialogueLogicEditAttributeAction EditAction(
            string id,
            FunctionWithReturnType action)
        {
            return new DialogueLogicEditAttributeAction
            {
                id = id,
                type = DialogueActionType.EditAttribute,
                logic = new UILogicAction
                {
                    type = LogicType.UI,
                    action = action,
                },
            };
        }

        private static DialoguePauseAction PauseAction(
            string id,
            string reason,
            double? autoResumeDurationSeconds = null)
        {
            return new DialoguePauseAction
            {
                id = id,
                type = DialogueActionType.Pause,
                reason = reason,
                autoResumeDurationSeconds = autoResumeDurationSeconds,
            };
        }

        private static Dialogue ConditionsNodeDialogue()
        {
            return new Dialogue
            {
                id = "dialogue-conditions-node",
                projectId = ProjectId,
                name = "Conditions Node Dialogue",
                description = null,
                linkedValues = new DialogueLinkedValue[0],
                settings = new DialogueSettings(),
                primaryLinkedValueId = null,
                triggerNode = new DialogueTriggerNode
                {
                    id = "dialogue-conditions-node-trigger",
                    type = DialogueNodeType.Trigger,
                    layout = new DialogueNodeLayout(),
                    toNodeId = "conditions-start",
                    linkedValues = new DialogueLinkedValue[0],
                    conditions = new LogicCondition[0],
                    dialogueGroupSettings = new DialogueGroupSettings
                    {
                        dialogueGroupId = "group-standard",
                        priority = new DialogueGroupPrioritySettings(),
                    },
                },
                nodes = new Dictionary<string, DialogueBodyNode>
                {
                    ["conditions-start"] = new DialogueConditionsNode
                    {
                        id = "conditions-start",
                        type = DialogueNodeType.Conditions,
                        layout = new DialogueNodeLayout(),
                        linkedValues = new DialogueLinkedValue[0],
                        outcomes = new[]
                        {
                            new DialogueOutcome
                            {
                                id = "false-outcome",
                                name = "False",
                                toNodeId = "text-false",
                                conditions = new[] { Condition(BoolGetter(false)) },
                            },
                            new DialogueOutcome
                            {
                                id = "true-outcome",
                                name = "True",
                                toNodeId = "text-true",
                                conditions = new[] { Condition(BoolGetter(true)) },
                            },
                        },
                    },
                    ["text-false"] = TextNode("text-false", "The false branch."),
                    ["text-true"] = TextNode("text-true", "The true branch."),
                },
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static Dialogue OptionsDialogue()
        {
            return new Dialogue
            {
                id = "dialogue-options",
                projectId = ProjectId,
                name = "Choice Dialogue",
                description = null,
                linkedValues = new DialogueLinkedValue[0],
                settings = new DialogueSettings { defaultSaveOptionChoices = true },
                primaryLinkedValueId = null,
                triggerNode = new DialogueTriggerNode
                {
                    id = "dialogue-options-trigger",
                    type = DialogueNodeType.Trigger,
                    layout = new DialogueNodeLayout(),
                    toNodeId = "text-choice",
                    linkedValues = new DialogueLinkedValue[0],
                    conditions = new LogicCondition[0],
                    dialogueGroupSettings = new DialogueGroupSettings
                    {
                        dialogueGroupId = "group-standard",
                        priority = new DialogueGroupPrioritySettings(),
                    },
                },
                nodes = new Dictionary<string, DialogueBodyNode>
                {
                    ["text-choice"] = TextNode(
                        "text-choice",
                        "Pick one.",
                        new[]
                        {
                            new NeoCompose.Runtime.Json.DialogueTextOption
                            {
                                id = "option-a",
                                text = "A",
                                toNodeId = "text-after-choice",
                            },
                            new NeoCompose.Runtime.Json.DialogueTextOption
                            {
                                id = "option-b",
                                text = "B",
                            },
                        }),
                    ["text-after-choice"] = TextNode("text-after-choice", "You picked A."),
                },
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static Dialogue OptionsNoSaveChoiceDialogue()
        {
            return new Dialogue
            {
                id = "dialogue-options-no-save",
                projectId = ProjectId,
                name = "Choice Dialogue Without Saved Choice",
                description = null,
                linkedValues = new DialogueLinkedValue[0],
                settings = new DialogueSettings { defaultSaveOptionChoices = false },
                primaryLinkedValueId = null,
                triggerNode = new DialogueTriggerNode
                {
                    id = "dialogue-options-no-save-trigger",
                    type = DialogueNodeType.Trigger,
                    layout = new DialogueNodeLayout(),
                    toNodeId = "text-choice-no-save",
                    linkedValues = new DialogueLinkedValue[0],
                    conditions = new LogicCondition[0],
                    dialogueGroupSettings = new DialogueGroupSettings
                    {
                        dialogueGroupId = "group-standard",
                        priority = new DialogueGroupPrioritySettings(),
                    },
                },
                nodes = new Dictionary<string, DialogueBodyNode>
                {
                    ["text-choice-no-save"] = TextNode(
                        "text-choice-no-save",
                        "Pick one.",
                        new[]
                        {
                            new NeoCompose.Runtime.Json.DialogueTextOption
                            {
                                id = "option-no-save",
                                text = "No save",
                            },
                        }),
                },
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static Dialogue OptionSettingsDialogue()
        {
            return new Dialogue
            {
                id = "dialogue-option-settings",
                projectId = ProjectId,
                name = "Option Settings Dialogue",
                description = null,
                linkedValues = new DialogueLinkedValue[0],
                settings = new DialogueSettings(),
                primaryLinkedValueId = null,
                triggerNode = new DialogueTriggerNode
                {
                    id = "dialogue-option-settings-trigger",
                    type = DialogueNodeType.Trigger,
                    layout = new DialogueNodeLayout(),
                    toNodeId = "text-option-settings",
                    linkedValues = new DialogueLinkedValue[0],
                    conditions = new LogicCondition[0],
                    dialogueGroupSettings = new DialogueGroupSettings
                    {
                        dialogueGroupId = "group-standard",
                        priority = new DialogueGroupPrioritySettings(),
                    },
                },
                nodes = new Dictionary<string, DialogueBodyNode>
                {
                    ["text-option-settings"] = TextNode(
                        "text-option-settings",
                        "Pick one.",
                        new[]
                        {
                            new NeoCompose.Runtime.Json.DialogueTextOption
                            {
                                id = "option-hidden",
                                text = "Hidden",
                                settings = new DialogueTextOptionSettings
                                {
                                    conditions = new[] { Condition(BoolGetter(false)) },
                                    selectableConditions = new[] { Condition(StringGetter("should not evaluate")) },
                                },
                            },
                            new NeoCompose.Runtime.Json.DialogueTextOption
                            {
                                id = "option-disabled",
                                text = "Disabled",
                                settings = new DialogueTextOptionSettings
                                {
                                    selectableConditions = new[] { Condition(BoolGetter(false)) },
                                },
                            },
                            new NeoCompose.Runtime.Json.DialogueTextOption
                            {
                                id = "option-visible",
                                text = "Visible",
                                settings = new DialogueTextOptionSettings
                                {
                                    conditions = new[] { Condition(ContextEquals("optionId", "option-visible")) },
                                    selectableConditions = new[] { Condition(ContextEquals("optionId", "option-visible")) },
                                },
                            },
                        }),
                },
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static Dialogue OptionConditionErrorDialogue()
        {
            return new Dialogue
            {
                id = "dialogue-option-condition-error",
                projectId = ProjectId,
                name = "Option Condition Error Dialogue",
                description = null,
                linkedValues = new DialogueLinkedValue[0],
                settings = new DialogueSettings(),
                primaryLinkedValueId = null,
                triggerNode = new DialogueTriggerNode
                {
                    id = "dialogue-option-condition-error-trigger",
                    type = DialogueNodeType.Trigger,
                    layout = new DialogueNodeLayout(),
                    toNodeId = "text-option-condition-error",
                    linkedValues = new DialogueLinkedValue[0],
                    conditions = new LogicCondition[0],
                    dialogueGroupSettings = new DialogueGroupSettings
                    {
                        dialogueGroupId = "group-standard",
                        priority = new DialogueGroupPrioritySettings(),
                    },
                },
                nodes = new Dictionary<string, DialogueBodyNode>
                {
                    ["text-option-condition-error"] = TextNode(
                        "text-option-condition-error",
                        "Pick one.",
                        new[]
                        {
                            new NeoCompose.Runtime.Json.DialogueTextOption
                            {
                                id = "option-error",
                                text = "Error",
                                settings = new DialogueTextOptionSettings
                                {
                                    conditions = new[] { Condition(StringGetter("not bool")) },
                                },
                            },
                        }),
                },
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static Dialogue TextVariablesRootDialogue()
        {
            return new Dialogue
            {
                id = "dialogue-text-variables-root",
                projectId = ProjectId,
                name = "Text Variables Root Dialogue",
                description = null,
                linkedValues = new DialogueLinkedValue[0],
                settings = new DialogueSettings(),
                primaryLinkedValueId = null,
                triggerNode = Trigger("dialogue-text-variables-root", "text-variable-root"),
                nodes = new Dictionary<string, DialogueBodyNode>
                {
                    ["text-variable-root"] = TextNode(
                        "text-variable-root",
                        "Score {{neo-var:score}}, asset score {{neo-var:asset-score}}.",
                        variables: new Dictionary<string, DialogueTextVariable>
                        {
                            ["score"] = TextVariable(
                                "score",
                                RootKeyPointer("Save", "Score"),
                                IntTypeInfo()),
                            ["asset-score"] = TextVariable(
                                "asset-score",
                                RootKeyPointer("Assets", "Score"),
                                IntTypeInfo()),
                        }),
                },
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static Dialogue TextVariablePrimaryDialogue()
        {
            return new Dialogue
            {
                id = "dialogue-text-variable-primary",
                projectId = ProjectId,
                name = "Text Variable Primary Dialogue",
                description = null,
                linkedValues = new DialogueLinkedValue[0],
                settings = new DialogueSettings(),
                primaryLinkedValueId = "asset-item-value",
                triggerNode = Trigger("dialogue-text-variable-primary", "text-variable-primary"),
                nodes = new Dictionary<string, DialogueBodyNode>
                {
                    ["text-variable-primary"] = TextNode(
                        "text-variable-primary",
                        "Hello {{neo-var:item-name}}.",
                        variables: new Dictionary<string, DialogueTextVariable>
                        {
                            ["item-name"] = TextVariable(
                                "item-name",
                                KeyOfPointer(ThisPointer(), "Name"),
                                StringTypeInfo()),
                        }),
                },
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static Dialogue OptionVariablePrimaryDialogue()
        {
            return new Dialogue
            {
                id = "dialogue-option-variable-primary",
                projectId = ProjectId,
                name = "Option Variable Primary Dialogue",
                description = null,
                linkedValues = new DialogueLinkedValue[0],
                settings = new DialogueSettings(),
                primaryLinkedValueId = "asset-item-value",
                triggerNode = Trigger("dialogue-option-variable-primary", "text-option-variable"),
                nodes = new Dictionary<string, DialogueBodyNode>
                {
                    ["text-option-variable"] = TextNode(
                        "text-option-variable",
                        "Pick one.",
                        new[]
                        {
                            new NeoCompose.Runtime.Json.DialogueTextOption
                            {
                                id = "option-variable",
                                text = "Take {{neo-var:item-name}}",
                                variables = new Dictionary<string, DialogueTextVariable>
                                {
                                    ["item-name"] = TextVariable(
                                        "item-name",
                                        KeyOfPointer(ThisPointer(), "Name"),
                                        StringTypeInfo()),
                                },
                            },
                        }),
                },
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static Dialogue TextVariableMissingDialogue()
        {
            return new Dialogue
            {
                id = "dialogue-text-variable-missing",
                projectId = ProjectId,
                name = "Text Variable Missing Dialogue",
                description = null,
                linkedValues = new DialogueLinkedValue[0],
                settings = new DialogueSettings(),
                primaryLinkedValueId = null,
                triggerNode = Trigger("dialogue-text-variable-missing", "text-variable-missing"),
                nodes = new Dictionary<string, DialogueBodyNode>
                {
                    ["text-variable-missing"] = TextNode(
                        "text-variable-missing",
                        "Missing {{neo-var:missing}}."),
                },
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static LogicCondition Condition(FunctionWithReturnType getter)
        {
            return new UILogicCondition
            {
                type = LogicType.UI,
                getter = getter,
            };
        }

        private static FunctionWithReturnType BoolGetter(bool value)
        {
            return Getter(
                new PrimitiveTypeInfo
                {
                    type = AttributeType.Bool,
                    required = true,
                },
                JToken.FromObject(value));
        }

        private static FunctionWithReturnType StringGetter(string value)
        {
            return Getter(
                new PrimitiveTypeInfo
                {
                    type = AttributeType.String,
                    required = true,
                },
                JToken.FromObject(value));
        }

        private static FunctionWithReturnType StringGetterFromPointer(
            Pointer pointer,
            TypeInfo sourceType)
        {
            return new FunctionWithReturnType
            {
                typeInfo = StringTypeInfo(),
                parameters = new Variable[0],
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new StringifyPointer
                        {
                            type = PointerKind.Stringify,
                            pointer = pointer,
                            sourceType = sourceType,
                        },
                    },
                },
            };
        }

        private static FunctionWithReturnType Getter(TypeInfo typeInfo, JToken value)
        {
            return new FunctionWithReturnType
            {
                typeInfo = typeInfo,
                parameters = new Variable[0],
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new ValuePointer
                        {
                            type = PointerKind.Value,
                            value = new Value
                            {
                                typeInfo = typeInfo,
                                value = value,
                            },
                        },
                    },
                },
            };
        }

        private static FunctionWithReturnType ContextEquals(string key, string value)
        {
            return BoolExpressionGetter(
                ContextKeyPointer(key),
                OperatorKind.EqualTo,
                StringPointer(value));
        }

        private static FunctionWithReturnType ContextIsNotNull(string key)
        {
            return BoolExpressionGetter(
                ContextKeyPointer(key),
                OperatorKind.DoesNotEqual,
                NullPointer());
        }

        private static FunctionWithReturnType ThisEqualsContextTrigger()
        {
            return BoolExpressionGetter(
                ThisPointer(),
                OperatorKind.EqualTo,
                ContextKeyPointer("trigger"));
        }

        private static FunctionWithReturnType BoolExpressionGetter(
            Pointer operand1,
            string operatorKind,
            Pointer operand2)
        {
            return new FunctionWithReturnType
            {
                typeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.Bool,
                    required = true,
                },
                parameters = new Variable[0],
                instructions = new Instruction[]
                {
                    new ReturnInstruction
                    {
                        type = InstructionKind.Return,
                        pointer = new OperationPointer
                        {
                            type = PointerKind.Operation,
                            operation = new BooleanOperation
                            {
                                type = OperationKind.Boolean,
                                expression = new BooleanExpression
                                {
                                    condition = new Condition
                                    {
                                        type = operatorKind,
                                        operand1 = operand1,
                                        operand2 = operand2,
                                    },
                                },
                            },
                        },
                    },
                },
            };
        }

        private static Pointer ThisPointer()
        {
            return new VariablePointer
            {
                type = PointerKind.Variable,
                variableId = "__this__",
            };
        }

        private static Pointer ContextKeyPointer(string key)
        {
            return new KeyOfPointer
            {
                type = PointerKind.KeyOf,
                keyOf = new KeyOf
                {
                    pointer = new VariablePointer
                    {
                        type = PointerKind.Variable,
                        variableId = "__context__",
                    },
                    key = StringPointer(key),
                },
            };
        }

        private static Pointer KeyOfPointer(Pointer pointer, string key)
        {
            return new KeyOfPointer
            {
                type = PointerKind.KeyOf,
                keyOf = new KeyOf
                {
                    pointer = pointer,
                    key = StringPointer(key),
                },
            };
        }

        private static Pointer RootKeyPointer(params string[] keys)
        {
            Pointer pointer = new VariablePointer
            {
                type = PointerKind.Variable,
                variableId = "__root__",
            };
            foreach (var key in keys)
            {
                pointer = new KeyOfPointer
                {
                    type = PointerKind.KeyOf,
                    keyOf = new KeyOf
                    {
                        pointer = pointer,
                        key = StringPointer(key),
                    },
                };
            }
            return pointer;
        }

        private static Pointer NullPointer()
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo
                    {
                        type = AttributeType.Null,
                        required = false,
                    },
                    value = JValue.CreateNull(),
                },
            };
        }

        private static FunctionWithReturnType AssignAction(
            Pointer target,
            TypeInfo typeInfo,
            Pointer value,
            string operatorValue = "=",
            string? writability = WritabilityKind.Save)
        {
            return ActionFunction(new Instruction[]
            {
                new AssignInstruction
                {
                    type = InstructionKind.Assign,
                    target = new WriteTarget
                    {
                        pointer = target,
                        typeInfo = typeInfo,
                        writability = writability,
                    },
                    operatorValue = operatorValue,
                    pointer = value,
                },
            });
        }

        private static FunctionWithReturnType CollectionAction(
            Pointer target,
            TypeInfo typeInfo,
            string mutation,
            params Pointer[] args)
        {
            return ActionFunction(new Instruction[]
            {
                new CollectionCallInstruction
                {
                    type = InstructionKind.CollectionCall,
                    target = new WriteTarget
                    {
                        pointer = target,
                        typeInfo = typeInfo,
                        writability = WritabilityKind.Save,
                    },
                    mutation = mutation,
                    args = args,
                },
            });
        }

        private static FunctionWithReturnType ThrowAction(string message)
        {
            return ActionFunction(new Instruction[]
            {
                new ThrowInstruction
                {
                    type = InstructionKind.Throw,
                    pointer = StringPointer(message),
                },
            });
        }

        private static FunctionWithReturnType DeferredScoreAction()
        {
            return ActionFunction(new Instruction[]
            {
                new VariableInstruction
                {
                    type = InstructionKind.Variable,
                    variable = new Variable
                    {
                        id = "score",
                        typeInfo = IntTypeInfo(),
                        pointer = new CallNativeFunctionPointer
                        {
                            type = PointerKind.CallNativeFunction,
                            attributeId = "attr-deferred-score",
                            thisPointer = NullPointer(),
                            args = new Pointer[0],
                        },
                    },
                },
                new AssignInstruction
                {
                    type = InstructionKind.Assign,
                    target = new WriteTarget
                    {
                        pointer = new ReferencePointer
                        {
                            type = PointerKind.Reference,
                            valueId = "score-value",
                        },
                        typeInfo = IntTypeInfo(),
                        writability = WritabilityKind.Save,
                    },
                    operatorValue = "=",
                    pointer = new VariablePointer
                    {
                        type = PointerKind.Variable,
                        variableId = "score",
                    },
                },
            });
        }

        private static FunctionWithReturnType ActionFunction(Instruction[] instructions)
        {
            return new FunctionWithReturnType
            {
                typeInfo = new PrimitiveTypeInfo
                {
                    type = AttributeType.Null,
                    required = false,
                },
                parameters = new Variable[0],
                instructions = instructions,
            };
        }

        private static PrimitiveTypeInfo IntTypeInfo()
        {
            return new PrimitiveTypeInfo
            {
                type = AttributeType.Int,
                required = true,
            };
        }

        private static PrimitiveTypeInfo BoolTypeInfo()
        {
            return new PrimitiveTypeInfo
            {
                type = AttributeType.Bool,
                required = true,
            };
        }

        private static PrimitiveTypeInfo StringTypeInfo()
        {
            return new PrimitiveTypeInfo
            {
                type = AttributeType.String,
                required = true,
            };
        }

        private static CollectionTypeInfo ListTypeInfo(TypeInfo entryTypeInfo)
        {
            return new CollectionTypeInfo
            {
                type = AttributeType.List,
                required = true,
                entryTypeInfo = entryTypeInfo,
            };
        }

        private static CollectionTypeInfo DictionaryTypeInfo(TypeInfo entryTypeInfo)
        {
            return new CollectionTypeInfo
            {
                type = AttributeType.Dictionary,
                required = true,
                entryTypeInfo = entryTypeInfo,
            };
        }

        private static LookupTypeInfo LookupTypeInfo(
            string collectionAttributeId,
            TypeInfo entryTypeInfo)
        {
            return new LookupTypeInfo
            {
                type = AttributeType.Lookup,
                required = true,
                collectionAttributeId = collectionAttributeId,
                collectionValueId = null,
                entryTypeInfo = entryTypeInfo,
            };
        }

        private static CustomTypeInfo CustomTypeInfo(string typeId)
        {
            return new CustomTypeInfo
            {
                type = AttributeType.Custom,
                required = true,
                typeId = typeId,
            };
        }

        private static Pointer NumberPointer(double value)
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = IntTypeInfo(),
                    value = JToken.FromObject(value),
                },
            };
        }

        private static Pointer BoolPointer(bool value)
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = BoolTypeInfo(),
                    value = JToken.FromObject(value),
                },
            };
        }

        private static Pointer ArithmeticPointer(string op, params Pointer[] pointers)
        {
            return new OperationPointer
            {
                type = PointerKind.Operation,
                operation = new ArithmeticOperation
                {
                    type = OperationKind.Arithmetic,
                    arithmetic = new ArithmeticOpInfo
                    {
                        type = op,
                        pointers = pointers,
                    },
                },
            };
        }

        private static Pointer StringPointer(string value)
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = StringTypeInfo(),
                    value = JToken.FromObject(value),
                },
            };
        }

        private static void SeedList(NeoClient client, string listValueId)
        {
            client.SetSaveValue(new StringAttributeValue
            {
                id = "list-entry-potion",
                createdAt = Now,
                updatedAt = Now,
                value = "Potion",
            });
            client.SetSaveValue(new StringAttributeValue
            {
                id = "list-entry-elixir",
                createdAt = Now,
                updatedAt = Now,
                value = "Elixir",
            });
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = listValueId,
                createdAt = Now,
                updatedAt = Now,
                value = new[] { "list-entry-potion", "list-entry-elixir" },
            });
        }

        private static void SeedDictionary(NeoClient client, string dictionaryValueId)
        {
            client.SetSaveValue(new StringAttributeValue
            {
                id = "dict-entry-slot",
                createdAt = Now,
                updatedAt = Now,
                value = "Potion",
            });
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = dictionaryValueId,
                createdAt = Now,
                updatedAt = Now,
                value = new Dictionary<string, string>
                {
                    ["slot"] = "dict-entry-slot",
                },
            });
        }

        private static DialogueTextNode TextNode(
            string id,
            string text,
            NeoCompose.Runtime.Json.DialogueTextOption[]? options = null,
            string? primaryLinkedValueId = null,
            string[]? linkedValueIds = null,
            Dictionary<string, DialogueTextVariable>? variables = null)
        {
            return new DialogueTextNode
            {
                id = id,
                type = DialogueNodeType.Text,
                layout = new DialogueNodeLayout(),
                text = text,
                variables = variables,
                primaryLinkedValueId = primaryLinkedValueId,
                linkedValues = LinkedValues(linkedValueIds),
                optionSettings = options == null
                    ? null
                    : new DialogueOptionSettings
                    {
                        options = options,
                    },
            };
        }

        private static DialogueTriggerNode Trigger(string dialogueId, string toNodeId)
        {
            return new DialogueTriggerNode
            {
                id = $"{dialogueId}-trigger",
                type = DialogueNodeType.Trigger,
                layout = new DialogueNodeLayout(),
                toNodeId = toNodeId,
                linkedValues = new DialogueLinkedValue[0],
                conditions = new LogicCondition[0],
                dialogueGroupSettings = new DialogueGroupSettings
                {
                    dialogueGroupId = "group-standard",
                    priority = new DialogueGroupPrioritySettings(),
                },
            };
        }

        private static DialogueTextVariable TextVariable(
            string id,
            Pointer pointer,
            TypeInfo sourceType)
        {
            return new DialogueTextVariable
            {
                id = id,
                sourcePath = id,
                displayPath = id,
                label = id,
                typeInfo = sourceType,
                pointer = pointer,
                getter = StringGetterFromPointer(pointer, sourceType),
            };
        }

        private static object? ResolveClientValue(NeoClient client, string valueId)
        {
            if (!client.TryGetValue(valueId, out AttributeValue? row)) return null;
            var ctx = new NeoCompose.Runtime.NeoScript.NSGetterEvaluator.Context(
                client,
                thisValue: null,
                rootValue: null);
            return NeoCompose.Runtime.NeoScript.NSGetterEvaluator.UnwrapRow(row, ctx);
        }

        private sealed class ManualPauseScheduler : INeoDialoguePauseScheduler
        {
            private readonly List<System.Action> callbacks = new();

            public int PendingCount => callbacks.Count;

            public System.IDisposable Schedule(
                System.TimeSpan delay,
                System.Action callback)
            {
                callbacks.Add(callback);
                return new TestDisposable(() => callbacks.Remove(callback));
            }

            public void RunNext()
            {
                var callback = callbacks[0];
                callbacks.RemoveAt(0);
                callback();
            }

            public void RunAll()
            {
                while (callbacks.Count > 0)
                {
                    RunNext();
                }
            }
        }

        private sealed class TestDisposable : System.IDisposable
        {
            private System.Action? dispose;

            public TestDisposable(System.Action dispose)
            {
                this.dispose = dispose;
            }

            public void Dispose()
            {
                var current = dispose;
                dispose = null;
                current?.Invoke();
            }
        }

        private sealed class TestDialogueLogger : INeoDialogueLogger
        {
            public readonly List<string> Warnings = new();
            public readonly List<string> Errors = new();
            public readonly List<System.Exception> Exceptions = new();

            public void LogWarning(string message) => Warnings.Add(message);
            public void LogError(string message) => Errors.Add(message);
            public void LogException(System.Exception exception) => Exceptions.Add(exception);
        }

        private sealed class TestDialogues : NeoDialoguesBase
        {
            public TestDialogues(
                NeoClient client,
                NeoDialogueRuntimeOptions? options = null,
                INeoDialogueMemoryStore? memoryStore = null,
                NeoDialogueValueResolver? valueResolver = null)
                : base(client, options, memoryStore, valueResolver) { }
        }

        private sealed class TestStandardDialogueGroup : NeoStandardDialogueGroup
        {
            public TestStandardDialogueGroup(NeoDialoguesBase root, string groupId)
                : base(root, groupId) { }

            public bool TryTrigger(out NeoDialogueTriggerResult result)
            {
                return TryTriggerStandard(out result);
            }
        }

        private sealed class TestLookupDialogueGroup : NeoLookupDialogueGroup<TestLookupValue>
        {
            public TestLookupDialogueGroup(NeoDialoguesBase root, string groupId)
                : base(root, groupId) { }

            public bool TryTrigger(TestLookupValue lookup, out NeoDialogueTriggerResult result)
            {
                return TryTriggerLookup(lookup, out result);
            }
        }

        private class TestLookupValue : INeoValueReference
        {
            public string? valueId { get; }

            public TestLookupValue(string? valueId)
            {
                this.valueId = valueId;
            }
        }

        private sealed class DerivedTestLookupValue : TestLookupValue
        {
            public DerivedTestLookupValue(string? valueId)
                : base(valueId) { }
        }

        private sealed class TestMemoryStore : INeoDialogueMemoryStore
        {
            private readonly Dictionary<string, TestDialogueMemory> dialogues = new();

            public TestDialogueMemory GetOrCreateTestDialogueMemory(string dialogueId)
            {
                return (TestDialogueMemory)GetOrCreateDialogueMemory(dialogueId);
            }

            public INeoDialogueMemory GetOrCreateDialogueMemory(string dialogueId)
            {
                if (!dialogues.TryGetValue(dialogueId, out TestDialogueMemory memory))
                {
                    memory = new TestDialogueMemory();
                    dialogues[dialogueId] = memory;
                }
                return memory;
            }

            public INeoDialogueMemory? FindDialogueMemory(string dialogueId)
            {
                return dialogues.TryGetValue(dialogueId, out TestDialogueMemory memory)
                    ? memory
                    : null;
            }
        }

        private sealed class TestDialogueMemory : INeoDialogueMemory
        {
            private readonly Dictionary<string, TestTextNodeMemory> textNodes = new();

            public int VisitCount { get; set; }
            public string? LastVisitedAt { get; set; }

            public INeoTextNodeMemory GetOrCreateTextNodeMemory(string textNodeId)
            {
                if (!textNodes.TryGetValue(textNodeId, out TestTextNodeMemory memory))
                {
                    memory = new TestTextNodeMemory();
                    textNodes[textNodeId] = memory;
                }
                return memory;
            }

            public INeoTextNodeMemory? FindTextNodeMemory(string textNodeId)
            {
                return textNodes.TryGetValue(textNodeId, out TestTextNodeMemory memory)
                    ? memory
                    : null;
            }
        }

        private sealed class TestTextNodeMemory : INeoTextNodeMemory
        {
            private readonly HashSet<string> choices = new();

            public int VisitCount { get; set; }
            public string? LastVisitedAt { get; set; }
            public string? MostRecentChoiceId { get; set; }

            public bool HasChoice(string choiceId)
            {
                return choices.Contains(choiceId);
            }

            public void AddChoice(string choiceId, string createdAt)
            {
                choices.Add(choiceId);
            }
        }
    }
}
