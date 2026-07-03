// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
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
            string layerId = ReadTargetLayerId(link, "TileLayer");
            var order = 0;

            void Project(Vector2Int projectedCell, string instanceId, NeoGeneratedCustomValue tileValue)
            {
                byCell[projectedCell] = new NeoResolvedTileInstance(
                    instanceId,
                    layerId,
                    projectedCell,
                    tileValue,
                    order++,
                    NeoTileOutputSourceKind.TileLayerLink,
                    null,
                    sourceId);
            }

            if (TryGetValueRow(link, out var client, out ObjectAttributeValue? linkRow))
            {
                var origin = ReadRowOrigin(client!, linkRow!);
                var wrapperItems = SnapshotWrapperTileItems(link);
                foreach (var instanceValueId in ReadRowListIds(client!, linkRow!, "Tiles"))
                {
                    if (!wrapperItems.TryGetValue(instanceValueId, out var item)) continue;
                    var cell = ReadRowCell(client!, instanceValueId) ?? item.Cell;
                    Project(origin + cell, instanceValueId, item.Tile);
                }
            }
            else
            {
                var origin = ReadLinkOrigin(link);
                foreach (var tileInstance in ReadEnumerableProperty(link, "Tiles"))
                {
                    var cell = NeoGeneratedTypesSupport.ReadVector2IntValue(
                        ReadOptionalProperty(tileInstance, "Cell"));
                    if (cell == null) continue;
                    if (ReadOptionalProperty(tileInstance, "Tile") is not NeoGeneratedCustomValue tileValue)
                    {
                        continue;
                    }

                    var instanceValue = tileInstance as INeoValueReference;
                    string instanceId = string.IsNullOrEmpty(instanceValue?.valueId)
                        ? $"{sourceId}:{order}"
                        : instanceValue!.valueId!;
                    Project(origin + cell.Value, instanceId, tileValue);
                }
            }

            var winners = new List<NeoResolvedTileInstance>(byCell.Values);
            winners.Sort((left, right) => left.Order.CompareTo(right.Order));
            return new TileProjection(winners, byCell);
        }

        private readonly struct WrapperTileItem
        {
            public WrapperTileItem(Vector2Int cell, NeoGeneratedCustomValue tile)
            {
                Cell = cell;
                Tile = tile;
            }

            public Vector2Int Cell { get; }
            public NeoGeneratedCustomValue Tile { get; }
        }

        /// <summary>
        /// The wrapper's tile instances keyed by value id — used to resolve
        /// typed tile values for the row-listed members (rows alone can't
        /// resolve generated types without the project's factories).
        /// </summary>
        private static Dictionary<string, WrapperTileItem> SnapshotWrapperTileItems(
            INeoTileLayerLinkValue link)
        {
            var items = new Dictionary<string, WrapperTileItem>();
            foreach (var tileInstance in ReadEnumerableProperty(link, "Tiles"))
            {
                if (tileInstance is not INeoValueReference instanceValue) continue;
                if (string.IsNullOrEmpty(instanceValue.valueId)) continue;
                var cell = NeoGeneratedTypesSupport.ReadVector2IntValue(
                    ReadOptionalProperty(tileInstance, "Cell"));
                if (cell == null) continue;
                if (ReadOptionalProperty(tileInstance, "Tile") is not NeoGeneratedCustomValue tileValue)
                {
                    continue;
                }
                items[instanceValue.valueId!] = new WrapperTileItem(cell.Value, tileValue);
            }
            return items;
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
            string sourceId = link.valueId ?? string.Empty;
            string layerId = ReadTargetLayerId(link, "ObjectLayer");
            var origin = ReadLinkOrigin(link);
            var order = 0;

            void Project(NeoGeneratedCustomValue generatedObject)
            {
                var localPosition = NeoGeneratedTypesSupport.ReadVector3Value(
                    ReadOptionalProperty(generatedObject, "Position")) ?? Vector3.zero;
                var cell = origin + new Vector2Int(
                    Mathf.RoundToInt(localPosition.x),
                    Mathf.RoundToInt(localPosition.y));
                string instanceId = string.IsNullOrEmpty(generatedObject.valueId)
                    ? $"{sourceId}:{order}"
                    : generatedObject.valueId!;
                objects.Add(new NeoResolvedObjectInstance(
                    instanceId,
                    layerId,
                    cell,
                    new[] { cell },
                    generatedObject,
                    order++));
            }

            if (TryGetValueRow(link, out var client, out ObjectAttributeValue? linkRow))
            {
                origin = ReadRowOrigin(client!, linkRow!);
                var wrapperObjects = new Dictionary<string, NeoGeneratedCustomValue>();
                foreach (var objectValue in ReadEnumerableProperty(link, "Objects"))
                {
                    if (objectValue is not NeoGeneratedCustomValue generatedObject) continue;
                    if (string.IsNullOrEmpty(generatedObject.valueId)) continue;
                    wrapperObjects[generatedObject.valueId!] = generatedObject;
                }

                foreach (var objectValueId in ReadRowListIds(client!, linkRow!, "Objects"))
                {
                    if (!wrapperObjects.TryGetValue(objectValueId, out var generatedObject)) continue;
                    Project(generatedObject);
                }
            }
            else
            {
                foreach (var objectValue in ReadEnumerableProperty(link, "Objects"))
                {
                    if (objectValue is not NeoGeneratedCustomValue generatedObject) continue;
                    Project(generatedObject);
                }
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
            out ObjectAttributeValue? linkRow)
        {
            client = null;
            linkRow = null;
            if (link is not NeoGeneratedCustomValue generated) return false;
            if (string.IsNullOrEmpty(generated.valueId)) return false;
            client = generated.Client;
            return client.TryGetValue(generated.valueId, out linkRow) && linkRow?.value is not null;
        }

        private static IReadOnlyList<string> ReadRowListIds(
            NeoClient client,
            ObjectAttributeValue linkRow,
            string schemaKey)
        {
            if (linkRow.value is null ||
                !linkRow.value.TryGetValue(schemaKey, out string listValueId) ||
                !client.TryGetValue(listValueId, out ArrayAttributeValue? listRow) ||
                listRow?.value is null)
            {
                return Array.Empty<string>();
            }

            return listRow.value;
        }

        private static Vector2Int ReadRowOrigin(NeoClient client, ObjectAttributeValue linkRow)
        {
            if (linkRow.value is null ||
                !linkRow.value.TryGetValue("Position", out string positionValueId) ||
                !client.TryGetValue(positionValueId, out Vector3AttributeValue? positionRow) ||
                positionRow?.value is null)
            {
                return Vector2Int.zero;
            }

            return new Vector2Int(
                Mathf.RoundToInt(positionRow.value.x),
                Mathf.RoundToInt(positionRow.value.y));
        }

        private static Vector2Int? ReadRowCell(NeoClient client, string tileInstanceValueId)
        {
            if (!client.TryGetValue(tileInstanceValueId, out ObjectAttributeValue? instanceRow) ||
                instanceRow?.value is null ||
                !instanceRow.value.TryGetValue("Cell", out string cellValueId) ||
                !client.TryGetValue(cellValueId, out Vector2AttributeValue? cellRow) ||
                cellRow?.value is null)
            {
                return null;
            }

            return new Vector2Int(
                Mathf.RoundToInt(cellRow.value.x),
                Mathf.RoundToInt(cellRow.value.y));
        }

        private static Vector2Int ReadLinkOrigin(INeoValueReference link)
        {
            var position = NeoGeneratedTypesSupport.ReadVector3Value(
                ReadOptionalProperty(link, "Position"));
            if (position == null) return Vector2Int.zero;
            return new Vector2Int(
                Mathf.RoundToInt(position.Value.x),
                Mathf.RoundToInt(position.Value.y));
        }

        private static string ReadTargetLayerId(INeoValueReference link, string propertyName)
        {
            return ReadOptionalProperty(link, propertyName) is INeoValueReference layer
                && !string.IsNullOrEmpty(layer.valueId)
                    ? layer.valueId!
                    : string.Empty;
        }

        private static IEnumerable ReadEnumerableProperty(object source, string propertyName)
        {
            return ReadOptionalProperty(source, propertyName) as IEnumerable
                ?? Array.Empty<object>();
        }

        private static object? ReadOptionalProperty(object? source, string propertyName)
        {
            if (source is null) return null;
            var property = source.GetType().GetProperty(propertyName);
            if (property is null || !property.CanRead) return null;
            try
            {
                return property.GetValue(source);
            }
            catch (Exception)
            {
                // Generated getters throw for required members that have no
                // value yet; an unreadable member simply doesn't contribute.
                return null;
            }
        }
    }
}
