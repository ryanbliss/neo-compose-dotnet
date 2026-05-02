// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a Custom-typed attribute. Children are keyed by
    /// the schema field name (from <see cref="CustomType.schema"/>);
    /// each value is a <see cref="NeoAttribute"/> for that schema's
    /// underlying attribute id, bound to the value referenced from the
    /// parent record's value-map entry.
    /// </summary>
    public class NeoAttributeCustom
        : NeoAttribute<CustomAttribute, ObjectAttributeValue>,
          IEnumerable<KeyValuePair<string, NeoAttribute>>
    {
        protected CustomType type;
        /// <summary>
        /// Inheritance chain (child-first) for the row's effective
        /// type. Empty when the chain is cyclic — see
        /// <see cref="ResolveTypeContext"/>.
        /// </summary>
        public IList<CustomType> inheritanceChain { get; private set; } = new List<CustomType>();
        /// <summary>
        /// Schema entries merged across <see cref="inheritanceChain"/>
        /// (base-first; child overrides win at the same key). Replaces
        /// direct <c>type.schema</c> access so descendants see fields
        /// inherited from ancestor Custom types.
        /// </summary>
        public IList<MergedSchemaEntry> mergedSchema { get; private set; } = new List<MergedSchemaEntry>();
        protected Dictionary<string, NeoAttribute> childAttributes = new();

        public NeoAttributeCustom(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId)
        {
            type = ResolveCustomType();
            ResolveTypeContext();
            // Schema-driven init runs after `type` + merged schema are
            // wired so child attribute lookups via the merged schema
            // resolve correctly; the base ctor's value-driven
            // Initialize ran without walking children because the
            // schema was empty then. We re-walk now.
            ReinitializeChildren();
        }

        public NeoAttributeCustom(NeoClient client, CustomAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId)
        {
            type = ResolveCustomType();
            ResolveTypeContext();
            ReinitializeChildren();
        }

        /// <summary>
        /// Hook for child instantiation — returns the read-only kind.
        /// <see cref="NeoAttributeCustomSaved"/> overrides this to
        /// return Saved kinds so descendants of a writeable Custom are
        /// also writeable.
        /// </summary>
        protected virtual NeoAttribute CreateChild(
            NeoClient client,
            Attribute childAttribute,
            string? overrideValueId)
        {
            return Create(client, childAttribute, overrideValueId);
        }

        public NeoAttribute this[string key]
        {
            get => Get<NeoAttribute>(key);
        }

        public TNeoAttribute Get<TNeoAttribute>(string key)
            where TNeoAttribute : NeoAttribute
        {
            if (!TryGet(key, out TNeoAttribute attr))
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"No child {nameof(NeoAttribute)} for {nameof(key)} '{key}' on {nameof(NeoAttributeCustom)} {attribute.id}");
            }
            return attr;
        }

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

        protected TValue? GetValueData<TValue>(string key) where TValue : AttributeValue
        {
            if (!TryGetValueData(key, out TValue value))
            {
                if (attribute.required)
                {
                    throw new System.NullReferenceException(
                        $"{attribute.required} is true, but value not found");
                }
                return null;
            }
            return value;
        }

        protected bool TryGetValueData<TValue>(string key, out TValue outValue)
            where TValue : AttributeValue
        {
            if (value?.value is not null && value.value.TryGetValue(key, out string valueIdForKey))
            {
                return client.TryGetValue(valueIdForKey, out outValue);
            }
            outValue = null!;
            return false;
        }

        protected TAttribute GetAttribute<TAttribute>(string key)
            where TAttribute : Attribute
        {
            if (!TryGetAttribute(key, out TAttribute childAttribute))
            {
                throw new System.NullReferenceException(
                    $"attribute for {nameof(key)} '{key}' not found");
            }
            return childAttribute;
        }

        protected bool TryGetAttribute<TAttribute>(string key, out TAttribute outAttribute)
            where TAttribute : Attribute
        {
            // Walks the merged schema rather than `type.schema` directly
            // so a descendant Custom row sees keys inherited from
            // ancestor types in its `extendsTypeId` chain.
            string? attributeIdForKey = LookupMergedAttributeId(key);
            if (attributeIdForKey is not null)
            {
                return client.TryGetAttribute(attributeIdForKey, out outAttribute);
            }
            outAttribute = null!;
            return false;
        }

        /// <summary>
        /// Returns the resolved attribute id for <paramref name="key"/>
        /// according to the merged schema (child overrides win), or null
        /// when the key isn't in any ancestor's schema.
        /// </summary>
        protected string? LookupMergedAttributeId(string key)
        {
            foreach (var entry in mergedSchema)
            {
                if (entry.schemaKey == key) return entry.attributeId;
            }
            return null;
        }

        protected override void Initialize(ObjectAttributeValue value)
        {
            base.Initialize(value);
            // Children are walked from ReinitializeChildren — `type`
            // isn't set yet on the first base-ctor pass.
        }

        /// <summary>
        /// Walks <c>value.value</c> and rebuilds the
        /// <see cref="childAttributes"/> dict from scratch using the
        /// current <see cref="type"/>'s schema. Called after the
        /// schema is wired (post-base-ctor), and again whenever a
        /// Saved mutation invalidates the cached children.
        /// </summary>
        protected void ReinitializeChildren()
        {
            childAttributes.Clear();
            if (value?.value is null) return;
            foreach (var kvp in value.value)
            {
                if (!TryGetAttribute(kvp.Key, out Attribute childAttribute)) continue;
                childAttributes[kvp.Key] = CreateChild(client, childAttribute, kvp.Value);
            }
        }

        public IEnumerator<KeyValuePair<string, NeoAttribute>> GetEnumerator()
        {
            return childAttributes.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        private CustomType ResolveCustomType()
        {
            if (!client.TryGetType(attribute.customTypeId, out CustomType match))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(attribute.customTypeId),
                    $"No custom type for {nameof(attribute)}.{nameof(attribute.customTypeId)} {attribute.customTypeId}");
            }
            return match;
        }

        /// <summary>
        /// Walks the <c>extendsTypeId</c> chain from <see cref="type"/>
        /// upward and computes the merged schema. Cycles are caught and
        /// degrade to an empty chain / schema (matching the TS-side
        /// CustomValueNodeVM behavior — UI shows no fields rather than
        /// throwing an unrecoverable error). Computed once at
        /// construction; the wire DTOs are read-mostly so we don't
        /// invalidate on type-graph changes.
        /// </summary>
        private void ResolveTypeContext()
        {
            try
            {
                inheritanceChain = CustomTypeInheritance.ResolveChain(
                    type.id,
                    id => client.TryGetType(id, out var t) ? t : null);
                mergedSchema = CustomTypeInheritance.MergeSchemas(inheritanceChain);
            }
            catch (CircularInheritanceError ex)
            {
                Debug.LogError(ex);
                inheritanceChain = new List<CustomType>();
                mergedSchema = new List<MergedSchemaEntry>();
            }
        }
    }

    /// <summary>
    /// Writeable variant of <see cref="NeoAttributeCustom"/>. All
    /// descendants are also Saved (the
    /// <see cref="CreateChild"/> override returns
    /// <see cref="NeoAttribute.CreateSaved"/> kinds).
    /// </summary>
    public class NeoAttributeCustomSaved : NeoAttributeCustom
    {
        public NeoAttributeCustomSaved(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeCustomSaved(NeoClient client, CustomAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }

        protected override NeoAttribute CreateChild(
            NeoClient client,
            Attribute childAttribute,
            string? overrideValueId)
        {
            return CreateSaved(client, childAttribute, overrideValueId);
        }

        /// <summary>
        /// Sets the schema-keyed child to <paramref name="setValue"/>.
        /// Updates the existing entry in place when one exists; otherwise
        /// creates a fresh value row, registers it under
        /// <c>attributeValueOverrides</c>, and links it into the parent
        /// record's value-map.
        /// </summary>
        public void Set<TChildValue>(string key, TChildValue? setValue)
        {
            string nowIso = System.DateTime.UtcNow.ToString("o");

            // Resolution flows through the merged schema (inheritance
            // chain), so a Set against a key inherited from an ancestor
            // type still resolves the right child attribute.
            string? schemaKeyedAttributeId = LookupMergedAttributeId(key);
            if (schemaKeyedAttributeId is null)
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Merged schema for type {type.id} (chain depth {inheritanceChain.Count}) does not contain key '{key}'");
            }
            if (!client.TryGetAttribute(schemaKeyedAttributeId, out Attribute childAttribute))
            {
                throw new System.Exception(
                    $"No attribute for {nameof(schemaKeyedAttributeId)} '{schemaKeyedAttributeId}'");
            }
            if (childAttribute.required && setValue is null)
            {
                throw new System.ArgumentNullException(
                    nameof(setValue),
                    $"Cannot be null when child attribute '{key}' is required");
            }

            if (TryGetValueData(key, out AttributeValue<TChildValue?> existing))
            {
                existing.value = setValue;
                existing.updatedAt = nowIso;
                client.SetSaveValue(existing);
                return;
            }

            // No existing value row — create one + link it under the
            // parent record. The new child row goes into saveData.values
            // directly; the parent's value-map gets the new id appended;
            // the parent itself is re-saved so the new key/id pair
            // persists.
            string newValueId = System.Guid.NewGuid().ToString();
            AttributeValue newValueRow = AttributeValueFactory.Create(
                childAttribute, setValue, newValueId, nowIso, nowIso);
            client.SetSaveValue(newValueRow);

            // Make sure the parent has a value to link into. If we're
            // setting on a Custom that has never had a value, we need
            // to materialize one first.
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

            childAttributes[key] = CreateChild(client, childAttribute, newValueId);
        }
    }
}
