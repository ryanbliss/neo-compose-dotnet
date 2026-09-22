// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    internal sealed partial class NeoTileGridLookupCache
    {
        /// <summary>One object's validated move: the records to swap and the change to publish.</summary>
        internal sealed class ObjectMove
        {
            internal readonly NeoTileGridLookupCache Cache;
            private readonly List<(string layer, ObjectLayerIndex index, NeoObjectPlacementRecord before, NeoObjectPlacementRecord after)> objects = new();
            private readonly List<(string layer, TileLayerIndex index, int slot, NeoTilePlacementRecord before, NeoTilePlacementRecord after)> tiles = new();
            internal NeoTileGridChangedArgs? Change;

            private ObjectMove(NeoTileGridLookupCache cache) => Cache = cache;

            /// <summary>
            /// Validates moving <paramref name="objectId"/> to <paramref name="cell"/>
            /// against <paramref name="cache"/>'s indexes. Returns null when the
            /// object is not indexed there or stays in its cell; throws on a
            /// collision. Changes no index.
            /// </summary>
            internal static ObjectMove? Prepare(NeoTileGridLookupCache cache, string objectId, Vector2Int cell)
            {
                // A moving object stays in its cell for most frames. Allocate the
                // move bookkeeping only once a footprint actually changes cell.
                ObjectMove? move = null;
                Vector2Int delta = default;
                IReadOnlyList<string> objectLayerIds = cache.ObjectLayerIds;
                for (int layer = 0; layer < objectLayerIds.Count; layer++)
                {
                    string layerId = objectLayerIds[layer];
                    // These are committed indexes, never a speculative layer rebuild.
                    var index = cache.GetObjectLayerIndex(layerId);
                    if (!index.ById.TryGetValue(objectId, out var before) || before.Cell == cell) continue;
                    // One row, one Position: every layer indexing this object
                    // agrees on where it was, so the delta the tile projection
                    // below consumes is fixed by the first of them.
                    if (move is null) delta = cell - before.Cell;
                    var footprint = new Vector2Int[before.Footprint.Count];
                    for (int i = 0; i < footprint.Length; i++)
                    {
                        var next = footprint[i] = before.Footprint[i] + delta;
                        if (index.CandidatesByCell.TryGetValue(next, out var occupants))
                            foreach (var occupant in occupants)
                                if (occupant.InstanceId != objectId) throw Occupied(layerId, objectId, occupant.InstanceId, next);
                    }
                    (move ??= new ObjectMove(cache)).objects.Add((layerId, index, before, new NeoObjectPlacementRecord(
                        objectId, cell, footprint, before.Order, before.AssetClassId, before.AssetValueId, before.Ownership)));
                }
                if (move is null) return null;
                IReadOnlyList<string> tileLayerIds = cache.TileLayerIds;
                for (int layer = 0; layer < tileLayerIds.Count; layer++)
                {
                    string layerId = tileLayerIds[layer];
                    var index = cache.GetTileLayerIndex(layerId);
                    if (!index.RecordIndicesByObject.TryGetValue(objectId, out var slots)) continue;
                    foreach (int slot in slots)
                    {
                        var before = index.Records[slot];
                        var next = before.Cell + delta;
                        if (index.CandidatesByCell.TryGetValue(next, out var occupants))
                            foreach (var occupant in occupants)
                                if (occupant.SourceTileLayerLinkId == before.SourceTileLayerLinkId
                                    && occupant.SourceObjectInstanceId != objectId)
                                    throw new NeoPlacementValidationException("tile-cell-occupied",
                                        $"Tile link '{before.SourceTileLayerLinkId}' has more than one tile at {next}.");
                        move.tiles.Add((layerId, index, slot, before, new NeoTilePlacementRecord(before.InstanceId,
                            before.PlacementValueId, next, before.AssetClassId, before.Ownership, before.Order,
                            before.SourceTileLayerLinkId, before.SourceObjectInstanceId, before.UpdatedAtMs, before.CellValueId)));
                    }
                }
                return move;
            }

            /// <summary>Patches the indexes for this prepared move and tells NeoScript grid queries.</summary>
            internal void Apply()
            {
                var objectCells = new Dictionary<string, HashSet<Vector2Int>>();
                var objectIds = new Dictionary<string, List<NeoObjectInstanceId>>();
                var tileCells = new Dictionary<string, HashSet<Vector2Int>>();
                foreach (var item in objects)
                    foreach (var cell in item.before.Footprint) Remove(item.index.CandidatesByCell, cell, item.before);
                foreach (var item in tiles) Remove(item.index.CandidatesByCell, item.before.Cell, item.before);
                foreach (var item in objects)
                {
                    item.index.ById[item.after.InstanceId] = item.after;
                    item.index.Records[item.index.RecordIndices[item.after.InstanceId]] = item.after;
                    foreach (var cell in item.after.Footprint)
                        Add(item.index.CandidatesByCell, cell, item.after).Sort((a, b) => a.Order.CompareTo(b.Order));
                    if (!objectCells.TryGetValue(item.layer, out var cells))
                    {
                        objectCells[item.layer] = cells = new HashSet<Vector2Int>();
                        objectIds[item.layer] = new List<NeoObjectInstanceId>();
                    }
                    cells.UnionWith(item.before.Footprint);
                    cells.UnionWith(item.after.Footprint);
                    objectIds[item.layer].Add(item.after.InstanceId);
                }
                foreach (var item in tiles)
                {
                    item.index.Records[item.slot] = item.after;
                    Add(item.index.CandidatesByCell, item.after.Cell, item.after).Sort(CompareLoserToWinner);
                    item.index.InvalidateCellOrder();
                    if (!tileCells.TryGetValue(item.layer, out var cells)) tileCells[item.layer] = cells = new HashSet<Vector2Int>();
                    cells.Add(item.before.Cell);
                    cells.Add(item.after.Cell);
                }
                var objectChanges = new List<NeoObjectLayerChangedArgs>();
                foreach (var entry in objectCells)
                    objectChanges.Add(new NeoObjectLayerChangedArgs(entry.Key, Array.Empty<NeoObjectInstanceId>(),
                        objectIds[entry.Key], new List<Vector2Int>(entry.Value), NeoTileGridChangeSourceKind.Direct, null)
                        { PositionsOnly = true });
                var tileChanges = new List<NeoTileLayerChangedArgs>();
                foreach (var entry in tileCells)
                {
                    var clear = new List<Vector2Int>();
                    var refresh = new List<Vector2Int>();
                    foreach (var cell in entry.Value)
                        (Cache.tileLayers[entry.Key].CandidatesByCell.ContainsKey(cell) ? refresh : clear).Add(cell);
                    tileChanges.Add(new NeoTileLayerChangedArgs(entry.Key, clear, refresh, NeoTileGridChangeSourceKind.Direct, null));
                }
                Change = new NeoTileGridChangedArgs(Cache.primitive.GridValueId, tileChanges, objectChanges,
                    Cache.primitive.Client.CurrentChangeSource);
                Cache.primitive.Client.ScriptGridQueries.NotifyChanged(Change);
            }
        }

        internal void RaiseChanged(ObjectMove move) => Changed?.Invoke(move.Change!);

        private static NeoPlacementValidationException Occupied(string layer, string id, string other, Vector2Int cell) =>
            new("tile-grid-object-cell-occupied", $"Object layer '{layer}' has objects '{id}' and '{other}' at {cell}.");

        private static void Remove<T>(Dictionary<Vector2Int, List<T>> buckets, Vector2Int cell, T item)
        {
            if (!buckets.TryGetValue(cell, out var values)) return;
            values.Remove(item);
            if (values.Count == 0) buckets.Remove(cell);
        }

        private static List<T> Add<T>(Dictionary<Vector2Int, List<T>> buckets, Vector2Int cell, T item)
        {
            if (!buckets.TryGetValue(cell, out var values)) buckets[cell] = values = new List<T>();
            values.Add(item);
            return values;
        }
    }
}
