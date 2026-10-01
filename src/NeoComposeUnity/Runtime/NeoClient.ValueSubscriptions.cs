// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Buffers;
using System.Collections.Generic;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // One handler list per value id. A multicast delegate would copy its
        // whole invocation array on every subscribe and unsubscribe, which an
        // animation with sixty tracks sharing one dependency turns into an
        // O(n²) churn on every re-resolution.
        private readonly Dictionary<string, List<Action<NeoValueOwnership, string>>> writableValueSubscriptions = new(StringComparer.Ordinal);

        internal IDisposable SubscribeWritableValue(string valueId, Action<NeoValueOwnership, string> handler)
        {
            if (!writableValueSubscriptions.TryGetValue(valueId, out var handlers))
            {
                writableValueSubscriptions[valueId] = handlers = new List<Action<NeoValueOwnership, string>>(1);
                if (ExistingValueNode(valueId) is { } node)
                    node.subscribers = handlers;
            }
            handlers.Add(handler);
            return new WritableValueSubscription(this, valueId, handler);
        }

        private void UnsubscribeWritableValue(string valueId, Action<NeoValueOwnership, string> handler)
        {
            if (!writableValueSubscriptions.TryGetValue(valueId, out var handlers))
                return;
            if (!handlers.Remove(handler))
                return;
            if (handlers.Count != 0)
                return;
            writableValueSubscriptions.Remove(valueId);
            if (ExistingValueNode(valueId) is { } node)
                node.subscribers = null;
        }

        /// <summary>
        /// The plan whose own rows the current publication delivers, or null
        /// when a leaf, placement, constructor or removal write publishes.
        /// </summary>
        internal NeoWritePlan? PublishingPlan
        {
            get; private set;
        }

        private int changeBatchDepth;
        private List<(NeoMember node, NeoMember changed, NeoListChangedArgs? listChange)> pendingChanges = new();
        private List<(NeoMember node, NeoMember changed, NeoListChangedArgs? listChange)>? spareChanges;
        private readonly Dictionary<(NeoMember node, NeoMember changed), int> pendingChangeIndex = new();
        // A merged list change's ids, owned by the batch until it is raised,
        // so a commit of n entry changes merges in O(n).
        private readonly Dictionary<NeoListChangedArgs, (List<string> list, HashSet<string> set)> mergedListIds = new();

        /// <summary>
        /// Raises <paramref name="node"/>'s OnChanged. A commit publishes one
        /// change row by row, and a node can hear several of them (its own
        /// row, an entry it rebuilt, a membership), so inside a commit each
        /// (node, changed) pair is queued once and raised after every row is
        /// stored and published.
        /// </summary>
        internal void RaiseChanged(NeoMember node, NeoMember changed)
        {
            if (changeBatchDepth == 0)
            {
                node.InvokeChanged(changed, null);
                return;
            }
            NeoListChangedArgs? listChange = node.PendingListChange;
            if (pendingChangeIndex.TryGetValue((node, changed), out int index))
            {
                var pending = pendingChanges[index];
                pendingChanges[index] = (node, changed, MergeListChanges(pending.listChange, listChange));
                return;
            }
            pendingChangeIndex.Add((node, changed), pendingChanges.Count);
            pendingChanges.Add((node, changed, listChange));
        }

        private void BeginChangeBatch()
        {
            changeBatchDepth++;
        }

        private void EndChangeBatch()
        {
            if (--changeBatchDepth > 0 || pendingChanges.Count == 0)
                return;
            // A listener's own commit queues and raises its own batch.
            var draining = pendingChanges;
            pendingChanges = spareChanges ?? new();
            spareChanges = null;
            pendingChangeIndex.Clear();
            mergedListIds.Clear();
            try
            {
                foreach (var (node, changed, listChange) in draining)
                {
                    if (!node.isDisposed)
                        node.InvokeChanged(changed, listChange);
                }
            }
            finally
            {
                draining.Clear();
                spareChanges = draining;
            }
        }

        private NeoListChangedArgs? MergeListChanges(NeoListChangedArgs? first, NeoListChangedArgs? next)
        {
            if (first is null || next is null || ReferenceEquals(first, next))
                return first ?? next;
            if (first.Kind == NeoListChangeKind.Unknown || next.Kind == NeoListChangeKind.Unknown)
                return NeoListChangedArgs.Unknown;
            if (first.Kind == next.Kind && first.Kind is NeoListChangeKind.Add or NeoListChangeKind.Remove or NeoListChangeKind.Set)
            {
                NeoListChangedArgs merged = OwnListChange(first, out var ids);
                foreach (string id in EntryIds(next))
                {
                    if (ids.set.Add(id))
                        ids.list.Add(id);
                }
                return merged;
            }
            if (next.Kind == NeoListChangeKind.Set)
                return Subsumes(first, next);
            if (first.Kind == NeoListChangeKind.Set)
                return Subsumes(next, first);
            return NeoListChangedArgs.Unknown;

            // A change that names every entry a Set touched (adding, removing
            // or replacing it in the same commit), or replaces the whole
            // list, already reports that Set.
            NeoListChangedArgs Subsumes(NeoListChangedArgs change, NeoListChangedArgs set)
            {
                if (change.Kind == NeoListChangeKind.Replace
                    && change.AddedValueIds.Count == 0
                    && change.RemovedValueIds.Count == 0
                    && change.ReplacedValueIds.Count == 0)
                {
                    return change;
                }
                HashSet<string> named;
                if (mergedListIds.TryGetValue(change, out var owned))
                {
                    named = owned.set;
                }
                else
                {
                    named = new HashSet<string>(change.AddedValueIds, StringComparer.Ordinal);
                    named.UnionWith(change.RemovedValueIds);
                    named.UnionWith(change.ReplacedValueIds);
                }
                foreach (string id in set.ReplacedValueIds)
                {
                    if (!named.Contains(id))
                        return NeoListChangedArgs.Unknown;
                }
                return change;
            }
        }

        /// <summary>A batch-owned copy of <paramref name="change"/> whose ids a merge may extend.</summary>
        private NeoListChangedArgs OwnListChange(
            NeoListChangedArgs change,
            out (List<string> list, HashSet<string> set) ids)
        {
            if (mergedListIds.TryGetValue(change, out ids))
                return change;
            var entryIds = new List<string>(EntryIds(change));
            ids = (entryIds, new HashSet<string>(entryIds, StringComparer.Ordinal));
            NeoListChangedArgs owned = change.Kind switch
            {
                NeoListChangeKind.Add => new NeoListChangedArgs(change.Kind, addedValueIds: entryIds),
                NeoListChangeKind.Remove => new NeoListChangedArgs(change.Kind, removedValueIds: entryIds),
                _ => new NeoListChangedArgs(change.Kind, replacedValueIds: entryIds),
            };
            mergedListIds.Add(owned, ids);
            return owned;
        }

        /// <summary>The ids an Add, Remove or Set change carries.</summary>
        private static IReadOnlyList<string> EntryIds(NeoListChangedArgs change) => change.Kind switch
        {
            NeoListChangeKind.Add => change.AddedValueIds,
            NeoListChangeKind.Remove => change.RemovedValueIds,
            _ => change.ReplacedValueIds,
        };

        /// <param name="node">The live node of <paramref name="valueId"/>, when the caller holds it.</param>
        private void PublishWritableValueChange(
            NeoValueOwnership ownership, string valueId, NeoWritePlan? plan = null, NeoValueNode? node = null)
        {
            RefreshSharedEvaluationRow(ownership, valueId);
            List<Action<NeoValueOwnership, string>>? handlers = node is { live: true }
                ? node.subscribers
                : writableValueSubscriptions.TryGetValue(valueId, out var found) ? found : null;
            // Invoke over a snapshot so reentrant writes, subscriptions and
            // disposal during a callback neither skip nor repeat a handler.
            if (handlers is { Count: not 0 })
            {
                NeoWritePlan? outer = PublishingPlan;
                PublishingPlan = plan;
                try
                {
                    // A lone handler is read out before it runs, which is
                    // already a snapshot.
                    if (handlers.Count == 1)
                        handlers[0](ownership, valueId);
                    else
                        InvokeSnapshot(handlers, ownership, valueId);
                }
                finally
                {
                    PublishingPlan = outer;
                }
            }
            OnWritableValueChanged?.Invoke(ownership, valueId);
        }

        private static void InvokeSnapshot(
            List<Action<NeoValueOwnership, string>> handlers,
            NeoValueOwnership ownership,
            string valueId)
        {
            int count = handlers.Count;
            var snapshot = ArrayPool<Action<NeoValueOwnership, string>>.Shared.Rent(count);
            handlers.CopyTo(snapshot, 0);
            try
            {
                for (int i = 0; i < count; i++)
                    snapshot[i](ownership, valueId);
            }
            finally
            {
                Array.Clear(snapshot, 0, count);
                ArrayPool<Action<NeoValueOwnership, string>>.Shared.Return(snapshot);
            }
        }

        private sealed class WritableValueSubscription : IDisposable
        {
            private NeoClient? client;
            private readonly string valueId;
            private readonly Action<NeoValueOwnership, string> handler;

            internal WritableValueSubscription(NeoClient client, string valueId, Action<NeoValueOwnership, string> handler)
            {
                this.client = client;
                this.valueId = valueId;
                this.handler = handler;
            }

            public void Dispose()
            {
                NeoClient? owner = client;
                if (owner is null)
                    return;
                client = null;
                owner.UnsubscribeWritableValue(valueId, handler);
            }
        }
    }
}
