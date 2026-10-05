// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // Release supplies the old collection because its pending array has
        // already dropped the entry when this method runs.
        internal void CaptureListenerMove(NeoWritePlan plan, NeoValueOwnership source,
            NeoValueOwnership target, string valueId, Member? member, string? parentId = null)
        {
            if (!HasListenerMoveWork(plan))
                return;
            if (scriptWriteBatch is { IsStaging: false } batch)
            {
                batch.Stage(_ => CaptureListenerMove(plan, source, target, valueId, member, parentId));
                return;
            }
            EnsureListenerSlots();
            string root = parentId is not null && plan.FindListenerMove(source, parentId, out _, out var parentMove)
                ? parentMove.Root : ListenerBindingRoot(parentId ?? valueId, source);
            var visited = new HashSet<(NeoValueOwnership scope, string id)>();
            Visit(source, target, valueId, member);

            void Visit(NeoValueOwnership scope, NeoValueOwnership destination, string id, Member? declaration)
            {
                if (!visited.Add((scope, id)))
                    return;
                bool hasPrevious = plan.FindListenerMove(scope, id, out var key, out var previous);
                if (hasPrevious && previous.Target == destination)
                    return;
                MemberValue? row = plan.HeldBy?.PendingRow(scope, id) ?? plan.Resolve(scope, id);
                if (row is null || row.IsRemoved)
                    return;
                if (row is not ObjectMemberValue && row is not ArrayMemberValue)
                    return;
                if (!hasPrevious)
                    key = (scope, id);
                if (hasPrevious)
                    plan.SetListenerMove(key, new(previous.Scope, previous.Root, destination, previous.OwnerId, previous.TargetOwnerId));
                else
                {
                    defaultChangeListeners.TryGetValue((scope, id), out var defaults);
                    // Same-store adoption sees the prospective graph after its
                    // old edge was removed. Installed metadata still knows the
                    // binding from before that removal.
                    string origin = defaults.rootId ?? root;
                    if (listenerSlotsByOwner.TryGetValue((scope, id), out var slots) && slots.Count != 0)
                    {
                        using var enumerator = slots.Values.GetEnumerator();
                        if (enumerator.MoveNext())
                            origin = enumerator.Current.RootId;
                    }
                    plan.SetListenerMove(key, new(scope, origin, destination, id));
                    if (row is ObjectMemberValue { classId: not null } || declaration is ClassMember)
                        plan.AfterCommit(() =>
                    {
                        var move = plan.ListenerMoves![key];
                        string targetId = move.TargetOwnerId;
                        if (move.Scope != scope || targetId != id)
                            RemoveListenerDefault(scope, id);
                        var renames = ListenerRenames(plan);
                        if (defaultChangeListeners.TryGetValue((move.Scope, targetId), out var current))
                            InstallListenerDefault(current.expansionId, move.Scope, targetId, RemapListenerEntry(current.members, renames), move.Root);
                        else if (defaults.members is not null && TryGetValue(move.Scope, targetId, out _))
                            InstallListenerDefault(defaults.expansionId, move.Scope, targetId, RemapListenerEntry(defaults.members, renames), move.Root);
                        RefreshListenerOwnerRegistration(move.Scope, move.Root, targetId);
                    });
                }
                foreach (var child in EnumerateOwnedChildLinks(row, declaration))
                    Visit(ChildOwnership(child.member, scope), ChildOwnership(child.member, destination), child.valueId, child.member);
                if (row is ObjectMemberValue && TryResolveVirtualClassChildren(id, out var children))
                    foreach (var child in children)
                    {
                        Member? childMember = TryResolveOwnedChildMember(row, declaration, child.Key);
                        if (childMember is not null)
                            Visit(ChildOwnership(childMember, scope), ChildOwnership(childMember, destination), child.Value, childMember);
                    }
                if (declaration is ListMember list && IsUnorderedList(list)
                    && TryResolveCollectionEntryMember(list, row) is Member entry)
                {
                    var entries = new HashSet<string>(EnumerateContainerMemberValueIds(scope, id));
                    entries.UnionWith(plan.ContainerCandidates(id));
                    foreach (string child in entries)
                        Visit(ChildOwnership(entry, scope), ChildOwnership(entry, destination), child, entry);
                }
            }
        }

        private bool HasListenerMoveWork(NeoWritePlan plan) =>
            hasListenerSources || plan.ListenerEntries?.Count > 0;

        private static Dictionary<(NeoValueOwnership? scope, string id), string> ListenerRenames(NeoWritePlan plan) => plan.ListenerRenames();

        private static Dictionary<string, NeoDelegateValue[]> RemapListenerEntry(
            Dictionary<string, NeoDelegateValue[]> entry, Dictionary<(NeoValueOwnership? scope, string id), string> renames,
            bool durable = false)
        {
            if (renames.Count == 0)
                return entry;
            Dictionary<string, NeoDelegateValue[]>? changed = null;
            foreach (var slot in entry)
            {
                NeoDelegateValue[]? targets = null;
                for (int i = 0; i < slot.Value.Length; i++)
                    if (slot.Value[i].valueId is string receiver && renames.TryGetValue((durable ? NeoValueOwnership.Save : null, receiver), out string? next))
                    {
                        targets ??= (NeoDelegateValue[])slot.Value.Clone();
                        targets[i] = targets[i].PersistedCopy();
                        targets[i].valueId = next;
                    }
                if (targets is null)
                    continue;
                changed ??= new(entry, StringComparer.Ordinal);
                var seen = new HashSet<string>(StringComparer.Ordinal);
                changed[slot.Key] = targets.Where(target => seen.Add(NeoActionValue.ListenerIdentity(target))).ToArray();
            }
            return changed ?? entry;
        }

        private static NeoChangeListenerMap? RemapListenerMap(NeoChangeListenerMap? map,
            Dictionary<(NeoValueOwnership? scope, string id), string> renames, NeoValueOwnership ownerScope,
            Func<string, NeoValueOwnership>? resolveOwnerScope = null)
        {
            if (map is null)
                return null;
            NeoChangeListenerMap? result = null;
            foreach (var owner in map)
            {
                string id = renames.GetValueOrDefault((resolveOwnerScope?.Invoke(owner.Key) ?? ownerScope, owner.Key)) ?? owner.Key;
                var entry = RemapListenerEntry(owner.Value, renames);
                if (id == owner.Key && ReferenceEquals(entry, owner.Value))
                    continue;
                if (result is null)
                {
                    result = new();
                    foreach (var entryToCopy in map)
                        result.Add(entryToCopy.Key, entryToCopy.Value);
                }
                result.Remove(owner.Key);
                result[id] = entry;
            }
            return result ?? map;
        }

        private void CaptureShadowListenerRename(NeoWritePlan plan, NeoValueOwnership scope,
            string sourceId, MemberValue shadow)
        {
            if (!HasListenerMoveWork(plan) && shadow.changeListeners?.Count is not > 0
                && shadow.copiedChangeListeners?.Count is not > 0)
                return;
            if (!plan.FindListenerMove(scope, sourceId, out var key, out var move))
            {
                key = (scope, sourceId);
                move = new(scope, sourceId, scope, sourceId);
            }
            plan.SetListenerMove(key, new(move.Scope, move.Root, scope, move.OwnerId, shadow.id));
            var renames = new Dictionary<(NeoValueOwnership? scope, string id), string>
            {
                [(scope, sourceId)] = shadow.id,
                [(null, sourceId)] = shadow.id,
            };
            shadow.changeListeners = RemapListenerMap(shadow.changeListeners, renames, scope);
            shadow.copiedChangeListeners = RemapListenerMap(shadow.copiedChangeListeners, renames, scope);
        }

        private void RemapInheritedListenerReceivers(NeoWritePlan plan,
            Dictionary<(NeoValueOwnership? scope, string id), string> renames)
        {
            // Constructor defaults are instance metadata. Keep authored
            // declarations intact while following moved receiver instances.
            foreach (var owner in defaultChangeListeners)
            {
                if (plan.FindListenerMove(owner.Key.scope, owner.Key.ownerId, out _, out _))
                    continue;
                var remapped = RemapListenerEntry(owner.Value.members, renames);
                if (!ReferenceEquals(remapped, owner.Value.members))
                    plan.AfterCommit(() => InstallListenerDefault(owner.Value.expansionId,
                        owner.Key.scope, owner.Key.ownerId, remapped, owner.Value.rootId));
            }
            var carriers = new HashSet<(NeoValueOwnership scope, string id)>(copiedListenerRoots.Keys);
            foreach (var row in plan.Rows)
                if (row.Value?.copiedChangeListeners is not null)
                    carriers.Add(row.Key);
            foreach (var key in carriers)
            {
                if (plan.Resolve(key.scope, key.id) is not { copiedChangeListeners: not null } current)
                    continue;
                var remapped = RemapListenerMap(current.copiedChangeListeners, renames, key.scope,
                    ownerId => CopiedListenerOwnerScope(key, ownerId));
                if (ReferenceEquals(remapped, current.copiedChangeListeners))
                    continue;
                MemberValue next = CloneValueRow(current);
                next.copiedChangeListeners = remapped;
                bool staged = plan.Rows.ContainsKey(key);
                plan.Set(key.scope, next, plan.ChangedField(key), !staged || plan.IsSilent(key));
            }
        }

        private NeoValueOwnership CopiedListenerOwnerScope((NeoValueOwnership scope, string id) carrier, string ownerId)
        {
            // The installed index records mixed-tier owners before a move hides
            // their old rows from the prospective graph.
            if (copiedListenerRootByOwner.TryGetValue((carrier.scope, ownerId), out var root) && root == carrier)
                return carrier.scope;
            var other = carrier.scope == NeoValueOwnership.Session ? NeoValueOwnership.Save : NeoValueOwnership.Session;
            if (copiedListenerRootByOwner.TryGetValue((other, ownerId), out root) && root == carrier)
                return other;
            if (TryGetCommittedValue(carrier.scope, ownerId, out _))
                return carrier.scope;
            return TryGetCommittedOwnership(ownerId, out var scope) ? scope : carrier.scope;
        }

        private void RemapInboundListenerEntries(NeoWritePlan plan, Dictionary<(NeoValueOwnership? scope, string id), string> renames)
        {
            var entries = new Dictionary<(NeoValueOwnership lifetime, NeoValueOwnership scope, string root, string owner), Dictionary<string, NeoDelegateValue[]>?>();
            if (saveData.changeListeners is not null)
                foreach (var root in saveData.changeListeners)
                    foreach (var owner in root.Value)
                        entries[(NeoValueOwnership.Save, NeoValueOwnership.Save, root.Key, owner.Key)] = owner.Value;
            foreach (var root in sessionChangeListeners)
                foreach (var owner in root.Value)
                    entries[(NeoValueOwnership.Session, root.Key.scope, root.Key.rootId, owner.Key)] = owner.Value;
            if (plan.ListenerEntries is not null)
                foreach (var entry in plan.ListenerEntries)
                    entries[entry.Key] = entry.Value;
            foreach (var entry in entries)
            {
                if (entry.Value is null)
                    continue;
                var remapped = RemapListenerEntry(entry.Value, renames, entry.Key.lifetime == NeoValueOwnership.Save);
                if (!ReferenceEquals(remapped, entry.Value))
                    plan.SetListenerEntry(entry.Key.lifetime, entry.Key.root, entry.Key.owner, remapped, entry.Key.scope);
            }
        }

        private void PrepareListenerMoves(NeoWritePlan plan)
        {
            if (plan.ListenerMoves is null)
                return;
            PrepareVirtualListenerRenames(plan);
            var roots = new Dictionary<(NeoValueOwnership scope, string id), string>();
            var renames = ListenerRenames(plan);
            foreach (var key in plan.ListenerMoves.Keys.ToArray())
                PrepareListenerMove(plan, key.scope, key.id, roots, renames);
            if (renames.Count != 0)
            {
                RemapInboundListenerEntries(plan, renames);
                RemapInheritedListenerReceivers(plan, renames);
            }
        }

        private void PrepareVirtualListenerRenames(NeoWritePlan plan)
        {
            if (candidateReplay is null || plan.ListenerMoves is null || ListenerRenames(plan).Count == 0)
                return;
            var pending = new Queue<(string before, string after)>();
            var byRoot = new Dictionary<string, List<(NeoValueOwnership scope, string id)>>();
            foreach (var move in plan.ListenerMoves)
            {
                if (move.Key.id != move.Value.TargetOwnerId)
                    pending.Enqueue((move.Key.id, move.Value.TargetOwnerId));
                if (!virtualClassPlacementByChildId.TryGetValue(move.Key.id, out var placement))
                    continue;
                if (!byRoot.TryGetValue(placement.rootId, out var children))
                    byRoot[placement.rootId] = children = new();
                children.Add(move.Key);
            }
            var visited = new HashSet<string>();
            while (pending.Count != 0)
            {
                var root = pending.Dequeue();
                if (!visited.Add(root.before) || !byRoot.TryGetValue(root.before, out var children)
                    || !candidateReplay.Expansions.TryGetValue(root.after, out var expansion))
                    continue;
                // Paths identify occurrences within one construction frame.
                // Nested frames are joined only after their root is mapped.
                var paths = new Dictionary<string, (string id, VirtualClassPlacement placement)>();
                foreach (var placement in expansion.Placements)
                    paths[placement.Value.path] = (placement.Key, placement.Value);
                foreach (var key in children)
                {
                    var before = virtualClassPlacementByChildId[key.id];
                    var move = plan.ListenerMoves[key];
                    if (!paths.TryGetValue(before.path, out var after)
                        || before.member.id != after.placement.member.id
                        || move.Target != after.placement.ownership)
                        continue;
                    plan.SetListenerMove(key, new(move.Scope, move.Root, move.Target, move.OwnerId, after.id));
                    pending.Enqueue((key.id, after.id));
                }
            }
        }

        private void PrepareListenerMove(NeoWritePlan plan, NeoValueOwnership scope, string ownerId,
            Dictionary<(NeoValueOwnership scope, string id), string>? roots = null,
            Dictionary<(NeoValueOwnership? scope, string id), string>? renames = null)
        {
            if (!plan.FindListenerMove(scope, ownerId, out var key, out var move))
                return;
            MemberValue? owner = plan.Resolve(move.Target, move.TargetOwnerId);
            if (owner is null || owner.IsRemoved)
                return;
            string root = ListenerBindingRoot(move.TargetOwnerId, move.Target, roots);
            if (move.Scope == move.Target && move.Root == root && move.OwnerId == move.TargetOwnerId)
                return;
            foreach (NeoValueOwnership lifetime in new[] { NeoValueOwnership.Save, NeoValueOwnership.Session })
            {
                // A Session entry remains Session even if its owner is adopted
                // into Save. Persisting it here would extend its lifetime.
                var before = Entry(lifetime, move.Scope, move.Root);
                if (before is null)
                    continue;
                if (Entry(lifetime, move.Target, root, move.TargetOwnerId) is { Count: > 0 })
                    throw new NSGetterRuntimeError($"Listener owner '{ownerId}' already has wiring at destination '{root}'.");
                plan.SetListenerEntry(lifetime, root, move.TargetOwnerId, RemapListenerEntry(before, renames ?? ListenerRenames(plan), lifetime == NeoValueOwnership.Save), move.Target);
                plan.SetListenerEntry(lifetime, move.Root, move.OwnerId, null, move.Scope);
            }
            plan.SetListenerMove(key, new(move.Target, root, move.Target, move.TargetOwnerId));

            Dictionary<string, NeoDelegateValue[]>? Entry(NeoValueOwnership lifetime, NeoValueOwnership scope, string binding, string? entryOwnerId = null)
            {
                string entryId = entryOwnerId ?? move.OwnerId;
                if (plan.ListenerEntries?.TryGetValue((lifetime, scope, binding, entryId), out var staged) == true)
                    return staged;
                return (lifetime == NeoValueOwnership.Session
                    ? sessionChangeListeners.GetValueOrDefault((scope, binding))
                    : scope == NeoValueOwnership.Save ? saveData.changeListeners?.GetValueOrDefault(binding) : null)?.GetValueOrDefault(entryId);
            }
        }
    }
}
