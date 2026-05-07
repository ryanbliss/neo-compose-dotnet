// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using DialogueModel = NeoCompose.Runtime.Json.Dialogue;

namespace NeoCompose.Runtime
{
    public abstract class NeoDialoguesBase
    {
        private readonly List<Action<NeoDialogue>> eligibleHandlers = new();

        protected NeoDialogueRuntimeOptions options { get; }
        protected INeoDialogueLogger logger { get; }

        public event Action<NeoDialogueEligibilityError>? OnEligibleError;

        protected NeoDialoguesBase(NeoDialogueRuntimeOptions? options = null)
        {
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
            result = NeoDialogueTriggerResult.NotFound();
            return false;
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

        protected NeoDialogue CreatePlaceholderDialogue(
            DialogueModel data,
            string? groupId,
            object? trigger = null,
            object? primary = null)
        {
            var context = new NeoDialogueContext(
                data.id,
                groupId,
                trigger,
                primary,
                new Dictionary<string, object?>());
            return new NeoDialogue(data, context, groupId);
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
            dialogue = null!;
            return false;
        }

        protected bool TryTriggerStandard(out NeoDialogueTriggerResult result)
        {
            result = NeoDialogueTriggerResult.NotFound();
            return false;
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
            dialogue = null!;
            return false;
        }

        protected bool TryTriggerLookup(TLookup lookup, out NeoDialogueTriggerResult result)
        {
            if (lookup == null) throw new ArgumentNullException(nameof(lookup));
            result = NeoDialogueTriggerResult.NotFound();
            return false;
        }
    }

    public abstract class NeoFolderDialogueGroup : NeoDialogueGroupBase
    {
        protected NeoFolderDialogueGroup(NeoDialoguesBase root, string groupId)
            : base(root, groupId) { }
    }
}
