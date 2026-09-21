// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Client-owned spatial index shared by all views of a grid.
    /// First access reads the layer's containment membership once; occupied
    /// cells are sorted lazily for deterministic enumeration. Point queries are O(1)
    /// dictionary hits. Invalidation is value-change-event
    /// driven: every value id the build consulted (grid row, Children list,
    /// link rows, containment list values, placement rows and their Cell
    /// children, object rows, ...) is recorded as a dependency, and a write
    /// to any of them — including the membership-change notification a
    /// containerId-carrying row raises for its container — drops the layer
    /// index for a lazy rebuild. Validated runtime leaf writes preserve these
    /// indexes; object movement patches only its footprints and projected tiles.
    /// </summary>
    internal sealed partial class NeoTileGridLookupCache : IDisposable
    {
        private static readonly IReadOnlyList<NeoTilePlacementRecord> EmptyTileRecords =
            Array.Empty<NeoTilePlacementRecord>();
        private static readonly IReadOnlyList<NeoObjectPlacementRecord> EmptyObjectRecords =
            Array.Empty<NeoObjectPlacementRecord>();

        private readonly NeoReadOnlyTileGridPrimitive primitive;
        private readonly Dictionary<string, TileLayerIndex> tileLayers = new();
        private readonly Dictionary<string, ObjectLayerIndex> objectLayers = new();
        private readonly Dictionary<string, TileLayerIndex> changedTileLayers = new();
        private readonly Dictionary<string, ObjectLayerIndex> changedObjectLayers = new();
        private readonly HashSet<string> knownTileLayers = new();
        private readonly HashSet<string> knownObjectLayers = new();
        private readonly Dictionary<string, HashSet<Vector2Int>> convertedTileCells = new();
        private event Action<NeoTileGridChangedArgs>? Changed;

        public NeoTileGridLookupCache(NeoClient client, string gridValueId)
        {
            primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, gridValueId);
            client.OnWritableValuesPublished += InvalidatePublishedValues;
            client.OnWritableValuesChanged += NotifyPublishedValues;
            client.OnValuePartitionChanged += HandleValuePartitionChanged;
        }

        public IDisposable Subscribe(Action<NeoTileGridChangedArgs> handler)
        {
            Changed += handler;
            return new NeoDisposableSubscription(() => Changed -= handler);
        }

        public void NotifyChanged(NeoTileGridChangedArgs args)
        {
            primitive.Client.ScriptGridQueries.NotifyChanged(args);
            Changed?.Invoke(args);
        }

        public void Dispose()
        {
            primitive.Client.OnWritableValuesPublished -= InvalidatePublishedValues;
            primitive.Client.OnWritableValuesChanged -= NotifyPublishedValues;
            primitive.Client.OnValuePartitionChanged -= HandleValuePartitionChanged;
            Changed = null;
            tileLayers.Clear();
            objectLayers.Clear();
            changedTileLayers.Clear();
            changedObjectLayers.Clear();
            knownTileLayers.Clear();
            knownObjectLayers.Clear();
            convertedTileCells.Clear();
        }

        // ------------------------------------------------------------------
        // Tile layer access.
        // ------------------------------------------------------------------

        public IReadOnlyList<NeoTilePlacementRecord> TileRecords(string layerId) =>
            GetTileLayerIndex(layerId).Records;

        public IReadOnlyList<List<NeoTilePlacementRecord>> TileCandidatesInCellOrder(
            string layerId) =>
            GetTileLayerIndex(layerId).CandidatesInCellOrder;

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

        public NeoObjectPlacementRecord? ObjectRecord(string layerId, string instanceId) =>
            GetObjectLayerIndex(layerId).ById.TryGetValue(instanceId, out var record) ? record : null;

        public IReadOnlyList<NeoObjectPlacementRecord> ObjectCandidatesAt(
            string layerId,
            Vector2Int cell)
        {
            return GetObjectLayerIndex(layerId).CandidatesByCell.TryGetValue(cell, out var records)
                ? records
                : EmptyObjectRecords;
        }

        public bool TryGetObjectCandidateAtAny(
            string layerId,
            IReadOnlyList<Vector2Int> cells,
            out NeoObjectPlacementRecord? record,
            out Vector2Int occupiedCell)
        {
            var candidatesByCell = GetObjectLayerIndex(layerId).CandidatesByCell;
            foreach (Vector2Int cell in cells)
            {
                if (!candidatesByCell.TryGetValue(cell, out var candidates)
                    || candidates.Count == 0)
                {
                    continue;
                }
                record = candidates[candidates.Count - 1];
                occupiedCell = cell;
                return true;
            }
            record = null;
            occupiedCell = default;
            return false;
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

        private void InvalidatePublishedValues(
            IReadOnlyCollection<(NeoValueOwnership ownership, string valueId)> changed,
            NeoWritePlan plan)
        {
            if (plan.ValidatedObjectInsertionGrid == primitive.GridValueId) return;
            if (plan.HasValidatedRuntimeLeaves) return;
            if (plan.ValidatedTileConversions.Count != 0)
            {
                ApplyTileConversions(plan.ValidatedTileConversions);
                return;
            }
            var ids = new HashSet<string>();
            foreach (var value in changed)
                if (!plan.UnchangedValueIds.Contains(value.valueId)) ids.Add(value.valueId);
            InvalidateDependents(tileLayers, changedTileLayers, ids);
            InvalidateDependents(objectLayers, changedObjectLayers, ids);
            foreach (string layerId in changedTileLayers.Keys)
                if (plan.PreparedTileLayers.TryGetValue((primitive.GridValueId, layerId), out var prepared))
                    tileLayers[layerId] = BuildTileLayerIndex(prepared.Records, prepared.DependencyIds);
            foreach (string layerId in changedObjectLayers.Keys)
                if (plan.PreparedObjectLayers.TryGetValue((primitive.GridValueId, layerId), out var prepared))
                    objectLayers[layerId] = BuildObjectLayerIndex(prepared.Records, prepared.DependencyIds);
        }

        private void ApplyTileConversions(IReadOnlyList<NeoValidatedTileConversion> conversions)
        {
            foreach (var conversion in conversions)
            {
                if (conversion.GridValueId != primitive.GridValueId
                    || !tileLayers.TryGetValue(conversion.LayerId, out var layer)
                    || !layer.FirstRecordIndexByPlacementId.TryGetValue(conversion.PlacementValueId, out int firstIndex))
                    continue;
                if (!convertedTileCells.TryGetValue(conversion.LayerId, out var cells))
                    convertedTileCells[conversion.LayerId] = cells = new HashSet<Vector2Int>();
                PatchRecord(firstIndex);
                if (layer.AdditionalRecordIndicesByPlacementId.TryGetValue(conversion.PlacementValueId, out var additional))
                    foreach (int index in additional) PatchRecord(index);
                // Old Cell dependencies can remain: a conversion can materialize
                // a default once, but repeated conversions retain that child ID.
                layer.DependencyIds.Add(conversion.NextCellValueId);

                void PatchRecord(int index)
                {
                    var before = layer.Records[index];
                    var after = new NeoTilePlacementRecord(
                        before.InstanceId, before.PlacementValueId, before.Cell,
                        conversion.NextClassId, before.Ownership, before.Order,
                        before.SourceTileLayerLinkId, before.SourceObjectInstanceId,
                        conversion.NextUpdatedAtMs, conversion.NextCellValueId);
                    layer.Records[index] = after;
                    var candidates = layer.CandidatesByCell[before.Cell];
                    candidates[candidates.IndexOf(before)] = after;
                    cells.Add(before.Cell);
                }
            }
            foreach (var pair in convertedTileCells)
                foreach (Vector2Int cell in pair.Value)
                    tileLayers[pair.Key].CandidatesByCell[cell].Sort(CompareLoserToWinner);
        }

        /// <summary>Only this grid's partition can change its owned placements.
        /// Rebuild subscribed baselines on load so a subsequent write still
        /// emits a diff even when no caller has queried the reloaded grid.</summary>
        private void HandleValuePartitionChanged(string mapKey)
        {
            string? gridClassId = primitive.Client.ResolveValueRow(primitive.GridValueId)?.classId;
            if (gridClassId is null || mapKey != NeoClient.MakeWorldPartitionKey(gridClassId)) return;
            tileLayers.Clear();
            objectLayers.Clear();
            changedTileLayers.Clear();
            changedObjectLayers.Clear();
            convertedTileCells.Clear();
            if (Changed is null || !primitive.Client.IsValuePartitionLoaded(mapKey)) return;
            foreach (string layerId in knownTileLayers) GetTileLayerIndex(layerId);
            foreach (string layerId in knownObjectLayers) GetObjectLayerIndex(layerId);
        }

        private static void InvalidateDependents<TIndex>(
            Dictionary<string, TIndex> indexes,
            Dictionary<string, TIndex> changedIndexes,
            HashSet<string> valueIds)
            where TIndex : class, ILayerIndex
        {
            List<string>? stale = null;
            foreach (var pair in indexes)
            {
                if (!pair.Value.DependencyIds.Overlaps(valueIds)) continue;
                stale ??= new List<string>();
                stale.Add(pair.Key);
                changedIndexes.TryAdd(pair.Key, pair.Value);
            }
            if (stale is null) return;
            foreach (var layerId in stale)
            {
                indexes.Remove(layerId);
            }
        }

        private void NotifyPublishedValues(
            IReadOnlyCollection<(NeoValueOwnership ownership, string valueId)> changed)
        {
            if (changedTileLayers.Count == 0 && changedObjectLayers.Count == 0 && convertedTileCells.Count == 0) return;
            var changedIds = new HashSet<string>();
            foreach (var value in changed) changedIds.Add(value.valueId);
            var tiles = new List<NeoTileLayerChangedArgs>();
            var objects = new List<NeoObjectLayerChangedArgs>();
            foreach (var pair in convertedTileCells)
                tiles.Add(new NeoTileLayerChangedArgs(pair.Key, Array.Empty<Vector2Int>(),
                    new List<Vector2Int>(pair.Value), NeoTileGridChangeSourceKind.Direct, null));
            foreach (var pair in changedTileLayers)
            {
                var next = GetTileLayerIndex(pair.Key);
                var clear = new List<Vector2Int>();
                foreach (var cell in pair.Value.CandidatesByCell.Keys)
                    if (!next.CandidatesByCell.ContainsKey(cell)) clear.Add(cell);
                var refresh = new List<Vector2Int>();
                foreach (var cell in next.CandidatesByCell)
                    if (!pair.Value.CandidatesByCell.TryGetValue(cell.Key, out var previous)
                        || TileCandidatesChanged(previous, cell.Value, changedIds))
                        refresh.Add(cell.Key);
                if (clear.Count == 0 && refresh.Count == 0) continue;
                tiles.Add(new NeoTileLayerChangedArgs(pair.Key, clear, refresh,
                    NeoTileGridChangeSourceKind.Direct, null));
            }
            foreach (var pair in changedObjectLayers)
            {
                var next = GetObjectLayerIndex(pair.Key);
                var removed = new List<NeoObjectInstanceId>();
                var cells = new HashSet<Vector2Int>();
                var contentCells = new HashSet<Vector2Int>();
                foreach (var previous in pair.Value.ById)
                    if (!next.ById.ContainsKey(previous.Key))
                    {
                        removed.Add(previous.Key);
                        cells.UnionWith(previous.Value.Footprint);
                        contentCells.UnionWith(previous.Value.Footprint);
                    }
                var updated = new List<NeoObjectInstanceId>();
                Dictionary<NeoObjectInstanceId, int>? orderOnly = null;
                foreach (var current in next.ById)
                {
                    if (pair.Value.ById.TryGetValue(current.Key, out var previous)
                        && !ObjectChanged(previous, current.Value, changedIds)) continue;
                    updated.Add(current.Key);
                    if (previous is not null && !ObjectChanged(previous, current.Value, changedIds, ignoreOrder: true))
                        (orderOnly ??= new())[current.Key] = current.Value.Order - previous.Order;
                    else
                    {
                        if (previous is not null) contentCells.UnionWith(previous.Footprint);
                        contentCells.UnionWith(current.Value.Footprint);
                    }
                    if (previous is not null) cells.UnionWith(previous.Footprint);
                    cells.UnionWith(current.Value.Footprint);
                }
                if (removed.Count == 0 && updated.Count == 0 && cells.Count == 0) continue;
                objects.Add(new NeoObjectLayerChangedArgs(pair.Key, removed, updated,
                    new List<Vector2Int>(cells), NeoTileGridChangeSourceKind.Direct, null) { OrderOnlyDeltas = orderOnly, ContentChangedCells = new List<Vector2Int>(contentCells) });
            }
            changedTileLayers.Clear();
            changedObjectLayers.Clear();
            convertedTileCells.Clear();
            if (tiles.Count != 0 || objects.Count != 0)
                NotifyChanged(new NeoTileGridChangedArgs(
                    primitive.GridValueId, tiles, objects, primitive.Client.CurrentChangeSource));
        }

        private static bool TileCandidatesChanged(
            IReadOnlyList<NeoTilePlacementRecord> previous,
            IReadOnlyList<NeoTilePlacementRecord> current,
            HashSet<string> changedIds)
        {
            if (previous.Count != current.Count) return true;
            for (int i = 0; i < current.Count; i++)
            {
                var before = previous[i];
                var after = current[i];
                if (before.InstanceId != after.InstanceId
                    || before.AssetClassId != after.AssetClassId
                    || before.Ownership != after.Ownership
                    || changedIds.Contains(after.PlacementValueId)
                    || (after.CellValueId is not null && changedIds.Contains(after.CellValueId)))
                    return true;
            }
            return false;
        }

        private static bool ObjectChanged(
            NeoObjectPlacementRecord before,
            NeoObjectPlacementRecord after,
            HashSet<string> changedIds,
            bool ignoreOrder = false)
        {
            if (changedIds.Contains(after.InstanceId)
                || before.Cell != after.Cell
                || before.AssetClassId != after.AssetClassId
                || before.AssetValueId != after.AssetValueId
                || before.Ownership != after.Ownership
                || (!ignoreOrder && before.Order != after.Order)
                || before.Footprint.Count != after.Footprint.Count) return true;
            for (int i = 0; i < after.Footprint.Count; i++)
                if (before.Footprint[i] != after.Footprint[i]) return true;
            return false;
        }

        // ------------------------------------------------------------------
        // Index construction.
        // ------------------------------------------------------------------

        private TileLayerIndex GetTileLayerIndex(string layerId)
        {
            knownTileLayers.Add(layerId);
            if (tileLayers.TryGetValue(layerId, out var index)) return index;
            var dependencyIds = new HashSet<string>();
            var records = primitive.BuildTileLayerRecords(layerId, dependencyIds);
            index = BuildTileLayerIndex(records, dependencyIds);
            tileLayers[layerId] = index;
            return index;
        }

        private static TileLayerIndex BuildTileLayerIndex(
            List<NeoTilePlacementRecord> records, HashSet<string> dependencyIds)
        {
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
            return new TileLayerIndex(records, byCell, dependencyIds);
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
            knownObjectLayers.Add(layerId);
            if (objectLayers.TryGetValue(layerId, out var index)) return index;
            var dependencyIds = new HashSet<string>();
            var records = primitive.BuildObjectLayerRecords(layerId, dependencyIds);
            index = BuildObjectLayerIndex(records, dependencyIds);
            objectLayers[layerId] = index;
            return index;
        }

        private static ObjectLayerIndex BuildObjectLayerIndex(
            List<NeoObjectPlacementRecord> records, HashSet<string> dependencyIds)
        {
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
            return new ObjectLayerIndex(records, byCell, dependencyIds);
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
                FirstRecordIndexByPlacementId = new Dictionary<string, int>(records.Count);
                for (int i = 0; i < records.Count; i++)
                {
                    string id = records[i].PlacementValueId;
                    if (FirstRecordIndexByPlacementId.TryAdd(id, i)) continue;
                    if (!AdditionalRecordIndicesByPlacementId.TryGetValue(id, out var indices))
                        AdditionalRecordIndicesByPlacementId[id] = indices = new List<int>();
                    indices.Add(i);
                }
                CandidatesByCell = candidatesByCell;
                DependencyIds = dependencyIds;
                for (int i = 0; i < records.Count; i++)
                    if (records[i].SourceObjectInstanceId is string objectId)
                    {
                        if (!RecordIndicesByObject.TryGetValue(objectId, out var indices))
                            RecordIndicesByObject[objectId] = indices = new List<int>();
                        indices.Add(i);
                    }
            }

            public Dictionary<string, List<int>> RecordIndicesByObject { get; } = new();
            private IReadOnlyList<List<NeoTilePlacementRecord>>? candidatesInCellOrder;
            internal void InvalidateCellOrder() => candidatesInCellOrder = null;
            public IReadOnlyList<List<NeoTilePlacementRecord>> CandidatesInCellOrder =>
                candidatesInCellOrder ??= SortCells();

            private IReadOnlyList<List<NeoTilePlacementRecord>> SortCells()
            {
                var ordered = new List<List<NeoTilePlacementRecord>>(CandidatesByCell.Values);
                ordered.Sort((left, right) =>
                {
                    int y = left[0].Cell.y.CompareTo(right[0].Cell.y);
                    return y != 0 ? y : left[0].Cell.x.CompareTo(right[0].Cell.x);
                });
                return ordered;
            }

            public List<NeoTilePlacementRecord> Records { get; }
            public Dictionary<string, int> FirstRecordIndexByPlacementId { get; }
            public Dictionary<string, List<int>> AdditionalRecordIndicesByPlacementId { get; } = new();
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
                ById = new Dictionary<string, NeoObjectPlacementRecord>(records.Count);
                for (int i = 0; i < records.Count; i++)
                {
                    ById.Add(records[i].InstanceId, records[i]);
                    RecordIndices.Add(records[i].InstanceId, i);
                }
                CandidatesByCell = candidatesByCell;
                DependencyIds = dependencyIds;
            }

            public Dictionary<string, int> RecordIndices { get; } = new();
            public Dictionary<string, NeoObjectPlacementRecord> ById { get; }
            public List<NeoObjectPlacementRecord> Records { get; }
            public Dictionary<Vector2Int, List<NeoObjectPlacementRecord>> CandidatesByCell { get; }
            public HashSet<string> DependencyIds { get; }
        }
    }

    public partial class NeoClient
    {
        private readonly Dictionary<string, NeoTileGridLookupCache> gridLookupCaches = new();

        internal NeoTileGridLookupCache GetGridLookupCache(string gridValueId)
        {
            if (!gridLookupCaches.TryGetValue(gridValueId, out var cache))
                gridLookupCaches[gridValueId] = cache = new NeoTileGridLookupCache(this, gridValueId);
            return cache;
        }

        private void DisposeGridLookupCaches()
        {
            foreach (var cache in gridLookupCaches.Values) cache.Dispose();
            gridLookupCaches.Clear();
        }
    }
}
