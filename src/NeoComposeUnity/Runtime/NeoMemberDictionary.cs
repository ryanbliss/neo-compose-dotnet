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
    /// Wrapper for a Dictionary-typed member. Children are keyed by
    /// user-set strings; each child is a <see cref="NeoMember"/>
    /// for the entry member (per
    /// <see cref="DictionaryMember.entryMemberId"/>) bound to
    /// the value referenced from the dict.
    /// </summary>
    public class NeoMemberDictionary
        : NeoMember<DictionaryMember, ObjectMemberValue>,
          IEnumerable<KeyValuePair<string, NeoMember>>
    {
        protected Member entryMember;
        protected Dictionary<string, NeoMember> childMembers = new();

        /// <summary>
        /// The (stamp-substituted) entry member children are constructed
        /// from. Exposed for <see cref="NeoGenericBindings"/> so collection
        /// codecs can resolve entry codecs before any entry node exists.
        /// </summary>
        internal Member EntryMember => entryMember;

        public NeoMemberDictionary(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership)
        {
            entryMember = ResolveEntryMember();
            ReinitializeChildren();
        }

        public NeoMemberDictionary(NeoClient client, DictionaryMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, member, overrideValueId, ownership)
        {
            entryMember = ResolveEntryMember();
            ReinitializeChildren();
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

        public NeoMember this[string key] => childMembers[key];

        public int Count => childMembers.Count;

        public bool ContainsKey(string key) => childMembers.ContainsKey(key);

        public bool TryGet<TNeoMember>(string key, [NotNullWhen(true)] out TNeoMember? outMember)
            where TNeoMember : NeoMember
        {
            if (childMembers.TryGetValue(key, out NeoMember? check) && check is TNeoMember match)
            {
                outMember = match;
                return true;
            }
            outMember = null;
            return false;
        }

        public IEnumerator<KeyValuePair<string, NeoMember>> GetEnumerator() =>
            childMembers.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        protected override void Initialize(ObjectMemberValue value)
        {
            base.Initialize(value);
            // entryMember isn't set yet on the first base-ctor pass;
            // ReinitializeChildren runs after the derived ctor wires it.
        }

        protected override void OnValueIdChainChanged()
        {
            base.OnValueIdChainChanged();
            // The newly-bound row may carry a genericBindings stamp the
            // construction-time row lacked — re-substitute the entry
            // member before re-walking children.
            entryMember = ResolveEntryMember();
            // The new bound value may have a different keyset — re-walk
            // children so disposed-orphans get released and new keys
            // get nodes.
            ReinitializeChildren();
        }

        public override void Dispose()
        {
            if (isDisposed) return;
            foreach (var child in childMembers.Values) child.Dispose();
            childMembers.Clear();
            base.Dispose();
        }

        protected void ReinitializeChildren()
        {
            var previousChildren = childMembers;
            childMembers = new();
            if (value?.value is null)
            {
                foreach (var child in previousChildren.Values) child.Dispose();
                return;
            }
            foreach (var kvp in value.value)
            {
                if (previousChildren.TryGetValue(kvp.Key, out NeoMember? existing)
                    && existing.member.id == entryMember.id
                    && (existing.overrideValueId == kvp.Value
                        || existing.value?.id == kvp.Value))
                {
                    childMembers[kvp.Key] = existing;
                    previousChildren.Remove(kvp.Key);
                    continue;
                }
                childMembers[kvp.Key] = CreateChild(client, entryMember, kvp.Value);
            }
            foreach (var child in previousChildren.Values) child.Dispose();
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

    public class NeoMemberDictionaryWritable : NeoMemberDictionary
    {
        public NeoMemberDictionaryWritable(NeoClient client, string memberId, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
            : base(client, memberId, overrideValueId, ownership) { }

        public NeoMemberDictionaryWritable(NeoClient client, DictionaryMember member, string? overrideValueId, NeoValueOwnership ownership = NeoValueOwnership.Asset)
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
        /// Sets the dictionary entry under <paramref name="key"/>.
        /// Updates an existing entry in place; otherwise creates a
        /// fresh entry value, links it under the parent's value-map,
        /// and re-saves the parent. If the parent itself has no
        /// stored value yet, materialises one first.
        /// </summary>
        internal void SetSerialized(string key, NeoValueWritePayload? setValue)
        {
            if (entryMember.required && (setValue is null || setValue.isNull))
            {
                throw new System.ArgumentNullException(
                    nameof(setValue),
                    $"Cannot be null when entry member is required");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryMember) ?? ownership;

            if (value?.value is not null
                && value.value.TryGetValue(key, out string existingValueId)
                && client.TryGetValue(entryOwnership, existingValueId, out MemberValue? existing))
            {
                if (setValue?.isValueReference == true)
                {
                    string importedValueId = client.ImportValueReference(
                        entryOwnership,
                        setValue.valueId!,
                        out bool sourceMoved,
                        existingValueId);
                    if (importedValueId == existingValueId)
                    {
                        return;
                    }
                    ObjectMemberValue parentRow = EnsureWritableObject(nowIso);
                    parentRow.value![key] = importedValueId;
                    parentRow.updatedAt = nowIso;
                    client.SetWritableValue(ownership, parentRow);
                    value = parentRow;
                    client.RemoveWritableValueAndDescendantsIfUnlinked(
                        entryOwnership, existingValueId, entryMember);
                    if (childMembers.TryGetValue(key, out NeoMember? linkedOldChild))
                    {
                        linkedOldChild.Dispose();
                    }
                    childMembers[key] = CreateChild(client, entryMember, importedValueId);
                    if (sourceMoved)
                    {
                        setValue.RetargetMovedReference(client, entryMember, importedValueId, entryOwnership);
                    }
                    NotifyChanged();
                    return;
                }
                // Reuse the entry's stable id: clone-on-write the entry row
                // (shadowing the authored default) and overwrite its content.
                MemberValue next = MemberValueFactory.Create(
                    entryMember,
                    setValue?.value,
                    existingValueId,
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
                client.SetWritablePayloadRows(entryOwnership, setValue?.value);
                client.SetWritableValue(entryOwnership, next);
                if (childMembers.TryGetValue(key, out NeoMember? oldChild))
                {
                    oldChild.Dispose();
                }
                childMembers[key] = CreateChild(client, entryMember, existingValueId);
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
                    setValue.RetargetMovedReference(client, entryMember, newValueId, entryOwnership);
                }
            }
            else
            {
                newValueId = System.Guid.NewGuid().ToString();
                MemberValue newValueRow = MemberValueFactory.Create(
                    entryMember, setValue?.value, newValueId, nowIso, nowIso);
                newValueRow.mapKey = client.ResolveCreatedValueMapKey(
                    entryMember,
                    value?.mapKey,
                    value?.classId);
                // A nested collection entry (e.g. Dictionary<string, List<T>>)
                // carries its own Decision-9 stamp, resolved through this
                // row's stamp.
                NeoGenericResolution.StampGenericBindings(
                    client,
                    entryMember,
                    newValueRow,
                    NeoGenericResolution.EnvFromStamp(value?.genericBindings));
                client.SetWritablePayloadRows(entryOwnership, setValue?.value);
                client.SetWritableValue(entryOwnership, newValueRow);
            }

            ObjectMemberValue keyedRow = EnsureWritableObject(nowIso);
            keyedRow.value![key] = newValueId;
            keyedRow.updatedAt = nowIso;
            client.SetWritableValue(ownership, keyedRow);
            value = keyedRow;

            childMembers[key] = CreateChild(client, entryMember, newValueId);
            NotifyChanged();
        }

        public void Remove(string key)
        {
            if (value?.value is null) return;
            if (!value.value.ContainsKey(key)) return;
            string nowIso = System.DateTime.UtcNow.ToString("o");

            // Clone-on-write the dict row (shadowing the authored default at
            // its stable id) and drop the key.
            ObjectMemberValue parentRow = EnsureWritableObject(nowIso);
            string removedValueId = parentRow.value![key];
            parentRow.value.Remove(key);
            parentRow.updatedAt = nowIso;
            client.SetWritableValue(ownership, parentRow);
            value = parentRow;

            // Dispose the child node (recursive — its own Dispose
            // disposes any grandchildren) and drop our reference.
            if (childMembers.TryGetValue(key, out NeoMember? child))
            {
                child.Dispose();
                childMembers.Remove(key);
            }

            // GC the orphaned value graph from the writable store. The
            // removed valueId may itself reference more child values
            // (e.g., the entry was a Class record); the cascade walks them.
            NeoValueOwnership entryOwnership =
                client.DeclaredOwnership(entryMember) ?? ownership;
            client.RemoveWritableValueAndDescendantsIfUnlinked(
                entryOwnership, removedValueId, entryMember);
            NotifyChanged();
        }

        internal override void BindChildValueId(NeoMember child, string childValueId)
        {
            string? key = null;
            foreach (var pair in childMembers)
            {
                if (ReferenceEquals(pair.Value, child)) { key = pair.Key; break; }
            }
            if (key is null)
            {
                throw new System.InvalidOperationException(
                    $"Cannot bind a child value on Dictionary '{member.id}': child is not a registered entry.");
            }
            string nowIso = System.DateTime.UtcNow.ToString("o");
            ObjectMemberValue parentRow = EnsureWritableObject(nowIso);
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
        private ObjectMemberValue EnsureWritableObject(string nowIso)
        {
            var writable = EnsureWritableValue();
            if (writable is not null)
            {
                writable.value ??= new Dictionary<string, string>();
                return writable;
            }
            ObjectMemberValue parentRow = new()
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
                client, member, parentRow, NeoGenericResolution.ResolveContextEnv(parent));
            BindNewValue(parentRow);
            // The freshly-stamped row may close generic entry references
            // the construction-time (row-less) resolution left raw.
            entryMember = ResolveEntryMember();
            return parentRow;
        }
    }
}
