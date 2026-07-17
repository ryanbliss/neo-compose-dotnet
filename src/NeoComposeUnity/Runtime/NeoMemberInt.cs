// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for an Int-typed member. The underlying
    /// <see cref="NumberMemberValue"/> stores its payload as
    /// <c>double?</c> (Int and Float share the wire numeric shape) —
    /// <see cref="NeoMemberIntWritable.Set"/> casts the int through
    /// the double slot.
    /// </summary>
    public class NeoMemberInt
        : NeoMember<IntMember, NumberMemberValue>
    {
        public NeoMemberInt(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberInt(NeoClient client, IntMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }
    }

    public class NeoMemberIntWritable : NeoMemberInt
    {
        public NeoMemberIntWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberIntWritable(NeoClient client, IntMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public void Set(int? newValue)
        {
            if (member.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(member)}.{nameof(member.required)} is true");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            double? doubleValue = newValue.HasValue ? newValue.Value : (double?)null;

            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = doubleValue;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable, "value");
                NotifyChanged();
                return;
            }

            NumberMemberValue newRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = doubleValue,
            };
            BindNewValue(newRow);
            NotifyChanged();
        }
    }
}
