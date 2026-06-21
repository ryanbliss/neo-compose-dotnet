// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Default runtime renderer for Neo TileGrid content. It renders winning
    /// tile candidates into Unity Tilemaps and object instances into child
    /// GameObjects with SpriteRenderers. Advanced SmartTile rules, tile-layer
    /// links, and authored collider-shape mapping are layered on top of this
    /// primitive renderer.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeoTileGridRenderer : MonoBehaviour
    {
        [SerializeField]
        private Grid? unityGrid;

        [SerializeField]
        private NeoAssetDatabase? assetDatabase;

        [SerializeField]
        private float cellSize = 1f;

        [SerializeField]
        private bool clearBeforeRender = true;

        [SerializeField]
        private bool renderObjects = true;

        [SerializeField]
        private bool addSpriteBoundsColliders;

        private const int FallbackSortingOrderStride = 1000;

        private readonly Dictionary<Sprite, Tile> spriteTiles = new();
        private readonly Dictionary<NeoGeneratedCustomValue, TileBase> generatedTileBases = new();
        private readonly Dictionary<string, TileBase> tileBasesByValueId = new();
        private readonly Dictionary<TileBase, NeoGeneratedCustomValue> valuesByTileBase = new();
        private RendererSmartTileNeighborMatcher? smartTileMatcher;

        public NeoTileGridLifecycle? Lifecycle { get; set; }

        public Grid UnityGrid => EnsureGrid();

        public NeoAssetDatabase? AssetDatabase
        {
            get => assetDatabase;
            set => assetDatabase = value;
        }

        public float CellSize
        {
            get => cellSize;
            set => cellSize = Mathf.Max(0.0001f, value);
        }

        public bool ClearBeforeRender
        {
            get => clearBeforeRender;
            set => clearBeforeRender = value;
        }

        public bool RenderObjects
        {
            get => renderObjects;
            set => renderObjects = value;
        }

        public bool AddSpriteBoundsColliders
        {
            get => addSpriteBoundsColliders;
            set => addSpriteBoundsColliders = value;
        }

        public void Render(object generatedGridContent)
        {
            if (generatedGridContent == null)
            {
                throw new ArgumentNullException(nameof(generatedGridContent));
            }

            var primitive = ReadProperty<NeoReadOnlyTileGridPrimitive>(
                generatedGridContent,
                "Primitive");
            if (primitive == null)
            {
                throw new ArgumentException(
                    "Generated grid content must expose a Primitive property.",
                    nameof(generatedGridContent));
            }

            Render(
                primitive,
                ReadLayerList<ReadOnlyNeoTileLayerRuntime>(
                    generatedGridContent,
                    "TileLayersInOrder"),
                ReadLayerList<ReadOnlyNeoObjectLayerRuntime>(
                    generatedGridContent,
                    "ObjectLayersInOrder"));
        }

        public void Render(
            NeoReadOnlyTileGridPrimitive primitive,
            IEnumerable<ReadOnlyNeoTileLayerRuntime> tileLayers,
            IEnumerable<ReadOnlyNeoObjectLayerRuntime>? objectLayers = null)
        {
            if (primitive == null) throw new ArgumentNullException(nameof(primitive));
            if (tileLayers == null) throw new ArgumentNullException(nameof(tileLayers));

            var grid = EnsureGrid();
            if (clearBeforeRender) ClearChildren(grid.transform);

            int sortingOrder = 0;
            foreach (var layer in tileLayers)
            {
                var tilemap = CreateTilemap(
                    grid.transform,
                    layer,
                    sortingOrder++ * FallbackSortingOrderStride);
                Lifecycle?.OnTileLayerCreated(new NeoTileLayerContext(this, layer, tilemap));
                foreach (var tile in layer.GetTiles())
                {
                    var tileBase = TileBaseFor(tile.Tile);
                    if (tileBase == null) continue;
                    tilemap.SetTile(
                        new Vector3Int(tile.Cell.x, tile.Cell.y, 0),
                        tileBase);
                }
            }

            if (renderObjects && objectLayers != null)
            {
                foreach (var layer in objectLayers)
                {
                    var root = CreateObjectLayerRoot(grid.transform, layer);
                    Lifecycle?.OnObjectLayerCreated(new NeoObjectLayerContext(this, layer, root));
                    var layerFallbackSortingOrder =
                        sortingOrder++ * FallbackSortingOrderStride;
                    foreach (var obj in layer.GetObjects())
                    {
                        SpawnObject(root.transform, layer, obj, layerFallbackSortingOrder);
                    }
                }
            }

            Lifecycle?.OnGridLoaded(new NeoTileGridLoadedContext(this, primitive));
        }

        public void Clear()
        {
            ClearChildren(EnsureGrid().transform);
            spriteTiles.Clear();
            generatedTileBases.Clear();
            tileBasesByValueId.Clear();
            valuesByTileBase.Clear();
        }

        private Grid EnsureGrid()
        {
            if (unityGrid == null)
            {
                unityGrid = GetComponent<Grid>();
                if (unityGrid == null)
                {
                    unityGrid = gameObject.AddComponent<Grid>();
                }
            }

            unityGrid.cellSize = new Vector3(cellSize, cellSize, 0f);
            return unityGrid;
        }

        private Tilemap CreateTilemap(
            Transform parent,
            ReadOnlyNeoTileLayerRuntime layer,
            int sortingOrder)
        {
            var go = new GameObject($"Tile Layer - {layer.DisplayName}");
            go.transform.SetParent(parent, false);
            var tilemap = go.AddComponent<Tilemap>();
            var renderer = go.AddComponent<TilemapRenderer>();
            ApplySorting(renderer, layer.SortingLayerName, layer.SortingOrder ?? sortingOrder);
            return tilemap;
        }

        private GameObject CreateObjectLayerRoot(
            Transform parent,
            ReadOnlyNeoObjectLayerRuntime layer)
        {
            var go = new GameObject($"Object Layer - {layer.DisplayName}");
            go.transform.SetParent(parent, false);
            return go;
        }

        private void SpawnObject(
            Transform parent,
            ReadOnlyNeoObjectLayerRuntime layer,
            NeoResolvedObjectInstance instance,
            int layerFallbackSortingOrder)
        {
            var go = new GameObject($"Object - {instance.InstanceId.Value}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = CellToLocalPosition(instance.Cell);

            var sprite = ResolveSprite(instance.Object);
            if (sprite == null) return;

            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            ApplySorting(
                renderer,
                layer.SortingLayerName,
                (layer.SortingOrder ?? layerFallbackSortingOrder) + instance.Order);

            if (TryResolveObjectColliderSpec(instance.Object, out var colliderSpec))
            {
                ApplyBoxCollider(go, colliderSpec);
                return;
            }

            if (addSpriteBoundsColliders)
            {
                ApplyBoxCollider(go, new NeoBoxColliderSpec(
                    sprite.bounds.size,
                    sprite.bounds.center,
                    isTrigger: false));
            }
        }

        private static void ApplySorting(
            Renderer renderer,
            string? sortingLayerName,
            int sortingOrder)
        {
            if (!string.IsNullOrWhiteSpace(sortingLayerName))
            {
                renderer.sortingLayerName = sortingLayerName;
            }

            renderer.sortingOrder = sortingOrder;
        }

        private Vector3 CellToLocalPosition(Vector2Int cell) =>
            new(cell.x * cellSize, cell.y * cellSize, 0f);

        private Tile TileForSprite(Sprite sprite)
        {
            if (spriteTiles.TryGetValue(sprite, out var tile)) return tile;
            tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = $"Neo Tile - {sprite.name}";
            tile.sprite = sprite;
            spriteTiles[sprite] = tile;
            return tile;
        }

        private TileBase? TileBaseFor(NeoGeneratedCustomValue value)
        {
            if (generatedTileBases.TryGetValue(value, out var generatedTile))
            {
                return generatedTile;
            }

            string? valueId = value.valueId;
            if (!string.IsNullOrEmpty(valueId)
                && tileBasesByValueId.TryGetValue(valueId!, out var valueTile))
            {
                generatedTileBases[value] = valueTile;
                return valueTile;
            }

            if (!string.IsNullOrEmpty(valueId))
            {
                var databaseTile = assetDatabase?.TryGetTileBase(valueId!);
                if (databaseTile != null)
                {
                    NeoTileAssetFactory.ConfigureRuntimeTileBase(
                        databaseTile,
                        SmartTileMatcher);
                    RegisterTileBase(value, databaseTile);
                    return databaseTile;
                }
            }

            TileBase? tileBase;
            var fallbackSprite = NeoTileAssetFactory.ResolveSprite(value);
            tileBase = NeoTileAssetFactory.TryResolveSmartTile(value, out _)
                ? NeoTileAssetFactory.CreateTransientTileBase(value, SmartTileMatcher)
                : fallbackSprite == null ? null : TileForSprite(fallbackSprite);

            if (tileBase != null)
            {
                RegisterTileBase(value, tileBase);
            }
            return tileBase;
        }

        private RendererSmartTileNeighborMatcher SmartTileMatcher =>
            smartTileMatcher ??= new RendererSmartTileNeighborMatcher(this);

        private void RegisterTileBase(NeoGeneratedCustomValue value, TileBase tileBase)
        {
            generatedTileBases[value] = tileBase;
            string? valueId = value.valueId;
            if (!string.IsNullOrEmpty(valueId))
            {
                tileBasesByValueId[valueId!] = tileBase;
            }
            valuesByTileBase[tileBase] = value;
        }

        private Sprite? ResolveSprite(NeoGeneratedCustomValue value)
        {
            return NeoTileAssetFactory.ResolveSprite(value);
        }

        internal static bool TryResolveObjectColliderSpec(
            object source,
            out NeoBoxColliderSpec spec)
        {
            spec = default;
            if (source == null) return false;

            var collider = ReadOptionalProperty(source, "Collider")
                ?? ReadOptionalProperty(source, "ObjectCollider")
                ?? ReadOptionalProperty(source, "BoxCollider");
            if (collider == null) return false;

            if (!TryReadVector2(collider, "Size", out var size)
                && !TryReadVector2(collider, "Bounds", out size)
                && !TryReadWidthHeight(collider, out size))
            {
                return false;
            }
            if (size.x <= 0f || size.y <= 0f) return false;

            if (!TryReadVector2(collider, "Offset", out var offset))
            {
                var hasOffsetX = TryReadFloat(collider, "OffsetX", out var offsetX);
                var hasOffsetY = TryReadFloat(collider, "OffsetY", out var offsetY);
                offset = hasOffsetX || hasOffsetY
                    ? new Vector2(offsetX, offsetY)
                    : Vector2.zero;
            }

            bool isTrigger = TryReadBool(collider, "IsTrigger", out var value)
                || TryReadBool(collider, "isTrigger", out value)
                ? value
                : false;
            spec = new NeoBoxColliderSpec(size, offset, isTrigger);
            return true;
        }

        private static void ApplyBoxCollider(GameObject target, NeoBoxColliderSpec spec)
        {
            var collider = target.AddComponent<BoxCollider2D>();
            collider.size = spec.Size;
            collider.offset = spec.Offset;
            collider.isTrigger = spec.IsTrigger;
        }

        private static bool TryReadVector2(
            object source,
            string propertyName,
            out Vector2 value)
        {
            value = default;
            var raw = ReadOptionalProperty(source, propertyName);
            var vector = NeoGeneratedTypesSupport.ReadVector2Value(raw);
            if (vector == null) return false;
            value = vector.Value;
            return true;
        }

        private static bool TryReadWidthHeight(object source, out Vector2 size)
        {
            size = default;
            bool hasWidth = TryReadFloat(source, "Width", out var width);
            bool hasHeight = TryReadFloat(source, "Height", out var height);
            if (!hasWidth || !hasHeight) return false;
            size = new Vector2(width, height);
            return true;
        }

        private static bool TryReadFloat(
            object source,
            string propertyName,
            out float value)
        {
            value = 0f;
            var raw = ReadOptionalProperty(source, propertyName);
            if (raw == null) return false;
            switch (raw)
            {
                case float f:
                    value = f;
                    return true;
                case double d:
                    value = (float)d;
                    return true;
                case int i:
                    value = i;
                    return true;
                case long l:
                    value = l;
                    return true;
                case decimal m:
                    value = (float)m;
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryReadBool(
            object source,
            string propertyName,
            out bool value)
        {
            value = false;
            var raw = ReadOptionalProperty(source, propertyName);
            if (raw is not bool boolValue) return false;
            value = boolValue;
            return true;
        }

        private static object? ReadOptionalProperty(object source, string propertyName)
        {
            return NeoTileAssetFactory.ReadOptionalProperty(source, propertyName);
        }

        private static T? ReadProperty<T>(object source, string propertyName)
            where T : class
        {
            var property = source.GetType().GetProperty(
                propertyName,
                BindingFlags.Public | BindingFlags.Instance);
            return property?.GetValue(source) as T;
        }

        private bool MatchesSmartTileNeighbor(
            NeoSmartTileNeighbor neighbor,
            TileBase? other)
        {
            switch (neighbor.Kind)
            {
                case NeoSmartTileNeighborKind.DontCare:
                    return true;
                case NeoSmartTileNeighborKind.ExactTile:
                    return MatchesExactSmartTileNeighbor(neighbor, other);
                case NeoSmartTileNeighborKind.NotExactTile:
                    return !MatchesExactSmartTileNeighbor(neighbor, other);
                case NeoSmartTileNeighborKind.InheritsFromType:
                    return other != null
                        && TryGetGeneratedValueForTileBase(other, out var inheritedValue)
                        && IsTypeOrSubtype(inheritedValue, neighbor.TypeId);
                case NeoSmartTileNeighborKind.NotInheritsFromType:
                    return other == null
                        || !TryGetGeneratedValueForTileBase(other, out var notInheritedValue)
                        || !IsTypeOrSubtype(notInheritedValue, neighbor.TypeId);
                case NeoSmartTileNeighborKind.Tag:
                    return false;
                case NeoSmartTileNeighborKind.NotTag:
                    return true;
                default:
                    return false;
            }
        }

        private bool MatchesExactSmartTileNeighbor(
            NeoSmartTileNeighbor neighbor,
            TileBase? other)
        {
            if (neighbor.Tile != null)
            {
                return TileBaseFor(neighbor.Tile) == other;
            }

            return other != null
                && !string.IsNullOrEmpty(neighbor.TileValueId)
                && TryGetGeneratedValueForTileBase(other, out var value)
                && string.Equals(value.valueId, neighbor.TileValueId, StringComparison.Ordinal);
        }

        private bool TryGetGeneratedValueForTileBase(
            TileBase tileBase,
            out NeoGeneratedCustomValue value)
        {
            if (valuesByTileBase.TryGetValue(tileBase, out value)) return true;
            foreach (var pair in tileBasesByValueId)
            {
                if (!ReferenceEquals(pair.Value, tileBase)) continue;
                foreach (var generatedPair in generatedTileBases)
                {
                    if (!string.Equals(
                        generatedPair.Key.valueId,
                        pair.Key,
                        StringComparison.Ordinal))
                    {
                        continue;
                    }
                    value = generatedPair.Key;
                    valuesByTileBase[tileBase] = value;
                    return true;
                }
            }
            value = null!;
            return false;
        }

        private static bool IsTypeOrSubtype(
            NeoGeneratedCustomValue value,
            string? requiredTypeId)
        {
            if (string.IsNullOrEmpty(requiredTypeId)) return false;
            string? currentTypeId = value.typeId;
            while (!string.IsNullOrEmpty(currentTypeId))
            {
                if (string.Equals(currentTypeId, requiredTypeId, StringComparison.Ordinal))
                {
                    return true;
                }
                if (!value.Client.TryGetType(currentTypeId!, out var type))
                {
                    return false;
                }
                currentTypeId = type.extendsTypeId;
            }
            return false;
        }

        private static IReadOnlyList<T> ReadLayerList<T>(
            object source,
            string propertyName)
            where T : class
        {
            var value = source.GetType()
                .GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
                ?.GetValue(source);
            var result = new List<T>();
            if (value is IEnumerable<T> typed)
            {
                result.AddRange(typed);
                return result;
            }
            if (value is not IEnumerable enumerable) return result;
            foreach (var item in enumerable)
            {
                if (item is T layer) result.Add(layer);
            }
            return result;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int index = parent.childCount - 1; index >= 0; index -= 1)
            {
                var child = parent.GetChild(index).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        private sealed class RendererSmartTileNeighborMatcher
            : INeoSmartTileNeighborMatcher
        {
            private readonly NeoTileGridRenderer owner;

            public RendererSmartTileNeighborMatcher(NeoTileGridRenderer owner)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
            }

            public bool Matches(NeoSmartTileNeighbor neighbor, TileBase? other) =>
                owner.MatchesSmartTileNeighbor(neighbor, other);
        }
    }

    internal readonly struct NeoBoxColliderSpec
    {
        public NeoBoxColliderSpec(Vector2 size, Vector2 offset, bool isTrigger)
        {
            Size = size;
            Offset = offset;
            IsTrigger = isTrigger;
        }

        public Vector2 Size { get; }
        public Vector2 Offset { get; }
        public bool IsTrigger { get; }
    }
}
