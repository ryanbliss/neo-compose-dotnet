// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private Dictionary<string, HashSet<string>>? writablePlacementParents;
        private readonly Dictionary<(NeoValueOwnership ownership, string id), HashSet<string>> writablePlacementChildren = new();

        internal static IEnumerable<string> PlacementChildIds(MemberValue row)
        {
            if (row is ObjectMemberValue { value: not null } obj)
                foreach (string child in obj.value.Values) yield return child;
            else if (row is ArrayMemberValue { value: not null } array)
                foreach (string child in array.value) yield return child;
            if (row is ObjectMemberValue { constructorArgs: not null } constructed)
                foreach (var token in constructed.constructorArgs.Values)
                    if (token?.Type == Newtonsoft.Json.Linq.JTokenType.String && (string?)token is string child) yield return child;
        }

        private void IndexPlacementParent(NeoValueOwnership ownership, MemberValue row)
        {
            if (writablePlacementParents is null) return;
            UnindexPlacementParent(ownership, row.id);
            var children = new HashSet<string>(PlacementChildIds(row));
            writablePlacementChildren[(ownership, row.id)] = children;
            foreach (string child in children)
            {
                if (!writablePlacementParents.TryGetValue(child, out var parents))
                    writablePlacementParents[child] = parents = new HashSet<string>();
                parents.Add(row.id);
            }
        }

        private void UnindexPlacementParent(NeoValueOwnership ownership, string id)
        {
            if (writablePlacementParents is null
                || !writablePlacementChildren.TryGetValue((ownership, id), out var children)) return;
            writablePlacementChildren.Remove((ownership, id));
            NeoValueOwnership other = ownership == NeoValueOwnership.Save ? NeoValueOwnership.Session : NeoValueOwnership.Save;
            foreach (string child in children)
            {
                if (writablePlacementChildren.TryGetValue((other, id), out var retained) && retained.Contains(child)) continue;
                if (writablePlacementParents.TryGetValue(child, out var parents)
                    && parents.Remove(id)
                    && parents.Count == 0)
                    writablePlacementParents.Remove(child);
            }
        }

        private IEnumerable<string> PlacementParents(string childId)
        {
            if (writablePlacementParents is null)
            {
                writablePlacementParents = new Dictionary<string, HashSet<string>>();
                foreach (MemberValue row in saveData.values.Values) IndexPlacementParent(NeoValueOwnership.Save, row);
                foreach (MemberValue row in sessionData.values.Values) IndexPlacementParent(NeoValueOwnership.Session, row);
            }
            if (writablePlacementParents.TryGetValue(childId, out var writable))
                foreach (string parent in writable) yield return parent;
            if (ValueInferenceIndex.Parents.TryGetValue(childId, out var authored))
                foreach (var parent in authored) yield return parent.Key;
            if (TryResolveVirtualPlacement(childId, out var placement))
                yield return placement.parentValueId;
        }

        internal bool HasWorldKind(string? classId, string kind)
        {
            if (string.IsNullOrEmpty(classId)) return false;
            foreach (NeoSchemaClass type in ResolveClassInheritanceChain(classId!))
                if (type.system?["worldKind"]?.ToString() == kind) return true;
            return false;
        }

        private static readonly Unity.Profiling.ProfilerMarker ValidatePlacementsMarker = new("NeoCompose.Write.ValidatePlacements");
        private void ValidateWritePlan(NeoWritePlan plan)
        {
            using var sample = ValidatePlacementsMarker.Auto();
            if (plan.Rows.Count == 0 && plan.Bindings.Count == 0) return;
            bool runtimeLeaves = IsRuntimeLeafWrite(plan);
            var positionObjects = new HashSet<string>();
            var stagedParents = new Dictionary<string, HashSet<string>>();
            var pending = new Queue<string>();
            var writtenDescendants = new HashSet<string>(plan.Rows.Keys.Select(key => key.id));
            foreach (var pair in plan.Rows)
            {
                pending.Enqueue(pair.Key.id);
                if (pair.Value is null) continue;
                if (!string.IsNullOrEmpty(pair.Value.containerId)) pending.Enqueue(pair.Value.containerId!);
                foreach (string child in PlacementChildIds(pair.Value))
                {
                    if (!stagedParents.TryGetValue(child, out var parents))
                        stagedParents[child] = parents = new HashSet<string>();
                    parents.Add(pair.Key.id);
                }
            }
            foreach (var binding in plan.Bindings.Values)
                if (binding.valueId is not null) pending.Enqueue(binding.valueId);
            if (candidateReplay is not null)
                foreach (var pair in candidateReplay.Values)
                    if (!virtualValues.TryGetValue(pair.Key, out var previousVirtual)
                        || !NeoSemanticJson.MemberRowsEqual(previousVirtual, pair.Value))
                        pending.Enqueue(pair.Key);
            var visited = new HashSet<string>();
            var grids = new HashSet<string>();
            var tiles = new HashSet<string>();
            var objects = new HashSet<string>();
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                if (!visited.Add(id)) continue;
                MemberValue? candidate = plan.Resolve(id);
                TryGetCommittedValue(id, out MemberValue? previous);
                if (HasWorldKind(candidate?.classId ?? previous?.classId, "tileGrid")) grids.Add(id);
                if (HasWorldKind(candidate?.classId, "tile")) tiles.Add(id);
                if (HasWorldKind(candidate?.classId, "object")) objects.Add(id);
                if (!string.IsNullOrEmpty(candidate?.containerId)) pending.Enqueue(candidate!.containerId!);
                if (!string.IsNullOrEmpty(previous?.containerId)) pending.Enqueue(previous!.containerId!);
                foreach (string parent in PlacementParents(id))
                {
                    if (!IsPlacementEdge(plan, parent, id)) continue;
                    if (writtenDescendants.Contains(id)) writtenDescendants.Add(parent);
                    if (runtimeLeaves && plan.Rows.Keys.Any(key => key.id == id)
                        && plan.Resolve(parent) is ObjectMemberValue positionOwner
                        && HasWorldKind(positionOwner.classId, "object")
                        && ResolveClassChildRow(positionOwner, "Position")?.id == id)
                        positionObjects.Add(parent);
                    if (writtenDescendants.Contains(id) && plan.Resolve(parent) is ObjectMemberValue owner && HasWorldKind(owner.classId, "tile"))
                    {
                        CheckFields(owner.value);
                        if (TryResolveVirtualClassChildren(parent, out var defaults)) CheckFields(defaults);
                        void CheckFields(Dictionary<string, string>? fields)
                        {
                            if (fields is null) return;
                            foreach (var field in fields)
                                if (field.Key != "Cell" && field.Value == id)
                                    throw PlacementError("tile-instance-member", $"Tile '{parent}' cannot write instance member '{field.Key}'; only Cell is writable.");
                        }
                    }
                    pending.Enqueue(parent);
                }
                if (stagedParents.TryGetValue(id, out var proposed))
                    foreach (string parent in proposed)
                        if (IsPlacementEdge(plan, parent, id)) pending.Enqueue(parent);
            }
            if (runtimeLeaves && tiles.Count == 0)
            {
                var positions = new Dictionary<string, Vector2Int>();
                using (ReadCandidate(plan))
                    foreach (string id in positionObjects)
                    {
                        var owner = (ObjectMemberValue)ResolveValueRow(id)!;
                        if (ResolveClassChildRow(owner, "Position") is not Vector3MemberValue { value: not null } position
                            || !Finite(position.value.x) || !Finite(position.value.y) || !Finite(position.value.z))
                            throw PlacementError("object-position-invalid", $"Object '{id}' requires a finite Position.");
                        // Use the same cell conversion as the normal placement builder.
                        if (grids.Count != 0)
                            positions[id] = NeoReadOnlyTileGridPrimitive.Resolve(this, grids.First())
                                .ReadObjectOrigin(owner, null);
                    }
                if (positions.Count != 0)
                    foreach (string gridId in grids)
                        GetGridLookupCache(gridId).PrepareObjectMoves(plan, positions);
                plan.HasValidatedRuntimeLeaves = true;
                return;
            }
            // These builders read rows and declarations only. Do not resolve
            // generated wrappers or populate persistent layer caches here.
            var primitives = new Dictionary<string, NeoReadOnlyTileGridPrimitive>();
            foreach (string gridId in grids) primitives[gridId] = NeoReadOnlyTileGridPrimitive.Resolve(this, gridId);
            using (ReadCandidate(plan))
            {
                var compatibleLayers = new Dictionary<(bool tile, string classId), HashSet<string>>();
                foreach (string objectId in objects)
                    if (ResolveValueRow(objectId) is ObjectMemberValue obj && !obj.IsRemoved) ValidateObjectFootprint(obj);
                if (TryValidateObjectInsertion(plan, primitives, compatibleLayers)) return;
                if (TryValidateDirectTileConversions(plan, primitives, compatibleLayers)) return;
                foreach (string tileId in tiles) ValidateTileRow(tileId);
                foreach (string gridId in grids) ValidateGridPlacements(plan, gridId, primitives[gridId], compatibleLayers);
            }
        }

        // The parent index also contains lookup selections and constructor
        // arguments. Those are references, not containment: editing a catalog
        // or config row must not walk every world object that references it.
        // Replayed outputs are validated separately above.
        private bool IsPlacementEdge(NeoWritePlan plan, string parentId, string childId)
        {
            MemberValue? next = plan.Resolve(parentId);
            TryGetCommittedValue(parentId, out MemberValue? previous);
            if (HasWorldKind(next?.classId ?? previous?.classId, "object"))
                return GeometryChild(next as ObjectMemberValue) || GeometryChild(previous as ObjectMemberValue);
            if (TryResolveVirtualPlacement(childId, out var placement)
                && placement.parentValueId == parentId) return true;
            return Owns(next) || Owns(previous);

            bool GeometryChild(ObjectMemberValue? row)
            {
                if (row is null) return false;
                return ResolveClassChildRow(row, "Position")?.id == childId
                    || ResolveClassChildRow(row, "PlacementTiles")?.id == childId;
            }

            bool Owns(MemberValue? row)
            {
                if (row is ObjectMemberValue obj) return obj.value?.ContainsValue(childId) == true;
                return row is ArrayMemberValue array && array.value is not null
                    && Array.IndexOf(array.value, childId) >= 0
                    && TryInferMemberForValueId(parentId, out Member? member) && member is ListMember;
            }
        }

        // Only value replacements with unchanged graph edges qualify. Cell edits,
        // collection edits and constructor replay retain structural validation.
        private bool IsRuntimeLeafWrite(NeoWritePlan plan)
        {
            if (candidateReplay is not null || plan.Bindings.Count != 0 || plan.Rows.Count == 0) return false;
            foreach (var pair in plan.Rows)
            {
                MemberValue? next = pair.Value;
                if (next is null || next.IsRemoved
                    || !TryGetCommittedOverlaidValue(pair.Key.ownership, pair.Key.id, out MemberValue? previous)
                    || previous.IsRemoved || next.GetType() != previous.GetType()
                    || next.classId != previous.classId || next.containerId != previous.containerId
                    || next.mapKey != previous.mapKey || next.sourceValueId != previous.sourceValueId
                    || next.hasInstanceConstructorId || next.constructorArgs is not null
                    || next.instanceVariantId is not null || next.instanceVariantRowValueId is not null)
                    return false;
                if (next is NumberMemberValue or StringMemberValue or BoolMemberValue
                    or Vector3MemberValue or ColorMemberValue or FileMemberValue or SpriteMemberValue) continue;
                if (next is ArrayMemberValue && TryInferMemberForValueId(next.id, out Member? member)
                    && member is EnumMember or LookupMember or DialogueLookupMember) continue;
                return false;
            }
            return true;
        }

        private void ValidateTileRow(string tileId)
        {
            if (ResolveValueRow(tileId) is not ObjectMemberValue tile || tile.IsRemoved) return;
            if (!TryGetClass(tile.classId!, out NeoSchemaClass? type)
                || type.Modifier == NeoClassModifierKind.Abstract)
                throw PlacementError("tile-class-abstract", $"Tile '{tileId}' must have a concrete class.");
            // Virtual rows include class defaults for reading. Only a physical
            // instance row stores overrides; defaults are not instance writes.
            bool stored = data.values.ContainsKey(tileId)
                || (candidateReadPlan is null
                    ? sessionData.values.ContainsKey(tileId) || saveData.values.ContainsKey(tileId)
                    : candidateReadPlan.TryGetWritable(NeoValueOwnership.Session, tileId, out MemberValue? _)
                        || candidateReadPlan.TryGetWritable(NeoValueOwnership.Save, tileId, out MemberValue? _));
            if (stored && tile.value is not null)
                foreach (string key in tile.value.Keys)
                    if (key != "Cell")
                        throw PlacementError("tile-instance-member", $"Tile '{tileId}' cannot store instance member '{key}'; only Cell is writable.");
            ValidatePlacementCell(tile);
        }

        private void ValidatePlacementCell(ObjectMemberValue tile)
        {
            string tileId = tile.id;
            MemberValue? cell = ResolveClassChildRow(tile, "Cell");
            NeoVector2Value? point = (cell as Vector2MemberValue)?.value;
            if (cell is null && tile.value?.ContainsKey("Cell") != true)
            {
                foreach (var field in ResolveStoredInstanceSchema(tile.classId!))
                    if (field.schemaKey == "Cell" && TryGetMember(field.memberId, out Member? declaration)
                        && declaration is Vector2IntMember vectorDeclaration)
                    {
                        point = vectorDeclaration.defaultValue?.value;
                        break;
                    }
            }
            if (point is null || !Finite(point.x) || !Finite(point.y)
                || point.x < int.MinValue || point.x > int.MaxValue
                || point.y < int.MinValue || point.y > int.MaxValue
                || point.x != Math.Truncate(point.x)
                || point.y != Math.Truncate(point.y))
                throw PlacementError("tile-cell-invalid", $"Tile '{tileId}' requires an integer Cell.");
        }

        private void ValidateGridPlacements(
            NeoWritePlan plan, string gridId, NeoReadOnlyTileGridPrimitive primitive,
            Dictionary<(bool tile, string classId), HashSet<string>> compatibleLayers)
        {
            if (ResolveValueRow(gridId) is not ObjectMemberValue { classId: not null } grid || grid.IsRemoved) return;
            if (ValidatePlacementCollectionField(grid, "Children") is ArrayMemberValue children)
                foreach (string childId in PlacementListEntries(children.id))
                {
                    ObjectMemberValue link = RequirePlacementClass(childId, null);
                    if (HasWorldKind(link.classId, "tileLayerLink")) ValidatePlacementCollectionField(link, "Tiles");
                    if (HasWorldKind(link.classId, "objectLayerLink")) ValidatePlacementCollectionField(link, "Objects");
                }
            foreach (NeoGridLayerLinkModel link in primitive.ResolveGridLinks(null))
            {
                foreach (string entryId in PlacementListEntries(link.ListValueId))
                {
                    ObjectMemberValue entry = RequirePlacementClass(entryId, link.IsTileLink ? "tile" : "object");
                    if (link.IsTileLink) ValidateTileRow(entryId);
                    else
                    {
                        if (ResolveClassChildRow(entry, "Position") is MemberValue position
                            && (position is not Vector3MemberValue { value: not null } vector
                                || !Finite(vector.value.x) || !Finite(vector.value.y) || !Finite(vector.value.z)))
                            throw PlacementError("object-position-invalid", $"Object '{entryId}' requires a finite Position.");
                        ValidateObjectFootprint(entry);
                    }
                }
            }
            var tileImports = new HashSet<string>(InternalRecordRelations.ResolveTargetIds(InternalRecordRelationKinds.WorldGridTileImport, grid.classId));
            var objectImports = new HashSet<string>(InternalRecordRelations.ResolveTargetIds(InternalRecordRelationKinds.WorldGridObjectImport, grid.classId));
            foreach (string layerId in primitive.ResolveTileLayerIds())
            {
                var occupied = new HashSet<(string source, Vector2Int cell)>();
                var dependencies = new HashSet<string>();
                var records = primitive.BuildTileLayerRecords(layerId, dependencies);
                plan.PreparedTileLayers[(gridId, layerId)] = new NeoPreparedLayerRecords<NeoTilePlacementRecord>(records, dependencies);
                foreach (NeoTilePlacementRecord tile in records)
                {
                    ValidateTileRow(tile.PlacementValueId);
                    ValidateLayerClass(tile.AssetClassId, layerId, tileImports, true, compatibleLayers);
                    if (!occupied.Add((tile.SourceTileLayerLinkId, tile.Cell)))
                        throw PlacementError("tile-cell-occupied", $"Tile link '{tile.SourceTileLayerLinkId}' has more than one tile at {tile.Cell}.");
                }
            }
            foreach (string layerId in primitive.ResolveObjectLayerIds())
            {
                var occupied = new Dictionary<Vector2Int, string>();
                var dependencies = new HashSet<string>();
                var records = primitive.BuildObjectLayerRecords(layerId, dependencies);
                plan.PreparedObjectLayers[(gridId, layerId)] = new NeoPreparedLayerRecords<NeoObjectPlacementRecord>(records, dependencies);
                foreach (NeoObjectPlacementRecord obj in records)
                {
                    ValidateLayerClass(obj.AssetClassId, layerId, objectImports, false, compatibleLayers);
                    if (ResolveValueRow(obj.InstanceId) is ObjectMemberValue row
                        && row.instanceVariantId is string variantId
                        && (!data.variants.TryGetValue(variantId, out VariantRecord? variant)
                            || !ResolveClassInheritanceChain(obj.AssetClassId).Any(type => type.id == variant.classId)))
                        throw PlacementError("object-variant-invalid", $"Object '{obj.InstanceId}' has an incompatible variant '{variantId}'.");
                    foreach (Vector2Int cell in obj.Footprint)
                    {
                        if (occupied.TryGetValue(cell, out string other) && other != obj.InstanceId)
                            throw PlacementError("tile-grid-object-cell-occupied", $"Object layer '{layerId}' has objects '{other}' and '{obj.InstanceId}' at {cell}.");
                        occupied[cell] = obj.InstanceId;
                    }
                }
            }
        }

        private void ValidateObjectFootprint(ObjectMemberValue owner)
        {
            if (ValidatePlacementCollectionField(owner, "PlacementTiles") is not ArrayMemberValue footprint) return;
            string? entryClassId = null;
            foreach (var field in ResolveStoredInstanceSchema(owner.classId!))
                if (field.schemaKey == "PlacementTiles"
                    && TryGetMember(field.memberId, out Member? member) && member is ListMember list
                    && TryGetMember(list.entryMemberId, out Member? entry) && entry is ClassMember classEntry)
                {
                    entryClassId = classEntry.classId;
                    break;
                }
            if (string.IsNullOrEmpty(entryClassId))
                throw PlacementError("placement-list-invalid", $"Object '{owner.id}' PlacementTiles requires a declared class entry type.");
            foreach (string tileId in PlacementListEntries(footprint.id))
            {
                ObjectMemberValue tile = RequirePlacementClass(tileId, null);
                if (!ResolveClassInheritanceChain(tile.classId!).Any(type => type.id == entryClassId))
                    throw PlacementError("object-footprint-class-invalid", $"Object '{owner.id}' footprint '{tileId}' must inherit '{entryClassId}'.");
                ValidatePlacementCell(tile);
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private ObjectMemberValue RequirePlacementClass(string valueId, string? kind)
        {
            if (ResolveValueRow(valueId) is not ObjectMemberValue { classId: not null } row
                || !TryGetClass(row.classId, out NeoSchemaClass? type)
                || type.Modifier == NeoClassModifierKind.Abstract
                || (kind is not null && !HasWorldKind(row.classId, kind)))
                throw PlacementError("placement-class-invalid", $"Placement '{valueId}' must be a concrete {kind ?? "world"} class row.");
            return row;
        }

        private ArrayMemberValue? ValidatePlacementCollectionField(ObjectMemberValue owner, string key)
        {
            MemberValue? row = ResolveClassChildRow(owner, key);
            if (row is null)
            {
                if (owner.value?.TryGetValue(key, out string? id) == true
                    && ResolveValueRow(id)?.IsRemoved != true)
                    throw PlacementError("placement-row-missing", $"Placement '{owner.id}' field '{key}' references missing row '{id}'.");
                return null;
            }
            if (row.IsRemoved) return null;
            if (row is not ArrayMemberValue array)
                throw PlacementError("placement-list-invalid", $"Placement '{owner.id}' field '{key}' must be a List row.");
            return array;
        }

        private IEnumerable<string> PlacementListEntries(string listId)
        {
            if (ResolveValueRow(listId) is not ArrayMemberValue list)
                throw PlacementError("placement-list-invalid", $"Placement list '{listId}' is not a List row.");
            if (list.value is null)
            {
                if (TryInferMemberForValueId(listId, out Member? member)
                    && member.Requirement == NeoMemberRequirementKind.Required)
                    throw PlacementError("placement-list-required", $"Placement list '{listId}' cannot be null.");
                yield break;
            }
            var seen = new HashSet<string>();
            foreach (string id in list.value)
            {
                MemberValue? row = ResolveValueRow(id);
                if (row?.IsRemoved == true) continue;
                if (row is null)
                    throw PlacementError("placement-row-missing", $"Placement list '{listId}' references missing row '{id}'.");
                if (!seen.Add(id))
                    throw PlacementError("placement-row-duplicate", $"Placement list '{listId}' contains row '{id}' more than once.");
                yield return id;
            }
            foreach (string id in GetUnorderedListEntryIds(listId))
                if (seen.Add(id)) yield return id;
        }

        private void ValidateLayerClass(
            string classId, string layerId, HashSet<string> imports, bool tile,
            Dictionary<(bool tile, string classId), HashSet<string>> compatibleLayers)
        {
            if (!imports.Contains(classId))
                throw PlacementError("tile-grid-asset-not-imported", $"Class '{classId}' is not imported by the grid.");
            if (!compatibleLayers.TryGetValue((tile, classId), out var layers))
            {
                string relation = tile ? InternalRecordRelationKinds.WorldTileCompatibleLayer : InternalRecordRelationKinds.WorldObjectCompatibleLayer;
                compatibleLayers[(tile, classId)] = layers = new HashSet<string>(InternalRecordRelations.ResolveTargetIds(relation, classId));
            }
            if (!layers.Contains(layerId))
                throw PlacementError("placement-layer-incompatible", $"Class '{classId}' is not compatible with layer '{layerId}'.");
        }

        private static NeoPlacementValidationException PlacementError(string code, string message) => new(code, message);
    }
}
