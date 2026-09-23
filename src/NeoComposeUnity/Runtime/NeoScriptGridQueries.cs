// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>Placement reads retained by an evaluation, including cells with no matches.</summary>
    public sealed class NeoScriptGridReads : IDisposable
    {
        private sealed class GridReads
        {
            internal readonly HashSet<Vector2Int> objects = new();
            internal readonly HashSet<Vector2Int> tiles = new();
            internal readonly HashSet<string> placements = new();
            internal IDisposable? subscription;
            internal IDisposable? layerSubscription;
        }
        private readonly Dictionary<INeoTileGridContent, GridReads> grids = new();
        private readonly Action? invalidated;
        public NeoScriptGridReads(Action? invalidated = null) => this.invalidated = invalidated;

        internal void Record(INeoTileGridContent content, string placementId, Vector2Int? cell, bool tile)
        {
            if (!grids.TryGetValue(content, out GridReads reads))
            {
                reads = new GridReads();
                grids.Add(content, reads);
                if (invalidated is not null)
                    reads.subscription = content.Primitive.Client.ScriptGridQueries.OnChanged(content.Primitive.GridValueId, change =>
                    {
                        foreach (var layer in change.ObjectLayers)
                        {
                            foreach (var id in layer.ChangedInstances)
                                if (reads.placements.Contains(id.Value)) { Invalidate(); return; }
                            foreach (var changed in layer.ChangedCells)
                                if (reads.objects.Contains(changed)) { Invalidate(); return; }
                        }
                        foreach (var layer in change.TileLayers)
                            foreach (var changed in layer.ChangedCells)
                                if (reads.tiles.Contains(changed)) { Invalidate(); return; }
                    });
            }
            if (invalidated is not null && reads.layerSubscription is null)
                reads.layerSubscription = content.Primitive.Client.ScriptGridQueries.OnLayerInvalidated(content.Primitive.GridValueId,
                    (layerId, tileLayer) =>
                    {
                        if (tileLayer ? reads.tiles.Count > 0 : reads.objects.Count > 0 || reads.placements.Count > 0)
                            Invalidate();
                    });
            reads.placements.Add(placementId);
            if (cell is Vector2Int queried) (tile ? reads.tiles : reads.objects).Add(queried);
        }

        private bool isInvalidated;
        private void Invalidate()
        {
            if (isInvalidated) return;
            isInvalidated = true;
            invalidated?.Invoke();
        }

        private readonly Dictionary<NeoClient, (HashSet<(NeoValueOwnership ownership, string id)> ids, Action<NeoValueOwnership, string> handler)> values = new();
        internal void RecordValue(NeoClient client, NeoValueOwnership ownership, string id)
        {
            if (grids.Count == 0 || invalidated is null) return;
            if (!values.TryGetValue(client, out var reads))
            {
                var ids = new HashSet<(NeoValueOwnership ownership, string id)>();
                void Changed(NeoValueOwnership changedOwnership, string changedId)
                {
                    if (ids.Contains((changedOwnership, changedId))) Invalidate();
                }
                reads = (ids, Changed);
                values.Add(client, reads);
                client.OnWritableValueChanged += Changed;
            }
            reads.ids.Add((ownership, id));
        }

        public void Dispose()
        {
            foreach (GridReads reads in grids.Values) { reads.subscription?.Dispose(); reads.layerSubscription?.Dispose(); }
            grids.Clear();
            foreach (var reads in values) reads.Key.OnWritableValueChanged -= reads.Value.handler;
            values.Clear();
        }

        /// <summary>Drops every recorded read and subscription so the same instance can record a new evaluation.</summary>
        internal void Reset()
        {
            Dispose();
            isInvalidated = false;
        }
    }

    public sealed class NeoScriptGridQueries
    {
        private readonly NeoClient client;
        private readonly Dictionary<string, INeoTileGridContent> contentByGrid = new();
        private readonly Dictionary<string, (string grid, string layer, string instance)> placements = new();
        private IReadOnlyDictionary<string, Func<NeoClient, string, INeoTileGridContent>> factories =
            new Dictionary<string, Func<NeoClient, string, INeoTileGridContent>>();

        internal NeoScriptGridQueries(NeoClient client) => this.client = client;
        private event Action<NeoTileGridChangedArgs>? Changed;
        internal void NotifyChanged(NeoTileGridChangedArgs change) => Changed?.Invoke(change);
        internal IDisposable OnChanged(string gridId, Action<NeoTileGridChangedArgs> handler)
        {
            void Handle(NeoTileGridChangedArgs change) { if (change.GridValueId == gridId) handler(change); }
            Changed += Handle;
            return new NeoDisposableSubscription(() => Changed -= Handle);
        }

        private event Action<string, string, bool>? LayerInvalidated;
        internal void NotifyLayerInvalidated(string gridId, string layerId, bool tile) => LayerInvalidated?.Invoke(gridId, layerId, tile);
        internal IDisposable OnLayerInvalidated(string gridId, Action<string, bool> handler)
        {
            void Handle(string changedGrid, string layerId, bool tile) { if (gridId == changedGrid) handler(layerId, tile); }
            LayerInvalidated += Handle;
            return new NeoDisposableSubscription(() => LayerInvalidated -= Handle);
        }

        public void RegisterFactories(IReadOnlyDictionary<string, Func<NeoClient, string, INeoTileGridContent>> factories) => this.factories = factories;
        public void RegisterContent(INeoTileGridContent content) => contentByGrid[content.Primitive.GridValueId] = content;
        internal void RegisterContent(INeoTileGridContent content, string gridId) => contentByGrid[gridId] = content;
        internal void Bind(string receiverId, string gridId, string layerId, string instanceId) => placements[receiverId] = (gridId, layerId, instanceId);

        private (INeoTileGridContent content, NeoObjectPlacementRecord placement) Resolve(string receiverId)
        {
            if (placements.TryGetValue(receiverId, out var binding)
                && contentByGrid.TryGetValue(binding.grid, out var content))
            {
                var placement = content.Primitive.LookupCache.ObjectRecord(binding.layer, binding.instance);
                if (placement is not null) return (content, placement);
                placements.Remove(receiverId);
            }
            // Direct stored-row invocation may precede access through generated grid content.
            // Resolve its owning grid once; subsequent calls use the placement/cache binding.
            string current = receiverId;
            var visited = new HashSet<string>();
            while (visited.Add(current))
            {
                if (!client.TryGetValue(current, out MemberValue? row)) break;
                if (row.classId is string classId && factories.TryGetValue(classId, out var factory))
                {
                    content = contentByGrid.TryGetValue(current, out var existing) ? existing : factory(client, current);
                    RegisterContent(content);
                    foreach (var layer in content.ObjectLayersInOrder)
                    {
                        var placement = content.Primitive.LookupCache.ObjectRecord(layer.LayerId, receiverId);
                        if (placement is null) continue;
                        Bind(receiverId, current, layer.LayerId, placement.InstanceId);
                        return (content, placement);
                    }
                    break;
                }
                string? parent = null;
                foreach (string candidate in client.GridQueryParents(current)) { parent = candidate; break; }
                if (parent is null) break;
                current = parent;
            }
            throw new NSGetterRuntimeError("Grid queries require an actual placed NeoObject in an owning grid.");
        }

        public object? Invoke(string memberId, INeoValueReference receiver, object?[] args)
        {
            if (receiver.valueId is not string id || !client.TryGetValue(id, out MemberValue? row))
                throw new NSGetterRuntimeError("Grid query receiver has no placement identity.");
            var ownership = client.TryGetValueOwnership(id, out NeoValueOwnership found) ? found : NeoValueOwnership.Asset;
            var ctx = client.CreateGetterContext(ownership);
            object? value = NSGetterEvaluator.UnwrapRow(row, ctx, ownership);
            if (!TryInvoke(memberId, value, args, ctx, out object? result))
                throw new NSGetterRuntimeError("Unknown grid query member.");
            return result;
        }

        internal bool TryInvoke(string memberId, object? receiver, object?[] args, NSGetterEvaluator.Context ctx, out object? result)
        {
            result = null;
            bool getCell = memberId == "system_df1c2d06-eeec-5340-addc-740f3668c9e4";
            bool getObjects = memberId == "system_f5ca386c-990c-54a1-8473-2d49d2cd887d";
            bool getTile = memberId == "system_593e6208-e2ca-505e-9933-04b17102b6d2";
            if (!getCell && !getObjects && !getTile) return false;
            string receiverId = NSGetterEvaluator.FindRowIdByReference(receiver, ctx)
                ?? (receiver as INeoValueReference)?.valueId
                ?? throw new NSGetterRuntimeError("Grid query receiver has no placement identity.");
            var (content, placement) = Resolve(receiverId);
            ctx.gridReads?.Record(content, placement.InstanceId, null, false);
            ctx.client.NoteGridRead(content, placement.InstanceId, null, false);
            if (getCell)
            {
                result = NeoVectorValues.FromVector2Int(placement.Cell);
                return true;
            }
            NeoCellPattern pattern = NeoCellPatternStorage.ReadRuntime(args[0], ctx);
            ctx.allocationTracker.ConsumeCollectionVisit(pattern.Count);
            var objects = getObjects ? new List<object?>() : null;
            foreach (Vector2Int cell in pattern.GetCells(placement.Cell))
            {
                ctx.gridReads?.Record(content, placement.InstanceId, cell, getTile);
                ctx.client.NoteGridRead(content, placement.InstanceId, cell, getTile);
                if (getTile)
                {
                    var tile = content.GetTile(cell);
                    if (tile is null) continue;
                    result = RuntimeValue(tile, ctx);
                    return true;
                }
                // NeoScript already consumes stored-row views. Do not create
                // generated C# wrappers and intermediate lists for every cell.
                foreach (var layer in content.ObjectLayersInOrder)
                    foreach (var item in content.Primitive.LookupCache.ObjectCandidatesAt(layer.LayerId, cell))
                    {
                        ctx.allocationTracker.ConsumeProducedCollectionEntry();
                        Bind(item.InstanceId, content.Primitive.GridValueId, layer.LayerId, item.InstanceId);
                        objects!.Add(RuntimeValue(item.InstanceId, ctx));
                    }
            }
            result = getObjects ? objects!.ToArray() : null;
            return true;
        }

        private static object? RuntimeValue(INeoValueReference reference, NSGetterEvaluator.Context ctx)
        {
            if (reference.valueId is not string id)
                throw new NSGetterRuntimeError("Grid query result has no stored value.");
            return RuntimeValue(id, ctx);
        }

        private static object? RuntimeValue(string id, NSGetterEvaluator.Context ctx)
        {
            if (!ctx.client.TryGetValue(id, out MemberValue? row))
                throw new NSGetterRuntimeError("Grid query result has no stored value.");
            var ownership = ctx.client.TryGetValueOwnership(id, out NeoValueOwnership found) ? found : NeoValueOwnership.Asset;
            return NSGetterEvaluator.UnwrapRow(row, ctx, ownership);
        }
    }
}
