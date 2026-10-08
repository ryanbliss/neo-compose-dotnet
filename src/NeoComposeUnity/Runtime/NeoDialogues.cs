// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DialogueModel = NeoCompose.Runtime.Json.Dialogue;
using DialogueGroupModel = NeoCompose.Runtime.Json.DialogueGroup;
using PriorityGroupModel = NeoCompose.Runtime.Json.PriorityGroup;

namespace NeoCompose.Runtime
{
    public abstract class NeoDialoguesBase
    {
        private readonly List<Action<NeoDialogue>> eligibleHandlers = new();
        private readonly Dictionary<(string groupId, string? lookupValueId), DialogueModel[]>
            dialoguesByTrigger;
        private readonly Queue<NeoDialogueRequest> requests = new();
        // The running dialogue's id and every queued one, so none queues twice.
        private readonly HashSet<string> requestedIds = new(StringComparer.Ordinal);
        private NeoDialogueRequest? running;
        private NeoDialogueRequest? ended;
        private bool runningTurn;
        private bool warnedNoPresenter;

        protected NeoClient client
        {
            get;
        }
        protected NeoDialogueRuntimeOptions options
        {
            get;
        }
        protected INeoDialogueLogger logger
        {
            get;
        }
        protected INeoDialogueMemoryStore? memoryStore
        {
            get;
        }
        protected NeoDialogueValueResolver? valueResolver
        {
            get;
        }

        public event Action<NeoDialogueEligibilityError>? OnEligibleError;

        /// <summary>
        /// Raised when a triggered dialogue takes its turn. Subscribe to its
        /// <see cref="NeoDialogue.OnShow"/>, <see cref="NeoDialogue.OnPause"/>,
        /// and <see cref="NeoDialogue.OnError"/> here. The SDK calls
        /// <c>Start()</c> right after every handler returns.
        /// </summary>
        public event Action<NeoDialogue>? OnTrigger;

        /// <summary>Dialogues over <paramref name="project"/>, whose values resolve to its generated views.</summary>
        protected NeoDialoguesBase(
            NeoProjectClient project,
            NeoDialogueRuntimeOptions? options,
            INeoDialogueMemoryStore? memoryStore)
            : this(project.Client, options, memoryStore, project.ResolveValue)
        {
        }

        protected NeoDialoguesBase(
            NeoClient client,
            NeoDialogueRuntimeOptions? options = null,
            INeoDialogueMemoryStore? memoryStore = null,
            NeoDialogueValueResolver? valueResolver = null)
        {
            this.client = client;
            this.options = options ?? new NeoDialogueRuntimeOptions();
            logger = this.options.ResolveLogger();
            this.memoryStore = memoryStore;
            this.valueResolver = valueResolver;
            dialoguesByTrigger = client.dialogues.Values
                .Where(dialogue => !string.IsNullOrEmpty(
                    dialogue.triggerNode?.dialogueGroupSettings?.dialogueGroupId))
                .GroupBy(dialogue => (
                    dialogue.triggerNode!.dialogueGroupSettings!.dialogueGroupId!,
                    dialogue.triggerNode.dialogueGroupSettings.lookupValueId))
                .ToDictionary(group => group.Key, group => group.ToArray());
            // Self-register so runtime values (e.g. NeoDialogueReference) can
            // reach the trigger API through the client without a compile-time
            // dependency on the generated NeoDialogues.
            client.RegisterDialoguesApi(this);
        }

        /// <summary>
        /// Triggers a direct dialogue. Returns true when it started or was
        /// queued; <paramref name="onFinish"/> runs once it ends.
        /// </summary>
        public bool TryTrigger(string dialogueId, Action? onFinish = null)
        {
            client.EnsureNotDisposed();
            if (!HasPresenter())
                return false;
            if (!client.dialogues.ContainsKey(dialogueId))
            {
                logger.LogWarning($"Dialogue '{dialogueId}' was not found.");
                return false;
            }
            if (!TryEvaluateDirectDialogue(dialogueId, out DialogueModel? data, out Exception? error))
            {
                if (error != null)
                    logger.LogException(error);
                return false;
            }
            return Enqueue(data!, ResolveDirectTrigger(data!), onFinish);
        }

        public int VisitCount(string pointer)
        {
            return NeoDialogueMemoryQueries.VisitCount(memoryStore, pointer);
        }

        public bool HasVisited(string pointer)
        {
            return NeoDialogueMemoryQueries.HasVisited(memoryStore, pointer);
        }

        /// <summary>
        /// Evaluates whether a direct dialogue can trigger without constructing or
        /// registering a runtime <see cref="NeoDialogue"/> instance.
        /// </summary>
        public bool CanTrigger(string dialogueId)
        {
            client.EnsureNotDisposed();
            return TryEvaluateDirectDialogue(dialogueId, out _, out _);
        }

        private bool TryEvaluateDirectDialogue(
            string dialogueId,
            out DialogueModel? data,
            out Exception? error)
        {
            error = null;
            if (!client.dialogues.TryGetValue(dialogueId, out data))
                return false;

            var validationError = ValidateTriggerableDialogue(data, directTrigger: true);
            if (validationError != null)
            {
                error = validationError;
                return false;
            }

            var groupId = data.triggerNode?.dialogueGroupSettings?.dialogueGroupId;
            var trigger = ResolveDirectTrigger(data);
            var context = CreateContext(data, trigger);
            try
            {
                if (!EvaluateGroupConditionChain(groupId, context, trigger))
                    return false;
                if (!PassesOccurrenceLimit(data))
                    return false;
                context.CurrentPrimary = ResolveTriggerCurrentPrimary(data, trigger);
                return NeoDialogueConditionEvaluator.EvaluateAll(
                    client,
                    data.triggerNode?.conditions,
                    context,
                    memoryStore);
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }
        }

        internal bool CanTriggerGroup(
            string groupId,
            object? trigger,
            string? lookupValueId)
        {
            client.EnsureNotDisposed();
            return TrySelectGroupDialogue(
                groupId,
                trigger,
                lookupValueId,
                selectRankedCandidate: false,
                out _,
                out _,
                out _);
        }

        internal bool TryTriggerGroup(string groupId, Action? onFinish)
        {
            client.EnsureNotDisposed();
            return HasPresenter() && TrySelectAndEnqueue(groupId, null, null, onFinish);
        }

        internal bool TryTriggerLookupGroup(
            string groupId,
            object? lookup,
            string? lookupValueId,
            Action? onFinish)
        {
            client.EnsureNotDisposed();
            if (!HasPresenter())
                return false;
            if (string.IsNullOrEmpty(lookupValueId))
            {
                logger.LogException(new InvalidOperationException(
                    $"Lookup dialogue group '{groupId}' requires a value with a Neo value id."));
                return false;
            }
            return TrySelectAndEnqueue(groupId, lookup, lookupValueId, onFinish);
        }

        private bool TrySelectAndEnqueue(
            string groupId,
            object? trigger,
            string? lookupValueId,
            Action? onFinish)
        {
            bool selected = TrySelectGroupDialogue(
                groupId,
                trigger,
                lookupValueId,
                selectRankedCandidate: true,
                out DialogueModel? data,
                out List<NeoDialogueTriggerWarning> warnings,
                out Exception? error);
            foreach (var warning in warnings)
                logger.LogWarning(warning.Message);
            if (error != null)
                logger.LogException(error);
            return selected && Enqueue(data!, trigger, onFinish);
        }

        /// <summary>
        /// One warning per client: a host without a presenter rejects every
        /// request rather than queueing dialogues nothing will show.
        /// </summary>
        private bool HasPresenter()
        {
            if (OnTrigger != null)
                return true;
            if (!warnedNoPresenter)
            {
                warnedNoPresenter = true;
                logger.LogWarning(
                    "Dialogue request rejected because nothing presents dialogues. Subscribe to Dialogues.OnTrigger before triggering.");
            }
            return false;
        }

        private bool Enqueue(DialogueModel data, object? trigger, Action? onFinish)
        {
            if (!requestedIds.Add(data.id))
                return false;
            requests.Enqueue(new NeoDialogueRequest(data, trigger, onFinish));
            client.RunDialogueTurn();
            return true;
        }

        /// <summary>
        /// Ends a finished dialogue's turn and starts queued ones until one
        /// is running. The client calls it only outside NeoScript executions;
        /// a request or end inside one reaches here at the outermost exit.
        /// </summary>
        internal void RunTurn()
        {
            // Code a turn runs can end or request a dialogue; this loop
            // picks that up instead of nesting a second turn.
            if (runningTurn)
                return;
            runningTurn = true;
            try
            {
                while (true)
                {
                    if (ended != null)
                    {
                        Action? onFinish = ended.onFinish;
                        ended = null;
                        RunOnFinish(onFinish);
                        continue;
                    }
                    if (running != null || requests.Count == 0)
                        return;
                    Start(requests.Dequeue());
                }
            }
            finally
            {
                runningTurn = false;
            }
        }

        private void Start(NeoDialogueRequest request)
        {
            Action<NeoDialogue>? presenter = OnTrigger;
            if (presenter == null)
            {
                logger.LogWarning(
                    $"Dropped {requests.Count + 1} queued dialogue requests because Dialogues.OnTrigger has no subscriber.");
                ClearRequests();
                return;
            }
            running = request;
            NeoDialogue? dialogue = null;
            try
            {
                dialogue = CreateDialogue(request, () => End(request));
                presenter(dialogue);
                // A presenter that disposed the dialogue already ended its turn.
                if (dialogue.State == NeoDialogueState.Created)
                    dialogue.Start();
            }
            catch (Exception error)
            {
                // A failure Start() rethrows was logged when it disposed the dialogue.
                if (dialogue?.State != NeoDialogueState.Disposed)
                    logger.LogException(error);
                if (dialogue == null)
                    End(request);
                else
                    dialogue.Dispose();
            }
        }

        private void End(NeoDialogueRequest request)
        {
            requestedIds.Remove(request.data.id);
            running = null;
            ended = request;
            client.RunDialogueTurn();
        }

        private void RunOnFinish(Action? onFinish)
        {
            try
            {
                onFinish?.Invoke();
            }
            catch (Exception error)
            {
                logger.LogException(error);
            }
        }

        /// <summary>Client disposal: nothing queued runs, and no <c>onFinish</c> does either.</summary>
        internal void ClearRequests()
        {
            requests.Clear();
            requestedIds.Clear();
            running = null;
            ended = null;
        }

        private bool TrySelectGroupDialogue(
            string groupId,
            object? trigger,
            string? lookupValueId,
            bool selectRankedCandidate,
            out DialogueModel? selected,
            out List<NeoDialogueTriggerWarning> warnings,
            out Exception? error)
        {
            selected = null;
            warnings = new List<NeoDialogueTriggerWarning>();
            var candidateWarnings = warnings;
            error = null;
            if (!client.dialogueGroups.ContainsKey(groupId))
            {
                warnings.Add(new NeoDialogueTriggerWarning(
                    $"Dialogue group '{groupId}' was not found.",
                    groupId: groupId));
                return false;
            }

            try
            {
                if (!EvaluateGroupConditionChain(
                    groupId,
                    new NeoDialogueContext(
                        dialogueId: "",
                        groupId: groupId,
                        trigger: trigger,
                        primary: null,
                        linkedValues: new Dictionary<string, object?>()),
                    trigger))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex;
                return false;
            }

            if (!dialoguesByTrigger.TryGetValue(
                (groupId, lookupValueId),
                out DialogueModel[] indexedCandidates))
            {
                warnings.Add(new NeoDialogueTriggerWarning(
                    lookupValueId is null
                        ? $"Dialogue group '{groupId}' has no standard trigger dialogues."
                        : $"Dialogue group '{groupId}' has no trigger dialogues linked to value '{lookupValueId}'.",
                    groupId: groupId));
                return false;
            }
            var candidates = indexedCandidates
                .Where(dialogue =>
                {
                    var validationError = ValidateTriggerableDialogue(dialogue, directTrigger: false);
                    if (validationError != null)
                    {
                        candidateWarnings.Add(new NeoDialogueTriggerWarning(
                            validationError.Message,
                            dialogueId: dialogue.id,
                            groupId: groupId));
                        return false;
                    }
                    if (!PassesOccurrenceLimit(dialogue))
                        return false;
                    var context = CreateContext(dialogue, trigger);
                    try
                    {
                        context.CurrentPrimary = ResolveTriggerCurrentPrimary(dialogue, trigger);
                        return NeoDialogueConditionEvaluator.EvaluateAll(
                            client,
                            dialogue.triggerNode?.conditions,
                            context,
                            memoryStore);
                    }
                    catch (Exception ex)
                    {
                        candidateWarnings.Add(new NeoDialogueTriggerWarning(
                            NeoCompose.Runtime.NeoScript.NeoScriptDebug.FormatError(ex),
                            dialogueId: dialogue.id,
                            groupId: groupId));
                        return false;
                    }
                })
                .ToArray();

            if (candidates.Length == 0)
            {
                return false;
            }

            selected = selectRankedCandidate
                ? SelectRankedCandidate(candidates, groupId, warnings)
                : candidates[0];
            return true;
        }

        public NeoDialogueWatcher OnEligible(Action<NeoDialogue> handler)
        {
            return OnEligible(handler, new NeoOnEligibleOptions());
        }

        public NeoDialogueWatcher OnEligible(Action<NeoDialogue> handler, NeoOnEligibleOptions options)
        {
            eligibleHandlers.Add(handler);
            return new NeoDialogueWatcher(() => eligibleHandlers.Remove(handler));
        }

        protected void EmitEligible(NeoDialogue dialogue)
        {
            foreach (var handler in eligibleHandlers.ToArray())
            {
                handler(dialogue);
            }
        }

        protected void EmitEligibleError(NeoDialogueEligibilityError error)
        {
            if (OnEligibleError != null)
            {
                OnEligibleError.Invoke(error);
                return;
            }
            logger.LogException(error.Exception);
        }

        /// <summary>
        /// Builds a request's dialogue when its turn comes, so a queued one
        /// registers nothing and records no visit while it waits.
        /// </summary>
        private NeoDialogue CreateDialogue(NeoDialogueRequest request, Action ended)
        {
            DialogueModel data = request.data;
            return new NeoDialogue(
                client,
                data,
                CreateContext(data, request.trigger),
                logger,
                options,
                memoryStore,
                valueResolver,
                data.triggerNode?.dialogueGroupSettings?.dialogueGroupId,
                ended);
        }

        protected NeoDialogueContext CreateContext(
            DialogueModel data,
            object? trigger = null)
        {
            string? groupId = data.triggerNode?.dialogueGroupSettings?.dialogueGroupId;
            return new NeoDialogueContext(
                data.id,
                groupId,
                trigger,
                ResolvePrimary(data.primaryLinkedValueId),
                ResolveLinkedValues(data.linkedValues));
        }

        protected internal object? ResolveValue(string valueId)
        {
            return valueResolver?.Invoke(valueId);
        }

        internal static string? GetValueId(object? value)
        {
            return value is INeoValueReference reference
                ? reference.valueId
                : null;
        }

        private Exception? ValidateTriggerableDialogue(
            DialogueModel dialogue,
            bool directTrigger)
        {
            if (dialogue.triggerNode == null)
            {
                return new InvalidOperationException(
                    $"Dialogue '{dialogue.id}' is missing a trigger node.");
            }
            if (string.IsNullOrEmpty(dialogue.triggerNode.toNodeId))
            {
                return new InvalidOperationException(
                    $"Dialogue '{dialogue.id}' trigger node does not point to a body node.");
            }
            if (dialogue.nodes == null || !dialogue.nodes.ContainsKey(dialogue.triggerNode.toNodeId!))
            {
                return new InvalidOperationException(
                    $"Dialogue '{dialogue.id}' trigger node points to missing body node '{dialogue.triggerNode.toNodeId}'.");
            }

            var groupId = dialogue.triggerNode.dialogueGroupSettings?.dialogueGroupId;
            if (groupId != null
                && client.dialogueGroups.TryGetValue(groupId, out DialogueGroupModel group)
                && group is NeoCompose.Runtime.Json.LookupDialogueGroup
                && directTrigger)
            {
                string? lookupValueId = dialogue.triggerNode.dialogueGroupSettings?.lookupValueId;
                if (string.IsNullOrEmpty(lookupValueId))
                {
                    return new InvalidOperationException(
                        $"Lookup dialogue '{dialogue.id}' is missing lookupValueId.");
                }
                if (!client.TryGetValue(lookupValueId!, out NeoCompose.Runtime.Json.MemberValue? _))
                {
                    return new InvalidOperationException(
                        $"Lookup dialogue '{dialogue.id}' references missing lookup value '{lookupValueId}'.");
                }
                if (ResolveValue(lookupValueId!) == null)
                {
                    return new InvalidOperationException(
                        $"Lookup dialogue '{dialogue.id}' could not resolve lookup value '{lookupValueId}'.");
                }
            }
            return null;
        }

        private object? ResolveDirectTrigger(DialogueModel dialogue)
        {
            string? groupId = dialogue.triggerNode?.dialogueGroupSettings?.dialogueGroupId;
            if (groupId == null
                || !client.dialogueGroups.TryGetValue(groupId, out DialogueGroupModel group)
                || group is not NeoCompose.Runtime.Json.LookupDialogueGroup)
            {
                return null;
            }
            string? lookupValueId = dialogue.triggerNode?.dialogueGroupSettings?.lookupValueId;
            return string.IsNullOrEmpty(lookupValueId) ? null : ResolveValue(lookupValueId!);
        }

        private object? ResolvePrimary(string? primaryLinkedValueId)
        {
            if (string.IsNullOrEmpty(primaryLinkedValueId))
                return null;
            return ResolveValue(primaryLinkedValueId!);
        }

        private object? ResolveTriggerCurrentPrimary(DialogueModel dialogue, object? trigger)
        {
            object? triggerPrimary = ResolvePrimary(dialogue.triggerNode?.primaryLinkedValueId);
            if (triggerPrimary != null)
                return triggerPrimary;
            object? dialoguePrimary = ResolvePrimary(dialogue.primaryLinkedValueId);
            if (dialoguePrimary != null)
                return dialoguePrimary;
            return trigger;
        }

        private IReadOnlyDictionary<string, object?> ResolveLinkedValues(
            NeoCompose.Runtime.Json.DialogueLinkedValue[]? linkedValues)
        {
            var result = new Dictionary<string, object?>();
            if (linkedValues == null)
                return result;
            foreach (var linkedValue in linkedValues)
            {
                if (string.IsNullOrEmpty(linkedValue.valueId))
                    continue;
                result[linkedValue.valueId] = ResolveValue(linkedValue.valueId);
            }
            return result;
        }

        private bool EvaluateGroupConditionChain(
            string? groupId,
            NeoDialogueContext context,
            object? thisValue)
        {
            object? previousPrimary = context.CurrentPrimary;
            context.CurrentPrimary = thisValue;
            try
            {
                foreach (var group in GetGroupChain(groupId))
                {
                    if (!NeoDialogueConditionEvaluator.EvaluateAll(
                        client,
                        group.conditions,
                        context,
                        memoryStore))
                    {
                        return false;
                    }
                }
                return true;
            }
            finally
            {
                context.CurrentPrimary = previousPrimary;
            }
        }

        private IEnumerable<DialogueGroupModel> GetGroupChain(string? groupId)
        {
            var chain = new List<DialogueGroupModel>();
            var seen = new HashSet<string>();
            string? currentId = groupId;
            while (!string.IsNullOrEmpty(currentId)
                && seen.Add(currentId)
                && client.dialogueGroups.TryGetValue(currentId, out DialogueGroupModel group))
            {
                chain.Add(group);
                currentId = group.parentDialogueGroupId;
            }
            chain.Reverse();
            return chain;
        }

        private bool PassesOccurrenceLimit(DialogueModel dialogue)
        {
            var limit = dialogue.triggerNode?.occurrenceLimitSettings;
            if (limit == null)
                return true;
            var visitCount = memoryStore
                ?.FindDialogueMemory(dialogue.id)
                ?.VisitCount
                ?? 0;
            return visitCount < limit.count;
        }

        private DialogueModel SelectRankedCandidate(
            IReadOnlyList<DialogueModel> candidates,
            string groupId,
            List<NeoDialogueTriggerWarning> warnings)
        {
            var priorityGroup = ResolvePriorityGroup(groupId, warnings);
            var priorityIndexes = candidates.ToDictionary(
                dialogue => dialogue.id,
                dialogue => PriorityIndex(dialogue, priorityGroup, warnings));
            int topPriority = candidates.Min(dialogue => priorityIndexes[dialogue.id]);
            var samePriority = candidates
                .Where(dialogue => priorityIndexes[dialogue.id] == topPriority)
                .ToArray();

            var ordered = samePriority
                .Where(dialogue => dialogue.triggerNode?.dialogueGroupSettings?.priority?.relativeOrder != null)
                .ToArray();
            if (ordered.Length > 0)
            {
                int bestOrder = ordered.Min(dialogue =>
                    dialogue.triggerNode!.dialogueGroupSettings!.priority.relativeOrder!.Value);
                var sameOrder = ordered
                    .Where(dialogue =>
                        dialogue.triggerNode!.dialogueGroupSettings!.priority.relativeOrder == bestOrder)
                    .ToArray();
                if (sameOrder.Length > 1)
                {
                    warnings.Add(new NeoDialogueTriggerWarning(
                        $"Multiple dialogues share relativeOrder {bestOrder} in group '{groupId}'.",
                        groupId: groupId));
                }
                return BreakVisitAndRecencyTie(sameOrder);
            }

            return BreakVisitAndRecencyTie(samePriority);
        }

        private PriorityGroupModel? ResolvePriorityGroup(
            string groupId,
            List<NeoDialogueTriggerWarning> warnings)
        {
            string? priorityGroupId = null;
            foreach (var group in GetGroupChain(groupId).Reverse())
            {
                if (string.IsNullOrEmpty(group.priorityGroupIdOverride))
                    continue;
                priorityGroupId = group.priorityGroupIdOverride;
                break;
            }
            priorityGroupId ??= client.project.defaultPriorityGroupId;

            if (string.IsNullOrEmpty(priorityGroupId))
                return null;
            if (client.priorityGroups.TryGetValue(priorityGroupId!, out PriorityGroupModel priorityGroup))
            {
                return priorityGroup;
            }

            warnings.Add(new NeoDialogueTriggerWarning(
                $"Dialogue group '{groupId}' references missing priority group '{priorityGroupId}'.",
                groupId: groupId));

            string? fallbackId = client.project.defaultPriorityGroupId;
            if (!string.IsNullOrEmpty(fallbackId)
                && fallbackId != priorityGroupId
                && client.priorityGroups.TryGetValue(fallbackId!, out PriorityGroupModel fallback))
            {
                return fallback;
            }
            return null;
        }

        private int PriorityIndex(
            DialogueModel dialogue,
            PriorityGroupModel? priorityGroup,
            List<NeoDialogueTriggerWarning> warnings)
        {
            if (priorityGroup?.options == null || priorityGroup.options.Length == 0)
                return 0;
            string? priorityOptionId = dialogue
                .triggerNode
                ?.dialogueGroupSettings
                ?.priority
                ?.priorityOptionId;
            int lowest = priorityGroup.options.Length - 1;
            if (string.IsNullOrEmpty(priorityOptionId))
                return lowest;
            for (int i = 0; i < priorityGroup.options.Length; i++)
            {
                if (priorityGroup.options[i].id == priorityOptionId)
                    return i;
            }
            warnings.Add(new NeoDialogueTriggerWarning(
                $"Dialogue '{dialogue.id}' references missing priority option '{priorityOptionId}'.",
                dialogueId: dialogue.id,
                groupId: dialogue.triggerNode?.dialogueGroupSettings?.dialogueGroupId));
            return lowest;
        }

        private DialogueModel BreakVisitAndRecencyTie(IReadOnlyList<DialogueModel> candidates)
        {
            int lowestVisitCount = candidates.Min(VisitCount);
            var sameVisitCount = candidates
                .Where(dialogue => VisitCount(dialogue) == lowestVisitCount)
                .ToArray();
            if (sameVisitCount.Length == 1)
                return sameVisitCount[0];
            return WeightedRandomByLastVisitedAt(sameVisitCount);
        }

        private int VisitCount(DialogueModel dialogue)
        {
            return memoryStore?.FindDialogueMemory(dialogue.id)?.VisitCount ?? 0;
        }

        private DialogueModel WeightedRandomByLastVisitedAt(IReadOnlyList<DialogueModel> candidates)
        {
            var now = options.ResolveUtcNow();
            double total = 0;
            var weights = new double[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                weights[i] = RecencyWeight(candidates[i], now);
                total += weights[i];
            }

            double selected = options.ResolveRandomDouble() * total;
            for (int i = 0; i < candidates.Count; i++)
            {
                selected -= weights[i];
                if (selected <= 0)
                    return candidates[i];
            }
            return candidates[candidates.Count - 1];
        }

        private double RecencyWeight(DialogueModel dialogue, DateTime now)
        {
            string? lastVisitedAt = memoryStore
                ?.FindDialogueMemory(dialogue.id)
                ?.LastVisitedAt;
            if (string.IsNullOrEmpty(lastVisitedAt))
                return 4;
            if (!DateTime.TryParse(
                    lastVisitedAt,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out DateTime parsed))
            {
                return 4;
            }
            double halfLifeSeconds = Math.Max(1, options.ResolveRecencyHalfLife().TotalSeconds);
            double ageSeconds = Math.Max(0, (now - parsed.ToUniversalTime()).TotalSeconds);
            return Math.Max(0.25, Math.Min(4, 1 + ageSeconds / halfLifeSeconds));
        }
    }

    public abstract class NeoDialogueGroupBase
    {
        protected NeoDialoguesBase root
        {
            get;
        }
        protected string groupId
        {
            get;
        }
        public string GroupId
        {
            get;
        }

        protected NeoDialogueGroupBase(NeoDialoguesBase root, string groupId)
        {
            this.root = root;
            this.groupId = groupId;
            GroupId = groupId;
        }

        public NeoDialogueWatcher OnEligible(Action<NeoDialogue> handler)
        {
            return OnEligible(handler, new NeoOnEligibleOptions());
        }

        public virtual NeoDialogueWatcher OnEligible(
            Action<NeoDialogue> handler,
            NeoOnEligibleOptions options)
        {
            return new NeoDialogueWatcher(() => { });
        }
    }

    public abstract class NeoTriggerableDialogueGroup : NeoDialogueGroupBase
    {
        public event Action<NeoDialogueEligibilityError>? OnEligibleError;

        protected NeoTriggerableDialogueGroup(NeoDialoguesBase root, string groupId)
            : base(root, groupId) { }

        protected void EmitEligibleError(NeoDialogueEligibilityError error)
        {
            OnEligibleError?.Invoke(error);
        }
    }

    public abstract class NeoStandardDialogueGroup : NeoTriggerableDialogueGroup
    {
        protected NeoStandardDialogueGroup(NeoDialoguesBase root, string groupId)
            : base(root, groupId) { }

        protected bool TryTriggerStandard(Action? onFinish)
        {
            return root.TryTriggerGroup(groupId, onFinish);
        }

        /// <summary>
        /// Evaluates group eligibility without constructing a dialogue instance.
        /// </summary>
        public bool CanTrigger()
        {
            return root.CanTriggerGroup(groupId, null, null);
        }
    }

    public abstract class NeoLookupDialogueGroup<TLookup> : NeoTriggerableDialogueGroup
        where TLookup : class
    {
        protected NeoLookupDialogueGroup(NeoDialoguesBase root, string groupId)
            : base(root, groupId) { }

        protected bool TryTriggerLookup(TLookup lookup, Action? onFinish)
        {
            if (lookup == null)
                throw new ArgumentNullException(nameof(lookup));
            return root.TryTriggerLookupGroup(
                groupId,
                lookup,
                NeoDialoguesBase.GetValueId(lookup),
                onFinish);
        }

        /// <summary>
        /// Evaluates lookup-group eligibility without constructing a dialogue instance.
        /// </summary>
        public bool CanTrigger(TLookup lookup)
        {
            if (lookup == null)
                throw new ArgumentNullException(nameof(lookup));
            string? valueId = NeoDialoguesBase.GetValueId(lookup);
            if (string.IsNullOrEmpty(valueId))
                return false;
            return root.CanTriggerGroup(groupId, lookup, valueId);
        }
    }

    /// <summary>A selected dialogue waiting for its turn, or the one running.</summary>
    internal sealed class NeoDialogueRequest
    {
        internal readonly DialogueModel data;
        internal readonly object? trigger;
        internal readonly Action? onFinish;

        internal NeoDialogueRequest(DialogueModel data, object? trigger, Action? onFinish)
        {
            this.data = data;
            this.trigger = trigger;
            this.onFinish = onFinish;
        }
    }

    public abstract class NeoFolderDialogueGroup : NeoDialogueGroupBase
    {
        protected NeoFolderDialogueGroup(NeoDialoguesBase root, string groupId)
            : base(root, groupId) { }
    }
}
