// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private CandidateReplay? candidateReplay;
        internal MemberValue? ReplayAllocation(string id) =>
            candidateReplay is not null && candidateReplay.Allocations.TryGetValue(id, out MemberValue? row) ? row : null;

        private CandidateReplay? PrepareCandidateExpansions(NeoWritePlan plan)
        {
            if (!virtualInstanceReplayReady || isReplayingVirtualInstance) return null;
            CandidateReplay? candidate = null;
            var pending = new Queue<string>(plan.Rows.Keys.Select(key => key.id));
            foreach (var binding in plan.Bindings)
            {
                pending.Enqueue($"static:{binding.Key.ownership}:{binding.Key.memberId}");
                if (binding.Value.valueId is string next) pending.Enqueue(next);
                if (GetWritableStore(binding.Key.ownership).staticBindings.TryGetValue(binding.Key.memberId, out string? old) && old is not null)
                    pending.Enqueue(old);
            }
            var visited = new HashSet<string>();
            using (ReadCandidate(plan))
            {
                while (pending.Count != 0)
                {
                    string id = pending.Dequeue();
                    if (!visited.Add(id)) continue;
                    if (!CanReuseVirtualExpansionForLocalOverlay(plan, id, out bool unchanged))
                    {
                        if (ResolveValueRow(id) is ObjectMemberValue row && IsStoredClassDefaultRoot(row)) AddRoot(id);
                        if (virtualFootprintByRoot.ContainsKey(id)) AddRoot(id);
                        if (virtualRootByFootprintId.TryGetValue(id, out string? root)) AddRoot(root);
                    }
                    if (unchanged) continue;
                    if (constructorArgumentRootsByValueId.TryGetValue(id, out var dependencies))
                        foreach (string dependent in dependencies) AddRoot(dependent);
                }
            }
            if (candidate is null) return null;
            foreach (var write in plan.Rows)
                if (write.Value is ObjectMemberValue next
                    && TryGetCommittedOverlaidValue(write.Key.ownership, write.Key.id, out ObjectMemberValue? previous)
                    && (next.classId != previous.classId
                        || next.instanceVariantId != previous.instanceVariantId
                        || next.instanceVariantRowValueId != previous.instanceVariantRowValueId
                        || next.instanceConstructorId != previous.instanceConstructorId))
                    candidate.ReplacedConstructionRoots.Add(write.Key.id);
            return candidate;

            void AddRoot(string id)
            {
                candidate ??= new CandidateReplay(this, plan);
                if (!candidate.AffectedRoots.Add(id)) return;
                pending.Enqueue(id);
                if (virtualValueIdsByRoot.TryGetValue(id, out var values)) candidate.HiddenVirtualIds.UnionWith(values);
                if (virtualFootprintByRoot.TryGetValue(id, out var footprint))
                    foreach (string valueId in footprint) pending.Enqueue(valueId);
            }
        }

        private bool CanReuseVirtualExpansionForLocalOverlay(NeoWritePlan plan, string id, out bool unchanged)
        {
            unchanged = false;
            if (CurrentChangeSource == NeoChangeSource.External
                || !(plan.Rows.ContainsKey((NeoValueOwnership.Save, id))
                    || plan.Rows.ContainsKey((NeoValueOwnership.Session, id)))
                || plan.Resolve(id) is not MemberValue next
                || !TryGetCommittedValue(id, out MemberValue? previous)
                || !TryGetCommittedOwnership(id, out NeoValueOwnership oldOwnership)
                || !plan.TryGetOwnership(id, out NeoValueOwnership nextOwnership)
                || next.GetType() != previous.GetType()
                || next.IsRemoved || previous.IsRemoved
                || next.classId != previous.classId
                || next.containerId != previous.containerId
                || next.mapKey != previous.mapKey) return false;

            // A stored leaf shadows its cached default directly. It cannot
            // retire an owned default subtree. Actual constructor reads still
            // invalidate through constructorArgumentRootsByValueId below.
            if (next is NumberMemberValue or StringMemberValue or BoolMemberValue
                or Vector2MemberValue or Vector3MemberValue or ColorMemberValue
                or FileMemberValue or SpriteMemberValue or NullMemberValue
                or DelegateMemberValue or ActionMemberValue) return true;

            if (next is ArrayMemberValue && TryInferMemberForValueId(id, out Member? selectionMember)
                && selectionMember is EnumMember or LookupMember or DialogueLookupMember) return true;

            // A first leaf overlay changes ownership without changing the
            // containing graph. Structural reuse still requires the same store.
            if (oldOwnership != nextOwnership) return false;

            // Compound setters may include a parent whose field links and
            // construction recipe did not change alongside the changed leaf.
            if (next is ObjectMemberValue { classId: not null }
                && NeoSemanticJson.MemberRowsEqual(previous, next))
            {
                unchanged = true;
                return true;
            }

            // Adding collection entries preserves every existing default edge.
            // Replacements, removals, null collections and class field maps
            // retain replay so superseded virtual subtrees are retired.
            if (next is ObjectMemberValue { classId: null, value: not null } dictionary
                && previous is ObjectMemberValue { classId: null, value: not null } oldDictionary
                && TryInferMemberForValueId(id, out Member? member) && member is DictionaryMember)
                return oldDictionary.value.All(pair => dictionary.value.TryGetValue(pair.Key, out string? valueId)
                    && valueId == pair.Value);
            if (next is ArrayMemberValue { value: not null } list
                && previous is ArrayMemberValue { value: not null } oldList
                && TryInferMemberForValueId(id, out Member? listMember) && listMember is ListMember)
                return list.value.Length >= oldList.value.Length
                    && oldList.value.SequenceEqual(list.value.Take(oldList.value.Length));
            return false;
        }

        private void PrepareCandidateRoot(string rootId)
        {
            CandidateReplay candidate = candidateReplay!;
            if (candidate.RetainedRoots.Contains(rootId) || candidate.Expansions.ContainsKey(rootId)) return;
            if (virtualRootByFootprintId.TryGetValue(rootId, out string? ownerRoot)
                && ownerRoot != rootId && candidate.AffectedRoots.Contains(ownerRoot)
                && !replayingVirtualRootIds.Contains(ownerRoot))
                PrepareCandidateRoot(ownerRoot);
            if (ResolveValueRow(rootId) is not ObjectMemberValue root || !IsStoredClassDefaultRoot(root)) return;
            // A materialized node inside a sparse root is overlaid by that
            // root's expansion. Replaying it again as a separate default root
            // would replace stable descendant paths with a new id namespace.
            if (!IsVirtualInstanceRoot(root)
                && virtualRootByFootprintId.TryGetValue(rootId, out string? containingRoot)
                && containingRoot != rootId && candidate.AffectedRoots.Contains(containingRoot)) return;
            // A constructor can return fully materialized nested instances.
            // If this child has never owned a separate expansion, the enclosing
            // replay already constructed it and all its declaration defaults.
            if (!virtualFootprintByRoot.ContainsKey(rootId)
                && virtualRootByFootprintId.TryGetValue(rootId, out var enclosing)
                && enclosing != rootId
                && (candidate.RetainedRoots.Contains(enclosing)
                    || candidate.Values.ContainsKey(rootId) && candidate.Expansions.ContainsKey(enclosing))) return;
            if (!IsVirtualInstanceRoot(root)
                && (!TryInferMemberForValueId(rootId, out Member? member)
                    || member is not ClassMember placement || placement.Payload == NeoMemberPayloadKind.Partial)) return;
            if (CanRetainCandidateRoot(candidate, root))
            {
                candidate.RetainedRoots.Add(rootId);
                if (virtualValueIdsByRoot.TryGetValue(rootId, out var retainedIds))
                    candidate.HiddenVirtualIds.ExceptWith(retainedIds);
                return;
            }
            if (!replayingVirtualRootIds.Add(rootId))
                throw new InvalidOperationException($"Sparse constructor dependency cycle at '{rootId}'.");
            try { candidate.Add(ExpandVirtualInstanceRootCore(root, prepareOnly: true)); }
            finally { replayingVirtualRootIds.Remove(rootId); }
        }

        private bool CanRetainCandidateRoot(CandidateReplay candidate, ObjectMemberValue root)
        {
            if (CurrentChangeSource == NeoChangeSource.External
                || !virtualFootprintByRoot.TryGetValue(root.id, out var footprint)
                || !SameRow(PreviousRow(root.id), root)) return false;
            foreach (var write in candidate.Plan.Rows.Keys)
                if (footprint.Contains(write.id)) return false;
            if (constructorArgumentValueIdsByRoot.TryGetValue(root.id, out var dependencies))
            {
                foreach (string id in dependencies)
                {
                    if (id.StartsWith("static:", StringComparison.Ordinal))
                    {
                        foreach (var binding in candidate.Plan.Bindings.Keys)
                            if (id == $"static:{binding.ownership}:{binding.memberId}") return false;
                        continue;
                    }
                    bool written = candidate.Plan.Rows.ContainsKey((NeoValueOwnership.Save, id))
                        || candidate.Plan.Rows.ContainsKey((NeoValueOwnership.Session, id));
                    if (!written && footprint.Contains(id)) continue;
                    if (!written && !candidate.HiddenVirtualIds.Contains(id)) continue;
                    if (candidate.HiddenVirtualIds.Contains(id) && !candidate.Values.ContainsKey(id))
                        return false;
                    if (!SameRow(PreviousRow(id), candidate.Plan.Resolve(id)))
                        return false;
                }
            }
            if (virtualValueIdsByRoot.TryGetValue(root.id, out var values))
                foreach (string id in values)
                    if (candidate.Values.TryGetValue(id, out var proposed)
                        && virtualValues.TryGetValue(id, out var current) && !SameRow(current, proposed)) return false;
            return true;

            MemberValue? PreviousRow(string id) => sessionData.values.TryGetValue(id, out var session) ? session
                : saveData.values.TryGetValue(id, out var save) ? save
                : data.values.TryGetValue(id, out var asset) ? asset
                : virtualValues.TryGetValue(id, out var cached) ? cached : null;

            static bool SameRow(MemberValue? left, MemberValue? right) => NeoSemanticJson.MemberRowsEqual(left, right);
        }

        private CandidateReplay? ValidatePreparedWrite(NeoWritePlan plan)
        {
            CandidateReplay? candidate = PrepareCandidateExpansions(plan);
            if (candidate is null) { ValidateWritePlan(plan); return null; }
            candidateReplay = candidate;
            try
            {
                using (ReadCandidate(plan))
                {
                    foreach (string id in candidate.AffectedRoots.OrderBy(id => id).ToArray()) PrepareCandidateRoot(id);
                    ValidateWritePlan(plan);
                }
                return candidate;
            }
            finally
            {
                candidate.Dispose();
                candidateReplay = null;
            }
        }

        private void InstallCandidateExpansions(CandidateReplay? candidate,
            HashSet<(NeoValueOwnership ownership, string valueId)> changed)
        {
            if (candidate is null) return;
            var retired = new HashSet<string>();
            foreach (string root in candidate.AffectedRoots)
            {
                if (CurrentChangeSource != NeoChangeSource.External
                    && !candidate.ReplacedConstructionRoots.Contains(root)) continue;
                if (virtualValueIdsByRoot.TryGetValue(root, out var oldIds)) retired.UnionWith(oldIds);
                if (candidate.Expansions.TryGetValue(root, out var next)) retired.UnionWith(next.Values.Keys);
            }
            DisposeWrappersTouchingRows(retired);
            // Replaying an enclosing root also rebuilds unchanged siblings.
            // Only semantic row or ownership changes need notifications.
            foreach (string id in candidate.HiddenVirtualIds)
            {
                if (!virtualValueOwnership.TryGetValue(id, out var oldOwnership)) continue;
                if (candidate.Values.TryGetValue(id, out MemberValue next)
                    && candidate.Ownership.TryGetValue(id, out var nextOwnership)
                    && oldOwnership == nextOwnership
                    && virtualValues.TryGetValue(id, out MemberValue previous)
                    && NeoSemanticJson.MemberRowsEqual(previous, next)) continue;
                changed.Add((oldOwnership, id));
                if (candidate.Ownership.TryGetValue(id, out var changedOwnership))
                    changed.Add((changedOwnership, id));
            }
            foreach (var row in candidate.Ownership)
                if (!candidate.HiddenVirtualIds.Contains(row.Key)) changed.Add((row.Value, row.Key));
            foreach (string root in candidate.AffectedRoots)
                if (!candidate.RetainedRoots.Contains(root)) ClearVirtualInstanceRoot(root);
            foreach (PreparedVirtualExpansion expansion in candidate.Expansions.Values)
                InstallVirtualExpansion(expansion);
            foreach (string root in candidate.AffectedRoots)
                if (!candidate.RetainedRoots.Contains(root) && nodesByValueId.TryGetValue(root, out var nodes))
                    foreach (NeoMember node in nodes.ToArray())
                        if (!node.isDisposed && node is NeoMemberClass classNode
                            && TryGetOverlaidValue(node.ownership, root, out ObjectMemberValue? _))
                            classNode.RefreshCommittedValue();
        }

        private bool TryResolveVirtualValue(string id, out MemberValue value)
        {
            if (candidateReplay is not null)
            {
                if (candidateReplay.Values.TryGetValue(id, out value)) return true;
                if (candidateReplay.HiddenVirtualIds.Contains(id)) { value = null!; return false; }
            }
            return virtualValues.TryGetValue(id, out value);
        }

        private bool TryResolveVirtualOwnership(string id, out NeoValueOwnership ownership)
        {
            if (candidateReplay is not null)
            {
                if (candidateReplay.Ownership.TryGetValue(id, out ownership)) return true;
                if (candidateReplay.HiddenVirtualIds.Contains(id)) { ownership = default; return false; }
            }
            return virtualValueOwnership.TryGetValue(id, out ownership);
        }

        private bool TryResolveVirtualPlacement(string id, out VirtualClassPlacement placement)
        {
            if (candidateReplay is not null)
            {
                if (candidateReplay.Placements.TryGetValue(id, out placement)) return true;
                if (virtualClassPlacementByChildId.TryGetValue(id, out var previous)
                    && candidateReplay.AffectedRoots.Contains(previous.rootId)) { placement = null!; return false; }
            }
            return virtualClassPlacementByChildId.TryGetValue(id, out placement);
        }

        private bool TryResolveVirtualClassChildren(string parentId, out Dictionary<string, string> children)
        {
            if (candidateReplay is not null)
            {
                if (candidateReplay.ClassChildren.TryGetValue(parentId, out children)) return true;
                if (virtualRootByFootprintId.TryGetValue(parentId, out string rootId)
                    && candidateReplay.AffectedRoots.Contains(rootId)) { children = null!; return false; }
            }
            return virtualClassChildren.TryGetValue(parentId, out children);
        }

        // Constructor allocations and wrappers belong to the proposed graph.
        // Existing stores and the public wrapper registry remain untouched.
        private sealed class CandidateReplay : IDisposable
        {
            internal readonly NeoWritePlan Plan;
            internal bool PreparingVariant;
            internal readonly Dictionary<string, MemberValue> Allocations = new();
            internal readonly Dictionary<string, NeoMember> Nodes = new();
            internal readonly Dictionary<string, NeoGeneratedClassValue> GeneratedValues = new();
            internal readonly Dictionary<string, HashSet<string>> ContainerMembers = new();
            internal readonly Dictionary<string, HashSet<string>> Parents = new();
            internal readonly Dictionary<string, HashSet<string>> VirtualContainerMembers = new();
            internal readonly Dictionary<string, PreparedVirtualExpansion> Expansions = new();
            internal readonly Dictionary<string, MemberValue> Values = new();
            internal readonly Dictionary<string, NeoValueOwnership> Ownership = new();
            internal readonly Dictionary<string, Dictionary<string, string>> ClassChildren = new();
            internal readonly Dictionary<string, VirtualClassPlacement> Placements = new();
            internal readonly HashSet<string> AffectedRoots = new();
            internal readonly HashSet<string> RetainedRoots = new();
            internal readonly HashSet<string> ReplacedConstructionRoots = new();
            internal readonly HashSet<string> HiddenVirtualIds = new();
            internal readonly IReadOnlyDictionary<string, MemberValue> SessionRows;

            internal CandidateReplay(NeoClient client, NeoWritePlan plan)
            {
                Plan = plan;
                SessionRows = new ReplaySessionRows(client.sessionData.values, Allocations, plan);
            }

            internal void SetAllocation(string id, MemberValue? row)
            {
                if (Allocations.TryGetValue(id, out MemberValue? oldRow))
                    foreach (string child in PlacementChildIds(oldRow))
                        if (Parents.TryGetValue(child, out var parents)) parents.Remove(id);
                if (Allocations.TryGetValue(id, out MemberValue? previous) && previous.containerId is string oldContainer
                    && ContainerMembers.TryGetValue(oldContainer, out var oldMembers)) oldMembers.Remove(id);
                if (row is null) { Allocations.Remove(id); return; }
                Allocations[id] = row;
                foreach (string child in PlacementChildIds(row))
                {
                    if (!Parents.TryGetValue(child, out var parents)) Parents[child] = parents = new HashSet<string>();
                    parents.Add(id);
                }
                if (row.containerId is string container)
                {
                    if (!ContainerMembers.TryGetValue(container, out var members))
                        ContainerMembers[container] = members = new HashSet<string>();
                    members.Add(id);
                }
            }

            internal void Add(PreparedVirtualExpansion expansion)
            {
                if (Expansions.TryGetValue(expansion.Root.id, out var previous))
                {
                    foreach (string id in previous.Values.Keys) { Values.Remove(id); Ownership.Remove(id); }
                    foreach (string id in previous.ClassChildren.Keys) ClassChildren.Remove(id);
                    foreach (string id in previous.Placements.Keys) Placements.Remove(id);
                    foreach (var pair in previous.Values)
                        if (pair.Value.containerId is string container
                            && VirtualContainerMembers.TryGetValue(container, out var members))
                        {
                            members.Remove(pair.Key);
                            if (members.Count == 0) VirtualContainerMembers.Remove(container);
                        }
                }
                Expansions[expansion.Root.id] = expansion;
                foreach (var pair in expansion.Values)
                {
                    Values[pair.Key] = pair.Value;
                    if (pair.Value.containerId is string container)
                    {
                        if (!VirtualContainerMembers.TryGetValue(container, out var members))
                            VirtualContainerMembers[container] = members = new HashSet<string>();
                        members.Add(pair.Key);
                    }
                }
                foreach (var pair in expansion.Ownership) Ownership[pair.Key] = pair.Value;
                foreach (var pair in expansion.ClassChildren) ClassChildren[pair.Key] = pair.Value;
                foreach (var pair in expansion.Placements) Placements[pair.Key] = pair.Value;
            }

            internal void Apply(NeoWritePlan plan)
            {
                if (PreparingVariant && !Plan.Client.isReplayingVirtualInstance)
                {
                    foreach (var pair in plan.Rows)
                    {
                        if (pair.Value is null) Plan.Remove(pair.Key.ownership, pair.Key.id);
                        else Plan.Set(pair.Key.ownership, pair.Value,
                            plan.Fields.GetValueOrDefault(pair.Key), plan.Silent.Contains(pair.Key));
                        // Promotion removes a constructed Session allocation
                        // while preserving the same id in its destination store.
                        if (Allocations.ContainsKey(pair.Key.id)) SetAllocation(pair.Key.id, null);
                    }
                    foreach (var binding in plan.Bindings)
                        Plan.Bind(binding.Key.ownership, binding.Key.memberId, binding.Value.present, binding.Value.valueId);
                    foreach (var binding in plan.NodeBindings) Plan.NodeBindings[binding.Key] = binding.Value;
                    plan.NotifyCommitted();
                    foreach (NeoMember node in Nodes.Values.ToArray())
                        if (!node.isDisposed && (node.overrideValueId ?? node.value?.id) is string id && plan.Rows.ContainsKey((node.ownership, id)))
                            node.RefreshCommittedValue();
                    plan.NotifyCompleted();
                    return;
                }
                if (plan.Bindings.Count != 0)
                    throw new InvalidOperationException("Stored constructor replay cannot change global bindings.");
                foreach (var pair in plan.Rows)
                {
                    if (pair.Key.ownership != NeoValueOwnership.Session
                        || (!Allocations.ContainsKey(pair.Key.id)
                            && Plan.Client.TryGetCommittedValue(pair.Key.id, out MemberValue? _)))
                        throw new InvalidOperationException($"Stored constructor replay cannot change existing value '{pair.Key.id}'.");
                }
                foreach (var pair in plan.Rows)
                {
                    SetAllocation(pair.Key.id, pair.Value);
                }
                plan.NotifyCommitted();
                plan.NotifyCompleted();
            }

            public void Dispose()
            {
                foreach (NeoGeneratedClassValue value in GeneratedValues.Values.ToArray()) value.Dispose();
                GeneratedValues.Clear();
                foreach (NeoMember node in Nodes.Values.ToArray()) node.Dispose();
                Nodes.Clear();
                Allocations.Clear();
            }
        }

        private IReadOnlyCollection<string> RemoveReplayAllocationGraph(string valueId)
        {
            var removed = new HashSet<string>();
            Remove(valueId, null);
            return removed;
            void Remove(string id, Member? member)
            {
                if (!removed.Add(id) || !candidateReplay!.Allocations.TryGetValue(id, out MemberValue? row)) return;
                foreach (var child in EnumerateOwnedChildLinks(row, member)) Remove(child.valueId, child.member);
                if (candidateReplay.ContainerMembers.TryGetValue(id, out var members))
                    foreach (string child in members.ToArray()) Remove(child, null);
                candidateReplay.SetAllocation(id, null);
            }
        }

        private sealed class ReplaySessionRows : IReadOnlyDictionary<string, MemberValue>
        {
            private readonly IReadOnlyDictionary<string, MemberValue> stored;
            private readonly IReadOnlyDictionary<string, MemberValue> allocated;
            private readonly NeoWritePlan plan;
            internal ReplaySessionRows(IReadOnlyDictionary<string, MemberValue> stored, IReadOnlyDictionary<string, MemberValue> allocated, NeoWritePlan plan)
            { this.stored = stored; this.allocated = allocated; this.plan = plan; }
            public MemberValue this[string key] => TryGetValue(key, out var row) ? row : throw new KeyNotFoundException(key);
            public IEnumerable<string> Keys => this.Select(pair => pair.Key);
            public IEnumerable<MemberValue> Values => this.Select(pair => pair.Value);
            public int Count => this.Count();
            public bool ContainsKey(string key) => TryGetValue(key, out _);
            public bool TryGetValue(string key, out MemberValue value)
            {
                if (allocated.TryGetValue(key, out value)) return true;
                if (plan.Rows.TryGetValue((NeoValueOwnership.Session, key), out var staged))
                { value = staged!; return staged is not null; }
                return stored.TryGetValue(key, out value);
            }
            public IEnumerator<KeyValuePair<string, MemberValue>> GetEnumerator()
            {
                foreach (var pair in allocated) yield return pair;
                foreach (var pair in plan.Rows)
                    if (pair.Key.ownership == NeoValueOwnership.Session && pair.Value is not null && !allocated.ContainsKey(pair.Key.id))
                        yield return new KeyValuePair<string, MemberValue>(pair.Key.id, pair.Value);
                foreach (var pair in stored)
                    if (!allocated.ContainsKey(pair.Key) && !plan.Rows.ContainsKey((NeoValueOwnership.Session, pair.Key))) yield return pair;
            }
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
