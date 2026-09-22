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
        private static readonly Unity.Profiling.ProfilerMarker PlacementWriteMarker = new("NeoCompose.Write.Placement");
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
        internal bool TryWritePlacement(
            NeoValueOwnership ownership, ObjectMemberValue owner, string key, MemberValue next, Member member)
        {
            if (!IsPlacementMember(owner.classId, key)) return TryWriteLeaf(ownership, next, member, "value");
            if (key != PositionKey || next is not Vector3MemberValue position) return false;
            return TryWriteObjectPosition(ownership, owner, position, member);
        }

        private bool TryWriteObjectPosition(
            NeoValueOwnership ownership, ObjectMemberValue owner, Vector3MemberValue next, Member member)
        {
            if (!CanWriteLeaf(ownership, next, member)) return false;
            using var marker = PlacementWriteMarker.Auto();
            if (next.value is not { } value || !Finite(value.x) || !Finite(value.y) || !Finite(value.z))
                throw PlacementError("object-position-invalid", $"Object '{owner.id}' requires a finite Position.");
            // Same rounding as the layer builders (ReadObjectOrigin).
            var cell = new Vector2Int(Mathf.RoundToInt(value.x), Mathf.RoundToInt(value.y));
            List<ObjectMove> moves = objectMoveScratch;
            if (moves.Count != 0) moves = new List<ObjectMove>();
            try
            {
                // Validation changes no index; a collision leaves the store
                // and every index as they were.
                foreach (NeoTileGridLookupCache cache in gridLookupCaches.Values)
                    if (ObjectMove.Prepare(cache, owner.id, cell) is { } move) moves.Add(move);
                StoreLeaf(ownership, next);
                if (moves.Count != 0)
                {
                    InvalidateGridDependentGetterMemo();
                    foreach (var move in moves) move.Apply();
                }
                NotifyWritableValueChanged(ownership, next.id, "value");
                // Lifecycle filters read generated properties, whose nodes
                // refresh during the value notifications above.
                foreach (var move in moves) move.Cache.RaiseChanged(move);
            }
            finally
            {
                moves.Clear();
            }
            return true;
        }
    }
}
