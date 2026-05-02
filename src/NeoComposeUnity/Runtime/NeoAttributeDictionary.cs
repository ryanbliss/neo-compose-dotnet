// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Dictionary-typed attribute. Children are keyed by
    /// user-set strings; each child is a <see cref="NeoAttribute"/>
    /// for the entry attribute (per
    /// <see cref="DictionaryAttribute.entryAttributeId"/>) bound to
    /// the value referenced from the dict.
    /// </summary>
    public class NeoAttributeDictionary
        : NeoAttribute<DictionaryAttribute, ObjectAttributeValue>,
          IEnumerable<KeyValuePair<string, NeoAttribute>>
    {
        protected Attribute entryAttribute;
        protected Dictionary<string, NeoAttribute> childAttributes = new();

        public NeoAttributeDictionary(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId)
        {
            entryAttribute = ResolveEntryAttribute();
            ReinitializeChildren();
        }

        public NeoAttributeDictionary(NeoClient client, DictionaryAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId)
        {
            entryAttribute = ResolveEntryAttribute();
            ReinitializeChildren();
        }

        protected virtual NeoAttribute CreateChild(
            NeoClient client,
            Attribute childAttribute,
            string? overrideValueId)
        {
            return Create(client, childAttribute, overrideValueId);
        }

        public NeoAttribute this[string key] => childAttributes[key];

        public bool TryGet<TNeoAttribute>(string key, [NotNullWhen(true)] out TNeoAttribute? outAttribute)
            where TNeoAttribute : NeoAttribute
        {
            if (childAttributes.TryGetValue(key, out NeoAttribute? check) && check is TNeoAttribute match)
            {
                outAttribute = match;
                return true;
            }
            outAttribute = null;
            return false;
        }

        public IEnumerator<KeyValuePair<string, NeoAttribute>> GetEnumerator() =>
            childAttributes.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        protected override void Initialize(ObjectAttributeValue value)
        {
            base.Initialize(value);
            // entryAttribute isn't set yet on the first base-ctor pass;
            // ReinitializeChildren runs after the derived ctor wires it.
        }

        protected override void OnValueIdChainChanged()
        {
            base.OnValueIdChainChanged();
            // The new bound value may have a different keyset — re-walk
            // children so disposed-orphans get released and new keys
            // get nodes.
            ReinitializeChildren();
        }

        public override void Dispose()
        {
            if (isDisposed) return;
            foreach (var child in childAttributes.Values) child.Dispose();
            childAttributes.Clear();
            base.Dispose();
        }

        protected void ReinitializeChildren()
        {
            // Dispose any existing children before clearing — they
            // may have been bound to value-ids that aren't in the
            // new value graph, and leaving them registered would
            // leak them in client.nodes.
            foreach (var child in childAttributes.Values) child.Dispose();
            childAttributes.Clear();
            if (value?.value is null) return;
            foreach (var kvp in value.value)
            {
                childAttributes[kvp.Key] = CreateChild(client, entryAttribute, kvp.Value);
            }
        }

        private Attribute ResolveEntryAttribute()
        {
            if (!client.TryGetAttribute(attribute.entryAttributeId, out Attribute? match))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(attribute.entryAttributeId),
                    $"No attribute for {nameof(attribute)}.{nameof(attribute.entryAttributeId)} {attribute.entryAttributeId}");
            }
            return match;
        }
    }

    public class NeoAttributeDictionarySaved : NeoAttributeDictionary
    {
        public NeoAttributeDictionarySaved(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeDictionarySaved(NeoClient client, DictionaryAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }

        protected override NeoAttribute CreateChild(
            NeoClient client,
            Attribute childAttribute,
            string? overrideValueId)
        {
            return CreateSaved(client, childAttribute, overrideValueId);
        }

        /// <summary>
        /// Sets the dictionary entry under <paramref name="key"/>.
        /// Updates an existing entry in place; otherwise creates a
        /// fresh entry value, links it under the parent's value-map,
        /// and re-saves the parent. If the parent itself has no
        /// stored value yet, materialises one first.
        /// </summary>
        public void Set<TEntryValue>(string key, TEntryValue? setValue)
        {
            if (entryAttribute.required && setValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(setValue),
                    $"Cannot be null when entry attribute is required");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");

            if (value?.value is not null
                && value.value.TryGetValue(key, out string existingValueId)
                && client.TryGetValue(existingValueId, out AttributeValue<TEntryValue?>? existing))
            {
                existing.value = setValue;
                existing.updatedAt = nowIso;
                client.SetSaveValue(existing);
                return;
            }

            string newValueId = System.Guid.NewGuid().ToString();
            AttributeValue newValueRow = AttributeValueFactory.Create(
                entryAttribute, setValue, newValueId, nowIso, nowIso);
            client.SetSaveValue(newValueRow);

            if (value is null)
            {
                ObjectAttributeValue parentRow = new()
                {
                    id = System.Guid.NewGuid().ToString(),
                    createdAt = nowIso,
                    updatedAt = nowIso,
                    value = new Dictionary<string, string>(),
                };
                client.AddSaveValue(attribute.id, parentRow);
                RefreshFromValueData();
            }

            value!.value ??= new Dictionary<string, string>();
            value.value[key] = newValueId;
            value.updatedAt = nowIso;
            client.SetSaveValue(value);

            childAttributes[key] = CreateChild(client, entryAttribute, newValueId);
        }

        public void Remove(string key)
        {
            if (value?.value is null) return;
            if (!value.value.TryGetValue(key, out string removedValueId)) return;
            string nowIso = System.DateTime.UtcNow.ToString("o");

            // Mutate the parent's dict + persist.
            value.value.Remove(key);
            value.updatedAt = nowIso;
            client.SetSaveValue(value);

            // Dispose the child node (recursive — its own Dispose
            // disposes any grandchildren) and drop our reference.
            if (childAttributes.TryGetValue(key, out NeoAttribute? child))
            {
                child.Dispose();
                childAttributes.Remove(key);
            }

            // GC the orphaned value graph from the save file. The
            // removed valueId may itself reference more child values
            // (e.g., the entry was a Custom record); RemoveSaveValueAndDescendants
            // walks them.
            client.RemoveSaveValueAndDescendants(removedValueId);
        }
    }
}
