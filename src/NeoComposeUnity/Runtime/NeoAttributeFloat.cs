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
        public NeoAttributeFloat(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeFloat(NeoClient client, FloatAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }
    }

    public class NeoAttributeFloatSaved : NeoAttributeFloat
    {
        public NeoAttributeFloatSaved(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeFloatSaved(NeoClient client, FloatAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }

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
                client.SetSaveValue(existing);
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
            client.AddSaveValue(attribute.id, newRow);
            RefreshFromValueData();
        }
    }
}
