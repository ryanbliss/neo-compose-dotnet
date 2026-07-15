// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Decimal-typed member. Shares the underlying
    /// <see cref="StringMemberValue"/> with String — disambiguate via
    /// <c>member.kind</c> when needed (the same pattern as
    /// <see cref="NeoMemberFloat"/> sharing
    /// <see cref="NumberMemberValue"/> with Int). The stored string is a
    /// canonical decimal literal; convert via
    /// <see cref="NeoDecimalValues"/> (specs/decimal-member.md decision 5).
    /// </summary>
    public class NeoMemberDecimal
        : NeoMember<DecimalMember, StringMemberValue>
    {
        public NeoMemberDecimal(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberDecimal(NeoClient client, DecimalMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }
    }

    /// <summary>
    /// Writeable variant of <see cref="NeoMemberDecimal"/>.
    /// </summary>
    public class NeoMemberDecimalWritable : NeoMemberDecimal
    {
        public NeoMemberDecimalWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberDecimalWritable(NeoClient client, DecimalMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        /// <summary>
        /// Sets the underlying decimal, formatting to a canonical decimal
        /// string through <see cref="NeoDecimalValues.FormatOrNull"/>.
        /// Mirrors <see cref="NeoMemberFloatWritable.Set"/>: clone-on-writes
        /// the existing value row when one is bound; otherwise mints a fresh
        /// row and binds it through the parent container.
        /// </summary>
        public void Set(decimal? newValue)
        {
            if (member.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(member)}.{nameof(member.required)} is true");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            string? canonical = NeoDecimalValues.FormatOrNull(newValue);

            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = canonical;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable);
                NotifyChanged();
                return;
            }

            StringMemberValue newRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = canonical,
            };
            BindNewValue(newRow);
            NotifyChanged();
        }
    }
}
