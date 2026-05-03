// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Bool-typed attribute. Read-only — use
    /// <see cref="NeoAttributeBoolSaved"/> to mutate.
    /// </summary>
    public class NeoAttributeBool
        : NeoAttribute<BoolAttribute, BoolAttributeValue>
    {
        public NeoAttributeBool(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeBool(NeoClient client, BoolAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }
    }

    public class NeoAttributeBoolSaved : NeoAttributeBool
    {
        public NeoAttributeBoolSaved(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeBoolSaved(NeoClient client, BoolAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }

        public void Set(bool? newValue)
        {
            if (attribute.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(attribute)}.{nameof(attribute.required)} is true");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");

            if (value is BoolAttributeValue existing)
            {
                existing.value = newValue;
                existing.updatedAt = nowIso;
                client.SetSaveValue(existing);
                NotifyChanged();
                return;
            }

            string newValueId = System.Guid.NewGuid().ToString();
            BoolAttributeValue newRow = new()
            {
                id = newValueId,
                createdAt = nowIso,
                updatedAt = nowIso,
                value = newValue,
            };
            client.AddSaveValue(attribute.id, newRow);
            RefreshFromValueData();
            NotifyChanged();
        }
    }
}
