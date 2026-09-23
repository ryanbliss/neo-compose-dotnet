// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        private bool TryValidateObjectInsertion(NeoWritePlan plan,
            Dictionary<string, NeoReadOnlyTileGridPrimitive> grids,
            Dictionary<(bool tile, string classId), HashSet<string>> compatibleLayers)
        {
            if (plan.ObjectInsertion is not { } insertion || plan.Bindings.Count != 0
                || grids.Count != 1 || !grids.TryGetValue(insertion.gridId, out var primitive)
                || !gridLookupCaches.TryGetValue(insertion.gridId, out var cache)
                || plan.Resolve(insertion.instanceId) is not ObjectMemberValue { classId: not null } root
                || root.IsRemoved || root.containerId != insertion.listId
                || !TryInferMemberForValueId(insertion.listId, out Member? listMember)
                || listMember is not ListMember list || !IsUnorderedList(list)) return false;

            // The operation hint is not proof: adoption and constructor replay
            // may stage other writes. Only a new owned graph and an unchanged
            // unordered membership row qualify for this path.
            var owned = new HashSet<string>();
            var pending = new Stack<(string id, Member? member)>();
            pending.Push((root.id, null));
            while (pending.Count != 0)
            {
                var next = pending.Pop();
                if (!owned.Add(next.id) || plan.Resolve(next.id) is not MemberValue row) continue;
                foreach (var child in EnumerateOwnedChildLinks(row, next.member)) pending.Push((child.valueId, child.member));
            }
            foreach (var write in plan.Rows)
            {
                if (owned.Contains(write.Key.id)) continue;
                if (write.Key.id != insertion.listId
                    || !TryGetCommittedValue(write.Key.id, out MemberValue? before)
                    || !NeoSemanticJson.MemberRowsEqual(before, write.Value)) return false;
            }
            if (candidateReplay is not null && candidateReplay.AffectedRoots.Any(id => !owned.Contains(id))) return false;

            var links = primitive.ResolveGridLinks(null).Where(link => !link.IsTileLink && link.LayerId == insertion.layerId).ToArray();
            if (!links.Any(link => link.ListValueId == insertion.listId)) return false;
            var members = new List<string>();
            foreach (var link in links) members.AddRange(primitive.ResolveListEntryIds(link.ListValueId, null));
            var tileDependencies = new HashSet<string>();
            if (primitive.HasObjectCarriedTiles(root, tileDependencies)) return false;
            var dependencies = new HashSet<string> { root.id };
            Vector2Int origin = primitive.ReadObjectOrigin(root, dependencies);
            tileDependencies.UnionWith(dependencies);
            var footprint = primitive.ReadObjectFootprint(root, origin, dependencies);
            RequirePlacementClass(root.id, "object");
            ValidateObjectFootprint(root);
            if (ResolveClassChildRow(root, "Position") is not Vector3MemberValue { value: not null } position
                || !Finite(position.value.x) || !Finite(position.value.y) || !Finite(position.value.z))
                throw PlacementError("object-position-invalid", $"Object '{root.id}' requires a finite Position.");
            if (ResolveValueRow(insertion.gridId) is not ObjectMemberValue { classId: not null } grid) return false;
            var imports = new HashSet<string>(InternalRecordRelations.ResolveTargetIds(InternalRecordRelationKinds.WorldGridObjectImport, grid.classId!));
            ValidateLayerClass(root.classId!, insertion.layerId, imports, false, compatibleLayers);
            if (root.instanceVariantId is string variantId
                && (!data.variants.TryGetValue(variantId, out var variant)
                    || !ResolveClassInheritanceChain(root.classId).Any(type => type.id == variant.classId)))
                throw PlacementError("object-variant-invalid", $"Object '{root.id}' has an incompatible variant '{variantId}'.");
            if (!plan.TryGetOwnership(root.id, out var ownership)) return false;
            return cache.PrepareObjectInsertion(plan, insertion.layerId, members,
                new NeoObjectPlacementRecord(root.id, origin, footprint, 0, root.classId,
                    root.value?.GetValueOrDefault("assetValueId"), ownership), dependencies, tileDependencies);
        }
    }

    internal sealed partial class NeoTileGridLookupCache
    {
        internal bool PrepareObjectInsertion(NeoWritePlan plan, string layerId, IReadOnlyList<string> members,
            NeoObjectPlacementRecord added, HashSet<string> dependencies, HashSet<string> tileDependencies)
        {
            // Never seed an index from speculative rows. TrySpawn's collision
            // query normally warmed this committed index before preparing its plan.
            if (!objectLayers.TryGetValue(layerId, out var index) || index.ById.ContainsKey(added.InstanceId)) return false;
            if (members.Count != index.Records.Count + 1) return false;
            int insertedAt = -1, previousIndex = 0;
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] == added.InstanceId) { insertedAt = i; continue; }
                if (previousIndex >= index.Records.Count || index.Records[previousIndex++].InstanceId != members[i]) return false;
            }
            if (insertedAt < 0) return false;
            foreach (var cell in added.Footprint)
                if (index.CandidatesByCell.TryGetValue(cell, out var occupants) && occupants.Count != 0)
                    throw Occupied(layerId, added.InstanceId, occupants[0].InstanceId, cell);

            added = WithOrder(added, insertedAt);
            plan.ValidatedObjectInsertionGrid = primitive.GridValueId;
            plan.AfterCommit(() =>
            {
                // Membership order is the layer's enumeration order. Shift only the following
                // ranks, without rereading any sibling's value graph; the shift
                // keeps their relative order, so it is not a change.
                for (int i = insertedAt; i < index.Records.Count; i++)
                {
                    var before = index.Records[i];
                    var after = WithOrder(before, before.Order + 1);
                    index.Records[i] = after;
                    index.ById[after.InstanceId] = after;
                    index.RecordIndices[after.InstanceId] = i + 1;
                    foreach (var cell in before.Footprint)
                    {
                        var bucket = index.CandidatesByCell[cell];
                        int slot = bucket.IndexOf(before);
                        if (slot >= 0) bucket[slot] = after;
                    }
                }
                index.Records.Insert(insertedAt, added);
                index.ById.Add(added.InstanceId, added);
                index.RecordIndices.Add(added.InstanceId, insertedAt);
                foreach (var cell in added.Footprint) Add(index.CandidatesByCell, cell, added);
                index.DependencyIds.UnionWith(dependencies);
                // Future changes may add carried tile links to this object.
                foreach (var layer in tileLayers.Values) layer.DependencyIds.UnionWith(tileDependencies);
                var change = new NeoTileGridChangedArgs(primitive.GridValueId,
                    objectLayers: new[] { new NeoObjectLayerChangedArgs(layerId, Array.Empty<NeoObjectInstanceId>(),
                        new NeoObjectInstanceId[] { added.InstanceId }, added.Footprint, NeoTileGridChangeSourceKind.Direct, null) },
                    source: primitive.Client.CurrentChangeSource);
                primitive.Client.ScriptGridQueries.NotifyChanged(change);
                plan.AfterNotifications(() => Changed?.Invoke(change));
            });
            return true;
        }

        private static NeoObjectPlacementRecord WithOrder(NeoObjectPlacementRecord row, int order) =>
            new(row.InstanceId, row.Cell, row.Footprint, order, row.AssetClassId, row.AssetValueId, row.Ownership);
    }
}
