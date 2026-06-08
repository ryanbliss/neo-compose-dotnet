// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a List-typed attribute. Children are positionally
    /// indexed; each child is a <see cref="NeoAttribute"/> for the
    /// list's entry attribute (per
    /// <see cref="ListAttribute.entryAttributeId"/>) bound to the
    /// value referenced from the list.
    /// </summary>
    public class NeoAttributeList
        : NeoAttribute<ListAttribute, ArrayAttributeValue>,
          IEnumerable<NeoAttribute>
    {
        protected Attribute entryAttribute;
        protected List<NeoAttribute> childAttributes = new();

        public NeoAttributeList(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership)
        {
            entryAttribute = ResolveEntryAttribute();
            ReinitializeChildren();
        }

        public NeoAttributeList(NeoClient client, ListAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
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
            var child = Create(client, childAttribute, overrideValueId);
            child.parent = this;
            return child;
        }

        public NeoAttribute this[int index] => childAttributes[index];

        public int Count => childAttributes.Count;

        public IEnumerator<NeoAttribute> GetEnumerator() =>
            childAttributes.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        protected override void Initialize(ArrayAttributeValue value)
        {
            base.Initialize(value);
            // entryAttribute isn't set yet on the first base-ctor pass;
            // ReinitializeChildren runs after the derived ctor wires it.
        }

        protected override void OnValueIdChainChanged()
        {
            base.OnValueIdChainChanged();
            ReinitializeChildren();
        }

        public override void Dispose()
        {
            if (isDisposed) return;
            foreach (var child in childAttributes) child.Dispose();
            childAttributes.Clear();
            base.Dispose();
        }

        protected void ReinitializeChildren()
        {
            // Dispose existing children before clearing — they may have
            // been bound to value-ids that aren't in the new value
            // graph; leaving them registered would leak them in
            // client.nodes.
            foreach (var child in childAttributes) child.Dispose();
            childAttributes.Clear();
            if (value?.value is null) return;
            foreach (var entryValueId in value.value)
            {
                childAttributes.Add(CreateChild(client, entryAttribute, entryValueId));
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

    public class NeoAttributeListWritable : NeoAttributeList
    {
        public NeoAttributeListWritable(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership) { }

        public NeoAttributeListWritable(NeoClient client, ListAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership) { }

        protected override NeoAttribute CreateChild(
            NeoClient client,
            Attribute childAttribute,
            string? overrideValueId)
        {
            var child = CreateWritable(client, childAttribute, overrideValueId, ownership);
            child.parent = this;
            return child;
        }

        /// <summary>
        /// Appends a new entry to the end of the list. Clone-on-writes the
        /// list's own array row (shadowing the authored default at its
        /// stable id) before appending; mints + binds an empty array row
        /// first when the list has no bound value yet.
        /// </summary>
        internal void AddSerialized(NeoValueWritePayload? entryValue)
        {
            if (entryAttribute.required && (entryValue is null || entryValue.isNull))
            {
                throw new System.ArgumentNullException(
                    nameof(entryValue),
                    "Cannot be null when entry attribute is required");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            ArrayAttributeValue parentRow = EnsureWritableArray(nowIso);

            string newValueId;
            if (entryValue?.isValueReference == true)
            {
                newValueId = client.ImportValueReference(ownership, entryValue.valueId!);
            }
            else
            {
                newValueId = System.Guid.NewGuid().ToString();
                AttributeValue newValueRow = AttributeValueFactory.Create(
                    entryAttribute, entryValue?.value, newValueId, nowIso, nowIso);
                client.SetWritablePayloadRows(ownership, entryValue?.value);
                client.SetWritableValue(ownership, newValueRow);
            }

            string[] currentArr = parentRow.value ?? System.Array.Empty<string>();
            string[] nextArr = new string[currentArr.Length + 1];
            System.Array.Copy(currentArr, nextArr, currentArr.Length);
            nextArr[currentArr.Length] = newValueId;
            parentRow.value = nextArr;
            parentRow.updatedAt = nowIso;
            client.SetWritableValue(ownership, parentRow);
            // List/Dictionary nodes don't refresh their cached value on a write
            // event, so retarget explicitly to the just-shadowed row.
            value = parentRow;

            childAttributes.Add(CreateChild(client, entryAttribute, newValueId));
            NotifyChanged();
        }

        /// <summary>
        /// Replaces the entry at <paramref name="index"/>. Reuses the
        /// existing entry's stable id (other references to it see the
        /// change) and clone-on-writes the list's own array row only when a
        /// value-reference swap changes which id sits in the slot.
        /// </summary>
        internal void SetSerialized(int index, NeoValueWritePayload? entryValue)
        {
            if (entryAttribute.required && (entryValue is null || entryValue.isNull))
            {
                throw new System.ArgumentNullException(
                    nameof(entryValue),
                    "Cannot be null when entry attribute is required");
            }
            if (value?.value is null || index < 0 || index >= value.value.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index));
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            string entryValueId = value.value[index];

            if (entryValue?.isValueReference == true)
            {
                string importedValueId = client.ImportValueReference(ownership, entryValue.valueId!);
                ArrayAttributeValue parentRow = EnsureWritableArray(nowIso);
                parentRow.value![index] = importedValueId;
                parentRow.updatedAt = nowIso;
                client.SetWritableValue(ownership, parentRow);
                value = parentRow;
                client.RemoveWritableValueAndDescendantsIfUnlinked(ownership, entryValueId);
                childAttributes[index].Dispose();
                childAttributes[index] = CreateChild(client, entryAttribute, importedValueId);
                NotifyChanged();
                return;
            }

            if (!client.TryGetValue(ownership, entryValueId, out AttributeValue? existing))
            {
                throw new System.InvalidOperationException(
                    $"List entry value '{entryValueId}' at index {index} not found");
            }
            AttributeValue next = AttributeValueFactory.Create(
                entryAttribute,
                entryValue?.value,
                entryValueId,
                existing.createdAt,
                nowIso);
            client.SetWritablePayloadRows(ownership, entryValue?.value);
            client.SetWritableValue(ownership, next);
            childAttributes[index].Dispose();
            childAttributes[index] = CreateChild(client, entryAttribute, entryValueId);
            NotifyChanged();
        }

        /// <summary>
        /// Removes the entry at <paramref name="index"/>. Clone-on-writes
        /// the list's own array row so the shrink shadows the authored
        /// default at its stable id, disposes the child
        /// <see cref="NeoAttribute"/> bound to that slot, and cascade-deletes
        /// the orphaned value graph from the writable store.
        /// </summary>
        public void RemoveAt(int index)
        {
            if (value?.value is null || index < 0 || index >= value.value.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index));
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            ArrayAttributeValue parentRow = EnsureWritableArray(nowIso);
            string[] currentArr = parentRow.value!;
            string removedValueId = currentArr[index];

            string[] nextArr = new string[currentArr.Length - 1];
            for (int i = 0, j = 0; i < currentArr.Length; i++)
            {
                if (i == index) continue;
                nextArr[j++] = currentArr[i];
            }
            parentRow.value = nextArr;
            parentRow.updatedAt = nowIso;
            client.SetWritableValue(ownership, parentRow);
            value = parentRow;

            // Dispose the child node + drop our reference. Its own
            // Dispose recursively disposes any descendants the entry
            // owned in the wrapper tree.
            NeoAttribute removedChild = childAttributes[index];
            removedChild.Dispose();
            childAttributes.RemoveAt(index);

            // GC the orphaned value graph from the writable store.
            client.RemoveWritableValueAndDescendantsIfUnlinked(ownership, removedValueId);
            NotifyChanged();
        }

        /// <summary>
        /// Returns the list's own array row guaranteed writable (a
        /// clone-on-write shadow at the stable id), minting + binding a
        /// fresh empty array through the parent when nothing is bound yet.
        /// </summary>
        private ArrayAttributeValue EnsureWritableArray(string nowIso)
        {
            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value ??= System.Array.Empty<string>();
                return writable;
            }
            ArrayAttributeValue parentRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = System.Array.Empty<string>(),
            };
            BindNewValue(parentRow);
            return parentRow;
        }
    }
}
