// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for an Int-typed attribute. The underlying
    /// <see cref="NumberAttributeValue"/> stores its payload as
    /// <c>double?</c> (Int and Float share the wire numeric shape) —
    /// <see cref="NeoAttributeIntWritable.Set"/> casts the int through
    /// the double slot.
    /// </summary>
    public class NeoAttributeInt
        : NeoAttribute<IntAttribute, NumberAttributeValue>
    {
        public NeoAttributeInt(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeInt(NeoClient client, IntAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }
    }

    public class NeoAttributeIntWritable : NeoAttributeInt
    {
        public NeoAttributeIntWritable(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeIntWritable(NeoClient client, IntAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        public void Set(int? newValue)
        {
            if (attribute.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(attribute)}.{nameof(attribute.required)} is true");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            double? doubleValue = newValue.HasValue ? newValue.Value : (double?)null;

            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = doubleValue;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable);
                NotifyChanged();
                return;
            }

            NumberAttributeValue newRow = new()
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
