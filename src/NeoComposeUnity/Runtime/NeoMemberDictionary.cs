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
            return CreateOwnedChild(client, childMember, overrideValueId, writableFamily: false);
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

        protected override void RefreshValueIdChain()
        {
            base.RefreshValueIdChain();
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
            if (!BeginDisposeChildren()) return;
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
            return CreateOwnedChild(client, childMember, overrideValueId, writableFamily: true);
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
            if (entryMember.Requirement == NeoMemberRequirementKind.Required && (setValue is null || setValue.isNull))
                throw new System.ArgumentNullException(nameof(setValue), "Cannot be null when entry member is required");
            var plan = new NeoWritePlan(client);
            NeoTimestamp nowIso = NeoTimestamp.Now();
            NeoValueOwnership entryOwnership = client.ChildOwnership(entryMember, ownership);
            ObjectMemberValue parentRow = EnsureWritableObject(plan, nowIso);
            parentRow.value!.TryGetValue(key, out string? previousId);
            MemberValue? previous = previousId is null ? null : plan.Resolve(entryOwnership, previousId);
            string nextId;
            if (setValue?.isValueReference == true)
            {
                nextId = client.ImportValueReference(plan, entryOwnership, setValue.valueId!, out bool sourceMoved, previousId);
                if (nextId == previousId) return;
                if (sourceMoved)
                    plan.AfterCommit(() => setValue.RetargetMovedReference(client, entryMember, nextId, entryOwnership));
            }
            else
            {
                nextId = previous?.id ?? System.Guid.NewGuid().ToString();
                MemberValue next = MemberValueFactory.Create(entryMember, setValue?.value, nextId, previous?.createdAt ?? nowIso, nowIso);
                next.genericBindings = previous?.genericBindings;
                next.mapKey = previous?.mapKey ?? client.ResolveCreatedValueMapKey(entryMember, parentRow.mapKey, parentRow.classId);
                NeoGenericResolution.StampGenericBindings(client, entryMember, next, NeoGenericResolution.EnvFromStamp(parentRow.genericBindings));
                client.StageWritablePayloadRows(plan, entryOwnership, setValue?.value);
                client.StageInPlaceReplacement(plan, entryOwnership, next, entryMember);
            }
            parentRow.value[key] = nextId;
            parentRow.updatedAt = nowIso;
            plan.Set(ownership, parentRow);
            if (previousId is not null && previousId != nextId)
                client.StageUnlinkedRemovals(plan, entryOwnership, new[] { previousId }, entryMember);
            plan.Commit();
            value = parentRow;
            if (childMembers.TryGetValue(key, out NeoMember? previousChild)) previousChild.Dispose();
            childMembers[key] = CreateChild(client, entryMember, nextId);
            NotifyChanged();
        }

        public void Remove(string key)
        {
            if (value?.value is null) return;
            if (!value.value.ContainsKey(key)) return;
            NeoTimestamp nowIso = NeoTimestamp.Now();

            // Clone-on-write the dict row (shadowing the authored default at
            // its stable id) and drop the key.
            var plan = new NeoWritePlan(client);
            ObjectMemberValue parentRow = EnsureWritableObject(plan, nowIso);
            string removedValueId = parentRow.value![key];
            parentRow.value.Remove(key);
            parentRow.updatedAt = nowIso;
            plan.Set(ownership, parentRow);
            NeoValueOwnership entryOwnership = client.ChildOwnership(entryMember, ownership);
            client.StageUnlinkedRemovals(plan, entryOwnership, new[] { removedValueId }, entryMember);
            plan.Commit();
            value = parentRow;

            // Dispose the child node (recursive — its own Dispose
            // disposes any grandchildren) and drop our reference.
            if (childMembers.TryGetValue(key, out NeoMember? child))
            {
                child.Dispose();
                childMembers.Remove(key);
            }

            NotifyChanged();
        }

        internal override void BindChildValueId(NeoWritePlan plan, NeoMember child, string childValueId)
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
            NeoTimestamp nowIso = NeoTimestamp.Now();
            ObjectMemberValue parentRow = EnsureWritableObject(plan, nowIso);
            parentRow.value![key] = childValueId;
            parentRow.updatedAt = nowIso;
            plan.Set(ownership, parentRow);
            plan.AfterCommit(() =>
            {
                value = parentRow;
                ReinitializeChildren();
            });
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
        private ObjectMemberValue EnsureWritableObject(NeoTimestamp nowIso)
        {
            var plan = new NeoWritePlan(client);
            var row = EnsureWritableObject(plan, nowIso);
            if (plan.Rows.Count > 0) plan.Commit();
            return row;
        }

        private ObjectMemberValue EnsureWritableObject(NeoWritePlan plan, NeoTimestamp nowIso)
        {
            var writable = WritableCandidate(plan);
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
            BindNewValue(plan, parentRow);
            // The freshly-stamped row may close generic entry references
            // the construction-time (row-less) resolution left raw.
            plan.AfterCommit(() => entryMember = ResolveEntryMember());
            return parentRow;
        }
    }
}
