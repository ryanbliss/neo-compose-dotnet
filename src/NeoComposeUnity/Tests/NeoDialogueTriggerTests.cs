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

            Assert.IsTrue(result.ok);
            Assert.IsNotNull(result.dialogue);
            Assert.AreEqual("dialogue-direct", result.dialogue!.id);
            Assert.AreEqual("group-standard", result.dialogue.groupId);
            Assert.IsFalse(result.dialogue.isStarted);
        }

        [Test]
        public void StandardGroupTryTrigger_ReturnsFirstDialogueInGroup()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            var group = new TestStandardDialogueGroup(root, "group-standard");

            Assert.IsTrue(group.TryTrigger(out NeoDialogueTriggerResult result));

            Assert.IsTrue(result.ok);
            Assert.IsNotNull(result.dialogue);
            Assert.AreEqual("dialogue-direct", result.dialogue!.id);
            Assert.AreEqual("group-standard", result.dialogue.context.groupId);
        }

        [Test]
        public void LookupGroupTryTrigger_FiltersByLookupValueId()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            var group = new TestLookupDialogueGroup(root, "group-lookup");

            Assert.IsTrue(group.TryTrigger(new TestLookupValue("lookup-value-b"), out NeoDialogueTriggerResult result));

            Assert.IsTrue(result.ok);
            Assert.IsNotNull(result.dialogue);
            Assert.AreEqual("dialogue-lookup-b", result.dialogue!.id);
            Assert.AreEqual("lookup-value-b", ((TestLookupValue)result.dialogue.context.trigger!).valueId);
            Assert.AreEqual(result.dialogue.context.trigger, result.dialogue.context.primary);
        }

        [Test]
        public void LookupGroupTryTrigger_RequiresValueReferenceId()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            var group = new TestLookupDialogueGroup(root, "group-lookup");

            Assert.IsFalse(group.TryTrigger(new TestLookupValue(null), out NeoDialogueTriggerResult result));

            Assert.IsFalse(result.ok);
            Assert.IsNotNull(result.error);
            StringAssert.Contains("requires a value with a Neo value id", result.error!.Message);
        }

        [Test]
        public void TryTrigger_WithFalseCondition_ReturnsNotFound()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);

            Assert.IsFalse(root.TryTrigger("dialogue-condition-false", out NeoDialogueTriggerResult result));

            Assert.IsFalse(result.ok);
            Assert.IsNull(result.dialogue);
            Assert.IsNull(result.error);
        }

        [Test]
        public void TryTrigger_WithNonBoolCondition_ReturnsError()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);

            Assert.IsFalse(root.TryTrigger("dialogue-condition-error", out NeoDialogueTriggerResult result));

            Assert.IsFalse(result.ok);
            Assert.IsNotNull(result.error);
            StringAssert.Contains("expected bool", result.error!.Message);
        }

        [Test]
        public void Start_EmitsTextNode_AndFinishesOnNext()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-direct", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            bool finished = false;
            dialogue.ShowText += node => shown = node;
            dialogue.OnFinish += () => finished = true;

            dialogue.Start();

            Assert.IsTrue(dialogue.isStarted);
            Assert.IsNotNull(shown);
            Assert.AreEqual("text-start", shown!.id);
            Assert.AreEqual("Hello there.", shown.text);
            Assert.IsFalse(finished);

            shown.Next();

            Assert.IsTrue(finished);
            Assert.IsTrue(dialogue.isDisposed);
            Assert.Throws<System.ObjectDisposedException>(() => shown.Next());
            Assert.Throws<System.InvalidOperationException>(() => dialogue.Start());
        }

        [Test]
        public void TextOption_Select_TransitionsToNextNode()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-options", out NeoDialogue dialogue));

            var shown = new List<NeoDialogueTextNode>();
            bool finished = false;
            dialogue.ShowText += shown.Add;
            dialogue.OnFinish += () => finished = true;

            dialogue.Start();

            Assert.AreEqual(1, shown.Count);
            Assert.AreEqual("text-choice", shown[0].id);
            Assert.AreEqual(2, shown[0].Options.Count);
            Assert.IsTrue(shown[0].saveChoice);
            Assert.Throws<System.InvalidOperationException>(() => shown[0].Next());

            shown[0].Options[0].Select();

            Assert.AreEqual(2, shown.Count);
            Assert.AreEqual("text-after-choice", shown[1].id);
            Assert.AreEqual("option-a", dialogue.context.optionId);
            Assert.Throws<System.InvalidOperationException>(() => shown[0].Options[1].Select());

            shown[1].Next();

            Assert.IsTrue(finished);
            Assert.IsTrue(dialogue.isDisposed);
        }

        [Test]
        public void ConditionsNode_SelectsFirstMatchingOutcome()
        {
            var client = CreateClient();
            var root = new TestDialogues(client);
            Assert.IsTrue(root.TryTrigger("dialogue-conditions-node", out NeoDialogue dialogue));

            NeoDialogueTextNode? shown = null;
            dialogue.ShowText += node => shown = node;

            dialogue.Start();

            Assert.IsNotNull(shown);
            Assert.AreEqual("text-true", shown!.id);
            Assert.AreEqual("The true branch.", shown.text);
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
                    createdAt = Now,
                    updatedAt = Now,
                },
                attributes = new Dictionary<string, Attribute>
                {
                    ["root-assets"] = RootAttribute("root-assets", "Assets"),
                    ["root-save"] = RootAttribute("root-save", "Save"),
                },
                values = new Dictionary<string, AttributeValue>(),
                types = new Dictionary<string, CustomType>
                {
                    ["type-root"] = new()
                    {
                        id = "type-root",
                        _id = "type-root",
                        projectId = ProjectId,
                        name = "Root",
                        schema = new Dictionary<string, string>(),
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
                },
                dialogues = new Dictionary<string, Dialogue>
                {
                    ["dialogue-direct"] = Dialogue(
                        "dialogue-direct",
                        "A Direct Dialogue",
                        "group-standard"),
                    ["dialogue-options"] = OptionsDialogue(),
                    ["dialogue-condition-false"] = Dialogue(
                        "dialogue-condition-false",
                        "Condition False",
                        "group-standard",
                        conditions: new[] { Condition(BoolGetter(false)) }),
                    ["dialogue-condition-error"] = Dialogue(
                        "dialogue-condition-error",
                        "Condition Error",
                        "group-standard",
                        conditions: new[] { Condition(StringGetter("not bool")) }),
                    ["dialogue-conditions-node"] = ConditionsNodeDialogue(),
                    ["dialogue-lookup-a"] = Dialogue(
                        "dialogue-lookup-a",
                        "Lookup A",
                        "group-lookup",
                        "lookup-value-a"),
                    ["dialogue-lookup-b"] = Dialogue(
                        "dialogue-lookup-b",
                        "Lookup B",
                        "group-lookup",
                        "lookup-value-b"),
                },
                priorityGroups = new Dictionary<string, PriorityGroup>(),
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
                createdAt = Now,
                updatedAt = Now,
            };
        }

        private static Dialogue Dialogue(
            string id,
            string name,
            string groupId,
            string? lookupValueId = null,
            LogicCondition[]? conditions = null)
        {
            return new Dialogue
            {
                id = id,
                _id = id,
                projectId = ProjectId,
                name = name,
                description = null,
                linkedValues = new DialogueLinkedValue[0],
                settings = new DialogueSettings(),
                primaryLinkedValueId = null,
                triggerNode = new DialogueTriggerNode
                {
                    id = $"{id}-trigger",
                    type = DialogueNodeType.Trigger,
                    layout = new DialogueNodeLayout(),
                    toNodeId = "text-start",
                    linkedValues = new DialogueLinkedValue[0],
                    conditions = conditions ?? new LogicCondition[0],
                    dialogueGroupSettings = new DialogueGroupSettings
                    {
                        dialogueGroupId = groupId,
                        lookupValueId = lookupValueId,
                        priority = new DialogueGroupPrioritySettings(),
                    },
                },
                nodes = new Dictionary<string, DialogueBodyNode>
                {
                    ["text-start"] = TextNode("text-start", "Hello there."),
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

        private static DialogueTextNode TextNode(
            string id,
            string text,
            NeoCompose.Runtime.Json.DialogueTextOption[]? options = null)
        {
            return new DialogueTextNode
            {
                id = id,
                type = DialogueNodeType.Text,
                layout = new DialogueNodeLayout(),
                text = text,
                linkedValues = new DialogueLinkedValue[0],
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
            public TestDialogues(NeoClient client) : base(client) { }
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

        private sealed class TestLookupValue : INeoValueReference
        {
            public string? valueId { get; }

            public TestLookupValue(string? valueId)
            {
                this.valueId = valueId;
            }
        }
    }
}
