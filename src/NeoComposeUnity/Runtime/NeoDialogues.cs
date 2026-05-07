// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using DialogueModel = NeoCompose.Runtime.Json.Dialogue;

namespace NeoCompose.Runtime
{
    public abstract class NeoDialoguesBase
    {
        private readonly List<Action<NeoDialogue>> eligibleHandlers = new();

        protected NeoClient client { get; }
        protected NeoDialogueRuntimeOptions options { get; }
        protected INeoDialogueLogger logger { get; }

        public event Action<NeoDialogueEligibilityError>? OnEligibleError;

        protected NeoDialoguesBase(NeoClient client, NeoDialogueRuntimeOptions? options = null)
        {
            this.client = client;
            this.options = options ?? new NeoDialogueRuntimeOptions();
            logger = this.options.ResolveLogger();
        }

        public virtual bool TryTrigger(string dialogueId, out NeoDialogue dialogue)
        {
            if (TryTrigger(dialogueId, out NeoDialogueTriggerResult result) && result.dialogue != null)
            {
                dialogue = result.dialogue;
                return true;
            }
            if (result.error != null)
            {
                logger.LogException(result.error);
            }
            dialogue = null!;
            return false;
        }

        public virtual bool TryTrigger(string dialogueId, out NeoDialogueTriggerResult result)
        {
            if (!client.dialogues.TryGetValue(dialogueId, out DialogueModel data))
            {
                result = NeoDialogueTriggerResult.NotFound();
                return false;
            }

            result = NeoDialogueTriggerResult.Success(CreateDialogue(data, null, null));
            return true;
        }

        internal bool TryTriggerGroup(
            string groupId,
            object? trigger,
            string? lookupValueId,
            out NeoDialogueTriggerResult result)
        {
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

            var candidates = client.dialogues.Values
                .Where(dialogue => IsDialogueInGroup(dialogue, groupId, lookupValueId))
                .OrderBy(dialogue => dialogue.name)
                .ThenBy(dialogue => dialogue.id)
                .ToArray();

            if (candidates.Length == 0)
            {
                result = NeoDialogueTriggerResult.NotFound();
                return false;
            }

            result = NeoDialogueTriggerResult.Success(
                CreateDialogue(candidates[0], trigger, trigger));
            return true;
        }

        public IDisposable OnEligible(Action<NeoDialogue> handler)
        {
            return OnEligible(handler, new NeoOnEligibleOptions());
        }

        public IDisposable OnEligible(Action<NeoDialogue> handler, NeoOnEligibleOptions options)
        {
            eligibleHandlers.Add(handler);
            return new NeoDisposableAction(() => eligibleHandlers.Remove(handler));
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
            logger.LogException(error.exception);
        }

        protected NeoDialogue CreateDialogue(
            DialogueModel data,
            object? trigger = null,
            object? primary = null)
        {
            string? groupId = data.triggerNode?.dialogueGroupSettings?.dialogueGroupId;
            var context = new NeoDialogueContext(
                data.id,
                groupId,
                trigger,
                primary,
                new Dictionary<string, object?>());
            return new NeoDialogue(data, context, logger, groupId);
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
    }

    public abstract class NeoDialogueGroupBase
    {
        protected NeoDialoguesBase root { get; }
        public string groupId { get; }

        protected NeoDialogueGroupBase(NeoDialoguesBase root, string groupId)
        {
            this.root = root;
            this.groupId = groupId;
        }

        public IDisposable OnEligible(Action<NeoDialogue> handler)
        {
            return OnEligible(handler, new NeoOnEligibleOptions());
        }

        public virtual IDisposable OnEligible(
            Action<NeoDialogue> handler,
            NeoOnEligibleOptions options)
        {
            return new NeoDisposableAction(() => { });
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
            if (TryTriggerStandard(out NeoDialogueTriggerResult result) && result.dialogue != null)
            {
                dialogue = result.dialogue;
                return true;
            }
            if (result.error != null)
            {
                EmitEligibleError(new NeoDialogueEligibilityError(result.error, groupId: groupId));
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
            if (TryTriggerLookup(lookup, out NeoDialogueTriggerResult result) && result.dialogue != null)
            {
                dialogue = result.dialogue;
                return true;
            }
            if (result.error != null)
            {
                EmitEligibleError(new NeoDialogueEligibilityError(result.error, groupId: groupId));
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
