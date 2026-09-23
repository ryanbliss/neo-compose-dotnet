// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    internal sealed class NeoPreparedLayerRecords<T>
    {
        internal readonly List<T> Records;
        internal readonly HashSet<string> DependencyIds;
        internal NeoPreparedLayerRecords(List<T> records, HashSet<string> dependencyIds)
        { Records = records; DependencyIds = dependencyIds; }
    }

    /// <summary>One tile layer's flattened records and what flattening them read.</summary>
    internal sealed class NeoTileLayerBuild
    {
        internal readonly List<NeoTilePlacementRecord> Records = new();
        /// <summary>Every row read; a committed write to any re-flattens the layer.</summary>
        internal readonly HashSet<string> DependencyIds = new();
        /// <summary>
        /// The Enabled and nested Position rows carried links read. A runtime
        /// leaf write skips the plan, so these re-flatten the layer from the
        /// leaf path instead (<see cref="NeoTileGridLookupCache.InvalidateLeaf"/>).
        /// </summary>
        internal readonly HashSet<string> LeafDependencyIds = new();
        /// <summary>The object-carried links that flatten into this layer, enabled or not.</summary>
        internal readonly HashSet<string> CarriedLinkIds = new();
    }

    /// <summary>A prepared data edit. Building it never changes the live graph.</summary>
    internal sealed class NeoWritePlan
    {
        internal readonly NeoClient Client;
        internal readonly long BaseRevision;
        internal (string gridId, string layerId, string listId, string instanceId)? ObjectInsertion;
        internal string? ValidatedObjectInsertionGrid;
        internal readonly HashSet<string> UnchangedValueIds = new();
        internal readonly List<NeoValidatedTileConversion> ValidatedTileConversions = new();
        internal readonly Dictionary<(string gridId, string layerId), NeoTileLayerBuild> PreparedTileLayers = new();
        internal readonly Dictionary<(string gridId, string layerId), NeoPreparedLayerRecords<NeoObjectPlacementRecord>> PreparedObjectLayers = new();
        internal readonly Dictionary<(NeoValueOwnership ownership, string id), MemberValue?> Rows = new();
        internal readonly Dictionary<(NeoValueOwnership ownership, string id), string?> Fields = new();
        internal readonly HashSet<(NeoValueOwnership ownership, string id)> Silent = new();
        internal readonly Dictionary<(NeoValueOwnership ownership, string memberId), (bool present, string? valueId)> Bindings = new();
        internal readonly Dictionary<NeoMember, string> NodeBindings = new();
        private readonly List<Action> afterCommit = new();
        private readonly List<Action> afterNotifications = new();
        private Dictionary<string, HashSet<string>>? containerCandidates;
        private Dictionary<string, HashSet<string>>? parentCandidates;

        internal NeoWritePlan(NeoClient client)
        { Client = client; BaseRevision = client.WriteRevision; }

        internal void Set(NeoValueOwnership ownership, MemberValue row, string? changedField = null, bool silent = false)
        {
            if (ownership == NeoValueOwnership.Asset)
                throw new InvalidOperationException("Cannot write immutable asset data.");
            var key = (ownership, row.id);
            Rows[key] = row;
            containerCandidates = null;
            parentCandidates = null;
            Fields[key] = changedField;
            if (silent) Silent.Add(key); else Silent.Remove(key);
        }

        internal void Remove(NeoValueOwnership ownership, string id)
        {
            if (ownership == NeoValueOwnership.Asset)
                throw new InvalidOperationException("Cannot remove immutable asset data.");
            var key = (ownership, id);
            Rows[key] = null;
            parentCandidates = null;
            Silent.Remove(key);
        }

        internal MemberValue? Resolve(NeoValueOwnership ownership, string id)
        {
            if (Rows.TryGetValue((ownership, id), out MemberValue? candidate))
            {
                if (candidate is not null) return candidate.IsRemoved ? null : candidate;
                return Client.TryGetCommittedValue(NeoValueOwnership.Asset, id, out MemberValue? fallback) ? fallback : null;
            }
            if (Client.ReplayAllocation(id) is MemberValue allocation) return allocation;
            return Client.ResolveWritePlanFallback(this, ownership, id);
        }

        internal bool TryGetWritable(NeoValueOwnership ownership, string id, out MemberValue? row)
        {
            if (Rows.TryGetValue((ownership, id), out row)) return row is not null;
            return Client.TryGetWritableValue(ownership, id, out row);
        }

        internal bool TryGetWritable<T>(NeoValueOwnership ownership, string id, out T? row) where T : MemberValue
        {
            bool found = TryGetWritable(ownership, id, out MemberValue? value);
            row = value as T;
            return found && row is not null;
        }

        internal MemberValue? Resolve(string id)
        {
            if (TryGetWritable(NeoValueOwnership.Session, id, out MemberValue? session)) return session;
            if (TryGetWritable(NeoValueOwnership.Save, id, out MemberValue? save)) return save;
            return Client.ResolveWritePlanGlobalFallback(this, id);
        }

        internal bool TryGet<T>(NeoValueOwnership ownership, string id, out T? row) where T : MemberValue
        {
            row = Resolve(ownership, id) as T;
            return row is not null;
        }

        internal bool TryGet<T>(string id, out T? row) where T : MemberValue
        {
            row = Resolve(id) as T;
            return row is not null;
        }

        internal bool TryGetOwnership(string id, out NeoValueOwnership ownership)
        {
            // Ownership resolution is metadata, like the committed-store path.
            // It must not turn a class reference into a whole-payload read.
            using var metadataReads = Client.SuppressValueReads();
            if (TryGetWritable(NeoValueOwnership.Session, id, out _))
            { ownership = NeoValueOwnership.Session; return true; }
            if (TryGetWritable(NeoValueOwnership.Save, id, out _))
            { ownership = NeoValueOwnership.Save; return true; }
            return Client.TryGetCommittedOwnership(id, out ownership);
        }

        internal IEnumerable<string> ParentCandidates(string childId)
        {
            if (parentCandidates is null)
            {
                parentCandidates = new Dictionary<string, HashSet<string>>();
                foreach (MemberValue? row in Rows.Values)
                {
                    if (row is null) continue;
                    foreach (string child in NeoClient.PlacementChildIds(row))
                    {
                        if (!parentCandidates.TryGetValue(child, out var parents)) parentCandidates[child] = parents = new HashSet<string>();
                        parents.Add(row.id);
                    }
                }
            }
            return parentCandidates.TryGetValue(childId, out var values) ? values : Array.Empty<string>();
        }

        internal IEnumerable<string> ContainerCandidates(string containerId)
        {
            if (containerCandidates is null)
            {
                containerCandidates = new Dictionary<string, HashSet<string>>();
                foreach (MemberValue? row in Rows.Values)
                {
                    if (string.IsNullOrEmpty(row?.containerId)) continue;
                    if (!containerCandidates.TryGetValue(row!.containerId!, out var ids))
                        containerCandidates[row.containerId!] = ids = new HashSet<string>();
                    ids.Add(row.id);
                }
            }
            return containerCandidates.TryGetValue(containerId, out var candidates)
                ? candidates : Array.Empty<string>();
        }

        internal void Bind(NeoValueOwnership ownership, string memberId, bool present, string? valueId) =>
            Bindings[(ownership, memberId)] = (present, valueId);

        internal void AfterNotifications(Action callback) => afterNotifications.Add(callback);
        internal void NotifyCompleted()
        {
            foreach (Action callback in afterNotifications) callback();
        }
        internal void AfterCommit(Action callback) => afterCommit.Add(callback);
        internal void NotifyCommitted()
        {
            foreach (Action callback in afterCommit) callback();
        }

        internal void Commit() => Client.CommitWritePlan(this);
    }
}

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private static readonly Unity.Profiling.ProfilerMarker CommitWriteMarker = new("NeoCompose.Write.Commit");
        internal long WriteRevision { get; private set; }

        private NeoWritePlan? candidateReadPlan;
        internal MemberValue? ResolveWritePlanGlobalFallback(NeoWritePlan plan, string id)
        {
            if (candidateReadPlan is not null && !ReferenceEquals(candidateReadPlan, plan)) return candidateReadPlan.Resolve(id);
            if (data.values.TryGetValue(id, out MemberValue asset)) return asset;
            return TryResolveVirtualValue(id, out MemberValue virtualRow) ? virtualRow : null;
        }

        internal MemberValue? ResolveWritePlanFallback(NeoWritePlan plan, NeoValueOwnership ownership, string id)
        {
            if (candidateReadPlan is not null && !ReferenceEquals(candidateReadPlan, plan))
                return candidateReadPlan.Resolve(ownership, id);
            return TryGetCommittedOverlaidValue(ownership, id, out MemberValue? row) ? row : null;
        }

        internal IDisposable ReadCandidate(NeoWritePlan plan)
        {
            NeoWritePlan? previous = candidateReadPlan;
            candidateReadPlan = plan;
            return new NeoDisposableAction(() => candidateReadPlan = previous);
        }

        internal event Action<IReadOnlyCollection<(NeoValueOwnership ownership, string valueId)>, NeoWritePlan>? OnWritableValuesPublished;
        internal event Action<IReadOnlyCollection<(NeoValueOwnership ownership, string valueId)>>? OnWritableValuesChanged;

        private static bool WritesAnyChild(NeoWritePlan plan, NeoValueOwnership ownership, ObjectMemberValue parentRow)
        {
            foreach (string childId in parentRow.value!.Values)
                if (plan.Rows.ContainsKey((ownership, childId))) return true;
            return false;
        }

        private bool commitScratchInUse;
        private readonly HashSet<(NeoValueOwnership ownership, string valueId)> commitChangedScratch = new();
        private readonly Dictionary<(NeoValueOwnership ownership, string id), string> commitOldContainersScratch = new();

        internal void CommitWritePlan(NeoWritePlan plan)
        {
            using var marker = CommitWriteMarker.Auto();
            if (nestedConstructedRows is not null)
                foreach (var key in plan.Rows.Keys)
                    if (nestedConstructedRows.TryGetValue(key.id, out var producer)
                        && !ReferenceEquals(producer, nestedConstructorCapture))
                        producer.HasExternalWrites = true;
            if (plan.Bindings.Count != 0)
                for (var scope = nestedConstructorCapture; scope is not null; scope = scope.Parent) scope.HasExternalWrites = true;
            if (nestedConstructorCapture is not null)
                foreach (var key in plan.Rows.Keys)
                {
                    bool exists = ResolveValueRow(key.id) is not null;
                    for (var scope = nestedConstructorCapture; scope is not null; scope = scope.Parent)
                    {
                        if (!exists) scope.Allocations.Add(key.id);
                        else if (!scope.Allocations.Contains(key.id)) scope.HasExternalWrites = true;
                    }
                }
            if (replayAllocationScope is not null && candidateReplay is null)
                foreach (var pair in plan.Rows)
                    if (pair.Key.ownership == NeoValueOwnership.Session && pair.Value is not null
                        && !sessionData.values.ContainsKey(pair.Key.id)) RecordReplayAllocation(pair.Key.id);
            if (candidateReplay is not null)
            { candidateReplay.Apply(plan); return; }
            if (!ReferenceEquals(plan.Client, this))
                throw new ArgumentException("Write plan belongs to another client.", nameof(plan));
            foreach (var pair in plan.Rows.ToArray())
                if (pair.Key.ownership == NeoValueOwnership.Save && pair.Value is not null)
                    StageConstructorDependencies(plan, pair.Value);
            foreach (var pair in plan.Rows)
                if (pair.Value is not null) StampMapKeyForWrite(pair.Key.ownership, pair.Value);

            if (plan.BaseRevision != WriteRevision)
                throw new InvalidOperationException("The data graph changed while this write was being prepared.");
            CandidateReplay? preparedExpansions = ValidatePreparedWrite(plan);
            if (plan.BaseRevision != WriteRevision)
                throw new InvalidOperationException("The data graph changed during candidate validation.");
            foreach (var row in plan.Rows)
                if (TryGetCommittedOwnership(row.Key.id, out var previousOwnership)
                    && previousOwnership == row.Key.ownership
                    && ReplayRowsEqual(PreviousReplayRow(row.Key.id), row.Value))
                    plan.UnchangedValueIds.Add(row.Key.id);
            // Notifications can commit again before this commit returns, so
            // the scratch sets serve only the outermost commit.
            bool pooledScratch = !commitScratchInUse;
            commitScratchInUse = true;
            HashSet<(NeoValueOwnership ownership, string valueId)> changed = pooledScratch ? commitChangedScratch : new();
            Dictionary<(NeoValueOwnership ownership, string id), string> oldContainers = pooledScratch ? commitOldContainersScratch : new();
            try
            {
            bool touchesWorld = false;
            foreach (var pair in plan.Rows)
            {
                if (TryResolveContainerIdForValueId(pair.Key.id, out string? containerId))
                {
                    oldContainers[pair.Key] = containerId!;
                    changed.Add((pair.Key.ownership, containerId!));
                }
                if (!string.IsNullOrEmpty(pair.Value?.containerId))
                    changed.Add((pair.Key.ownership, pair.Value!.containerId!));
                changed.Add(pair.Key);
                if (!touchesWorld)
                    touchesWorld = IsWorldClass(pair.Value?.classId)
                        || TryGetCommittedValue(pair.Key.id, out MemberValue? previousRow) && IsWorldClass(previousRow?.classId);
            }
            foreach (var binding in plan.Bindings)
            {
                var current = GetWritableStore(binding.Key.ownership).staticBindings;
                if (current.TryGetValue(binding.Key.memberId, out string? prior) && prior is not null)
                    changed.Add((binding.Key.ownership, prior));
                if (binding.Value.valueId is string next) changed.Add((binding.Key.ownership, next));
            }
            foreach (var pair in plan.Rows)
            {
                if (pair.Value is null)
                {
                    GetWritableStore(pair.Key.ownership).values.Remove(pair.Key.id);
                    IndexStoreRemove(pair.Key.ownership, pair.Key.id);
                }
                else StoreWritableValue(pair.Key.ownership, pair.Value);
                TouchWritableStoreUpdatedAt(pair.Key.ownership);
            }
            foreach (var pair in plan.Bindings)
            {
                var bindings = GetWritableStore(pair.Key.ownership).staticBindings;
                if (pair.Value.present) bindings[pair.Key.memberId] = pair.Value.valueId;
                else bindings.Remove(pair.Key.memberId);
                TouchWritableStoreUpdatedAt(pair.Key.ownership);
            }
            WriteRevision++;
            InstallCandidateExpansions(preparedExpansions, changed);
            if (plan.Bindings.Count != 0) InvalidateGetterMemo();
            else InvalidateGetterMemoForRows(changed);
            if (touchesWorld) InvalidateGridDependentGetterMemo();
            if (sharedEvaluationContext is not null)
                foreach (var item in changed)
                    if (!plan.Rows.ContainsKey(item) || plan.Silent.Contains(item))
                        RefreshSharedEvaluationRow(item.ownership, item.valueId);
            OnWritableValuesPublished?.Invoke(changed, plan);
            plan.NotifyCommitted();
            OnWritableValuesChanged?.Invoke(changed);
            using (SuspendContainerNotifications())
            {
                foreach (var pair in plan.Rows)
                {
                    if (plan.Silent.Contains(pair.Key))
                    {
                        if (pair.Key.ownership == NeoValueOwnership.Save
                            && !suppressLiveAutoCommit && loader is NeoSaveSynchronizer synchronizer)
                            synchronizer.MarkDirtyValue(pair.Key.id, null);
                        continue;
                    }
                    plan.Fields.TryGetValue(pair.Key, out string? changedField);
                    NotifyWritableValueChanged(pair.Key.ownership, pair.Key.id, changedField,
                        valueChanged: !(plan.UnchangedValueIds.Contains(pair.Key.id)
                            && pair.Value is ObjectMemberValue { classId: not null, value: not null } parentRow
                            && WritesAnyChild(plan, pair.Key.ownership, parentRow)));
                    if (oldContainers.TryGetValue(pair.Key, out string? containerId))
                        RaiseContainerChanged(pair.Key.ownership, containerId);
                }
                foreach (var item in changed)
                    if (!plan.Rows.ContainsKey((item.ownership, item.valueId))
                        && preparedExpansions?.HiddenVirtualIds.Contains(item.valueId) == true)
                        PublishWritableValueChange(item.ownership, item.valueId);
                foreach (var pair in plan.Bindings)
                {
                    OnStaticBindingChanged?.Invoke(pair.Key.ownership, pair.Key.memberId);
                    if (pair.Key.ownership == NeoValueOwnership.Save
                        && !suppressLiveAutoCommit && loader is NeoSaveSynchronizer synchronizer)
                        synchronizer.MarkDirtyStaticBinding(pair.Key.memberId);
                    if (pair.Key.ownership == NeoValueOwnership.Save && !suppressLiveAutoCommit)
                        ScheduleLiveAutoCommit();
                }
            }
            plan.NotifyCompleted();
            }
            finally
            {
                if (pooledScratch)
                {
                    changed.Clear();
                    oldContainers.Clear();
                    commitScratchInUse = false;
                }
            }
        }
    }
}

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        internal void StageUnlinkedRemovals(
            NeoWritePlan plan, NeoValueOwnership ownership, IEnumerable<string> valueIds, Member? member)
            => StageUnlinkedRemovals(plan, ownership, valueIds.Select(id => (id, member)));

        internal void StageUnlinkedRemovals(NeoWritePlan plan, NeoValueOwnership ownership,
            IEnumerable<(string valueId, Member? member)> roots)
        {
            // A detached default often has no writable rows at all. Find the
            // actual removals before walking global reachability, and share
            // that walk across all children released by one assignment.
            var removals = new List<string>();
            var visited = new HashSet<string>();
            foreach (var root in roots)
                StageOwnedRemoval(plan, ownership, root.valueId, root.member, false, visited, null, removals);
            if (removals.Count == 0) return;
            HashSet<string> reachable;
            using (ReadCandidate(plan))
            {
                if (CanProveUnreachable(ownership, removals))
                {
                    foreach (string id in removals) RemoveOwnedRow(plan, ownership, id);
                    return;
                }
                reachable = BuildReachableWritableValueIds(ownership);
            }
            foreach (string id in removals)
                if (!reachable.Contains(id)) RemoveOwnedRow(plan, ownership, id);
        }

        internal void StageOwnedRemoval(
            NeoWritePlan plan, NeoValueOwnership ownership, string valueId, Member? member,
            bool tombstone = false)
        {
            StageOwnedRemoval(plan, ownership, valueId, member, tombstone, new HashSet<string>(), null);
        }

        private void StageOwnedRemoval(
            NeoWritePlan plan, NeoValueOwnership ownership, string valueId, Member? member,
            bool tombstone, HashSet<string> visited, HashSet<string>? reachable, List<string>? removals = null)
        {
            if (reachable?.Contains(valueId) == true || !visited.Add(valueId)) return;
            MemberValue? row = plan.Resolve(ownership, valueId);
            if (row is not null)
            {
                foreach (var child in EnumerateOwnedChildLinks(row, member))
                {
                    NeoValueOwnership childOwnership = ChildOwnership(child.member, ownership);
                    if (childOwnership != ownership) continue;
                    StageOwnedRemoval(plan, ownership, child.valueId, child.member, false, visited, reachable, removals);
                }
                if (member is ListMember list && IsUnorderedList(list))
                {
                    Member? entry = TryResolveCollectionEntryMember(member, row);
                    var entries = new HashSet<string>(EnumerateContainerMemberValueIds(ownership, valueId));
                    entries.UnionWith(plan.ContainerCandidates(valueId));
                    foreach (string childId in entries)
                        StageOwnedRemoval(plan, ownership, childId, entry, false, visited, reachable, removals);
                }
            }
            if (tombstone)
            {
                NeoTimestamp now = NeoTimestamp.Now();
                plan.Set(ownership, new NullMemberValue
                {
                    id = valueId, createdAt = now, updatedAt = now, mark = NeoValueMarks.Removed,
                }, "mark");
                StageVirtualFootprintRemoval(plan, ownership, valueId);
            }
            else if (plan.TryGetWritable(ownership, valueId, out _))
            {
                if (removals is not null) removals.Add(valueId);
                else RemoveOwnedRow(plan, ownership, valueId);
            }
        }

        private void RemoveOwnedRow(NeoWritePlan plan, NeoValueOwnership ownership, string valueId)
        {
            plan.Remove(ownership, valueId);
            StageVirtualFootprintRemoval(plan, ownership, valueId);
        }

        /// <summary>
        /// P75: a virtual root's stored overrides (materialized spines, pins)
        /// live at ids minted from that root's namespace, so nothing else owns
        /// them and they die with it, along with what they own. Reachability
        /// cannot prove this before the write installs: the root's committed
        /// expansion still links them. A removal stays in its own store, plus
        /// the Session overlay above a removed Save root; a Session tombstone
        /// over a Save root leaves the Save overrides for when it lifts.
        /// </summary>
        private void StageVirtualFootprintRemoval(NeoWritePlan plan, NeoValueOwnership ownership, string rootId)
        {
            if (!virtualFootprintByRoot.TryGetValue(rootId, out var footprint)) return;
            foreach (string id in footprint)
            {
                if (id == rootId) continue;
                Drop(ownership, id);
                if (ownership == NeoValueOwnership.Save) Drop(NeoValueOwnership.Session, id);
            }

            void Drop(NeoValueOwnership store, string id)
            {
                if (plan.TryGetWritable(store, id, out _))
                    StageOwnedRemoval(plan, store, id, TryInferMemberForValueId(id, out Member? member) ? member : null);
            }
        }
    }
}
