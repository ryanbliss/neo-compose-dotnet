// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Decimal-typed attribute. Shares the underlying
    /// <see cref="StringAttributeValue"/> with String — disambiguate via
    /// <c>attribute.type</c> when needed (the same pattern as
    /// <see cref="NeoAttributeFloat"/> sharing
    /// <see cref="NumberAttributeValue"/> with Int). The stored string is a
    /// canonical decimal literal; convert via
    /// <see cref="NeoDecimalValues"/> (specs/decimal-attribute.md decision 5).
    /// </summary>
    public class NeoAttributeDecimal
        : NeoAttribute<DecimalAttribute, StringAttributeValue>
    {
        public NeoAttributeDecimal(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeDecimal(NeoClient client, DecimalAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }
    }

    /// <summary>
    /// Writeable variant of <see cref="NeoAttributeDecimal"/>.
    /// </summary>
    public class NeoAttributeDecimalWritable : NeoAttributeDecimal
    {
        public NeoAttributeDecimalWritable(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeDecimalWritable(NeoClient client, DecimalAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        /// <summary>
        /// Sets the underlying decimal, formatting to a canonical decimal
        /// string through <see cref="NeoDecimalValues.FormatOrNull"/>.
        /// Mirrors <see cref="NeoAttributeFloatWritable.Set"/>: clone-on-writes
        /// the existing value row when one is bound; otherwise mints a fresh
        /// row and binds it through the parent container.
        /// </summary>
        public void Set(decimal? newValue)
        {
            if (attribute.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(attribute)}.{nameof(attribute.required)} is true");
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

            StringAttributeValue newRow = new()
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
