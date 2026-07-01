// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public interface INeoTileGridContent
    {
        NeoReadOnlyTileGridPrimitive Primitive { get; }
        IReadOnlyList<ReadOnlyNeoTileLayerRuntime> TileLayersInOrder { get; }
        IReadOnlyList<ReadOnlyNeoObjectLayerRuntime> ObjectLayersInOrder { get; }
        NeoTileGridRenderer? Renderer { get; }
        IDisposable OnChanged(Action<NeoTileGridChangedArgs> handler);
    }

    public interface INeoWritableTileGridContent : INeoTileGridContent
    {
        new NeoTileGridPrimitive Primitive { get; }
    }

    public static class NeoTileGridContentLookupExtensions
    {
        public static NeoResolvedTileInstance<TTile>? GetTile<TTile>(
            this ReadOnlyNeoTileLayerRuntime layer,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            return layer.GetTile(cell)?.As<TTile>();
        }

        public static IReadOnlyList<NeoResolvedTileInstance<TTile>> GetTiles<TTile>(
            this ReadOnlyNeoTileLayerRuntime layer,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            var tile = layer.GetTile(cell)?.As<TTile>();
            return tile is null
                ? Array.Empty<NeoResolvedTileInstance<TTile>>()
                : new[] { tile };
        }

        public static IReadOnlyList<NeoResolvedTileInstance<TTile>> GetTiles<TTile>(
            this ReadOnlyNeoTileLayerRuntime layer)
            where TTile : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            var typedTiles = new List<NeoResolvedTileInstance<TTile>>();
            foreach (var tile in layer.GetTiles())
            {
                var typed = tile.As<TTile>();
                if (typed is not null)
                {
                    typedTiles.Add(typed);
                }
            }
            return typedTiles;
        }

        public static NeoResolvedObjectInstance<TObject>? GetObject<TObject>(
            this ReadOnlyNeoObjectLayerRuntime layer,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            return layer.GetObject(cell)?.As<TObject>();
        }

        public static IReadOnlyList<NeoResolvedObjectInstance<TObject>> GetObjects<TObject>(
            this ReadOnlyNeoObjectLayerRuntime layer,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            var typedObjects = new List<NeoResolvedObjectInstance<TObject>>();
            foreach (var obj in layer.GetObjects(cell))
            {
                var typed = obj.As<TObject>();
                if (typed is not null)
                {
                    typedObjects.Add(typed);
                }
            }
            return typedObjects;
        }

        public static IReadOnlyList<NeoResolvedObjectInstance<TObject>> GetObjects<TObject>(
            this ReadOnlyNeoObjectLayerRuntime layer)
            where TObject : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            var typedObjects = new List<NeoResolvedObjectInstance<TObject>>();
            foreach (var obj in layer.GetObjects())
            {
                var typed = obj.As<TObject>();
                if (typed is not null)
                {
                    typedObjects.Add(typed);
                }
            }
            return typedObjects;
        }

        public static NeoResolvedTileInstance? GetTile(
            this INeoTileGridContent content,
            Vector2Int cell)
        {
            var tiles = content.GetTiles(cell);
            return tiles.Count == 0 ? null : tiles[tiles.Count - 1];
        }

        public static IReadOnlyList<NeoResolvedTileInstance> GetTiles(
            this INeoTileGridContent content,
            Vector2Int cell)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            var tiles = new List<NeoResolvedTileInstance>();
            foreach (var layer in content.TileLayersInOrder)
            {
                var tile = layer.GetTile(cell);
                if (tile is not null)
                {
                    tiles.Add(tile);
                }
            }
            return tiles;
        }

        public static NeoResolvedTileInstance<TTile>? GetTile<TTile>(
            this INeoTileGridContent content,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            var tiles = content.GetTiles<TTile>(cell);
            return tiles.Count == 0 ? null : tiles[tiles.Count - 1];
        }

        public static IReadOnlyList<NeoResolvedTileInstance<TTile>> GetTiles<TTile>(
            this INeoTileGridContent content,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            var typedTiles = new List<NeoResolvedTileInstance<TTile>>();
            foreach (var tile in content.GetTiles(cell))
            {
                var typed = tile.As<TTile>();
                if (typed is not null)
                {
                    typedTiles.Add(typed);
                }
            }
            return typedTiles;
        }

        public static NeoResolvedTileInstance? GetTile(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
        {
            var tiles = content.GetTiles(source, cell);
            return tiles.Count == 0 ? null : tiles[tiles.Count - 1];
        }

        public static IReadOnlyList<NeoResolvedTileInstance> GetTiles(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            string? sourceValueId = source.valueId;
            if (string.IsNullOrEmpty(sourceValueId))
            {
                return Array.Empty<NeoResolvedTileInstance>();
            }

            var tiles = new List<NeoResolvedTileInstance>();
            foreach (var tile in content.GetTiles(cell))
            {
                if (tile.SourceKind != NeoTileOutputSourceKind.TileLayerLink) continue;
                if (!string.Equals(tile.SourceTileLayerLinkId, sourceValueId, StringComparison.Ordinal)) continue;
                tiles.Add(tile);
            }
            return tiles;
        }

        public static NeoResolvedTileInstance<TTile>? GetTile<TTile>(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            var tiles = content.GetTiles<TTile>(source, cell);
            return tiles.Count == 0 ? null : tiles[tiles.Count - 1];
        }

        public static IReadOnlyList<NeoResolvedTileInstance<TTile>> GetTiles<TTile>(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            var typedTiles = new List<NeoResolvedTileInstance<TTile>>();
            foreach (var tile in content.GetTiles(source, cell))
            {
                var typed = tile.As<TTile>();
                if (typed is not null)
                {
                    typedTiles.Add(typed);
                }
            }
            return typedTiles;
        }

        public static NeoResolvedTileInstance? GetTile(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
        {
            return content.GetTile(source, cell);
        }

        public static IReadOnlyList<NeoResolvedTileInstance> GetTiles(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
        {
            return content.GetTiles(source, cell);
        }

        public static NeoResolvedTileInstance<TTile>? GetTile<TTile>(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            return content.GetTile<TTile>(source, cell);
        }

        public static IReadOnlyList<NeoResolvedTileInstance<TTile>> GetTiles<TTile>(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            return content.GetTiles<TTile>(source, cell);
        }

        public static NeoResolvedObjectInstance? GetObject(
            this INeoTileGridContent content,
            Vector2Int cell)
        {
            var objects = content.GetObjects(cell);
            return objects.Count == 0 ? null : objects[objects.Count - 1];
        }

        public static IReadOnlyList<NeoResolvedObjectInstance> GetObjects(
            this INeoTileGridContent content,
            Vector2Int cell)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            var objects = new List<NeoResolvedObjectInstance>();
            foreach (var layer in content.ObjectLayersInOrder)
            {
                objects.AddRange(layer.GetObjects(cell));
            }
            return objects;
        }

        public static NeoResolvedObjectInstance<TObject>? GetObject<TObject>(
            this INeoTileGridContent content,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            var objects = content.GetObjects<TObject>(cell);
            return objects.Count == 0 ? null : objects[objects.Count - 1];
        }

        public static IReadOnlyList<NeoResolvedObjectInstance<TObject>> GetObjects<TObject>(
            this INeoTileGridContent content,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            var typedObjects = new List<NeoResolvedObjectInstance<TObject>>();
            foreach (var obj in content.GetObjects(cell))
            {
                var typed = obj.As<TObject>();
                if (typed is not null)
                {
                    typedObjects.Add(typed);
                }
            }
            return typedObjects;
        }

        public static NeoResolvedObjectInstance? GetObject(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
        {
            var objects = content.GetObjects(source, cell);
            return objects.Count == 0 ? null : objects[objects.Count - 1];
        }

        public static IReadOnlyList<NeoResolvedObjectInstance> GetObjects(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            string? sourceValueId = source.valueId;
            if (string.IsNullOrEmpty(sourceValueId))
            {
                return Array.Empty<NeoResolvedObjectInstance>();
            }

            var objects = new List<NeoResolvedObjectInstance>();
            foreach (var obj in content.GetObjects(cell))
            {
                if (!string.Equals(obj.Info.valueId, sourceValueId, StringComparison.Ordinal)) continue;
                objects.Add(obj);
            }
            return objects;
        }

        public static NeoResolvedObjectInstance<TObject>? GetObject<TObject>(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            var objects = content.GetObjects<TObject>(source, cell);
            return objects.Count == 0 ? null : objects[objects.Count - 1];
        }

        public static IReadOnlyList<NeoResolvedObjectInstance<TObject>> GetObjects<TObject>(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            var typedObjects = new List<NeoResolvedObjectInstance<TObject>>();
            foreach (var obj in content.GetObjects(source, cell))
            {
                var typed = obj.As<TObject>();
                if (typed is not null)
                {
                    typedObjects.Add(typed);
                }
            }
            return typedObjects;
        }

        public static NeoResolvedObjectInstance? GetObject(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
        {
            return content.GetObject(source, cell);
        }

        public static IReadOnlyList<NeoResolvedObjectInstance> GetObjects(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
        {
            return content.GetObjects(source, cell);
        }

        public static NeoResolvedObjectInstance<TObject>? GetObject<TObject>(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            return content.GetObject<TObject>(source, cell);
        }

        public static IReadOnlyList<NeoResolvedObjectInstance<TObject>> GetObjects<TObject>(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            return content.GetObjects<TObject>(source, cell);
        }
    }

    public sealed class NeoTileGridChangedArgs
    {
        public NeoTileGridChangedArgs(
            string gridValueId,
            IReadOnlyList<NeoTileLayerChangedArgs>? tileLayers = null,
            IReadOnlyList<NeoObjectLayerChangedArgs>? objectLayers = null,
            NeoChangeSource source = NeoChangeSource.Local)
        {
            GridValueId = gridValueId ?? throw new ArgumentNullException(nameof(gridValueId));
            TileLayers = tileLayers ?? Array.Empty<NeoTileLayerChangedArgs>();
            ObjectLayers = objectLayers ?? Array.Empty<NeoObjectLayerChangedArgs>();
            Source = source;
        }

        public string GridValueId { get; }
        public IReadOnlyList<NeoTileLayerChangedArgs> TileLayers { get; }
        public IReadOnlyList<NeoObjectLayerChangedArgs> ObjectLayers { get; }
        public NeoChangeSource Source { get; }
    }

    public enum NeoTileGridChangeSourceKind
    {
        Direct = 0,
        TileLayerLink = 1,
        ObjectLayer = 2,
    }

    public sealed class NeoTileLayerChangedArgs
    {
        public NeoTileLayerChangedArgs(
            string layerId,
            IReadOnlyList<Vector2Int> cellsToClear,
            IReadOnlyList<Vector2Int> cellsToSetOrRefresh,
            NeoTileGridChangeSourceKind sourceKind,
            string? sourceId)
        {
            LayerId = layerId ?? throw new ArgumentNullException(nameof(layerId));
            CellsToClear = cellsToClear ?? throw new ArgumentNullException(nameof(cellsToClear));
            CellsToSetOrRefresh = cellsToSetOrRefresh ?? throw new ArgumentNullException(nameof(cellsToSetOrRefresh));
            if (CellsToClear.Count == 0 && CellsToSetOrRefresh.Count == 0)
            {
                throw new ArgumentException(
                    "Tile layer changes must include explicit cells to clear or set/refresh.");
            }
            SourceKind = sourceKind;
            SourceId = sourceId;
            ChangedCells = NeoTileGridChangedArgsSupport.UnionCells(
                CellsToClear,
                CellsToSetOrRefresh);
        }

        public string LayerId { get; }
        public IReadOnlyList<Vector2Int> CellsToClear { get; }
        public IReadOnlyList<Vector2Int> CellsToSetOrRefresh { get; }
        public NeoTileGridChangeSourceKind SourceKind { get; }
        public string? SourceId { get; }
        public IReadOnlyList<Vector2Int> ChangedCells { get; }
    }

    public sealed class NeoObjectLayerChangedArgs
    {
        public NeoObjectLayerChangedArgs(
            string layerId,
            IReadOnlyList<NeoObjectInstanceId> removedInstances,
            IReadOnlyList<NeoObjectInstanceId> addedOrChangedInstances,
            IReadOnlyList<Vector2Int> changedCells,
            NeoTileGridChangeSourceKind sourceKind,
            string? sourceId)
        {
            LayerId = layerId ?? throw new ArgumentNullException(nameof(layerId));
            RemovedInstances = removedInstances ?? throw new ArgumentNullException(nameof(removedInstances));
            AddedOrChangedInstances = addedOrChangedInstances ?? throw new ArgumentNullException(nameof(addedOrChangedInstances));
            ChangedCells = changedCells ?? throw new ArgumentNullException(nameof(changedCells));
            if (RemovedInstances.Count == 0 && AddedOrChangedInstances.Count == 0 && ChangedCells.Count == 0)
            {
                throw new ArgumentException(
                    "Object layer changes must include explicit instances or cells.");
            }
            SourceKind = sourceKind;
            SourceId = sourceId;
            ChangedInstances = NeoTileGridChangedArgsSupport.UnionInstances(
                RemovedInstances,
                AddedOrChangedInstances);
        }

        public string LayerId { get; }
        public IReadOnlyList<NeoObjectInstanceId> RemovedInstances { get; }
        public IReadOnlyList<NeoObjectInstanceId> AddedOrChangedInstances { get; }
        public IReadOnlyList<NeoObjectInstanceId> ChangedInstances { get; }
        public IReadOnlyList<Vector2Int> ChangedCells { get; }
        public NeoTileGridChangeSourceKind SourceKind { get; }
        public string? SourceId { get; }
    }

    internal readonly struct NeoTileLayerLinkDependency
    {
        public NeoTileLayerLinkDependency(string sourceValueId, string targetTileLayerId)
        {
            SourceValueId = sourceValueId ?? throw new ArgumentNullException(nameof(sourceValueId));
            TargetTileLayerId = targetTileLayerId ?? throw new ArgumentNullException(nameof(targetTileLayerId));
        }

        public string SourceValueId { get; }
        public string TargetTileLayerId { get; }
    }

    internal static class NeoTileGridChangedArgsSupport
    {
        public static IReadOnlyList<Vector2Int> UnionCells(
            IReadOnlyList<Vector2Int> first,
            IReadOnlyList<Vector2Int> second)
        {
            var cells = new List<Vector2Int>(first.Count + second.Count);
            AddUnique(cells, first);
            AddUnique(cells, second);
            return cells;
        }

        private static void AddUnique(
            List<Vector2Int> target,
            IReadOnlyList<Vector2Int> source)
        {
            foreach (var cell in source)
            {
                if (target.Contains(cell)) continue;
                target.Add(cell);
            }
        }

        public static IReadOnlyList<NeoObjectInstanceId> UnionInstances(
            IReadOnlyList<NeoObjectInstanceId> first,
            IReadOnlyList<NeoObjectInstanceId> second)
        {
            var ids = new List<NeoObjectInstanceId>(first.Count + second.Count);
            foreach (var id in first)
            {
                if (ids.Contains(id)) continue;
                ids.Add(id);
            }
            foreach (var id in second)
            {
                if (ids.Contains(id)) continue;
                ids.Add(id);
            }
            return ids;
        }
    }
}
