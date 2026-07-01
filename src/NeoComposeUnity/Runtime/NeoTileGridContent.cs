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
