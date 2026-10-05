// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    internal sealed class NeoPreparedLayerRecords<T>
    {
        internal readonly List<T> Records;
        internal readonly HashSet<string> DependencyIds;
        internal NeoPreparedLayerRecords(List<T> records, HashSet<string> dependencyIds)
        {
            Records = records;
            DependencyIds = dependencyIds;
        }
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
        internal readonly int? RequiredSaveFormatRevision;
        internal long BaseRevision
        {
            get; private set;
        }
        internal (string gridId, string layerId, string listId, string instanceId)? ObjectInsertion;
        internal string? ValidatedObjectInsertionGrid;
        // A freshly constructed receiver is only gaining its variant stamp.
        // Completeness still has to prove that its graph supplies every default.
        internal string? ConstructedVariantRoot;
        // Collections most writes never fill are allocated on first use.
        internal HashSet<string>? UnchangedValueIds;
        internal List<NeoValidatedTileConversion>? ValidatedTileConversions;
        internal Dictionary<(string gridId, string layerId), NeoTileLayerBuild>? PreparedTileLayers;
        internal Dictionary<(string gridId, string layerId), NeoPreparedLayerRecords<NeoObjectPlacementRecord>>? PreparedObjectLayers;
        // Sized for a collection write: its row, and an entry with a few fields.
        internal readonly Dictionary<(NeoValueOwnership ownership, string id), MemberValue?> Rows = new(8);
        // Counts staged row and binding changes, so a replay prepared against
        // this plan can tell the plan still matches it.
        internal int Version;
        private Dictionary<(NeoValueOwnership ownership, string id), string>? changedFields;
        private HashSet<(NeoValueOwnership ownership, string id)>? silentRows;
        // Most plans bind no static; they share one empty map until the first Bind.
        private static readonly Dictionary<(NeoValueOwnership ownership, string memberId), (bool present, string? valueId)> NoBindings = new();
        private Dictionary<(NeoValueOwnership ownership, string memberId), (bool present, string? valueId)>? bindings;
        /// <summary>The staged static bindings. Read-only: <see cref="Bind"/> writes them.</summary>
        internal Dictionary<(NeoValueOwnership ownership, string memberId), (bool present, string? valueId)> Bindings => bindings ?? NoBindings;
        private Dictionary<NeoMember, string>? nodeBindings;
        private Callbacks afterCommit;
        private Callbacks afterNotifications;
        // The collection mutators behind this plan: almost always one.
        private NeoMember? reportingNode;
        private List<NeoMember>? moreReportingNodes;
        private Dictionary<string, HashSet<string>>? containerCandidates;
        // A child's staged parent, or a set when more than one row links it,
        // rented from the client until the commit ends.
        private Dictionary<string, object>? parentCandidates;
        private Dictionary<string, HashSet<(NeoValueOwnership scope, string id)>>? copiedListenerCarriers;
        // Each staged row's prior state while a checkpoint is open.
        private List<RowEntry>? journal;
        private int openCheckpoints;
        internal Dictionary<(NeoValueOwnership ownership, NeoValueOwnership scope, string rootId, string ownerId), Dictionary<string, NeoDelegateValue[]>?>? ListenerEntries;
        private List<((NeoValueOwnership ownership, NeoValueOwnership scope, string rootId, string ownerId) key, bool present, Dictionary<string, NeoDelegateValue[]>? entry)>? listenerJournal;
        internal readonly struct ListenerMove
        {
            internal readonly NeoValueOwnership Scope;
            internal readonly string Root;
            internal readonly NeoValueOwnership Target;
            internal readonly string OwnerId;
            internal readonly string TargetOwnerId;
            internal ListenerMove(NeoValueOwnership scope, string root, NeoValueOwnership target,
                string ownerId, string? targetOwnerId = null)
            {
                Scope = scope;
                Root = root;
                Target = target;
                OwnerId = ownerId;
                TargetOwnerId = targetOwnerId ?? ownerId;
            }
        }
        internal Dictionary<(NeoValueOwnership scope, string id), ListenerMove>? ListenerMoves;
        // Null scope follows pre-commit global receiver resolution. An explicit
        // scope identifies an owner, or a durable receiver in the Save store.
        private Dictionary<(NeoValueOwnership? scope, string id), string>? listenerRenames;
        private List<((NeoValueOwnership scope, string id) key, bool present, ListenerMove previous)>? listenerMoveJournal;

        internal void SetListenerMove((NeoValueOwnership scope, string id) key, ListenerMove move)
        {
            ListenerMoves ??= new();
            bool present = ListenerMoves.TryGetValue(key, out var previous);
            if (!present || previous.TargetOwnerId != move.TargetOwnerId)
                listenerRenames = null;
            if (openCheckpoints != 0)
                (listenerMoveJournal ??= new()).Add((key, present, previous));
            ListenerMoves[key] = move;
        }

        internal Dictionary<(NeoValueOwnership? scope, string id), string> ListenerRenames()
        {
            if (listenerRenames is not null)
                return listenerRenames;
            listenerRenames = new();
            if (ListenerMoves is not null)
                foreach (var pair in ListenerMoves)
                    if (pair.Key.id != pair.Value.TargetOwnerId)
                    {
                        listenerRenames[pair.Key] = pair.Value.TargetOwnerId;
                        if (!Client.TryGetCommittedOwnership(pair.Key.id, out var scope) || scope == pair.Key.scope)
                            listenerRenames[(null, pair.Key.id)] = pair.Value.TargetOwnerId;
                    }
            return listenerRenames;
        }
        internal bool FindListenerMove(NeoValueOwnership scope, string id,
            out (NeoValueOwnership scope, string id) key, out ListenerMove move)
        {
            key = (scope, id);
            move = default;
            if (ListenerMoves is null)
                return false;
            if (ListenerMoves.TryGetValue(key, out move))
                return true;
            key = (NeoValueOwnership.Session, id);
            return scope == NeoValueOwnership.Save && ListenerMoves.TryGetValue(key, out move) && move.Target == scope;
        }

        /// <summary>The held script batch this plan stages for, which every row it stages touches.</summary>
        internal NeoWriteBatch? HeldBy;

        internal NeoWritePlan(NeoClient client, int? requiredSaveFormatRevision = null)
        {
            Client = client;
            RequiredSaveFormatRevision = requiredSaveFormatRevision;
            BaseRevision = client.WriteRevision;
        }

        /// <summary>
        /// Takes the current revision as this plan's base. Only a held
        /// script batch's plan does: it commits before anything else reads
        /// or writes a row it stages.
        /// </summary>
        internal void Rebase() => BaseRevision = Client.WriteRevision;

        /// <summary>Opens a checkpoint <see cref="Rollback"/> returns the plan to.</summary>
        internal Checkpoint Open()
        {
            openCheckpoints++;
            return new Checkpoint((journal ??= new List<RowEntry>()).Count, afterCommit.Count, listenerJournal?.Count ?? 0, listenerMoveJournal?.Count ?? 0);
        }

        /// <summary>Keeps what was staged since the last open checkpoint.</summary>
        internal void Close()
        {
            if (--openCheckpoints == 0)
            {
                journal!.Clear();
                listenerJournal?.Clear();
                listenerMoveJournal?.Clear();
            }
        }

        /// <summary>Drops what was staged since <paramref name="checkpoint"/>.</summary>
        internal void Rollback(Checkpoint checkpoint)
        {
            listenerRenames = null;
            if (listenerMoveJournal is not null)
            {
                for (int index = listenerMoveJournal.Count - 1; index >= checkpoint.ListenerMoveJournal; index--)
                {
                    var entry = listenerMoveJournal[index];
                    if (entry.present)
                        ListenerMoves![entry.key] = entry.previous;
                    else
                        ListenerMoves!.Remove(entry.key);
                }
                listenerMoveJournal.RemoveRange(checkpoint.ListenerMoveJournal, listenerMoveJournal.Count - checkpoint.ListenerMoveJournal);
            }
            if (listenerJournal is not null)
            {
                for (int index = listenerJournal.Count - 1; index >= checkpoint.ListenerJournal; index--)
                {
                    var entry = listenerJournal[index];
                    if (entry.present)
                        ListenerEntries![entry.key] = entry.entry;
                    else
                        ListenerEntries!.Remove(entry.key);
                    Version++;
                }
                listenerJournal.RemoveRange(checkpoint.ListenerJournal, listenerJournal.Count - checkpoint.ListenerJournal);
            }
            for (int index = journal!.Count - 1; index >= checkpoint.Journal; index--)
            {
                RowEntry entry = journal[index];
                Version++;
                if (entry.Staged)
                    Rows[entry.Key] = entry.Row;
                else
                    Rows.Remove(entry.Key);
                if (entry.ChangedField is not null)
                    (changedFields ??= new())[entry.Key] = entry.ChangedField;
                else
                    changedFields?.Remove(entry.Key);
                if (entry.Silent)
                    (silentRows ??= new()).Add(entry.Key);
                else
                    silentRows?.Remove(entry.Key);
            }
            journal.RemoveRange(checkpoint.Journal, journal.Count - checkpoint.Journal);
            afterCommit.Truncate(checkpoint.AfterCommit);
            containerCandidates = null;
            copiedListenerCarriers = null;
            ReleaseParentCandidates();
            Close();
        }

        private void Record((NeoValueOwnership ownership, string id) key)
        {
            if (openCheckpoints == 0)
                return;
            bool staged = Rows.TryGetValue(key, out MemberValue? row);
            journal!.Add(new RowEntry(key, staged, row, ChangedField(key), IsSilent(key)));
        }

        internal readonly struct Checkpoint
        {
            internal readonly int Journal;
            internal readonly int AfterCommit;
            internal readonly int ListenerJournal;
            internal readonly int ListenerMoveJournal;

            internal Checkpoint(int journal, int afterCommit, int listenerJournal, int listenerMoveJournal)
            {
                Journal = journal;
                AfterCommit = afterCommit;
                ListenerJournal = listenerJournal;
                ListenerMoveJournal = listenerMoveJournal;
            }
        }

        private readonly struct RowEntry
        {
            internal readonly (NeoValueOwnership ownership, string id) Key;
            internal readonly bool Staged;
            internal readonly MemberValue? Row;
            internal readonly string? ChangedField;
            internal readonly bool Silent;

            internal RowEntry((NeoValueOwnership ownership, string id) key, bool staged, MemberValue? row, string? changedField, bool silent)
            {
                Key = key;
                Staged = staged;
                Row = row;
                ChangedField = changedField;
                Silent = silent;
            }
        }

        internal void Set(NeoValueOwnership ownership, MemberValue row, string? changedField = null, bool silent = false)
        {
            if (ownership == NeoValueOwnership.Asset)
                throw new InvalidOperationException("Cannot write immutable asset data.");
            Client.ThrowIfDepartedWrite(row.id);
            var key = (ownership, row.id);
            Record(key);
            Restage(key, row);
            containerCandidates = null;
            if (changedField is not null)
                (changedFields ??= new())[key] = changedField;
            else
                changedFields?.Remove(key);
            if (silent)
                (silentRows ??= new()).Add(key);
            else
                silentRows?.Remove(key);
        }

        internal void SetListenerEntry(NeoValueOwnership ownership, string rootId, string ownerId,
            Dictionary<string, NeoDelegateValue[]>? entry, NeoValueOwnership? scope = null)
        {
            if (ownership == NeoValueOwnership.Asset)
                throw new InvalidOperationException("Cannot write runtime listeners to immutable asset data.");
            var key = (ownership, scope: scope ?? ownership, rootId, ownerId);
            ListenerEntries ??= new();
            if (openCheckpoints != 0)
            {
                bool present = ListenerEntries.TryGetValue(key, out var previous);
                (listenerJournal ??= new()).Add((key, present, previous));
            }
            ListenerEntries[key] = entry;
            Version++;
        }

        internal string? ChangedField((NeoValueOwnership ownership, string id) key) =>
            changedFields is not null && changedFields.TryGetValue(key, out string? field) ? field : null;

        internal bool IsSilent((NeoValueOwnership ownership, string id) key) => silentRows?.Contains(key) == true;

        internal bool TryGetNodeBinding(NeoMember node, [NotNullWhen(true)] out string? valueId)
        {
            valueId = null;
            return nodeBindings?.TryGetValue(node, out valueId) == true;
        }

        internal void BindNode(NeoMember node, string valueId) => (nodeBindings ??= new())[node] = valueId;

        internal IEnumerable<KeyValuePair<NeoMember, string>> NodeBindings =>
            nodeBindings ?? (IEnumerable<KeyValuePair<NeoMember, string>>)Array.Empty<KeyValuePair<NeoMember, string>>();

        internal void Remove(NeoValueOwnership ownership, string id)
        {
            if (ownership == NeoValueOwnership.Asset)
                throw new InvalidOperationException("Cannot remove immutable asset data.");
            Client.ThrowIfDepartedWrite(id);
            var key = (ownership, id);
            Record(key);
            Restage(key, null);
            silentRows?.Remove(key);
        }

        internal MemberValue? Resolve(NeoValueOwnership ownership, string id)
        {
            if (Rows.TryGetValue((ownership, id), out MemberValue? candidate))
            {
                if (candidate is not null)
                    return candidate.IsRemoved ? null : candidate;
                return Client.TryGetCommittedValue(NeoValueOwnership.Asset, id, out MemberValue? fallback) ? fallback : null;
            }
            if (Client.ReplayAllocation(id) is MemberValue allocation)
                return allocation;
            return Client.ResolveWritePlanFallback(this, ownership, id);
        }

        internal bool TryGetWritable(NeoValueOwnership ownership, string id, out MemberValue? row)
        {
            if (Rows.TryGetValue((ownership, id), out row))
                return row is not null;
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
            if (TryGetWritable(NeoValueOwnership.Session, id, out MemberValue? session))
                return session;
            if (TryGetWritable(NeoValueOwnership.Save, id, out MemberValue? save))
                return save;
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
            {
                ownership = NeoValueOwnership.Session;
                return true;
            }
            if (TryGetWritable(NeoValueOwnership.Save, id, out _))
            {
                ownership = NeoValueOwnership.Save;
                return true;
            }
            return Client.TryGetCommittedOwnership(id, out ownership);
        }

        internal IEnumerable<(NeoValueOwnership scope, string id)> CopiedListenerCarriers(string ownerId)
        {
            if (copiedListenerCarriers is null)
            {
                copiedListenerCarriers = new(StringComparer.Ordinal);
                foreach (var row in Rows)
                    IndexCopiedListenerCarrier(row.Key, row.Value, add: true);
            }
            return copiedListenerCarriers.TryGetValue(ownerId, out var roots)
                ? roots : Array.Empty<(NeoValueOwnership, string)>();
        }

        private void IndexCopiedListenerCarrier((NeoValueOwnership scope, string id) key, MemberValue? row, bool add)
        {
            if (copiedListenerCarriers is null || row?.copiedChangeListeners is not { } map)
                return;
            foreach (string ownerId in map.Keys)
            {
                if (!copiedListenerCarriers.TryGetValue(ownerId, out var roots))
                {
                    if (!add)
                        continue;
                    copiedListenerCarriers[ownerId] = roots = new();
                }
                if (add)
                    roots.Add(key);
                else
                {
                    roots.Remove(key);
                    if (roots.Count == 0)
                        copiedListenerCarriers.Remove(ownerId);
                }
            }
        }

        /// <summary>Stages <paramref name="row"/> at <paramref name="key"/>, keeping a built parent index current.</summary>
        private void Restage((NeoValueOwnership ownership, string id) key, MemberValue? row)
        {
            if (HeldBy is not null)
                HeldBy.Touch(key.id);
            else if (Client.PendingScriptWrites is not null)
                Client.ObserveScriptWrites(key.id);
            if (parentCandidates is not null && Rows.TryGetValue(key, out MemberValue? previous))
                IndexParentCandidates(key.ownership, previous, add: false);
            if (Rows.TryGetValue(key, out var previousCarrier))
                IndexCopiedListenerCarrier(key, previousCarrier, add: false);
            Rows[key] = row;
            IndexCopiedListenerCarrier(key, row, add: true);
            Version++;
            if (parentCandidates is not null)
                IndexParentCandidates(key.ownership, row, add: true);
        }

        /// <summary>
        /// Adds to <paramref name="into"/> the staged rows that may link <paramref name="childId"/>, beyond
        /// the links the client's committed placement index already holds.
        /// Every caller unions these with that index, so a staged row only
        /// contributes the children past its unchanged leading ones: an append
        /// to a long list row indexes one child, not the whole list. Built
        /// once, then kept current as rows stage: a plan that stages many
        /// rows and asks after each would otherwise rebuild it each time.
        /// </summary>
        internal void CollectParentCandidates(string childId, ICollection<string> into)
        {
            if (parentCandidates is null)
            {
                parentCandidates = Client.RentParentIndex();
                foreach (var pair in Rows)
                    IndexParentCandidates(pair.Key.ownership, pair.Value, add: true);
            }
            if (!parentCandidates.TryGetValue(childId, out object? parentOrSet))
                return;
            if (parentOrSet is HashSet<string> parentSet)
            {
                foreach (string parent in parentSet)
                    into.Add(parent);
            }
            else
                into.Add((string)parentOrSet);
        }

        private void IndexParentCandidates(NeoValueOwnership ownership, MemberValue? row, bool add)
        {
            if (row is not ObjectMemberValue and not ArrayMemberValue)
                return;
            // An array row's ids are its children; a record's are collected
            // rather than enumerated, which would cost an enumerator call each.
            string[]? ids = (row as ArrayMemberValue)?.value;
            List<string> collected = Client.RentIdList();
            try
            {
                if (row is ObjectMemberValue)
                    NeoClient.CollectPlacementChildIds(row, collected);
                string[]? committed = Client.IndexedPlacementChildren(ownership, row.id);
                int prefix = committed is null ? 0 : NeoClient.SharedPrefix(committed, ids, collected);
                int count = ids?.Length ?? collected.Count;
                for (int index = prefix; index < count; index++)
                {
                    // An id array is indexed directly, not through IReadOnlyList.
                    string child = ids is not null ? ids[index] : collected[index];
                    if (!parentCandidates!.TryGetValue(child, out object? parents))
                    {
                        if (add)
                            parentCandidates[child] = row.id;
                    }
                    else if (parents is HashSet<string> set)
                    {
                        if (add)
                            set.Add(row.id);
                        else
                            set.Remove(row.id);
                    }
                    else if (!string.Equals((string)parents, row.id, StringComparison.Ordinal))
                    {
                        if (add)
                            parentCandidates[child] = new HashSet<string>(StringComparer.Ordinal) { (string)parents, row.id };
                    }
                    else if (!add)
                        parentCandidates.Remove(child);
                }
            }
            finally
            {
                Client.ReturnIdList(collected);
            }
        }

        /// <summary>Drops the staged parent index, which no caller holds past a lookup, back to the client's pool.</summary>
        internal void ReleaseParentCandidates()
        {
            if (parentCandidates is not null)
                Client.ReturnParentIndex(parentCandidates);
            parentCandidates = null;
        }

        internal IEnumerable<string> ContainerCandidates(string containerId)
        {
            if (containerCandidates is null)
            {
                containerCandidates = new Dictionary<string, HashSet<string>>();
                foreach (MemberValue? row in Rows.Values)
                {
                    if (string.IsNullOrEmpty(row?.containerId))
                        continue;
                    if (!containerCandidates.TryGetValue(row!.containerId!, out var ids))
                        containerCandidates[row.containerId!] = ids = new HashSet<string>();
                    ids.Add(row.id);
                }
            }
            return containerCandidates.TryGetValue(containerId, out var candidates)
                ? candidates : Array.Empty<string>();
        }

        internal void Bind(NeoValueOwnership ownership, string memberId, bool present, string? valueId)
        {
            Version++;
            (bindings ??= new())[(ownership, memberId)] = (present, valueId);
        }

        internal void AfterNotifications(Action callback) => afterNotifications.Add(callback);
        internal void AfterNotifications(INeoPlanCallback callback) => afterNotifications.Add(callback);

        /// <summary>
        /// Marks <paramref name="node"/> as the collection mutator behind this
        /// plan: it publishes one precise change itself, so its row change
        /// must not raise a second, unknown one.
        /// </summary>
        internal void ReportsOwnChange(NeoMember node)
        {
            if (reportingNode is null)
                reportingNode = node;
            else if (!IsReportingOwnChange(node))
                (moreReportingNodes ??= new List<NeoMember>()).Add(node);
        }

        internal bool IsReportingOwnChange(NeoMember node)
        {
            if (ReferenceEquals(reportingNode, node))
                return true;
            if (moreReportingNodes is not null)
                for (int index = 0; index < moreReportingNodes.Count; index++)
                    if (ReferenceEquals(moreReportingNodes[index], node))
                        return true;
            return false;
        }

        /// <summary>
        /// The member of the reporting node bound to row <paramref name="valueId"/>:
        /// a collection mutator knows its own member, which spares inferring it.
        /// </summary>
        internal Member? ReportingMember(string valueId)
        {
            if (reportingNode is null)
                return null;
            if ((reportingNode.overrideValueId ?? reportingNode.value?.id) == valueId)
                return reportingNode.member;
            if (moreReportingNodes is not null)
                for (int index = 0; index < moreReportingNodes.Count; index++)
                {
                    NeoMember node = moreReportingNodes[index];
                    if ((node.overrideValueId ?? node.value?.id) == valueId)
                        return node.member;
                }
            return null;
        }

        internal void NotifyCompleted() => afterNotifications.Run();
        internal void AfterCommit(Action callback) => afterCommit.Add(callback);
        internal void NotifyCommitted() => afterCommit.Run();

        internal void Commit() => Client.CommitWritePlan(this);

        /// <summary>
        /// Callbacks in the order added: an <see cref="Action"/> or an
        /// <see cref="INeoPlanCallback"/>. Most plans add one, which needs no list.
        /// </summary>
        private struct Callbacks
        {
            private object? first;
            private List<object>? rest;

            internal int Count => first is null ? 0 : 1 + (rest?.Count ?? 0);

            internal void Truncate(int count)
            {
                if (count == 0)
                {
                    first = null;
                    rest = null;
                }
                else
                    rest?.RemoveRange(count - 1, rest.Count - (count - 1));
            }

            internal void Add(object callback)
            {
                if (first is null)
                    first = callback;
                else
                    (rest ??= new List<object>()).Add(callback);
            }

            internal void Run()
            {
                if (first is not null)
                    Invoke(first);
                if (rest is null)
                    return;
                foreach (object callback in rest)
                    Invoke(callback);
            }

            private static void Invoke(object callback)
            {
                if (callback is Action action)
                    action();
                else
                    ((INeoPlanCallback)callback).Run();
            }
        }
    }

    /// <summary>Plan work that carries its own state, so it needs no closure and delegate.</summary>
    internal interface INeoPlanCallback
    {
        void Run();
    }
}

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
#if NEO_COMPOSE_PROFILING
        private static readonly Unity.Profiling.ProfilerMarker CommitWriteMarker = new("NeoCompose.Write.Commit");
#endif
        internal long WriteRevision
        {
            get; private set;
        }

        // The revision of the latest write a prepared plan conflicts with. A
        // held script batch's commits don't count: a plan that reads or
        // writes a row the batch stages commits it first.
        private long foreignWriteRevision;

        private NeoWritePlan? candidateReadPlan;
        internal MemberValue? ResolveWritePlanGlobalFallback(NeoWritePlan plan, string id)
        {
            if (candidateReadPlan is not null && !ReferenceEquals(candidateReadPlan, plan))
                return candidateReadPlan.Resolve(id);
            if (data.values.TryGetValue(id, out MemberValue asset))
                return asset;
            return TryResolveVirtualValue(id, out MemberValue virtualRow) ? virtualRow : null;
        }

        internal MemberValue? ResolveWritePlanFallback(NeoWritePlan plan, NeoValueOwnership ownership, string id)
        {
            if (candidateReadPlan is not null && !ReferenceEquals(candidateReadPlan, plan))
                return candidateReadPlan.Resolve(ownership, id);
            return TryGetCommittedOverlaidValue(ownership, id, out MemberValue? row) ? row : null;
        }

        internal CandidateReadScope ReadCandidate(NeoWritePlan plan)
        {
            NeoWritePlan? previous = candidateReadPlan;
            candidateReadPlan = plan;
            return new CandidateReadScope(this, previous);
        }

        /// <summary>Restores the previous candidate plan; a struct, so a scope allocates nothing.</summary>
        internal readonly struct CandidateReadScope : IDisposable
        {
            private readonly NeoClient client;
            private readonly NeoWritePlan? previous;

            internal CandidateReadScope(NeoClient client, NeoWritePlan? previous)
            {
                this.client = client;
                this.previous = previous;
            }

            public void Dispose() => client.candidateReadPlan = previous;
        }

        internal event Action<IReadOnlyCollection<(NeoValueOwnership ownership, string valueId)>, NeoWritePlan>? OnWritableValuesPublished;
        internal event Action<IReadOnlyCollection<(NeoValueOwnership ownership, string valueId)>>? OnWritableValuesChanged;

        private static bool WritesAnyChild(NeoWritePlan plan, NeoValueOwnership ownership, ObjectMemberValue parentRow)
        {
            foreach (string childId in parentRow.value!.Values)
                if (plan.Rows.ContainsKey((ownership, childId)))
                    return true;
            return false;
        }

        private readonly List<(NeoValueOwnership scope, MemberValue? before, MemberValue? after)> commitListenerRowsScratch = new();
        private bool commitScratchInUse;
        private readonly HashSet<(NeoValueOwnership ownership, string valueId)> commitChangedScratch = new();
        private readonly Dictionary<(NeoValueOwnership ownership, string id), string> commitOldContainersScratch = new();
        private readonly HashSet<(NeoValueOwnership ownership, string id)> commitSameMembershipScratch = new();

        private static bool HasSaveRow(NeoWritePlan plan)
        {
            foreach (var pair in plan.Rows)
            {
                if (pair.Key.ownership == NeoValueOwnership.Save && pair.Value is not null)
                    return true;
            }
            return false;
        }

        internal void CommitWritePlan(NeoWritePlan plan)
        {
            if (scriptWriteBatch is null)
            {
                CommitPreparedPlan(plan);
                return;
            }
            commitsUnderScriptBatch++;
            try
            {
                CommitPreparedPlan(plan);
            }
            finally
            {
                commitsUnderScriptBatch--;
            }
        }

        private void CommitPreparedPlan(NeoWritePlan plan)
        {
#if NEO_COMPOSE_PROFILING
            using var marker = CommitWriteMarker.Auto();
#endif
            if (nestedConstructedRows is not null)
                foreach (var key in plan.Rows.Keys)
                    if (nestedConstructedRows.TryGetValue(key.id, out var producer)
                        && !ReferenceEquals(producer, nestedConstructorCapture))
                        producer.HasExternalWrites = true;
            if (plan.Bindings.Count != 0)
                for (var scope = nestedConstructorCapture; scope is not null; scope = scope.Parent)
                    scope.HasExternalWrites = true;
            if (nestedConstructorCapture is not null)
                foreach (var key in plan.Rows.Keys)
                {
                    bool exists = ResolveValueRow(key.id) is not null;
                    for (var scope = nestedConstructorCapture; scope is not null; scope = scope.Parent)
                    {
                        if (!exists)
                            scope.Allocations.Add(key.id);
                        else if (!scope.Allocations.Contains(key.id))
                            scope.HasExternalWrites = true;
                    }
                }
            if (replayAllocationScope is not null && candidateReplay is null)
                foreach (var pair in plan.Rows)
                    if (pair.Key.ownership == NeoValueOwnership.Session && pair.Value is not null
                        && !sessionData.values.ContainsKey(pair.Key.id))
                        RecordReplayAllocation(pair.Key.id);
            if (candidateReplay is not null)
            {
                candidateReplay.Apply(plan);
                return;
            }
            if (!ReferenceEquals(plan.Client, this))
                throw new ArgumentException("Write plan belongs to another client.", nameof(plan));
            // Staging adds rows, so it walks a snapshot, taken only when a Save row needs one.
            if (HasSaveRow(plan))
                foreach (var pair in plan.Rows.ToArray())
                    if (pair.Key.ownership == NeoValueOwnership.Save && pair.Value is not null)
                        StageConstructorDependencies(plan, pair.Value);
            foreach (var pair in plan.Rows)
                if (pair.Value is not null)
                    StampMapKeyForWrite(pair.Key.ownership, pair.Value);

            if (plan.BaseRevision < foreignWriteRevision)
                throw new InvalidOperationException("The data graph changed while this write was being prepared.");
            int? requiredSaveFormatRevision = NeoSaveFormat.Combine(saveData.requiredSaveFormatRevision, plan.RequiredSaveFormatRevision);
            CandidateReplay? preparedExpansions = ValidatePreparedWrite(plan, out var preparedListeners);
            if (plan.BaseRevision < foreignWriteRevision)
                throw new InvalidOperationException("The data graph changed during candidate validation.");
            foreach (var row in plan.Rows)
                if (TryGetCommittedOwnership(row.Key.id, out var previousOwnership)
                    && previousOwnership == row.Key.ownership
                    && ReplayRowsEqual(PreviousReplayRow(row.Key.id), row.Value))
                    (plan.UnchangedValueIds ??= new()).Add(row.Key.id);
            // Notifications can commit again before this commit returns, so
            // the scratch sets serve only the outermost commit.
            bool pooledScratch = !commitScratchInUse;
            commitScratchInUse = true;
            var listenerRows = pooledScratch ? commitListenerRowsScratch : new List<(NeoValueOwnership scope, MemberValue? before, MemberValue? after)>();
            HashSet<(NeoValueOwnership ownership, string valueId)> changed = pooledScratch ? commitChangedScratch : new();
            Dictionary<(NeoValueOwnership ownership, string id), string> oldContainers = pooledScratch ? commitOldContainersScratch : new();
            // Rows that stay live in the same container. Their list wrappers
            // hear about them through the row's own id, so the container
            // itself did not change.
            HashSet<(NeoValueOwnership ownership, string id)> sameMembership = pooledScratch ? commitSameMembershipScratch : new();
            bool batched = false;
            bool held = false;
            bool gridChange = false;
            HashSet<(NeoValueOwnership scope, string id)>? renamedOwners = null;
            if (plan.ListenerMoves is not null)
                foreach (var move in plan.ListenerMoves)
                    if (move.Key.id != move.Value.TargetOwnerId)
                        (renamedOwners ??= new()).Add((move.Value.Target, move.Value.TargetOwnerId));
            try
            {
                foreach (var pair in plan.Rows)
                {
                    if (plan.IsSilent(pair.Key))
                        continue;
                    TryGetValue(pair.Key.ownership, pair.Key.id, out MemberValue? previous);
                    if (CurrentChangeSource != NeoChangeSource.External
                        || !NeoSemanticJson.MemberRowsEqual(previous, pair.Value, ignoreChangeListeners: true))
                        listenerRows.Add((pair.Key.ownership, previous, pair.Value));
                }
                foreach (var pair in plan.Rows)
                {
                    if (TryResolveContainerIdForValueId(pair.Key.id, out string? containerId))
                    {
                        if (pair.Value is { IsRemoved: false } next
                            && TryGetCommittedValue(pair.Key.ownership, pair.Key.id, out MemberValue? previous)
                            && !previous.IsRemoved
                            && previous.containerId == next.containerId)
                            sameMembership.Add(pair.Key);
                        else
                            oldContainers[pair.Key] = containerId!;
                        changed.Add((pair.Key.ownership, containerId!));
                    }
                    if (!string.IsNullOrEmpty(pair.Value?.containerId))
                        changed.Add((pair.Key.ownership, pair.Value!.containerId!));
                    changed.Add(pair.Key);
                }
                foreach (var binding in plan.Bindings)
                {
                    var current = GetWritableStore(binding.Key.ownership).staticBindings;
                    if (current.TryGetValue(binding.Key.memberId, out string? prior) && prior is not null)
                        changed.Add((binding.Key.ownership, prior));
                    if (binding.Value.valueId is string next)
                        changed.Add((binding.Key.ownership, next));
                }
                // Grid indexes go stale with the rows, and stay so until their
                // change publishes: a getter that read a grid is no hit
                // until then.
                BeginGridChange();
                gridChange = true;
                NoteDepartures(plan);
                foreach (var pair in plan.Rows)
                {
                    if (pair.Value is null)
                    {
                        GetWritableStore(pair.Key.ownership).values.Remove(pair.Key.id);
                        IndexStoreRemove(pair.Key.ownership, pair.Key.id);
                    }
                    else
                        StoreWritableValue(pair.Key.ownership, pair.Value);
                    TouchWritableStoreUpdatedAt(pair.Key.ownership);
                }
                foreach (var pair in plan.Bindings)
                {
                    var bindings = GetWritableStore(pair.Key.ownership).staticBindings;
                    if (pair.Value.present)
                        bindings[pair.Key.memberId] = pair.Value.valueId;
                    else
                        bindings.Remove(pair.Key.memberId);
                    TouchWritableStoreUpdatedAt(pair.Key.ownership);
                }
                CommitListenerEntries(plan, preparedListeners);
                saveData.requiredSaveFormatRevision = NeoSaveFormat.Combine(saveData.requiredSaveFormatRevision, requiredSaveFormatRevision);
                WriteRevision++;
                if (plan.HeldBy is null)
                    foreignWriteRevision = WriteRevision;
                BeginChangeBatch();
                batched = true;
                foreach (var row in listenerRows)
                    RecordListenerRow(row.scope, row.before, row.after,
                        row.after is not null && renamedOwners?.Contains((row.scope, row.after.id)) == true);
                // Effects the commit queues run once it publishes, inside its change batch.
                HoldGetterChanges();
                held = true;
                InstallCandidateExpansions(preparedExpansions, changed);
                if (plan.Bindings.Count != 0)
                {
                    InvalidateGetterMemo();
                    foreach (var binding in plan.Bindings)
                        InvalidateGetterMemoForRow(StaticReadKey(binding.Key.memberId, binding.Key.ownership));
                    NoteEffectBindingChange();
                }
                InvalidateGetterMemoForRows(changed);
                if (sharedEvaluationContext is not null)
                    foreach (var item in changed)
                        if (!plan.Rows.ContainsKey(item) || plan.IsSilent(item))
                            RefreshSharedEvaluationRow(item.ownership, item.valueId);
                OnWritableValuesPublished?.Invoke(changed, plan);
                plan.NotifyCommitted();
                OnWritableValuesChanged?.Invoke(changed);
                if (plan.ListenerEntries is not null)
                    foreach (var change in plan.ListenerEntries)
                        if (change.Key.ownership == NeoValueOwnership.Save)
                            RaiseSaveListenerChanged(change.Key.rootId, change.Key.ownerId);
                EndGridChange();
                gridChange = false;
                using (SuspendContainerNotifications())
                {
                    foreach (var pair in plan.Rows)
                    {
                        if (plan.IsSilent(pair.Key))
                        {
                            if (pair.Key.ownership == NeoValueOwnership.Save
                                && !suppressLiveAutoCommit && loader is NeoSaveSynchronizer synchronizer)
                                synchronizer.MarkDirtyValue(pair.Key.id, null);
                            continue;
                        }
                        NotifyWritableValueChanged(pair.Key.ownership, pair.Key.id, plan.ChangedField(pair.Key),
                            valueChanged: !(plan.UnchangedValueIds?.Contains(pair.Key.id) == true
                                && pair.Value is ObjectMemberValue { classId: not null, value: not null } parentRow
                                && WritesAnyChild(plan, pair.Key.ownership, parentRow)),
                            membershipChanged: !sameMembership.Contains(pair.Key), plan: plan);
                        if (oldContainers.TryGetValue(pair.Key, out string? containerId))
                            RaiseContainerChanged(pair.Key.ownership, containerId, plan);
                    }
                    foreach (var item in changed)
                        if (!plan.Rows.ContainsKey((item.ownership, item.valueId))
                            && preparedExpansions?.HiddenVirtualIds.Contains(item.valueId) == true)
                            PublishWritableValueChange(item.ownership, item.valueId, plan);
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
                    listenerRows.Clear();
                    oldContainers.Clear();
                    sameMembership.Clear();
                    commitScratchInUse = false;
                }
                if (gridChange)
                    EndGridChange();
                try
                {
                    if (held)
                        ReleaseGetterChanges();
                }
                finally
                {
                    if (batched)
                        EndChangeBatch();
                    plan.ReleaseParentCandidates();
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
            List<string> removals = RentIdList();
            HashSet<string> visited = RentIdSet();
            try
            {
                foreach (var root in roots)
                    StageOwnedRemoval(plan, ownership, root.valueId, root.member, false, visited, null, removals);
                RemoveUnreachable(plan, ownership, removals);
            }
            finally
            {
                ReturnIdList(removals);
                ReturnIdSet(visited);
            }
        }

        /// <summary>The one-row form above, which most removals are.</summary>
        internal void StageUnlinkedRemovals(
            NeoWritePlan plan, NeoValueOwnership ownership, string valueId, Member? member)
        {
            List<string> removals = RentIdList();
            HashSet<string> visited = RentIdSet();
            try
            {
                StageOwnedRemoval(plan, ownership, valueId, member, false, visited, null, removals);
                RemoveUnreachable(plan, ownership, removals);
            }
            finally
            {
                ReturnIdList(removals);
                ReturnIdSet(visited);
            }
        }

        private void RemoveUnreachable(NeoWritePlan plan, NeoValueOwnership ownership, List<string> removals)
        {
            if (removals.Count == 0)
                return;
            HashSet<string> reachable;
            using (ReadCandidate(plan))
            {
                if (CanProveUnreachable(ownership, removals))
                {
                    foreach (string id in removals)
                        RemoveOwnedRow(plan, ownership, id);
                    return;
                }
                reachable = BuildReachableWritableValueIds(ownership);
            }
            foreach (string id in removals)
                if (!reachable.Contains(id))
                    RemoveOwnedRow(plan, ownership, id);
        }

        /// <summary>
        /// Stages <paramref name="next"/> over the row at its own id. An
        /// in-place replacement keeps the slot id, so nothing else frees what
        /// the replaced row owned. It releases the owned children that
        /// <paramref name="next"/> no longer links, plus the replaced
        /// instance's sparse footprint.
        /// </summary>
        internal void StageInPlaceReplacement(
            NeoWritePlan plan, NeoValueOwnership ownership, MemberValue next, Member? member,
            string? changedField = null)
        {
            // A pending collection's row is the one replaced, and preparing
            // the batch must not restage it over the replacement.
            MemberValue? previous = plan.HeldBy?.PendingRow(ownership, next.id) ?? plan.Resolve(ownership, next.id);
            plan.HeldBy?.Discard(ownership, next.id);
            plan.Set(ownership, next, changedField);
            plan.AfterCommit(() => pendingListenerChanges.Replacements.Add((ownership, next.id)));
            StageVirtualFootprintRemoval(plan, ownership, next.id);
            if (previous is null)
                return;
            List<(string valueId, Member? member)>? released = null;
            foreach (var child in EnumerateOwnedChildLinks(previous, member))
                if (ChildOwnership(child.member, ownership) == ownership)
                    (released ??= new()).Add(child);
            if (released is null)
                return;
            var kept = new HashSet<string>(EnumerateOwnedChildLinks(next, member).Select(child => child.valueId));
            released.RemoveAll(child => kept.Contains(child.valueId));
            if (released.Count != 0)
                StageUnlinkedRemovals(plan, ownership, released);
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
            if (reachable?.Contains(valueId) == true || !visited.Add(valueId))
                return;
            MemberValue? row = plan.HeldBy?.PendingRow(ownership, valueId) ?? plan.Resolve(ownership, valueId);
            if (row is not null)
            {
                foreach (var child in EnumerateOwnedChildLinks(row, member))
                {
                    NeoValueOwnership childOwnership = ChildOwnership(child.member, ownership);
                    if (childOwnership != ownership)
                        continue;
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
                plan.HeldBy?.Discard(ownership, valueId);
                NeoTimestamp now = NeoTimestamp.Now();
                plan.Set(ownership, new NullMemberValue
                {
                    id = valueId,
                    createdAt = now,
                    updatedAt = now,
                    mark = NeoValueMarks.Removed,
                }, "mark");
                StageVirtualFootprintRemoval(plan, ownership, valueId);
            }
            // A pending collection is writable once the held batch commits.
            else if (plan.TryGetWritable(ownership, valueId, out _) || plan.HeldBy?.Collection(ownership, valueId) is not null)
            {
                if (removals is not null)
                    removals.Add(valueId);
                else
                    RemoveOwnedRow(plan, ownership, valueId);
            }
        }

        private void RemoveOwnedRow(NeoWritePlan plan, NeoValueOwnership ownership, string valueId)
        {
            plan.HeldBy?.Discard(ownership, valueId);
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
            if (!virtualFootprintByRoot.TryGetValue(rootId, out var footprint))
                return;
            foreach (string id in footprint)
            {
                if (id == rootId)
                    continue;
                Drop(ownership, id);
                if (ownership == NeoValueOwnership.Save)
                    Drop(NeoValueOwnership.Session, id);
            }

            void Drop(NeoValueOwnership store, string id)
            {
                if (plan.TryGetWritable(store, id, out _))
                    StageOwnedRemoval(plan, store, id, TryInferMemberForValueId(id, out Member? member) ? member : null);
            }
        }
    }
}
