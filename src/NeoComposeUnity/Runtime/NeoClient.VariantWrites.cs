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
            PreparedVariant? prepared = null;
            if (candidateReplay is not null)
                throw new InvalidOperationException("A candidate graph is already active.");
            candidateReplay = candidate;
            try
            {
                using (ReadCandidate(plan))
                {
                    var scoped = new NeoMemberClassWritable(this, receiver.member,
                        receiver.overrideValueId ?? receiver.value?.id, receiver.ownership, bindsOnRead: true);
                    apply(scoped);
                    if (placement is not null)
                    {
                        var row = ResolveValueRow(receiverId!) as ObjectMemberValue;
                        if (row is null)
                            throw PlacementError("object-variant-placement-removed", $"Variant removed placed object '{receiverId}'.");
                        var (primitive, cells) = placement.Value;
                        var next = primitive.ReadObjectFootprint(row, WorldPlacementOrigin(row.id).origin, null);
                        if (!cells.SetEquals(next))
                            throw PlacementError("object-variant-footprint-changed", $"Variant must preserve the occupied cells of placed object '{receiverId}'.");
                    }
                    foreach (var allocation in candidate.Allocations)
                        plan.Set(NeoValueOwnership.Session, allocation.Value);
                }
                // Keep the root even when its expansion contains nested roots.
                // Commit must prepare the complete variant graph after clearing overrides.
                prepared = candidate.PreparedVariant;
            }
            finally
            {
                candidate.Dispose();
                candidateReplay = null;
            }
            try
            {
                preparedVariant = prepared;
                plan.Commit();
            }
            finally
            {
                preparedVariant = null;
            }
        }

        private (NeoReadOnlyTileGridPrimitive primitive, HashSet<Vector2Int> cells)? FindVariantPlacement(string? receiverId)
        {
            if (receiverId is null || !HasWorldKind(ResolveValueRow(receiverId)?.classId, "object"))
                return null;
            var visited = new HashSet<string>();
            string id = receiverId;
            while (visited.Add(id))
            {
                if (ResolveValueRow(id) is ObjectMemberValue row
                    && HasWorldKind(NeoGeneratedTypesSupport.ResolveClassValueClassId(this, id, row), "tileGrid"))
                {
                    var primitive = NeoReadOnlyTileGridPrimitive.Resolve(this, id);
                    foreach (string layerId in primitive.ResolveObjectLayerIds())
                    {
                        var record = GetGridLookupCache(id).ObjectRecord(layerId, receiverId);
                        if (record is not null)
                            return (primitive, new HashSet<Vector2Int>(record.Footprint));
                    }
                }
                if (!TryGetGridQueryParent(id, out string? parent, out _))
                    break;
                id = parent;
            }
            return null;
        }

        internal bool DeferVariantAliasRetarget(NeoGeneratedClassValue value, ClassMember member,
            string valueId, NeoValueOwnership ownership)
        {
            CandidateReplay? candidate = candidateReplay;
            if (candidate?.PreparingVariant != true
                || candidate.Nodes.Values.Any(node => ReferenceEquals(node, value.BackingNode)))
                return false;
            candidate.Plan.AfterCommit(() => value.RetargetWritableReference(member, valueId, ownership));
            return true;
        }

        private void RefreshPreparedVariant(NeoMemberClassWritable node, ObjectMemberValue root)
        {
            CandidateReplay candidate = candidateReplay!;
            candidate.AffectedRoots.Add(root.id);
            if (virtualValueIdsByRoot.TryGetValue(root.id, out var previous))
                candidate.HiddenVirtualIds.UnionWith(previous);
            // A variant replays its root for the new selection, and again when
            // its declarative halves clear a pin. A replay must not read the
            // previous one's defaults as stored rows: it would omit them, and
            // Add discards the old ones.
            candidate.Remove(root.id);
            replayingVirtualRootIds.Enter(this, root);
            try
            {
                int version = candidate.Plan.Version;
                long writeRevision = WriteRevision;
                var expansion = ExpandVirtualInstanceRootCore(root, prepareOnly: true);
                candidate.Add(expansion);
                candidate.PreparedVariant = new PreparedVariant(candidate.Plan, version, writeRevision, expansion);
            }
            finally { replayingVirtualRootIds.Remove(root.id); }
            node.RefreshCommittedValue();
            RefreshVirtualWrapperTree(node);
        }

        // The last replay of a variant apply, kept for its commit.
        private PreparedVariant? preparedVariant;

        /// <summary>
        /// A variant apply's final replay of its root, against its plan at
        /// <see cref="Version"/> and the committed graph at
        /// <see cref="WriteRevision"/>. The commit's validation reuses it
        /// rather than replaying the root again when it would read the same
        /// graph.
        /// </summary>
        private sealed class PreparedVariant
        {
            internal readonly NeoWritePlan Plan;
            internal readonly int Version;
            internal readonly long WriteRevision;
            internal readonly PreparedVirtualExpansion Expansion;
            // Virtual rows copy the root's map key, which the commit can stamp in place.
            internal readonly string? MapKey;

            internal PreparedVariant(NeoWritePlan plan, int version, long writeRevision, PreparedVirtualExpansion expansion)
            {
                Plan = plan;
                Version = version;
                WriteRevision = writeRevision;
                Expansion = expansion;
                MapKey = expansion.Root.mapKey;
            }
        }

        /// <summary>
        /// Whether <paramref name="root"/>'s commit replay would read exactly
        /// what its variant apply's last replay did: the same plan and
        /// committed graph, unchanged since, the same root row and ownership,
        /// and no other expansion in the proposed graph. Any other affected
        /// root is a materialized spine inside this one, which validation
        /// skips.
        /// </summary>
        private bool TryReusePreparedVariant(CandidateReplay candidate, ObjectMemberValue root,
            NeoValueOwnership? replayOwnership, NestedReplayBoundary? boundary)
        {
            if (preparedVariant is not { } prepared
                || !ReferenceEquals(prepared.Plan, candidate.Plan)
                || prepared.Version != candidate.Plan.Version
                || prepared.WriteRevision != WriteRevision
                || !ReferenceEquals(prepared.Expansion.Root, root)
                || prepared.MapKey != root.mapKey
                || prepared.Expansion.Nested.Count != 0
                || boundary is not null
                || candidate.Values.Count != 0
                || candidate.Allocations.Count != 0)
                return false;
            NeoValueOwnership ownership = replayOwnership ?? (TryGetValueOwnership(root.id, out var resolved)
                ? resolved : ResolveAuthoredOwnership(root.id, root));
            if (ownership != prepared.Expansion.RootOwnership)
                return false;
            foreach (string id in candidate.AffectedRoots)
            {
                if (id == root.id)
                    continue;
                if (virtualFootprintByRoot.ContainsKey(id))
                    return false;
                if (ResolveValueRow(id) is not ObjectMemberValue spine || OverlayingRoot(id, spine) != root.id)
                    return false;
            }
            candidate.Add(prepared.Expansion);
            return true;
        }
    }
}
