// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections;
using System.Collections.Generic;
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

        public bool TryGet<TNeoAttribute>(string key, out TNeoAttribute outAttribute)
            where TNeoAttribute : NeoAttribute
        {
            if (childAttributes.TryGetValue(key, out NeoAttribute check) && check is TNeoAttribute match)
            {
                outAttribute = match;
                return true;
            }
            outAttribute = null!;
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

        protected void ReinitializeChildren()
        {
            childAttributes.Clear();
            if (value?.value is null) return;
            foreach (var kvp in value.value)
            {
                childAttributes[kvp.Key] = CreateChild(client, entryAttribute, kvp.Value);
            }
        }

        private Attribute ResolveEntryAttribute()
        {
            if (!client.TryGetAttribute(attribute.entryAttributeId, out Attribute match))
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
                && client.TryGetValue(existingValueId, out AttributeValue<TEntryValue?> existing))
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
            if (value?.value is null || !value.value.ContainsKey(key)) return;
            string nowIso = System.DateTime.UtcNow.ToString("o");
            value.value.Remove(key);
            value.updatedAt = nowIso;
            client.SetSaveValue(value);
            childAttributes.Remove(key);
        }
    }
}
