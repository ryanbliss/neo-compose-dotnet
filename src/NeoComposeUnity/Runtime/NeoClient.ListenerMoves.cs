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
            if (sessionChangeListeners.Count == 0 && saveData.changeListeners?.Count is not > 0
                && defaultChangeListeners.Count == 0 && copiedListenerRoots.Count == 0
                && authoredListenerRoots.Count == 0 && plan.ListenerEntries?.Count is not > 0)
                return;
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
                    plan.SetListenerMove(key, new(previous.Scope, previous.Root, destination));
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
                    plan.SetListenerMove(key, new(scope, origin, destination));
                    if (row is ObjectMemberValue { classId: not null } || declaration is ClassMember)
                        plan.AfterCommit(() =>
                    {
                        var move = plan.ListenerMoves![key];
                        if (move.Scope != scope)
                            RemoveListenerDefault(scope, id);
                        if (defaultChangeListeners.TryGetValue((move.Scope, id), out var current))
                            InstallListenerDefault(current.expansionId, move.Scope, id, current.members, move.Root);
                        else if (defaults.members is not null && TryGetValue(move.Scope, id, out _))
                            InstallListenerDefault(defaults.expansionId, move.Scope, id, defaults.members, move.Root);
                        RefreshListenerOwnerRegistration(move.Scope, move.Root, id);
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

        private void PrepareListenerMoves(NeoWritePlan plan)
        {
            if (plan.ListenerMoves is null)
                return;
            var roots = new Dictionary<(NeoValueOwnership scope, string id), string>();
            foreach (var key in plan.ListenerMoves.Keys.ToArray())
                PrepareListenerMove(plan, key.scope, key.id, roots);
        }

        private void PrepareListenerMove(NeoWritePlan plan, NeoValueOwnership scope, string ownerId,
            Dictionary<(NeoValueOwnership scope, string id), string>? roots = null)
        {
            if (!plan.FindListenerMove(scope, ownerId, out var key, out var move))
                return;
            MemberValue? owner = plan.Resolve(move.Target, ownerId);
            if (owner is null || owner.IsRemoved)
                return;
            string root = ListenerBindingRoot(ownerId, move.Target, roots);
            if (move.Scope == move.Target && move.Root == root)
                return;
            foreach (NeoValueOwnership lifetime in new[] { NeoValueOwnership.Save, NeoValueOwnership.Session })
            {
                // A Session entry remains Session even if its owner is adopted
                // into Save. Persisting it here would extend its lifetime.
                var before = Entry(lifetime, move.Scope, move.Root);
                if (before is null)
                    continue;
                if (Entry(lifetime, move.Target, root) is { Count: > 0 })
                    throw new NSGetterRuntimeError($"Listener owner '{ownerId}' already has wiring at destination '{root}'.");
                plan.SetListenerEntry(lifetime, root, ownerId, before, move.Target);
                plan.SetListenerEntry(lifetime, move.Root, ownerId, null, move.Scope);
            }
            plan.SetListenerMove(key, new(move.Target, root, move.Target));

            Dictionary<string, NeoDelegateValue[]>? Entry(NeoValueOwnership lifetime, NeoValueOwnership scope, string binding)
            {
                if (plan.ListenerEntries?.TryGetValue((lifetime, scope, binding, ownerId), out var staged) == true)
                    return staged;
                return (lifetime == NeoValueOwnership.Session
                    ? sessionChangeListeners.GetValueOrDefault((scope, binding))
                    : scope == NeoValueOwnership.Save ? saveData.changeListeners?.GetValueOrDefault(binding) : null)?.GetValueOrDefault(ownerId);
            }
        }
    }
}
