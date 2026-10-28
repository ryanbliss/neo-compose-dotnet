// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using UnityEngine;

namespace NeoCompose.Runtime
{
    internal static class NeoWorldLayerRuntime
    {
        private static readonly Dictionary<string, (bool tile, bool link, string operation)> Methods = new()
        {
            ["system_e1453818-2e4d-5ec3-b7ee-a2994507e539"] = (true, false, "TrySet"),
            ["system_4087fd9c-ceb1-5616-8779-6c4d7f379bfc"] = (true, false, "GetCount"),
            ["system_7afcfb2d-dccb-5398-b431-865c1457eaba"] = (true, false, "Contains"),
            ["system_e30a8a13-9a4a-5d1a-bbd8-988cf42eb3ae"] = (true, false, "IndexOf"),
            ["system_c9a358ad-96ee-5c93-8321-73d5a546e0fe"] = (true, false, "Remove"),
            ["system_3ecba6cc-9e6c-5b5d-aa5a-1de26eedaa6e"] = (true, false, "RemoveAt"),
            ["system_f77c4cbb-2df3-5350-b0bc-9bf99ade5082"] = (true, false, "Clear"),
            ["system_47765255-929f-5bc3-bb80-c02d4d7470bd"] = (true, false, "GetEnumerator"),
            ["system_d9d97231-c764-5824-b9b0-3c449743180e"] = (true, false, "GetTileCell"),
            ["system_92bac216-5060-5043-90fa-6666cc37f868"] = (true, false, "GetTileId"),
            ["system_0e919dfb-35f1-5620-a170-c8a341c7c4ff"] = (true, false, "GetTilesPattern"),
            ["system_d8484b9c-4463-504e-a9e6-f20a0ebcd9d0"] = (true, true, "GetTileCell"),
            ["system_ed52c435-3d4a-555c-bc63-9db796f471fe"] = (true, true, "GetTileId"),
            ["system_87bc5708-4c9f-5263-91c2-c83cb76f9357"] = (true, true, "GetTilesPattern"),
            ["system_a147a969-b23e-58a8-b351-bdbde968ac00"] = (false, false, "TrySpawn"),
            ["system_e2575ec5-04b2-5f6b-8c8e-0ce04387308c"] = (false, false, "GetCount"),
            ["system_cd0cbb0e-8baf-5c93-884f-6921b1e5be23"] = (false, false, "Contains"),
            ["system_1be7077b-1cb3-51c3-8a76-b77fd3c5cc7c"] = (false, false, "IndexOf"),
            ["system_6d61ff59-58dc-5c14-9a0d-e36aa8d1885a"] = (false, false, "Remove"),
            ["system_529499bf-265c-5e98-b474-bee69567a1f3"] = (false, false, "RemoveAt"),
            ["system_0a1422c9-62b4-59d9-aafc-dadabfc710d3"] = (false, false, "Clear"),
            ["system_3c9d3213-1ded-5ade-9e86-a8a698fadbaa"] = (false, false, "GetEnumerator"),
            ["system_1362a56e-3d3d-56e6-b115-24a78f216b1b"] = (false, false, "GetObjectCell"),
            ["system_d0cba2de-82ee-56b9-8b47-61ebc1e6153e"] = (false, false, "GetObjectId"),
            ["system_2efd48d3-ab2e-52e6-a6ed-8a67513ea63d"] = (false, false, "GetObjectsCell"),
            ["system_0b9f7ba6-987a-597d-8fd3-f7604b91ff36"] = (false, false, "GetObjectsPattern"),
            ["system_855ebeef-50d6-563a-9518-56472de73ce4"] = (false, true, "GetObjectCell"),
            ["system_536e03f8-d177-5fbb-8391-112706dc761e"] = (false, true, "GetObjectId"),
            ["system_cce85e15-bede-5e77-8463-63805445eeeb"] = (false, true, "GetObjectsCell"),
            ["system_a2a601b0-7811-514c-896c-472db86fe38e"] = (false, true, "GetObjectsPattern"),
        };

        internal sealed class LayerView : NeoGeneratedClassValue
        {
            internal readonly NeoReadOnlyTileGridPrimitive Grid;
            internal readonly string LayerId;
            internal LayerView(NeoReadOnlyTileGridPrimitive grid, string layerId)
                : base(grid.Client, new NeoMemberClass(grid.Client,
                    NeoReadOnlyTileGridPrimitive.CreateClassDefaultMember(layerId, $"__world_layer:{grid.GridValueId}:{layerId}"), null), layerId)
            {
                Grid = grid;
                LayerId = layerId;
                MarkClassDefaultReference();
            }
        }

        internal static object ResolveLayer(object? receiver, string layerId, string source, NSGetterEvaluator.Context ctx)
        {
            ctx.client.CommitScriptWritesForGrid();
            var value = NeoLayerLinkRuntime.View(receiver, ctx);
            string gridId;
            if (source == "link")
            {
                bool tile = ctx.client.HasWorldKind(value.classId, "tileLayerLink");
                string target = NeoWorldLayerLinkResolver.ResolveTargetLayerClassId(ctx.client, value.valueId!, value.classId!, tile);
                if (target != layerId)
                    throw new NSGetterRuntimeError("Layer access does not match the link's declared relation.");
                gridId = ctx.client.WorldPlacementOrigin(value.valueId!).gridId
                    ?? throw new NSGetterRuntimeError("A detached layer link has no owning grid Layer.");
            }
            else if (source == "grid" && ctx.client.HasWorldKind(value.classId, "tileGrid"))
                gridId = value.valueId!;
            else
                throw new NSGetterRuntimeError("Content requires a NeoTileGrid receiver.");
            bool tileLayer = ctx.client.HasWorldKind(layerId, "tileLayer");
            string relation = tileLayer ? InternalRecordRelationKinds.WorldGridTileLayer : InternalRecordRelationKinds.WorldGridObjectLayer;
            if (ctx.client.ResolveValueRow(gridId) is not ObjectMemberValue gridRow)
                throw new NSGetterRuntimeError($"Grid '{gridId}' has no stored object value.");
            string gridClass = NeoGeneratedTypesSupport.ResolveClassValueClassId(ctx.client, gridId, gridRow)
                ?? throw new NSGetterRuntimeError($"Grid '{gridId}' has no class.");
            if (!ctx.client.InternalRecordRelations.ResolveTargetIds(relation, gridClass).Contains(layerId))
                throw new NSGetterRuntimeError($"Grid '{gridClass}' has no declared layer '{layerId}'.");
            var content = ctx.client.ScriptGridQueries.ResolveContent(gridId);
            foreach (var layer in content.TileLayersInOrder)
                if (layer.LayerId == layerId && layer is NeoGeneratedClassValue generatedTileLayer)
                    return generatedTileLayer;
            foreach (var layer in content.ObjectLayersInOrder)
                if (layer.LayerId == layerId && layer is NeoGeneratedClassValue generatedObjectLayer)
                    return generatedObjectLayer;
            return new LayerView(content.Primitive, layerId);
        }

        internal static bool Handles(string memberId) => Methods.ContainsKey(memberId);

        internal static bool TryInvoke(string memberId, object? receiver, object?[] args, TypeInfo[]? types,
            NSGetterEvaluator.Context ctx, out object? result)
        {
            result = null;
            if (!Methods.TryGetValue(memberId, out var method))
                return false;
            ctx.client.CommitScriptWritesForGrid();
            var scope = new Scope(receiver, method.tile, method.link, ctx);
            string op = method.operation;
            TypeInfo element = types is { Length: > 0 } ? types[0] : new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = method.tile ? "system_d7fa161b-b269-4bf1-9ec9-3aebec67e6e0" : "system_61b30a92-90dc-4bf8-8503-ee4f6414effc"
            };
            IEnumerable<string> rows;
            if (op is "TrySpawn" or "TrySet")
            {
                var cell = Cell(args[0]);
                var entry = NeoLayerLinkRuntime.View(args[1], ctx);
                var grid = scope.Grid!;
                result = NeoLayerLinkRuntime.ConstructResult(grid.TryPlaceConstructedLayerEntry(scope.LayerId, cell, entry, method.tile,
                    ctx.valueOwnership == NeoValueOwnership.Session ? NeoValueOwnership.Session : NeoValueOwnership.Save), ctx);
                return true;
            }
            if (op.EndsWith("Pattern", StringComparison.Ordinal))
            {
                var pattern = NeoCellPatternOffsets.Read(args[0], ctx);
                rows = scope.Pattern(Cell(args[1]), pattern);
            }
            else if (op.EndsWith("Cell", StringComparison.Ordinal))
                rows = scope.At(Cell(args[0]));
            else if (op.EndsWith("Id", StringComparison.Ordinal))
                rows = scope.ById(args[0] as string);
            else
                rows = scope.All();
            var sequence = new NeoWorldQuerySequence(ctx.client, element, rows);
            if (op.StartsWith("GetObjects", StringComparison.Ordinal) || op.StartsWith("GetTiles", StringComparison.Ordinal))
                result = sequence;
            else if (op == "GetEnumerator")
                result = sequence.GetEnumerator(ctx);
            else if (op.StartsWith("GetObject", StringComparison.Ordinal) || op.StartsWith("GetTile", StringComparison.Ordinal))
            {
                var cursor = sequence.GetEnumerator(ctx);
                try
                {
                    result = cursor.MoveNext(ctx) ? cursor.Current(ctx) : null;
                }
                finally { cursor.Dispose(ctx); }
            }
            else if (op == "GetCount")
                result = (long)rows.Count();
            else if (op is "Contains" or "IndexOf" or "Remove")
            {
                string? id = NSGetterEvaluator.FindRowIdByReference(args[0], ctx) ?? (args[0] as INeoValueReference)?.valueId;
                int index = 0;
                bool found = false;
                foreach (string candidate in rows)
                {
                    if (candidate == id)
                    {
                        found = true;
                        break;
                    }
                    index++;
                }
                if (op == "Remove" && found)
                    scope.Remove(id!, ctx);
                result = op == "IndexOf" ? (object)(long)(found ? index : -1) : found;
            }
            else if (op == "RemoveAt")
            {
                int index = checked(Convert.ToInt32(args[0]));
                if (index < 0)
                    throw new NSGetterRuntimeError("Layer index must be nonnegative.");
                string? id = rows.Skip(index).FirstOrDefault();
                if (id is null)
                    throw new NSGetterRuntimeError("Layer index is out of range.");
                scope.Remove(id, ctx);
            }
            else if (op == "Clear")
                scope.Clear(ctx);
            return true;
        }

        private static Vector2Int Cell(object? value) => NeoGeneratedTypesSupport.ReadVector2IntValue(value)
            ?? throw new NSGetterRuntimeError("A layer cell must be a Vector2Int.");

        private sealed class Scope
        {
            private readonly NeoClient client;
            private readonly bool tile;
            private readonly NeoGeneratedClassValue? link;
            internal readonly NeoReadOnlyTileGridPrimitive? Grid;
            internal readonly string LayerId;

            internal Scope(object? receiver, bool tile, bool directLink, NSGetterEvaluator.Context ctx)
            {
                client = ctx.client;
                this.tile = tile;
                if (directLink)
                {
                    link = NeoLayerLinkRuntime.View(receiver, ctx);
                    LayerId = NeoWorldLayerLinkResolver.ResolveTargetLayerClassId(client, link.valueId!, link.classId!, tile);
                    var placement = client.WorldPlacementOrigin(link.valueId!);
                    Grid = placement.gridId is null ? null : NeoReadOnlyTileGridPrimitive.Resolve(client, placement.gridId);
                }
                else if (receiver is LayerView view)
                {
                    Grid = view.Grid;
                    LayerId = view.LayerId;
                }
                else if (receiver is NeoGeneratedTileLayerValue tiles)
                {
                    Grid = tiles.BoundPrimitive;
                    LayerId = tiles.LayerId;
                }
                else if (receiver is NeoGeneratedObjectLayerValue objects)
                {
                    Grid = objects.BoundPrimitive;
                    LayerId = objects.LayerId;
                }
                else
                    throw new NSGetterRuntimeError("Layer APIs require a layer bound to a grid.");
            }

            internal IEnumerable<string> All()
            {
                if (link is not null)
                {
                    var list = link.BackingNode.Get<NeoMemberList>(tile ? "Tiles" : "Objects");
                    client.NoteRowRead(link.valueId!);
                    if (list.value is { } listRow)
                        client.NoteRowRead(listRow.id);
                    foreach (string id in list.ResolveEntryValueIds())
                        yield return id;
                }
                else if (tile)
                {
                    NoteMembership();
                    foreach (var candidates in Grid!.LookupCache.TileCandidatesInCellOrder(LayerId))
                        if (candidates.Count != 0)
                            yield return candidates[candidates.Count - 1].PlacementValueId;
                }
                else
                {
                    NoteMembership();
                    foreach (var row in Grid!.LookupCache.ObjectRecords(LayerId))
                        yield return row.InstanceId;
                }
            }

            private void NoteMembership()
            {
                client.NoteGridLayerRead(client.ScriptGridQueries.ResolveContent(Grid!.GridValueId), LayerId, tile);
            }

            internal IEnumerable<string> At(Vector2Int cell)
            {
                if (Grid is not null)
                {
                    client.NoteGridRead(client.ScriptGridQueries.ResolveContent(Grid.GridValueId), link?.valueId ?? Grid.GridValueId, cell, tile);
                    if (tile)
                    {
                        var candidates = Grid.LookupCache.TileCandidatesAt(LayerId, cell);
                        for (int i = candidates.Count - 1; i >= 0; i--)
                            if (link is null || candidates[i].SourceTileLayerLinkId == link.valueId)
                            {
                                yield return candidates[i].PlacementValueId;
                                break;
                            }
                    }
                    else
                        foreach (var row in Grid.LookupCache.ObjectCandidatesAt(LayerId, cell))
                            if (link is null || row.SourceLayerLinkId == link.valueId)
                                yield return row.InstanceId;
                    yield break;
                }
                var primitive = NeoReadOnlyTileGridPrimitive.Resolve(client, link!.valueId!);
                foreach (string id in All())
                {
                    if (client.ResolveValueRow(id) is not ObjectMemberValue row)
                        continue;
                    client.NoteRowRead(id);
                    if (tile)
                    {
                        if (client.ResolveClassChildRow(row, "Cell") is Vector2MemberValue { value: not null } local)
                        {
                            client.NoteRowRead(local.id);
                            if (NeoReadOnlyTileGridPrimitive.RoundedPlacementCell(local.value.x, local.value.y) == cell)
                                yield return id;
                        }
                    }
                    else
                    {
                        var dependencies = new HashSet<string>();
                        bool found = primitive.ReadObjectFootprint(row, primitive.ReadObjectOrigin(row, dependencies), dependencies).Contains(cell);
                        foreach (string dependency in dependencies)
                            client.NoteRowRead(dependency);
                        if (found)
                            yield return id;
                    }
                }
            }

            internal IEnumerable<string> Pattern(Vector2Int cell, Vector2Int[] offsets)
            {
                foreach (var offset in offsets)
                    foreach (string id in At(NeoReadOnlyTileGridPrimitive.CheckedPlacementCell(cell, offset)))
                        yield return id;
            }

            internal IEnumerable<string> ById(string? instanceId)
            {
                if (instanceId is null)
                    yield break;
                if (tile && Grid is not null)
                {
                    NoteMembership();
                    var record = Grid.LookupCache.TileInstanceRecord(LayerId, instanceId);
                    if (record is not null && (link is null || record.SourceTileLayerLinkId == link.valueId))
                        yield return record.PlacementValueId;
                    yield break;
                }
                if (!tile && Grid is not null)
                {
                    NoteMembership();
                    var record = Grid.LookupCache.ObjectRecord(LayerId, instanceId);
                    if (record is not null && (link is null || record.SourceLayerLinkId == link.valueId))
                        yield return record.InstanceId;
                    yield break;
                }
                foreach (string id in All())
                    if (id == instanceId)
                    {
                        yield return id;
                        yield break;
                    }
            }

            internal IEnumerable<string> Stored()
            {
                if (link is not null || !tile)
                {
                    foreach (string id in All())
                        yield return id;
                    yield break;
                }
                NoteMembership();
                foreach (var record in Grid!.LookupCache.TileRecords(LayerId))
                    yield return record.PlacementValueId;
            }

            internal void Clear(NSGetterEvaluator.Context context)
            {
                var plan = new NeoWritePlan(client);
                foreach (string id in Stored())
                    PrepareRemove(plan, id, context);
                plan.Commit();
            }

            internal void Remove(string id, NSGetterEvaluator.Context context)
            {
                var plan = new NeoWritePlan(client);
                PrepareRemove(plan, id, context);
                plan.Commit();
            }

            private void PrepareRemove(NeoWritePlan plan, string id, NSGetterEvaluator.Context context)
            {
                if (plan.Resolve(id) is not { IsRemoved: false })
                    return;
                string? sourceLink = tile
                    ? Grid!.LookupCache.TileRecord(LayerId, id)?.SourceTileLayerLinkId
                    : Grid!.LookupCache.ObjectRecord(LayerId, id)?.SourceLayerLinkId;
                if (sourceLink is null)
                    throw new NSGetterRuntimeError("Layer entry has no owning link.");
                var owner = NeoLayerLinkRuntime.View(NSGetterEvaluator.EvaluatePointer(new ReferencePointer { type = PointerKind.Reference, valueId = sourceLink }, new NeoScriptScope(0), context), context);
                var list = NeoGeneratedTypesSupport.AsWritable(owner.BackingNode, owner.StorageOwnership).Get<NeoMemberList>(tile ? "Tiles" : "Objects");
                if (tile && list.ownership == NeoValueOwnership.Asset)
                {
                    var ownership = context.valueOwnership == NeoValueOwnership.Session ? NeoValueOwnership.Session : NeoValueOwnership.Save;
                    if (context.valueOwnership == NeoValueOwnership.Asset && Grid is NeoTileGridPrimitive mutable)
                        ownership = mutable.WriteOwnership;
                    bool tombstone = client.values.ContainsKey(id) || !plan.TryGetWritable(ownership, id, out _);
                    client.StageOwnedRemoval(plan, ownership, id, list.EntryMember, tombstone);
                    return;
                }
                if (list is not NeoMemberListWritable writable || list.ownership == NeoValueOwnership.Asset)
                    throw new NSGetterRuntimeError("Layer content is immutable.");
                writable.PrepareRemoveUnorderedEntry(plan, id);
            }

        }
    }
}
