// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        internal bool IsPreparingVariant => candidateReplay?.PreparingVariant == true;

        // A variant's Apply closure and declarative override removal form one
        // data edit. Execute against scoped wrappers, then validate the complete
        // graph through the same plan used by ordinary and remote writes.
        internal void PrepareVariantApply(NeoMemberClassWritable receiver,
            Action<NeoMemberClassWritable> apply)
        {
            string? receiverId = receiver.overrideValueId ?? receiver.value?.id;
            var placement = FindVariantPlacement(receiverId);
            var plan = new NeoWritePlan(this);
            var candidate = new CandidateReplay(this, plan) { PreparingVariant = true };
            if (candidateReplay is not null) throw new InvalidOperationException("A candidate graph is already active.");
            candidateReplay = candidate;
            try
            {
                using (ReadCandidate(plan))
                {
                    var scoped = new NeoMemberClassWritable(this, receiver.member,
                        receiver.overrideValueId ?? receiver.value?.id, receiver.ownership);
                    apply(scoped);
                    if (placement is not null)
                    {
                        var row = ResolveValueRow(receiverId!) as ObjectMemberValue;
                        if (row is null)
                            throw PlacementError("object-variant-placement-removed", $"Variant removed placed object '{receiverId}'.");
                        var (primitive, cells) = placement.Value;
                        var next = primitive.ReadObjectFootprint(row, primitive.ReadObjectOrigin(row, null), null);
                        if (!cells.SetEquals(next))
                            throw PlacementError("object-variant-footprint-changed", $"Variant must preserve the occupied cells of placed object '{receiverId}'.");
                    }
                    foreach (var allocation in candidate.Allocations)
                        plan.Set(NeoValueOwnership.Session, allocation.Value);
                }
            }
            finally
            {
                candidate.Dispose();
                candidateReplay = null;
            }
            plan.Commit();
        }

        private (NeoReadOnlyTileGridPrimitive primitive, HashSet<Vector2Int> cells)? FindVariantPlacement(string? receiverId)
        {
            if (receiverId is null || !HasWorldKind(ResolveValueRow(receiverId)?.classId, "object")) return null;
            var pending = new Queue<string>();
            var visited = new HashSet<string>();
            pending.Enqueue(receiverId);
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                if (!visited.Add(id)) continue;
                if (HasWorldKind(ResolveValueRow(id)?.classId, "tileGrid"))
                {
                    var primitive = NeoReadOnlyTileGridPrimitive.Resolve(this, id);
                    foreach (string layerId in primitive.ResolveObjectLayerIds())
                    {
                        var record = GetGridLookupCache(id).ObjectRecord(layerId, receiverId);
                        if (record is not null) return (primitive, new HashSet<Vector2Int>(record.Footprint));
                    }
                }
                foreach (string parent in GridQueryParents(id)) pending.Enqueue(parent);
            }
            return null;
        }

        internal bool DeferVariantAliasRetarget(NeoGeneratedClassValue value, ClassMember member,
            string valueId, NeoValueOwnership ownership)
        {
            CandidateReplay? candidate = candidateReplay;
            if (candidate?.PreparingVariant != true
                || candidate.Nodes.Values.Any(node => ReferenceEquals(node, value.BackingNode))) return false;
            candidate.Plan.AfterCommit(() => value.RetargetWritableReference(member, valueId, ownership));
            return true;
        }

        private void RefreshPreparedVariant(NeoMemberClassWritable node, ObjectMemberValue root)
        {
            CandidateReplay candidate = candidateReplay!;
            candidate.AffectedRoots.Add(root.id);
            if (virtualValueIdsByRoot.TryGetValue(root.id, out var previous))
                candidate.HiddenVirtualIds.UnionWith(previous);
            if (!replayingVirtualRootIds.Add(root.id))
                throw new InvalidOperationException($"Sparse constructor dependency cycle at '{root.id}'.");
            try { candidate.Add(ExpandVirtualInstanceRootCore(root, prepareOnly: true)); }
            finally { replayingVirtualRootIds.Remove(root.id); }
            node.RefreshCommittedValue();
            RefreshVirtualWrapperTree(node);
        }
    }
}
