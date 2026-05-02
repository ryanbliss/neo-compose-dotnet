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

        public NeoAttributeList(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId)
        {
            entryAttribute = ResolveEntryAttribute();
            ReinitializeChildren();
        }

        public NeoAttributeList(NeoClient client, ListAttribute attribute, string? overrideValueId)
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

    public class NeoAttributeListSaved : NeoAttributeList
    {
        public NeoAttributeListSaved(NeoClient client, string attributeId, string? overrideValueId)
            : base(client, attributeId, overrideValueId) { }

        public NeoAttributeListSaved(NeoClient client, ListAttribute attribute, string? overrideValueId)
            : base(client, attribute, overrideValueId) { }

        protected override NeoAttribute CreateChild(
            NeoClient client,
            Attribute childAttribute,
            string? overrideValueId)
        {
            var child = CreateSaved(client, childAttribute, overrideValueId);
            child.parent = this;
            return child;
        }

        /// <summary>
        /// Appends a new entry to the end of the list. If the parent
        /// list itself has no stored value yet, materialises one first.
        /// </summary>
        public void Add<TEntryValue>(TEntryValue? entryValue)
        {
            string nowIso = System.DateTime.UtcNow.ToString("o");
            EnsureParentExists(nowIso);

            string newValueId = System.Guid.NewGuid().ToString();
            AttributeValue newValueRow = AttributeValueFactory.Create(
                entryAttribute, entryValue, newValueId, nowIso, nowIso);
            client.SetSaveValue(newValueRow);

            string[] currentArr = value!.value ?? System.Array.Empty<string>();
            string[] nextArr = new string[currentArr.Length + 1];
            System.Array.Copy(currentArr, nextArr, currentArr.Length);
            nextArr[currentArr.Length] = newValueId;
            value.value = nextArr;
            value.updatedAt = nowIso;
            client.SetSaveValue(value);

            childAttributes.Add(CreateChild(client, entryAttribute, newValueId));
        }

        /// <summary>
        /// Replaces the entry at <paramref name="index"/>. Updates the
        /// existing value row in place rather than swapping ids, so
        /// any other references to that value see the change.
        /// </summary>
        public void Set<TEntryValue>(int index, TEntryValue? entryValue)
        {
            if (value?.value is null || index < 0 || index >= value.value.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index));
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            string entryValueId = value.value[index];

            if (client.TryGetValue(entryValueId, out AttributeValue<TEntryValue?>? existing))
            {
                existing.value = entryValue;
                existing.updatedAt = nowIso;
                client.SetSaveValue(existing);
                return;
            }
            throw new System.InvalidOperationException(
                $"List entry value '{entryValueId}' at index {index} not found");
        }

        /// <summary>
        /// Removes the entry at <paramref name="index"/>. Disposes the
        /// child <see cref="NeoAttribute"/> bound to that slot,
        /// re-saves the parent so the array shrinks, and cascade-deletes
        /// the orphaned value graph from <see cref="ProjectSaveData.values"/>.
        /// </summary>
        public void RemoveAt(int index)
        {
            if (value?.value is null || index < 0 || index >= value.value.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index));
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            string removedValueId = value.value[index];

            // Build the new array without the removed slot + persist.
            string[] currentArr = value.value;
            string[] nextArr = new string[currentArr.Length - 1];
            for (int i = 0, j = 0; i < currentArr.Length; i++)
            {
                if (i == index) continue;
                nextArr[j++] = currentArr[i];
            }
            value.value = nextArr;
            value.updatedAt = nowIso;
            client.SetSaveValue(value);

            // Dispose the child node + drop our reference. Its own
            // Dispose recursively disposes any descendants the entry
            // owned in the wrapper tree.
            NeoAttribute removedChild = childAttributes[index];
            removedChild.Dispose();
            childAttributes.RemoveAt(index);

            // GC the orphaned value graph from the save file.
            client.RemoveSaveValueAndDescendants(removedValueId);
        }

        private void EnsureParentExists(string nowIso)
        {
            if (value is not null) return;
            ArrayAttributeValue parentRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = System.Array.Empty<string>(),
            };
            client.AddSaveValue(attribute.id, parentRow);
            RefreshFromValueData();
        }
    }
}
