// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
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

        public NeoAttributeCustom(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership)
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

        public NeoAttributeCustom(NeoClient client, CustomAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership)
        {
            type = ResolveCustomType();
            ResolveTypeContext();
            ReinitializeChildren();
        }

        /// <summary>
        /// Hook for child instantiation — returns the read-only kind for
        /// Inherit children. <see cref="NeoAttributeCustomWritable"/>
        /// overrides this to return Writable kinds so descendants of a
        /// writeable Custom are also writeable. An explicit declared
        /// storage on the child (specs/attribute-storage.md §2.1) overrides
        /// the family default in both directions: a Save/Session-stamped
        /// child is writable even under a read-only parent. Sets
        /// <see cref="NeoAttribute.parent"/> on the constructed child so
        /// consumers (e.g., <see cref="NeoAttributeNSGetter.Compute"/>)
        /// can walk up.
        /// </summary>
        protected virtual NeoAttribute CreateChild(
            NeoClient client,
            Attribute childAttribute,
            string? overrideValueId)
        {
            NeoValueOwnership? declared = client.DeclaredOwnership(childAttribute);
            NeoAttribute child =
                declared == NeoValueOwnership.Save || declared == NeoValueOwnership.Session
                    ? CreateWritable(client, childAttribute, overrideValueId, declared.Value)
                    : Create(client, childAttribute, overrideValueId);
            child.parent = this;
            return child;
        }

        public NeoAttribute this[string key]
        {
            get => Get<NeoAttribute>(key);
        }

        public TNeoAttribute Get<TNeoAttribute>(string key)
            where TNeoAttribute : NeoAttribute
        {
            if (!TryGet(key, out TNeoAttribute? attr))
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"No child {nameof(NeoAttribute)} for {nameof(key)} '{key}' on {nameof(NeoAttributeCustom)} {attribute.id}");
            }
            return attr;
        }

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

        /// <summary>
        /// Writable view over the same record (same attribute / value id /
        /// ownership). Generated ReadOnly classes use this for members with
        /// an explicit Save/Session storage stamp
        /// (specs/attribute-storage.md §8.3): the view's
        /// <see cref="SetSerializedValue"/> routes the stamped leaf's write
        /// into the leaf's own writable store, while unstamped keys keep
        /// throwing the static-storage error.
        /// </summary>
        internal NeoAttributeCustomWritable AsWritableView()
        {
            if (this is NeoAttributeCustomWritable writable) return writable;
            var view = new NeoAttributeCustomWritable(client, attribute, overrideValueId, ownership)
            {
                parent = parent,
            };
            return view;
        }

        internal bool TryGetSchemaKeyForChild(
            NeoAttribute child,
            [NotNullWhen(true)] out string? schemaKey)
        {
            foreach (var pair in childAttributes)
            {
                if (ReferenceEquals(pair.Value, child))
                {
                    schemaKey = pair.Key;
                    return true;
                }
            }
            schemaKey = null;
            return false;
        }

        protected TValue? GetValueData<TValue>(string key) where TValue : AttributeValue
        {
            if (!TryGetValueData(key, out TValue? value))
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

        protected bool TryGetValueData<TValue>(string key, [NotNullWhen(true)] out TValue? outValue)
            where TValue : AttributeValue
        {
            if (value?.value is not null && value.value.TryGetValue(key, out string valueIdForKey))
            {
                return client.TryGetValue(valueIdForKey, out outValue);
            }
            outValue = null;
            return false;
        }

        protected TAttribute GetAttribute<TAttribute>(string key)
            where TAttribute : Attribute
        {
            if (!TryGetAttribute(key, out TAttribute? childAttribute))
            {
                throw new System.NullReferenceException(
                    $"attribute for {nameof(key)} '{key}' not found");
            }
            return childAttribute;
        }

        protected bool TryGetAttribute<TAttribute>(string key, [NotNullWhen(true)] out TAttribute? outAttribute)
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
            outAttribute = null;
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

        protected override void OnValueIdChainChanged()
        {
            base.OnValueIdChainChanged();
            // The new value's record may carry a different keyset —
            // re-walk so disposed-orphans get released and any new
            // schema-keys get nodes.
            ReinitializeChildren();
        }

        public override void Dispose()
        {
            if (isDisposed) return;
            foreach (var child in childAttributes.Values)
            {
                child.OnChanged -= HandleChildChanged;
                child.Dispose();
            }
            childAttributes.Clear();
            base.Dispose();
        }

        /// <summary>
        /// Walks <c>value.value</c> and rebuilds the
        /// <see cref="childAttributes"/> dict from scratch using the
        /// current <see cref="type"/>'s schema. Called after the
        /// schema is wired (post-base-ctor), and again whenever a
        /// Writable mutation invalidates the cached children.
        /// </summary>
        protected void ReinitializeChildren()
        {
            var previousChildren = childAttributes;
            childAttributes = new();
            foreach (var entry in mergedSchema)
            {
                if (!client.TryGetAttribute(entry.attributeId, out Attribute? childAttribute)) continue;
                string? childValueId = value?.value is not null
                    && value.value.TryGetValue(entry.schemaKey, out string valueIdForKey)
                        ? valueIdForKey
                        : null;
                if (previousChildren.TryGetValue(entry.schemaKey, out NeoAttribute? existing)
                    && existing.attribute.id == childAttribute.id
                    && existing.overrideValueId == childValueId)
                {
                    childAttributes[entry.schemaKey] = existing;
                    previousChildren.Remove(entry.schemaKey);
                    continue;
                }
                var child = CreateChild(client, childAttribute, childValueId);
                child.OnChanged += HandleChildChanged;
                childAttributes[entry.schemaKey] = child;
            }
            foreach (var child in previousChildren.Values)
            {
                child.OnChanged -= HandleChildChanged;
                child.Dispose();
            }
        }

        protected void HandleChildChanged(NeoAttribute child)
        {
            NotifyChanged(child);
        }

        protected void NotifyChildChanged(string key)
        {
            if (childAttributes.TryGetValue(key, out NeoAttribute? child))
            {
                NotifyChanged(child);
                return;
            }
            NotifyChanged();
        }

        protected static bool ChildSelfNotifies(NeoAttribute child)
        {
            return child is not NeoAttributeDictionary
                && child is not NeoAttributeList;
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
            if (!client.TryGetType(attribute.customTypeId, out CustomType? match))
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
    /// <see cref="NeoAttribute.CreateWritable"/> kinds).
    /// </summary>
    public class NeoAttributeCustomWritable : NeoAttributeCustom
    {
        public NeoAttributeCustomWritable(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeCustomWritable(NeoClient client, CustomAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        protected override NeoAttribute CreateChild(
            NeoClient client,
            Attribute childAttribute,
            string? overrideValueId)
        {
            // An explicit declared storage fixes the child's shape in both
            // families (specs/attribute-storage.md §8.3): Static-stamped
            // children stay read-only even under a writable parent;
            // Save/Session stamps pin the child to that ownership store.
            NeoValueOwnership? declared = client.DeclaredOwnership(childAttribute);
            NeoAttribute child = declared switch
            {
                NeoValueOwnership.Asset => Create(client, childAttribute, overrideValueId),
                NeoValueOwnership.Save or NeoValueOwnership.Session =>
                    CreateWritable(client, childAttribute, overrideValueId, declared.Value),
                _ => CreateWritable(client, childAttribute, overrideValueId, ownership),
            };
            child.parent = this;
            return child;
        }

        public TNeoAttribute GetOrCreateCollection<TNeoAttribute>(string key)
            where TNeoAttribute : NeoAttribute
        {
            // A child that already resolves a value (authored default or a
            // prior write) is returned as-is — clone-on-write happens lazily
            // on its first mutation. A child with no resolved value (optional
            // key absent from the record, no authored default) is bound to an
            // empty collection now, so the returned node tracks its array/map
            // by a stable id instead of mint-binding mid-mutation.
            if (TryGet(key, out TNeoAttribute? existing) && existing.value is not null)
            {
                return existing;
            }

            string? schemaKeyedAttributeId = LookupMergedAttributeId(key);
            if (schemaKeyedAttributeId is null)
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Merged schema for type {type.id} (chain depth {inheritanceChain.Count}) does not contain key '{key}'");
            }
            if (!client.TryGetAttribute(schemaKeyedAttributeId, out Attribute? childAttribute))
            {
                throw new System.Exception(
                    $"No attribute for {nameof(schemaKeyedAttributeId)} '{schemaKeyedAttributeId}'");
            }

            NeoValueWritePayload initialValue = childAttribute switch
            {
                ListAttribute => NeoValueWritePayload.FromValue(System.Array.Empty<string>()),
                DictionaryAttribute => NeoValueWritePayload.FromValue(new Dictionary<string, string>()),
                _ => throw new System.InvalidOperationException(
                    $"Attribute '{key}' is not a collection attribute."),
            };
            SetSerializedValue(key, initialValue);
            return Get<TNeoAttribute>(key);
        }

        public NeoAttributeLookupWritable GetOrCreateLookup(string key)
        {
            if (TryGet(key, out NeoAttributeLookupWritable? existing) && existing.value is not null)
            {
                return existing;
            }

            string? schemaKeyedAttributeId = LookupMergedAttributeId(key);
            if (schemaKeyedAttributeId is null)
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Merged schema for type {type.id} (chain depth {inheritanceChain.Count}) does not contain key '{key}'");
            }
            if (!client.TryGetAttribute(schemaKeyedAttributeId, out Attribute? childAttribute))
            {
                throw new System.Exception(
                    $"No attribute for {nameof(schemaKeyedAttributeId)} '{schemaKeyedAttributeId}'");
            }
            if (childAttribute is not LookupAttribute)
            {
                throw new System.InvalidOperationException(
                    $"Attribute '{key}' is not a lookup attribute.");
            }

            SetSerializedValue(key, NeoValueWritePayload.FromValue(System.Array.Empty<string>()));
            return Get<NeoAttributeLookupWritable>(key);
        }

        public NeoAttributeStringWritable GetOrCreateString(
            string key,
            string? initialValue = null)
        {
            if (TryGet(key, out NeoAttributeStringWritable? existing) && existing.value is not null)
            {
                return existing;
            }

            string? schemaKeyedAttributeId = LookupMergedAttributeId(key);
            if (schemaKeyedAttributeId is null)
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Merged schema for type {type.id} (chain depth {inheritanceChain.Count}) does not contain key '{key}'");
            }
            if (!client.TryGetAttribute(schemaKeyedAttributeId, out Attribute? childAttribute))
            {
                throw new System.Exception(
                    $"No attribute for {nameof(schemaKeyedAttributeId)} '{schemaKeyedAttributeId}'");
            }
            if (childAttribute is not StringAttribute)
            {
                throw new System.InvalidOperationException(
                    $"Attribute '{key}' is not a string attribute.");
            }

            SetSerializedValue(key, NeoValueWritePayload.FromValue(initialValue));
            return Get<NeoAttributeStringWritable>(key);
        }

        public void SetStringLiteral(string key, string? value)
        {
            string? schemaKeyedAttributeId = LookupMergedAttributeId(key);
            if (schemaKeyedAttributeId is null)
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Merged schema for type {type.id} (chain depth {inheritanceChain.Count}) does not contain key '{key}'");
            }
            if (!client.TryGetAttribute(schemaKeyedAttributeId, out Attribute? childAttribute))
            {
                throw new System.Exception(
                    $"No attribute for {nameof(schemaKeyedAttributeId)} '{schemaKeyedAttributeId}'");
            }
            if (childAttribute is not StringAttribute)
            {
                throw new System.InvalidOperationException(
                    $"Attribute '{key}' is not a string attribute.");
            }

            SetSerializedValue(key, NeoValueWritePayload.FromValue(value));
        }

        /// <summary>
        /// Sets the schema-keyed child to <paramref name="setValue"/>.
        /// Reuses the existing entry's stable id when one is bound
        /// (clone-on-writing the record + entry rows so they shadow the
        /// authored defaults); otherwise mints a fresh value row and links
        /// it into the record's (clone-on-write) value-map under
        /// <paramref name="key"/>.
        /// </summary>
        internal void SetSerializedValue(string key, NeoValueWritePayload? setValue)
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
            if (!client.TryGetAttribute(schemaKeyedAttributeId, out Attribute? childAttribute))
            {
                throw new System.Exception(
                    $"No attribute for {nameof(schemaKeyedAttributeId)} '{schemaKeyedAttributeId}'");
            }
            if (childAttribute.required && (setValue is null || setValue.isNull))
            {
                throw new System.ArgumentNullException(
                    nameof(setValue),
                    $"Cannot be null when child attribute '{key}' is required");
            }

            // Per-placement storage (specs/attribute-storage.md): a declared
            // storage stamp on the child pins which writable store the leaf
            // shadows into, independent of this record's own ownership — the
            // headline case being a Save-stamped field on a static record.
            NeoValueOwnership childOwnership =
                client.DeclaredOwnership(childAttribute) ?? ownership;
            if (childOwnership == NeoValueOwnership.Asset)
            {
                throw new System.InvalidOperationException(
                    $"Cannot write '{key}' on Custom '{attribute.id}': its effective storage is static.");
            }
            bool recordWritable = ownership != NeoValueOwnership.Asset;

            if (value?.value is not null
                && value.value.TryGetValue(key, out string existingValueId)
                && client.TryGetValue(childOwnership, existingValueId, out AttributeValue? existing))
            {
                if (setValue?.isValueReference == true)
                {
                    if (!recordWritable)
                    {
                        throw new System.InvalidOperationException(
                            $"Cannot rebind '{key}' on static Custom '{attribute.id}': a static record's value map is authored data. Only the stamped leaf's own value may be written.");
                    }
                    string importedValueId = client.ImportValueReference(ownership, setValue.valueId!);
                    ObjectAttributeValue record = EnsureWritableObject(nowIso);
                    record.value![key] = importedValueId;
                    record.updatedAt = nowIso;
                    client.SetWritableValue(ownership, record);
                    value = record;
                    client.RemoveWritableValueAndDescendantsIfUnlinked(ownership, existingValueId);
                    ReinitializeChildren();
                    NotifyChildChanged(key);
                    return;
                }
                bool childWillSelfNotify = childAttributes.TryGetValue(key, out NeoAttribute? existingChild)
                    && ChildSelfNotifies(existingChild);
                // Reuse the entry's stable id: a fresh row at the same id
                // shadows the authored default in the child's writable store.
                AttributeValue next = AttributeValueFactory.Create(
                    childAttribute,
                    setValue?.value,
                    existingValueId,
                    existing.createdAt,
                    nowIso);
                client.SetWritablePayloadRows(childOwnership, setValue?.value);
                client.SetWritableValue(childOwnership, next);
                ReinitializeChildren();
                if (!childWillSelfNotify)
                {
                    NotifyChildChanged(key);
                }
                return;
            }

            // No existing entry for this key — mint a fresh value row and
            // link it under the record's value-map. A static record cannot
            // gain keys at runtime: linking mutates the record itself.
            if (!recordWritable)
            {
                throw new System.InvalidOperationException(
                    $"Cannot write '{key}' on static Custom '{attribute.id}': the stamped leaf has no authored value to shadow, and a static record cannot gain new keys at runtime. Author a value for '{key}' in the web editor.");
            }
            string newValueId;
            if (setValue?.isValueReference == true)
            {
                newValueId = client.ImportValueReference(ownership, setValue.valueId!);
            }
            else
            {
                newValueId = System.Guid.NewGuid().ToString();
                AttributeValue newValueRow = AttributeValueFactory.Create(
                    childAttribute, setValue?.value, newValueId, nowIso, nowIso);
                client.SetWritablePayloadRows(childOwnership, setValue?.value);
                client.SetWritableValue(childOwnership, newValueRow);
            }

            ObjectAttributeValue keyedRecord = EnsureWritableObject(nowIso);
            keyedRecord.value![key] = newValueId;
            keyedRecord.updatedAt = nowIso;
            client.SetWritableValue(ownership, keyedRecord);
            value = keyedRecord;

            ReinitializeChildren();
            NotifyChildChanged(key);
        }

        internal override void BindChildValueId(NeoAttribute child, string childValueId)
        {
            if (!TryGetSchemaKeyForChild(child, out string? key))
            {
                throw new System.InvalidOperationException(
                    $"Cannot bind a child value on Custom '{attribute.id}': child is not a registered schema field.");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            ObjectAttributeValue record = EnsureWritableObject(nowIso);
            record.value![key] = childValueId;
            record.updatedAt = nowIso;
            client.SetWritableValue(ownership, record);
            value = record;
            ReinitializeChildren();
            NotifyChildChanged(key);
        }

        /// <summary>
        /// Returns the record's own object row guaranteed writable (a
        /// clone-on-write shadow at the stable id), minting + binding a
        /// fresh empty record through the parent when nothing is bound yet.
        /// </summary>
        private ObjectAttributeValue EnsureWritableObject(string nowIso)
        {
            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value ??= new Dictionary<string, string>();
                return writable;
            }
            ObjectAttributeValue record = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = new Dictionary<string, string>(),
            };
            BindNewValue(record);
            return record;
        }

        /// <summary>
        /// Removes the schema-keyed child under <paramref name="key"/>.
        /// Disposes the child <see cref="NeoAttribute"/>, drops the
        /// key from the parent record, persists, and cascade-deletes
        /// the orphaned value graph from
        /// <see cref="ProjectSaveData.values"/>.
        /// No-op if the key isn't present.
        /// </summary>
        public void Remove(string key)
        {
            if (value?.value is null) return;
            if (!value.value.ContainsKey(key)) return;
            string nowIso = System.DateTime.UtcNow.ToString("o");

            // Clone-on-write the record (shadowing the authored default at
            // its stable id) and drop the key.
            ObjectAttributeValue record = EnsureWritableObject(nowIso);
            string removedValueId = record.value![key];
            record.value.Remove(key);
            record.updatedAt = nowIso;
            client.SetWritableValue(ownership, record);
            value = record;

            if (childAttributes.TryGetValue(key, out NeoAttribute? child))
            {
                child.OnChanged -= HandleChildChanged;
                child.Dispose();
                childAttributes.Remove(key);
            }

            client.RemoveWritableValueAndDescendantsIfUnlinked(ownership, removedValueId);
            NotifyChanged();
        }

        /// <summary>
        /// Explicitly unsets the optional schema-keyed field under
        /// <paramref name="key"/> by stamping a removal tombstone at the child's
        /// stable value id, so the field resolves as unset rather than the
        /// authored default. Sparse: the record keeps the key and is left
        /// untouched (contrast <see cref="Remove"/>, which drops the key and
        /// reverts to the authored default). No-op when the key is not bound to a
        /// value. Throws if the field is required.
        /// </summary>
        public void Unset(string key)
        {
            string? attributeId = LookupMergedAttributeId(key);
            if (attributeId is null)
            {
                throw new System.Collections.Generic.KeyNotFoundException(
                    $"Merged schema for type {type.id} (chain depth {inheritanceChain.Count}) does not contain key '{key}'");
            }
            if (client.TryGetAttribute(attributeId, out Attribute? childAttribute)
                && childAttribute.required)
            {
                throw new System.InvalidOperationException(
                    $"Cannot unset required field '{key}'.");
            }
            if (value?.value is null || !value.value.TryGetValue(key, out string childValueId))
            {
                return;
            }
            client.WriteRemovalTombstone(ownership, childValueId);
            ReinitializeChildren();
            NotifyChildChanged(key);
        }
    }
}
