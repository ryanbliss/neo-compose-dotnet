// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// A geometry-preserving direct tile placement conversion proven by the
    /// shared placement validator. The lookup cache can update this placement
    /// without rebuilding its layer.
    /// </summary>
    internal sealed class NeoValidatedTileConversion
    {
        internal NeoValidatedTileConversion(
            string gridValueId,
            string layerId,
            string placementValueId,
            string nextClassId,
            string nextCellValueId,
            double nextUpdatedAtMs)
        {
            GridValueId = gridValueId;
            LayerId = layerId;
            PlacementValueId = placementValueId;
            NextClassId = nextClassId;
            NextCellValueId = nextCellValueId;
            NextUpdatedAtMs = nextUpdatedAtMs;
        }

        internal string GridValueId { get; }
        internal string LayerId { get; }
        internal string PlacementValueId { get; }
        internal string NextClassId { get; }
        internal string NextCellValueId { get; }
        internal double NextUpdatedAtMs { get; }
    }

    public partial class NeoClient
    {
        private bool TryValidateDirectTileConversions(
            NeoWritePlan plan,
            Dictionary<string, NeoReadOnlyTileGridPrimitive> primitives,
            Dictionary<(bool tile, string classId), HashSet<string>> compatibleLayers)
        {
            if (plan.Bindings.Count != 0 || plan.Rows.Count == 0) return false;

            var conversions = new List<(
                ObjectMemberValue next,
                string nextCellValueId)>();
            var conversionRootIds = new HashSet<string>();
            var conversionRootKeys = new HashSet<(NeoValueOwnership ownership, string id)>();
            var allowedCells = new HashSet<(NeoValueOwnership ownership, string id)>();
            foreach (var pair in plan.Rows)
            {
                if (pair.Value is not ObjectMemberValue next
                    || next.IsRemoved
                    || !TryGetCommittedOverlaidValue(
                        pair.Key.ownership,
                        next.id,
                        out ObjectMemberValue? previous)
                    || previous.IsRemoved
                    || previous.classId == next.classId
                    || string.IsNullOrEmpty(previous.classId)
                    || string.IsNullOrEmpty(next.classId)
                    || !HasWorldKind(previous.classId, "tile")
                    || !HasWorldKind(next.classId, "tile"))
                {
                    continue;
                }
                if (!plan.TryGetOwnership(next.id, out NeoValueOwnership effectiveOwnership)
                    || effectiveOwnership != pair.Key.ownership
                    || !ReferenceEquals(plan.Resolve(next.id), next)
                    || next.value is not { Count: 1 }
                    || !next.value.TryGetValue("Cell", out string? nextCellValueId)
                    || string.IsNullOrEmpty(nextCellValueId)
                    || previous.value is not { Count: 1 }
                    || !previous.value.TryGetValue("Cell", out string? previousCellValueId)
                    || previousCellValueId != nextCellValueId
                    || next.hasInstanceConstructorId
                    || next.constructorArgs is not null
                    || next.instanceVariantId is not null
                    || next.instanceVariantRowValueId is not null
                    || next.genericBindings is not null
                    || next.mark is not null
                    || next.containerId != previous.containerId
                    || next.mapKey != previous.mapKey
                    || next.sourceValueId != previous.sourceValueId
                    || !next.createdAt.Equals(previous.createdAt)
                    || plan.Resolve(pair.Key.ownership, nextCellValueId!)
                        is not Vector2MemberValue { value: not null } nextCell
                    || nextCell.id != nextCellValueId
                    || !TryResolveCommittedTileCellRow(
                        pair.Key.ownership,
                        previous,
                        out Vector2MemberValue? previousCell)
                    || !SameCellExceptPartition(previousCell!, nextCell)
                    || nextCell.value.x != Math.Truncate(nextCell.value.x)
                    || nextCell.value.y != Math.Truncate(nextCell.value.y)
                    || nextCell.value.x < int.MinValue
                    || nextCell.value.x > int.MaxValue
                    || nextCell.value.y < int.MinValue
                    || nextCell.value.y > int.MaxValue)
                {
                    return false;
                }
                conversionRootIds.Add(next.id);
                conversionRootKeys.Add(pair.Key);
                allowedCells.Add((pair.Key.ownership, nextCellValueId!));
                conversions.Add((next, nextCellValueId!));
            }
            if (conversions.Count == 0) return false;
            foreach (var pair in plan.Rows)
            {
                if (conversionRootKeys.Contains(pair.Key)) continue;
                if (!allowedCells.Contains(pair.Key)
                    || pair.Value is not Vector2MemberValue { value: not null })
                {
                    return false;
                }
            }
            if (candidateReplay is not null
                && candidateReplay.AffectedRoots.Any(id => !conversionRootIds.Contains(id)))
            {
                return false;
            }

            var validated = new List<NeoValidatedTileConversion>(conversions.Count);
            foreach (var conversion in conversions)
            {
                if (!TryResolveDirectTileRoute(
                        plan,
                        conversion.next,
                        primitives,
                        out string? gridValueId,
                        out NeoGridLayerLinkModel? link))
                {
                    return false;
                }
                ValidateTileRow(conversion.next.id);
                if (ResolveValueRow(gridValueId!)
                    is not ObjectMemberValue { classId: not null } grid)
                {
                    return false;
                }
                var imports = new HashSet<string>(
                        InternalRecordRelations.ResolveTargetIds(
                        InternalRecordRelationKinds.WorldGridTileImport,
                        grid.classId!));
                ValidateLayerClass(
                    conversion.next.classId!,
                    link!.LayerId,
                    imports,
                    tile: true,
                    compatibleLayers);
                validated.Add(new NeoValidatedTileConversion(
                    gridValueId!,
                    link.LayerId,
                    conversion.next.id,
                    conversion.next.classId!,
                    conversion.nextCellValueId,
                    conversion.next.updatedAt.EpochMilliseconds));
            }
            plan.ValidatedTileConversions.AddRange(validated);
            return true;
        }

        private bool TryResolveCommittedTileCellRow(
            NeoValueOwnership ownership,
            ObjectMemberValue tile,
            out Vector2MemberValue? cell)
        {
            cell = null;
            if (tile.value?.TryGetValue("Cell", out string? cellId) != true) return false;
            if (!TryGetCommittedOverlaidValue(
                    ownership,
                    cellId,
                    out Vector2MemberValue? stored)) return false;
            cell = stored;
            return stored.value is not null;
        }

        private bool TryResolveDirectTileRoute(
            NeoWritePlan plan,
            ObjectMemberValue placement,
            Dictionary<string, NeoReadOnlyTileGridPrimitive> primitives,
            out string? gridValueId,
            out NeoGridLayerLinkModel? link)
        {
            var pending = new Queue<string>();
            pending.Enqueue(placement.id);
            if (!string.IsNullOrEmpty(placement.containerId))
                pending.Enqueue(placement.containerId!);
            var visited = new HashSet<string>();
            var grids = new HashSet<string>();
            var links = new HashSet<string>();
            while (pending.Count != 0)
            {
                string id = pending.Dequeue();
                if (!visited.Add(id)) continue;
                MemberValue? row = plan.Resolve(id);
                if (row is ObjectMemberValue { classId: not null } objectRow)
                {
                    if (HasWorldKind(objectRow.classId, "tileGrid")) grids.Add(id);
                    if (HasWorldKind(objectRow.classId, "tileLayerLink")) links.Add(id);
                }
                if (!string.IsNullOrEmpty(row?.containerId)) pending.Enqueue(row!.containerId!);
                foreach (string parent in PlacementParents(id)) pending.Enqueue(parent);
                foreach (string parent in plan.ParentCandidates(id)) pending.Enqueue(parent);
            }
            gridValueId = grids.Count == 1 ? grids.First() : null;
            string? linkValueId = links.Count == 1 ? links.First() : null;
            NeoReadOnlyTileGridPrimitive? primitive = gridValueId is not null && primitives.TryGetValue(gridValueId, out var found)
                ? found
                : null;
            link = null;
            if (primitive is not null && linkValueId is not null)
            {
                foreach (NeoGridLayerLinkModel candidate in primitive.ResolveGridLinks(null))
                {
                    if (!candidate.IsTileLink
                        || candidate.LinkValueId != linkValueId
                        || candidate.ListValueId != placement.containerId)
                    {
                        continue;
                    }
                    if (link is not null) return false;
                    link = candidate;
                }
            }
            return gridValueId is not null && primitive is not null && link is not null;
        }

        private static bool SameCellExceptPartition(
            Vector2MemberValue previous,
            Vector2MemberValue next)
        {
            if (ReferenceEquals(previous, next)) return true;
            var previousJson = JObject.FromObject(previous);
            var nextJson = JObject.FromObject(next);
            return JToken.DeepEquals(previousJson, nextJson);
        }
    }
}
