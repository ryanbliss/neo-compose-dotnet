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
        private readonly Dictionary<(string grid, string layer, NeoValueOwnership ownership), object> layerViews = new();
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
            layerViews.Clear();
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
            if (NeoWorldLayerRuntime.Handles(memberId) || memberId == NeoLayerLinkRuntime.TrySpawnChildId)
            {
                var context = client.CreateGetterContext((receiver as NeoGeneratedClassValue)?.StorageOwnership ?? NeoValueOwnership.Asset);
                if (NeoWorldLayerRuntime.TryInvoke(memberId, receiver, args, typeArguments, context, out object? native)
                    || NeoLayerLinkRuntime.TryInvoke(memberId, receiver, args, context, out native))
                    return native;
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

        public TLayer Layer<TLayer>(INeoValueReference link, string layerId) where TLayer : class
        {
            string id = link.valueId ?? throw new NSGetterRuntimeError("Layer link has no identity.");
            string gridId = client.WorldPlacementOrigin(id).gridId
                ?? throw new NSGetterRuntimeError("A detached layer link has no owning grid Layer.");
            var content = ResolveContent(gridId);
            var ownership = (link as NeoGeneratedClassValue)?.StorageOwnership ?? NeoValueOwnership.Asset;
            var key = (gridId, layerId, ownership);
            if (layerViews.TryGetValue(key, out var cached))
                return (TLayer)cached;
            foreach (var layer in content.TileLayersInOrder)
                if (layer.LayerId == layerId && layer is TLayer tiles && layer is NeoGeneratedTileLayerValue tileView)
                {
                    var result = ownership == NeoValueOwnership.Asset ? tiles
                        : content.Primitive.BindReadOnlyTileLayer<TLayer>(layerId, tileView.ImportedClassIds, ownership);
                    layerViews[key] = result;
                    return result;
                }
            foreach (var layer in content.ObjectLayersInOrder)
                if (layer.LayerId == layerId && layer is TLayer objects && layer is NeoGeneratedObjectLayerValue objectView)
                {
                    var result = ownership == NeoValueOwnership.Asset ? objects
                        : content.Primitive.BindReadOnlyObjectLayer<TLayer>(layerId, objectView.ImportedClassIds, ownership);
                    layerViews[key] = result;
                    return result;
                }
            throw new NSGetterRuntimeError($"Grid has no generated layer '{layerId}'.");
        }

        private const string ObjectGridCellId = "system_a7c3197e-e672-5f58-a82e-39db2d864a78";
        private const string TileGridCellId = "system_223292cc-c600-57b6-a927-69bee647d82a";
        private const string GetCellId = "system_df1c2d06-eeec-5340-addc-740f3668c9e4";
        private const string GetObjectsId = "system_f5ca386c-990c-54a1-8473-2d49d2cd887d";
        private const string GetTileId = "system_593e6208-e2ca-505e-9933-04b17102b6d2";
        private const string GetTilesId = "system_5a41d36a-bcd9-4867-9e46-d743f6e09582";
        private const string TileGetObjectsId = "system_bf87a1b7-b1ff-48dc-bbf0-286bd2739a9b";
        private const string TileGetTileId = "system_2c92a799-5733-418c-af70-31d7fe22956f";
        private const string TileGetTilesId = "system_7aac6739-41ad-4d7f-88d0-15b5f0416e67";
        private const string NeoObjectClassId = "system_f671535d-0eb6-4139-9caf-5c5276febd31";
        private const string NeoTileClassId = "system_d7fa161b-b269-4bf1-9ec9-3aebec67e6e0";
        private const string GetObjectAreaId = "system_7ad477e2-be5b-4892-b9ba-2a2f2476988d";
        private const string GetTileAreaId = "system_a92b0911-8e7e-48ef-a12e-f3c55c33f64b";
        private const string NeoTileGridClassId = "system_b78f7f1d-f63c-4abb-ae65-852e69246534";
        // P114 §3. Far past any world tree; ends a corrupt cycle without a visited set.
        private const int MaxAreaDepth = 256;

        /// <summary>Whether <paramref name="memberId"/> is a grid query this class answers.</summary>
        internal static bool Handles(string memberId) => memberId is
            ObjectGridCellId or TileGridCellId or GetCellId or GetObjectsId or GetTileId or GetTilesId or GetObjectAreaId or GetTileAreaId
            or TileGetObjectsId or TileGetTileId or TileGetTilesId;

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

        internal static bool ReturnsFreshList(string? memberId) => false;

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
            if (memberId == GetCellId)
            {
                var row = client.ResolveValueRow(receiverId) as ObjectMemberValue
                    ?? throw new NSGetterRuntimeError("Cell requires a world object.");
                client.NoteRowRead(row.id);
                var positionRow = client.ResolveClassChildRow(row, "Position") as Vector3MemberValue;
                if (positionRow is not null)
                    client.NoteRowRead(positionRow.id);
                var position = positionRow?.value;
                result = NeoVectorValues.FromVector2Int(NeoReadOnlyTileGridPrimitive.RoundedPlacementCell(position?.x ?? 0, position?.y ?? 0));
                return true;
            }
            if (memberId is ObjectGridCellId or TileGridCellId)
            {
                var (gridId, worldOrigin) = client.WorldPlacementOrigin(receiverId);
                if (gridId is not null)
                {
                    if (memberId == TileGridCellId && client.ResolveValueRow(receiverId) is ObjectMemberValue tile
                        && client.ResolveClassChildRow(tile, "Cell") is Vector2MemberValue { value: not null } local)
                    {
                        client.NoteRowRead(local.id);
                        worldOrigin = Cell(worldOrigin, NeoReadOnlyTileGridPrimitive.RoundedPlacementCell(local.value.x, local.value.y));
                    }
                    result = NeoVectorValues.FromVector2Int(worldOrigin);
                }
                return true;
            }
            bool getObjects = memberId is GetObjectsId or TileGetObjectsId;
            bool getTile = memberId is GetTileId or TileGetTileId;
            bool getTiles = memberId is GetTilesId or TileGetTilesId;
            // The grid indexes only committed placements.
            ctx.client.CommitScriptWritesForGrid();
            bool tileReceiver = memberId is TileGetObjectsId or TileGetTileId or TileGetTilesId;
            PlacementBinding? binding = null;
            INeoTileGridContent content;
            Vector2Int receiverCell;
            string placementId;
            if (tileReceiver)
            {
                (content, receiverCell, placementId) = ResolveTile(receiverId);
            }
            else
            {
                binding = client.ReadsDepartedRows(receiverId) && departedPlacements.ContainsKey(receiverId)
                    ? Resolve(receiverId) : TryResolve(receiverId);
                if (binding is not null)
                {
                    content = binding.content!;
                    receiverCell = binding.record!.Cell;
                    placementId = binding.record.InstanceId;
                }
                else
                {
                    var (gridId, worldOrigin) = client.WorldPlacementOrigin(receiverId);
                    content = ResolveContent(gridId ?? throw new NSGetterRuntimeError("Grid queries require an owning tile grid."));
                    receiverCell = worldOrigin;
                    placementId = receiverId;
                }
            }
            Vector2Int[] pattern = NeoCellPatternOffsets.Read(args[0], ctx);
            string? filterClassId = FilterClassId(typeArguments);
            // Only the origin is historical. Index rebuilds and target values must
            // read the surviving rows, even inside a departure hook.
            using var liveRows = client.ReadLiveRows();
            if (binding is not null && departedPlacements.TryGetValue(receiverId, out var departed)
                && ReferenceEquals(binding, departed)
                && (client.IsDeparted(binding.grid) || client.IsWorldPartitionUnloaded(binding.grid)))
            {
                // Accessing the primitive here could reload an unloaded partition
                // or resolve a replacement grid with the same id.
                if (!getTile)
                {
                    result = new NeoWorldQuerySequence(client,
                        new ClassTypeInfo { type = MemberKind.Class, required = true, classId = getTiles ? NeoTileClassId : NeoObjectClassId },
                        Array.Empty<string>());
                }
                return true;
            }
            ctx.client.NoteGridRead(content, placementId, null, false);
            // Bodies compiled before the offset existed pass only the pattern.
            Vector2Int origin = Shifted(receiverCell, args.Length > 1 ? args[1] : null);
            bool tiles = getTile || getTiles;
            var ids = QueryRows(content, placementId, origin, pattern, tiles);
            var element = typeArguments is { Length: > 0 } ? typeArguments[0]
                : new ClassTypeInfo { type = MemberKind.Class, required = true, classId = tiles ? NeoTileClassId : NeoObjectClassId };
            if (getTile)
            {
                foreach (string id in ids)
                {
                    object? tile = RuntimeValue(id, ctx);
                    if (filterClassId is null || NSGetterEvaluator.RuntimeClassExtends(tile, filterClassId, ctx))
                    {
                        result = tile;
                        break;
                    }
                }
            }
            else
                result = new NeoWorldQuerySequence(client, element, ids);
            return true;
        }

        private IEnumerable<string> QueryRows(INeoTileGridContent content, string receiverId,
            Vector2Int origin, Vector2Int[] pattern, bool tiles)
        {
            foreach (var offset in pattern)
            {
                var cell = Cell(origin, offset);
                client.NoteGridRead(content, receiverId, cell, tiles);
                if (tiles)
                {
                    if (content.GetTile(cell)?.valueId is string tileId)
                        yield return tileId;
                    continue;
                }
                foreach (var layer in content.ObjectLayersInOrder)
                    foreach (var row in content.Primitive.LookupCache.ObjectCandidatesAt(layer.LayerId, cell))
                        yield return row.InstanceId;
            }
        }

        private (INeoTileGridContent content, Vector2Int cell, string placementId) ResolveTile(string receiverId)
        {
            string gridId = AreaOf(receiverId)
                ?? throw new NSGetterRuntimeError("Grid queries require a NeoTile in an owning grid.");
            var content = ResolveContent(gridId);
            if (client.ResolveValueRow(receiverId) is not ObjectMemberValue tile
                || !client.HasWorldKind(tile.classId, "tile"))
                throw new NSGetterRuntimeError($"Value '{receiverId}' is not a tile.");
            var origin = client.WorldPlacementOrigin(receiverId).origin;
            if (client.ResolveClassChildRow(tile, "Cell") is not Vector2MemberValue { value: not null } local)
                throw new NSGetterRuntimeError($"Tile '{receiverId}' has no local Cell.");
            client.NoteRowRead(local.id);
            var cell = Cell(origin, NeoReadOnlyTileGridPrimitive.RoundedPlacementCell(local.value.x, local.value.y));
            client.NoteGridRead(content, receiverId, cell, true);
            return (content, cell, receiverId);
        }

        internal INeoTileGridContent ResolveContent(string gridId)
        {
            if (!contentByGrid.TryGetValue(gridId, out var content))
            {
                MemberValue grid = client.ResolveValueRow(gridId)
                    ?? throw new NSGetterRuntimeError($"Grid '{gridId}' has no stored value.");
                string classId = ClassIdOf(gridId, grid)
                    ?? throw new NSGetterRuntimeError($"Grid '{gridId}' has no class.");
                if (!factories.TryGetValue(classId, out var factory))
                    throw new NSGetterRuntimeError($"Grid '{gridId}' has no registered content factory.");
                content = factory(client, gridId);
                RegisterContent(content);
            }
            return content;
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
