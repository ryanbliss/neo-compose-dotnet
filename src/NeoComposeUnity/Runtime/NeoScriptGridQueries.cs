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
                SubscribeGrid(content, reads);
            }
            reads.placements.Add(placementId);
            if (cell is Vector2Int queried)
                (tile ? reads.tiles : reads.objects).Add(queried);
        }

        private void SubscribeGrid(INeoTileGridContent content, GridReads reads)
        {
            if (invalidated is null)
                return;
            reads.subscription = content.Primitive.Client.ScriptGridQueries.OnChanged(content.Primitive.GridValueId, change =>
            {
                foreach (var layer in change.ObjectLayers)
                {
                    foreach (var id in layer.ChangedInstances)
                        if (reads.placements.Contains(id.Value))
                        {
                            Invalidate();
                            return;
                        }
                    foreach (var changed in layer.ChangedCells)
                        if (reads.objects.Contains(changed))
                        {
                            Invalidate();
                            return;
                        }
                }
                foreach (var layer in change.TileLayers)
                    foreach (var changed in layer.ChangedCells)
                        if (reads.tiles.Contains(changed))
                        {
                            Invalidate();
                            return;
                        }
            });
            reads.layerSubscription = content.Primitive.Client.ScriptGridQueries.OnLayerInvalidated(content.Primitive.GridValueId,
                (layerId, tileLayer) =>
                {
                    if (tileLayer ? reads.tiles.Count > 0 : reads.objects.Count > 0 || reads.placements.Count > 0)
                        Invalidate();
                });
        }

        private bool isInvalidated;
        private void Invalidate()
        {
            if (isInvalidated)
                return;
            isInvalidated = true;
            invalidated?.Invoke();
        }

        private readonly Dictionary<NeoClient, (HashSet<(NeoValueOwnership ownership, string id)> ids, Action<NeoValueOwnership, string> handler)> values = new();
        /// <summary>Whether a grid read was recorded, so value reads are recorded too.</summary>
        internal bool RecordsGrid => grids.Count != 0;
        internal void RecordValue(NeoClient client, NeoValueOwnership ownership, string id)
        {
            if (!RecordsGrid || invalidated is null)
                return;
            if (!values.TryGetValue(client, out var reads))
            {
                reads = SubscribeValues(client);
                values.Add(client, reads);
            }
            reads.ids.Add((ownership, id));
        }

        // Keep callback captures off RecordValue's hot frame, including its
        // no-grid early return. Merely reading a scalar needs no closure.
        private (HashSet<(NeoValueOwnership ownership, string id)> ids, Action<NeoValueOwnership, string> handler)
            SubscribeValues(NeoClient client)
        {
            var ids = new HashSet<(NeoValueOwnership ownership, string id)>();
            void Changed(NeoValueOwnership ownership, string id)
            {
                if (ids.Contains((ownership, id)))
                    Invalidate();
            }
            client.OnWritableValueChanged += Changed;
            return (ids, Changed);
        }

        public void Dispose()
        {
            // Most evaluations read no grid or watched value: skip the walks.
            if (grids.Count == 0 && values.Count == 0)
                return;
            foreach (GridReads reads in grids.Values)
            {
                reads.subscription?.Dispose();
                reads.layerSubscription?.Dispose();
            }
            grids.Clear();
            foreach (var reads in values)
                reads.Key.OnWritableValueChanged -= reads.Value.handler;
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
        // A class, so a lookup hands back one reference: copying a tuple of
        // references out of the table costs a GC write barrier per field.
        private sealed class PlacementBinding
        {
            internal readonly string grid;
            internal readonly string layer;
            internal readonly string instance;
            // The placement last resolved, held while the grid's content and
            // its lookup cache's object layer indexes stay the same and no
            // grid change has moved an object.
            internal INeoTileGridContent? content;
            internal NeoTileGridLookupCache? cache;
            internal NeoObjectPlacementRecord? record;
            internal int layersVersion;
            internal int changeEpoch;

            internal PlacementBinding(string grid, string layer, string instance)
            {
                this.grid = grid;
                this.layer = layer;
                this.instance = instance;
            }
        }

        private readonly Dictionary<string, PlacementBinding> placements = new();
        // The last receiver's binding: a NeoScript caller usually queries
        // around one receiver several times in a row. Any write to
        // placements clears it.
        private string? lastReceiverId;
        private PlacementBinding? lastBinding;
        // GetObjects' buffers; filling them runs no NeoScript, so no query nests inside another.
        private readonly List<object?> queriedObjects = new();
        private Dictionary<Vector2Int, List<NeoObjectPlacementRecord>>[] layerCellsBuffer =
            Array.Empty<Dictionary<Vector2Int, List<NeoObjectPlacementRecord>>>();
        // Whose indexes layerCellsBuffer holds: they serve the next query of
        // the same layers until the lookup cache drops an object layer index.
        private string[] layerCellsLayerIds = Array.Empty<string>();
        private int layerCellsLayerCount;
        private NeoTileGridLookupCache? layerCellsCache;
        private int layerCellsVersion;
        private IReadOnlyDictionary<string, Func<NeoClient, string, INeoTileGridContent>> factories =
            new Dictionary<string, Func<NeoClient, string, INeoTileGridContent>>();

        // Moves with every grid change and content registration, so a binding's held placement is current.
        private int changeEpoch;

        internal NeoScriptGridQueries(NeoClient client) => this.client = client;
        private event Action<NeoTileGridChangedArgs>? Changed;
        internal void NotifyChanged(NeoTileGridChangedArgs change)
        {
            changeEpoch++;
            Changed?.Invoke(change);
        }
        internal IDisposable OnChanged(string gridId, Action<NeoTileGridChangedArgs> handler)
        {
            void Handle(NeoTileGridChangedArgs change)
            {
                if (change.GridValueId == gridId)
                    handler(change);
            }
            Changed += Handle;
            return new NeoDisposableSubscription(() => Changed -= Handle);
        }

        private event Action<string, string, bool>? LayerInvalidated;
        internal void NotifyLayerInvalidated(string gridId, string layerId, bool tile) => LayerInvalidated?.Invoke(gridId, layerId, tile);
        internal IDisposable OnLayerInvalidated(string gridId, Action<string, bool> handler)
        {
            void Handle(string changedGrid, string layerId, bool tile)
            {
                if (gridId == changedGrid)
                    handler(layerId, tile);
            }
            LayerInvalidated += Handle;
            return new NeoDisposableSubscription(() => LayerInvalidated -= Handle);
        }

        public void RegisterFactories(IReadOnlyDictionary<string, Func<NeoClient, string, INeoTileGridContent>> factories) => this.factories = factories;
        public void RegisterContent(INeoTileGridContent content) => RegisterContent(content, content.Primitive.GridValueId);
        internal void RegisterContent(INeoTileGridContent content, string gridId)
        {
            contentByGrid[gridId] = content;
            changeEpoch++;
        }
        internal void Bind(string receiverId, string gridId, string layerId, string instanceId)
        {
            // Queries rebind the same placements every call; only a change writes.
            if (placements.TryGetValue(receiverId, out PlacementBinding? bound)
                && ReferenceEquals(bound.grid, gridId)
                && ReferenceEquals(bound.layer, layerId)
                && ReferenceEquals(bound.instance, instanceId))
            {
                return;
            }
            placements[receiverId] = new PlacementBinding(gridId, layerId, instanceId);
            lastReceiverId = null;
        }

        /// <summary>The receiver's binding, holding its current content and placement.</summary>
        private PlacementBinding Resolve(string receiverId)
        {
            INeoTileGridContent? content;
            PlacementBinding? binding = ReferenceEquals(lastReceiverId, receiverId) ? lastBinding : null;
            if (binding is not null || placements.TryGetValue(receiverId, out binding))
            {
                if (binding.record is not null && binding.changeEpoch == changeEpoch)
                {
                    // Read on every query: the getter re-ensures an unloaded world partition.
                    NeoTileGridLookupCache cache = binding.content!.Primitive.LookupCache;
                    if (ReferenceEquals(cache, binding.cache) && cache.ObjectLayersVersion == binding.layersVersion)
                        return Remember(receiverId, binding);
                }
                if (contentByGrid.TryGetValue(binding.grid, out content) && HoldPlacement(binding, content))
                    return Remember(receiverId, binding);
                placements.Remove(receiverId);
                lastReceiverId = null;
            }
            // Direct stored-row invocation may precede access through generated grid content.
            // Resolve its owning grid once; subsequent calls use the placement/cache binding.
            string current = receiverId;
            var visited = new HashSet<string>();
            while (visited.Add(current))
            {
                if (!client.TryGetValue(current, out MemberValue? row))
                    break;
                if (row.classId is string classId && factories.TryGetValue(classId, out var factory))
                {
                    content = contentByGrid.TryGetValue(current, out var existing) ? existing : factory(client, current);
                    RegisterContent(content);
                    foreach (var layer in content.ObjectLayersInOrder)
                    {
                        var placement = content.Primitive.LookupCache.ObjectRecord(layer.LayerId, receiverId);
                        if (placement is null)
                            continue;
                        Bind(receiverId, current, layer.LayerId, placement.InstanceId);
                        PlacementBinding bound = placements[receiverId];
                        HoldPlacement(bound, content);
                        return Remember(receiverId, bound);
                    }
                    break;
                }
                string? parent = null;
                foreach (string candidate in client.GridQueryParents(current))
                {
                    parent = candidate;
                    break;
                }
                if (parent is null)
                    break;
                current = parent;
            }
            throw new NSGetterRuntimeError("Grid queries require an actual placed NeoObject in an owning grid.");
        }

        // Compared first: rewriting the same references pays their write barriers.
        private PlacementBinding Remember(string receiverId, PlacementBinding binding)
        {
            if (!ReferenceEquals(lastReceiverId, receiverId))
                lastReceiverId = receiverId;
            if (!ReferenceEquals(lastBinding, binding))
                lastBinding = binding;
            return binding;
        }

        private bool HoldPlacement(PlacementBinding binding, INeoTileGridContent content)
        {
            NeoTileGridLookupCache cache = content.Primitive.LookupCache;
            binding.record = cache.ObjectRecord(binding.layer, binding.instance);
            if (binding.record is null)
                return false;
            binding.content = content;
            binding.cache = cache;
            binding.layersVersion = cache.ObjectLayersVersion;
            binding.changeEpoch = changeEpoch;
            return true;
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

        private const string GetObjectsId = "system_f5ca386c-990c-54a1-8473-2d49d2cd887d";
        private const string GetTileId = "system_593e6208-e2ca-505e-9933-04b17102b6d2";

        /// <summary>GetObjects and GetTile, which read their one pattern argument as offsets.</summary>
        internal static bool ReadsCells(string? memberId) => memberId is GetObjectsId or GetTileId;

        /// <summary>Whether the call returns a fresh array of the queried objects.</summary>
        internal static bool ReturnsObjects(string? memberId) => memberId == GetObjectsId;

        // Compares layer ids rather than the list: content may reuse one
        // list across layer changes.
        private bool HoldsLayerCells(IReadOnlyList<IReadOnlyNeoObjectLayerRuntime> layers)
        {
            if (layerCellsLayerCount != layers.Count)
                return false;
            for (int layer = 0; layer < layers.Count; layer++)
            {
                if (!string.Equals(layerCellsLayerIds[layer], layers[layer].LayerId, StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        internal bool TryInvoke(string memberId, object? receiver, object?[] args, NSGetterEvaluator.Context ctx, out object? result)
        {
            result = null;
            bool getCell = memberId == "system_df1c2d06-eeec-5340-addc-740f3668c9e4";
            bool getObjects = memberId == GetObjectsId;
            bool getTile = memberId == GetTileId;
            if (!getCell && !getObjects && !getTile)
                return false;
            // The grid indexes only committed placements.
            ctx.client.CommitScriptWrites();
            string receiverId = NSGetterEvaluator.FindRowIdByReference(receiver, ctx)
                ?? (receiver as INeoValueReference)?.valueId
                ?? throw new NSGetterRuntimeError("Grid query receiver has no placement identity.");
            PlacementBinding binding = Resolve(receiverId);
            INeoTileGridContent content = binding.content!;
            NeoObjectPlacementRecord placement = binding.record!;
            ctx.gridReads?.Record(content, placement.InstanceId, null, false);
            ctx.client.NoteGridRead(content, placement.InstanceId, null, false);
            if (getCell)
            {
                result = NeoVectorValues.FromVector2Int(placement.Cell);
                return true;
            }
            NeoCellPattern pattern = NeoCellPatternStorage.ReadRuntime(args[0], ctx);
            Vector2Int origin = placement.Cell;
            // Resolve each layer's cell index once per query rather than once
            // per layer per cell.
            IReadOnlyList<IReadOnlyNeoObjectLayerRuntime> layers = content.ObjectLayersInOrder;
            Dictionary<Vector2Int, List<NeoObjectPlacementRecord>>[]? layerCells = null;
            if (getObjects)
            {
                NeoTileGridLookupCache cache = content.Primitive.LookupCache;
                if (!ReferenceEquals(layerCellsCache, cache)
                    || layerCellsVersion != cache.ObjectLayersVersion
                    || !HoldsLayerCells(layers))
                {
                    if (layerCellsBuffer.Length < layers.Count)
                    {
                        layerCellsBuffer = new Dictionary<Vector2Int, List<NeoObjectPlacementRecord>>[layers.Count];
                        layerCellsLayerIds = new string[layers.Count];
                    }
                    for (int layer = 0; layer < layers.Count; layer++)
                    {
                        string layerId = layers[layer].LayerId;
                        layerCellsBuffer[layer] = cache.ObjectCandidatesByCell(layerId);
                        layerCellsLayerIds[layer] = layerId;
                    }
                    layerCellsLayerCount = layers.Count;
                    layerCellsCache = cache;
                    layerCellsVersion = cache.ObjectLayersVersion;
                }
                layerCells = layerCellsBuffer;
                queriedObjects.Clear();
            }
            for (int offset = 0; offset < pattern.Count; offset++)
            {
                Vector2Int cell = pattern.CellAt(origin, offset);
                ctx.gridReads?.Record(content, placement.InstanceId, cell, getTile);
                ctx.client.NoteGridRead(content, placement.InstanceId, cell, getTile);
                if (getTile)
                {
                    var tile = content.GetTile(cell);
                    if (tile is null)
                        continue;
                    result = RuntimeValue(tile, ctx);
                    return true;
                }
                // NeoScript already consumes stored-row views. Do not create
                // generated C# wrappers and intermediate lists for every cell.
                for (int layer = 0; layer < layers.Count; layer++)
                {
                    if (!layerCells![layer].TryGetValue(cell, out var items))
                        continue;
                    string layerId = layers[layer].LayerId;
                    for (int i = 0; i < items.Count; i++)
                    {
                        NeoObjectPlacementRecord item = items[i];
                        // Resolve validates every binding, so a stale mark
                        // costs only its slow path.
                        if (!ReferenceEquals(item.queryBoundLayerId, layerId))
                        {
                            Bind(item.InstanceId, content.Primitive.GridValueId, layerId, item.InstanceId);
                            item.queryBoundLayerId = layerId;
                        }
                        queriedObjects.Add(RuntimeValue(item.InstanceId, ref item.valueNode, ctx));
                    }
                }
            }
            if (!getObjects)
                return true;
            object?[] objects = NSGetterEvaluator.TemporaryLists.Rent(queriedObjects.Count);
            queriedObjects.CopyTo(objects);
            queriedObjects.Clear();
            ctx.NoteFreshList(objects);
            NSGetterEvaluator.TemporaryLists.MarkExclusive(objects);
            result = objects;
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
            NeoValueNode? node = null;
            return RuntimeValue(id, ref node, ctx);
        }

        private static object? RuntimeValue(string id, ref NeoValueNode? node, NSGetterEvaluator.Context ctx)
        {
            if (ctx.client.ReadValue(id, ref node) is not { } row)
                throw new NSGetterRuntimeError("Grid query result has no stored value.");
            var ownership = ctx.client.TryGetValueOwnership(id, ref node, out NeoValueOwnership found) ? found : NeoValueOwnership.Asset;
            return NSGetterEvaluator.UnwrapRow(row, ctx, ownership, node);
        }
    }
}
