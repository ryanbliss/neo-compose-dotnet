// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Runtime contract implemented by generated tile layer link values
    /// (world kind <c>tileLayerLink</c>, e.g. a generated <c>NeoTileLayerLink</c>
    /// family). Grants the <c>GetTile</c>/<c>GetTiles</c> query extensions in
    /// <see cref="NeoLayerLinkLookupExtensions"/>.
    /// </summary>
    public interface INeoTileLayerLinkValue : INeoValueReference
    {
    }

    /// <summary>
    /// Runtime contract implemented by generated object layer link values
    /// (world kind <c>objectLayerLink</c>). Grants the <c>GetObject</c>/<c>GetObjects</c>
    /// query extensions in <see cref="NeoLayerLinkLookupExtensions"/>.
    /// </summary>
    public interface INeoObjectLayerLinkValue : INeoValueReference
    {
    }

    /// <summary>
    /// Grid-space lookups over a layer link's own authored content: the link's
    /// <c>Position</c> is the projection origin, so cells here line up with the
    /// cells the link contributes to its target layer. Single-result overloads
    /// with a <see cref="NeoCellPattern"/> return the first match in pattern
    /// order (nearest first for center-out patterns).
    /// </summary>
    public static class NeoLayerLinkLookupExtensions
    {
        public static IReadOnlyList<NeoResolvedTileInstance> GetTiles(
            this INeoTileLayerLinkValue link)
        {
            return ProjectTiles(link).Winners;
        }

        public static IReadOnlyList<NeoResolvedTileInstance<TTile>> GetTiles<TTile>(
            this INeoTileLayerLinkValue link)
            where TTile : class, INeoValueReference
        {
            return TypedTiles<TTile>(ProjectTiles(link).Winners);
        }

        public static NeoResolvedTileInstance? GetTile(
            this INeoTileLayerLinkValue link,
            Vector2Int cell)
        {
            ProjectTiles(link).ByCell.TryGetValue(cell, out var tile);
            return tile;
        }

        public static NeoResolvedTileInstance<TTile>? GetTile<TTile>(
            this INeoTileLayerLinkValue link,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            return link.GetTile(cell)?.As<TTile>();
        }

        public static IReadOnlyList<NeoResolvedTileInstance> GetTiles(
            this INeoTileLayerLinkValue link,
            Vector2Int cell)
        {
            var tile = link.GetTile(cell);
            return tile is null
                ? Array.Empty<NeoResolvedTileInstance>()
                : new[] { tile };
        }

        public static NeoResolvedTileInstance? GetTile(
            this INeoTileLayerLinkValue link,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var projection = ProjectTiles(link);
            foreach (var cell in pattern.GetCells(origin))
            {
                if (projection.ByCell.TryGetValue(cell, out var tile))
                {
                    return tile;
                }
            }
            return null;
        }

        public static NeoResolvedTileInstance<TTile>? GetTile<TTile>(
            this INeoTileLayerLinkValue link,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TTile : class, INeoValueReference
        {
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var projection = ProjectTiles(link);
            foreach (var cell in pattern.GetCells(origin))
            {
                if (projection.ByCell.TryGetValue(cell, out var tile))
                {
                    var typed = tile.As<TTile>();
                    if (typed is not null) return typed;
                }
            }
            return null;
        }

        public static IReadOnlyList<NeoResolvedTileInstance> GetTiles(
            this INeoTileLayerLinkValue link,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var projection = ProjectTiles(link);
            var tiles = new List<NeoResolvedTileInstance>();
            foreach (var cell in pattern.GetCells(origin))
            {
                if (projection.ByCell.TryGetValue(cell, out var tile))
                {
                    tiles.Add(tile);
                }
            }
            return tiles;
        }

        public static IReadOnlyList<NeoResolvedTileInstance<TTile>> GetTiles<TTile>(
            this INeoTileLayerLinkValue link,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TTile : class, INeoValueReference
        {
            return TypedTiles<TTile>(link.GetTiles(origin, pattern));
        }

        public static IReadOnlyList<NeoResolvedObjectInstance> GetObjects(
            this INeoObjectLayerLinkValue link)
        {
            return ProjectObjects(link);
        }

        public static IReadOnlyList<NeoResolvedObjectInstance<TObject>> GetObjects<TObject>(
            this INeoObjectLayerLinkValue link)
            where TObject : class, INeoValueReference
        {
            return TypedObjects<TObject>(ProjectObjects(link));
        }

        public static NeoResolvedObjectInstance? GetObject(
            this INeoObjectLayerLinkValue link,
            Vector2Int cell)
        {
            foreach (var obj in ProjectObjects(link))
            {
                if (obj.Cell == cell) return obj;
            }
            return null;
        }

        public static NeoResolvedObjectInstance<TObject>? GetObject<TObject>(
            this INeoObjectLayerLinkValue link,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            foreach (var obj in ProjectObjects(link))
            {
                if (obj.Cell != cell) continue;
                var typed = obj.As<TObject>();
                if (typed is not null) return typed;
            }
            return null;
        }

        public static IReadOnlyList<NeoResolvedObjectInstance> GetObjects(
            this INeoObjectLayerLinkValue link,
            Vector2Int cell)
        {
            var objects = new List<NeoResolvedObjectInstance>();
            foreach (var obj in ProjectObjects(link))
            {
                if (obj.Cell == cell)
                {
                    objects.Add(obj);
                }
            }
            return objects;
        }

        public static NeoResolvedObjectInstance? GetObject(
            this INeoObjectLayerLinkValue link,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var projection = ProjectObjects(link);
            foreach (var cell in pattern.GetCells(origin))
            {
                foreach (var obj in projection)
                {
                    if (obj.Cell == cell) return obj;
                }
            }
            return null;
        }

        public static NeoResolvedObjectInstance<TObject>? GetObject<TObject>(
            this INeoObjectLayerLinkValue link,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TObject : class, INeoValueReference
        {
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var projection = ProjectObjects(link);
            foreach (var cell in pattern.GetCells(origin))
            {
                foreach (var obj in projection)
                {
                    if (obj.Cell != cell) continue;
                    var typed = obj.As<TObject>();
                    if (typed is not null) return typed;
                }
            }
            return null;
        }

        public static IReadOnlyList<NeoResolvedObjectInstance> GetObjects(
            this INeoObjectLayerLinkValue link,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var projection = ProjectObjects(link);
            var objects = new List<NeoResolvedObjectInstance>();
            foreach (var cell in pattern.GetCells(origin))
            {
                foreach (var obj in projection)
                {
                    if (obj.Cell == cell)
                    {
                        objects.Add(obj);
                    }
                }
            }
            return objects;
        }

        public static IReadOnlyList<NeoResolvedObjectInstance<TObject>> GetObjects<TObject>(
            this INeoObjectLayerLinkValue link,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TObject : class, INeoValueReference
        {
            return TypedObjects<TObject>(link.GetObjects(origin, pattern));
        }

        private readonly struct TileProjection
        {
            public TileProjection(
                IReadOnlyList<NeoResolvedTileInstance> winners,
                IReadOnlyDictionary<Vector2Int, NeoResolvedTileInstance> byCell)
            {
                Winners = winners;
                ByCell = byCell;
            }

            public IReadOnlyList<NeoResolvedTileInstance> Winners { get; }
            public IReadOnlyDictionary<Vector2Int, NeoResolvedTileInstance> ByCell { get; }
        }

        /// <summary>
        /// Projects the link's authored tiles into grid space the same way the
        /// grid does: origin + tile cell, one winner per cell (last authored
        /// wins), source recorded as this link. Membership and cells come from
        /// the client's value rows when available — rows update before change
        /// notifications fire, while wrapper child nodes can lag one dispatch
        /// behind (the same reason the renderer's live sync prefers rows).
        /// </summary>
        private static TileProjection ProjectTiles(INeoTileLayerLinkValue link)
        {
            if (link is null) throw new ArgumentNullException(nameof(link));

            var byCell = new Dictionary<Vector2Int, NeoResolvedTileInstance>();
            string sourceId = link.valueId ?? string.Empty;
            if (!TryGetValueRow(link, out var client, out ObjectMemberValue? linkRow))
            {
                return new TileProjection(
                    Array.Empty<NeoResolvedTileInstance>(),
                    byCell);
            }
            string layerId = ReadTargetLayerClassId(
                (NeoGeneratedClassValue)link,
                InternalRecordRelationKinds.WorldTileLayerLinkTarget);
            var order = 0;
            var origin = ReadRowOrigin(client!, linkRow!);
            foreach (var instanceValueId in ReadRowListIds(client!, linkRow!, "Tiles"))
            {
                if (!client!.TryGetValue(
                        instanceValueId,
                        out ObjectMemberValue? placement)
                    || placement?.value is null)
                {
                    continue;
                }
                string? assetClassId = ReadDirectReference(
                    placement.value,
                    "assetClassId");
                if (assetClassId is null) continue;
                string? assetValueId = ReadDirectReference(
                    placement.value,
                    "assetValueId");
                var tileValue = client.ResolveRegisteredGeneratedAsset(
                    assetClassId,
                    assetValueId);
                if (tileValue is null) continue;
                var cell = ReadRowCell(client, instanceValueId);
                if (cell is null) continue;
                var projectedCell = origin + cell.Value;
                byCell[projectedCell] = new NeoResolvedTileInstance(
                    instanceValueId,
                    layerId,
                    projectedCell,
                    tileValue,
                    order++,
                    NeoTileOutputSourceKind.TileLayerLink,
                    null,
                    sourceId);
            }

            var winners = new List<NeoResolvedTileInstance>(byCell.Values);
            winners.Sort((left, right) => left.Order.CompareTo(right.Order));
            return new TileProjection(winners, byCell);
        }

        /// <summary>
        /// Projects the link's authored objects into grid space: origin plus
        /// each object's rounded <c>Position</c>, in authored order. Membership
        /// comes from the client's value rows when available, for the same
        /// notification-ordering reason as <see cref="ProjectTiles"/>.
        /// </summary>
        private static IReadOnlyList<NeoResolvedObjectInstance> ProjectObjects(
            INeoObjectLayerLinkValue link)
        {
            if (link is null) throw new ArgumentNullException(nameof(link));

            var objects = new List<NeoResolvedObjectInstance>();
            if (!TryGetValueRow(link, out var client, out ObjectMemberValue? linkRow))
            {
                return objects;
            }
            string layerId = ReadTargetLayerClassId(
                (NeoGeneratedClassValue)link,
                InternalRecordRelationKinds.WorldObjectLayerLinkTarget);
            var origin = ReadRowOrigin(client!, linkRow!);
            var order = 0;

            foreach (var objectValueId in ReadRowListIds(client!, linkRow!, "Objects"))
            {
                if (!client!.TryGetValue(
                        objectValueId,
                        out ObjectMemberValue? objectRow)
                    || objectRow?.value is null)
                {
                    continue;
                }
                string? assetClassId = ReadDirectReference(
                    objectRow.value,
                    "assetClassId") ?? objectRow.classId;
                if (string.IsNullOrWhiteSpace(assetClassId)) continue;
                var generatedObject = client.ResolveRegisteredGeneratedAsset(
                    assetClassId!,
                    objectValueId);
                if (generatedObject is null) continue;
                var localPosition = ReadRowPosition(client, objectRow);
                var cell = origin + new Vector2Int(
                    Mathf.RoundToInt(localPosition.x),
                    Mathf.RoundToInt(localPosition.y));
                objects.Add(new NeoResolvedObjectInstance(
                    objectValueId,
                    layerId,
                    cell,
                    new[] { cell },
                    generatedObject,
                    order++));
            }

            return objects;
        }

        private static IReadOnlyList<NeoResolvedTileInstance<TTile>> TypedTiles<TTile>(
            IReadOnlyList<NeoResolvedTileInstance> tiles)
            where TTile : class, INeoValueReference
        {
            var typedTiles = new List<NeoResolvedTileInstance<TTile>>();
            foreach (var tile in tiles)
            {
                var typed = tile.As<TTile>();
                if (typed is not null)
                {
                    typedTiles.Add(typed);
                }
            }
            return typedTiles;
        }

        private static IReadOnlyList<NeoResolvedObjectInstance<TObject>> TypedObjects<TObject>(
            IReadOnlyList<NeoResolvedObjectInstance> objects)
            where TObject : class, INeoValueReference
        {
            var typedObjects = new List<NeoResolvedObjectInstance<TObject>>();
            foreach (var obj in objects)
            {
                var typed = obj.As<TObject>();
                if (typed is not null)
                {
                    typedObjects.Add(typed);
                }
            }
            return typedObjects;
        }

        private static bool TryGetValueRow(
            INeoValueReference link,
            out NeoClient? client,
            out ObjectMemberValue? linkRow)
        {
            client = null;
            linkRow = null;
            if (link is not NeoGeneratedClassValue generated) return false;
            if (string.IsNullOrEmpty(generated.valueId)) return false;
            client = generated.Client;
            return client.TryGetValue(generated.valueId, out linkRow) && linkRow?.value is not null;
        }

        private static IReadOnlyList<string> ReadRowListIds(
            NeoClient client,
            ObjectMemberValue linkRow,
            string schemaKey)
        {
            if (linkRow.value is null ||
                !linkRow.value.TryGetValue(schemaKey, out string listValueId) ||
                !client.TryGetValue(listValueId, out ArrayMemberValue? listRow) ||
                listRow?.value is null)
            {
                return Array.Empty<string>();
            }

            // Ordered lists store ids inline. Unordered lists use the
            // containment join; their inline array is only the present/null
            // discriminator.
            var ids = new List<string>(listRow.value);
            var seen = new HashSet<string>(ids);
            foreach (var joinedId in client.GetUnorderedListEntryIds(listValueId))
            {
                if (!seen.Add(joinedId)) continue;
                ids.Add(joinedId);
            }
            return ids;
        }

        private static Vector2Int ReadRowOrigin(NeoClient client, ObjectMemberValue linkRow)
        {
            if (linkRow.value is null ||
                !linkRow.value.TryGetValue("Position", out string positionValueId) ||
                !client.TryGetValue(positionValueId, out Vector3MemberValue? positionRow) ||
                positionRow?.value is null)
            {
                return Vector2Int.zero;
            }

            return new Vector2Int(
                Mathf.RoundToInt(positionRow.value.x),
                Mathf.RoundToInt(positionRow.value.y));
        }

        private static Vector3 ReadRowPosition(
            NeoClient client,
            ObjectMemberValue objectRow)
        {
            if (objectRow.value is null
                || !objectRow.value.TryGetValue("Position", out string positionValueId)
                || !client.TryGetValue(
                    positionValueId,
                    out Vector3MemberValue? positionRow)
                || positionRow?.value is null)
            {
                return Vector3.zero;
            }
            return new Vector3(
                (float)positionRow.value.x,
                (float)positionRow.value.y,
                (float)positionRow.value.z);
        }

        private static Vector2Int? ReadRowCell(NeoClient client, string tileInstanceValueId)
        {
            if (!client.TryGetValue(tileInstanceValueId, out ObjectMemberValue? instanceRow) ||
                instanceRow?.value is null ||
                !instanceRow.value.TryGetValue("Cell", out string cellValueId) ||
                !client.TryGetValue(cellValueId, out Vector2MemberValue? cellRow) ||
                cellRow?.value is null)
            {
                return null;
            }

            return new Vector2Int(
                Mathf.RoundToInt(cellRow.value.x),
                Mathf.RoundToInt(cellRow.value.y));
        }

        private static string? ReadDirectReference(
            IReadOnlyDictionary<string, string> value,
            string key)
        {
            foreach (var pair in value)
            {
                if (!string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                return string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value;
            }
            return null;
        }

        private static string ReadTargetLayerClassId(
            NeoGeneratedClassValue link,
            string relationKind)
        {
            if (string.IsNullOrWhiteSpace(link.classId)) return string.Empty;
            var targets = link.Client.InternalRecordRelations.Resolve(
                relationKind,
                link.classId!);
            return targets.Count == 0 ? string.Empty : targets[0].TargetRecordId;
        }

    }
}
