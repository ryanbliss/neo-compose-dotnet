// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using JsonEnum = NeoCompose.Runtime.Json.Enum;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for an Enum-typed attribute. The stored value is a
    /// <see cref="ArrayAttributeValue"/> whose <c>value</c> is the
    /// list of selected option ids. Options live on the linked
    /// <see cref="JsonEnum"/> (resolved via
    /// <see cref="EnumAttribute.enumId"/>) — they're static metadata,
    /// not children.
    /// </summary>
    public class NeoAttributeEnum
        : NeoAttribute<EnumAttribute, ArrayAttributeValue>
    {
        protected JsonEnum enumDef;

        public NeoAttributeEnum(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId)
        {
            enumDef = ResolveEnum();
        }

        public NeoAttributeEnum(NeoClient client, EnumAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId)
        {
            enumDef = ResolveEnum();
        }

        /// <summary>
        /// Returns the currently-selected option ids. Empty array when
        /// nothing is set; never null.
        /// </summary>
        public string[] Selected() => value?.value ?? System.Array.Empty<string>();

        /// <summary>
        /// Returns the linked <see cref="EnumOption"/> for an id, or
        /// throws if the id isn't a known option of this enum.
        /// </summary>
        public EnumOption GetOption(string optionId)
        {
            if (!enumDef.options.TryGetValue(optionId, out EnumOption match))
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Enum {enumDef.id} has no option '{optionId}'");
            }
            return match;
        }

        private JsonEnum ResolveEnum()
        {
            if (!client.TryGetEnum(attribute.enumId, out JsonEnum? match))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(attribute.enumId),
                    $"No enum for {nameof(attribute)}.{nameof(attribute.enumId)} {attribute.enumId}");
            }
            return match;
        }
    }

    public class NeoAttributeEnumSaved : NeoAttributeEnum
    {
        public NeoAttributeEnumSaved(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeEnumSaved(NeoClient client, EnumAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }

        /// <summary>
        /// Overwrites the selected option ids. Each id is validated
        /// against the linked enum's options. When
        /// <c>multiselect</c> is false, only the first id in
        /// <paramref name="optionIds"/> is honored.
        /// </summary>
        public void Set(string[]? optionIds)
        {
            if (attribute.required && (optionIds is null || optionIds.Length == 0))
            {
                throw new System.ArgumentNullException(
                    nameof(optionIds),
                    $"Cannot be null/empty when {nameof(attribute)}.{nameof(attribute.required)} is true");
            }

            string[]? normalized = optionIds;
            if (normalized is not null)
            {
                if (!attribute.multiselect && normalized.Length > 1)
                {
                    normalized = new[] { normalized[0] };
                }
                foreach (var id in normalized)
                {
                    if (!enumDef.options.ContainsKey(id))
                    {
                        throw new System.ArgumentException(
                            $"Enum {enumDef.id} has no option '{id}'", nameof(optionIds));
                    }
                }
            }

            string nowIso = System.DateTime.UtcNow.ToString("o");

            if (value is ArrayAttributeValue existing)
            {
                existing.value = normalized;
                existing.updatedAt = nowIso;
                client.SetSaveValue(existing);
                NotifyChanged();
                return;
            }

            string newValueId = System.Guid.NewGuid().ToString();
            ArrayAttributeValue newRow = new()
            {
                id = newValueId,
                createdAt = nowIso,
                updatedAt = nowIso,
                value = normalized,
            };
            client.AddSaveValue(attribute.id, newRow);
            RefreshFromValueData();
            NotifyChanged();
        }
    }
}
