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
        internal void PrepareObjectMoves(NeoWritePlan plan, Dictionary<string, Vector2Int> positions)
        {
            var objects = new List<(string layer, ObjectLayerIndex index, NeoObjectPlacementRecord before, NeoObjectPlacementRecord after)>();
            var deltas = new Dictionary<string, Vector2Int>();
            foreach (string layerId in primitive.ResolveObjectLayerIds())
            {
                // These are committed indexes, never a speculative layer rebuild.
                var index = GetObjectLayerIndex(layerId);
                var proposed = new Dictionary<Vector2Int, string>();
                foreach (var position in positions)
                {
                    if (!index.ById.TryGetValue(position.Key, out var before) || before.Cell == position.Value) continue;
                    var delta = position.Value - before.Cell;
                    deltas[position.Key] = delta;
                    var footprint = new Vector2Int[before.Footprint.Count];
                    for (int i = 0; i < footprint.Length; i++)
                    {
                        var cell = footprint[i] = before.Footprint[i] + delta;
                        if (proposed.TryGetValue(cell, out string other) && other != position.Key)
                            throw Occupied(layerId, position.Key, other, cell);
                        proposed[cell] = position.Key;
                        if (index.CandidatesByCell.TryGetValue(cell, out var occupants))
                            foreach (var occupant in occupants)
                            {
                                if (occupant.InstanceId == position.Key) continue;
                                // A second moved object is checked against its proposed footprint.
                                if (positions.TryGetValue(occupant.InstanceId, out var next) && next != occupant.Cell) continue;
                                throw Occupied(layerId, position.Key, occupant.InstanceId, cell);
                            }
                    }
                    objects.Add((layerId, index, before, new NeoObjectPlacementRecord(before.InstanceId,
                        position.Value, footprint, before.Order, before.AssetClassId, before.AssetValueId, before.Ownership)));
                }
            }
            if (objects.Count == 0) return;

            var tiles = new List<(string layer, TileLayerIndex index, int slot, NeoTilePlacementRecord before, NeoTilePlacementRecord after)>();
            foreach (string layerId in primitive.ResolveTileLayerIds())
            {
                var index = GetTileLayerIndex(layerId);
                var proposed = new HashSet<(string source, Vector2Int cell)>();
                foreach (var movement in deltas)
                {
                    if (!index.RecordIndicesByObject.TryGetValue(movement.Key, out var slots)) continue;
                    foreach (int slot in slots)
                    {
                        var before = index.Records[slot];
                        var cell = before.Cell + movement.Value;
                        bool collision = !proposed.Add((before.SourceTileLayerLinkId, cell));
                        if (index.CandidatesByCell.TryGetValue(cell, out var occupants))
                            foreach (var occupant in occupants)
                                if (occupant.SourceTileLayerLinkId == before.SourceTileLayerLinkId
                                    && (occupant.SourceObjectInstanceId is null || !deltas.ContainsKey(occupant.SourceObjectInstanceId)))
                                    collision = true;
                        if (collision)
                            throw new NeoPlacementValidationException("tile-cell-occupied",
                                $"Tile link '{before.SourceTileLayerLinkId}' has more than one tile at {cell}.");
                        tiles.Add((layerId, index, slot, before, new NeoTilePlacementRecord(before.InstanceId,
                            before.PlacementValueId, cell, before.AssetClassId, before.Ownership, before.Order,
                            before.SourceTileLayerLinkId, before.SourceObjectInstanceId, before.UpdatedAtMs, before.CellValueId)));
                    }
                }
            }

            // Validation above changes no index. Publish all patches before notifying any observer.
            plan.AfterCommit(() =>
            {
                var objectCells = new Dictionary<string, HashSet<Vector2Int>>();
                var objectIds = new Dictionary<string, List<NeoObjectInstanceId>>();
                var tileCells = new Dictionary<string, HashSet<Vector2Int>>();
                foreach (var move in objects)
                    foreach (var cell in move.before.Footprint) Remove(move.index.CandidatesByCell, cell, move.before);
                foreach (var move in tiles) Remove(move.index.CandidatesByCell, move.before.Cell, move.before);
                foreach (var move in objects)
                {
                    move.index.ById[move.after.InstanceId] = move.after;
                    move.index.Records[move.index.RecordIndices[move.after.InstanceId]] = move.after;
                    foreach (var cell in move.after.Footprint)
                        Add(move.index.CandidatesByCell, cell, move.after).Sort((a, b) => a.Order.CompareTo(b.Order));
                    if (!objectCells.TryGetValue(move.layer, out var cells))
                    {
                        objectCells[move.layer] = cells = new HashSet<Vector2Int>();
                        objectIds[move.layer] = new List<NeoObjectInstanceId>();
                    }
                    cells.UnionWith(move.before.Footprint);
                    cells.UnionWith(move.after.Footprint);
                    objectIds[move.layer].Add(move.after.InstanceId);
                }
                foreach (var move in tiles)
                {
                    move.index.Records[move.slot] = move.after;
                    Add(move.index.CandidatesByCell, move.after.Cell, move.after).Sort(CompareLoserToWinner);
                    move.index.InvalidateCellOrder();
                    if (!tileCells.TryGetValue(move.layer, out var cells)) tileCells[move.layer] = cells = new HashSet<Vector2Int>();
                    cells.Add(move.before.Cell);
                    cells.Add(move.after.Cell);
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
                        (tileLayers[entry.Key].CandidatesByCell.ContainsKey(cell) ? refresh : clear).Add(cell);
                    tileChanges.Add(new NeoTileLayerChangedArgs(entry.Key, clear, refresh, NeoTileGridChangeSourceKind.Direct, null));
                }
                var change = new NeoTileGridChangedArgs(primitive.GridValueId, tileChanges, objectChanges,
                    primitive.Client.CurrentChangeSource);
                primitive.Client.ScriptGridQueries.NotifyChanged(change);
                // Lifecycle filters read generated properties, whose nodes refresh
                // during the value notifications following this publication.
                plan.AfterNotifications(() => Changed?.Invoke(change));
            });
        }

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
