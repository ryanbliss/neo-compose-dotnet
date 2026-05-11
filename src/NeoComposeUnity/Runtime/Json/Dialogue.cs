// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    public enum LogicType
    {
        UI = 0,
        Code = 1,
    }

    [JsonConverter(typeof(LogicConditionConverter))]
    public abstract class LogicCondition
    {
        public LogicType type;
    }

    public class UILogicCondition : LogicCondition
    {
        public FunctionWithReturnType getter = null!;
    }

    public class CodeLogicCondition : LogicCondition
    {
        public string code = null!;
        public FunctionWithReturnType getter = null!;
    }

    public class LogicConditionConverter : DiscriminatedConverter<LogicCondition>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
        {
            switch ((LogicType)discriminator.Value<int>())
            {
                case LogicType.UI: return typeof(UILogicCondition);
                case LogicType.Code: return typeof(CodeLogicCondition);
                default: return null;
            }
        }
    }

    [JsonConverter(typeof(LogicActionConverter))]
    public abstract class LogicAction
    {
        public LogicType type;
    }

    public class UILogicAction : LogicAction
    {
        public FunctionWithReturnType action = null!;
    }

    public class CodeLogicAction : LogicAction
    {
        public string code = null!;
        public FunctionWithReturnType action = null!;
    }

    public class LogicActionConverter : DiscriminatedConverter<LogicAction>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
        {
            switch ((LogicType)discriminator.Value<int>())
            {
                case LogicType.UI: return typeof(UILogicAction);
                case LogicType.Code: return typeof(CodeLogicAction);
                default: return null;
            }
        }
    }

    public enum DialogueLinkedValueSource
    {
        Manual = 0,
        Primary = 1,
        Logic = 2,
        Lookup = 3,
    }

    public class DialogueLinkedValue
    {
        public string valueId = null!;
        public DialogueLinkedValueSource source;
    }

    public class DialogueSettings
    {
        public bool defaultSaveOptionChoices;
    }

    public enum DialogueNodeType
    {
        Trigger = 0,
        Text = 1,
        Actions = 2,
        Conditions = 3,
    }

    public class DialogueNodeLayout
    {
        public float x;
        public float y;
        public float width;
        public float height;
        public bool isOpen;
    }

    public abstract class DialogueNode
    {
        public string id = null!;
        public DialogueNodeType type;
        public DialogueNodeLayout layout = null!;
        public string? name;
        public string? toNodeId;
        public string? primaryLinkedValueId;
        public DialogueLinkedValue[] linkedValues = null!;
    }

    public class DialogueTriggerNode : DialogueNode
    {
        public LogicCondition[] conditions = null!;
        public DialogueGroupSettings? dialogueGroupSettings;
        public OccurrenceLimitSettings? occurrenceLimitSettings;
    }

    [JsonConverter(typeof(DialogueBodyNodeConverter))]
    public abstract class DialogueBodyNode : DialogueNode { }

    public class DialogueTextNode : DialogueBodyNode
    {
        public string text = null!;
        public Dictionary<string, DialogueTextVariable>? variables;
        public DialogueOptionSettings? optionSettings;
    }

    public class DialogueActionsNode : DialogueBodyNode
    {
        public DialogueAction[] actions = null!;
    }

    public class DialogueConditionsNode : DialogueBodyNode
    {
        public DialogueOutcome[] outcomes = null!;
    }

    public class DialogueBodyNodeConverter : DiscriminatedConverter<DialogueBodyNode>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
        {
            switch ((DialogueNodeType)discriminator.Value<int>())
            {
                case DialogueNodeType.Text: return typeof(DialogueTextNode);
                case DialogueNodeType.Actions: return typeof(DialogueActionsNode);
                case DialogueNodeType.Conditions: return typeof(DialogueConditionsNode);
                default: return null;
            }
        }
    }

    public class DialogueOptionSettings
    {
        public DialogueTextOption[] options = null!;
        public bool? saveChoice;
    }

    public class DialogueTextOption
    {
        public string id = null!;
        public string text = null!;
        public Dictionary<string, DialogueTextVariable>? variables;
        public string? name;
        public string? toNodeId;
        public DialogueTextOptionSettings? settings;
    }

    public class DialogueTextVariable
    {
        public string id = null!;
        public string sourcePath = null!;
        public string displayPath = null!;
        public string label = null!;
        public TypeInfo typeInfo = null!;
        public Pointer pointer = null!;
        public FunctionWithReturnType getter = null!;
    }

    public class DialogueTextOptionSettings
    {
        public LogicCondition[]? conditions;
        public LogicCondition[]? selectableConditions;
    }

    public enum DialogueActionType
    {
        EditAttribute = 0,
    }

    [JsonConverter(typeof(DialogueActionConverter))]
    public abstract class DialogueAction
    {
        public string id = null!;
        public DialogueActionType type;
    }

    public class DialogueLogicEditAttributeAction : DialogueAction
    {
        public LogicAction logic = null!;
    }

    public class DialogueActionConverter : DiscriminatedConverter<DialogueAction>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
        {
            switch ((DialogueActionType)discriminator.Value<int>())
            {
                case DialogueActionType.EditAttribute: return typeof(DialogueLogicEditAttributeAction);
                default: return null;
            }
        }
    }

    public class DialogueOutcome
    {
        public string id = null!;
        public string name = null!;
        public string? toNodeId;
        public LogicCondition[] conditions = null!;
    }

    public class DialogueGroupSettings
    {
        public string dialogueGroupId = null!;
        public string? lookupValueId;
        public DialogueGroupPrioritySettings priority = null!;
    }

    public class DialogueGroupPrioritySettings
    {
        public string? priorityTypeId;
        public int? relativeOrder;
    }

    public class OccurrenceLimitSettings
    {
        public int count;
    }

    public class Dialogue
    {
        public string id = null!;
        public string _id = null!;
        public string projectId = null!;
        public string name = null!;
        public string? description;
        public Dictionary<string, DialogueBodyNode> nodes = null!;
        public DialogueLinkedValue[] linkedValues = null!;
        public DialogueSettings settings = null!;
        public DialogueTriggerNode? triggerNode;
        public string? primaryLinkedValueId;
        [JsonConverter(typeof(TolerantStringConverter))]
        public string createdAt = null!;
        [JsonConverter(typeof(TolerantStringConverter))]
        public string updatedAt = null!;
    }
}
