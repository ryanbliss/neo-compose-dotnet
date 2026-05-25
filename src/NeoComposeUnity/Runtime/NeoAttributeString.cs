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

        public string? Text => ResolveText(value);

        public string? TextId
        {
            get
            {
                if (!attribute.localizable) return null;
                if (value?.neoLocalizationMode == NeoStringLocalizationMode.Literal) return null;
                return value?.value;
            }
        }

        protected string? ResolveText(StringAttributeValue? row)
        {
            if (row?.value == null) return null;
            if (!attribute.localizable) return row.value;
            if (row.neoLocalizationMode == NeoStringLocalizationMode.Literal) return row.value;
            return client.Localization.ResolveText(row.value);
        }
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
            SetLiteralOverride(newValue);
        }

        public void SetLiteralOverride(string? newValue)
        {
            if (attribute.required && newValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(newValue),
                    $"Cannot be null when {nameof(attribute)}.{nameof(attribute.required)} is true");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");

            if (TryGetWritableStringValue(out var existing))
            {
                existing.value = newValue;
                existing.neoLocalizationMode = NeoStringLocalizationMode.Literal;
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
                neoLocalizationMode = NeoStringLocalizationMode.Literal,
            };
            client.AddWritableValue(ownership, attribute.id, newRow);
            RefreshFromValueData();
            NotifyChanged();
        }

        private bool TryGetWritableStringValue(
            [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out StringAttributeValue? existing)
        {
            existing = null;
            if (ownership == NeoValueOwnership.Asset)
            {
                existing = value;
                return existing != null;
            }

            var currentValueId = valueId;
            return currentValueId != null &&
                client.TryGetWritableValue(ownership, currentValueId, out existing);
        }

        public void ClearOverride()
        {
            if (ownership == NeoValueOwnership.Asset) return;
            client.RemoveWritableOverride(ownership, attribute.id);
        }
    }
}
