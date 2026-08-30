// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Bool-typed member. Read-only — use
    /// <see cref="NeoMemberBoolWritable"/> to mutate.
    /// </summary>
    public class NeoMemberBool
        : NeoMember<BoolMember, BoolMemberValue>
    {
        public NeoMemberBool(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberBool(NeoClient client, BoolMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }
    }

    public class NeoMemberBoolWritable : NeoMemberBool
    {
        public NeoMemberBoolWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberBoolWritable(NeoClient client, BoolMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public void Set(bool? newValue)
        {
            if (member.Requirement == NeoMemberRequirementKind.Required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(member)} requirement is Required");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");

            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = newValue;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                // No NotifyChanged() here — the write above already raised it
                // through this node's own OnValueIdChainChanged. See that
                // method's remarks.
                return;
            }

            BoolMemberValue newRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = newValue,
            };
            BindNewValue(newRow);
            NotifyChanged();
        }
    }
}
