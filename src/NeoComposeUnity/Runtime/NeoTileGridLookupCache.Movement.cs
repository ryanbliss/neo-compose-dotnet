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
        private readonly Stack<ObjectMove> objectMovePool = new();

        /// <summary>One object's validated move: the records to swap and the change to publish.</summary>
        internal sealed class ObjectMove
        {
            internal readonly NeoTileGridLookupCache Cache;
            private readonly List<(string layer, ObjectLayerIndex index, NeoObjectPlacementRecord before, NeoObjectPlacementRecord after)> objects = new();
            private readonly List<(string layer, TileLayerIndex index, int slot, NeoTilePlacementRecord before, NeoTilePlacementRecord after)> tiles = new();
            private readonly List<(NeoReadOnlyTileGridPrimitive.PlacementNode node, (double x, double y) offset)> nodes = new();
            private NeoReadOnlyTileGridPrimitive.PlacementNode? movedNode;
            private (double x, double y) position;
            internal NeoTileGridChangedArgs? Change;

            private ObjectMove(NeoTileGridLookupCache cache) => Cache = cache;

            /// <summary>
            /// Validates moving <paramref name="objectId"/> to <paramref name="cell"/>
            /// against <paramref name="cache"/>'s indexes. Returns null when the
            /// object is not indexed there or stays in its cell; throws on a
            /// collision. Changes no index.
            /// </summary>
            internal static ObjectMove? Prepare(NeoTileGridLookupCache cache, string objectId, (double x, double y) position)
            {
                if (!cache.primitive.PlacementGraph.Nodes.TryGetValue(objectId, out var root)
                    || root.Position == position)
                    return null;
                (double x, double y) offset = (root.Offset.x + position.x - root.Position.x, root.Offset.y + position.y - root.Position.y);
                var move = cache.objectMovePool.Count == 0 ? new ObjectMove(cache) : cache.objectMovePool.Pop();
                move.movedNode = root;
                move.position = position;
                try
                {
                    if (root.Children.Count == 0 && NeoReadOnlyTileGridPrimitive.RoundedPlacementCell(offset.x, offset.y) == root.Cell)
                    {
                        move.nodes.Add((root, offset));
                        return move;
                    }
                    Collect(move, root, offset);
                    var moving = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var state in move.nodes)
                        if (state.node.PlacementLayerId is not null)
                            moving.Add(state.node.Row.id);
                    // Validate the candidate subtree against itself and against
                    // committed occupants outside it. No stored row/index changes.
                    var occupied = new Dictionary<(string layer, Vector2Int cell), string>();
                    foreach (var state in move.nodes)
                    {
                        var node = state.node;
                        var cell = NeoReadOnlyTileGridPrimitive.RoundedPlacementCell(state.offset.x, state.offset.y);
                        long dx = (long)cell.x - node.Cell.x;
                        long dy = (long)cell.y - node.Cell.y;
                        if (node.PlacementLayerId is string layerId)
                        {
                            var index = cache.GetObjectLayerIndex(layerId);
                            var before = index.ById[node.Row.id];
                            var footprint = new Vector2Int[before.Footprint.Count];
                            for (int i = 0; i < footprint.Length; i++)
                            {
                                var next = footprint[i] = Translate(before.Footprint[i], dx, dy);
                                if (occupied.TryGetValue((layerId, next), out string other) && other != node.Row.id)
                                    throw Occupied(layerId, node.Row.id, other, next);
                                occupied[(layerId, next)] = node.Row.id;
                                if (index.CandidatesByCell.TryGetValue(next, out var occupants))
                                    foreach (var occupant in occupants)
                                        if (!moving.Contains(occupant.InstanceId))
                                            throw Occupied(layerId, node.Row.id, occupant.InstanceId, next);
                            }
                            if ((dx != 0 || dy != 0))
                                move.objects.Add((layerId, index, before, new NeoObjectPlacementRecord(
                                    node.Row.id, cell, footprint, before.Order, before.AssetClassId, before.AssetValueId,
                                    before.Ownership, before.SourceLayerLinkId, before.ParentObjectId)));
                        }
                        if ((dx != 0 || dy != 0) && node.Link is { IsTileLink: true } link)
                        {
                            var index = cache.GetTileLayerIndex(link.LayerId);
                            if (!index.RecordIndicesByLink.TryGetValue(link.LinkValueId, out var slots))
                                continue;
                            foreach (int slot in slots)
                            {
                                var before = index.Records[slot];
                                var next = Translate(before.Cell, dx, dy);
                                move.tiles.Add((link.LayerId, index, slot, before, new NeoTilePlacementRecord(before.InstanceId,
                                    before.PlacementValueId, next, before.AssetClassId, before.Ownership, before.Order,
                                    before.SourceTileLayerLinkId, before.SourceObjectInstanceId, before.UpdatedAtMs, before.CellValueId)));
                            }
                        }
                    }
                    return move;

                }
                catch
                {
                    move.Release();
                    throw;
                }
            }

            private static void Collect(ObjectMove move, NeoReadOnlyTileGridPrimitive.PlacementNode node, (double x, double y) offset)
            {
                move.nodes.Add((node, offset));
                foreach (var child in node.Children)
                    Collect(move, child, (offset.x + child.Position.x, offset.y + child.Position.y));
            }

            internal void Release()
            {
                objects.Clear();
                tiles.Clear();
                nodes.Clear();
                movedNode = null;
                Change = null;
                Cache.objectMovePool.Push(this);
            }

            private static Vector2Int Translate(Vector2Int cell, long dx, long dy)
            {
                long x = cell.x + dx, y = cell.y + dy;
                if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
                    throw new NeoPlacementValidationException("tile-cell-invalid", "Moved placement exceeds the integer cell range.");
                return new Vector2Int((int)x, (int)y);
            }

            /// <summary>Patches the indexes for this prepared move and tells NeoScript grid queries.</summary>
            internal void Apply()
            {
                movedNode!.Position = position;
                foreach (var state in nodes)
                {
                    state.node.Offset = state.offset;
                }
                if (objects.Count == 0 && tiles.Count == 0)
                    return;
                var objectCells = new Dictionary<string, HashSet<Vector2Int>>();
                var objectIds = new Dictionary<string, List<NeoObjectInstanceId>>();
                var tileCells = new Dictionary<string, HashSet<Vector2Int>>();
                foreach (var item in objects)
                    foreach (var cell in item.before.Footprint)
                        Remove(item.index.CandidatesByCell, cell, item.before);
                foreach (var item in tiles)
                    Remove(item.index.CandidatesByCell, item.before.Cell, item.before);
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
                    if (!tileCells.TryGetValue(item.layer, out var cells))
                        tileCells[item.layer] = cells = new HashSet<Vector2Int>();
                    cells.Add(item.before.Cell);
                    cells.Add(item.after.Cell);
                }
                var objectChanges = new List<NeoObjectLayerChangedArgs>();
                foreach (var entry in objectCells)
                    objectChanges.Add(new NeoObjectLayerChangedArgs(entry.Key, Array.Empty<NeoObjectInstanceId>(),
                        objectIds[entry.Key], new List<Vector2Int>(entry.Value), NeoTileGridChangeSourceKind.Direct, null)
                    {
                        PositionsOnly = true
                    });
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

        internal void RaiseChanged(ObjectMove move)
        {
            if (move.Change is not null)
                Changed?.Invoke(move.Change);
        }

        private static NeoPlacementValidationException Occupied(string layer, string id, string other, Vector2Int cell) =>
            new("tile-grid-object-cell-occupied", $"Object layer '{layer}' has objects '{id}' and '{other}' at {cell}.");

        private static void Remove<T>(Dictionary<Vector2Int, List<T>> buckets, Vector2Int cell, T item)
        {
            if (!buckets.TryGetValue(cell, out var values))
                return;
            values.Remove(item);
            if (values.Count == 0)
                buckets.Remove(cell);
        }

        private static List<T> Add<T>(Dictionary<Vector2Int, List<T>> buckets, Vector2Int cell, T item)
        {
            if (!buckets.TryGetValue(cell, out var values))
                buckets[cell] = values = new List<T>();
            values.Add(item);
            return values;
        }
    }
}
