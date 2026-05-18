// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Float-typed attribute. Shares the underlying
    /// <see cref="NumberAttributeValue"/> with Int — disambiguate via
    /// <c>attribute.type</c> when needed.
    /// </summary>
    public class NeoAttributeFloat
        : NeoAttribute<FloatAttribute, NumberAttributeValue>
    {
        public NeoAttributeFloat(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeFloat(NeoClient client, FloatAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }
    }

    public class NeoAttributeFloatWritable : NeoAttributeFloat
    {
        public NeoAttributeFloatWritable(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeFloatWritable(NeoClient client, FloatAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        public void Set(float? newValue)
        {
            if (attribute.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(attribute)}.{nameof(attribute.required)} is true");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            double? doubleValue = newValue.HasValue ? newValue.Value : (double?)null;

            if (value is NumberAttributeValue existing)
            {
                existing.value = doubleValue;
                existing.updatedAt = nowIso;
                client.SetWritableValue(ownership, existing);
                NotifyChanged();
                return;
            }

            string newValueId = System.Guid.NewGuid().ToString();
            NumberAttributeValue newRow = new()
            {
                id = newValueId,
                createdAt = nowIso,
                updatedAt = nowIso,
                value = doubleValue,
            };
            client.AddWritableValue(ownership, attribute.id, newRow);
            RefreshFromValueData();
            NotifyChanged();
        }
    }
}
