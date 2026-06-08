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
        public NeoAttributeLookup(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeLookup(NeoClient client, LookupAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

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

            if (!client.TryGetAttribute(attribute.collectionAttributeId, out Attribute? targetAttribute))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(attribute.collectionAttributeId),
                    $"No attribute for collection target {attribute.collectionAttributeId}");
            }

            string? targetValueId = ResolveTargetValueId(targetAttribute);
            if (targetValueId is null)
            {
                throw new System.InvalidOperationException(
                    $"Lookup target {attribute.collectionAttributeId} has no bound value");
            }
            if (!client.TryGetValue(targetValueId, out AttributeValue? targetValue))
            {
                throw new System.InvalidOperationException(
                    $"Lookup target value {targetValueId} not found");
            }
            client.TryGetValueOwnership(targetValueId, out NeoValueOwnership targetOwnership);

            // The entry attribute defines the type of each selected
            // entry. List/Lookup → entryAttributeId; Dictionary →
            // entryAttributeId; Custom → schema-keyed (lookup into
            // Custom collections isn't currently supported).
            Attribute entryAttr = ResolveEntryAttribute(targetAttribute);

            foreach (var id in selectedIds)
            {
                resolved.Add(targetOwnership == NeoValueOwnership.Save || targetOwnership == NeoValueOwnership.Session
                    ? CreateWritable(client, entryAttr, id, targetOwnership)
                    : Create(client, entryAttr, id));
            }
            return resolved;
        }

        internal bool IsSelectableId(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId)) return false;
            AttributeValue targetValue = ResolveTargetValue(out _);
            return targetValue switch
            {
                ArrayAttributeValue array when array.value is not null =>
                    System.Array.IndexOf(array.value, valueId) >= 0,
                ObjectAttributeValue obj when obj.value is not null =>
                    obj.value.ContainsValue(valueId),
                _ => false,
            };
        }

        internal Attribute ResolveEntryAttributeForLookup() =>
            ResolveEntryAttribute(ResolveTargetAttribute());

        private Attribute ResolveTargetAttribute()
        {
            if (!client.TryGetAttribute(attribute.collectionAttributeId, out Attribute? targetAttribute))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(attribute.collectionAttributeId),
                    $"No attribute for collection target {attribute.collectionAttributeId}");
            }
            return targetAttribute;
        }

        private AttributeValue ResolveTargetValue(out NeoValueOwnership targetOwnership)
        {
            Attribute targetAttribute = ResolveTargetAttribute();
            string? targetValueId = ResolveTargetValueId(targetAttribute);
            if (targetValueId is null)
            {
                throw new System.InvalidOperationException(
                    $"Lookup target {attribute.collectionAttributeId} has no bound value");
            }
            if (!client.TryGetValue(targetValueId, out AttributeValue? targetValue))
            {
                throw new System.InvalidOperationException(
                    $"Lookup target value {targetValueId} not found");
            }
            client.TryGetValueOwnership(targetValueId, out targetOwnership);
            return targetValue;
        }

        private string? ResolveTargetValueId(Attribute targetAttribute)
        {
            return client.TryResolveLookupCollectionValueId(
                targetAttribute.id,
                attribute.collectionValueId,
                out string? targetValueId)
                    ? targetValueId
                    : null;
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
            if (!client.TryGetAttribute(entryAttributeId, out Attribute? entryAttr))
            {
                throw new System.InvalidOperationException(
                    $"Lookup entry attribute {entryAttributeId} not found");
            }
            return entryAttr;
        }
    }

    public class NeoAttributeLookupWritable : NeoAttributeLookup
    {
        public NeoAttributeLookupWritable(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeLookupWritable(NeoClient client, LookupAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

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

            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value = normalized;
                writable.updatedAt = nowIso;
                client.SetWritableValue(ownership, writable);
                NotifyChanged();
                return;
            }

            ArrayAttributeValue newRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = normalized,
            };
            BindNewValue(newRow);
            NotifyChanged();
        }

        public bool Add(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId))
            {
                throw new System.InvalidOperationException(
                    "Lookup selection id cannot be null or empty.");
            }
            if (!IsSelectableId(valueId))
            {
                throw new System.InvalidOperationException(
                    $"Lookup selection id '{valueId}' is not present in the configured lookup collection.");
            }
            var selected = new List<string>(Selected());
            if (selected.Contains(valueId)) return false;
            selected.Add(valueId);
            Set(selected.ToArray());
            return true;
        }

        public bool Remove(string valueId)
        {
            if (string.IsNullOrWhiteSpace(valueId)) return false;
            var selected = new List<string>(Selected());
            bool removed = selected.Remove(valueId);
            if (!removed) return false;
            Set(selected.ToArray());
            return true;
        }

        public void Clear()
        {
            Set(System.Array.Empty<string>());
        }
    }
}
