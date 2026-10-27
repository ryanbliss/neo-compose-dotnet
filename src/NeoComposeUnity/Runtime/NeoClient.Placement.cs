// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using UnityEngine;
using ObjectMove = NeoCompose.Runtime.NeoTileGridLookupCache.ObjectMove;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        // The placement API. Two scalar members carry grid invariants: an
        // object's Position and a tile's Cell. Generated setters and NeoScript
        // assignments to those members come here rather than to the plain leaf
        // write, so the grid indexes learn about the move. An object's
        // Position is validated against the loaded grid indexes and stored in
        // place, patching only the moved footprint and its projected tiles; a
        // tile's Cell still commits through the plan, which rebuilds the
        // layer. Any other member routed here is an ordinary leaf write.
#if NEO_COMPOSE_PROFILING
        private static readonly Unity.Profiling.ProfilerMarker PlacementWriteMarker = new("NeoCompose.Write.Placement");
#endif
        private const string PositionKey = "Position";
        private const string CellKey = "Cell";
        private readonly List<ObjectMove> objectMoveScratch = new();
        private readonly Dictionary<string, bool> placedObjects = new(StringComparer.Ordinal);

        /// <summary>
        /// Whether writes to <paramref name="key"/> on <paramref name="owner"/>
        /// must go through the placement API. Every other key answers from
        /// the switch alone; <see cref="HasWorldKind"/> memoizes the kinds.
        /// </summary>
        internal bool IsPlacementMember(MemberValue? owner, string key) => key switch
        {
            PositionKey => owner is not null && HasWorldKind(owner.classId, "object") && IsPlacedObject(owner.id),
            CellKey => HasWorldKind(owner?.classId, "tile") || HasWorldKind(owner?.classId, "placementTile"),
            _ => false,
        };

        /// <summary>
        /// Whether <paramref name="objectId"/> is placed in a grid this client
        /// indexes. A composition child never is, so its Position is an
        /// ordinary leaf (P111 §3). The first write after the memo is
        /// forgotten probes every object layer of every lookup cache, building
        /// any index not yet built, as a placement write does; later writes
        /// cost one lookup.
        /// </summary>
        private bool IsPlacedObject(string objectId)
        {
            // A candidate's rows must never seed an index. Its writes take the
            // plan either way, so it keeps the placement route unprobed.
            if (candidateReplay is not null || candidateReadPlan is not null)
                return true;
            if (placedObjects.TryGetValue(objectId, out bool placed))
                return placed;
            foreach (NeoTileGridLookupCache cache in gridLookupCacheList)
            {
                if (cache.IndexesObject(objectId))
                {
                    placed = true;
                    break;
                }
            }
            placedObjects[objectId] = placed;
            return placed;
        }

        /// <summary>
        /// Called whenever an object layer index may be dropped (every plan
        /// commit, a grid change, a partition reload) or a grid gains a lookup
        /// cache, any of which can change which objects are placed.
        /// </summary>
        internal void ForgetObjectPlacements() => placedObjects.Clear();

        /// <summary>A validated insertion placed <paramref name="objectId"/>; no other object's placement changed.</summary>
        internal void RememberPlacedObject(string objectId) => placedObjects[objectId] = true;

        /// <summary>
        /// Writes <paramref name="next"/> as the value of <paramref name="key"/>
        /// on <paramref name="owner"/>. Returns false, having changed nothing,
        /// when the write needs a full plan: a tile's Cell, a first write over
        /// a sparse default, or a write during replay.
        /// </summary>
        /// <param name="node">The caller's node for <paramref name="next"/>'s id, if it holds one.</param>
        internal bool TryWritePlacement(
            NeoValueOwnership ownership,
            ObjectMemberValue owner,
            string key,
            MemberValue next,
            Member member,
            NeoValueNode? node = null)
        {
            if (!IsPlacementMember(owner, key))
                return TryWriteLeaf(ownership, next, member, "value", node);
            if (key != PositionKey || next is not Vector3MemberValue position)
                return false;
            return TryWriteObjectPosition(ownership, owner, position, member, node);
        }

        private bool TryWriteObjectPosition(
            NeoValueOwnership ownership, ObjectMemberValue owner, Vector3MemberValue next, Member member, NeoValueNode? node)
        {
            if (!CanWriteLeaf(ownership, next, member, ref node))
                return false;
#if NEO_COMPOSE_PROFILING
            using var marker = PlacementWriteMarker.Auto();
#endif
            if (next.value is not { } value || !Finite(value.x) || !Finite(value.y) || !Finite(value.z))
                throw PlacementError("object-position-invalid", $"Object '{owner.id}' requires a finite Position.");
            // Same rounding as the layer builders (ReadObjectOrigin).
            var cell = NeoReadOnlyTileGridPrimitive.RoundedPlacementCell(value.x, value.y);
            List<ObjectMove> moves = objectMoveScratch;
            if (moves.Count != 0)
                moves = new List<ObjectMove>();
            // Getter watchers hear the move once every index it touches is
            // current.
            HoldGetterChanges();
            bool gridLeaf = false;
            try
            {
                // Validation changes no index; a collision leaves the store
                // and every index as they were.
                foreach (NeoTileGridLookupCache cache in gridLookupCacheList)
                    if (ObjectMove.Prepare(cache, owner.id, cell) is { } move)
                        moves.Add(move);
                StoreLeaf(ownership, next, node!);
                // Each move publishes its change to the getter memo as it
                // applies, before anything else reads.
                foreach (var move in moves)
                    move.Apply();
                gridLeaf = InvalidateGridLeaf(next.id);
                NotifyWritableValueChanged(ownership, next.id, "value", membershipChanged: false, node: node);
                // Lifecycle filters read generated properties, whose nodes
                // refresh during the value notifications above.
                foreach (var move in moves)
                    move.Cache.RaiseChanged(move);
                if (gridLeaf)
                    PublishGridLeaf(ownership, next.id);
            }
            finally
            {
                moves.Clear();
                if (gridLeaf)
                    EndGridChange();
                ReleaseGetterChanges();
            }
            return true;
        }
    }
}
