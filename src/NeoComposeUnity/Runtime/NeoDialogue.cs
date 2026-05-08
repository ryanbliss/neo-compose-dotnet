// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using DialogueActionsNodeModel = NeoCompose.Runtime.Json.DialogueActionsNode;
using DialogueActionModel = NeoCompose.Runtime.Json.DialogueAction;
using DialogueConditionsNodeModel = NeoCompose.Runtime.Json.DialogueConditionsNode;
using DialogueLogicEditAttributeActionModel = NeoCompose.Runtime.Json.DialogueLogicEditAttributeAction;
using DialogueModel = NeoCompose.Runtime.Json.Dialogue;
using DialogueOutcomeModel = NeoCompose.Runtime.Json.DialogueOutcome;
using DialogueTextNodeModel = NeoCompose.Runtime.Json.DialogueTextNode;
using DialogueTextOptionModel = NeoCompose.Runtime.Json.DialogueTextOption;
using CodeLogicActionModel = NeoCompose.Runtime.Json.CodeLogicAction;
using UILogicActionModel = NeoCompose.Runtime.Json.UILogicAction;

namespace NeoCompose.Runtime
{
    public sealed class NeoDialogue : IDisposable
    {
        private readonly NeoClient client;
        private readonly INeoDialogueLogger logger;
        private readonly NeoDialogueRuntimeOptions options;
        private readonly INeoDialogueMemoryStore? memoryStore;
        private readonly NeoDialogueValueResolver? valueResolver;
        private bool started;

        public string Id { get; }
        public string Name { get; }
        public string? Description { get; }
        public string? GroupId { get; }
        public string? LookupValueId { get; }
        public object? Primary { get; }
        public IReadOnlyDictionary<string, object?> LinkedValues { get; }
        public DialogueModel Data { get; }
        public NeoDialogueContext Context { get; }
        public NeoDialogueState State { get; private set; } = NeoDialogueState.Created;
        public bool IsStarted => started;
        public bool IsDisposed => State == NeoDialogueState.Disposed;

        public event Action<NeoDialogueTextNode>? OnShow;
        public event Action? OnFinish;
        public event Action<Exception>? OnError;

        public NeoDialogue(
            NeoClient client,
            DialogueModel data,
            NeoDialogueContext context,
            INeoDialogueLogger logger,
            NeoDialogueRuntimeOptions options,
            INeoDialogueMemoryStore? memoryStore,
            NeoDialogueValueResolver? valueResolver,
            string? groupId = null)
        {
            this.client = client;
            this.logger = logger;
            this.options = options;
            this.memoryStore = memoryStore;
            this.valueResolver = valueResolver;
            Data = data;
            Context = context;
            Id = data.id;
            Name = data.name;
            Description = data.description;
            this.GroupId = groupId;
            LookupValueId = data.triggerNode?.dialogueGroupSettings?.lookupValueId;
            Primary = context.Primary;
            LinkedValues = context.LinkedValues;
        }

        public void Start()
        {
            if (State != NeoDialogueState.Created)
            {
                if (State == NeoDialogueState.Disposed)
                {
                    throw new ObjectDisposedException(nameof(NeoDialogue));
                }
                throw new InvalidOperationException($"Dialogue '{Id}' has already started.");
            }
            started = true;
            State = NeoDialogueState.Started;
            try
            {
                RecordDialogueVisit();
            }
            catch (Exception ex)
            {
                Fail(ex);
                return;
            }
            EnterNode(Data.triggerNode?.toNodeId);
        }

        internal void EnterNode(string? nodeId)
        {
            EnsureActive();
            if (string.IsNullOrEmpty(nodeId))
            {
                Finish();
                return;
            }
            if (Data.nodes == null || !Data.nodes.TryGetValue(nodeId, out var node))
            {
                Fail(new KeyNotFoundException(
                    $"Dialogue '{Id}' points to missing node '{nodeId}'."));
                return;
            }

            Context.NodeId = nodeId;
            Context.CurrentPrimary = ResolvePrimary(node.primaryLinkedValueId);
            switch (node)
            {
                case DialogueTextNodeModel textNode:
                    EnterTextNode(textNode);
                    break;
                case DialogueActionsNodeModel actionsNode:
                    EnterActionsNode(actionsNode);
                    break;
                case DialogueConditionsNodeModel conditionsNode:
                    EnterConditionsNode(conditionsNode);
                    break;
                default:
                    Fail(new InvalidOperationException(
                        $"Dialogue '{Id}' has unsupported node type '{node.GetType().Name}'."));
                    break;
            }
        }

        private void EnterTextNode(DialogueTextNodeModel node)
        {
            try
            {
                RecordTextNodeVisit(node.id);
            }
            catch (Exception ex)
            {
                Fail(ex);
                return;
            }

            bool optionSelected = false;
            bool saveChoice =
                node.optionSettings?.saveChoice
                ?? Data.settings?.defaultSaveOptionChoices
                ?? false;
            var optionModels = node.optionSettings?.options ?? Array.Empty<DialogueTextOptionModel>();
            var options = new List<NeoDialogueTextOption>(optionModels.Length);
            foreach (var optionModel in optionModels)
            {
                options.Add(new NeoDialogueTextOption(
                    optionModel.id,
                    optionModel.text,
                    optionModel.name,
                    () =>
                    {
                        if (optionSelected)
                        {
                            throw new InvalidOperationException(
                                $"Text node '{node.id}' has already selected an option.");
                        }
                        optionSelected = true;
                        Context.OptionId = optionModel.id;
                        if (saveChoice)
                        {
                            try
                            {
                                SaveTextNodeChoice(node.id, optionModel.id);
                            }
                            catch (Exception ex)
                            {
                                Fail(ex);
                                return;
                            }
                        }
                        EnterNode(optionModel.toNodeId);
                    },
                    EnsureActive));
            }

            OnShow?.Invoke(new NeoDialogueTextNode(
                node.id,
                node.text,
                node.name,
                Context.CurrentPrimary,
                ResolveLinkedValues(node.linkedValues),
                saveChoice,
                options,
                () => EnterNode(node.toNodeId),
                EnsureActive));
        }

        private void EnterActionsNode(DialogueActionsNodeModel node)
        {
            try
            {
                foreach (var action in node.actions ?? Array.Empty<DialogueActionModel>())
                {
                    if (action is DialogueLogicEditAttributeActionModel logicAction)
                    {
                        var compiled = logicAction.logic switch
                        {
                            UILogicActionModel ui => ui.action,
                            CodeLogicActionModel code => code.action,
                            _ => null,
                        };
                        if (compiled == null)
                        {
                            throw new InvalidOperationException(
                                $"Dialogue action '{action.id}' has no compiled action.");
                        }
                        NeoDialogueActionEvaluator.Execute(client, compiled, Context, memoryStore);
                        continue;
                    }
                    throw new NotSupportedException(
                        $"Dialogue action '{action.id}' has unsupported action type '{action.GetType().Name}'.");
                }
            }
            catch (Exception ex)
            {
                Fail(ex);
                return;
            }
            EnterNode(node.toNodeId);
        }

        private void EnterConditionsNode(DialogueConditionsNodeModel node)
        {
            foreach (var outcome in node.outcomes ?? Array.Empty<DialogueOutcomeModel>())
            {
                bool matched;
                try
                {
                    matched = NeoDialogueConditionEvaluator.EvaluateAll(
                        client,
                        outcome.conditions,
                        Context,
                        memoryStore);
                }
                catch (Exception ex)
                {
                    Fail(ex);
                    return;
                }
                if (!matched) continue;
                EnterNode(outcome.toNodeId);
                return;
            }
            Finish();
        }

        private void RecordDialogueVisit()
        {
            var memory = memoryStore?.GetOrCreateDialogueMemory(Id);
            if (memory == null) return;
            memory.VisitCount += 1;
            memory.LastVisitedAt = CurrentUtcIso();
        }

        private void RecordTextNodeVisit(string textNodeId)
        {
            var memory = memoryStore?.GetOrCreateDialogueMemory(Id)
                .GetOrCreateTextNodeMemory(textNodeId);
            if (memory == null) return;
            memory.VisitCount += 1;
            memory.LastVisitedAt = CurrentUtcIso();
        }

        private void SaveTextNodeChoice(string textNodeId, string optionId)
        {
            var memory = memoryStore?.GetOrCreateDialogueMemory(Id)
                .GetOrCreateTextNodeMemory(textNodeId);
            if (memory == null) return;
            memory.MostRecentChoiceId = optionId;
            if (!memory.HasChoice(optionId))
            {
                memory.AddChoice(optionId, CurrentUtcIso());
            }
        }

        private object? ResolvePrimary(string? nodePrimaryLinkedValueId)
        {
            string? primaryLinkedValueId = string.IsNullOrEmpty(nodePrimaryLinkedValueId)
                ? Data.primaryLinkedValueId
                : nodePrimaryLinkedValueId;
            if (string.IsNullOrEmpty(primaryLinkedValueId)) return Context.Trigger;
            return valueResolver?.Invoke(primaryLinkedValueId!);
        }

        private IReadOnlyDictionary<string, object?> ResolveLinkedValues(
            NeoCompose.Runtime.Json.DialogueLinkedValue[]? linkedValues)
        {
            var result = new Dictionary<string, object?>();
            if (linkedValues == null || linkedValues.Length == 0) return result;
            foreach (var linkedValue in linkedValues)
            {
                if (string.IsNullOrEmpty(linkedValue.valueId)) continue;
                result[linkedValue.valueId] = valueResolver?.Invoke(linkedValue.valueId);
            }
            return result;
        }

        private string CurrentUtcIso()
        {
            return options.ResolveUtcNow().ToString("o");
        }

        private void EnsureActive()
        {
            if (State == NeoDialogueState.Disposed || State == NeoDialogueState.Finished)
            {
                throw new ObjectDisposedException(nameof(NeoDialogue));
            }
        }

        internal void Finish()
        {
            if (State == NeoDialogueState.Disposed || State == NeoDialogueState.Finished) return;
            State = NeoDialogueState.Finished;
            OnFinish?.Invoke();
            ClearListeners();
        }

        internal void Fail(Exception exception)
        {
            if (State == NeoDialogueState.Disposed || State == NeoDialogueState.Finished) return;
            if (OnError != null)
            {
                OnError.Invoke(exception);
                Dispose();
                return;
            }
            logger.LogException(exception);
            Dispose();
            throw exception;
        }

        public void Dispose()
        {
            if (State == NeoDialogueState.Disposed) return;
            State = NeoDialogueState.Disposed;
            ClearListeners();
        }

        private void ClearListeners()
        {
            OnShow = null;
            OnFinish = null;
            OnError = null;
        }
    }

    public sealed class NeoDialogueTextNode
    {
        private readonly Action next;
        private readonly Action ensureActive;

        public string Id { get; }
        public string Text { get; }
        public string? Name { get; }
        public object? Primary { get; }
        public IReadOnlyDictionary<string, object?> LinkedValues { get; }
        public bool SaveChoice { get; }
        public IReadOnlyList<NeoDialogueTextOption> Options { get; }

        public NeoDialogueTextNode(
            string id,
            string text,
            string? name,
            object? primary,
            IReadOnlyDictionary<string, object?> linkedValues,
            bool saveChoice,
            IReadOnlyList<NeoDialogueTextOption> options,
            Action next,
            Action ensureActive)
        {
            Id = id;
            Text = text;
            Name = name;
            Primary = primary;
            LinkedValues = linkedValues;
            SaveChoice = saveChoice;
            Options = options;
            this.next = next;
            this.ensureActive = ensureActive;
        }

        public void Next()
        {
            ensureActive();
            if (Options.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Text node '{Id}' has options; select an option instead of calling Next().");
            }
            next();
        }
    }

    public sealed class NeoDialogueTextOption
    {
        private readonly Action select;
        private readonly Action ensureActive;
        private bool selected;

        public string Id { get; }
        public string Text { get; }
        public string? Name { get; }

        public NeoDialogueTextOption(
            string id,
            string text,
            string? name,
            Action select,
            Action ensureActive)
        {
            Id = id;
            Text = text;
            Name = name;
            this.select = select;
            this.ensureActive = ensureActive;
        }

        public void Select()
        {
            ensureActive();
            if (selected)
            {
                throw new InvalidOperationException($"Dialogue option '{Id}' has already been selected.");
            }
            selected = true;
            select();
        }
    }
}
