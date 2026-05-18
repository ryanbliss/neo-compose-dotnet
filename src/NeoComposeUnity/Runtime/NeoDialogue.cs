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
using DialoguePauseActionModel = NeoCompose.Runtime.Json.DialoguePauseAction;
using DialogueTextNodeModel = NeoCompose.Runtime.Json.DialogueTextNode;
using DialogueTextOptionModel = NeoCompose.Runtime.Json.DialogueTextOption;
using CodeLogicActionModel = NeoCompose.Runtime.Json.CodeLogicAction;
using UILogicActionModel = NeoCompose.Runtime.Json.UILogicAction;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Runtime instance of a single triggered dialogue. Subscribe to its events,
    /// call <see cref="Start"/>, then drive shown text nodes through
    /// <see cref="NeoDialogueTextNode.Next"/> or <see cref="NeoDialogueTextOption.Select"/>.
    /// </summary>
    public sealed class NeoDialogue : IDisposable
    {
        private readonly NeoClient client;
        private readonly INeoDialogueLogger logger;
        private readonly NeoDialogueRuntimeOptions options;
        private readonly INeoDialogueMemoryStore? memoryStore;
        private readonly NeoDialogueValueResolver? valueResolver;
        private readonly IDisposable clientRegistration;
        private NeoDialoguePauseAction? activePauseAction;
        private bool started;

        /// <summary>
        /// Stable dialogue id from the exported Neo Compose project.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Author-facing dialogue name from the exported Neo Compose project.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Optional dialogue description from the exported Neo Compose project.
        /// </summary>
        public string? Description { get; }

        /// <summary>
        /// Dialogue group id that triggered this dialogue, when the dialogue belongs to a group.
        /// </summary>
        public string? GroupId { get; }

        /// <summary>
        /// Stored lookup value id for lookup-triggered dialogues, when one is configured.
        /// </summary>
        public string? LookupValueId { get; }

        /// <summary>
        /// Resolved primary value for the dialogue. Body nodes can expose a different current
        /// primary through <see cref="NeoDialogueTextNode.Primary"/>.
        /// </summary>
        public object? Primary { get; }

        /// <summary>
        /// Dialogue-level linked values resolved by exported value id.
        /// </summary>
        public IReadOnlyDictionary<string, object?> LinkedValues { get; }

        /// <summary>
        /// Raw exported dialogue model backing this runtime instance.
        /// </summary>
        public DialogueModel Data { get; }

        /// <summary>
        /// Mutable runtime context used while conditions and actions execute.
        /// </summary>
        public NeoDialogueContext Context { get; }

        /// <summary>
        /// Current lifecycle state for this dialogue instance.
        /// </summary>
        public NeoDialogueState State { get; private set; } = NeoDialogueState.Created;

        /// <summary>
        /// Returns whether <see cref="Start"/> has been called for this dialogue instance.
        /// </summary>
        public bool IsStarted => started;

        /// <summary>
        /// Returns whether this dialogue has been disposed and can no longer be driven.
        /// </summary>
        public bool IsDisposed => State == NeoDialogueState.Disposed;

        /// <summary>
        /// Raised whenever the dialogue reaches a text node that should be shown to the player.
        /// </summary>
        public event Action<NeoDialogueTextNode>? OnShow;

        /// <summary>
        /// Raised whenever the dialogue reaches a pause action.
        /// </summary>
        public event Action<NeoDialoguePauseAction>? OnPause;

        /// <summary>
        /// Raised when the dialogue reaches the end of its flow.
        /// </summary>
        public event Action? OnFinish;

        /// <summary>
        /// Raised when dialogue execution fails. If no handler is attached, the exception is
        /// logged and thrown through the default runtime error path.
        /// </summary>
        public event Action<Exception>? OnError;

        /// <summary>
        /// Creates a runtime dialogue instance from exported data and runtime services.
        /// Generated dialogue-group code normally constructs this for you.
        /// </summary>
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
            clientRegistration = client.RegisterDialogue(this);
        }

        /// <summary>
        /// Returns how many times this dialogue has been started in the configured dialogue memory store.
        /// The count is incremented before the first node is entered during <see cref="Start"/>.
        /// </summary>
        public int VisitCount()
        {
            return NeoDialogueMemoryQueries.VisitCount(memoryStore, Id);
        }

        /// <summary>
        /// Returns whether this dialogue has been started at least once in the configured dialogue memory store.
        /// </summary>
        public bool HasVisited()
        {
            return VisitCount() > 0;
        }

        /// <summary>
        /// Starts dialogue execution. A dialogue instance can only be started once.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// Thrown when the dialogue has already been disposed.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the dialogue has already been started.
        /// </exception>
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

            string text;
            try
            {
                text = InterpolateTextNodeText(node);
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
            var hiddenOptions = new List<NeoDialogueHiddenTextOption>();
            foreach (var optionModel in optionModels)
            {
                bool visible;
                bool selectable;
                try
                {
                    visible = EvaluateOptionConditions(
                        optionModel,
                        optionModel.settings?.conditions);
                    selectable = visible
                        && EvaluateOptionConditions(
                            optionModel,
                            optionModel.settings?.selectableConditions);
                }
                catch (Exception ex)
                {
                    Fail(ex);
                    return;
                }
                if (!visible)
                {
                    hiddenOptions.Add(new NeoDialogueHiddenTextOption(
                        optionModel.id,
                        optionModel.text,
                        optionModel.name));
                    continue;
                }
                string optionText;
                try
                {
                    optionText = InterpolateOptionText(node.id, optionModel);
                }
                catch (Exception ex)
                {
                    Fail(ex);
                    return;
                }
                options.Add(new NeoDialogueTextOption(
                    Id,
                    node.id,
                    optionModel.id,
                    optionText,
                    optionModel.name,
                    saveChoice,
                    memoryStore,
                    selectable,
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
                Id,
                node.id,
                text,
                node.name,
                Context.CurrentPrimary,
                ResolveLinkedValues(node.linkedValues),
                saveChoice,
                memoryStore,
                options,
                hiddenOptions,
                () => EnterNode(node.toNodeId),
                EnsureActive));
        }

        private string InterpolateTextNodeText(DialogueTextNodeModel node)
        {
            return NeoDialogueTextInterpolator.Interpolate(
                client,
                node.text,
                node.variables,
                Context,
                memoryStore,
                "text node",
                node.id);
        }

        private string InterpolateOptionText(
            string textNodeId,
            DialogueTextOptionModel optionModel)
        {
            string? previousOptionId = Context.OptionId;
            try
            {
                Context.OptionId = optionModel.id;
                return NeoDialogueTextInterpolator.Interpolate(
                    client,
                    optionModel.text,
                    optionModel.variables,
                    Context,
                    memoryStore,
                    "text option",
                    $"{textNodeId}/{optionModel.id}");
            }
            finally
            {
                Context.OptionId = previousOptionId;
            }
        }

        private bool EvaluateOptionConditions(
            DialogueTextOptionModel optionModel,
            NeoCompose.Runtime.Json.LogicCondition[]? conditions)
        {
            string? previousOptionId = Context.OptionId;
            try
            {
                Context.OptionId = optionModel.id;
                return NeoDialogueConditionEvaluator.EvaluateAll(
                    client,
                    conditions,
                    Context,
                    memoryStore);
            }
            finally
            {
                Context.OptionId = previousOptionId;
            }
        }

        private void EnterActionsNode(DialogueActionsNodeModel node)
        {
            ContinueActionsNode(node, 0);
        }

        private void ContinueActionsNode(DialogueActionsNodeModel node, int startIndex)
        {
            EnsureActive();
            try
            {
                var actions = node.actions ?? Array.Empty<DialogueActionModel>();
                for (int i = startIndex; i < actions.Length; i++)
                {
                    var action = actions[i];
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
                    if (action is DialoguePauseActionModel pauseAction)
                    {
                        EnterPauseAction(node, pauseAction, i + 1);
                        return;
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

        private void EnterPauseAction(
            DialogueActionsNodeModel node,
            DialoguePauseActionModel action,
            int nextActionIndex)
        {
            State = NeoDialogueState.Paused;
            var pause = new NeoDialoguePauseAction(
                Id,
                node.id,
                action.id,
                action.reason,
                action.autoResumeDurationSeconds,
                () => ResumePause(node, nextActionIndex, autoResume: false),
                () => ResumePause(node, nextActionIndex, autoResume: true),
                EnsurePauseCanResume);
            activePauseAction = pause;

            if (OnPause == null && action.autoResumeDurationSeconds == null)
            {
                logger.LogWarning(
                    $"Dialogue '{Id}' paused at actions node '{node.id}', action '{action.id}' with no OnPause listener. Reason: '{action.reason}'.");
            }

            try
            {
                OnPause?.Invoke(pause);
            }
            catch (Exception ex)
            {
                Fail(ex);
                return;
            }

            if (!pause.Paused) return;
            if (action.autoResumeDurationSeconds == null) return;
            if (action.autoResumeDurationSeconds.Value == 0)
            {
                pause.ResumeFromAuto();
                return;
            }

            pause.ScheduleAutoResume(
                options.ResolvePauseScheduler(),
                TimeSpan.FromSeconds(action.autoResumeDurationSeconds.Value));
        }

        private void ResumePause(
            DialogueActionsNodeModel node,
            int nextActionIndex,
            bool autoResume)
        {
            var pause = activePauseAction;
            if (pause == null)
            {
                if (autoResume) return;
                throw new InvalidOperationException(
                    $"Dialogue '{Id}' does not have an active pause action to resume.");
            }
            if (State == NeoDialogueState.Disposed)
            {
                if (autoResume) return;
                throw new ObjectDisposedException(
                    nameof(NeoDialogue),
                    $"Dialogue '{Id}' was disposed before pause action '{pause.Id}' could resume.");
            }
            if (State == NeoDialogueState.Finished)
            {
                if (autoResume) return;
                throw new InvalidOperationException(
                    $"Dialogue '{Id}' already finished before pause action '{pause.Id}' could resume.");
            }
            activePauseAction = null;
            pause.MarkResumed();
            State = NeoDialogueState.Started;
            ContinueActionsNode(node, nextActionIndex);
        }

        private void EnsurePauseCanResume(NeoDialoguePauseAction pause)
        {
            if (client.IsDisposed)
            {
                throw new ObjectDisposedException(
                    nameof(NeoClient),
                    $"Cannot resume pause action '{pause.Id}' because its owning NeoClient was disposed.");
            }
            if (State == NeoDialogueState.Disposed)
            {
                throw new ObjectDisposedException(
                    nameof(NeoDialogue),
                    $"Cannot resume pause action '{pause.Id}' because dialogue '{Id}' was disposed.");
            }
            if (State == NeoDialogueState.Finished)
            {
                throw new InvalidOperationException(
                    $"Cannot resume pause action '{pause.Id}' because dialogue '{Id}' already finished.");
            }
            if (!ReferenceEquals(activePauseAction, pause))
            {
                throw new InvalidOperationException(
                    $"Cannot resume pause action '{pause.Id}' because it is no longer the active pause for dialogue '{Id}'.");
            }
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
            if (string.IsNullOrEmpty(primaryLinkedValueId)) return Context.Primary;
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
            activePauseAction?.DisposeFromOwner("dialogue finished");
            activePauseAction = null;
            State = NeoDialogueState.Finished;
            OnFinish?.Invoke();
            ClearListeners();
            clientRegistration.Dispose();
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

        /// <summary>
        /// Disposes this dialogue instance and clears event listeners.
        /// </summary>
        public void Dispose()
        {
            if (State == NeoDialogueState.Disposed) return;
            activePauseAction?.DisposeFromOwner("dialogue disposed");
            activePauseAction = null;
            State = NeoDialogueState.Disposed;
            ClearListeners();
            clientRegistration.Dispose();
        }

        internal void DisposeFromClient()
        {
            if (State == NeoDialogueState.Disposed) return;
            activePauseAction?.DisposeFromOwner("NeoClient disposed");
            activePauseAction = null;
            State = NeoDialogueState.Disposed;
            ClearListeners();
            clientRegistration.Dispose();
        }

        private void ClearListeners()
        {
            OnShow = null;
            OnPause = null;
            OnFinish = null;
            OnError = null;
        }
    }

    /// <summary>
    /// Runtime pause action emitted by <see cref="NeoDialogue.OnPause"/>.
    /// </summary>
    public sealed class NeoDialoguePauseAction
    {
        private readonly Action resume;
        private readonly Action resumeFromAuto;
        private readonly Action<NeoDialoguePauseAction> ensureCanResume;
        private IDisposable? autoResume;
        private bool resumed;
        private bool disposed;
        private string? disposedReason;

        public string Id { get; }
        public string DialogueId { get; }
        public string NodeId { get; }
        public string Reason { get; }
        public double? AutoResumeDurationSeconds { get; }
        public bool Paused => !resumed && !disposed;

        internal NeoDialoguePauseAction(
            string dialogueId,
            string nodeId,
            string id,
            string reason,
            double? autoResumeDurationSeconds,
            Action resume,
            Action resumeFromAuto,
            Action<NeoDialoguePauseAction> ensureCanResume)
        {
            DialogueId = dialogueId;
            NodeId = nodeId;
            Id = id;
            Reason = reason;
            AutoResumeDurationSeconds = autoResumeDurationSeconds;
            this.resume = resume;
            this.resumeFromAuto = resumeFromAuto;
            this.ensureCanResume = ensureCanResume;
        }

        public void Resume()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(
                    nameof(NeoDialoguePauseAction),
                    $"Cannot resume pause action '{Id}' because it was disposed when {disposedReason}.");
            }
            if (resumed)
            {
                throw new InvalidOperationException(
                    $"Pause action '{Id}' has already been resumed.");
            }
            ensureCanResume(this);
            resume();
        }

        internal void ResumeFromAuto()
        {
            if (!Paused) return;
            try
            {
                ensureCanResume(this);
            }
            catch (Exception)
            {
                return;
            }
            resumeFromAuto();
        }

        internal void ScheduleAutoResume(
            INeoDialoguePauseScheduler scheduler,
            TimeSpan delay)
        {
            if (!Paused) return;
            autoResume?.Dispose();
            autoResume = scheduler.Schedule(delay, ResumeFromAuto);
        }

        internal void MarkResumed()
        {
            autoResume?.Dispose();
            autoResume = null;
            resumed = true;
        }

        internal void DisposeFromOwner(string reason)
        {
            if (disposed) return;
            autoResume?.Dispose();
            autoResume = null;
            disposed = true;
            disposedReason = reason;
        }
    }

    /// <summary>
    /// Text node currently being shown by a <see cref="NeoDialogue"/>.
    /// </summary>
    public sealed class NeoDialogueTextNode
    {
        private readonly string dialogueId;
        private readonly INeoDialogueMemoryStore? memoryStore;
        private readonly Action next;
        private readonly Action ensureActive;

        /// <summary>
        /// Stable text node id from the exported Neo Compose dialogue graph.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Text content to display for this node.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Optional author-facing node name.
        /// </summary>
        public string? Name { get; }

        /// <summary>
        /// Resolved primary value for this node. Falls back to the dialogue primary when the
        /// node does not override it.
        /// </summary>
        public object? Primary { get; }

        /// <summary>
        /// Node-level linked values resolved by exported value id.
        /// </summary>
        public IReadOnlyDictionary<string, object?> LinkedValues { get; }

        /// <summary>
        /// Whether selecting an option on this node should be persisted to dialogue memory.
        /// </summary>
        public bool SaveChoice { get; }

        /// <summary>
        /// Visible options for this text node. Use <see cref="NeoDialogueTextOption.Select"/>
        /// to advance through an option.
        /// </summary>
        public IReadOnlyList<NeoDialogueTextOption> Options { get; }

        /// <summary>
        /// Options hidden by option visibility conditions. These are provided for diagnostics
        /// and custom UI, but cannot be selected.
        /// </summary>
        public IReadOnlyList<NeoDialogueHiddenTextOption> HiddenOptions { get; }

        /// <summary>
        /// Creates a runtime wrapper for a shown text node.
        /// Generated dialogue runtime code normally constructs this for you.
        /// </summary>
        public NeoDialogueTextNode(
            string dialogueId,
            string id,
            string text,
            string? name,
            object? primary,
            IReadOnlyDictionary<string, object?> linkedValues,
            bool saveChoice,
            INeoDialogueMemoryStore? memoryStore,
            IReadOnlyList<NeoDialogueTextOption> options,
            IReadOnlyList<NeoDialogueHiddenTextOption> hiddenOptions,
            Action next,
            Action ensureActive)
        {
            this.dialogueId = dialogueId;
            Id = id;
            Text = text;
            Name = name;
            Primary = primary;
            LinkedValues = linkedValues;
            SaveChoice = saveChoice;
            this.memoryStore = memoryStore;
            Options = options;
            HiddenOptions = hiddenOptions;
            this.next = next;
            this.ensureActive = ensureActive;
        }

        /// <summary>
        /// Returns how many times this text node has been shown in the configured dialogue memory store.
        /// The count is incremented before <see cref="NeoDialogue.OnShow"/> is raised.
        /// </summary>
        public int VisitCount()
        {
            return NeoDialogueMemoryQueries.VisitCount(memoryStore, $"{dialogueId},{Id}");
        }

        /// <summary>
        /// Returns whether this text node has been shown at least once in the configured dialogue memory store.
        /// </summary>
        public bool HasVisited()
        {
            return VisitCount() > 0;
        }

        /// <summary>
        /// Advances to this node's next linked node. Use option <see cref="NeoDialogueTextOption.Select"/>
        /// instead when <see cref="Options"/> is not empty.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// Thrown when the owning dialogue has finished or been disposed.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when this node has visible options.
        /// </exception>
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

    /// <summary>
    /// Visible selectable option on a <see cref="NeoDialogueTextNode"/>.
    /// </summary>
    public sealed class NeoDialogueTextOption
    {
        private readonly string dialogueId;
        private readonly string textNodeId;
        private readonly bool saveChoice;
        private readonly INeoDialogueMemoryStore? memoryStore;
        private readonly Action select;
        private readonly Action ensureActive;
        private bool selected;

        /// <summary>
        /// Stable option id from the exported Neo Compose dialogue graph.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Text content to display for this option.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Optional author-facing option name.
        /// </summary>
        public string? Name { get; }

        /// <summary>
        /// Whether this option can currently be selected. UI code should usually bind this
        /// to its button's interactable/enabled state.
        /// </summary>
        public bool Selectable { get; }

        /// <summary>
        /// Creates a runtime wrapper for a visible text option.
        /// Generated dialogue runtime code normally constructs this for you.
        /// </summary>
        public NeoDialogueTextOption(
            string dialogueId,
            string textNodeId,
            string id,
            string text,
            string? name,
            bool saveChoice,
            INeoDialogueMemoryStore? memoryStore,
            bool selectable,
            Action select,
            Action ensureActive)
        {
            this.dialogueId = dialogueId;
            this.textNodeId = textNodeId;
            Id = id;
            Text = text;
            Name = name;
            this.saveChoice = saveChoice;
            this.memoryStore = memoryStore;
            Selectable = selectable;
            this.select = select;
            this.ensureActive = ensureActive;
        }

        /// <summary>
        /// Returns whether this option has been selected before for its text node.
        /// This requires the parent text node's <see cref="NeoDialogueTextNode.SaveChoice"/> to be enabled;
        /// otherwise Neo Compose does not track option choices and this method throws.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown when the parent text node has choice saving disabled.
        /// </exception>
        public bool HasChosen()
        {
            if (!saveChoice)
            {
                throw new InvalidOperationException(
                    $"Dialogue option '{Id}' belongs to text node '{textNodeId}', but SaveChoice is disabled. Enable SaveChoice for that text node before calling HasChosen().");
            }
            return NeoDialogueMemoryQueries.HasVisited(memoryStore, $"{dialogueId},{textNodeId},{Id}");
        }

        /// <summary>
        /// Selects this option, records the choice when the parent node saves choices, and advances
        /// the owning dialogue to the option's target node.
        /// </summary>
        /// <exception cref="ObjectDisposedException">
        /// Thrown when the owning dialogue has finished or been disposed.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Thrown when <see cref="Selectable"/> is false or this option was already selected.
        /// </exception>
        public void Select()
        {
            ensureActive();
            if (!Selectable)
            {
                throw new InvalidOperationException(
                    $"Dialogue option '{Id}' is not selectable. Check option.Selectable before calling Select(); for Unity UI, bind Button.interactable = option.Selectable.");
            }
            if (selected)
            {
                throw new InvalidOperationException($"Dialogue option '{Id}' has already been selected.");
            }
            selected = true;
            select();
        }
    }

    /// <summary>
    /// Option hidden by visibility conditions on a text node.
    /// </summary>
    public sealed class NeoDialogueHiddenTextOption
    {
        /// <summary>
        /// Stable option id from the exported Neo Compose dialogue graph.
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Text content configured for the hidden option.
        /// </summary>
        public string Text { get; }

        /// <summary>
        /// Optional author-facing option name.
        /// </summary>
        public string? Name { get; }

        /// <summary>
        /// Creates a runtime wrapper for an option hidden by visibility conditions.
        /// </summary>
        public NeoDialogueHiddenTextOption(
            string id,
            string text,
            string? name)
        {
            Id = id;
            Text = text;
            Name = name;
        }
    }
}
