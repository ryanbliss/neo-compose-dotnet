// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using NeoCompose.Runtime.Json;
using Member = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Wrapper for a List-typed member. Children are positionally
    /// indexed; each child is a <see cref="NeoMember"/> for the
    /// list's entry member (per
    /// <see cref="ListMember.entryMemberId"/>) bound to the
    /// value referenced from the list.
    /// </summary>
    public class NeoMemberList
        : NeoMember<ListMember, ArrayMemberValue>,
          IEnumerable<NeoMember>
    {
        protected Member entryMember;
        protected List<NeoMember> childMembers = new();
        private Dictionary<string, NeoMember>? childrenByValueId;
        private readonly Dictionary<string, NeoRawListIndex> derivedIndexes = new();

        /// <summary>
        /// Operation counters used by tests and development diagnostics to
        /// verify that warm index lookup does not regress to a linear scan.
        /// They deliberately count work rather than wall-clock time so the
        /// signal is stable across Mono, IL2CPP, and CI hardware.
        /// </summary>
        internal NeoListIndexDiagnostics IndexDiagnostics { get; } = new();

        /// <summary>
        /// The (stamp-substituted) entry member children are constructed
        /// from. Exposed for <see cref="NeoGenericBindings"/> so collection
        /// codecs can resolve entry codecs before any entry node exists.
        /// </summary>
        internal Member EntryMember => entryMember;

        /// <summary>
        /// True when the member declares <see cref="NeoListKind.Unordered"/>
        /// through its numeric <c>listKind</c> enum value:
        /// the stored value is only the null-vs-present discriminator
        /// (<c>null</c> or <c>[]</c>) and membership resolves by join over
        /// <see cref="MemberValue.containerId"/>, enumerated id-sorted.
        /// </summary>
        public bool IsUnordered { get; }

        public NeoMemberList(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership)
        {
            entryMember = ResolveEntryMember();
            IsUnordered = client.IsUnorderedList(member);
            ReinitializeChildren();
            client.OnValuePartitionChanged += HandleValuePartitionChanged;
        }

        public NeoMemberList(NeoClient client, ListMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership)
        {
            entryMember = ResolveEntryMember();
            IsUnordered = client.IsUnorderedList(member);
            ReinitializeChildren();
            client.OnValuePartitionChanged += HandleValuePartitionChanged;
        }

        protected virtual NeoMember CreateChild(
            NeoClient client,
            Member childMember,
            string? overrideValueId)
        {
            NeoValueOwnership? declared = client.DeclaredOwnership(childMember);
            NeoMember child =
                declared == NeoValueOwnership.Save || declared == NeoValueOwnership.Session
                    ? CreateWritable(client, childMember, overrideValueId, declared.Value)
                    : Create(client, childMember, overrideValueId);
            child.parent = this;
            return child;
        }

        public NeoMember this[int index] => childMembers[index];

        public int Count => childMembers.Count;

        internal NeoListChangedArgs? ActiveListChange { get; private set; }

        public IEnumerator<NeoMember> GetEnumerator() =>
            childMembers.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        protected override void Initialize(ArrayMemberValue value)
        {
            base.Initialize(value);
            // entryMember isn't set yet on the first base-ctor pass;
            // ReinitializeChildren runs after the derived ctor wires it.
        }

        protected override void OnValueIdChainChanged()
        {
            value = valueData;
            // The newly-bound row may carry a genericBindings stamp the
            // construction-time row lacked (or vice versa) — re-substitute
            // the entry member before re-walking children.
            entryMember = ResolveEntryMember();
            ReinitializeChildren();
            InvalidateAllIndexes();
            NotifyListChanged(NeoListChangedArgs.Unknown, applyToIndexes: false);
        }

        public override void Dispose()
        {
            if (isDisposed) return;
            client.OnValuePartitionChanged -= HandleValuePartitionChanged;
            foreach (var child in childMembers)
            {
                child.OnChanged -= HandleChildChanged;
                child.Dispose();
            }
            childMembers.Clear();
            childrenByValueId?.Clear();
            childrenByValueId = null;
            derivedIndexes.Clear();
            base.Dispose();
        }

        private void HandleValuePartitionChanged(string _)
        {
            if (isDisposed) return;
            // A partition may add/remove unordered members or rows below an
            // ordered member while keeping the same stable ids. Force fresh
            // child nodes so indexed fields cannot retain unloaded values.
            foreach (NeoMember child in childMembers)
            {
                child.OnChanged -= HandleChildChanged;
                child.Dispose();
            }
            childMembers = new List<NeoMember>();
            entryMember = ResolveEntryMember();
            ReinitializeChildren();
            InvalidateAllIndexes();
            NotifyListChanged(NeoListChangedArgs.Unknown, applyToIndexes: false);
        }

        /// <summary>
        /// Looks up a materialized child by its stable value id. The map is
        /// built lazily, then maintained with List membership changes.
        /// </summary>
        internal bool TryGetChildById(
            string valueId,
            [NotNullWhen(true)] out NeoMember? child)
        {
            if (valueId is null) throw new ArgumentNullException(nameof(valueId));
            EnsureIdentityIndex();
            IndexDiagnostics.IdentityLookupCount += 1;
            return childrenByValueId!.TryGetValue(valueId, out child);
        }

        internal bool ContainsValueId(string valueId)
        {
            if (valueId is null) throw new ArgumentNullException(nameof(valueId));
            EnsureIdentityIndex();
            IndexDiagnostics.IdentityLookupCount += 1;
            return childrenByValueId!.ContainsKey(valueId);
        }

        internal NeoRawListIndex GetDerivedIndex(string schemaKey, bool unique)
        {
            if (schemaKey is null) throw new ArgumentNullException(nameof(schemaKey));
            ListIndexDefinition definition = ResolveIndexDefinition(schemaKey);
            if (definition.Kind == NeoListIndexKind.Unique != unique)
            {
                throw new InvalidOperationException(
                    $"List index '{schemaKey}' on member '{member.id}' is declared "
                    + $"{(definition.Kind == NeoListIndexKind.Unique ? "unique" : "many")}, but the generated runtime view expects "
                    + $"{(unique ? "unique" : "many")}.");
            }
            if (!derivedIndexes.TryGetValue(schemaKey, out NeoRawListIndex? index))
            {
                index = new NeoRawListIndex(this, definition);
                derivedIndexes.Add(schemaKey, index);
            }
            return index;
        }

        private ListIndexDefinition ResolveIndexDefinition(string schemaKey)
        {
            if (member.indexes is not null)
            {
                foreach (ListIndexDefinition definition in member.indexes)
                {
                    if (definition is not null
                        && string.Equals(definition.schemaKey, schemaKey, StringComparison.Ordinal))
                    {
                        return definition;
                    }
                }
            }
            throw new KeyNotFoundException(
                $"List member '{member.id}' has no declared index named '{schemaKey}'.");
        }

        internal void EnsureIdentityIndex()
        {
            if (childrenByValueId is not null) return;
            var map = new Dictionary<string, NeoMember>(childMembers.Count, StringComparer.Ordinal);
            foreach (NeoMember child in childMembers)
            {
                string id = EntryValueId(child);
                IndexDiagnostics.IdentityBuildEntryCount += 1;
                if (!map.TryAdd(id, child))
                {
                    throw new InvalidOperationException(
                        $"List member '{member.id}' value '{value?.id ?? "<unbound>"}' "
                        + $"contains duplicate entry value id '{id}'.");
                }
            }
            childrenByValueId = map;
            IndexDiagnostics.IdentityBuildCount += 1;
        }

        internal string EntryValueId(NeoMember child)
        {
            string? id = child.overrideValueId ?? child.value?.id;
            if (string.IsNullOrEmpty(id))
            {
                throw new InvalidOperationException(
                    $"Entry of List member '{member.id}' has no stable value id.");
            }
            return id!;
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
                var row = client.ResolveValueRow(entryId);
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
            var previousChildren = childMembers;
            childMembers = new();
            var entryValueIds = ResolveEntryValueIds();
            if (value?.value is null)
            {
                foreach (var child in previousChildren)
                {
                    child.OnChanged -= HandleChildChanged;
                    child.Dispose();
                }
                return;
            }
            for (int i = 0; i < entryValueIds.Count; i++)
            {
                string entryValueId = entryValueIds[i];
                if (i < previousChildren.Count)
                {
                    var existing = previousChildren[i];
                    if (existing.member.id == entryMember.id
                        && (existing.overrideValueId == entryValueId
                            || existing.value?.id == entryValueId))
                    {
                        childMembers.Add(existing);
                        previousChildren[i] = null!;
                        continue;
                    }
                }
                NeoMember child = CreateChild(client, entryMember, entryValueId);
                child.OnChanged += HandleChildChanged;
                childMembers.Add(child);
                // Reconciliation may recreate a retained child at a new
                // ordinal (notably unordered id-sorted membership). Keep an
                // already-materialized identity map pointing at the live node;
                // genuinely-added ids are inserted from the subsequent
                // NeoListChangedArgs so duplicate detection remains central.
                if (childrenByValueId is not null
                    && childrenByValueId.ContainsKey(entryValueId))
                {
                    childrenByValueId[entryValueId] = child;
                }
            }
            foreach (var child in previousChildren)
            {
                if (child is not null)
                {
                    child.OnChanged -= HandleChildChanged;
                    child.Dispose();
                }
            }
        }

        protected void HandleChildChanged(NeoMember changed)
        {
            NeoMember? entry = changed;
            while (entry is not null && entry.parent != this)
            {
                entry = entry.parent;
            }
            if (entry is null)
            {
                InvalidateDerivedIndexes();
                NotifyListChanged(NeoListChangedArgs.Unknown, applyToIndexes: false);
                return;
            }
            NotifyListChanged(new NeoListChangedArgs(
                NeoListChangeKind.Set,
                replacedValueIds: new[] { EntryValueId(entry) }));
        }

        protected void NotifyListChanged(
            NeoListChangedArgs change,
            bool applyToIndexes = true)
        {
            if (applyToIndexes) ApplyListChangeToIndexes(change);
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

        private void ApplyListChangeToIndexes(NeoListChangedArgs change)
        {
            if (change.Kind == NeoListChangeKind.Unknown)
            {
                InvalidateAllIndexes();
                return;
            }

            if (childrenByValueId is not null)
            {
                foreach (string removedId in change.RemovedValueIds)
                {
                    childrenByValueId.Remove(removedId);
                }
                if (change.Kind == NeoListChangeKind.Clear)
                {
                    childrenByValueId.Clear();
                }
                foreach (string addedId in change.AddedValueIds)
                {
                    NeoMember child = FindChildByIdLinear(addedId);
                    if (!childrenByValueId.TryAdd(addedId, child))
                    {
                        throw new InvalidOperationException(
                            $"List member '{member.id}' contains duplicate entry value id '{addedId}'.");
                    }
                }
                foreach (string replacedId in change.ReplacedValueIds)
                {
                    childrenByValueId[replacedId] = FindChildByIdLinear(replacedId);
                }
            }

            foreach (NeoRawListIndex index in derivedIndexes.Values)
            {
                foreach (string removedId in change.RemovedValueIds)
                {
                    index.RemoveEntry(removedId);
                }
                if (change.Kind == NeoListChangeKind.Clear)
                {
                    index.Clear();
                }
                foreach (string addedId in change.AddedValueIds)
                {
                    index.UpdateEntry(addedId);
                }
                foreach (string replacedId in change.ReplacedValueIds)
                {
                    index.UpdateEntry(replacedId);
                }
            }

            if (change.Kind == NeoListChangeKind.Replace
                && change.RemovedValueIds.Count == 0
                && change.AddedValueIds.Count == 0
                && change.ReplacedValueIds.Count == 0)
            {
                InvalidateAllIndexes();
            }
        }

        private NeoMember FindChildByIdLinear(string valueId)
        {
            foreach (NeoMember child in childMembers)
            {
                if (string.Equals(EntryValueId(child), valueId, StringComparison.Ordinal))
                {
                    return child;
                }
            }
            throw new InvalidOperationException(
                $"List member '{member.id}' reported added entry '{valueId}', but no child is materialized for it.");
        }

        private void InvalidateDerivedIndexes()
        {
            foreach (NeoRawListIndex index in derivedIndexes.Values)
            {
                index.Invalidate();
            }
        }

        private void InvalidateAllIndexes()
        {
            childrenByValueId = null;
            InvalidateDerivedIndexes();
        }

        protected Member ResolveEntryMember()
        {
            if (!client.TryGetMember(member.entryMemberId, out Member? match))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(member.entryMemberId),
                    $"No member for {nameof(member)}.{nameof(member.entryMemberId)} {member.entryMemberId}");
            }
            // Entry substitution is lazy via the row's genericBindings stamp
            // (specs/class-generics.md Decision 9). A generic entry
            // subtree with no bound row yet keeps the raw record — no entry
            // can exist until a (stamped) row is bound, at which point
            // OnValueIdChainChanged re-substitutes.
            var stamp = value?.genericBindings;
            if (stamp is null) return match;
            return NeoGenericResolution.SubstituteMember(
                client,
                match,
                NeoGenericResolution.EnvFromStamp(stamp));
        }
    }

    public class NeoMemberListWritable : NeoMemberList
    {
        public NeoMemberListWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberListWritable(NeoClient client, ListMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership) { }

        protected override NeoMember CreateChild(
            NeoClient client,
            Member childMember,
            string? overrideValueId)
        {
            NeoValueOwnership? declared = client.DeclaredOwnership(childMember);
            NeoMember child = declared switch
            {
                NeoValueOwnership.Asset => Create(client, childMember, overrideValueId),
                NeoValueOwnership.Save or NeoValueOwnership.Session =>
                    CreateWritable(client, childMember, overrideValueId, declared.Value),
                _ => CreateWritable(client, childMember, overrideValueId, ownership),
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
            if (entryMember.Requirement == NeoMemberRequirementKind.Required && (entryValue is null || entryValue.isNull))
            {
                throw new System.ArgumentNullException(
                    nameof(entryValue),
                    "Cannot be null when entry member is required");
            }
            if (IsUnordered)
            {
                AddSerializedUnordered(entryValue);
                return;
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryMember) ?? ownership;
            ArrayMemberValue parentRow = EnsureWritableArray(nowIso);

            string newValueId;
            if (entryValue?.isValueReference == true)
            {
                newValueId = client.ImportValueReference(
                    entryOwnership,
                    entryValue.valueId!,
                    out bool sourceMoved);
                if (sourceMoved)
                {
                    entryValue.RetargetMovedReference(client, entryMember, newValueId, entryOwnership);
                }
            }
            else
            {
                newValueId = System.Guid.NewGuid().ToString();
                MemberValue newValueRow = MemberValueFactory.Create(
                    entryMember, entryValue?.value, newValueId, nowIso, nowIso);
                newValueRow.mapKey = client.ResolveCreatedValueMapKey(
                    entryMember,
                    parentRow.mapKey,
                    parentRow.classId);
                // A nested collection entry (e.g. List<List<T>>) carries its
                // own Decision-9 stamp, resolved through this row's stamp.
                NeoGenericResolution.StampGenericBindings(
                    client,
                    entryMember,
                    newValueRow,
                    NeoGenericResolution.EnvFromStamp(parentRow.genericBindings));
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
                    $"List member '{member.id}' is unordered; entries have no position to replace at. Remove the entry and add the replacement instead.");
            }
            if (entryMember.Requirement == NeoMemberRequirementKind.Required && (entryValue is null || entryValue.isNull))
            {
                throw new System.ArgumentNullException(
                    nameof(entryValue),
                    "Cannot be null when entry member is required");
            }
            if (value?.value is null || index < 0 || index >= value.value.Length)
            {
                throw new System.ArgumentOutOfRangeException(nameof(index));
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            string entryValueId = value.value[index];
            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryMember) ?? ownership;

            if (entryValue?.isValueReference == true)
            {
                string importedValueId = client.ImportValueReference(
                    entryOwnership,
                    entryValue.valueId!,
                    out bool sourceMoved,
                    entryValueId);
                if (importedValueId == entryValueId)
                {
                    return;
                }
                ArrayMemberValue parentRow = EnsureWritableArray(nowIso);
                parentRow.value![index] = importedValueId;
                parentRow.updatedAt = nowIso;
                client.SetWritableValue(ownership, parentRow);
                value = parentRow;
                client.RemoveWritableValueAndDescendantsIfUnlinked(
                    entryOwnership, entryValueId, entryMember);
                NeoMember previousChild = childMembers[index];
                previousChild.OnChanged -= HandleChildChanged;
                previousChild.Dispose();
                NeoMember replacementChild = CreateChild(
                    client, entryMember, importedValueId);
                replacementChild.OnChanged += HandleChildChanged;
                childMembers[index] = replacementChild;
                if (sourceMoved)
                {
                    entryValue.RetargetMovedReference(client, entryMember, importedValueId, entryOwnership);
                }
                NotifyListChanged(new NeoListChangedArgs(
                    NeoListChangeKind.Replace,
                    removedValueIds: new[] { entryValueId },
                    addedValueIds: new[] { importedValueId },
                    replacedValueIds: new[] { entryValueId }));
                return;
            }

            if (!client.TryGetValue(entryOwnership, entryValueId, out MemberValue? existing))
            {
                throw new System.InvalidOperationException(
                    $"List entry value '{entryValueId}' at index {index} not found");
            }
            MemberValue next = MemberValueFactory.Create(
                entryMember,
                entryValue?.value,
                entryValueId,
                existing.createdAt,
                nowIso);
            // A shadow of a stamped nested-collection entry keeps the
            // immutable stamp (spec Decision 9/16).
            next.genericBindings = existing.genericBindings;
            NeoGenericResolution.StampGenericBindings(
                client,
                entryMember,
                next,
                NeoGenericResolution.EnvFromStamp(value?.genericBindings));
            client.SetWritablePayloadRows(entryOwnership, entryValue?.value);
            client.SetWritableValue(entryOwnership, next);
            NeoMember replacedChild = childMembers[index];
            replacedChild.OnChanged -= HandleChildChanged;
            replacedChild.Dispose();
            NeoMember newChild = CreateChild(client, entryMember, entryValueId);
            newChild.OnChanged += HandleChildChanged;
            childMembers[index] = newChild;
            NotifyListChanged(new NeoListChangedArgs(
                NeoListChangeKind.Set,
                replacedValueIds: new[] { entryValueId }));
        }

        /// <summary>
        /// Removes the entry at <paramref name="index"/>. Clone-on-writes
        /// the list's own array row so the shrink shadows the authored
        /// default at its stable id, disposes the child
        /// <see cref="NeoMember"/> bound to that slot, and cascade-deletes
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
            ArrayMemberValue parentRow = EnsureWritableArray(nowIso);
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
            NeoMember removedChild = childMembers[index];
            removedChild.OnChanged -= HandleChildChanged;
            removedChild.Dispose();
            childMembers.RemoveAt(index);

            // GC the orphaned value graph from the writable store.
            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryMember) ?? ownership;
            client.RemoveWritableValueAndDescendantsIfUnlinked(
                entryOwnership, removedValueId, entryMember);
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
            ArrayMemberValue parentRow = EnsureWritableArray(nowIso);
            string[] removedValueIds = parentRow.value ?? System.Array.Empty<string>();
            if (removedValueIds.Length == 0)
            {
                return;
            }

            parentRow.value = System.Array.Empty<string>();
            parentRow.updatedAt = nowIso;
            client.SetWritableValue(ownership, parentRow);
            value = parentRow;

            foreach (var child in childMembers)
            {
                child.OnChanged -= HandleChildChanged;
                child.Dispose();
            }
            childMembers.Clear();

            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryMember) ?? ownership;
            foreach (var removedValueId in removedValueIds)
            {
                client.RemoveWritableValueAndDescendantsIfUnlinked(
                    entryOwnership,
                    removedValueId,
                    entryMember);
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
        public void Remove(NeoMember entry)
        {
            if (entry is null) throw new System.ArgumentNullException(nameof(entry));
            string? entryValueId = entry.value?.id;
            if (entryValueId is null)
            {
                throw new System.InvalidOperationException(
                    $"Cannot remove entry from list '{member.id}': the entry node has no bound value row.");
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
                    $"Value '{entryValueId}' is not an entry of list member '{member.id}'.");
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
                    $"Value '{entryValueId}' is not an entry of unordered list member '{member.id}'.");
            }

            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryMember) ?? ownership;
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
                client.DeclaredOwnership(entryMember) ?? ownership;
            ArrayMemberValue containerRow = ResolveUnorderedContainerForAdd(nowIso);
            string containerValueId = containerRow.id;
            // Storage partitions (spec §6): created member rows live in their
            // container's partition. The member row itself would inherit
            // through its containerId at the write chokepoint; payload rows
            // (owned children of the new member) have no containment edge, so
            // they are stamped here.
            string? partitionMapKey =
                client.ResolveValueRow(containerValueId)?.mapKey ?? containerRow.mapKey;
            if (partitionMapKey is not null && entryValue?.value is NeoValuePayload wrappedPayload)
            {
                foreach (var payloadRow in wrappedPayload.valueRows)
                {
                    if (!string.IsNullOrEmpty(payloadRow.mapKey)) continue;
                    payloadRow.mapKey = partitionMapKey;
                }
            }

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
                    entryValue.RetargetMovedReference(client, entryMember, newValueId, entryOwnership);
                }
            }
            else
            {
                newValueId = System.Guid.NewGuid().ToString();
                MemberValue newValueRow = MemberValueFactory.Create(
                    entryMember, entryValue?.value, newValueId, nowIso, nowIso);
                newValueRow.containerId = containerValueId;
                // Nested collection members carry their own Decision-9
                // stamp, resolved through the container row's stamp.
                NeoGenericResolution.StampGenericBindings(
                    client,
                    entryMember,
                    newValueRow,
                    NeoGenericResolution.EnvFromStamp(containerRow.genericBindings));
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
                client.DeclaredOwnership(entryMember) ?? ownership;

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
                ArrayMemberValue containerRow = EnsureWritableArray(nowIso);
                containerRow.value = System.Array.Empty<string>();
                containerRow.updatedAt = nowIso;
                client.SetWritableValue(ownership, containerRow);
                value = containerRow;
            }

            foreach (var child in childMembers)
            {
                child.OnChanged -= HandleChildChanged;
                child.Dispose();
            }
            childMembers.Clear();
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
                    $"AssignSerialized is the unordered whole-list path; ordered list member '{member.id}' assigns through SetSerialized/AddSerialized.");
            }
            ClearSerializedUnordered();
            string nowIso = System.DateTime.UtcNow.ToString("o");

            if (setValue is null || setValue.isNull)
            {
                // Setting an unordered list to null destroys the instance; the
                // members were tombstoned above, in the same write.
                ArrayMemberValue containerRow = EnsureWritableArray(nowIso);
                containerRow.value = null;
                containerRow.updatedAt = nowIso;
                client.SetWritableValue(ownership, containerRow);
                value = containerRow;
                ReinitializeChildren();
                NotifyListChanged(new NeoListChangedArgs(NeoListChangeKind.Replace));
                return;
            }

            // Present: null → [] is a fresh, empty instance.
            ArrayMemberValue presentRow = EnsureWritableArray(nowIso);
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
                    $"Unordered list member '{member.id}' whole-list assignment expects entry value ids (string[]), got '{setValue.value.GetType().Name}'.");
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
            var effectiveRow = client.ResolveValueRow(entryValueId);
            bool isJoinedMember = effectiveRow?.containerId == value?.id
                || (client.TryResolveContainerIdForValueId(entryValueId, out string? containerId)
                    && containerId == value?.id);
            if (!isJoinedMember)
            {
                // Legacy inline entry: drop it from the array shadow and GC.
                string nowIso = System.DateTime.UtcNow.ToString("o");
                ArrayMemberValue containerRow = EnsureWritableArray(nowIso);
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
                client.RemoveWritableValueAndDescendantsIfUnlinked(
                    entryOwnership, entryValueId, entryMember);
                return;
            }

            if (client.values.ContainsKey(entryValueId))
            {
                client.WriteRemovalTombstone(entryOwnership, entryValueId);
                return;
            }
            if (client.TryGetWritableValue(entryOwnership, entryValueId, out MemberValue? _))
            {
                client.RemoveWritableValueAndDescendants(
                    entryOwnership, entryValueId, entryMember);
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
            if (!client.TryGetWritableValue(entryOwnership, entryValueId, out MemberValue? row))
            {
                throw new System.InvalidOperationException(
                    $"Cannot stamp containerId on '{entryValueId}': the imported entry row is not present in the {entryOwnership} store.");
            }
            if (row.IsRemoved)
            {
                // Re-adding a previously-removed authored member resurrects it.
                if (!client.values.TryGetValue(entryValueId, out MemberValue authored))
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
        private ArrayMemberValue ResolveUnorderedContainerForAdd(string nowIso)
        {
            var resolved = value ?? valueData;
            if (resolved is not null)
            {
                if (resolved.value is null)
                {
                    throw new System.InvalidOperationException(
                        $"Unordered list member '{member.id}' (value '{resolved.id}') is null; assign an empty list before adding entries.");
                }
                return resolved;
            }
            ArrayMemberValue minted = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = System.Array.Empty<string>(),
            };
            // SDK-created rows carry the Decision-9 stamp from the wrapper
            // tree's enclosing context (the parent Class node's env or an
            // enclosing collection row's own stamp).
            NeoGenericResolution.StampGenericBindings(
                client, member, minted, NeoGenericResolution.ResolveContextEnv(parent));
            BindNewValue(minted);
            // The freshly-stamped row may close generic entry references
            // the construction-time (row-less) resolution left raw.
            entryMember = ResolveEntryMember();
            return minted;
        }

        /// <summary>
        /// Returns the list's own array row guaranteed writable (a
        /// clone-on-write shadow at the stable id), minting + binding a
        /// fresh empty array through the parent when nothing is bound yet.
        /// </summary>
        private ArrayMemberValue EnsureWritableArray(string nowIso)
        {
            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value ??= System.Array.Empty<string>();
                return writable;
            }
            ArrayMemberValue parentRow = new()
            {
                id = System.Guid.NewGuid().ToString(),
                createdAt = nowIso,
                updatedAt = nowIso,
                value = System.Array.Empty<string>(),
            };
            // SDK-created rows carry the Decision-9 stamp from the wrapper
            // tree's enclosing context (spec §9).
            NeoGenericResolution.StampGenericBindings(
                client, member, parentRow, NeoGenericResolution.ResolveContextEnv(parent));
            BindNewValue(parentRow);
            // The freshly-stamped row may close generic entry references
            // the construction-time (row-less) resolution left raw.
            entryMember = ResolveEntryMember();
            return parentRow;
        }
    }
}
