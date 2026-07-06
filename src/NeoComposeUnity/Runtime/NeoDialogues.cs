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

        protected NeoClient client { get; }
        protected NeoDialogueRuntimeOptions options { get; }
        protected INeoDialogueLogger logger { get; }
        protected INeoDialogueMemoryStore? memoryStore { get; }
        protected NeoDialogueValueResolver? valueResolver { get; }

        public event Action<NeoDialogueEligibilityError>? OnEligibleError;

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
            // Self-register so runtime values (e.g. NeoDialogueReference) can
            // reach the trigger API through the client without a compile-time
            // dependency on the generated NeoDialogues.
            client.RegisterDialoguesApi(this);
        }

        public virtual bool TryTrigger(string dialogueId, out NeoDialogue dialogue)
        {
            client.EnsureNotDisposed();
            if (TryTrigger(dialogueId, out NeoDialogueTriggerResult result) && result.Dialogue != null)
            {
                dialogue = result.Dialogue;
                return true;
            }
            if (result.Error != null)
            {
                logger.LogException(result.Error);
            }
            foreach (var warning in result.Warnings)
            {
                logger.LogWarning(warning.Message);
            }
            dialogue = null!;
            return false;
        }

        public int VisitCount(string pointer)
        {
            return NeoDialogueMemoryQueries.VisitCount(memoryStore, pointer);
        }

        public bool HasVisited(string pointer)
        {
            return NeoDialogueMemoryQueries.HasVisited(memoryStore, pointer);
        }

        public virtual bool TryTrigger(string dialogueId, out NeoDialogueTriggerResult result)
        {
            client.EnsureNotDisposed();
            if (!client.dialogues.TryGetValue(dialogueId, out DialogueModel data))
            {
                result = NeoDialogueTriggerResult.NotFound();
                return false;
            }

            var validationError = ValidateTriggerableDialogue(data, directTrigger: true);
            if (validationError != null)
            {
                result = NeoDialogueTriggerResult.Failed(validationError);
                return false;
            }

            var groupId = data.triggerNode?.dialogueGroupSettings?.dialogueGroupId;
            var trigger = ResolveDirectTrigger(data);
            var context = CreateContext(data, trigger);
            try
            {
                if (!EvaluateGroupConditionChain(groupId, context, trigger))
                {
                    result = NeoDialogueTriggerResult.NotFound();
                    return false;
                }
                if (!PassesOccurrenceLimit(data))
                {
                    result = NeoDialogueTriggerResult.NotFound();
                    return false;
                }
                context.CurrentPrimary = ResolveTriggerCurrentPrimary(data, trigger);
                if (!NeoDialogueConditionEvaluator.EvaluateAll(
                    client,
                    data.triggerNode?.conditions,
                    context,
                    memoryStore))
                {
                    result = NeoDialogueTriggerResult.NotFound();
                    return false;
                }
            }
            catch (Exception ex)
            {
                result = NeoDialogueTriggerResult.Failed(ex);
                return false;
            }

            result = NeoDialogueTriggerResult.Success(CreateDialogue(data, context));
            return true;
        }

        internal bool TryTriggerGroup(
            string groupId,
            object? trigger,
            string? lookupValueId,
            out NeoDialogueTriggerResult result)
        {
            client.EnsureNotDisposed();
            if (!client.dialogueGroups.ContainsKey(groupId))
            {
                result = NeoDialogueTriggerResult.NotFound(new[]
                {
                    new NeoDialogueTriggerWarning(
                        $"Dialogue group '{groupId}' was not found.",
                        groupId: groupId),
                });
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
                    result = NeoDialogueTriggerResult.NotFound();
                    return false;
                }
            }
            catch (Exception ex)
            {
                result = NeoDialogueTriggerResult.Failed(ex);
                return false;
            }

            var warnings = new List<NeoDialogueTriggerWarning>();
            var candidates = client.dialogues.Values
                .Where(dialogue => IsDialogueInGroup(dialogue, groupId, lookupValueId))
                .Where(dialogue =>
                {
                    var validationError = ValidateTriggerableDialogue(dialogue, directTrigger: false);
                    if (validationError != null)
                    {
                        warnings.Add(new NeoDialogueTriggerWarning(
                            validationError.Message,
                            dialogueId: dialogue.id,
                            groupId: groupId));
                        return false;
                    }
                    if (!PassesOccurrenceLimit(dialogue)) return false;
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
                        warnings.Add(new NeoDialogueTriggerWarning(
                            ex.Message,
                            dialogueId: dialogue.id,
                            groupId: groupId));
                        return false;
                    }
                })
                .ToArray();

            if (candidates.Length == 0)
            {
                result = NeoDialogueTriggerResult.NotFound(warnings);
                return false;
            }

            var selected = SelectRankedCandidate(candidates, groupId, warnings);
            result = NeoDialogueTriggerResult.Success(
                CreateDialogue(selected, CreateContext(selected, trigger)),
                warnings);
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

        protected NeoDialogue CreateDialogue(
            DialogueModel data,
            NeoDialogueContext context)
        {
            string? groupId = data.triggerNode?.dialogueGroupSettings?.dialogueGroupId;
            return new NeoDialogue(
                client,
                data,
                context,
                logger,
                options,
                memoryStore,
                valueResolver,
                groupId);
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

        protected object? ResolveValue(string valueId)
        {
            return valueResolver?.Invoke(valueId);
        }

        internal static string? GetValueId(object? value)
        {
            return value is INeoValueReference reference
                ? reference.valueId
                : null;
        }

        private static bool IsDialogueInGroup(
            DialogueModel dialogue,
            string groupId,
            string? lookupValueId)
        {
            var settings = dialogue.triggerNode?.dialogueGroupSettings;
            if (settings?.dialogueGroupId != groupId) return false;
            if (lookupValueId == null) return true;
            return settings.lookupValueId == lookupValueId;
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
                if (!client.TryGetValue(lookupValueId!, out NeoCompose.Runtime.Json.AttributeValue? _))
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
            if (string.IsNullOrEmpty(primaryLinkedValueId)) return null;
            return ResolveValue(primaryLinkedValueId!);
        }

        private object? ResolveTriggerCurrentPrimary(DialogueModel dialogue, object? trigger)
        {
            object? triggerPrimary = ResolvePrimary(dialogue.triggerNode?.primaryLinkedValueId);
            if (triggerPrimary != null) return triggerPrimary;
            object? dialoguePrimary = ResolvePrimary(dialogue.primaryLinkedValueId);
            if (dialoguePrimary != null) return dialoguePrimary;
            return trigger;
        }

        private IReadOnlyDictionary<string, object?> ResolveLinkedValues(
            NeoCompose.Runtime.Json.DialogueLinkedValue[]? linkedValues)
        {
            var result = new Dictionary<string, object?>();
            if (linkedValues == null) return result;
            foreach (var linkedValue in linkedValues)
            {
                if (string.IsNullOrEmpty(linkedValue.valueId)) continue;
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
            if (limit == null) return true;
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
                if (string.IsNullOrEmpty(group.priorityGroupIdOverride)) continue;
                priorityGroupId = group.priorityGroupIdOverride;
                break;
            }
            priorityGroupId ??= client.project.defaultPriorityGroupId;

            if (string.IsNullOrEmpty(priorityGroupId)) return null;
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
            if (priorityGroup?.options == null || priorityGroup.options.Length == 0) return 0;
            string? priorityTypeId = dialogue
                .triggerNode
                ?.dialogueGroupSettings
                ?.priority
                ?.priorityTypeId;
            int lowest = priorityGroup.options.Length - 1;
            if (string.IsNullOrEmpty(priorityTypeId)) return lowest;
            for (int i = 0; i < priorityGroup.options.Length; i++)
            {
                if (priorityGroup.options[i].id == priorityTypeId) return i;
            }
            warnings.Add(new NeoDialogueTriggerWarning(
                $"Dialogue '{dialogue.id}' references missing priority option '{priorityTypeId}'.",
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
            if (sameVisitCount.Length == 1) return sameVisitCount[0];
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
                if (selected <= 0) return candidates[i];
            }
            return candidates[candidates.Count - 1];
        }

        private double RecencyWeight(DialogueModel dialogue, DateTime now)
        {
            string? lastVisitedAt = memoryStore
                ?.FindDialogueMemory(dialogue.id)
                ?.LastVisitedAt;
            if (string.IsNullOrEmpty(lastVisitedAt)) return 4;
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
        protected NeoDialoguesBase root { get; }
        protected string groupId { get; }
        public string GroupId { get; }

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

        protected bool TryTriggerStandard(out NeoDialogue dialogue)
        {
            if (TryTriggerStandard(out NeoDialogueTriggerResult result) && result.Dialogue != null)
            {
                dialogue = result.Dialogue;
                return true;
            }
            if (result.Error != null)
            {
                EmitEligibleError(new NeoDialogueEligibilityError(result.Error, groupId: groupId));
            }
            dialogue = null!;
            return false;
        }

        protected bool TryTriggerStandard(out NeoDialogueTriggerResult result)
        {
            return root.TryTriggerGroup(groupId, null, null, out result);
        }
    }

    public abstract class NeoLookupDialogueGroup<TLookup> : NeoTriggerableDialogueGroup
        where TLookup : class
    {
        protected NeoLookupDialogueGroup(NeoDialoguesBase root, string groupId)
            : base(root, groupId) { }

        protected bool TryTriggerLookup(TLookup lookup, out NeoDialogue dialogue)
        {
            if (lookup == null) throw new ArgumentNullException(nameof(lookup));
            if (TryTriggerLookup(lookup, out NeoDialogueTriggerResult result) && result.Dialogue != null)
            {
                dialogue = result.Dialogue;
                return true;
            }
            if (result.Error != null)
            {
                EmitEligibleError(new NeoDialogueEligibilityError(result.Error, groupId: groupId));
            }
            dialogue = null!;
            return false;
        }

        protected bool TryTriggerLookup(TLookup lookup, out NeoDialogueTriggerResult result)
        {
            if (lookup == null) throw new ArgumentNullException(nameof(lookup));
            string? valueId = NeoDialoguesBase.GetValueId(lookup);
            if (string.IsNullOrEmpty(valueId))
            {
                result = NeoDialogueTriggerResult.Failed(
                    new InvalidOperationException(
                        $"Lookup dialogue group '{groupId}' requires a value with a Neo value id."));
                return false;
            }
            return root.TryTriggerGroup(groupId, lookup, valueId, out result);
        }
    }

    public abstract class NeoFolderDialogueGroup : NeoDialogueGroupBase
    {
        protected NeoFolderDialogueGroup(NeoDialoguesBase root, string groupId)
            : base(root, groupId) { }
    }
}
