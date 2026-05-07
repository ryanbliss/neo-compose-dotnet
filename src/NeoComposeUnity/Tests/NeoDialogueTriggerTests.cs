// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
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
            Assert.AreEqual(result.Dialogue.Context.Trigger, result.Dialogue.Context.Primary);
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
            Assert.AreSame(result.Dialogue.Context.Trigger, result.Dialogue.Context.Primary);
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

            dialogue.Start();

            var dialogueMemory = memory.FindDialogueMemory("dialogue-direct");
            Assert.IsNotNull(dialogueMemory);
            Assert.AreEqual(1, dialogueMemory!.VisitCount);
            Assert.AreEqual("2026-05-07T12:34:56.0000000Z", dialogueMemory.LastVisitedAt);

            var textMemory = dialogueMemory.FindTextNodeMemory("text-start");
            Assert.IsNotNull(textMemory);
            Assert.AreEqual(1, textMemory!.VisitCount);
            Assert.AreEqual("2026-05-07T12:34:56.0000000Z", textMemory.LastVisitedAt);
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
            shown[0].Options[0].Select();

            var textMemory = memory
                .FindDialogueMemory("dialogue-options")!
                .FindTextNodeMemory("text-choice");
            Assert.IsNotNull(textMemory);
            Assert.AreEqual("option-a", textMemory!.MostRecentChoiceId);
            Assert.IsTrue(textMemory.HasChoice("option-a"));
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
                value = 1,
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-assign-compound", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("score-value", out NumberAttributeValue? score));
            Assert.AreEqual(5, score!.value);
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

            Assert.IsTrue(client.TryGetValue("root-save-default-value", out ObjectAttributeValue? saveRoot));
            Assert.AreEqual("score-default-value", saveRoot!.value!["Score"]);
            Assert.IsTrue(client.TryGetValue("score-default-value", out NumberAttributeValue? score));
            Assert.AreEqual(22, score!.value);
            Assert.AreEqual("root-save-default-value", client.saveOverrides["root-save"]);
            CollectionAssert.IsEmpty(client.FindUnlinkedSaveValueIds());
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
            client.AddSaveValue("root-save", new ObjectAttributeValue
            {
                id = "root-save-value",
                createdAt = Now,
                updatedAt = Now,
                typeId = "type-root",
                value = new Dictionary<string, string>(),
            });
            client.SetSaveValue(new ObjectAttributeValue
            {
                id = "dict-value",
                createdAt = Now,
                updatedAt = Now,
                value = new Dictionary<string, string>
                {
                    ["slot"] = "root-save-value",
                },
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-dict-clear", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("dict-value", out ObjectAttributeValue? dict));
            Assert.AreEqual(0, dict!.value!.Count);
            Assert.IsTrue(client.TryGetValue("root-save-value", out ObjectAttributeValue? rootRow));
            Assert.AreEqual("type-root", rootRow!.typeId);
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
            client.AddSaveValue("root-save", new ObjectAttributeValue
            {
                id = "root-save-value",
                createdAt = Now,
                updatedAt = Now,
                typeId = "type-root",
                value = new Dictionary<string, string>(),
            });
            client.SetSaveValue(new ArrayAttributeValue
            {
                id = "list-value",
                createdAt = Now,
                updatedAt = Now,
                value = new[] { "root-save-value" },
            });
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-action-list-clear", out NeoDialogue dialogue));

            dialogue.Start();

            Assert.IsTrue(client.TryGetValue("list-value", out ArrayAttributeValue? list));
            Assert.AreEqual(0, list!.value!.Length);
            Assert.IsTrue(client.TryGetValue("root-save-value", out ObjectAttributeValue? rootRow));
            Assert.AreEqual("type-root", rootRow!.typeId);
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

        private static NeoClient CreateClient()
        {
            var data = new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    _id = ProjectId,
                    name = "Dialogue Project",
                    rootAssetsAttributeId = "root-assets",
                    rootSaveFileAttributeId = "root-save",
                    defaultPriorityGroupId = "priority-default",
                    createdAt = Now,
                    updatedAt = Now,
                },
                attributes = new Dictionary<string, Attribute>
                {
                    ["root-assets"] = RootAttribute("root-assets", "Assets"),
                    ["root-save"] = RootAttribute("root-save", "Save"),
                    ["attr-score"] = new IntAttribute
                    {
                        id = "attr-score",
                        _id = "attr-score",
                        projectId = ProjectId,
                        name = "Score",
                        type = AttributeType.Int,
                        required = true,
                        createdAt = Now,
                        updatedAt = Now,
                    },
                },
                values = new Dictionary<string, AttributeValue>
                {
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
                        },
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
                },
                types = new Dictionary<string, CustomType>
                {
                    ["type-root"] = new()
                    {
                        id = "type-root",
                        _id = "type-root",
                        projectId = ProjectId,
                        name = "Root",
                        schema = new Dictionary<string, string>
                        {
                            ["Score"] = "attr-score",
                        },
                        createdAt = Now,
                        updatedAt = Now,
                    },
                },
                enums = new Dictionary<string, Enum>(),
                dialogueGroups = new Dictionary<string, DialogueGroup>
                {
                    ["group-standard"] = new StandardDialogueGroup
                    {
                        id = "group-standard",
                        _id = "group-standard",
                        projectId = ProjectId,
                        name = "Standard",
                        type = DialogueGroupType.Standard,
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["group-lookup"] = new LookupDialogueGroup
                    {
                        id = "group-lookup",
                        _id = "group-lookup",
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
                        _id = "group-priority",
                        projectId = ProjectId,
                        name = "Priority",
                        type = DialogueGroupType.Standard,
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["group-visits"] = new StandardDialogueGroup
                    {
                        id = "group-visits",
                        _id = "group-visits",
                        projectId = ProjectId,
                        name = "Visits",
                        type = DialogueGroupType.Standard,
                        createdAt = Now,
                        updatedAt = Now,
                    },
                    ["group-folder-false"] = new FolderDialogueGroup
                    {
                        id = "group-folder-false",
                        _id = "group-folder-false",
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
                        _id = "group-child-of-false",
                        projectId = ProjectId,
                        name = "Child Of False",
                        type = DialogueGroupType.Standard,
                        parentDialogueGroupId = "group-folder-false",
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
                            NumberPointer(4),
                            "+=")),
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
                    ["dialogue-action-error"] = ActionDialogue(
                        "dialogue-action-error",
                        ThrowAction("boom")),
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
                    ["dialogue-text-linked-values"] = Dialogue(
                        "dialogue-text-linked-values",
                        "Text Linked Values",
                        "group-standard",
                        textLinkedValueIds: new[] { "text-linked-value-a" }),
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
                },
                priorityGroups = new Dictionary<string, PriorityGroup>
                {
                    ["priority-default"] = new PriorityGroup
                    {
                        id = "priority-default",
                        _id = "priority-default",
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

            string buffer = "";
            return new NeoClient(data, () => buffer, save => buffer = save);
        }

        private static CustomAttribute RootAttribute(string id, string name)
        {
            return new CustomAttribute
            {
                id = id,
                _id = id,
                projectId = ProjectId,
                name = name,
                type = AttributeType.Custom,
                customTypeId = "type-root",
                valueId = name == "Save" ? "root-save-default-value" : null,
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
                _id = id,
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
                _id = id,
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

        private static Dialogue ConditionsNodeDialogue()
        {
            return new Dialogue
            {
                id = "dialogue-conditions-node",
                _id = "dialogue-conditions-node",
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
                _id = "dialogue-options",
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
                                        type = OperatorKind.EqualTo,
                                        operand1 = ContextKeyPointer(key),
                                        operand2 = StringPointer(value),
                                    },
                                },
                            },
                        },
                    },
                },
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

        private static FunctionWithReturnType AssignAction(
            Pointer target,
            TypeInfo typeInfo,
            Pointer value,
            string operatorValue = "=")
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
                        writability = WritabilityKind.Save,
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
            string[]? linkedValueIds = null)
        {
            return new DialogueTextNode
            {
                id = id,
                type = DialogueNodeType.Text,
                layout = new DialogueNodeLayout(),
                text = text,
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
