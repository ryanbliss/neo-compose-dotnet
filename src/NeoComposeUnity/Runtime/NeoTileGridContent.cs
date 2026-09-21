// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public interface INeoTileGridContent
    {
        NeoReadOnlyTileGridPrimitive Primitive { get; }
        IReadOnlyList<IReadOnlyNeoTileLayerRuntime> TileLayersInOrder { get; }
        IReadOnlyList<IReadOnlyNeoObjectLayerRuntime> ObjectLayersInOrder { get; }
        NeoTileGridRenderer? Renderer { get; }
        IDisposable OnChanged(Action<NeoTileGridChangedArgs> handler);
    }

    public interface INeoWritableTileGridContent : INeoTileGridContent
    {
        new NeoTileGridPrimitive Primitive { get; }
    }

    public static class NeoTileGridContentLookupExtensions
    {
        public static TTile? GetTile<TTile>(
            this IReadOnlyNeoTileLayerRuntime layer,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            return layer.GetTile(cell) as TTile;
        }

        public static IReadOnlyList<TTile> GetTiles<TTile>(
            this IReadOnlyNeoTileLayerRuntime layer,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            var tile = layer.GetTile(cell) as TTile;
            return tile is null
                ? Array.Empty<TTile>()
                : new[] { tile };
        }

        public static IReadOnlyList<TTile> GetTiles<TTile>(
            this IReadOnlyNeoTileLayerRuntime layer)
            where TTile : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            var typedTiles = new List<TTile>();
            foreach (var tile in layer.GetTiles())
            {
                var typed = tile as TTile;
                if (typed is not null)
                {
                    typedTiles.Add(typed);
                }
            }
            return typedTiles;
        }

        public static TObject? GetObject<TObject>(
            this IReadOnlyNeoObjectLayerRuntime layer,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            return layer.GetObject(cell) as TObject;
        }

        public static IReadOnlyList<TObject> GetObjects<TObject>(
            this IReadOnlyNeoObjectLayerRuntime layer,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            var typedObjects = new List<TObject>();
            foreach (var obj in layer.GetObjects(cell))
            {
                var typed = obj as TObject;
                if (typed is not null)
                {
                    typedObjects.Add(typed);
                }
            }
            return typedObjects;
        }

        public static IReadOnlyList<TObject> GetObjects<TObject>(
            this IReadOnlyNeoObjectLayerRuntime layer)
            where TObject : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            var typedObjects = new List<TObject>();
            foreach (var obj in layer.GetObjects())
            {
                var typed = obj as TObject;
                if (typed is not null)
                {
                    typedObjects.Add(typed);
                }
            }
            return typedObjects;
        }

        /// <summary>
        /// First placed object in the layer (placement order) whose generated
        /// info is <typeparamref name="TObject"/>, or null when none is
        /// placed — the layer-scoped entry point for singleton-style objects,
        /// e.g. <c>content.Objects.GetObject&lt;PlayerSpawnObject&gt;()</c>.
        /// </summary>
        public static TObject? GetObject<TObject>(
            this IReadOnlyNeoObjectLayerRuntime layer)
            where TObject : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            foreach (var obj in layer.GetObjects())
            {
                var typed = obj as TObject;
                if (typed is not null)
                {
                    return typed;
                }
            }
            return null;
        }

        /// <summary>
        /// First placed object across every object layer (layer order, then
        /// placement order) whose generated info is <typeparamref name="TObject"/>,
        /// or null when none is placed. The grid-wide entry point for
        /// singleton-style objects, e.g.
        /// <c>content.GetObject&lt;PlayerSpawnObject&gt;()</c>.
        /// </summary>
        public static TObject? GetObject<TObject>(
            this INeoTileGridContent content)
            where TObject : class, INeoValueReference
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            foreach (var layer in content.ObjectLayersInOrder)
            {
                foreach (var obj in layer.GetObjects())
                {
                    var typed = obj as TObject;
                    if (typed is not null)
                    {
                        return typed;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Every placed object across every object layer (layer order, then
        /// placement order) whose generated info is <typeparamref name="TObject"/>.
        /// </summary>
        public static IReadOnlyList<TObject> GetObjects<TObject>(
            this INeoTileGridContent content)
            where TObject : class, INeoValueReference
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            var typedObjects = new List<TObject>();
            foreach (var layer in content.ObjectLayersInOrder)
            {
                typedObjects.AddRange(layer.GetObjects<TObject>());
            }
            return typedObjects;
        }

        public static NeoGeneratedClassValue? GetTile(
            this INeoTileGridContent content,
            Vector2Int cell)
        {
            var tiles = content.GetTiles(cell);
            return tiles.Count == 0 ? null : tiles[tiles.Count - 1];
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetTiles(
            this INeoTileGridContent content,
            Vector2Int cell)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            var tiles = new List<NeoGeneratedClassValue>();
            foreach (var layer in content.TileLayersInOrder)
            {
                var tile = layer.GetTile(cell);
                if (tile is not null)
                {
                    tiles.Add(tile);
                }
            }
            return tiles;
        }

        public static TTile? GetTile<TTile>(
            this INeoTileGridContent content,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            var tiles = content.GetTiles<TTile>(cell);
            return tiles.Count == 0 ? null : tiles[tiles.Count - 1];
        }

        public static IReadOnlyList<TTile> GetTiles<TTile>(
            this INeoTileGridContent content,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            var typedTiles = new List<TTile>();
            foreach (var tile in content.GetTiles(cell))
            {
                var typed = tile as TTile;
                if (typed is not null)
                {
                    typedTiles.Add(typed);
                }
            }
            return typedTiles;
        }

        public static NeoGeneratedClassValue? GetTile(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
        {
            var tiles = content.GetTiles(source, cell);
            return tiles.Count == 0 ? null : tiles[tiles.Count - 1];
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetTiles(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            string? sourceValueId = source.valueId;
            if (string.IsNullOrEmpty(sourceValueId))
            {
                return Array.Empty<NeoGeneratedClassValue>();
            }

            var tiles = new List<NeoGeneratedClassValue>();
            foreach (var layer in content.TileLayersInOrder)
            {
                var tile = NeoWorldLayerRuntimeSupport.GetTile(layer, cell);
                if (tile is null || tile.SourceKind != NeoTileOutputSourceKind.TileLayerLink) continue;
                if (!string.Equals(tile.SourceTileLayerLinkId, sourceValueId, StringComparison.Ordinal)) continue;
                tiles.Add(tile.Tile);
            }
            return tiles;
        }

        public static TTile? GetTile<TTile>(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            var tiles = content.GetTiles<TTile>(source, cell);
            return tiles.Count == 0 ? null : tiles[tiles.Count - 1];
        }

        public static IReadOnlyList<TTile> GetTiles<TTile>(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            var typedTiles = new List<TTile>();
            foreach (var tile in content.GetTiles(source, cell))
            {
                var typed = tile as TTile;
                if (typed is not null)
                {
                    typedTiles.Add(typed);
                }
            }
            return typedTiles;
        }

        public static NeoGeneratedClassValue? GetTile(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
        {
            return content.GetTile(source, cell);
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetTiles(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
        {
            return content.GetTiles(source, cell);
        }

        public static TTile? GetTile<TTile>(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            return content.GetTile<TTile>(source, cell);
        }

        public static IReadOnlyList<TTile> GetTiles<TTile>(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
            where TTile : class, INeoValueReference
        {
            return content.GetTiles<TTile>(source, cell);
        }

        public static NeoGeneratedClassValue? GetObject(
            this INeoTileGridContent content,
            Vector2Int cell)
        {
            var objects = content.GetObjects(cell);
            return objects.Count == 0 ? null : objects[objects.Count - 1];
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetObjects(
            this INeoTileGridContent content,
            Vector2Int cell)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            var objects = new List<NeoGeneratedClassValue>();
            foreach (var layer in content.ObjectLayersInOrder)
            {
                objects.AddRange(layer.GetObjects(cell));
            }
            return objects;
        }

        public static TObject? GetObject<TObject>(
            this INeoTileGridContent content,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            var objects = content.GetObjects<TObject>(cell);
            return objects.Count == 0 ? null : objects[objects.Count - 1];
        }

        public static IReadOnlyList<TObject> GetObjects<TObject>(
            this INeoTileGridContent content,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            var typedObjects = new List<TObject>();
            foreach (var obj in content.GetObjects(cell))
            {
                var typed = obj as TObject;
                if (typed is not null)
                {
                    typedObjects.Add(typed);
                }
            }
            return typedObjects;
        }

        public static NeoGeneratedClassValue? GetObject(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
        {
            var objects = content.GetObjects(source, cell);
            return objects.Count == 0 ? null : objects[objects.Count - 1];
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetObjects(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
        {
            if (source is null) throw new ArgumentNullException(nameof(source));
            string? sourceValueId = source.valueId;
            if (string.IsNullOrEmpty(sourceValueId))
            {
                return Array.Empty<NeoGeneratedClassValue>();
            }

            var objects = new List<NeoGeneratedClassValue>();
            foreach (var obj in content.GetObjects(cell))
            {
                if (!string.Equals(obj.valueId, sourceValueId, StringComparison.Ordinal)) continue;
                objects.Add(obj);
            }
            return objects;
        }

        public static TObject? GetObject<TObject>(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            var objects = content.GetObjects<TObject>(source, cell);
            return objects.Count == 0 ? null : objects[objects.Count - 1];
        }

        public static IReadOnlyList<TObject> GetObjects<TObject>(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            var typedObjects = new List<TObject>();
            foreach (var obj in content.GetObjects(source, cell))
            {
                var typed = obj as TObject;
                if (typed is not null)
                {
                    typedObjects.Add(typed);
                }
            }
            return typedObjects;
        }

        public static NeoGeneratedClassValue? GetObject(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
        {
            return content.GetObject(source, cell);
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetObjects(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
        {
            return content.GetObjects(source, cell);
        }

        public static TObject? GetObject<TObject>(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            return content.GetObject<TObject>(source, cell);
        }

        public static IReadOnlyList<TObject> GetObjects<TObject>(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int cell)
            where TObject : class, INeoValueReference
        {
            return content.GetObjects<TObject>(source, cell);
        }

        // --- NeoCellPattern overloads ---
        // Single-result pattern queries return the first match in pattern order, so
        // center-out patterns mean "the nearest match". Multi-result pattern queries
        // concatenate per-cell results in pattern order.

        public static NeoGeneratedClassValue? GetTile(
            this IReadOnlyNeoTileLayerRuntime layer,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            foreach (var cell in pattern.GetCells(origin))
            {
                var tile = layer.GetTile(cell);
                if (tile is not null) return tile;
            }
            return null;
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetTiles(
            this IReadOnlyNeoTileLayerRuntime layer,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var tiles = new List<NeoGeneratedClassValue>();
            foreach (var cell in pattern.GetCells(origin))
            {
                var tile = layer.GetTile(cell);
                if (tile is not null)
                {
                    tiles.Add(tile);
                }
            }
            return tiles;
        }

        public static TTile? GetTile<TTile>(
            this IReadOnlyNeoTileLayerRuntime layer,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TTile : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            foreach (var cell in pattern.GetCells(origin))
            {
                var tile = layer.GetTile<TTile>(cell);
                if (tile is not null) return tile;
            }
            return null;
        }

        public static IReadOnlyList<TTile> GetTiles<TTile>(
            this IReadOnlyNeoTileLayerRuntime layer,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TTile : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var tiles = new List<TTile>();
            foreach (var cell in pattern.GetCells(origin))
            {
                var tile = layer.GetTile<TTile>(cell);
                if (tile is not null)
                {
                    tiles.Add(tile);
                }
            }
            return tiles;
        }

        public static NeoGeneratedClassValue? GetObject(
            this IReadOnlyNeoObjectLayerRuntime layer,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            foreach (var cell in pattern.GetCells(origin))
            {
                var obj = layer.GetObject(cell);
                if (obj is not null) return obj;
            }
            return null;
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetObjects(
            this IReadOnlyNeoObjectLayerRuntime layer,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var objects = new List<NeoGeneratedClassValue>();
            foreach (var cell in pattern.GetCells(origin))
            {
                objects.AddRange(layer.GetObjects(cell));
            }
            return objects;
        }

        public static TObject? GetObject<TObject>(
            this IReadOnlyNeoObjectLayerRuntime layer,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TObject : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            foreach (var cell in pattern.GetCells(origin))
            {
                var typedObjects = layer.GetObjects<TObject>(cell);
                if (typedObjects.Count > 0) return typedObjects[0];
            }
            return null;
        }

        public static IReadOnlyList<TObject> GetObjects<TObject>(
            this IReadOnlyNeoObjectLayerRuntime layer,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TObject : class, INeoValueReference
        {
            if (layer is null) throw new ArgumentNullException(nameof(layer));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var typedObjects = new List<TObject>();
            foreach (var cell in pattern.GetCells(origin))
            {
                typedObjects.AddRange(layer.GetObjects<TObject>(cell));
            }
            return typedObjects;
        }

        public static NeoGeneratedClassValue? GetTile(
            this INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            foreach (var cell in pattern.GetCells(origin))
            {
                var tile = content.GetTile(cell);
                if (tile is not null) return tile;
            }
            return null;
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetTiles(
            this INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var tiles = new List<NeoGeneratedClassValue>();
            foreach (var cell in pattern.GetCells(origin))
            {
                tiles.AddRange(content.GetTiles(cell));
            }
            return tiles;
        }

        public static TTile? GetTile<TTile>(
            this INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TTile : class, INeoValueReference
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            foreach (var cell in pattern.GetCells(origin))
            {
                var tile = content.GetTile<TTile>(cell);
                if (tile is not null) return tile;
            }
            return null;
        }

        public static IReadOnlyList<TTile> GetTiles<TTile>(
            this INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TTile : class, INeoValueReference
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var tiles = new List<TTile>();
            foreach (var cell in pattern.GetCells(origin))
            {
                tiles.AddRange(content.GetTiles<TTile>(cell));
            }
            return tiles;
        }

        public static NeoGeneratedClassValue? GetObject(
            this INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            foreach (var cell in pattern.GetCells(origin))
            {
                var obj = content.GetObject(cell);
                if (obj is not null) return obj;
            }
            return null;
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetObjects(
            this INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var objects = new List<NeoGeneratedClassValue>();
            foreach (var cell in pattern.GetCells(origin))
            {
                objects.AddRange(content.GetObjects(cell));
            }
            return objects;
        }

        public static TObject? GetObject<TObject>(
            this INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TObject : class, INeoValueReference
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            foreach (var cell in pattern.GetCells(origin))
            {
                var typedObjects = content.GetObjects<TObject>(cell);
                if (typedObjects.Count > 0) return typedObjects[0];
            }
            return null;
        }

        public static IReadOnlyList<TObject> GetObjects<TObject>(
            this INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TObject : class, INeoValueReference
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var typedObjects = new List<TObject>();
            foreach (var cell in pattern.GetCells(origin))
            {
                typedObjects.AddRange(content.GetObjects<TObject>(cell));
            }
            return typedObjects;
        }

        public static NeoGeneratedClassValue? GetTile(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            foreach (var cell in pattern.GetCells(origin))
            {
                var tile = content.GetTile(source, cell);
                if (tile is not null) return tile;
            }
            return null;
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetTiles(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var tiles = new List<NeoGeneratedClassValue>();
            foreach (var cell in pattern.GetCells(origin))
            {
                tiles.AddRange(content.GetTiles(source, cell));
            }
            return tiles;
        }

        public static TTile? GetTile<TTile>(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TTile : class, INeoValueReference
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            foreach (var cell in pattern.GetCells(origin))
            {
                var tile = content.GetTile<TTile>(source, cell);
                if (tile is not null) return tile;
            }
            return null;
        }

        public static IReadOnlyList<TTile> GetTiles<TTile>(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TTile : class, INeoValueReference
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var tiles = new List<TTile>();
            foreach (var cell in pattern.GetCells(origin))
            {
                tiles.AddRange(content.GetTiles<TTile>(source, cell));
            }
            return tiles;
        }

        public static NeoGeneratedClassValue? GetObject(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            foreach (var cell in pattern.GetCells(origin))
            {
                var obj = content.GetObject(source, cell);
                if (obj is not null) return obj;
            }
            return null;
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetObjects(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var objects = new List<NeoGeneratedClassValue>();
            foreach (var cell in pattern.GetCells(origin))
            {
                objects.AddRange(content.GetObjects(source, cell));
            }
            return objects;
        }

        public static TObject? GetObject<TObject>(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TObject : class, INeoValueReference
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            foreach (var cell in pattern.GetCells(origin))
            {
                var typedObjects = content.GetObjects<TObject>(source, cell);
                if (typedObjects.Count > 0) return typedObjects[0];
            }
            return null;
        }

        public static IReadOnlyList<TObject> GetObjects<TObject>(
            this INeoTileGridContent content,
            INeoValueReference source,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TObject : class, INeoValueReference
        {
            if (content is null) throw new ArgumentNullException(nameof(content));
            if (pattern is null) throw new ArgumentNullException(nameof(pattern));
            var typedObjects = new List<TObject>();
            foreach (var cell in pattern.GetCells(origin))
            {
                typedObjects.AddRange(content.GetObjects<TObject>(source, cell));
            }
            return typedObjects;
        }

        public static NeoGeneratedClassValue? GetTile(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            return content.GetTile(source, origin, pattern);
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetTiles(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            return content.GetTiles(source, origin, pattern);
        }

        public static TTile? GetTile<TTile>(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TTile : class, INeoValueReference
        {
            return content.GetTile<TTile>(source, origin, pattern);
        }

        public static IReadOnlyList<TTile> GetTiles<TTile>(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TTile : class, INeoValueReference
        {
            return content.GetTiles<TTile>(source, origin, pattern);
        }

        public static NeoGeneratedClassValue? GetObject(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            return content.GetObject(source, origin, pattern);
        }

        public static IReadOnlyList<NeoGeneratedClassValue> GetObjects(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
        {
            return content.GetObjects(source, origin, pattern);
        }

        public static TObject? GetObject<TObject>(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TObject : class, INeoValueReference
        {
            return content.GetObject<TObject>(source, origin, pattern);
        }

        public static IReadOnlyList<TObject> GetObjects<TObject>(
            this INeoValueReference source,
            INeoTileGridContent content,
            Vector2Int origin,
            NeoCellPattern pattern)
            where TObject : class, INeoValueReference
        {
            return content.GetObjects<TObject>(source, origin, pattern);
        }

        /// <summary>
        /// The cell-space bounding box of every tile and object in the content —
        /// e.g. for framing a camera. Position is the minimum occupied cell and
        /// size spans the occupied cells (z flattened to a depth of 1). Size is
        /// zero when the content has no tiles or objects.
        /// </summary>
        public static BoundsInt ComputeCellBounds(this INeoTileGridContent content)
        {
            if (content is null) throw new ArgumentNullException(nameof(content));

            bool hasCells = false;
            int minX = 0, minY = 0, maxX = 0, maxY = 0;

            void Include(Vector2Int cell)
            {
                if (!hasCells)
                {
                    minX = maxX = cell.x;
                    minY = maxY = cell.y;
                    hasCells = true;
                    return;
                }

                minX = Mathf.Min(minX, cell.x);
                minY = Mathf.Min(minY, cell.y);
                maxX = Mathf.Max(maxX, cell.x);
                maxY = Mathf.Max(maxY, cell.y);
            }

            foreach (var layer in content.TileLayersInOrder)
            {
                foreach (var tile in NeoWorldLayerRuntimeSupport.GetRenderSnapshot(layer).Winners)
                {
                    Include(tile.Cell);
                }
            }

            foreach (var layer in content.ObjectLayersInOrder)
            {
                foreach (var obj in NeoWorldLayerRuntimeSupport.GetObjects(layer))
                {
                    if (obj.Footprint.Count == 0)
                    {
                        Include(obj.Cell);
                        continue;
                    }
                    foreach (var cell in obj.Footprint)
                    {
                        Include(cell);
                    }
                }
            }

            if (!hasCells)
            {
                return new BoundsInt();
            }

            return new BoundsInt(
                new Vector3Int(minX, minY, 0),
                new Vector3Int(maxX - minX + 1, maxY - minY + 1, 1));
        }
    }

    public sealed class NeoTileGridChangedArgs
    {
        public NeoTileGridChangedArgs(
            string gridValueId,
            IReadOnlyList<NeoTileLayerChangedArgs>? tileLayers = null,
            IReadOnlyList<NeoObjectLayerChangedArgs>? objectLayers = null,
            NeoChangeSource source = NeoChangeSource.Local)
        {
            GridValueId = gridValueId ?? throw new ArgumentNullException(nameof(gridValueId));
            TileLayers = tileLayers ?? Array.Empty<NeoTileLayerChangedArgs>();
            ObjectLayers = objectLayers ?? Array.Empty<NeoObjectLayerChangedArgs>();
            Source = source;
        }

        public string GridValueId { get; }
        public IReadOnlyList<NeoTileLayerChangedArgs> TileLayers { get; }
        public IReadOnlyList<NeoObjectLayerChangedArgs> ObjectLayers { get; }
        public NeoChangeSource Source { get; }
    }

    public enum NeoTileGridChangeSourceKind
    {
        Direct = 0,
        TileLayerLink = 1,
        ObjectLayer = 2,
    }

    public sealed class NeoTileLayerChangedArgs
    {
        public NeoTileLayerChangedArgs(
            string layerId,
            IReadOnlyList<Vector2Int> cellsToClear,
            IReadOnlyList<Vector2Int> cellsToSetOrRefresh,
            NeoTileGridChangeSourceKind sourceKind,
            string? sourceId)
        {
            LayerId = layerId ?? throw new ArgumentNullException(nameof(layerId));
            CellsToClear = cellsToClear ?? throw new ArgumentNullException(nameof(cellsToClear));
            CellsToSetOrRefresh = cellsToSetOrRefresh ?? throw new ArgumentNullException(nameof(cellsToSetOrRefresh));
            if (CellsToClear.Count == 0 && CellsToSetOrRefresh.Count == 0)
            {
                throw new ArgumentException(
                    "Tile layer changes must include explicit cells to clear or set/refresh.");
            }
            SourceKind = sourceKind;
            SourceId = sourceId;
            ChangedCells = NeoTileGridChangedArgsSupport.UnionCells(
                CellsToClear,
                CellsToSetOrRefresh);
        }

        public string LayerId { get; }
        public IReadOnlyList<Vector2Int> CellsToClear { get; }
        public IReadOnlyList<Vector2Int> CellsToSetOrRefresh { get; }
        public NeoTileGridChangeSourceKind SourceKind { get; }
        public string? SourceId { get; }
        public IReadOnlyList<Vector2Int> ChangedCells { get; }
    }

    public sealed class NeoObjectLayerChangedArgs
    {
        internal bool PositionsOnly { get; set; }
        internal IReadOnlyDictionary<NeoObjectInstanceId, int>? OrderOnlyDeltas { get; set; }
        public NeoObjectLayerChangedArgs(
            string layerId,
            IReadOnlyList<NeoObjectInstanceId> removedInstances,
            IReadOnlyList<NeoObjectInstanceId> addedOrChangedInstances,
            IReadOnlyList<Vector2Int> changedCells,
            NeoTileGridChangeSourceKind sourceKind,
            string? sourceId)
        {
            LayerId = layerId ?? throw new ArgumentNullException(nameof(layerId));
            RemovedInstances = removedInstances ?? throw new ArgumentNullException(nameof(removedInstances));
            AddedOrChangedInstances = addedOrChangedInstances ?? throw new ArgumentNullException(nameof(addedOrChangedInstances));
            ChangedCells = changedCells ?? throw new ArgumentNullException(nameof(changedCells));
            if (RemovedInstances.Count == 0 && AddedOrChangedInstances.Count == 0 && ChangedCells.Count == 0)
            {
                throw new ArgumentException(
                    "Object layer changes must include explicit instances or cells.");
            }
            SourceKind = sourceKind;
            SourceId = sourceId;
            ChangedInstances = NeoTileGridChangedArgsSupport.UnionInstances(
                RemovedInstances,
                AddedOrChangedInstances);
        }

        public string LayerId { get; }
        public IReadOnlyList<NeoObjectInstanceId> RemovedInstances { get; }
        public IReadOnlyList<NeoObjectInstanceId> AddedOrChangedInstances { get; }
        public IReadOnlyList<NeoObjectInstanceId> ChangedInstances { get; }
        public IReadOnlyList<Vector2Int> ChangedCells { get; }
        public NeoTileGridChangeSourceKind SourceKind { get; }
        public string? SourceId { get; }
    }

    internal static class NeoTileGridChangedArgsSupport
    {
        public static IReadOnlyList<Vector2Int> UnionCells(
            IReadOnlyList<Vector2Int> first,
            IReadOnlyList<Vector2Int> second) => Union(first, second);

        public static IReadOnlyList<NeoObjectInstanceId> UnionInstances(
            IReadOnlyList<NeoObjectInstanceId> first,
            IReadOnlyList<NeoObjectInstanceId> second) => Union(first, second);

        private static IReadOnlyList<T> Union<T>(IReadOnlyList<T> first, IReadOnlyList<T> second)
        {
            var result = new List<T>(first.Count + second.Count);
            var seen = new HashSet<T>();
            foreach (var item in first)
                if (seen.Add(item)) result.Add(item);
            foreach (var item in second)
                if (seen.Add(item)) result.Add(item);
            return result;
        }
    }
}
