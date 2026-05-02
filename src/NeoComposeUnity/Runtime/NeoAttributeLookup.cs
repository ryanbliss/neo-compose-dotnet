// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Lookup-typed attribute. Stores the selected ids
    /// (in the target collection) as a string-array value. The target
    /// collection is the attribute named by
    /// <see cref="LookupAttribute.collectionAttributeId"/>; the target
    /// value is either <see cref="LookupAttribute.collectionValueId"/>
    /// (when set) or the target attribute's own <c>valueId</c>.
    /// </summary>
    public class NeoAttributeLookup
        : NeoAttribute<LookupAttribute, ArrayAttributeValue>
    {
        public NeoAttributeLookup(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeLookup(NeoClient client, LookupAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }

        /// <summary>Selected ids in the target collection. Empty when nothing is set.</summary>
        public string[] Selected() => value?.value ?? System.Array.Empty<string>();

        /// <summary>
        /// Resolves the selected ids against the looked-up collection
        /// and returns the matching <see cref="NeoAttribute"/>s.
        /// Walks: <c>collectionAttributeId</c> → target attribute →
        /// target value (using <c>collectionValueId</c> if set, else
        /// the target attribute's <c>valueId</c>) → entries indexed by
        /// each selected id.
        ///
        /// <para>Resolved instances are constructed ad-hoc per call —
        /// this layer doesn't pin a global cache. Callers that hit the
        /// same Lookup repeatedly should cache the result.</para>
        /// </summary>
        public IList<NeoAttribute> GetSelected()
        {
            List<NeoAttribute> resolved = new();
            string[] selectedIds = Selected();
            if (selectedIds.Length == 0) return resolved;

            if (!client.TryGetAttribute(attribute.collectionAttributeId, out Attribute targetAttribute))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(attribute.collectionAttributeId),
                    $"No attribute for collection target {attribute.collectionAttributeId}");
            }

            // The target's value-id is either the explicit
            // collectionValueId override or the attribute's own valueId.
            string? targetValueId = attribute.collectionValueId ?? targetAttribute.valueId;
            if (targetValueId is null)
            {
                throw new System.InvalidOperationException(
                    $"Lookup target {attribute.collectionAttributeId} has no bound value");
            }
            if (!client.TryGetValue(targetValueId, out AttributeValue targetValue))
            {
                throw new System.InvalidOperationException(
                    $"Lookup target value {targetValueId} not found");
            }

            // The entry attribute defines the type of each selected
            // entry. List/Lookup → entryAttributeId; Dictionary →
            // entryAttributeId; Custom → schema-keyed (lookup into
            // Custom collections isn't currently supported).
            Attribute entryAttr = ResolveEntryAttribute(targetAttribute);

            foreach (var id in selectedIds)
            {
                resolved.Add(Create(client, entryAttr, id));
            }
            return resolved;
        }

        private Attribute ResolveEntryAttribute(Attribute targetAttribute)
        {
            string entryAttributeId = targetAttribute switch
            {
                ListAttribute l => l.entryAttributeId,
                DictionaryAttribute d => d.entryAttributeId,
                _ => throw new System.NotSupportedException(
                    $"Lookup target must be List or Dictionary; got {targetAttribute.GetType().Name}"),
            };
            if (!client.TryGetAttribute(entryAttributeId, out Attribute entryAttr))
            {
                throw new System.InvalidOperationException(
                    $"Lookup entry attribute {entryAttributeId} not found");
            }
            return entryAttr;
        }
    }

    public class NeoAttributeLookupSaved : NeoAttributeLookup
    {
        public NeoAttributeLookupSaved(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeLookupSaved(NeoClient client, LookupAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }

        /// <summary>
        /// Overwrites the selected ids. When
        /// <see cref="LookupAttribute.multiselect"/> is false, only
        /// the first id is honored.
        /// </summary>
        public void Set(string[]? selectedIds)
        {
            if (attribute.required && (selectedIds is null || selectedIds.Length == 0))
            {
                throw new System.ArgumentNullException(
                    nameof(selectedIds),
                    $"Cannot be null/empty when {nameof(attribute)}.{nameof(attribute.required)} is true");
            }

            string[]? normalized = selectedIds;
            if (normalized is not null && !attribute.multiselect && normalized.Length > 1)
            {
                normalized = new[] { normalized[0] };
            }

            string nowIso = System.DateTime.UtcNow.ToString("o");

            if (value is ArrayAttributeValue existing)
            {
                existing.value = normalized;
                existing.updatedAt = nowIso;
                client.SetSaveValue(existing);
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
        }
    }
}
