// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using DialogueModel = NeoCompose.Runtime.Json.Dialogue;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a DialogueLookup-typed member. Stores the selected
    /// <c>dialogueId</c>s as a string-array value. Unlike
    /// <see cref="NeoMemberLookup"/> there is no collection to resolve
    /// against — the ids map directly to dialogues, so this node only exposes
    /// the raw selection (<see cref="Selected"/>); the id → dialogue mapping is
    /// done by <see cref="NeoDialogueReference"/> / the dedicated set.
    /// </summary>
    public class NeoMemberDialogueLookup
        : NeoMember<DialogueLookupMember, ArrayMemberValue>
    {
        public NeoMemberDialogueLookup(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberDialogueLookup(NeoClient client, DialogueLookupMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>Selected <c>dialogueId</c>s. Empty when nothing is set.</summary>
        public string[] Selected() => value?.value ?? System.Array.Empty<string>();
    }

    public class NeoMemberDialogueLookupWritable : NeoMemberDialogueLookup
    {
        public NeoMemberDialogueLookupWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberDialogueLookupWritable(NeoClient client, DialogueLookupMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>
        /// Overwrites the selected dialogue ids. Honors the single-select clamp
        /// (only the first id survives when <see cref="DialogueLookupMember.multiselect"/>
        /// is false) and enforces the <see cref="DialogueLookupMember.dialogueGroupId"/>
        /// scope. This is the single write choke point — <see cref="Add"/>,
        /// <see cref="Remove"/>, <see cref="Clear"/>, and the generated property
        /// setter all funnel through it, so the group scope is validated in one
        /// place (spec §5.2).
        /// </summary>
        public void Set(string[]? selectedIds)
        {
            if (member.required && (selectedIds is null || selectedIds.Length == 0))
            {
                throw new System.ArgumentNullException(
                    nameof(selectedIds),
                    $"Cannot be null/empty when {nameof(member)}.{nameof(member.required)} is true");
            }

            string[]? normalized = selectedIds;
            if (normalized is not null && !member.multiselect && normalized.Length > 1)
            {
                normalized = new[] { normalized[0] };
            }

            if (normalized is not null && member.dialogueGroupId is not null)
            {
                foreach (var dialogueId in normalized)
                {
                    ValidateInGroup(dialogueId, member.dialogueGroupId);
                }
            }

            string nowIso = System.DateTime.UtcNow.ToString("o");

            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = normalized;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable);
                NotifyChanged();
                return;
            }

            ArrayMemberValue newRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = normalized,
            };
            BindNewValue(newRow);
            NotifyChanged();
        }

        public bool Add(string dialogueId)
        {
            if (string.IsNullOrWhiteSpace(dialogueId))
            {
                throw new System.InvalidOperationException(
                    "Dialogue id cannot be null or empty.");
            }
            var selected = new List<string>(Selected());
            if (selected.Contains(dialogueId)) return false;
            selected.Add(dialogueId);
            // Set() enforces the group scope; an out-of-group id throws here.
            Set(selected.ToArray());
            return true;
        }

        public bool Remove(string dialogueId)
        {
            if (string.IsNullOrWhiteSpace(dialogueId)) return false;
            var selected = new List<string>(Selected());
            bool removed = selected.Remove(dialogueId);
            if (!removed) return false;
            Set(selected.ToArray());
            return true;
        }

        public void Clear()
        {
            Set(System.Array.Empty<string>());
        }

        /// <summary>
        /// Throws when <paramref name="dialogueId"/> is unknown or does not
        /// belong to <paramref name="groupId"/>. Direct group membership only —
        /// a dialogue is "in" a group iff its trigger node's group settings name
        /// that group (spec §11). Distinct messages per failure so a screenshot
        /// pinpoints the cause.
        /// </summary>
        private void ValidateInGroup(string dialogueId, string groupId)
        {
            if (!client.dialogues.TryGetValue(dialogueId, out DialogueModel dialogue))
            {
                throw new System.InvalidOperationException(
                    $"DialogueLookup '{member.id}': dialogue '{dialogueId}' not found.");
            }
            string? actual = dialogue.triggerNode?.dialogueGroupSettings?.dialogueGroupId;
            if (actual != groupId)
            {
                throw new System.InvalidOperationException(
                    $"DialogueLookup '{member.id}' is scoped to group '{groupId}', " +
                    $"but dialogue '{dialogueId}' is in group '{actual ?? "(none)"}'.");
            }
        }
    }
}
