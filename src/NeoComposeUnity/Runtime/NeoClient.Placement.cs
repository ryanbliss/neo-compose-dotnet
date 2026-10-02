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

        /// <summary>
        /// Whether writes to <paramref name="key"/> on rows of
        /// <paramref name="classId"/> must go through the placement API.
        /// Every other key answers from the switch alone;
        /// <see cref="HasWorldKind"/> memoizes the two that do not.
        /// </summary>
        internal bool IsPlacementMember(string? classId, string key) => key switch
        {
            PositionKey => HasWorldKind(classId, "object"),
            CellKey => HasWorldKind(classId, "tile") || HasWorldKind(classId, "placementTile"),
            _ => false,
        };

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
            if (!IsPlacementMember(owner.classId, key))
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
            var cell = new Vector2Int(Mathf.RoundToInt(value.x), Mathf.RoundToInt(value.y));
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
