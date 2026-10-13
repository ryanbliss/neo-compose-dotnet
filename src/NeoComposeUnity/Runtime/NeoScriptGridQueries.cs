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
        // GetObjects' and GetTiles' buffers; filling them runs no NeoScript, so no query nests inside another.
        private readonly List<object?> queried = new();
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

        /// <summary>
        /// A grid change, once its indexes are current: moves the held
        /// placements on and forgets the getters that read what it names.
        /// </summary>
        internal void NotifyChanged(NeoTileGridChangedArgs change)
        {
            changeEpoch++;
            client.InvalidateGetterMemoForGridChange(change);
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
        private const string GetTilesId = "system_5a41d36a-bcd9-4867-9e46-d743f6e09582";

        /// <summary>GetObjects, GetTile and GetTiles, which read their pattern argument as offsets.</summary>
        internal static bool ReadsCells(string? memberId) => memberId is GetObjectsId or GetTileId or GetTilesId;

        /// <summary>Whether the call returns a fresh array of the queried objects or tiles.</summary>
        internal static bool ReturnsFreshList(string? memberId) => memberId is GetObjectsId or GetTilesId;

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
            bool getTiles = memberId == GetTilesId;
            if (!getCell && !getObjects && !getTile && !getTiles)
                return false;
            // The grid indexes only committed placements.
            ctx.client.CommitScriptWritesForGrid();
            string receiverId = NSGetterEvaluator.FindRowIdByReference(receiver, ctx)
                ?? (receiver as INeoValueReference)?.valueId
                ?? throw new NSGetterRuntimeError("Grid query receiver has no placement identity.");
            PlacementBinding binding = Resolve(receiverId);
            INeoTileGridContent content = binding.content!;
            NeoObjectPlacementRecord placement = binding.record!;
            ctx.client.NoteGridRead(content, placement.InstanceId, null, false);
            if (getCell)
            {
                result = NeoVectorValues.FromVector2Int(placement.Cell);
                return true;
            }
            NeoCellPattern pattern = NeoCellPatternStorage.ReadRuntime(args[0], ctx);
            // Bodies compiled before the offset existed pass only the pattern.
            Vector2Int origin = Shifted(placement.Cell, args.Length > 1 ? args[1] : null);
            bool tiles = getTile || getTiles;
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
            }
            // A query that threw mid-fill leaves its entries behind.
            queried.Clear();
            for (int offset = 0; offset < pattern.Count; offset++)
            {
                Vector2Int cell = pattern.CellAt(origin, offset);
                ctx.client.NoteGridRead(content, placement.InstanceId, cell, tiles);
                if (tiles)
                {
                    var tile = content.GetTile(cell);
                    if (tile is null)
                        continue;
                    if (getTiles)
                    {
                        queried.Add(RuntimeValue(tile, ctx));
                        continue;
                    }
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
                        queried.Add(RuntimeValue(item.InstanceId, ref item.valueNode, ctx));
                    }
                }
            }
            if (getTile)
                return true;
            object?[] list = NSGetterEvaluator.TemporaryLists.Rent(queried.Count);
            queried.CopyTo(list);
            queried.Clear();
            ctx.NoteFreshList(list);
            NSGetterEvaluator.TemporaryLists.MarkExclusive(list);
            result = list;
            return true;
        }

        /// <summary>The placement cell moved by the query's optional offset; null is no shift.</summary>
        private static Vector2Int Shifted(Vector2Int cell, object? offset)
        {
            if (offset is null)
                return cell;
            Vector2Int shift = NeoGeneratedTypesSupport.ReadVector2IntValue(offset)
                ?? throw new NSGetterRuntimeError("Grid query offset must be a Vector2Int.");
            return new Vector2Int(checked(cell.x + shift.x), checked(cell.y + shift.y));
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
