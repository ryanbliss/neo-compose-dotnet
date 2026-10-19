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
        private readonly Dictionary<string, PlacementBinding> departedPlacements = new();

        internal void CaptureDeparture(string receiverId)
        {
            using var liveRows = client.ReadLiveRows();
            if (TryResolve(receiverId) is not { } binding)
                return;
            // Keep the immutable placement record, not the mutable live binding.
            departedPlacements[receiverId] = new PlacementBinding(binding.grid, binding.layer, binding.instance)
            {
                content = binding.content,
                record = binding.record,
            };
        }

        internal void ReleaseDepartures()
        {
            foreach (string id in departedPlacements.Keys)
                placements.Remove(id);
            departedPlacements.Clear();
            lastReceiverId = null;
            lastBinding = null;
        }
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
            if (client.ReadsDepartedRows(receiverId) && departedPlacements.TryGetValue(receiverId, out var departed))
                return departed;
            return TryResolve(receiverId)
                ?? throw new NSGetterRuntimeError("Grid queries require an actual placed NeoObject in an owning grid.");
        }

        private PlacementBinding? TryResolve(string receiverId)
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
                if (!client.TryGetGridQueryParent(current, out string? parent, out _))
                    break;
                current = parent;
            }
            return null;
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

        /// <param name="typeArguments">A generic query's type arguments, from <see cref="NeoTypeArguments.Of{T}"/>.</param>
        public object? Invoke(string memberId, INeoValueReference receiver, object?[] args, TypeInfo[]? typeArguments = null)
        {
            // A pattern view reads as itself, so a pending one makes no rows
            // and a no-op hands back the same view.
            if (NeoCellPatternRuntime.Handles(memberId))
            {
                NeoCellPatternRuntime.TryInvoke(memberId, receiver, args, client.CreateGetterContext(NeoValueOwnership.Session), out object? pattern);
                return pattern;
            }
            if (receiver.valueId is not string id || !client.TryGetValue(id, out MemberValue? row))
            {
                // P114 §2. A value with no row is in no area.
                if (memberId is GetObjectAreaId or GetTileAreaId)
                    return null;
                throw new NSGetterRuntimeError("Grid query receiver has no placement identity.");
            }
            var ownership = client.TryGetValueOwnership(id, out NeoValueOwnership found) ? found : NeoValueOwnership.Asset;
            var ctx = client.CreateGetterContext(ownership);
            object? value = NSGetterEvaluator.UnwrapRow(row, ctx, ownership);
            if (!TryInvoke(memberId, value, args, typeArguments, ctx, out object? result))
                throw new NSGetterRuntimeError("Unknown grid query member.");
            return result;
        }

        private const string GetCellId = "system_df1c2d06-eeec-5340-addc-740f3668c9e4";
        private const string GetObjectsId = "system_f5ca386c-990c-54a1-8473-2d49d2cd887d";
        private const string GetTileId = "system_593e6208-e2ca-505e-9933-04b17102b6d2";
        private const string GetTilesId = "system_5a41d36a-bcd9-4867-9e46-d743f6e09582";
        private const string NeoObjectClassId = "system_f671535d-0eb6-4139-9caf-5c5276febd31";
        private const string NeoTileClassId = "system_d7fa161b-b269-4bf1-9ec9-3aebec67e6e0";
        private const string GetObjectAreaId = "system_7ad477e2-be5b-4892-b9ba-2a2f2476988d";
        private const string GetTileAreaId = "system_a92b0911-8e7e-48ef-a12e-f3c55c33f64b";
        private const string NeoTileGridClassId = "system_b78f7f1d-f63c-4abb-ae65-852e69246534";
        // P114 §3. Far past any world tree; ends a corrupt cycle without a visited set.
        private const int MaxAreaDepth = 256;

        /// <summary>Whether <paramref name="memberId"/> is a grid query this class answers.</summary>
        internal static bool Handles(string memberId) => memberId is
            GetCellId or GetObjectsId or GetTileId or GetTilesId or GetObjectAreaId or GetTileAreaId;

        /// <summary>
        /// P110 §6. The class a generic query keeps, or null to keep every
        /// winner: the declared base keeps today's scan.
        /// </summary>
        private static string? FilterClassId(TypeInfo[]? typeArguments)
        {
            if (typeArguments is null || typeArguments.Length == 0)
                return null;
            if (typeArguments[0] is not ClassTypeInfo classType)
            {
                throw new NSGetterRuntimeError(
                    $"Grid query type argument must be a class, not member kind {typeArguments[0].type}.");
            }
            return classType.classId is NeoObjectClassId or NeoTileClassId or NeoTileGridClassId ? null : classType.classId;
        }

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

        internal bool TryInvoke(
            string memberId,
            object? receiver,
            object?[] args,
            TypeInfo[]? typeArguments,
            NSGetterEvaluator.Context ctx,
            out object? result)
        {
            result = null;
            if (!Handles(memberId))
                return false;
            string? receiverId = NSGetterEvaluator.FindRowIdByReference(receiver, ctx)
                ?? (receiver as INeoValueReference)?.valueId;
            if (memberId is GetObjectAreaId or GetTileAreaId)
            {
                // The climb reads rows, not the grid's indexes, so pending writes stay pending.
                string? areaId = receiverId is null ? null : AreaOf(receiverId);
                result = areaId is null ? null : RuntimeValue(areaId, ctx);
                if (FilterClassId(typeArguments) is string areaClassId && !NSGetterEvaluator.RuntimeClassExtends(result, areaClassId, ctx))
                    result = null;
                return true;
            }
            if (receiverId is null)
                throw new NSGetterRuntimeError("Grid query receiver has no placement identity.");
            bool getCell = memberId == GetCellId;
            bool getObjects = memberId == GetObjectsId;
            bool getTile = memberId == GetTileId;
            bool getTiles = memberId == GetTilesId;
            // The grid indexes only committed placements.
            ctx.client.CommitScriptWritesForGrid();
            PlacementBinding binding = Resolve(receiverId);
            INeoTileGridContent content = binding.content!;
            NeoObjectPlacementRecord placement = binding.record!;
            if (getCell)
            {
                ctx.client.NoteGridRead(content, placement.InstanceId, null, false);
                result = NeoVectorValues.FromVector2Int(placement.Cell);
                return true;
            }
            Vector2Int[] pattern = NeoCellPatternOffsets.Read(args[0], ctx);
            string? filterClassId = FilterClassId(typeArguments);
            // Only the origin is historical. Index rebuilds and target values must
            // read the surviving rows, even inside a departure hook.
            using var liveRows = client.ReadLiveRows();
            if (departedPlacements.TryGetValue(receiverId, out var departed)
                && ReferenceEquals(binding, departed)
                && (client.IsDeparted(binding.grid) || client.IsWorldPartitionUnloaded(binding.grid)))
            {
                // Accessing the primitive here could reload an unloaded partition
                // or resolve a replacement grid with the same id.
                if (!getTile)
                {
                    object?[] empty = NSGetterEvaluator.TemporaryLists.Rent(0);
                    NSGetterEvaluator.TemporaryLists.MarkExclusive(empty);
                    result = empty;
                }
                return true;
            }
            ctx.client.NoteGridRead(content, placement.InstanceId, null, false);
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
            for (int offset = 0; offset < pattern.Length; offset++)
            {
                Vector2Int cell = Cell(origin, pattern[offset]);
                ctx.client.NoteGridRead(content, placement.InstanceId, cell, tiles);
                if (tiles)
                {
                    var tile = content.GetTile(cell);
                    if (tile is null)
                        continue;
                    // A cell's winner is kept or skipped; nothing under it is a candidate.
                    object? winner = RuntimeValue(tile, ctx);
                    if (filterClassId is not null && !NSGetterEvaluator.RuntimeClassExtends(winner, filterClassId, ctx))
                        continue;
                    if (getTiles)
                    {
                        queried.Add(winner);
                        continue;
                    }
                    result = winner;
                    return true;
                }
                // NeoScript already consumes stored-row views. Do not create
                // generated C# wrappers and intermediate lists for every cell.
                for (int layer = 0; layer < layers.Count; layer++)
                {
                    // An empty layer answers no cell; skip its lookup and
                    // the write barrier its out parameter costs.
                    Dictionary<Vector2Int, List<NeoObjectPlacementRecord>> cells = layerCells![layer];
                    if (cells.Count == 0 || !cells.TryGetValue(cell, out var items))
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
                        object? found = RuntimeValue(item.InstanceId, ref item.valueNode, ctx);
                        if (filterClassId is null || NSGetterEvaluator.RuntimeClassExtends(found, filterClassId, ctx))
                            queried.Add(found);
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

        /// <summary>
        /// P114 §3. The nearest tile grid that owns <paramref name="valueId"/>,
        /// or null. Notes the receiver and every owner it reaches except an
        /// unordered list: an entry's stamp is its edge, and reading the list
        /// would drop the getter whenever its layer spawns or despawns.
        /// </summary>
        private string? AreaOf(string valueId)
        {
            client.NoteRowRead(valueId);
            string current = valueId;
            bool stamped = false;
            for (int depth = 0; depth < MaxAreaDepth; depth++)
            {
                // A stamp names an unordered list, which is never a grid.
                if (!stamped)
                {
                    if (!client.TryGetValue(current, out MemberValue? row))
                        return null;
                    if (row is not ArrayMemberValue && client.HasWorldKind(ClassIdOf(current, row), "tileGrid"))
                        return current;
                }
                if (!client.TryGetGridQueryParent(current, out string? parent, out stamped))
                    return null;
                if (!stamped)
                    client.NoteRowRead(parent);
                current = parent;
            }
            return null;
        }

        /// <summary>A row's class: its stamp, else its placing member's (P75 virtual rows).</summary>
        private string? ClassIdOf(string id, MemberValue row) =>
            row.classId ?? (client.TryInferMemberForValueId(id, out Member? member)
                && member is ClassMember placing
                && placing.TryGetDeclaredClassId(out string declaredClassId)
                ? declaredClassId
                : null);

        /// <summary>The placement cell moved by the query's optional offset; null is zero.</summary>
        private static Vector2Int Shifted(Vector2Int cell, object? offset)
        {
            if (offset is null)
                return cell;
            Vector2Int shift = NeoGeneratedTypesSupport.ReadVector2IntValue(offset)
                ?? throw new NSGetterRuntimeError("Grid query offset must be a Vector2Int.");
            return Cell(cell, shift);
        }

        private static Vector2Int Cell(Vector2Int origin, Vector2Int offset)
        {
            long x = (long)origin.x + offset.x;
            long y = (long)origin.y + offset.y;
            if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
                throw new NSGetterRuntimeError("World cell coordinates must fit a signed 32-bit integer.");
            return new Vector2Int((int)x, (int)y);
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
