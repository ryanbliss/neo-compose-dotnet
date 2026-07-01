// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    internal sealed class NeoTileGridLookupCache
    {
        private readonly NeoReadOnlyTileGridPrimitive primitive;
        private readonly Dictionary<string, TileLayerIndex> tileLayers = new();
        private readonly Dictionary<string, ObjectLayerIndex> objectLayers = new();
        private readonly Dictionary<string, IDisposable> tileLayerLinkSubscriptions = new();

        public NeoTileGridLookupCache(NeoReadOnlyTileGridPrimitive primitive)
        {
            this.primitive = primitive ?? throw new ArgumentNullException(nameof(primitive));
        }

        public NeoResolvedTileInstance? ResolveTile(
            string layerId,
            Vector2Int cell,
            string expectedTileFamilyTypeId)
        {
            var index = GetTileLayerIndex(layerId, expectedTileFamilyTypeId);
            if (!index.LoadedCells.Contains(cell))
            {
                RefreshTileCell(index, cell);
            }
            return index.TilesByCell.TryGetValue(cell, out var tile) ? tile : null;
        }

        public IReadOnlyList<NeoResolvedObjectInstance> ResolveObjects(
            string layerId,
            Vector2Int cell,
            string expectedObjectFamilyTypeId)
        {
            var index = GetObjectLayerIndex(layerId, expectedObjectFamilyTypeId);
            if (!index.LoadedCells.Contains(cell))
            {
                RefreshObjectCell(index, cell);
            }
            return index.ObjectsByCell.TryGetValue(cell, out var objects)
                ? objects
                : Array.Empty<NeoResolvedObjectInstance>();
        }

        public void Apply(NeoTileGridChangedArgs args)
        {
            foreach (var change in args.TileLayers)
            {
                Apply(change);
            }
            foreach (var change in args.ObjectLayers)
            {
                Apply(change);
            }
        }

        private void Apply(NeoTileLayerChangedArgs change)
        {
            foreach (var index in tileLayers.Values)
            {
                if (index.LayerId != change.LayerId) continue;
                foreach (var cell in change.ChangedCells)
                {
                    if (!index.LoadedCells.Contains(cell)) continue;
                    RefreshTileCell(index, cell);
                }
            }
        }

        private void Apply(NeoObjectLayerChangedArgs change)
        {
            foreach (var index in objectLayers.Values)
            {
                if (index.LayerId != change.LayerId) continue;
                foreach (var cell in change.ChangedCells)
                {
                    if (!index.LoadedCells.Contains(cell)) continue;
                    RefreshObjectCell(index, cell);
                }
            }
        }

        private void EnsureTileLayerLinkSubscriptions(TileLayerIndex index)
        {
            foreach (var dependency in primitive.GetAuthoredTileLayerLinkDependencies())
            {
                if (index.LayerId != dependency.TargetTileLayerId)
                {
                    continue;
                }

                EnsureTileLayerLinkSubscription(
                    dependency.TargetTileLayerId,
                    dependency.SourceValueId);
            }
        }

        private void EnsureTileLayerLinkSubscription(
            string layerId,
            string sourceValueId)
        {
            if (string.IsNullOrEmpty(layerId) || string.IsNullOrEmpty(sourceValueId)) return;
            string key = SourceLinkKey(layerId, sourceValueId);
            if (tileLayerLinkSubscriptions.ContainsKey(key)) return;

            var source = primitive.ResolveGeneratedCustomValue(sourceValueId);
            if (source is null)
            {
                tileLayerLinkSubscriptions[key] = new NeoDisposableSubscription(() => {});
                return;
            }

            tileLayerLinkSubscriptions[key] = source.WatchAnyChange((_, __, ___) =>
                RefreshTileLayerLinkSource(layerId, sourceValueId));
        }

        private void RefreshTileLayerLinkSource(
            string layerId,
            string sourceValueId)
        {
            foreach (var index in tileLayers.Values)
            {
                if (index.LayerId != layerId) continue;
                RefreshTileLayerLinkSource(index, sourceValueId);
            }
        }

        private void RefreshTileLayerLinkSource(
            TileLayerIndex index,
            string sourceValueId)
        {
            var cells = new List<Vector2Int>();
            foreach (var pair in index.TilesByCell)
            {
                if (pair.Value.SourceKind != NeoTileOutputSourceKind.TileLayerLink ||
                    pair.Value.SourceTileLayerLinkId != sourceValueId)
                {
                    continue;
                }
                AddCell(cells, pair.Key);
            }

            foreach (var tile in primitive.GetTileLayerLinkTiles(
                index.LayerId,
                sourceValueId,
                index.ExpectedTypeId))
            {
                if (index.LoadedCells.Contains(tile.Cell))
                {
                    AddCell(cells, tile.Cell);
                }
            }

            foreach (var cell in cells)
            {
                RefreshTileCell(index, cell);
            }
        }

        private void RefreshTileCell(TileLayerIndex index, Vector2Int cell)
        {
            index.LoadedCells.Add(cell);
            var tile = primitive.ResolveTile(
                index.LayerId,
                cell,
                index.ExpectedTypeId);
            if (tile is null)
            {
                index.TilesByCell.Remove(cell);
                return;
            }

            index.TilesByCell[cell] = tile;
            if (tile.SourceKind == NeoTileOutputSourceKind.TileLayerLink &&
                !string.IsNullOrEmpty(tile.SourceTileLayerLinkId))
            {
                EnsureTileLayerLinkSubscription(
                    index.LayerId,
                    tile.SourceTileLayerLinkId!);
            }
        }

        private void RefreshObjectCell(ObjectLayerIndex index, Vector2Int cell)
        {
            index.LoadedCells.Add(cell);
            var objects = primitive.ResolveObjectsAtCell(
                index.LayerId,
                cell,
                index.ExpectedTypeId);
            if (objects.Count == 0)
            {
                index.ObjectsByCell.Remove(cell);
            }
            else
            {
                index.ObjectsByCell[cell] = new List<NeoResolvedObjectInstance>(objects);
            }
        }

        private static void AddCell(List<Vector2Int> cells, Vector2Int cell)
        {
            if (cells.Contains(cell)) return;
            cells.Add(cell);
        }

        private TileLayerIndex GetTileLayerIndex(
            string layerId,
            string expectedTileFamilyTypeId)
        {
            string key = CacheKey(layerId, expectedTileFamilyTypeId);
            if (tileLayers.TryGetValue(key, out var index))
            {
                return index;
            }

            index = new TileLayerIndex(layerId, expectedTileFamilyTypeId);
            tileLayers[key] = index;
            EnsureTileLayerLinkSubscriptions(index);
            return index;
        }

        private ObjectLayerIndex GetObjectLayerIndex(
            string layerId,
            string expectedObjectFamilyTypeId)
        {
            string key = CacheKey(layerId, expectedObjectFamilyTypeId);
            if (objectLayers.TryGetValue(key, out var index))
            {
                return index;
            }

            index = new ObjectLayerIndex(layerId, expectedObjectFamilyTypeId);
            objectLayers[key] = index;
            return index;
        }

        private static string CacheKey(string layerId, string expectedTypeId) =>
            $"{layerId}\n{expectedTypeId}";

        private static string SourceLinkKey(string layerId, string sourceValueId) =>
            $"{layerId}\n{sourceValueId}";

        private sealed class TileLayerIndex
        {
            public TileLayerIndex(string layerId, string expectedTypeId)
            {
                LayerId = layerId;
                ExpectedTypeId = expectedTypeId;
            }

            public string LayerId { get; }
            public string ExpectedTypeId { get; }
            public HashSet<Vector2Int> LoadedCells { get; } = new();
            public Dictionary<Vector2Int, NeoResolvedTileInstance> TilesByCell { get; } = new();
        }

        private sealed class ObjectLayerIndex
        {
            public ObjectLayerIndex(string layerId, string expectedTypeId)
            {
                LayerId = layerId;
                ExpectedTypeId = expectedTypeId;
            }

            public string LayerId { get; }
            public string ExpectedTypeId { get; }
            public HashSet<Vector2Int> LoadedCells { get; } = new();
            public Dictionary<Vector2Int, List<NeoResolvedObjectInstance>> ObjectsByCell { get; } = new();
        }
    }
}
