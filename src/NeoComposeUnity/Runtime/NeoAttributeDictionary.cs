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

        /// <summary>
        /// The (stamp-substituted) entry attribute children are constructed
        /// from. Exposed for <see cref="NeoGenericBindings"/> so collection
        /// codecs can resolve entry codecs before any entry node exists.
        /// </summary>
        internal Attribute EntryAttribute => entryAttribute;

        public NeoAttributeDictionary(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership)
        {
            entryAttribute = ResolveEntryAttribute();
            ReinitializeChildren();
        }

        public NeoAttributeDictionary(NeoClient client, DictionaryAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership)
        {
            entryAttribute = ResolveEntryAttribute();
            ReinitializeChildren();
        }

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

        public NeoAttribute this[string key] => childAttributes[key];

        public int Count => childAttributes.Count;

        public bool ContainsKey(string key) => childAttributes.ContainsKey(key);

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
            // The newly-bound row may carry a genericBindings stamp the
            // construction-time row lacked — re-substitute the entry
            // attribute before re-walking children.
            entryAttribute = ResolveEntryAttribute();
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
            var previousChildren = childAttributes;
            childAttributes = new();
            if (value?.value is null)
            {
                foreach (var child in previousChildren.Values) child.Dispose();
                return;
            }
            foreach (var kvp in value.value)
            {
                if (previousChildren.TryGetValue(kvp.Key, out NeoAttribute? existing)
                    && existing.attribute.id == entryAttribute.id
                    && (existing.overrideValueId == kvp.Value
                        || existing.value?.id == kvp.Value))
                {
                    childAttributes[kvp.Key] = existing;
                    previousChildren.Remove(kvp.Key);
                    continue;
                }
                childAttributes[kvp.Key] = CreateChild(client, entryAttribute, kvp.Value);
            }
            foreach (var child in previousChildren.Values) child.Dispose();
        }

        protected Attribute ResolveEntryAttribute()
        {
            if (!client.TryGetAttribute(attribute.entryAttributeId, out Attribute? match))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(attribute.entryAttributeId),
                    $"No attribute for {nameof(attribute)}.{nameof(attribute.entryAttributeId)} {attribute.entryAttributeId}");
            }
            // Entry substitution is lazy via the row's genericBindings stamp
            // (specs/custom-type-generics.md Decision 9). A generic entry
            // subtree with no bound row yet keeps the raw record — no entry
            // can exist until a (stamped) row is bound, at which point
            // OnValueIdChainChanged re-substitutes.
            var stamp = value?.genericBindings;
            if (stamp is null) return match;
            return NeoGenericResolution.SubstituteAttribute(
                client,
                match,
                NeoGenericResolution.EnvFromStamp(stamp));
        }
    }

    public class NeoAttributeDictionaryWritable : NeoAttributeDictionary
    {
        public NeoAttributeDictionaryWritable(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeDictionaryWritable(NeoClient client, DictionaryAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        protected override NeoAttribute CreateChild(
            NeoClient client,
            Attribute childAttribute,
            string? overrideValueId)
        {
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

        /// <summary>
        /// Sets the dictionary entry under <paramref name="key"/>.
        /// Updates an existing entry in place; otherwise creates a
        /// fresh entry value, links it under the parent's value-map,
        /// and re-saves the parent. If the parent itself has no
        /// stored value yet, materialises one first.
        /// </summary>
        internal void SetSerialized(string key, NeoValueWritePayload? setValue)
        {
            if (entryAttribute.required && (setValue is null || setValue.isNull))
            {
                throw new System.ArgumentNullException(
                    nameof(setValue),
                    $"Cannot be null when entry attribute is required");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryAttribute) ?? ownership;

            if (value?.value is not null
                && value.value.TryGetValue(key, out string existingValueId)
                && client.TryGetValue(entryOwnership, existingValueId, out AttributeValue? existing))
            {
                if (setValue?.isValueReference == true)
                {
                    string importedValueId = client.ImportValueReference(
                        entryOwnership,
                        setValue.valueId!,
                        out bool sourceMoved);
                    ObjectAttributeValue parentRow = EnsureWritableObject(nowIso);
                    parentRow.value![key] = importedValueId;
                    parentRow.updatedAt = nowIso;
                    client.SetWritableValue(ownership, parentRow);
                    value = parentRow;
                    client.RemoveWritableValueAndDescendantsIfUnlinked(entryOwnership, existingValueId);
                    if (childAttributes.TryGetValue(key, out NeoAttribute? linkedOldChild))
                    {
                        linkedOldChild.Dispose();
                    }
                    childAttributes[key] = CreateChild(client, entryAttribute, importedValueId);
                    if (sourceMoved)
                    {
                        setValue.RetargetMovedReference(client, entryAttribute, importedValueId, entryOwnership);
                    }
                    NotifyChanged();
                    return;
                }
                // Reuse the entry's stable id: clone-on-write the entry row
                // (shadowing the authored default) and overwrite its content.
                AttributeValue next = AttributeValueFactory.Create(
                    entryAttribute,
                    setValue?.value,
                    existingValueId,
                    existing.createdAt,
                    nowIso);
                // A shadow of a stamped nested-collection entry keeps the
                // immutable stamp (spec Decision 9/16).
                next.genericBindings = existing.genericBindings;
                NeoGenericResolution.StampGenericBindings(
                    client,
                    entryAttribute,
                    next,
                    NeoGenericResolution.EnvFromStamp(value?.genericBindings));
                client.SetWritablePayloadRows(entryOwnership, setValue?.value);
                client.SetWritableValue(entryOwnership, next);
                if (childAttributes.TryGetValue(key, out NeoAttribute? oldChild))
                {
                    oldChild.Dispose();
                }
                childAttributes[key] = CreateChild(client, entryAttribute, existingValueId);
                NotifyChanged();
                return;
            }

            string newValueId;
            if (setValue?.isValueReference == true)
            {
                newValueId = client.ImportValueReference(
                    entryOwnership,
                    setValue.valueId!,
                    out bool sourceMoved);
                if (sourceMoved)
                {
                    setValue.RetargetMovedReference(client, entryAttribute, newValueId, entryOwnership);
                }
            }
            else
            {
                newValueId = System.Guid.NewGuid().ToString();
                AttributeValue newValueRow = AttributeValueFactory.Create(
                    entryAttribute, setValue?.value, newValueId, nowIso, nowIso);
                // A nested collection entry (e.g. Dictionary<string, List<T>>)
                // carries its own Decision-9 stamp, resolved through this
                // row's stamp.
                NeoGenericResolution.StampGenericBindings(
                    client,
                    entryAttribute,
                    newValueRow,
                    NeoGenericResolution.EnvFromStamp(value?.genericBindings));
                client.SetWritablePayloadRows(entryOwnership, setValue?.value);
                client.SetWritableValue(entryOwnership, newValueRow);
            }

            ObjectAttributeValue keyedRow = EnsureWritableObject(nowIso);
            keyedRow.value![key] = newValueId;
            keyedRow.updatedAt = nowIso;
            client.SetWritableValue(ownership, keyedRow);
            value = keyedRow;

            childAttributes[key] = CreateChild(client, entryAttribute, newValueId);
            NotifyChanged();
        }

        public void Remove(string key)
        {
            if (value?.value is null) return;
            if (!value.value.ContainsKey(key)) return;
            string nowIso = System.DateTime.UtcNow.ToString("o");

            // Clone-on-write the dict row (shadowing the authored default at
            // its stable id) and drop the key.
            ObjectAttributeValue parentRow = EnsureWritableObject(nowIso);
            string removedValueId = parentRow.value![key];
            parentRow.value.Remove(key);
            parentRow.updatedAt = nowIso;
            client.SetWritableValue(ownership, parentRow);
            value = parentRow;

            // Dispose the child node (recursive — its own Dispose
            // disposes any grandchildren) and drop our reference.
            if (childAttributes.TryGetValue(key, out NeoAttribute? child))
            {
                child.Dispose();
                childAttributes.Remove(key);
            }

            // GC the orphaned value graph from the writable store. The
            // removed valueId may itself reference more child values
            // (e.g., the entry was a Custom record); the cascade walks them.
            client.RemoveWritableValueAndDescendantsIfUnlinked(ownership, removedValueId);
            NotifyChanged();
        }

        internal override void BindChildValueId(NeoAttribute child, string childValueId)
        {
            string? key = null;
            foreach (var pair in childAttributes)
            {
                if (ReferenceEquals(pair.Value, child)) { key = pair.Key; break; }
            }
            if (key is null)
            {
                throw new System.InvalidOperationException(
                    $"Cannot bind a child value on Dictionary '{attribute.id}': child is not a registered entry.");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            ObjectAttributeValue parentRow = EnsureWritableObject(nowIso);
            parentRow.value![key] = childValueId;
            parentRow.updatedAt = nowIso;
            client.SetWritableValue(ownership, parentRow);
            value = parentRow;
            ReinitializeChildren();
            NotifyChanged();
        }

        /// <summary>
        /// Returns the dictionary's own object row guaranteed writable (a
        /// clone-on-write shadow at the stable id), minting + binding a
        /// fresh row through the parent when nothing is bound yet. The freshly
        /// minted row is seeded from the currently-effective map (the authored
        /// default's entries), NOT an empty map — otherwise Remove/Clear would
        /// index keys the caller can see but the fresh row lacks (throwing
        /// KeyNotFoundException), and an overwrite of one key would silently
        /// drop the sibling default entries.
        /// </summary>
        private ObjectAttributeValue EnsureWritableObject(string nowIso)
        {
            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value ??= new Dictionary<string, string>();
                return writable;
            }
            ObjectAttributeValue parentRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = value?.value is null
                    ? new Dictionary<string, string>()
                    : new Dictionary<string, string>(value.value),
            };
            // SDK-created rows carry the Decision-9 stamp from the wrapper
            // tree's enclosing context (spec §9).
            NeoGenericResolution.StampGenericBindings(
                client, attribute, parentRow, NeoGenericResolution.ResolveContextEnv(parent));
            BindNewValue(parentRow);
            // The freshly-stamped row may close generic entry references
            // the construction-time (row-less) resolution left raw.
            entryAttribute = ResolveEntryAttribute();
            return parentRow;
        }
    }
}
