// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a String-typed attribute. Read-only — use
    /// <see cref="NeoAttributeStringWritable"/> to mutate.
    /// </summary>
    public class NeoAttributeString
        : NeoAttribute<StringAttribute, StringAttributeValue>
    {
        public NeoAttributeString(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeString(NeoClient client, StringAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }
    }

    /// <summary>
    /// Writeable variant of <see cref="NeoAttributeString"/>.
    /// </summary>
    public class NeoAttributeStringWritable : NeoAttributeString
    {
        public NeoAttributeStringWritable(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeStringWritable(NeoClient client, StringAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        /// <summary>
        /// Sets the underlying string. Updates an existing value row in
        /// place when one exists; otherwise creates a fresh row and
        /// registers it under the save's <c>attributeValueOverrides</c>.
        /// </summary>
        public void Set(string? newValue)
        {
            if (attribute.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(attribute)}.{nameof(attribute.required)} is true");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");

            if (value is StringAttributeValue existing)
            {
                existing.value = newValue;
                existing.updatedAt = nowIso;
                client.SetWritableValue(ownership, existing);
                NotifyChanged();
                return;
            }

            string newValueId = System.Guid.NewGuid().ToString();
            StringAttributeValue newRow = new()
            {
                id = newValueId,
                createdAt = nowIso,
                updatedAt = nowIso,
                value = newValue,
            };
            client.AddWritableValue(ownership, attribute.id, newRow);
            RefreshFromValueData();
            NotifyChanged();
        }
    }
}
