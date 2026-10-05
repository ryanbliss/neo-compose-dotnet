// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        internal enum ListenerCopyIntent
        {
            IndependentCopy, ConstructionProjection
        }

        // Copied baselines are separate from runtime subscriptions. Session-tier
        // entries survive adoption but never enter save serialization.
        private readonly Dictionary<(NeoValueOwnership scope, string id), NeoChangeListenerMap> copiedListenerRoots = new();
        private readonly Dictionary<(NeoValueOwnership scope, string id), (NeoValueOwnership scope, string id)> copiedListenerRootByOwner = new();
        private readonly Dictionary<(NeoValueOwnership scope, string id), List<(NeoValueOwnership scope, string id)>> copiedOwnersByRoot = new();
        private readonly HashSet<(NeoValueOwnership scope, string id)> dirtyCopiedListenerRoots = new();

        private Dictionary<string, NeoDelegateValue[]>? CopiedListenerBaseline(NeoValueOwnership scope, string ownerId,
            out bool adopted)
        {
            if (candidateReadPlan is { } plan)
                foreach (var pendingCarrier in plan.CopiedListenerCarriers(ownerId))
                    if (plan.Resolve(pendingCarrier.scope, pendingCarrier.id) is { copiedChangeListeners: not null } row
                        && (scope == pendingCarrier.scope || plan.Resolve(pendingCarrier.scope, ownerId) is null)
                        && row.copiedChangeListeners.TryGetValue(ownerId, out var pending))
                    {
                        adopted = row.copiedListenersAdopted;
                        return pending;
                    }
            RefreshCopiedListenerIndexes();
            adopted = false;
            if (!copiedListenerRootByOwner.TryGetValue((scope, ownerId), out var carrier))
                return null;
            if (candidateReadPlan?.Rows.ContainsKey(carrier) == true)
            {
                var pending = candidateReadPlan.Resolve(carrier.scope, carrier.id);
                adopted = pending?.copiedListenersAdopted == true;
                return pending?.copiedChangeListeners?.GetValueOrDefault(ownerId);
            }
            if (TryGetValue(carrier.scope, carrier.id, out MemberValue? carrierRow))
                adopted = carrierRow.copiedListenersAdopted;
            return copiedListenerRoots.GetValueOrDefault(carrier)?.GetValueOrDefault(ownerId);
        }

        private void IndexCopiedListeners(NeoValueOwnership scope, string rootId, NeoChangeListenerMap? map)
        {
            var key = (scope, rootId);
            bool previous = copiedListenerRoots.ContainsKey(key);
            if (map is { Count: > 0 })
                copiedListenerRoots[key] = map;
            else
                copiedListenerRoots.Remove(key);
            RefreshListenerSources();
            if (previous || map is { Count: > 0 })
            {
                dirtyCopiedListenerRoots.Add(key);
            }
        }

        private void RefreshCopiedListenerIndexes()
        {
            if (dirtyCopiedListenerRoots.Count == 0)
                return;
            var affected = new HashSet<(NeoValueOwnership scope, string id)>();
            // A commit publishes all rows before indexing their final residency.
            foreach (var root in dirtyCopiedListenerRoots)
            {
                if (copiedOwnersByRoot.Remove(root, out var previous))
                    foreach (var owner in previous)
                    {
                        affected.Add(owner);
                        if (copiedListenerRootByOwner.GetValueOrDefault(owner) == root)
                            copiedListenerRootByOwner.Remove(owner);
                    }
                if (!copiedListenerRoots.TryGetValue(root, out var map))
                    continue;
                var owners = new List<(NeoValueOwnership scope, string id)>(map.Count);
                foreach (string ownerId in map.Keys)
                {
                    var scope = root.scope;
                    if (!TryGetValue(scope, ownerId, out MemberValue? _)
                        && !TryGetValueOwnership(ownerId, out scope))
                        continue;
                    var key = (scope, ownerId);
                    copiedListenerRootByOwner[key] = root;
                    owners.Add(key);
                    affected.Add(key);
                }
                copiedOwnersByRoot[root] = owners;
            }
            // Registration reads copied baselines. Finish and drain the index
            // first so that those lookups cannot recursively refresh this work.
            dirtyCopiedListenerRoots.Clear();
            var bindings = new Dictionary<(NeoValueOwnership scope, string id), string>();
            foreach (var owner in affected)
                RefreshListenerOwnerRegistration(owner.scope, ListenerBindingRoot(owner.id, owner.scope, bindings), owner.id);
        }

        private void PrepareCopiedListeners(NeoWritePlan plan)
        {
            // Snapshot changed carriers because marking an adoption stages its row.
            var carriers = new List<(NeoValueOwnership scope, MemberValue row)>();
            foreach (var row in plan.Rows)
                if (row.Key.ownership == NeoValueOwnership.Save && row.Value is
                    {
                        IsRemoved: false,
                        copiedChangeListeners: not null, copiedListenersAdopted: false
                    })
                    carriers.Add((row.Key.ownership, row.Value));
            var roots = new Dictionary<(NeoValueOwnership scope, string id), string>();
            foreach (var carrier in carriers)
            {
                foreach (var entry in carrier.row.copiedChangeListeners!)
                {
                    if (plan.Resolve(carrier.scope, entry.Key) is not ObjectMemberValue { classId: not null } owner)
                        continue;
                    string rootId = ListenerBindingRoot(owner.id, carrier.scope, roots);
                    var key = (NeoValueOwnership.Save, carrier.scope, rootId, owner.id);
                    Dictionary<string, NeoDelegateValue[]>? pending = null;
                    bool edited = plan.ListenerEntries?.TryGetValue(key, out pending) == true;
                    var durable = pending is null ? new Dictionary<string, NeoDelegateValue[]>(StringComparer.Ordinal)
                        : new Dictionary<string, NeoDelegateValue[]>(pending, StringComparer.Ordinal);
                    foreach (var slot in entry.Value)
                    {
                        // An explicit subscription later in this plan wins over the copy.
                        if (edited && (pending is null || pending.ContainsKey(slot.Key)))
                            continue;
                        if (!TryGetMember(slot.Key, out Member? declaration))
                            continue;
                        Member member = ResolveOwnedMemberType(owner, null, owner.classId, declaration);
                        var observedLifetime = ChildOwnership(member, carrier.scope);
                        if (observedLifetime != NeoValueOwnership.Save)
                            continue;
                        var targets = new List<NeoDelegateValue>();
                        foreach (var target in slot.Value)
                            if (!IsSessionListenerTarget(target, observedLifetime))
                                targets.Add(target.PersistedCopy());
                        bool inherited = false;
                        foreach (var target in InheritedChangeListenerTargets(rootId, owner.id, slot.Key, carrier.scope, ignoreCopied: true))
                            if (!IsSessionListenerTarget(target, observedLifetime))
                            {
                                inherited = true;
                                break;
                            }
                        if (targets.Count > 0 || inherited)
                            durable[slot.Key] = targets.ToArray();
                    }
                    if (durable.Count > 0 || edited)
                        plan.SetListenerEntry(NeoValueOwnership.Save, rootId, owner.id, durable.Count == 0 ? null : durable, carrier.scope);
                }
                var adopted = CloneValueRow(carrier.row);
                adopted.copiedListenersAdopted = true;
                var rowKey = (carrier.scope, carrier.row.id);
                plan.Set(carrier.scope, adopted, plan.ChangedField(rowKey), plan.IsSilent(rowKey));
            }
        }

        private bool IsSessionListenerTarget(NeoDelegateValue target, NeoValueOwnership observedLifetime,
            NeoValueOwnership? receiverScope = null)
        {
            if (observedLifetime == NeoValueOwnership.Session)
                return true;
            if (target.valueId is not string receiver)
                return false;
            if (receiverScope is null && TryGetValueOwnership(receiver, out var residency))
                receiverScope = residency;
            return receiverScope == NeoValueOwnership.Session;
        }

        // Each repeated source subtree has its own correspondence. Receivers
        // resolve through the nearest occurrence before enclosing copies.
        private sealed class ClonedValueOccurrence
        {
            internal readonly Dictionary<(NeoValueOwnership scope, string id), (string id, Member? member)> Values = new();
            private readonly ClonedValueOccurrence? parent;
            internal ClonedValueOccurrence(ClonedValueOccurrence? parent) => this.parent = parent;
            internal bool TryResolve((NeoValueOwnership scope, string id) source, out string? id)
            {
                for (var occurrence = this; occurrence is not null; occurrence = occurrence.parent)
                    if (occurrence.Values.TryGetValue(source, out var copy))
                    {
                        id = copy.id;
                        return true;
                    }
                id = null;
                return false;
            }
        }

        private void CopyClonedListeners(NeoWritePlan plan, NeoValueOwnership targetOwnership,
            ClonedValueOccurrence copies, ListenerCopyIntent intent, NeoChangeListenerMap map,
            Dictionary<(NeoValueOwnership scope, string id), string> sourceRoots)
        {
            using (ReadCandidate(plan))
            {
                foreach (var copy in copies.Values)
                {
                    if (plan.Resolve(copy.Key.scope, copy.Key.id) is not ObjectMemberValue owner)
                        continue;
                    string? classId = owner.classId ?? plan.Resolve(targetOwnership, copy.Value.id)?.classId;
                    if (classId is null)
                        continue;
                    if (owner.classId is null)
                    {
                        owner = (ObjectMemberValue)CloneValueRow(owner);
                        owner.classId = classId;
                    }
                    string sourceRoot = ListenerBindingRoot(owner.id, copy.Key.scope, sourceRoots);
                    Dictionary<string, NeoDelegateValue[]>? durable = null;
                    if (copy.Key.scope != NeoValueOwnership.Session
                        && plan.ListenerEntries?.TryGetValue((NeoValueOwnership.Save, copy.Key.scope, sourceRoot, owner.id), out durable) != true)
                        durable = saveData.changeListeners?.GetValueOrDefault(sourceRoot)?.GetValueOrDefault(owner.id);
                    var construction = CopiedListenerBaseline(copy.Key.scope, owner.id, out bool adopted)
                        ?? (copy.Key.scope == NeoValueOwnership.Session ? PendingConstructionListeners(owner.id) : null);
                    var entry = new Dictionary<string, NeoDelegateValue[]>(StringComparer.Ordinal);
                    foreach (var slot in ResolveStoredInstanceSchema(owner.classId))
                    {
                        if (!TryGetMember(slot.memberId, out Member? declaration) || !IsChangeListenerMember(declaration))
                            continue;
                        var member = ResolveOwnedMemberType(owner, copy.Value.member, owner.classId, declaration);
                        var observedLifetime = ChildOwnership(member, copy.Key.scope);
                        if (observedLifetime == NeoValueOwnership.Asset)
                            continue;
                        string canonical = CanonicalListenerMemberId(member);
                        var baseline = adopted ? InheritedChangeListenerTargets(sourceRoot, owner.id, canonical, copy.Key.scope)
                            : construction?.GetValueOrDefault(canonical) ?? InheritedChangeListenerTargets(sourceRoot, owner.id, canonical, copy.Key.scope);
                        var targets = new List<NeoDelegateValue>();
                        NeoDelegateValue[]? durableTargets = null;
                        bool hasDurable = durable?.TryGetValue(canonical, out durableTargets) == true;
                        if (hasDurable)
                            targets.AddRange(durableTargets!);
                        foreach (var target in baseline)
                        {
                            bool session = observedLifetime == NeoValueOwnership.Session
                                || target.valueId is string receiver && TryGetValueOwnership(receiver, out var scope) && scope == NeoValueOwnership.Session;
                            if (!hasDurable || session)
                                targets.Add(target);
                        }
                        bool authoredSlot = data.values.GetValueOrDefault(sourceRoot)?.changeListeners?.GetValueOrDefault(owner.id)?.ContainsKey(canonical) == true;
                        if (targets.Count == 0 && !hasDurable && baseline.Length == 0
                            && construction?.ContainsKey(canonical) != true && !authoredSlot)
                            continue;
                        var remapped = new List<NeoDelegateValue>();
                        var seen = new HashSet<string>(StringComparer.Ordinal);
                        foreach (var target in targets)
                        {
                            var next = target.PersistedCopy();
                            if (next.valueId is string receiver)
                            {
                                if (TryGetValueOwnership(receiver, out var receiverScope) && copies.TryResolve((receiverScope, receiver), out string? receiverCopy))
                                    next.valueId = receiverCopy;
                                else if (intent == ListenerCopyIntent.IndependentCopy)
                                    continue;
                            }
                            if (seen.Add(NeoActionValue.ListenerIdentity(next)))
                                remapped.Add(next);
                        }
                        entry[canonical] = remapped.ToArray();
                    }
                    if (entry.Count > 0)
                        map[copy.Value.id] = entry;
                }
            }


        }
    }
}
