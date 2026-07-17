// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Float-typed member. Shares the underlying
    /// <see cref="NumberMemberValue"/> with Int — disambiguate via
    /// <c>member.kind</c> when needed.
    /// </summary>
    public class NeoMemberFloat
        : NeoMember<FloatMember, NumberMemberValue>
    {
        public NeoMemberFloat(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberFloat(NeoClient client, FloatMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }
    }

    public class NeoMemberFloatWritable : NeoMemberFloat
    {
        public NeoMemberFloatWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberFloatWritable(NeoClient client, FloatMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        public void Set(float? newValue)
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
