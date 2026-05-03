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
    /// <see cref="NeoAttributeIntSaved.Set"/> casts the int through
    /// the double slot.
    /// </summary>
    public class NeoAttributeInt
        : NeoAttribute<IntAttribute, NumberAttributeValue>
    {
        public NeoAttributeInt(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeInt(NeoClient client, IntAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }
    }

    public class NeoAttributeIntSaved : NeoAttributeInt
    {
        public NeoAttributeIntSaved(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeIntSaved(NeoClient client, IntAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }

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

            if (value is NumberAttributeValue existing)
            {
                existing.value = doubleValue;
                existing.updatedAt = nowIso;
                client.SetSaveValue(existing);
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
            client.AddSaveValue(attribute.id, newRow);
            RefreshFromValueData();
            NotifyChanged();
        }
    }
}
