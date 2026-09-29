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
                writableValueSubscriptions[valueId] = handlers = new List<Action<NeoValueOwnership, string>>(1);
            handlers.Add(handler);
            return new WritableValueSubscription(this, valueId, handler);
        }

        private void UnsubscribeWritableValue(string valueId, Action<NeoValueOwnership, string> handler)
        {
            if (!writableValueSubscriptions.TryGetValue(valueId, out var handlers))
                return;
            if (!handlers.Remove(handler))
                return;
            if (handlers.Count == 0)
                writableValueSubscriptions.Remove(valueId);
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

        private static NeoListChangedArgs? MergeListChanges(NeoListChangedArgs? first, NeoListChangedArgs? next)
        {
            if (first is null || next is null || ReferenceEquals(first, next))
                return first ?? next;
            if (first.Kind == next.Kind && first.Kind is NeoListChangeKind.Add or NeoListChangeKind.Remove or NeoListChangeKind.Set)
            {
                return new NeoListChangedArgs(
                    first.Kind,
                    Union(first.RemovedValueIds, next.RemovedValueIds),
                    Union(first.AddedValueIds, next.AddedValueIds),
                    Union(first.ReplacedValueIds, next.ReplacedValueIds));
            }
            // Setting an entry the same commit added is part of the add.
            if (first.Kind == NeoListChangeKind.Add && next.Kind == NeoListChangeKind.Set && Contains(first.AddedValueIds, next.ReplacedValueIds))
                return first;
            if (next.Kind == NeoListChangeKind.Add && first.Kind == NeoListChangeKind.Set && Contains(next.AddedValueIds, first.ReplacedValueIds))
                return next;
            return NeoListChangedArgs.Unknown;

            static IReadOnlyList<string> Union(IReadOnlyList<string> first, IReadOnlyList<string> next)
            {
                if (next.Count == 0 || Contains(first, next))
                    return first;
                var union = new List<string>(first);
                foreach (string id in next)
                {
                    if (!union.Contains(id))
                        union.Add(id);
                }
                return union;
            }

            static bool Contains(IReadOnlyList<string> ids, IReadOnlyList<string> subset)
            {
                foreach (string id in subset)
                {
                    bool found = false;
                    for (int i = 0; i < ids.Count && !found; i++)
                        found = ids[i] == id;
                    if (!found)
                        return false;
                }
                return true;
            }
        }

        private void PublishWritableValueChange(NeoValueOwnership ownership, string valueId, NeoWritePlan? plan = null)
        {
            RefreshSharedEvaluationRow(ownership, valueId);
            // Invoke over a snapshot so reentrant writes, subscriptions and
            // disposal during a callback neither skip nor repeat a handler.
            if (writableValueSubscriptions.TryGetValue(valueId, out var handlers) && handlers.Count != 0)
            {
                int count = handlers.Count;
                var snapshot = ArrayPool<Action<NeoValueOwnership, string>>.Shared.Rent(count);
                handlers.CopyTo(snapshot, 0);
                NeoWritePlan? outer = PublishingPlan;
                PublishingPlan = plan;
                try
                {
                    for (int i = 0; i < count; i++)
                        snapshot[i](ownership, valueId);
                }
                finally
                {
                    PublishingPlan = outer;
                    Array.Clear(snapshot, 0, count);
                    ArrayPool<Action<NeoValueOwnership, string>>.Shared.Return(snapshot);
                }
            }
            OnWritableValueChanged?.Invoke(ownership, valueId);
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
