// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        internal NeoPlacementOutcome TrySpawnChild(NeoGeneratedClassValue parent, Vector2Int cell, NeoGeneratedClassValue child)
        {
            if (!ReferenceEquals(parent.Client, this) || !ReferenceEquals(child.Client, this))
                throw new ArgumentException("Parent and child must belong to the same client.");
            if (!HasWorldKind(parent.classId, "object") || !HasWorldKind(child.classId, "objectBase"))
                return NeoPlacementOutcome.Error("placement-class-invalid", "TrySpawn requires a NeoObject parent and a NeoObjectBase child.");
            try
            {
                var collection = NeoGeneratedTypesSupport.AsWritable(parent.BackingNode, parent.StorageOwnership)
                    .Get<NeoMemberList>("Children");
                if (collection is not NeoMemberListWritable list || list.ownership == NeoValueOwnership.Asset)
                    throw new InvalidOperationException("Cannot spawn into immutable Children.");
                AssertConstructedValueAdoptable(list.ownership, child);
                var (gridId, origin) = WorldPlacementOrigin(parent.valueId!);
                long x = (long)cell.x - origin.x;
                long y = (long)cell.y - origin.y;
                if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue)
                    return NeoPlacementOutcome.Error("object-position-invalid", "Child position exceeds the integer cell range.");
                var plan = new NeoWritePlan(this) { PlacementGridId = gridId };
                PrepareConstructedObjectPlacement(plan, list, child, cell, parent.valueId!);
                using (ReadCandidate(plan))
                {
                    var root = (ObjectMemberValue)ResolveValueRow(parent.valueId!)!;
                    ValidateCompositionChildren(root, new HashSet<string>());
                    if (gridId is null)
                        ValidateDetachedWorldContent(root);
                }
                plan.Commit();
                return NeoPlacementOutcome.Success();
            }
            catch (NeoPlacementValidationException error)
            {
                return NeoPlacementOutcome.Error(error.ErrorCode, error.Message);
            }
        }

        internal NeoPlacementOutcome TryPlaceLayerEntry(
            NeoGeneratedClassValue link, Vector2Int cell, NeoGeneratedClassValue entry, bool tile,
            NeoValueOwnership? tileOverlayOwnership = null)
        {
            if (!ReferenceEquals(link.Client, this) || !ReferenceEquals(entry.Client, this))
                throw new ArgumentException("The layer link and placement must belong to the same client.");
            string linkId = link.valueId ?? throw new InvalidOperationException("Layer link has no backing row.");
            if (!HasWorldKind(link.classId, tile ? "tileLayerLink" : "objectLayerLink"))
                throw new ArgumentException("Placement receiver has the wrong layer link kind.", nameof(link));
            if (!HasWorldKind(entry.classId, tile ? "tile" : "object"))
                return NeoPlacementOutcome.Error("placement-class-invalid", tile ? "TrySet requires a NeoTile." : "TrySpawn requires a placeable NeoObject.");
            try
            {
                string layerId = NeoWorldLayerLinkResolver.ResolveTargetLayerClassId(this, linkId, link.classId!, tile);
                string relation = tile ? InternalRecordRelationKinds.WorldTileCompatibleLayer : InternalRecordRelationKinds.WorldObjectCompatibleLayer;
                bool compatible = false;
                foreach (string target in InternalRecordRelations.ResolveTargetIds(relation, entry.classId!))
                    if (target == layerId)
                    {
                        compatible = true;
                        break;
                    }
                if (!compatible)
                    return NeoPlacementOutcome.Error("placement-layer-incompatible", $"Class '{entry.classId}' is not compatible with layer '{layerId}'.");
                string collectionKey = tile ? "Tiles" : "Objects";
                var collection = NeoGeneratedTypesSupport.AsWritable(link.BackingNode, link.StorageOwnership)
                    .Get<NeoMemberList>(collectionKey);
                if (collection.ownership == NeoValueOwnership.Asset && tile && tileOverlayOwnership is NeoValueOwnership.Save or NeoValueOwnership.Session)
                    return TryPlaceConstructedTileOverlay(link, collection, entry, cell, layerId, tileOverlayOwnership.Value);
                if (collection.ownership == NeoValueOwnership.Asset)
                    throw new InvalidOperationException($"Cannot place content into immutable layer-link collection '{collectionKey}'.");
                if (collection is not NeoMemberListWritable list)
                    throw new InvalidOperationException($"Layer-link collection '{collectionKey}' has no writable storage view.");
                AssertConstructedValueAdoptable(list.ownership, entry);
                (string? gridId, Vector2Int origin) = LayerLinkPlacementOrigin(link);
                long localX = (long)cell.x - origin.x;
                long localY = (long)cell.y - origin.y;
                if (localX < int.MinValue || localX > int.MaxValue || localY < int.MinValue || localY > int.MaxValue)
                    return NeoPlacementOutcome.Error("tile-cell-invalid", "Placement cell exceeds the supported integer range.");
                var localCell = new Vector2Int((int)localX, (int)localY);
                NeoTileGridPrimitive? grid = gridId is null ? null
                    : list.ownership == NeoValueOwnership.Session
                        ? NeoTileGridPrimitive.ResolveForSession(this, gridId)
                        : NeoTileGridPrimitive.ResolveForSave(this, gridId);
                if (!tile && grid is not null)
                {
                    NeoPlacementOutcome? conflict = grid.ObjectPlacementConflict(layerId, cell, entry.classId!, entry.valueId);
                    if (conflict is not null)
                        return conflict;
                }
                var plan = new NeoWritePlan(this) { PlacementGridId = gridId };
                if (tile)
                {
                    foreach (string id in list.ResolveEntryValueIds())
                        if (ResolveValueRow(id) is ObjectMemberValue existing && PlacementCell(existing) == localCell)
                            list.PrepareRemoveUnorderedEntry(plan, id);
                }
                if (tile)
                    PrepareConstructedPlacement(plan, list, entry, localCell, tile: true);
                else
                    PrepareConstructedObjectPlacement(plan, list, entry, cell, linkId);
                if (!tile && gridId is not null)
                {
                    using (ReadCandidate(plan))
                    {
                        var owner = (ObjectMemberValue)ResolveValueRow(linkId)!;
                        var candidateList = (ArrayMemberValue)ResolveClassChildRow(owner, "Objects")!;
                        plan.ObjectInsertion = (gridId, layerId, candidateList.id, entry.valueId!);
                    }
                }
                if (gridId is null)
                    using (ReadCandidate(plan))
                        ValidateDetachedLayerLink(linkId, tile);
                if (grid is not null)
                {
                    if (grid.Lifecycle is not null)
                    {
                        using (ReadCandidate(plan))
                            ValidateWritePlan(plan);
                        grid.BeforeLinkPlacement(linkId, layerId, cell, entry, tile);
                    }
                }
                plan.Commit();
                return NeoPlacementOutcome.Success();
            }
            catch (NeoPlacementValidationException error)
            {
                return NeoPlacementOutcome.Error(error.ErrorCode, error.Message);
            }
        }

        // The grid and link entry points stage the position and adoption in one
        // plan, so a rejected placement does not move or consume its argument.
        internal void PrepareConstructedPlacement(
            NeoWritePlan plan, NeoMemberListWritable list, NeoGeneratedClassValue entry,
            Vector2Int cell, bool tile)
        {
            string id = PreparePlacementCoordinate(plan, entry, (cell.x, cell.y), tile);
            list.PrepareAddSerialized(plan, NeoValueWritePayload.FromValueReference(id, entry));
        }

        internal void PrepareConstructedObjectPlacement(
            NeoWritePlan plan, NeoMemberListWritable list, NeoGeneratedClassValue entry,
            Vector2Int gridCell, string ownerId)
        {
            var offset = WorldPlacementOffset(ownerId).offset;
            string id = PreparePlacementCoordinate(plan, entry,
                (gridCell.x - offset.x, gridCell.y - offset.y), tile: false);
            list.PrepareAddSerialized(plan, NeoValueWritePayload.FromValueReference(id, entry));
        }

        private string PreparePlacementCoordinate(NeoWritePlan plan, NeoGeneratedClassValue entry, (double x, double y) cell, bool tile)
        {
            string id = entry.valueId ?? throw new InvalidOperationException("Placement has no backing row.");
            NeoValueOwnership ownership = entry.ValueOwnership;
            if (!TryGetValue(ownership, id, out ObjectMemberValue? source))
                throw new InvalidOperationException($"Constructed placement '{id}' has no class row.");
            var row = (ObjectMemberValue)CloneRowForWrite(source);
            row.value ??= new Dictionary<string, string>();
            string key = tile ? "Cell" : "Position";
            MemberValue? prior = ResolveClassChildRow(source, key, ownership);
            MemberValue coordinate;
            if (tile)
            {
                var next = prior is Vector2MemberValue vector
                    ? (Vector2MemberValue)CloneRowForWrite(vector)
                    : new Vector2MemberValue { createdAt = NeoTimestamp.Now() };
                next.value = new NeoVector2Value { x = cell.x, y = cell.y };
                coordinate = next;
            }
            else
            {
                var next = prior is Vector3MemberValue vector
                    ? (Vector3MemberValue)CloneRowForWrite(vector)
                    : new Vector3MemberValue { createdAt = NeoTimestamp.Now() };
                next.value = new NeoVector3Value { x = cell.x, y = cell.y, z = (prior as Vector3MemberValue)?.value?.z ?? 0 };
                coordinate = next;
            }
            if (prior is null || !source.value!.TryGetValue(key, out string ownedCoordinateId) || ownedCoordinateId != prior.id)
                coordinate.id = Guid.NewGuid().ToString();
            coordinate.updatedAt = NeoTimestamp.Now();
            coordinate.mapKey = source.mapKey;
            row.value[key] = coordinate.id;
            plan.Set(ownership, coordinate);
            plan.Set(ownership, row);
            return id;
        }

        private NeoPlacementOutcome TryPlaceConstructedTileOverlay(
            NeoGeneratedClassValue link, NeoMemberList collection, NeoGeneratedClassValue entry,
            Vector2Int cell, string layerId, NeoValueOwnership ownership)
        {
            AssertConstructedValueAdoptable(ownership, entry);
            var (gridId, origin) = LayerLinkPlacementOrigin(link);
            if (gridId is null)
                throw new InvalidOperationException("Tile overlays require an owning grid.");
            var grid = ownership == NeoValueOwnership.Session
                ? NeoTileGridPrimitive.ResolveForSession(this, gridId)
                : NeoTileGridPrimitive.ResolveForSave(this, gridId);
            long localX = (long)cell.x - origin.x, localY = (long)cell.y - origin.y;
            if (localX < int.MinValue || localX > int.MaxValue || localY < int.MinValue || localY > int.MaxValue)
                return NeoPlacementOutcome.Error("tile-cell-invalid", "Placement cell exceeds the supported integer range.");
            var local = new Vector2Int((int)localX, (int)localY);
            var plan = new NeoWritePlan(this) { PlacementGridId = gridId };
            foreach (var existing in grid.LookupCache.TileRecords(layerId))
                if (existing.SourceTileLayerLinkId == link.valueId && existing.Cell == cell)
                {
                    string existingId = existing.PlacementValueId;
                    StageOwnedRemoval(plan, ownership, existingId, collection.EntryMember,
                        values.ContainsKey(existingId) || !plan.TryGetWritable(ownership, existingId, out _));
                }
            string id = PreparePlacementCoordinate(plan, entry, (local.x, local.y), true);
            string importedId = ImportValueReference(plan, ownership, id, out bool moved);
            var row = CloneRowForWrite(plan.Resolve(importedId)!);
            row.containerId = collection.value!.id;
            plan.Set(ownership, row);
            if (moved)
            {
                var payload = NeoValueWritePayload.FromValueReference(id, entry);
                plan.AfterCommit(() => payload.RetargetMovedReference(this, collection.EntryMember, importedId, ownership));
            }
            using (ReadCandidate(plan))
                ValidateWritePlan(plan);
            grid.BeforeLinkPlacement(link.valueId!, layerId, cell, entry, true);
            plan.Commit();
            return NeoPlacementOutcome.Success();
        }

        // Direct grid links already store grid cells. Carried links project
        // through their owning object's rounded cell and composition offsets.
        internal (string? gridId, Vector2Int origin) LayerLinkPlacementOrigin(NeoGeneratedClassValue link)
            => WorldPlacementOrigin(link.valueId!);

        internal (string? gridId, Vector2Int origin) WorldPlacementOrigin(string valueId)
        {
            var (gridId, offset) = WorldPlacementOffset(valueId);
            return (gridId, NeoReadOnlyTileGridPrimitive.RoundedPlacementCell(offset.x, offset.y));
        }

        internal (string? gridId, (double x, double y) offset) WorldPlacementOffset(string valueId)
        {
            string current = valueId;
            var seen = new HashSet<string>();
            var owners = new List<ObjectMemberValue>();
            while (seen.Add(current))
            {
                if (ResolveValueRow(current) is ObjectMemberValue row)
                {
                    NoteRowRead(row.id);
                    string? classId = row.classId;
                    if (classId is null && TryInferMemberForValueId(row.id, out Member? member)
                        && member is ClassMember placing && placing.TryGetDeclaredClassId(out string declaredClassId))
                        classId = declaredClassId;
                    if (HasWorldKind(classId, "tileGrid"))
                    {
                        (double x, double y) offset = (0, 0);
                        foreach (var owner in owners)
                        {
                            var position = PlacementPosition(owner);
                            offset = (offset.x + position.x, offset.y + position.y);
                        }
                        return (current, offset);
                    }
                    owners.Add(row);
                }
                string? parent = PlacementParent(current);
                if (parent is null)
                    return (null, (0, 0));
                current = parent;
            }
            throw PlacementError("placement-containment-cycle", $"World value '{valueId}' has cyclic ownership.");
        }

        private string? PlacementParent(string id)
        {
            if (ResolveValueRow(id) is { containerId: not null } child && !string.IsNullOrEmpty(child.containerId))
                return child.containerId;
            if (TryResolveVirtualPlacement(id, out var virtualPlacement))
                return virtualPlacement.parentValueId;
            string? found = null;
            foreach (string candidate in PlacementParents(id))
            {
                MemberValue? parent = ResolveValueRow(candidate);
                bool owns = parent is ObjectMemberValue record && record.value?.ContainsValue(id) == true
                    || parent is ArrayMemberValue array && array.value is not null && Array.IndexOf(array.value, id) >= 0;
                if (!owns)
                    continue;
                if (found is not null && found != candidate)
                    throw PlacementError("placement-owner-ambiguous", $"Placement '{id}' has multiple owning parents.");
                found = candidate;
            }
            return found;
        }

        private (double x, double y) PlacementPosition(ObjectMemberValue row)
        {
            if (ResolveClassChildRow(row, "Position") is not Vector3MemberValue { value: not null } position)
                return (0, 0);
            NoteRowRead(position.id);
            return (position.value.x, position.value.y);
        }

        private Vector2Int PlacementCell(ObjectMemberValue row) =>
            ResolveClassChildRow(row, "Cell") is Vector2MemberValue { value: not null } cell
                ? NeoReadOnlyTileGridPrimitive.RoundedPlacementCell(cell.value.x, cell.value.y) : Vector2Int.zero;

        private void ValidateDetachedLayerLink(string linkId, bool tile)
        {
            ObjectMemberValue link = RequirePlacementClass(linkId, tile ? "tileLayerLink" : "objectLayerLink");
            ValidateDetachedWorldContent(link);
        }

        private void ValidateDetachedWorldContent(ObjectMemberValue root)
        {
            var primitive = NeoReadOnlyTileGridPrimitive.Resolve(this, root.id);
            var graph = primitive.BuildDetachedPlacementGraph(root);
            var occupied = new Dictionary<(string layer, Vector2Int cell), string>();
            foreach (var node in graph.Ordered)
            {
                if (node.PlacementLayerId is string layer)
                {
                    RequirePlacementClass(node.Row.id, "object");
                    ValidateObjectPosition(node.Row);
                    ValidateObjectFootprint(node.Row);
                    RequireCompatible(node.Row, layer, false);
                    foreach (var cell in primitive.ReadObjectFootprint(node.Row, node.Cell, null))
                    {
                        var key = (layer, cell);
                        if (occupied.TryGetValue(key, out string previous) && previous != node.Row.id)
                            throw PlacementError("tile-grid-object-cell-occupied", $"Detached content has objects '{previous}' and '{node.Row.id}' at {cell} in layer '{layer}'.");
                        occupied[key] = node.Row.id;
                    }
                }
                if (node.Link is not { IsTileLink: true } tileLink)
                    continue;
                var tiles = new HashSet<Vector2Int>();
                foreach (string id in PlacementListEntries(tileLink.ListValueId))
                {
                    var tile = RequirePlacementClass(id, "tile");
                    ValidateTileRow(id);
                    RequireCompatible(tile, tileLink.LayerId, true);
                    var cell = NeoReadOnlyTileGridPrimitive.CheckedPlacementCell(node.Cell, PlacementCell(tile));
                    if (!tiles.Add(cell))
                        throw PlacementError("tile-cell-occupied", $"Tile link '{tileLink.LinkValueId}' has duplicate cells at {cell}.");
                }
            }

            void RequireCompatible(ObjectMemberValue row, string layer, bool tile)
            {
                string relation = tile ? InternalRecordRelationKinds.WorldTileCompatibleLayer : InternalRecordRelationKinds.WorldObjectCompatibleLayer;
                foreach (string target in InternalRecordRelations.ResolveTargetIds(relation, row.classId!))
                    if (target == layer)
                        return;
                throw PlacementError("placement-layer-incompatible", $"Class '{row.classId}' is not compatible with layer '{layer}'.");
            }
        }
    }
}
