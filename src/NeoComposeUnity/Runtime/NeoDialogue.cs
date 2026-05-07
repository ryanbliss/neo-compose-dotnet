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
        private bool started;
        private bool disposed;

        public string id { get; }
        public string? groupId { get; }
        public DialogueModel data { get; }
        public NeoDialogueContext context { get; }
        public bool isStarted => started;
        public bool isDisposed => disposed;

        public event Action<NeoDialogueTextNode>? ShowText;
        public event Action? OnFinish;
        public event Action<Exception>? OnError;

        public NeoDialogue(
            NeoClient client,
            DialogueModel data,
            NeoDialogueContext context,
            INeoDialogueLogger logger,
            NeoDialogueRuntimeOptions options,
            INeoDialogueMemoryStore? memoryStore,
            string? groupId = null)
        {
            this.client = client;
            this.data = data;
            this.context = context;
            this.logger = logger;
            this.options = options;
            this.memoryStore = memoryStore;
            id = data.id;
            this.groupId = groupId;
        }

        public void Start()
        {
            if (started)
            {
                throw new InvalidOperationException($"Dialogue '{id}' has already started.");
            }
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(NeoDialogue));
            }
            started = true;
            try
            {
                RecordDialogueVisit();
            }
            catch (Exception ex)
            {
                Fail(ex);
                return;
            }
            EnterNode(data.triggerNode?.toNodeId);
        }

        internal void EnterNode(string? nodeId)
        {
            EnsureActive();
            if (string.IsNullOrEmpty(nodeId))
            {
                Finish();
                return;
            }
            if (data.nodes == null || !data.nodes.TryGetValue(nodeId, out var node))
            {
                Fail(new KeyNotFoundException(
                    $"Dialogue '{id}' points to missing node '{nodeId}'."));
                return;
            }

            context.nodeId = nodeId;
            context.primary = context.trigger;
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
                        $"Dialogue '{id}' has unsupported node type '{node.GetType().Name}'."));
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
                ?? data.settings?.defaultSaveOptionChoices
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
                        context.optionId = optionModel.id;
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

            ShowText?.Invoke(new NeoDialogueTextNode(
                node.id,
                node.text,
                node.name,
                context.primary,
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
                        NeoDialogueActionEvaluator.Execute(client, compiled, context);
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
                        context);
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
            var memory = memoryStore?.GetOrCreateDialogueMemory(id);
            if (memory == null) return;
            memory.VisitCount += 1;
            memory.LastVisitedAt = CurrentUtcIso();
        }

        private void RecordTextNodeVisit(string textNodeId)
        {
            var memory = memoryStore?.GetOrCreateDialogueMemory(id)
                .GetOrCreateTextNodeMemory(textNodeId);
            if (memory == null) return;
            memory.VisitCount += 1;
            memory.LastVisitedAt = CurrentUtcIso();
        }

        private void SaveTextNodeChoice(string textNodeId, string optionId)
        {
            var memory = memoryStore?.GetOrCreateDialogueMemory(id)
                .GetOrCreateTextNodeMemory(textNodeId);
            if (memory == null) return;
            memory.MostRecentChoiceId = optionId;
            if (!memory.HasChoice(optionId))
            {
                memory.AddChoice(optionId);
            }
        }

        private string CurrentUtcIso()
        {
            return options.ResolveUtcNow().ToString("o");
        }

        private void EnsureActive()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(NeoDialogue));
            }
        }

        internal void Finish()
        {
            if (disposed) return;
            OnFinish?.Invoke();
            Dispose();
        }

        internal void Fail(Exception exception)
        {
            if (disposed) return;
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
            if (disposed) return;
            disposed = true;
            ShowText = null;
            OnFinish = null;
            OnError = null;
        }
    }

    public sealed class NeoDialogueTextNode
    {
        private readonly Action next;
        private readonly Action ensureActive;

        public string id { get; }
        public string text { get; }
        public string? name { get; }
        public object? Primary { get; }
        public bool saveChoice { get; }
        public IReadOnlyList<NeoDialogueTextOption> Options { get; }

        public NeoDialogueTextNode(
            string id,
            string text,
            string? name,
            object? primary,
            bool saveChoice,
            IReadOnlyList<NeoDialogueTextOption> options,
            Action next,
            Action ensureActive)
        {
            this.id = id;
            this.text = text;
            this.name = name;
            Primary = primary;
            this.saveChoice = saveChoice;
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
                    $"Text node '{id}' has options; select an option instead of calling Next().");
            }
            next();
        }
    }

    public sealed class NeoDialogueTextOption
    {
        private readonly Action select;
        private readonly Action ensureActive;
        private bool selected;

        public string id { get; }
        public string text { get; }
        public string? name { get; }

        public NeoDialogueTextOption(
            string id,
            string text,
            string? name,
            Action select,
            Action ensureActive)
        {
            this.id = id;
            this.text = text;
            this.name = name;
            this.select = select;
            this.ensureActive = ensureActive;
        }

        public void Select()
        {
            ensureActive();
            if (selected)
            {
                throw new InvalidOperationException($"Dialogue option '{id}' has already been selected.");
            }
            selected = true;
            select();
        }
    }
}
