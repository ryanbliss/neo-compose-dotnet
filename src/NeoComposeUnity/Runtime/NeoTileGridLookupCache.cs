// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Per-grid, per-layer spatial index over the values-native tile grid
    /// model. First access of a layer builds its index in one O(n) pass over
    /// the layer's containment membership (no JSON parsing or cloning);
    /// queries are O(1) dictionary hits. Invalidation is value-change-event
    /// driven: every value id the build consulted (grid row, Children list,
    /// link rows, containment list values, placement rows and their Cell/Tile
    /// children, object rows, ...) is recorded as a dependency, and a write
    /// to any of them — including the membership-change notification a
    /// containerId-carrying row raises for its container — drops the layer
    /// index for a lazy rebuild.
    /// </summary>
    internal sealed class NeoTileGridLookupCache
    {
        private static readonly IReadOnlyList<NeoTilePlacementRecord> EmptyTileRecords =
            Array.Empty<NeoTilePlacementRecord>();
        private static readonly IReadOnlyList<NeoObjectPlacementRecord> EmptyObjectRecords =
            Array.Empty<NeoObjectPlacementRecord>();

        private readonly NeoReadOnlyTileGridPrimitive primitive;
        private readonly Dictionary<string, TileLayerIndex> tileLayers = new();
        private readonly Dictionary<string, ObjectLayerIndex> objectLayers = new();

        public NeoTileGridLookupCache(NeoReadOnlyTileGridPrimitive primitive)
        {
            this.primitive = primitive ?? throw new ArgumentNullException(nameof(primitive));
            primitive.Client.OnWritableValueChanged += HandleWritableValueChanged;
            primitive.Client.OnValuePartitionChanged += HandleValuePartitionChanged;
        }

        // ------------------------------------------------------------------
        // Tile layer access.
        // ------------------------------------------------------------------

        public IReadOnlyList<NeoTilePlacementRecord> TileRecords(string layerId) =>
            GetTileLayerIndex(layerId).Records;

        public IReadOnlyDictionary<Vector2Int, List<NeoTilePlacementRecord>> TileCandidatesByCell(
            string layerId) =>
            GetTileLayerIndex(layerId).CandidatesByCell;

        public IReadOnlyList<NeoTilePlacementRecord> TileCandidatesAt(
            string layerId,
            Vector2Int cell)
        {
            return GetTileLayerIndex(layerId).CandidatesByCell.TryGetValue(cell, out var records)
                ? records
                : EmptyTileRecords;
        }

        // ------------------------------------------------------------------
        // Object layer access.
        // ------------------------------------------------------------------

        public IReadOnlyList<NeoObjectPlacementRecord> ObjectRecords(string layerId) =>
            GetObjectLayerIndex(layerId).Records;

        public IReadOnlyList<NeoObjectPlacementRecord> ObjectCandidatesAt(
            string layerId,
            Vector2Int cell)
        {
            return GetObjectLayerIndex(layerId).CandidatesByCell.TryGetValue(cell, out var records)
                ? records
                : EmptyObjectRecords;
        }

        // ------------------------------------------------------------------
        // Invalidation.
        // ------------------------------------------------------------------

        /// <summary>Grid-level change notifications (mutations, link renderer
        /// projections) invalidate the named layers directly.</summary>
        public void Apply(NeoTileGridChangedArgs args)
        {
            foreach (var change in args.TileLayers)
            {
                tileLayers.Remove(change.LayerId);
            }
            foreach (var change in args.ObjectLayers)
            {
                objectLayers.Remove(change.LayerId);
            }
        }

        private void HandleWritableValueChanged(NeoValueOwnership ownership, string valueId)
        {
            InvalidateDependents(tileLayers, valueId);
            InvalidateDependents(objectLayers, valueId);
        }

        /// <summary>Storage-partition load/unload rewrites a chunk of the
        /// authored graph wholesale; load/unload is rare, so every layer index
        /// is dropped for a lazy rebuild rather than diffing dependencies.</summary>
        private void HandleValuePartitionChanged(string mapKey)
        {
            tileLayers.Clear();
            objectLayers.Clear();
        }

        private static void InvalidateDependents<TIndex>(
            Dictionary<string, TIndex> indexes,
            string valueId)
            where TIndex : class, ILayerIndex
        {
            List<string>? stale = null;
            foreach (var pair in indexes)
            {
                if (!pair.Value.DependencyIds.Contains(valueId)) continue;
                stale ??= new List<string>();
                stale.Add(pair.Key);
            }
            if (stale is null) return;
            foreach (var layerId in stale)
            {
                indexes.Remove(layerId);
            }
        }

        // ------------------------------------------------------------------
        // Index construction.
        // ------------------------------------------------------------------

        private TileLayerIndex GetTileLayerIndex(string layerId)
        {
            if (tileLayers.TryGetValue(layerId, out var index)) return index;
            var dependencyIds = new HashSet<string>();
            var records = primitive.BuildTileLayerRecords(layerId, dependencyIds);
            var byCell = new Dictionary<Vector2Int, List<NeoTilePlacementRecord>>();
            foreach (var record in records)
            {
                if (!byCell.TryGetValue(record.Cell, out var cellRecords))
                {
                    cellRecords = new List<NeoTilePlacementRecord>();
                    byCell[record.Cell] = cellRecords;
                }
                cellRecords.Add(record);
            }
            foreach (var cellRecords in byCell.Values)
            {
                if (cellRecords.Count < 2) continue;
                // Loser→winner: the conflict tiebreak is (updatedAt desc,
                // id asc), and readers take the LAST resolvable candidate.
                cellRecords.Sort(CompareLoserToWinner);
            }
            index = new TileLayerIndex(records, byCell, dependencyIds);
            tileLayers[layerId] = index;
            return index;
        }

        private static int CompareLoserToWinner(
            NeoTilePlacementRecord left,
            NeoTilePlacementRecord right)
        {
            int updated = left.UpdatedAtMs.CompareTo(right.UpdatedAtMs);
            if (updated != 0) return updated;
            // Ties break id ASC for the winner; winner sits last, so sort
            // descending by instance id.
            return string.CompareOrdinal(right.InstanceId, left.InstanceId);
        }

        private ObjectLayerIndex GetObjectLayerIndex(string layerId)
        {
            if (objectLayers.TryGetValue(layerId, out var index)) return index;
            var dependencyIds = new HashSet<string>();
            var records = primitive.BuildObjectLayerRecords(layerId, dependencyIds);
            var byCell = new Dictionary<Vector2Int, List<NeoObjectPlacementRecord>>();
            foreach (var record in records)
            {
                foreach (var cell in record.Footprint)
                {
                    if (!byCell.TryGetValue(cell, out var cellRecords))
                    {
                        cellRecords = new List<NeoObjectPlacementRecord>();
                        byCell[cell] = cellRecords;
                    }
                    cellRecords.Add(record);
                }
            }
            index = new ObjectLayerIndex(records, byCell, dependencyIds);
            objectLayers[layerId] = index;
            return index;
        }

        private interface ILayerIndex
        {
            HashSet<string> DependencyIds { get; }
        }

        private sealed class TileLayerIndex : ILayerIndex
        {
            public TileLayerIndex(
                List<NeoTilePlacementRecord> records,
                Dictionary<Vector2Int, List<NeoTilePlacementRecord>> candidatesByCell,
                HashSet<string> dependencyIds)
            {
                Records = records;
                CandidatesByCell = candidatesByCell;
                DependencyIds = dependencyIds;
            }

            public List<NeoTilePlacementRecord> Records { get; }
            public Dictionary<Vector2Int, List<NeoTilePlacementRecord>> CandidatesByCell { get; }
            public HashSet<string> DependencyIds { get; }
        }

        private sealed class ObjectLayerIndex : ILayerIndex
        {
            public ObjectLayerIndex(
                List<NeoObjectPlacementRecord> records,
                Dictionary<Vector2Int, List<NeoObjectPlacementRecord>> candidatesByCell,
                HashSet<string> dependencyIds)
            {
                Records = records;
                CandidatesByCell = candidatesByCell;
                DependencyIds = dependencyIds;
            }

            public List<NeoObjectPlacementRecord> Records { get; }
            public Dictionary<Vector2Int, List<NeoObjectPlacementRecord>> CandidatesByCell { get; }
            public HashSet<string> DependencyIds { get; }
        }
    }
}
