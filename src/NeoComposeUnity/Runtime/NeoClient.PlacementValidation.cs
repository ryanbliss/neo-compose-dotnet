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
        // A child's writable parents: one parent id, or a HashSet in the rare
        // case of several. Owned children almost always have exactly one.
        private Dictionary<string, object>? writablePlacementParents;
        private readonly Dictionary<(NeoValueOwnership ownership, string id), string[]> writablePlacementChildren = new();
        private readonly List<string> placementChildScratch = new();

        internal static IEnumerable<string> PlacementChildIds(MemberValue row)
        {
            if (row is ObjectMemberValue { value: not null } obj)
                foreach (string child in obj.value.Values)
                    yield return child;
            else if (row is ArrayMemberValue { value: not null } array)
                foreach (string child in array.value)
                    yield return child;
            if (row is ObjectMemberValue { constructorArgs: not null } constructed)
                foreach (var token in constructed.constructorArgs.Values)
                    if (token?.Type == Newtonsoft.Json.Linq.JTokenType.String && (string?)token is string child)
                        yield return child;
        }

        /// <summary><see cref="PlacementChildIds"/> without its enumerator, for the per-row index.</summary>
        private static void CollectPlacementChildIds(MemberValue row, List<string> into)
        {
            if (row is ObjectMemberValue objectRow)
            {
                if (objectRow.value is not null)
                    foreach (string child in objectRow.value.Values)
                        into.Add(child);
                if (objectRow.constructorArgs is not null)
                    foreach (var token in objectRow.constructorArgs.Values)
                        if (token?.Type == JTokenType.String && (string?)token is string child)
                            into.Add(child);
            }
            else if (row is ArrayMemberValue { value: not null } array)
                into.AddRange(array.value);
        }

        private void IndexPlacementParent(NeoValueOwnership ownership, MemberValue row)
        {
            if (writablePlacementParents is null)
                return;
            List<string> next = placementChildScratch;
            next.Clear();
            // Only records and arrays link children; a leaf row has none to index.
            if (row is ObjectMemberValue or ArrayMemberValue)
                CollectPlacementChildIds(row, next);
            var key = (ownership, row.id);
            if (!writablePlacementChildren.TryGetValue(key, out string[]? previous))
            {
                if (next.Count == 0)
                    return;
                writablePlacementChildren[key] = next.ToArray();
                foreach (string child in next)
                    AddPlacementParent(child, row.id);
                return;
            }
            // Relink only past the unchanged leading children, so appending to
            // or popping a long list row touches the entries that changed.
            int prefix = 0;
            int shared = Math.Min(previous.Length, next.Count);
            while (prefix < shared && string.Equals(previous[prefix], next[prefix], StringComparison.Ordinal))
                prefix++;
            if (prefix == previous.Length && prefix == next.Count)
                return;
            writablePlacementChildren.TryGetValue(
                (ownership == NeoValueOwnership.Save ? NeoValueOwnership.Session : NeoValueOwnership.Save, row.id),
                out string[]? kept);
            HashSet<string>? nextSet = null;
            HashSet<string>? keptSet = null;
            bool few = previous.Length - prefix <= 4;
            for (int i = prefix; i < previous.Length; i++)
            {
                string child = previous[i];
                // The same id in the other store keeps the links it shares.
                if (HoldsChild(next, child, few, ref nextSet)
                    || (kept is not null && HoldsChild(kept, child, few, ref keptSet)))
                    continue;
                RemovePlacementParent(child, row.id);
            }
            for (int i = prefix; i < next.Count; i++)
                AddPlacementParent(next[i], row.id);
            if (next.Count == 0)
                writablePlacementChildren.Remove(key);
            else
                writablePlacementChildren[key] = next.ToArray();
        }

        // A scan for a few probes, and a set built once past that.
        private static bool HoldsChild(IReadOnlyList<string> children, string child, bool few, ref HashSet<string>? set)
        {
            if (few)
            {
                for (int i = 0; i < children.Count; i++)
                    if (string.Equals(children[i], child, StringComparison.Ordinal))
                        return true;
                return false;
            }
            return (set ??= new HashSet<string>(children, StringComparer.Ordinal)).Contains(child);
        }

        private void AddPlacementParent(string child, string parent)
        {
            if (!writablePlacementParents!.TryGetValue(child, out object? parents))
                writablePlacementParents[child] = parent;
            else if (parents is HashSet<string> set)
                set.Add(parent);
            else if (!string.Equals((string)parents, parent, StringComparison.Ordinal))
                writablePlacementParents[child] = new HashSet<string>(StringComparer.Ordinal) { (string)parents, parent };
        }

        private void RemovePlacementParent(string child, string parent)
        {
            if (!writablePlacementParents!.TryGetValue(child, out object? parents))
                return;
            if (parents is HashSet<string> set)
            {
                if (set.Remove(parent) && set.Count == 0)
                    writablePlacementParents.Remove(child);
            }
            else if (string.Equals((string)parents, parent, StringComparison.Ordinal))
                writablePlacementParents.Remove(child);
        }

        private void UnindexPlacementParent(NeoValueOwnership ownership, string id)
        {
            if (writablePlacementParents is null
                || !writablePlacementChildren.Remove((ownership, id), out string[]? children))
                return;
            NeoValueOwnership other = ownership == NeoValueOwnership.Save ? NeoValueOwnership.Session : NeoValueOwnership.Save;
            // The same id in the other store keeps the links it shares.
            HashSet<string>? retained = writablePlacementChildren.TryGetValue((other, id), out string[]? kept)
                ? new HashSet<string>(kept, StringComparer.Ordinal)
                : null;
            foreach (string child in children)
                if (retained is null || !retained.Contains(child))
                    RemovePlacementParent(child, id);
        }

        /// <summary>The children the committed placement index links from a stored row, if any.</summary>
        internal string[]? IndexedPlacementChildren(NeoValueOwnership ownership, string id)
        {
            EnsureWritablePlacementParents();
            return writablePlacementChildren.TryGetValue((ownership, id), out string[]? children) ? children : null;
        }

        private void EnsureWritablePlacementParents()
        {
            if (writablePlacementParents is not null)
                return;
            writablePlacementParents = new Dictionary<string, object>();
            foreach (MemberValue row in saveData.values.Values)
                IndexPlacementParent(NeoValueOwnership.Save, row);
            foreach (MemberValue row in sessionData.values.Values)
                IndexPlacementParent(NeoValueOwnership.Session, row);
        }

        private IEnumerable<string> PlacementParents(string childId)
        {
            EnsureWritablePlacementParents();
            if (writablePlacementParents!.TryGetValue(childId, out object? writable))
            {
                if (writable is HashSet<string> set)
                    foreach (string parent in set)
                        yield return parent;
                else
                    yield return (string)writable;
            }
            if (ValueInferenceIndex.Parents.TryGetValue(childId, out var authored))
                foreach (var parent in authored)
                    yield return parent.Key;
            if (TryResolveVirtualPlacement(childId, out var placement))
                yield return placement.parentValueId;
        }

        /// <summary>The iterator above without its enumerator objects, for the per-commit walk.</summary>
        private void CollectPlacementParents(string childId, ICollection<string> into)
        {
            EnsureWritablePlacementParents();
            if (writablePlacementParents!.TryGetValue(childId, out object? writable))
            {
                if (writable is HashSet<string> set)
                {
                    foreach (string parent in set)
                        into.Add(parent);
                }
                else
                    into.Add((string)writable);
            }
            if (ValueInferenceIndex.Parents.TryGetValue(childId, out var authored))
                foreach (var parent in authored)
                    into.Add(parent.Key);
            if (TryResolveVirtualPlacement(childId, out var placement))
                into.Add(placement.parentValueId);
        }

        // Every commit asks this for each row it walks past; the answer only
        // changes with the schema, which clears the cache with the class caches.
        // Each class's world kinds across its inheritance chain.
        private readonly Dictionary<string, string[]> worldKindsByClass = new(StringComparer.Ordinal);

        // A layer-link class's validated target layer, cleared with the class caches.
        internal readonly Dictionary<(string classId, bool tile), string> LayerLinkTargetByClass = new();

        internal bool HasWorldKind(string? classId, string kind) => HasWorldKind(WorldKinds(classId), kind);

        private static bool HasWorldKind(string[] kinds, string kind)
        {
            for (int i = 0; i < kinds.Length; i++)
            {
                if (kinds[i] == kind)
                    return true;
            }
            return false;
        }

        private string[] WorldKinds(string? classId)
        {
            if (string.IsNullOrEmpty(classId))
                return Array.Empty<string>();
            if (worldKindsByClass.TryGetValue(classId!, out string[]? kinds))
                return kinds;
            List<string>? found = null;
            foreach (NeoSchemaClass type in ResolveClassInheritanceChain(classId!))
                if (type.system?["worldKind"]?.ToString() is string kind)
                    (found ??= new List<string>()).Add(kind);
            kinds = found?.ToArray() ?? Array.Empty<string>();
            worldKindsByClass[classId!] = kinds;
            return kinds;
        }

        // The walk below runs on every commit. Its collections are reused
        // between commits; a validation nested inside another (a candidate
        // replay) gets its own throwaway set.
        private sealed class WriteValidationScratch
        {
            internal readonly Queue<string> pending = new();
            internal readonly HashSet<string> writtenDescendants = new(StringComparer.Ordinal);
            internal readonly HashSet<string> visited = new(StringComparer.Ordinal);
            internal readonly HashSet<string> grids = new(StringComparer.Ordinal);
            internal readonly HashSet<string> tiles = new(StringComparer.Ordinal);
            internal readonly HashSet<string> objects = new(StringComparer.Ordinal);
            internal readonly List<string> parents = new();
            internal readonly Dictionary<string, NeoReadOnlyTileGridPrimitive> primitives = new(StringComparer.Ordinal);
            internal readonly Dictionary<(bool tile, string classId), HashSet<string>> compatibleLayers = new();
            internal bool inUse;

            internal void Clear()
            {
                pending.Clear();
                writtenDescendants.Clear();
                visited.Clear();
                grids.Clear();
                tiles.Clear();
                objects.Clear();
                parents.Clear();
                primitives.Clear();
                compatibleLayers.Clear();
            }
        }

        private WriteValidationScratch? writeValidationScratch;

#if NEO_COMPOSE_PROFILING
        private static readonly Unity.Profiling.ProfilerMarker ValidatePlacementsMarker = new("NeoCompose.Write.ValidatePlacements");
#endif
        private void ValidateWritePlan(NeoWritePlan plan)
        {
#if NEO_COMPOSE_PROFILING
            using var sample = ValidatePlacementsMarker.Auto();
#endif
            if (plan.Rows.Count == 0 && plan.Bindings.Count == 0)
                return;
            WriteValidationScratch scratch;
            if (writeValidationScratch is null)
                scratch = writeValidationScratch = new WriteValidationScratch();
            else
                scratch = writeValidationScratch.inUse ? new WriteValidationScratch() : writeValidationScratch;
            scratch.inUse = true;
            try
            {
                ValidateWritePlan(plan, scratch);
            }
            finally
            {
                scratch.Clear();
                scratch.inUse = false;
            }
        }

        private void ValidateWritePlan(NeoWritePlan plan, WriteValidationScratch scratch)
        {
            Queue<string> pending = scratch.pending;
            HashSet<string> writtenDescendants = scratch.writtenDescendants;
            foreach (var key in plan.Rows.Keys)
                writtenDescendants.Add(key.id);
            foreach (var pair in plan.Rows)
            {
                pending.Enqueue(pair.Key.id);
                if (pair.Value is null)
                    continue;
                if (!string.IsNullOrEmpty(pair.Value.containerId))
                    pending.Enqueue(pair.Value.containerId!);
            }
            foreach (var binding in plan.Bindings.Values)
                if (binding.valueId is not null)
                    pending.Enqueue(binding.valueId);
            if (candidateReplay is not null)
                foreach (var pair in candidateReplay.Values)
                    if (!virtualValues.TryGetValue(pair.Key, out var previousVirtual)
                        || !NeoSemanticJson.MemberRowsEqual(previousVirtual, pair.Value))
                        pending.Enqueue(pair.Key);
            HashSet<string> visited = scratch.visited;
            HashSet<string> grids = scratch.grids;
            HashSet<string> tiles = scratch.tiles;
            HashSet<string> objects = scratch.objects;
            List<string> parentList = scratch.parents;
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                if (!visited.Add(id))
                    continue;
                MemberValue? candidate = plan.Resolve(id);
                TryGetCommittedValue(id, out MemberValue? previous);
                string[] kinds = WorldKinds(candidate?.classId);
                if (HasWorldKind(candidate?.classId is null ? WorldKinds(previous?.classId) : kinds, "tileGrid"))
                    grids.Add(id);
                if (HasWorldKind(kinds, "tile"))
                    tiles.Add(id);
                if (HasWorldKind(kinds, "object"))
                    objects.Add(id);
                if (!string.IsNullOrEmpty(candidate?.containerId))
                    pending.Enqueue(candidate!.containerId!);
                if (!string.IsNullOrEmpty(previous?.containerId))
                    pending.Enqueue(previous!.containerId!);
                parentList.Clear();
                CollectPlacementParents(id, parentList);
                for (int parentIndex = 0; parentIndex < parentList.Count; parentIndex++)
                {
                    string parent = parentList[parentIndex];
                    if (!IsPlacementEdge(plan, parent, id))
                        continue;
                    if (writtenDescendants.Contains(id))
                        writtenDescendants.Add(parent);
                    if (writtenDescendants.Contains(id) && plan.Resolve(parent) is ObjectMemberValue owner && HasWorldKind(owner.classId, "tile"))
                    {
                        CheckFields(owner.value);
                        if (TryResolveVirtualClassChildren(parent, out var defaults))
                            CheckFields(defaults);
                        void CheckFields(Dictionary<string, string>? fields)
                        {
                            if (fields is null)
                                return;
                            foreach (var field in fields)
                                if (field.Key != "Cell" && field.Value == id)
                                    throw PlacementError("tile-instance-member", $"Tile '{parent}' cannot write instance member '{field.Key}'; only Cell is writable.");
                        }
                    }
                    pending.Enqueue(parent);
                }
                foreach (string parent in plan.ParentCandidates(id))
                    if (IsPlacementEdge(plan, parent, id))
                        pending.Enqueue(parent);
            }
            // These builders read rows and declarations only. Do not resolve
            // generated wrappers or populate persistent layer caches here.
            Dictionary<string, NeoReadOnlyTileGridPrimitive> primitives = scratch.primitives;
            foreach (string gridId in grids)
                primitives[gridId] = NeoReadOnlyTileGridPrimitive.Resolve(this, gridId);
            using (ReadCandidate(plan))
            {
                Dictionary<(bool tile, string classId), HashSet<string>> compatibleLayers = scratch.compatibleLayers;
                foreach (string objectId in objects)
                    if (ResolveValueRow(objectId) is ObjectMemberValue obj && !obj.IsRemoved)
                        ValidateObjectFootprint(obj);
                if (TryValidateObjectInsertion(plan, primitives, compatibleLayers))
                    return;
                if (TryValidateDirectTileConversions(plan, primitives, compatibleLayers))
                    return;
                foreach (string tileId in tiles)
                    ValidateTileRow(tileId);
                foreach (string gridId in grids)
                    ValidateGridPlacements(plan, gridId, primitives[gridId], compatibleLayers);
            }
        }

        // The parent index also contains lookup selections and constructor
        // arguments. Those are references, not containment: editing a catalog
        // or config row must not walk every world object that references it.
        // Replayed outputs are validated separately above.
        private bool IsPlacementEdge(NeoWritePlan plan, string parentId, string childId)
        {
            // Resolved once, for whichever array row holds the child first.
            Member? arrayMember = null;
            bool arrayMemberKnown = false;
            MemberValue? next = plan.Resolve(parentId);
            TryGetCommittedValue(parentId, out MemberValue? previous);
            if (HasWorldKind(next?.classId ?? previous?.classId, "object"))
                return GeometryChild(next as ObjectMemberValue) || GeometryChild(previous as ObjectMemberValue);
            if (TryResolveVirtualPlacement(childId, out var placement)
                && placement.parentValueId == parentId)
                return true;
            return Owns(next) || Owns(previous);

            bool GeometryChild(ObjectMemberValue? row)
            {
                if (row is null)
                    return false;
                return ResolveClassChildRow(row, "Position")?.id == childId
                    || ResolveClassChildRow(row, "PlacementTiles")?.id == childId;
            }

            bool Owns(MemberValue? row)
            {
                if (row is ObjectMemberValue obj)
                    return obj.value?.ContainsValue(childId) == true;
                if (row is not ArrayMemberValue { value: not null } array
                    || Array.IndexOf(array.value, childId) < 0)
                    return false;
                if (!arrayMemberKnown)
                {
                    arrayMemberKnown = true;
                    arrayMember = PlannedMember(plan, parentId);
                }
                return arrayMember is ListMember;
            }
        }

        /// <summary>
        /// The member of row <paramref name="id"/>: the collection mutator
        /// behind <paramref name="plan"/> knows its own, others are inferred.
        /// </summary>
        private Member? PlannedMember(NeoWritePlan plan, string id) =>
            plan.ReportingMember(id) ?? (TryInferMemberForValueId(id, out Member? member) ? member : null);

        private void ValidateTileRow(string tileId)
        {
            if (ResolveValueRow(tileId) is not ObjectMemberValue tile || tile.IsRemoved)
                return;
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
                || !NeoNumbers.IsWhole(point.x)
                || !NeoNumbers.IsWhole(point.y))
                throw PlacementError("tile-cell-invalid", $"Tile '{tileId}' requires an integer Cell.");
        }

        private void ValidateGridPlacements(
            NeoWritePlan plan, string gridId, NeoReadOnlyTileGridPrimitive primitive,
            Dictionary<(bool tile, string classId), HashSet<string>> compatibleLayers)
        {
            if (ResolveValueRow(gridId) is not ObjectMemberValue { classId: not null } grid || grid.IsRemoved)
                return;
            if (ValidatePlacementCollectionField(grid, "Children") is ArrayMemberValue children)
                foreach (string childId in PlacementListEntries(children.id))
                {
                    ObjectMemberValue link = RequirePlacementClass(childId, null);
                    if (HasWorldKind(link.classId, "tileLayerLink"))
                        ValidatePlacementCollectionField(link, "Tiles");
                    if (HasWorldKind(link.classId, "objectLayerLink"))
                        ValidatePlacementCollectionField(link, "Objects");
                }
            foreach (NeoGridLayerLinkModel link in primitive.ResolveGridLinks(null))
            {
                foreach (string entryId in PlacementListEntries(link.ListValueId))
                {
                    ObjectMemberValue entry = RequirePlacementClass(entryId, link.IsTileLink ? "tile" : "object");
                    if (link.IsTileLink)
                        ValidateTileRow(entryId);
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
                var build = primitive.BuildTileLayerRecords(layerId);
                (plan.PreparedTileLayers ??= new())[(gridId, layerId)] = build;
                foreach (NeoTilePlacementRecord tile in build.Records)
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
                (plan.PreparedObjectLayers ??= new())[(gridId, layerId)] = new NeoPreparedLayerRecords<NeoObjectPlacementRecord>(records, dependencies);
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
            if (ValidatePlacementCollectionField(owner, "PlacementTiles") is not ArrayMemberValue footprint)
                return;
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
            if (row.IsRemoved)
                return null;
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
                if (row?.IsRemoved == true)
                    continue;
                if (row is null)
                    throw PlacementError("placement-row-missing", $"Placement list '{listId}' references missing row '{id}'.");
                if (!seen.Add(id))
                    throw PlacementError("placement-row-duplicate", $"Placement list '{listId}' contains row '{id}' more than once.");
                yield return id;
            }
            foreach (string id in GetUnorderedListEntryIds(listId))
                if (seen.Add(id))
                    yield return id;
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
