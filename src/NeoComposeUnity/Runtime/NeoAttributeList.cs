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

        /// <summary>
        /// True when the attribute declares <c>listKind: "unordered"</c>:
        /// the stored value is only the null-vs-present discriminator
        /// (<c>null</c> or <c>[]</c>) and membership resolves by join over
        /// <see cref="AttributeValue.containerId"/>, enumerated id-sorted.
        /// </summary>
        public bool IsUnordered { get; }

        public NeoAttributeList(NeoClient client, string attributeId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attributeId, overrideValueId, ownership)
        {
            entryAttribute = ResolveEntryAttribute();
            IsUnordered = client.IsUnorderedList(attribute);
            ReinitializeChildren();
        }

        public NeoAttributeList(NeoClient client, ListAttribute attribute, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, attribute, overrideValueId, ownership)
        {
            entryAttribute = ResolveEntryAttribute();
            IsUnordered = client.IsUnorderedList(attribute);
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

        public NeoAttribute this[int index] => childAttributes[index];

        public int Count => childAttributes.Count;

        internal NeoListChangedArgs? ActiveListChange { get; private set; }

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

        /// <summary>
        /// The list's current entry value ids. Ordered lists read the inline
        /// array as-is (array order is the list order). Unordered lists read
        /// the discriminator first — a null value resolves to no entries —
        /// then join the membership index (id-sorted), tolerating legacy
        /// inline ids (e.g. factory-minted session rows) ahead of the join.
        /// </summary>
        protected IReadOnlyList<string> ResolveEntryValueIds()
        {
            if (value?.value is null) return System.Array.Empty<string>();
            if (!IsUnordered) return value.value;
            var ids = new List<string>();
            var seen = new HashSet<string>();
            foreach (var entryId in value.value)
            {
                if (string.IsNullOrEmpty(entryId)) continue;
                if (!seen.Add(entryId)) continue;
                var row = client.ResolveEffectiveRow(entryId);
                if (row is null || row.IsRemoved) continue;
                ids.Add(entryId);
            }
            foreach (var entryId in client.GetUnorderedListEntryIds(value.id))
            {
                if (!seen.Add(entryId)) continue;
                ids.Add(entryId);
            }
            return ids;
        }

        protected void ReinitializeChildren()
        {
            var previousChildren = childAttributes;
            childAttributes = new();
            var entryValueIds = ResolveEntryValueIds();
            if (value?.value is null)
            {
                foreach (var child in previousChildren) child.Dispose();
                return;
            }
            for (int i = 0; i < entryValueIds.Count; i++)
            {
                string entryValueId = entryValueIds[i];
                if (i < previousChildren.Count)
                {
                    var existing = previousChildren[i];
                    if (existing.attribute.id == entryAttribute.id
                        && (existing.overrideValueId == entryValueId
                            || existing.value?.id == entryValueId))
                    {
                        childAttributes.Add(existing);
                        previousChildren[i] = null!;
                        continue;
                    }
                }
                childAttributes.Add(CreateChild(client, entryAttribute, entryValueId));
            }
            foreach (var child in previousChildren)
            {
                if (child is not null) child.Dispose();
            }
        }

        protected void NotifyListChanged(NeoListChangedArgs change)
        {
            ActiveListChange = change ?? NeoListChangedArgs.Unknown;
            try
            {
                NotifyChanged();
            }
            finally
            {
                ActiveListChange = null;
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
            if (IsUnordered)
            {
                AddSerializedUnordered(entryValue);
                return;
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryAttribute) ?? ownership;
            ArrayAttributeValue parentRow = EnsureWritableArray(nowIso);

            string newValueId;
            if (entryValue?.isValueReference == true)
            {
                newValueId = client.ImportValueReference(
                    entryOwnership,
                    entryValue.valueId!,
                    out bool sourceMoved);
                if (sourceMoved)
                {
                    entryValue.RetargetMovedReference(client, entryAttribute, newValueId, entryOwnership);
                }
            }
            else
            {
                newValueId = System.Guid.NewGuid().ToString();
                AttributeValue newValueRow = AttributeValueFactory.Create(
                    entryAttribute, entryValue?.value, newValueId, nowIso, nowIso);
                client.SetWritablePayloadRows(entryOwnership, entryValue?.value);
                client.SetWritableValue(entryOwnership, newValueRow);
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

            ReinitializeChildren();
            NotifyListChanged(new NeoListChangedArgs(
                NeoListChangeKind.Add,
                addedValueIds: new[] { newValueId }));
        }

        /// <summary>
        /// Replaces the entry at <paramref name="index"/>. Reuses the
        /// existing entry's stable id (other references to it see the
        /// change) and clone-on-writes the list's own array row only when a
        /// value-reference swap changes which id sits in the slot.
        /// </summary>
        internal void SetSerialized(int index, NeoValueWritePayload? entryValue)
        {
            if (IsUnordered)
            {
                throw new System.InvalidOperationException(
                    $"List attribute '{attribute.id}' is unordered; entries have no position to replace at. Remove the entry and add the replacement instead.");
            }
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
            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryAttribute) ?? ownership;

            if (entryValue?.isValueReference == true)
            {
                string importedValueId = client.ImportValueReference(
                    entryOwnership,
                    entryValue.valueId!,
                    out bool sourceMoved);
                ArrayAttributeValue parentRow = EnsureWritableArray(nowIso);
                parentRow.value![index] = importedValueId;
                parentRow.updatedAt = nowIso;
                client.SetWritableValue(ownership, parentRow);
                value = parentRow;
                client.RemoveWritableValueAndDescendantsIfUnlinked(entryOwnership, entryValueId);
                childAttributes[index].Dispose();
                childAttributes[index] = CreateChild(client, entryAttribute, importedValueId);
                if (sourceMoved)
                {
                    entryValue.RetargetMovedReference(client, entryAttribute, importedValueId, entryOwnership);
                }
                NotifyListChanged(new NeoListChangedArgs(
                    NeoListChangeKind.Replace,
                    removedValueIds: new[] { entryValueId },
                    addedValueIds: new[] { importedValueId },
                    replacedValueIds: new[] { entryValueId }));
                return;
            }

            if (!client.TryGetValue(entryOwnership, entryValueId, out AttributeValue? existing))
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
            client.SetWritablePayloadRows(entryOwnership, entryValue?.value);
            client.SetWritableValue(entryOwnership, next);
            childAttributes[index].Dispose();
            childAttributes[index] = CreateChild(client, entryAttribute, entryValueId);
            NotifyListChanged(new NeoListChangedArgs(
                NeoListChangeKind.Set,
                replacedValueIds: new[] { entryValueId }));
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
            if (IsUnordered)
            {
                var entryIds = ResolveEntryValueIds();
                if (index < 0 || index >= entryIds.Count)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(index));
                }
                RemoveById(entryIds[index]);
                return;
            }
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
            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryAttribute) ?? ownership;
            client.RemoveWritableValueAndDescendantsIfUnlinked(entryOwnership, removedValueId);
            NotifyListChanged(new NeoListChangedArgs(
                NeoListChangeKind.Remove,
                removedValueIds: new[] { removedValueId }));
        }

        internal void ClearSerialized()
        {
            if (IsUnordered)
            {
                ClearSerializedUnordered();
                return;
            }
            if (value?.value is null || value.value.Length == 0)
            {
                return;
            }

            string nowIso = System.DateTime.UtcNow.ToString("o");
            ArrayAttributeValue parentRow = EnsureWritableArray(nowIso);
            string[] removedValueIds = parentRow.value ?? System.Array.Empty<string>();
            if (removedValueIds.Length == 0)
            {
                return;
            }

            parentRow.value = System.Array.Empty<string>();
            parentRow.updatedAt = nowIso;
            client.SetWritableValue(ownership, parentRow);
            value = parentRow;

            foreach (var child in childAttributes)
            {
                child.Dispose();
            }
            childAttributes.Clear();

            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryAttribute) ?? ownership;
            foreach (var removedValueId in removedValueIds)
            {
                client.RemoveWritableValueAndDescendantsIfUnlinked(
                    entryOwnership,
                    removedValueId);
            }

            NotifyListChanged(new NeoListChangedArgs(
                NeoListChangeKind.Clear,
                removedValueIds: removedValueIds));
        }

        /// <summary>
        /// Removes the entry bound to <paramref name="entry"/> — the
        /// preferred removal verb for unordered lists (positions are
        /// meaningless); also works for ordered lists.
        /// </summary>
        public void Remove(NeoAttribute entry)
        {
            if (entry is null) throw new System.ArgumentNullException(nameof(entry));
            string? entryValueId = entry.value?.id;
            if (entryValueId is null)
            {
                throw new System.InvalidOperationException(
                    $"Cannot remove entry from list '{attribute.id}': the entry node has no bound value row.");
            }
            RemoveById(entryValueId);
        }

        /// <summary>
        /// Removes the entry whose value row is <paramref name="entryValueId"/>.
        /// For unordered lists: an authored member is subtracted with a
        /// removal tombstone in the entry ownership; an overlay-created
        /// member row is dropped along with its owned descendants.
        /// </summary>
        public void RemoveById(string entryValueId)
        {
            if (!IsUnordered)
            {
                var orderedIds = ResolveEntryValueIds();
                for (int i = 0; i < orderedIds.Count; i++)
                {
                    if (orderedIds[i] != entryValueId) continue;
                    RemoveAt(i);
                    return;
                }
                throw new System.ArgumentOutOfRangeException(
                    nameof(entryValueId),
                    $"Value '{entryValueId}' is not an entry of list attribute '{attribute.id}'.");
            }

            var entryIds = ResolveEntryValueIds();
            bool isEntry = false;
            foreach (var id in entryIds)
            {
                if (id != entryValueId) continue;
                isEntry = true;
                break;
            }
            if (!isEntry)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(entryValueId),
                    $"Value '{entryValueId}' is not an entry of unordered list attribute '{attribute.id}'.");
            }

            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryAttribute) ?? ownership;
            RemoveUnorderedEntry(entryOwnership, entryValueId);
            ReinitializeChildren();
            NotifyListChanged(new NeoListChangedArgs(
                NeoListChangeKind.Remove,
                removedValueIds: new[] { entryValueId }));
        }

        private void AddSerializedUnordered(NeoValueWritePayload? entryValue)
        {
            string nowIso = System.DateTime.UtcNow.ToString("o");
            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryAttribute) ?? ownership;
            ArrayAttributeValue containerRow = ResolveUnorderedContainerForAdd(nowIso);
            string containerValueId = containerRow.id;

            string newValueId;
            if (entryValue?.isValueReference == true)
            {
                newValueId = client.ImportValueReference(
                    entryOwnership,
                    entryValue.valueId!,
                    out bool sourceMoved);
                StampContainerId(entryOwnership, newValueId, containerValueId);
                if (sourceMoved)
                {
                    entryValue.RetargetMovedReference(client, entryAttribute, newValueId, entryOwnership);
                }
            }
            else
            {
                newValueId = System.Guid.NewGuid().ToString();
                AttributeValue newValueRow = AttributeValueFactory.Create(
                    entryAttribute, entryValue?.value, newValueId, nowIso, nowIso);
                newValueRow.containerId = containerValueId;
                client.SetWritablePayloadRows(entryOwnership, entryValue?.value);
                client.SetWritableValue(entryOwnership, newValueRow);
            }

            // Membership lives on the entry row; the container value is only
            // the null-vs-present discriminator and is NOT rewritten.
            ReinitializeChildren();
            NotifyListChanged(new NeoListChangedArgs(
                NeoListChangeKind.Add,
                addedValueIds: new[] { newValueId }));
        }

        private void ClearSerializedUnordered()
        {
            var entryIds = ResolveEntryValueIds();
            if (entryIds.Count == 0) return;
            string nowIso = System.DateTime.UtcNow.ToString("o");
            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryAttribute) ?? ownership;

            var removedValueIds = new List<string>(entryIds);
            using (client.SuspendContainerNotifications())
            {
                foreach (var entryValueId in removedValueIds)
                {
                    RemoveUnorderedEntry(entryOwnership, entryValueId);
                }
            }

            // Legacy inline ids (factory-minted rows referenced from the
            // array itself) also blank the array so the unordered invariant
            // — the stored array is always empty when present — holds.
            if (value?.value is not null && value.value.Length > 0)
            {
                ArrayAttributeValue containerRow = EnsureWritableArray(nowIso);
                containerRow.value = System.Array.Empty<string>();
                containerRow.updatedAt = nowIso;
                client.SetWritableValue(ownership, containerRow);
                value = containerRow;
            }

            foreach (var child in childAttributes)
            {
                child.Dispose();
            }
            childAttributes.Clear();
            ReinitializeChildren();
            NotifyListChanged(new NeoListChangedArgs(
                NeoListChangeKind.Clear,
                removedValueIds: removedValueIds));
        }

        /// <summary>
        /// Whole-list assignment translated to unordered semantics: clear the
        /// current membership, then either destroy the list instance
        /// (assigning null sets the discriminator to <c>null</c>) or start a
        /// fresh present instance (<c>[]</c>) and add each referenced entry.
        /// </summary>
        internal void AssignSerialized(NeoValueWritePayload? setValue)
        {
            if (!IsUnordered)
            {
                throw new System.InvalidOperationException(
                    $"AssignSerialized is the unordered whole-list path; ordered list attribute '{attribute.id}' assigns through SetSerialized/AddSerialized.");
            }
            ClearSerializedUnordered();
            string nowIso = System.DateTime.UtcNow.ToString("o");

            if (setValue is null || setValue.isNull)
            {
                // Setting an unordered list to null destroys the instance; the
                // members were tombstoned above, in the same write.
                ArrayAttributeValue containerRow = EnsureWritableArray(nowIso);
                containerRow.value = null;
                containerRow.updatedAt = nowIso;
                client.SetWritableValue(ownership, containerRow);
                value = containerRow;
                ReinitializeChildren();
                NotifyListChanged(new NeoListChangedArgs(NeoListChangeKind.Replace));
                return;
            }

            // Present: null → [] is a fresh, empty instance.
            ArrayAttributeValue presentRow = EnsureWritableArray(nowIso);
            if (presentRow.value is null || presentRow.value.Length > 0)
            {
                presentRow.value = System.Array.Empty<string>();
                presentRow.updatedAt = nowIso;
                client.SetWritableValue(ownership, presentRow);
                value = presentRow;
            }

            if (setValue.value is null)
            {
                ReinitializeChildren();
                NotifyListChanged(new NeoListChangedArgs(NeoListChangeKind.Replace));
                return;
            }
            if (setValue.value is not string[] entryValueIds)
            {
                throw new System.InvalidOperationException(
                    $"Unordered list attribute '{attribute.id}' whole-list assignment expects entry value ids (string[]), got '{setValue.value.GetType().Name}'.");
            }
            foreach (var entryValueId in entryValueIds)
            {
                AddSerializedUnordered(NeoValueWritePayload.FromValueReference(entryValueId, null));
            }
            NotifyListChanged(new NeoListChangedArgs(NeoListChangeKind.Replace));
        }

        /// <summary>
        /// Removes one unordered member per the overlay rules: authored
        /// members (or members living in a lower overlay) get a removal
        /// tombstone in <paramref name="entryOwnership"/>; members created in
        /// that overlay are dropped with their owned descendants. Legacy
        /// inline ids (referenced from the array rather than joined) fall
        /// back to array removal + garbage collection.
        /// </summary>
        private void RemoveUnorderedEntry(
            NeoValueOwnership entryOwnership,
            string entryValueId)
        {
            var effectiveRow = client.ResolveEffectiveRow(entryValueId);
            bool isJoinedMember = effectiveRow?.containerId == value?.id
                || (client.TryResolveContainerIdForValueId(entryValueId, out string? containerId)
                    && containerId == value?.id);
            if (!isJoinedMember)
            {
                // Legacy inline entry: drop it from the array shadow and GC.
                string nowIso = System.DateTime.UtcNow.ToString("o");
                ArrayAttributeValue containerRow = EnsureWritableArray(nowIso);
                if (containerRow.value is not null)
                {
                    var next = new List<string>(containerRow.value.Length);
                    foreach (var id in containerRow.value)
                    {
                        if (id == entryValueId) continue;
                        next.Add(id);
                    }
                    containerRow.value = next.ToArray();
                    containerRow.updatedAt = nowIso;
                    client.SetWritableValue(ownership, containerRow);
                    value = containerRow;
                }
                client.RemoveWritableValueAndDescendantsIfUnlinked(entryOwnership, entryValueId);
                return;
            }

            if (client.values.ContainsKey(entryValueId))
            {
                client.WriteRemovalTombstone(entryOwnership, entryValueId);
                return;
            }
            if (client.TryGetWritableValue(entryOwnership, entryValueId, out AttributeValue? _))
            {
                client.RemoveWritableValueAndDescendants(entryOwnership, entryValueId);
                return;
            }
            // The member lives in a lower overlay (e.g. session removal of a
            // save-created member): shadow it with a tombstone.
            client.WriteRemovalTombstone(entryOwnership, entryValueId);
        }

        private void StampContainerId(
            NeoValueOwnership entryOwnership,
            string entryValueId,
            string containerValueId)
        {
            if (!client.TryGetWritableValue(entryOwnership, entryValueId, out AttributeValue? row))
            {
                throw new System.InvalidOperationException(
                    $"Cannot stamp containerId on '{entryValueId}': the imported entry row is not present in the {entryOwnership} store.");
            }
            if (row.IsRemoved)
            {
                // Re-adding a previously-removed authored member resurrects it.
                if (!client.values.TryGetValue(entryValueId, out AttributeValue authored))
                {
                    throw new System.InvalidOperationException(
                        $"Cannot re-add '{entryValueId}' to unordered list '{containerValueId}': the row is tombstoned and has no authored backing to resurrect.");
                }
                if (authored.containerId == containerValueId)
                {
                    // The authored membership stamp suffices — drop the
                    // tombstone shadow so the authored member resurfaces.
                    client.RemoveWritableShadow(entryOwnership, entryValueId);
                    return;
                }
                var resurrected = client.CloneRowForWrite(authored);
                if (!string.IsNullOrEmpty(resurrected.containerId))
                {
                    throw new System.InvalidOperationException(
                        $"Value '{entryValueId}' already belongs to unordered list '{resurrected.containerId}'; containerId is immutable — remove and recreate to move it to '{containerValueId}'.");
                }
                resurrected.containerId = containerValueId;
                client.SetWritableValue(entryOwnership, resurrected);
                return;
            }
            if (row.containerId == containerValueId) return;
            if (!string.IsNullOrEmpty(row.containerId))
            {
                throw new System.InvalidOperationException(
                    $"Value '{entryValueId}' already belongs to unordered list '{row.containerId}'; containerId is immutable — remove and recreate to move it to '{containerValueId}'.");
            }
            row.containerId = containerValueId;
            client.SetWritableValue(entryOwnership, row);
        }

        /// <summary>
        /// Resolves the discriminator row an unordered Add joins against —
        /// no container write happens for an existing present list (the
        /// authored row's stable id is all membership needs). Mints + binds
        /// a fresh empty row when nothing is bound yet; throws when the list
        /// is explicitly null (adding requires a present instance).
        /// </summary>
        private ArrayAttributeValue ResolveUnorderedContainerForAdd(string nowIso)
        {
            var resolved = value ?? valueData;
            if (resolved is not null)
            {
                if (resolved.value is null)
                {
                    throw new System.InvalidOperationException(
                        $"Unordered list attribute '{attribute.id}' (value '{resolved.id}') is null; assign an empty list before adding entries.");
                }
                return resolved;
            }
            ArrayAttributeValue minted = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = System.Array.Empty<string>(),
            };
            BindNewValue(minted);
            return minted;
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
