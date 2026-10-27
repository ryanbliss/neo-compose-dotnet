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
        internal NeoPlacementOutcome TryPlaceLayerEntry(
            NeoGeneratedClassValue link, Vector2Int cell, NeoGeneratedClassValue entry, bool tile)
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
                PrepareConstructedPlacement(plan, list, entry, localCell, tile);
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
            list.PrepareAddSerialized(plan, NeoValueWritePayload.FromValueReference(id, entry));
        }

        // Direct grid links already store grid cells. Carried links project
        // through their owning object's rounded cell and composition offsets.
        internal (string? gridId, Vector2Int origin) LayerLinkPlacementOrigin(NeoGeneratedClassValue link)
        {
            string current = link.valueId!;
            var seen = new HashSet<string>();
            var owners = new List<ObjectMemberValue>();
            while (seen.Add(current))
            {
                if (ResolveValueRow(current) is ObjectMemberValue row)
                {
                    if (HasWorldKind(row.classId, "tileGrid"))
                    {
                        if (owners.Count <= 1)
                            return (current, Vector2Int.zero);
                        // The final owner is the direct grid link. The one
                        // before it is the placed object that carries this link.
                        ObjectMemberValue placed = owners[owners.Count - 2];
                        (double x, double y) offset = (0, 0);
                        for (int i = 0; i < owners.Count - 2; i++)
                        {
                            var child = PlacementPosition(owners[i]);
                            offset = (offset.x + child.x, offset.y + child.y);
                        }
                        var position = PlacementPosition(placed);
                        return (current, NeoReadOnlyTileGridPrimitive.CheckedPlacementCell(
                            NeoReadOnlyTileGridPrimitive.RoundedPlacementCell(position.x, position.y), NeoReadOnlyTileGridPrimitive.RoundedPlacementCell(offset.x, offset.y)));
                    }
                    owners.Add(row);
                }
                string? parent = PlacementParent(current);
                if (parent is null)
                    return (null, Vector2Int.zero);
                current = parent;
            }
            throw PlacementError("placement-containment-cycle", $"Layer link '{link.valueId}' has cyclic ownership.");
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

        private (double x, double y) PlacementPosition(ObjectMemberValue row) =>
            ResolveClassChildRow(row, "Position") is Vector3MemberValue { value: not null } position
                ? (position.value.x, position.value.y) : (0, 0);

        private Vector2Int PlacementCell(ObjectMemberValue row) =>
            ResolveClassChildRow(row, "Cell") is Vector2MemberValue { value: not null } cell
                ? NeoReadOnlyTileGridPrimitive.RoundedPlacementCell(cell.value.x, cell.value.y) : Vector2Int.zero;

        private void ValidateDetachedLayerLink(string linkId, bool tile)
        {
            ObjectMemberValue link = RequirePlacementClass(linkId, tile ? "tileLayerLink" : "objectLayerLink");
            ArrayMemberValue? list = ValidatePlacementCollectionField(link, tile ? "Tiles" : "Objects");
            if (list is null)
                return;
            var occupied = new Dictionary<Vector2Int, string>();
            foreach (string id in PlacementListEntries(list.id))
            {
                ObjectMemberValue row = RequirePlacementClass(id, tile ? "tile" : "object");
                if (tile)
                {
                    ValidateTileRow(id);
                    Reserve(PlacementCell(row), id);
                }
                else
                {
                    ValidateObjectPosition(row);
                    ValidateObjectFootprint(row);
                    var position = PlacementPosition(row);
                    Vector2Int origin = NeoReadOnlyTileGridPrimitive.RoundedPlacementCell(position.x, position.y);
                    if (ValidatePlacementCollectionField(row, "PlacementTiles") is not ArrayMemberValue footprint)
                        continue;
                    foreach (string footprintId in PlacementListEntries(footprint.id))
                        Reserve(NeoReadOnlyTileGridPrimitive.CheckedPlacementCell(origin, PlacementCell(RequirePlacementClass(footprintId, null))), id);
                }
            }
            void Reserve(Vector2Int cell, string id)
            {
                if (occupied.TryGetValue(cell, out string previous) && previous != id)
                    throw PlacementError(tile ? "tile-cell-occupied" : "tile-grid-object-cell-occupied",
                        $"Layer link '{linkId}' has placements '{previous}' and '{id}' at {cell}.");
                occupied[cell] = id;
            }
        }
    }
}
