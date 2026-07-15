// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// A stable, lightweight handle to a dialogue by id — the runtime value a
    /// <c>DialogueLookup</c> member resolves to. Triggering goes through
    /// <see cref="TryTrigger(out NeoDialogue)"/>, which honors the dialogue's
    /// trigger criteria (conditions, occurrence limits, group priority) exactly
    /// like <see cref="NeoDialoguesBase.TryTrigger(string, out NeoDialogue)"/> —
    /// the reference never bypasses them.
    ///
    /// <para>Two ways to construct one:</para>
    /// <list type="bullet">
    ///   <item>the <see cref="NeoDialogueReference(string)"/> id-only ctor, for
    ///     authoring a value to write, e.g.
    ///     <c>barrier.InteractDialogue = new NeoDialogueReference(id);</c>. Such
    ///     an instance is <em>unbound</em> and cannot trigger until the value is
    ///     read back from the SDK.</item>
    ///   <item>the client-bound ctor, emitted by generated getters and the
    ///     dialogue-reference set.</item>
    /// </list>
    /// </summary>
    public sealed class NeoDialogueReference
    {
        private readonly NeoClient? client;

        /// <summary>The referenced <c>dialogueId</c>.</summary>
        public string Id { get; }

        /// <summary>
        /// Authoring/assignment ctor. Produces an <em>unbound</em> reference
        /// suitable for setting a DialogueLookup value or adding to a
        /// <see cref="NeoDialogueReferenceSet"/>. Calling
        /// <see cref="TryTrigger(out NeoDialogue)"/> on an unbound reference
        /// throws — read the value back from the SDK to trigger it.
        /// </summary>
        public NeoDialogueReference(string dialogueId)
        {
            Id = dialogueId ?? throw new ArgumentNullException(nameof(dialogueId));
            client = null;
        }

        /// <summary>
        /// Client-bound ctor — emitted by generated getters and the
        /// dialogue-reference set enumerator. Public because the generated code
        /// lives in a separate assembly from this runtime.
        /// </summary>
        public NeoDialogueReference(NeoClient client, string dialogueId)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            Id = dialogueId ?? throw new ArgumentNullException(nameof(dialogueId));
        }

        /// <summary>
        /// Triggers the referenced dialogue, honoring all trigger criteria.
        /// Returns <c>false</c> when the id is unknown or the criteria are not
        /// met right now.
        /// </summary>
        public bool TryTrigger(out NeoDialogue dialogue)
        {
            return RequireDialoguesApi().TryTrigger(Id, out dialogue);
        }

        /// <summary>
        /// Triggers the referenced dialogue and surfaces the full
        /// <see cref="NeoDialogueTriggerResult"/> (warnings/errors included).
        /// </summary>
        public bool TryTrigger(out NeoDialogueTriggerResult result)
        {
            return RequireDialoguesApi().TryTrigger(Id, out result);
        }

        private NeoDialoguesBase RequireDialoguesApi()
        {
            if (client is null)
            {
                throw new InvalidOperationException(
                    "This NeoDialogueReference was constructed for assignment only " +
                    "and is not bound to a client; read the value back from the SDK " +
                    "before triggering.");
            }
            if (client.DialoguesApi is null)
            {
                throw new InvalidOperationException(
                    "No dialogues API is registered on this client. Construct the " +
                    "generated client (which builds its Dialogues) before triggering " +
                    "a NeoDialogueReference.");
            }
            return client.DialoguesApi;
        }
    }
}
