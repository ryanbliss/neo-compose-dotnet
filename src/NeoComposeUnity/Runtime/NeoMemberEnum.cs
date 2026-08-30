// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using JsonEnum = NeoCompose.Runtime.Json.Enum;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for an Enum-typed member. The stored value is a
    /// <see cref="ArrayMemberValue"/> whose <c>value</c> is the
    /// list of selected option ids. Options live on the linked
    /// <see cref="JsonEnum"/> (resolved via
    /// <see cref="EnumMember.enumId"/>) — they're static metadata,
    /// not children.
    /// </summary>
    public class NeoMemberEnum
        : NeoMember<EnumMember, ArrayMemberValue>
    {
        protected JsonEnum enumDef;

        public NeoMemberEnum(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership)
        {
            enumDef = ResolveEnum();
        }

        public NeoMemberEnum(NeoClient client, EnumMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership)
        {
            enumDef = ResolveEnum();
        }

        /// <summary>
        /// Returns the currently-selected option ids. Empty array when
        /// nothing is set; never null.
        /// </summary>
        public string[] Selected() => value?.value ?? System.Array.Empty<string>();

        /// <summary>
        /// Returns the linked <see cref="EnumOption"/> for an id, or
        /// throws if the id isn't a known option of this enum.
        /// </summary>
        public EnumOption GetOption(string optionId)
        {
            if (!enumDef.options.TryGetValue(optionId, out EnumOption match))
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Enum {enumDef.id} has no option '{optionId}'");
            }
            return match;
        }

        public string GetOptionText(string optionId)
        {
            return client.Localization.ResolveText(GetOption(optionId).text);
        }

        private JsonEnum ResolveEnum()
        {
            if (!client.TryGetEnum(member.enumId, out JsonEnum? match))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(member.enumId),
                    $"No enum for {nameof(member)}.{nameof(member.enumId)} {member.enumId}");
            }
            return match;
        }
    }

    public class NeoMemberEnumWritable : NeoMemberEnum
    {
        public NeoMemberEnumWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberEnumWritable(NeoClient client, EnumMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>
        /// Overwrites the selected option ids. Each id is validated
        /// against the linked enum's options. In single-select mode, only the
        /// first id in <paramref name="optionIds"/> is honored.
        /// </summary>
        public void Set(string[]? optionIds)
        {
            if (member.Requirement == NeoMemberRequirementKind.Required && (optionIds is null || optionIds.Length == 0))
            {
                throw new System.ArgumentNullException(
                    nameof(optionIds),
                    $"Cannot be null/empty when {nameof(member)} requirement is Required");
            }

            string[]? normalized = optionIds;
            if (normalized is not null)
            {
                if (member.Selection != NeoMemberSelectionKind.Multi && normalized.Length > 1)
                {
                    normalized = new[] { normalized[0] };
                }
                foreach (var id in normalized)
                {
                    if (!enumDef.options.ContainsKey(id))
                    {
                        throw new System.ArgumentException(
                            $"Enum {enumDef.id} has no option '{id}'", nameof(optionIds));
                    }
                }
            }

            string nowIso = System.DateTime.UtcNow.ToString("o");

            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = normalized;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                // No NotifyChanged() here — the write above already raised it
                // through this node's own OnValueIdChainChanged. See that
                // method's remarks.
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
    }
}
