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
        public static IReadOnlyList<NeoGeneratedClassValue> GetTiles(
            this INeoTileLayerLinkValue link)
        {
            return NeoWorldLayerRuntimeSupport.TileValues(ProjectTiles(link).Winners);
        }

        public static IReadOnlyList<TTile> GetTiles<TTile>(
            this INeoTileLayerLinkValue link)
            where TTile : class, INeoValueReference
        {
            return TypedTiles<TTile>(link.GetTiles());
        }

        public static NeoGeneratedClassValue? GetTile(
            this INeoTileLayerLinkValue link,
            Vector2Int cell)
        {
            ProjectTiles(link).ByCell.TryGetValue(cell, out var tile);
            return tile?.Tile;
        }

        public static TTile? GetTile<TTile>(
            this INeoTileLayerLinkValue link,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            return link.GetTile(cell) as TTile;
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetTiles(
            this INeoTileLayerLinkValue link,
            Vector2Int cell)
        {
            var tile = link.GetTile(cell);
            return tile is null
                ? Array.Empty<NeoGeneratedClassValue>()
                : new[] { tile };
        }

        public static NeoGeneratedClassValue? GetTile(
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
                    return tile?.Tile;
                }
            }
            return null;
        }

        public static TTile? GetTile<TTile>(
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

        public static IReadOnlyList<NeoGeneratedClassValue> GetTiles(
            this INeoTileLayerLinkValue link,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var projection = ProjectTiles(link);
            var tiles = new List<NeoGeneratedClassValue>();
            foreach (var cell in pattern.GetCells(origin))
            {
                if (projection.ByCell.TryGetValue(cell, out var tile))
                {
                    tiles.Add(tile.Tile);
                }
            }
            return tiles;
        }

        public static IReadOnlyList<TTile> GetTiles<TTile>(
            this INeoTileLayerLinkValue link,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TTile : class, INeoValueReference
        {
            return TypedTiles<TTile>(link.GetTiles(origin, pattern));
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetObjects(
            this INeoObjectLayerLinkValue link)
        {
            return NeoWorldLayerRuntimeSupport.ObjectValues(ProjectObjects(link));
        }

        public static IReadOnlyList<TObject> GetObjects<TObject>(
            this INeoObjectLayerLinkValue link)
            where TObject : class, INeoValueReference
        {
            return TypedObjects<TObject>(link.GetObjects());
        }

        public static NeoGeneratedClassValue? GetObject(
            this INeoObjectLayerLinkValue link,
            Vector2Int cell)
        {
            foreach (var obj in ProjectObjects(link))
            {
                if (obj.Cell == cell) return obj.Object;
            }
            return null;
        }

        public static TObject? GetObject<TObject>(
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

        public static IReadOnlyList<NeoGeneratedClassValue> GetObjects(
            this INeoObjectLayerLinkValue link,
            Vector2Int cell)
        {
            var objects = new List<NeoGeneratedClassValue>();
            foreach (var obj in ProjectObjects(link))
            {
                if (obj.Cell == cell)
                {
                    objects.Add(obj.Object);
                }
            }
            return objects;
        }

        public static NeoGeneratedClassValue? GetObject(
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
                    if (obj.Cell == cell) return obj.Object;
                }
            }
            return null;
        }

        public static TObject? GetObject<TObject>(
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

        public static IReadOnlyList<NeoGeneratedClassValue> GetObjects(
            this INeoObjectLayerLinkValue link,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var projection = ProjectObjects(link);
            var objects = new List<NeoGeneratedClassValue>();
            foreach (var cell in pattern.GetCells(origin))
            {
                foreach (var obj in projection)
                {
                    if (obj.Cell == cell)
                    {
                        objects.Add(obj.Object);
                    }
                }
            }
            return objects;
        }

        public static IReadOnlyList<TObject> GetObjects<TObject>(
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
                IReadOnlyList<NeoTileProjection> winners,
                IReadOnlyDictionary<Vector2Int, NeoTileProjection> byCell)
            {
                Winners = winners;
                ByCell = byCell;
            }

            public IReadOnlyList<NeoTileProjection> Winners { get; }
            public IReadOnlyDictionary<Vector2Int, NeoTileProjection> ByCell { get; }
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

            var byCell = new Dictionary<Vector2Int, NeoTileProjection>();
            string sourceId = link.valueId ?? string.Empty;
            if (!TryGetValueRow(link, out var client, out ObjectMemberValue? linkRow))
            {
                return new TileProjection(
                    Array.Empty<NeoTileProjection>(),
                    byCell);
            }
            string layerId = ReadTargetLayerClassId(
                (NeoGeneratedClassValue)link,
                linkRow!,
                isTileLink: true);
            var order = 0;
            var origin = ReadRowOrigin(client!, linkRow!);
            var ownership = ((NeoGeneratedClassValue)link).BackingNode.Get<NeoMemberList>("Tiles").ownership;
            foreach (var instanceValueId in ReadRowListIds(client!, linkRow!, "Tiles"))
            {
                if (!client!.TryGetValue(
                        instanceValueId,
                        out ObjectMemberValue? placement)
                    || placement?.value is null)
                {
                    continue;
                }
                string? assetClassId = placement.classId;
                if (assetClassId is null) continue;
                var tileValue = client.ResolveRegisteredGeneratedAsset(
                    assetClassId,
                    instanceValueId,
                    ownership == NeoValueOwnership.Asset ? null : ownership);
                if (tileValue is null) continue;
                var cell = ReadRowCell(client, instanceValueId);
                if (cell is null) continue;
                var projectedCell = origin + cell.Value;
                byCell[projectedCell] = new NeoTileProjection(
                    instanceValueId,
                    layerId,
                    projectedCell,
                    tileValue,
                    order++,
                    NeoTileOutputSourceKind.TileLayerLink,
                    null,
                    sourceId);
            }

            var winners = new List<NeoTileProjection>(byCell.Values);
            winners.Sort((left, right) => left.Order.CompareTo(right.Order));
            return new TileProjection(winners, byCell);
        }

        /// <summary>
        /// Projects the link's authored objects into grid space: origin plus
        /// each object's rounded <c>Position</c>, in authored order. Membership
        /// comes from the client's value rows when available, for the same
        /// notification-ordering reason as <see cref="ProjectTiles"/>.
        /// </summary>
        private static IReadOnlyList<NeoObjectProjection> ProjectObjects(
            INeoObjectLayerLinkValue link)
        {
            if (link is null) throw new ArgumentNullException(nameof(link));

            var objects = new List<NeoObjectProjection>();
            if (!TryGetValueRow(link, out var client, out ObjectMemberValue? linkRow))
            {
                return objects;
            }
            string layerId = ReadTargetLayerClassId(
                (NeoGeneratedClassValue)link,
                linkRow!,
                isTileLink: false);
            var origin = ReadRowOrigin(client!, linkRow!);
            var order = 0;
            var ownership = ((NeoGeneratedClassValue)link).BackingNode.Get<NeoMemberList>("Objects").ownership;

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
                    assetClassId!, objectValueId, ownership == NeoValueOwnership.Asset ? null : ownership);
                if (generatedObject is null) continue;
                var localPosition = ReadRowPosition(client, objectRow);
                var cell = origin + new Vector2Int(
                    Mathf.RoundToInt(localPosition.x),
                    Mathf.RoundToInt(localPosition.y));
                objects.Add(new NeoObjectProjection(
                    objectValueId,
                    layerId,
                    cell,
                    new[] { cell },
                    generatedObject,
                    order++));
            }

            return objects;
        }

        private static IReadOnlyList<TTile> TypedTiles<TTile>(
            IReadOnlyList<NeoGeneratedClassValue> tiles)
            where TTile : class, INeoValueReference
        {
            var typedTiles = new List<TTile>();
            foreach (var tile in tiles)
            {
                var typed = tile as TTile;
                if (typed is not null)
                {
                    typedTiles.Add(typed);
                }
            }
            return typedTiles;
        }

        private static IReadOnlyList<TObject> TypedObjects<TObject>(
            IReadOnlyList<NeoGeneratedClassValue> objects)
            where TObject : class, INeoValueReference
        {
            var typedObjects = new List<TObject>();
            foreach (var obj in objects)
            {
                var typed = obj as TObject;
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
            if (client.ResolveClassChildRow(linkRow, schemaKey)
                    is not ArrayMemberValue listRow ||
                listRow?.value is null)
            {
                return Array.Empty<string>();
            }
            string listValueId = listRow.id;

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
            if (client.ResolveClassChildRow(linkRow, "Position")
                    is not Vector3MemberValue positionRow ||
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
            if (client.ResolveClassChildRow(objectRow, "Position")
                    is not Vector3MemberValue positionRow
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
                instanceRow is null ||
                client.ResolveClassChildRow(instanceRow, "Cell")
                    is not Vector2MemberValue cellRow ||
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
            ObjectMemberValue linkRow,
            bool isTileLink)
        {
            return NeoWorldLayerLinkResolver.ResolveTargetLayerClassId(
                link.Client,
                link.valueId ?? linkRow.id,
                link.classId,
                isTileLink);
        }

    }

    /// <summary>
    /// Resolves each layer-link target from its declared class relation.
    /// </summary>
    internal static class NeoWorldLayerLinkResolver
    {
        internal static string ResolveTargetLayerClassId(
            NeoClient client,
            string linkValueId,
            string? linkClassId,
            bool isTileLink)
        {
            string label = isTileLink ? "Tile" : "Object";
            if (string.IsNullOrWhiteSpace(linkClassId))
            {
                throw new InvalidOperationException(
                    $"{label} layer link '{linkValueId}' is missing its class id.");
            }

            NeoSchemaClass systemBase = ValidateLinkClass(
                client,
                linkValueId,
                linkClassId!,
                isTileLink);

            string relationKind = isTileLink
                ? InternalRecordRelationKinds.WorldTileLayerLinkTarget
                : InternalRecordRelationKinds.WorldObjectLayerLinkTarget;
            var systemBaseTargets = client.InternalRecordRelations.Resolve(
                relationKind,
                systemBase.id);
            foreach (var target in systemBaseTargets)
            {
                if (!string.Equals(
                    target.DeclaredSourceRecordId,
                    systemBase.id,
                    StringComparison.Ordinal))
                {
                    continue;
                }
                throw new InvalidOperationException(
                    $"{label} layer-link system base '{systemBase.id}' must not declare a '{relationKind}' target relation (relation '{target.RelationIds[0]}'). Declare the target on a project-authored descendant instead.");
            }
            var targets = client.InternalRecordRelations.Resolve(
                relationKind,
                linkClassId!);
            string? relationTargetId = targets.Count == 0
                ? null
                : targets[0].TargetRecordId;
            if (relationTargetId is null)
            {
                throw new InvalidOperationException(
                    $"{label} layer link '{linkValueId}' has no effective '{relationKind}' relation.");
            }
            ValidateTargetClass(client, linkValueId, relationTargetId, isTileLink);
            return relationTargetId;
        }

        private static NeoSchemaClass ValidateLinkClass(
            NeoClient client,
            string linkValueId,
            string linkClassId,
            bool isTileLink)
        {
            string label = isTileLink ? "Tile" : "Object";
            string expectedWorldKind = isTileLink ? "tileLayerLink" : "objectLayerLink";
            if (!client.TryGetClass(linkClassId, out NeoSchemaClass? linkClass))
            {
                throw new InvalidOperationException(
                    $"{label} layer link '{linkValueId}' references missing link class '{linkClassId}'.");
            }
            if (linkClass.Modifier == NeoClassModifierKind.Abstract)
            {
                throw new InvalidOperationException(
                    $"{label} layer link '{linkValueId}' uses abstract link class '{linkClassId}'. Layer-link values require a concrete project-authored class.");
            }

            NeoSchemaClass systemBase = ResolveWorldKindOwner(
                client,
                linkClass,
                linkValueId,
                $"{label} layer link");
            string? actualWorldKind = systemBase.system?["worldKind"]?.ToString();
            if (!string.Equals(actualWorldKind, expectedWorldKind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{label} layer link '{linkValueId}' uses class '{linkClassId}', whose inherited world kind is '{actualWorldKind ?? "<missing>"}' instead of '{expectedWorldKind}'.");
            }
            if (systemBase.Modifier != NeoClassModifierKind.Abstract
                || string.Equals(systemBase.id, linkClassId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{label} layer link '{linkValueId}' uses class '{linkClassId}', which must inherit '{expectedWorldKind}' from an abstract layer-link system base.");
            }
            return systemBase;
        }

        private static void ValidateTargetClass(
            NeoClient client,
            string linkValueId,
            string targetClassId,
            bool isTileLink)
        {
            string label = isTileLink ? "Tile" : "Object";
            string expectedWorldKind = isTileLink ? "tileLayer" : "objectLayer";
            if (!client.TryGetClass(targetClassId, out NeoSchemaClass? targetClass))
            {
                throw new InvalidOperationException(
                    $"{label} layer link '{linkValueId}' targets missing layer class '{targetClassId}'.");
            }
            if (targetClass.Modifier == NeoClassModifierKind.Abstract)
            {
                throw new InvalidOperationException(
                    $"{label} layer link '{linkValueId}' targets abstract layer class '{targetClassId}'.");
            }

            NeoSchemaClass worldKindOwner = ResolveWorldKindOwner(
                client,
                targetClass,
                linkValueId,
                $"{label} layer link target");
            string? actualWorldKind = worldKindOwner.system?["worldKind"]?.ToString();
            if (!string.Equals(actualWorldKind, expectedWorldKind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{label} layer link '{linkValueId}' targets class '{targetClassId}', whose inherited world kind is '{actualWorldKind ?? "<missing>"}' instead of '{expectedWorldKind}'.");
            }
        }

        private static NeoSchemaClass ResolveWorldKindOwner(
            NeoClient client,
            NeoSchemaClass schemaClass,
            string linkValueId,
            string context)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            NeoSchemaClass? cursor = schemaClass;
            NeoSchemaClass? worldKindOwner = null;
            while (cursor is not null)
            {
                if (!visited.Add(cursor.id))
                {
                    throw new InvalidOperationException(
                        $"{context} '{linkValueId}' has a class inheritance cycle at '{cursor.id}'.");
                }
                string? worldKind = cursor.system?["worldKind"]?.ToString();
                if (worldKindOwner is null && !string.IsNullOrWhiteSpace(worldKind))
                {
                    worldKindOwner = cursor;
                }
                if (string.IsNullOrWhiteSpace(cursor.extendsClassId)) break;
                cursor = client.TryGetClass(cursor.extendsClassId!, out NeoSchemaClass? parent)
                    ? parent
                    : null;
            }
            return worldKindOwner ?? schemaClass;
        }

    }
}
