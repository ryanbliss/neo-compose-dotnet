// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NeoCompose.Runtime.Json;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace NeoCompose.Runtime
{
    public sealed class NeoTileGridRenderOptions
    {
        public int MaxTilesPerFrame { get; set; } = 512;

        public int MaxObjectsPerFrame { get; set; } = 8;

        public bool YieldBeforeRender { get; set; } = true;

        public bool LiveSync { get; set; } = true;

        public CancellationToken CancellationToken { get; set; } = default;

        internal int NormalizedMaxTilesPerFrame => Math.Max(1, MaxTilesPerFrame);

        internal int NormalizedMaxObjectsPerFrame => Math.Max(1, MaxObjectsPerFrame);
    }

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
        private const int MaxObjectCompositionDepth = 32;

        private readonly Dictionary<Sprite, Tile> spriteTiles = new();
        private readonly Dictionary<NeoGeneratedCustomValue, TileBase> generatedTileBases = new();
        private readonly Dictionary<string, TileBase> tileBasesByValueId = new();
        private readonly Dictionary<TileBase, NeoGeneratedCustomValue> valuesByTileBase = new();
        private readonly Dictionary<string, Tilemap> tilemapsByLayerId = new();
        private readonly Dictionary<string, Dictionary<Vector2Int, TileBase>> renderedTilesByLayerId = new();
        private readonly Dictionary<string, Dictionary<Vector2Int, int>> tileCandidateCountsByLayerId = new();
        private readonly Dictionary<string, Dictionary<Vector2Int, string>> renderedTileSourceIdsByLayerId = new();
        private readonly Dictionary<NeoTileSourceProjectionKey, Dictionary<Vector2Int, NeoResolvedTileInstance>>
            sourceLinkTilesByLayerAndSource = new();
        private readonly Dictionary<string, ReadOnlyNeoTileLayerRuntime> tileLayersByLayerId = new();
        private readonly Dictionary<string, ReadOnlyNeoObjectLayerRuntime> objectLayersByLayerId = new();
        private readonly Dictionary<string, GameObject> objectLayerRootsByLayerId = new();
        private readonly Dictionary<NeoObjectInstanceId, GameObject> objectRootsByInstanceId = new();
        private readonly Dictionary<string, int> objectLayerFallbackSortingOrdersByLayerId = new();
        private RendererSmartTileNeighborMatcher? smartTileMatcher;
        private NeoTileGridRenderSession? liveSession;
        private INeoTileGridContent? currentContent;
        private NeoReadOnlyTileGridPrimitive? renderedPrimitive;
        private CancellationTokenSource? activeRenderCancellation;

        private readonly struct NeoTileSourceProjectionKey
            : IEquatable<NeoTileSourceProjectionKey>
        {
            public NeoTileSourceProjectionKey(string layerId, string sourceId)
            {
                LayerId = layerId ?? throw new ArgumentNullException(nameof(layerId));
                SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
            }

            public string LayerId { get; }
            public string SourceId { get; }

            public bool Equals(NeoTileSourceProjectionKey other) =>
                LayerId == other.LayerId && SourceId == other.SourceId;

            public override bool Equals(object? obj) =>
                obj is NeoTileSourceProjectionKey other && Equals(other);

            public override int GetHashCode() =>
                HashCode.Combine(LayerId, SourceId);
        }

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

        public bool IsLiveSynced => liveSession != null;

        public INeoTileGridContent? CurrentContent => currentContent;

        public void Render(object generatedGridContent)
        {
            if (generatedGridContent == null)
            {
                throw new ArgumentNullException(nameof(generatedGridContent));
            }

            if (generatedGridContent is not INeoTileGridContent content)
            {
                throw new ArgumentException(
                    "Generated grid content must implement INeoTileGridContent.",
                    nameof(generatedGridContent));
            }

            Render(content);
        }

        public Awaitable RenderAsync(
            object generatedGridContent,
            NeoTileGridRenderOptions? options = null)
        {
            if (generatedGridContent == null)
            {
                throw new ArgumentNullException(nameof(generatedGridContent));
            }

            if (generatedGridContent is not INeoTileGridContent content)
            {
                throw new ArgumentException(
                    "Generated grid content must implement INeoTileGridContent.",
                    nameof(generatedGridContent));
            }

            return RenderAsync(content, options);
        }

        public void Render(INeoTileGridContent content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            CancelInFlightRender();
            StopLiveSync();
            RenderOneShot(
                content.Primitive,
                content.TileLayersInOrder,
                content.ObjectLayersInOrder);
            currentContent = content;
            StartLiveSync(content);
        }

        /// <summary>
        /// Renders the content over multiple frames. A new render on this renderer
        /// supersedes an in-flight one: the newest call wins and the superseded call
        /// observes <see cref="OperationCanceledException"/>, so callers never need
        /// their own generation counters.
        /// </summary>
        public async Awaitable RenderAsync(
            INeoTileGridContent content,
            NeoTileGridRenderOptions? options = null)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            options ??= new NeoTileGridRenderOptions();
            StopLiveSync();
            var renderScope = BeginRenderScope(options.CancellationToken);
            try
            {
                await RenderOneShotAsync(
                    content.Primitive,
                    content.TileLayersInOrder,
                    content.ObjectLayersInOrder,
                    options,
                    renderScope.Token);
                currentContent = content;
                if (options.LiveSync)
                {
                    StartLiveSync(content);
                }
            }
            finally
            {
                EndRenderScope(renderScope);
            }
        }

        public void Render(
            NeoReadOnlyTileGridPrimitive primitive,
            IEnumerable<ReadOnlyNeoTileLayerRuntime> tileLayers,
            IEnumerable<ReadOnlyNeoObjectLayerRuntime>? objectLayers = null)
        {
            CancelInFlightRender();
            StopLiveSync();
            currentContent = null;
            RenderOneShot(primitive, tileLayers, objectLayers);
        }

        private void RenderOneShot(
            NeoReadOnlyTileGridPrimitive primitive,
            IEnumerable<ReadOnlyNeoTileLayerRuntime> tileLayers,
            IEnumerable<ReadOnlyNeoObjectLayerRuntime>? objectLayers = null)
        {
            if (primitive == null) throw new ArgumentNullException(nameof(primitive));
            if (tileLayers == null) throw new ArgumentNullException(nameof(tileLayers));

            renderedPrimitive = primitive;
            var grid = EnsureGrid();
            if (clearBeforeRender)
            {
                ClearChildren(grid.transform);
                ClearRenderedIndexes();
            }

            int sortingOrder = 0;
            foreach (var layer in tileLayers)
            {
                tileLayersByLayerId[layer.LayerId] = layer;
                var tilemap = CreateTilemap(
                    grid.transform,
                    layer,
                    sortingOrder++ * FallbackSortingOrderStride);
                Lifecycle?.OnTileLayerCreated(new NeoTileLayerContext(this, layer, tilemap));
                var positions = new List<Vector3Int>();
                var tiles = new List<TileBase>();
                var renderedTiles = new Dictionary<Vector2Int, TileBase>();
                var snapshot = layer.GetRenderSnapshot();
                CacheTileLayerSnapshot(layer.LayerId, snapshot);
                foreach (var tile in snapshot.Winners)
                {
                    var tileBase = TileBaseFor(tile.Tile);
                    if (tileBase == null) continue;
                    positions.Add(new Vector3Int(tile.Cell.x, tile.Cell.y, 0));
                    tiles.Add(tileBase);
                    renderedTiles[tile.Cell] = tileBase;
                    CacheRenderedTileSource(layer.LayerId, tile);
                }
                renderedTilesByLayerId[layer.LayerId] = renderedTiles;

                if (positions.Count > 0)
                {
                    tilemap.SetTiles(positions.ToArray(), tiles.ToArray());
                    tilemap.CompressBounds();
                }
            }

            if (renderObjects && objectLayers != null)
            {
                foreach (var layer in objectLayers)
                {
                    objectLayersByLayerId[layer.LayerId] = layer;
                    var root = CreateObjectLayerRoot(grid.transform, layer);
                    Lifecycle?.OnObjectLayerCreated(new NeoObjectLayerContext(this, layer, root));
                    var layerFallbackSortingOrder =
                        sortingOrder++ * FallbackSortingOrderStride;
                    objectLayerFallbackSortingOrdersByLayerId[layer.LayerId] =
                        layerFallbackSortingOrder;
                    foreach (var obj in layer.GetObjects())
                    {
                        if (!ShouldRenderObjectInstance(layer, obj)) continue;
                        objectRootsByInstanceId[obj.InstanceId] =
                            SpawnObject(root.transform, layer, obj, layerFallbackSortingOrder);
                    }
                }
            }

            Lifecycle?.OnGridLoaded(new NeoTileGridLoadedContext(this, primitive));
        }

        /// <summary>
        /// Renders the layers over multiple frames. A new render on this renderer
        /// supersedes an in-flight one: the newest call wins and the superseded call
        /// observes <see cref="OperationCanceledException"/>, so callers never need
        /// their own generation counters.
        /// </summary>
        public async Awaitable RenderAsync(
            NeoReadOnlyTileGridPrimitive primitive,
            IEnumerable<ReadOnlyNeoTileLayerRuntime> tileLayers,
            IEnumerable<ReadOnlyNeoObjectLayerRuntime>? objectLayers = null,
            NeoTileGridRenderOptions? options = null)
        {
            options ??= new NeoTileGridRenderOptions();
            StopLiveSync();
            currentContent = null;
            var renderScope = BeginRenderScope(options.CancellationToken);
            try
            {
                await RenderOneShotAsync(primitive, tileLayers, objectLayers, options, renderScope.Token);
            }
            finally
            {
                EndRenderScope(renderScope);
            }
        }

        /// <summary>
        /// Cancels the render scope of any in-flight <c>RenderAsync</c>. The superseded
        /// call still owns (and disposes) its own scope; this only signals it.
        /// </summary>
        private void CancelInFlightRender()
        {
            var inFlight = activeRenderCancellation;
            if (inFlight == null) return;
            activeRenderCancellation = null;
            inFlight.Cancel();
        }

        private CancellationTokenSource BeginRenderScope(CancellationToken callerToken)
        {
            CancelInFlightRender();
            var renderScope = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            activeRenderCancellation = renderScope;
            return renderScope;
        }

        private void EndRenderScope(CancellationTokenSource renderScope)
        {
            if (activeRenderCancellation == renderScope)
            {
                activeRenderCancellation = null;
            }
            renderScope.Dispose();
        }

        private async Awaitable RenderOneShotAsync(
            NeoReadOnlyTileGridPrimitive primitive,
            IEnumerable<ReadOnlyNeoTileLayerRuntime> tileLayers,
            IEnumerable<ReadOnlyNeoObjectLayerRuntime>? objectLayers,
            NeoTileGridRenderOptions options,
            CancellationToken token)
        {
            if (primitive == null) throw new ArgumentNullException(nameof(primitive));
            if (tileLayers == null) throw new ArgumentNullException(nameof(tileLayers));

            renderedPrimitive = primitive;
            token.ThrowIfCancellationRequested();

            if (options.YieldBeforeRender)
            {
                await YieldNextFrameAsync(token);
            }

            var grid = EnsureGrid();
            if (clearBeforeRender)
            {
                ClearChildren(grid.transform);
                ClearRenderedIndexes();
                await YieldNextFrameAsync(token);
            }

            int sortingOrder = 0;
            foreach (var layer in tileLayers)
            {
                token.ThrowIfCancellationRequested();
                tileLayersByLayerId[layer.LayerId] = layer;
                var tilemap = CreateTilemap(
                    grid.transform,
                    layer,
                    sortingOrder++ * FallbackSortingOrderStride);
                var positions = new List<Vector3Int>(options.NormalizedMaxTilesPerFrame);
                var tiles = new List<TileBase>(options.NormalizedMaxTilesPerFrame);
                var renderedTiles = new Dictionary<Vector2Int, TileBase>();
                var snapshot = layer.GetRenderSnapshot();
                CacheTileLayerSnapshot(layer.LayerId, snapshot);
                foreach (var tile in snapshot.Winners)
                {
                    token.ThrowIfCancellationRequested();
                    var tileBase = TileBaseFor(tile.Tile);
                    if (tileBase == null) continue;

                    positions.Add(new Vector3Int(tile.Cell.x, tile.Cell.y, 0));
                    tiles.Add(tileBase);
                    renderedTiles[tile.Cell] = tileBase;
                    CacheRenderedTileSource(layer.LayerId, tile);
                    if (positions.Count < options.NormalizedMaxTilesPerFrame) continue;

                    SetTileBatch(tilemap, positions, tiles);
                    positions.Clear();
                    tiles.Clear();
                    await YieldNextFrameAsync(token);
                }

                if (positions.Count > 0)
                {
                    SetTileBatch(tilemap, positions, tiles);
                }

                tilemap.CompressBounds();
                renderedTilesByLayerId[layer.LayerId] = renderedTiles;
                Lifecycle?.OnTileLayerCreated(new NeoTileLayerContext(this, layer, tilemap));
                await YieldNextFrameAsync(token);
            }

            if (renderObjects && objectLayers != null)
            {
                foreach (var layer in objectLayers)
                {
                    token.ThrowIfCancellationRequested();
                    objectLayersByLayerId[layer.LayerId] = layer;
                    var root = CreateObjectLayerRoot(grid.transform, layer);
                    Lifecycle?.OnObjectLayerCreated(new NeoObjectLayerContext(this, layer, root));
                    var layerFallbackSortingOrder =
                        sortingOrder++ * FallbackSortingOrderStride;
                    objectLayerFallbackSortingOrdersByLayerId[layer.LayerId] =
                        layerFallbackSortingOrder;
                    var renderedThisFrame = 0;
                    foreach (var obj in layer.GetObjects())
                    {
                        token.ThrowIfCancellationRequested();
                        if (!ShouldRenderObjectInstance(layer, obj)) continue;
                        objectRootsByInstanceId[obj.InstanceId] =
                            SpawnObject(root.transform, layer, obj, layerFallbackSortingOrder);
                        renderedThisFrame += 1;
                        if (renderedThisFrame < options.NormalizedMaxObjectsPerFrame) continue;
                        renderedThisFrame = 0;
                        await YieldNextFrameAsync(token);
                    }

                    await YieldNextFrameAsync(token);
                }
            }

            token.ThrowIfCancellationRequested();
            Lifecycle?.OnGridLoaded(new NeoTileGridLoadedContext(this, primitive));
        }

        public void Clear()
        {
            CancelInFlightRender();
            StopLiveSync();
            currentContent = null;
            renderedPrimitive = null;
            ClearChildren(EnsureGrid().transform);
            ClearRenderedIndexes();
            spriteTiles.Clear();
            generatedTileBases.Clear();
            tileBasesByValueId.Clear();
            valuesByTileBase.Clear();
        }

        public void StopLiveSync()
        {
            var session = liveSession;
            if (session == null) return;
            liveSession = null;
            session.Dispose();
        }

        private void OnDestroy()
        {
            CancelInFlightRender();
            StopLiveSync();
        }

        /// <summary>
        /// Looks up the root GameObject this renderer created for an object instance.
        /// False when the instance hasn't been rendered (unknown id, vetoed by
        /// <see cref="NeoTileGridLifecycle.ShouldRenderObject"/>, or already despawned).
        /// </summary>
        public bool TryGetObjectRoot(NeoObjectInstanceId instanceId, out GameObject root)
        {
            if (objectRootsByInstanceId.TryGetValue(instanceId, out var rendered) && rendered != null)
            {
                root = rendered;
                return true;
            }

            root = null!;
            return false;
        }

        private bool ShouldRenderObjectInstance(
            ReadOnlyNeoObjectLayerRuntime layer,
            NeoResolvedObjectInstance instance)
        {
            var lifecycle = Lifecycle;
            if (lifecycle == null) return true;
            return lifecycle.ShouldRenderObject(new NeoObjectRenderContext(this, layer, instance));
        }

        public bool TryClearTile(string layerId, Vector2Int cell)
        {
            if (string.IsNullOrEmpty(layerId)) return false;
            if (!tilemapsByLayerId.TryGetValue(layerId, out var tilemap) || tilemap == null)
            {
                return false;
            }

            var position = new Vector3Int(cell.x, cell.y, 0);
            if (tilemap.GetTile(position) == null) return true;

            if (renderedTilesByLayerId.TryGetValue(layerId, out var renderedTiles))
            {
                renderedTiles.Remove(cell);
            }
            tilemap.SetTile(position, null);
            tilemap.CompressBounds();
            return true;
        }

        private void StartLiveSync(INeoTileGridContent content)
        {
            liveSession = new NeoTileGridRenderSession(this, content);
        }

        private void HandleGridChanged(NeoTileGridChangedArgs args)
        {
            if (currentContent == null) return;
            if (args.GridValueId != currentContent.Primitive.GridValueId) return;

            foreach (var layerChange in args.TileLayers)
            {
                if (!tileLayersByLayerId.TryGetValue(layerChange.LayerId, out var layer))
                {
                    continue;
                }
                ApplyTileLayerDelta(layer, layerChange);
            }

            if (!renderObjects) return;
            foreach (var layerChange in args.ObjectLayers)
            {
                if (!objectLayersByLayerId.TryGetValue(layerChange.LayerId, out var layer))
                {
                    continue;
                }
                ApplyObjectLayerDelta(layer, layerChange);
            }
        }

        private void ApplyTileLayerDelta(
            ReadOnlyNeoTileLayerRuntime layer,
            NeoTileLayerChangedArgs change)
        {
            if (!tilemapsByLayerId.TryGetValue(layer.LayerId, out var tilemap) ||
                tilemap == null)
            {
                return;
            }

            if (!renderedTilesByLayerId.TryGetValue(layer.LayerId, out var renderedTiles))
            {
                renderedTiles = new Dictionary<Vector2Int, TileBase>();
                renderedTilesByLayerId[layer.LayerId] = renderedTiles;
            }

            var touchedCells = new HashSet<Vector2Int>();
            foreach (var cell in change.CellsToClear)
            {
                touchedCells.Add(cell);
                if (TryApplyCachedSourceTileDelta(
                        layer.LayerId,
                        tilemap,
                        renderedTiles,
                        change,
                        cell,
                        isClear: true))
                {
                    continue;
                }
                SetResolvedTileAt(layer, tilemap, renderedTiles, cell);
            }

            foreach (var cell in change.CellsToSetOrRefresh)
            {
                touchedCells.Add(cell);
                if (TryApplyCachedSourceTileDelta(
                        layer.LayerId,
                        tilemap,
                        renderedTiles,
                        change,
                        cell,
                        isClear: false))
                {
                    continue;
                }
                SetResolvedTileAt(layer, tilemap, renderedTiles, cell);
            }
        }

        private bool TryApplyCachedSourceTileDelta(
            string layerId,
            Tilemap tilemap,
            Dictionary<Vector2Int, TileBase> renderedTiles,
            NeoTileLayerChangedArgs change,
            Vector2Int cell,
            bool isClear)
        {
            if (change.SourceKind != NeoTileGridChangeSourceKind.TileLayerLink ||
                string.IsNullOrEmpty(change.SourceId))
            {
                return false;
            }

            int candidateCount = GetTileCandidateCount(layerId, cell);
            if (isClear)
            {
                if (candidateCount > 0) return false;
                SetTileBaseAt(
                    layerId,
                    tilemap,
                    renderedTiles,
                    cell,
                    null,
                    null);
                return true;
            }

            var key = new NeoTileSourceProjectionKey(layerId, change.SourceId!);
            if (!sourceLinkTilesByLayerAndSource.TryGetValue(key, out var sourceTiles) ||
                !sourceTiles.TryGetValue(cell, out var tile))
            {
                return false;
            }
            if (candidateCount > 1) return false;

            SetTileBaseAt(
                layerId,
                tilemap,
                renderedTiles,
                cell,
                TileBaseFor(tile.Tile),
                change.SourceId);
            return true;
        }

        private void SetResolvedTileAt(
            ReadOnlyNeoTileLayerRuntime layer,
            Tilemap tilemap,
            Dictionary<Vector2Int, TileBase> renderedTiles,
            Vector2Int cell)
        {
            var resolved = layer.GetTile(cell);
            TileBase? nextTile = resolved == null ? null : TileBaseFor(resolved.Tile);
            SetTileBaseAt(
                layer.LayerId,
                tilemap,
                renderedTiles,
                cell,
                nextTile,
                resolved?.SourceKind == NeoTileOutputSourceKind.TileLayerLink
                    ? resolved.SourceTileLayerLinkId
                    : null);
        }

        private void SetTileBaseAt(
            string layerId,
            Tilemap tilemap,
            Dictionary<Vector2Int, TileBase> renderedTiles,
            Vector2Int cell,
            TileBase? nextTile,
            string? sourceId)
        {
            var position = new Vector3Int(cell.x, cell.y, 0);
            TileBase? previousTile = renderedTiles.TryGetValue(cell, out var stored)
                ? stored
                : tilemap.GetTile(position);
            if (nextTile == null)
            {
                renderedTiles.Remove(cell);
                if (renderedTileSourceIdsByLayerId.TryGetValue(layerId, out var sourcesByCell))
                {
                    sourcesByCell.Remove(cell);
                }
            }
            else
            {
                renderedTiles[cell] = nextTile;
                if (!string.IsNullOrEmpty(sourceId))
                {
                    if (!renderedTileSourceIdsByLayerId.TryGetValue(layerId, out var sourcesByCell))
                    {
                        sourcesByCell = new Dictionary<Vector2Int, string>();
                        renderedTileSourceIdsByLayerId[layerId] = sourcesByCell;
                    }
                    sourcesByCell[cell] = sourceId!;
                }
                else if (renderedTileSourceIdsByLayerId.TryGetValue(layerId, out var sourcesByCell))
                {
                    sourcesByCell.Remove(cell);
                }
            }
            if (tilemap.GetTile(position) == nextTile) return;
            tilemap.SetTile(position, nextTile);
            RefreshIfSmartTileChanged(tilemap, position, previousTile, nextTile);
        }

        private void ApplyObjectLayerDelta(
            ReadOnlyNeoObjectLayerRuntime layer,
            NeoObjectLayerChangedArgs change)
        {
            if (!objectLayerRootsByLayerId.TryGetValue(layer.LayerId, out var root) ||
                root == null)
            {
                root = CreateObjectLayerRoot(EnsureGrid().transform, layer);
                Lifecycle?.OnObjectLayerCreated(new NeoObjectLayerContext(this, layer, root));
            }

            var fallbackSortingOrder = objectLayerFallbackSortingOrdersByLayerId.TryGetValue(
                layer.LayerId,
                out int storedFallbackSortingOrder)
                    ? storedFallbackSortingOrder
                    : objectLayerFallbackSortingOrdersByLayerId.Count * FallbackSortingOrderStride;
            objectLayerFallbackSortingOrdersByLayerId[layer.LayerId] = fallbackSortingOrder;

            foreach (var instanceId in change.RemovedInstances)
            {
                DestroyRenderedObject(instanceId);
            }

            foreach (var instanceId in change.AddedOrChangedInstances)
            {
                DestroyRenderedObject(instanceId);
                var resolved = layer.GetObject(instanceId);
                if (resolved == null) continue;
                if (!ShouldRenderObjectInstance(layer, resolved)) continue;
                objectRootsByInstanceId[instanceId] =
                    SpawnObject(root.transform, layer, resolved, fallbackSortingOrder);
            }
        }

        private void DestroyRenderedObject(NeoObjectInstanceId instanceId)
        {
            if (!objectRootsByInstanceId.TryGetValue(instanceId, out var root) ||
                root == null)
            {
                objectRootsByInstanceId.Remove(instanceId);
                return;
            }

            objectRootsByInstanceId.Remove(instanceId);
            DestroyCompositionRoot(root);
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

        private void ClearRenderedIndexes()
        {
            tilemapsByLayerId.Clear();
            renderedTilesByLayerId.Clear();
            tileCandidateCountsByLayerId.Clear();
            renderedTileSourceIdsByLayerId.Clear();
            sourceLinkTilesByLayerAndSource.Clear();
            tileLayersByLayerId.Clear();
            objectLayersByLayerId.Clear();
            objectLayerRootsByLayerId.Clear();
            objectRootsByInstanceId.Clear();
            objectLayerFallbackSortingOrdersByLayerId.Clear();
        }

        private void CacheTileLayerSnapshot(
            string layerId,
            NeoTileLayerRenderSnapshot snapshot)
        {
            tileCandidateCountsByLayerId[layerId] =
                new Dictionary<Vector2Int, int>(snapshot.CandidateCountsByCell);

            var sourceIdsToRemove = new List<NeoTileSourceProjectionKey>();
            foreach (var key in sourceLinkTilesByLayerAndSource.Keys)
            {
                if (key.LayerId == layerId) sourceIdsToRemove.Add(key);
            }
            foreach (var key in sourceIdsToRemove)
            {
                sourceLinkTilesByLayerAndSource.Remove(key);
            }

            foreach (var pair in snapshot.TileLayerLinkTilesBySourceId)
            {
                var key = new NeoTileSourceProjectionKey(layerId, pair.Key);
                sourceLinkTilesByLayerAndSource[key] = ToCellMap(pair.Value);
            }
        }

        private void CacheRenderedTileSource(
            string layerId,
            NeoResolvedTileInstance tile)
        {
            if (!renderedTileSourceIdsByLayerId.TryGetValue(layerId, out var sourcesByCell))
            {
                sourcesByCell = new Dictionary<Vector2Int, string>();
                renderedTileSourceIdsByLayerId[layerId] = sourcesByCell;
            }
            if (tile.SourceKind == NeoTileOutputSourceKind.TileLayerLink &&
                !string.IsNullOrEmpty(tile.SourceTileLayerLinkId))
            {
                sourcesByCell[tile.Cell] = tile.SourceTileLayerLinkId!;
            }
            else
            {
                sourcesByCell.Remove(tile.Cell);
            }
        }

        private static Dictionary<Vector2Int, NeoResolvedTileInstance> ToCellMap(
            IEnumerable<NeoResolvedTileInstance> tiles)
        {
            var map = new Dictionary<Vector2Int, NeoResolvedTileInstance>();
            foreach (var tile in tiles)
            {
                map[tile.Cell] = tile;
            }
            return map;
        }

        private Dictionary<Vector2Int, NeoResolvedTileInstance> GetCachedSourceProjection(
            NeoTileSourceProjectionKey key)
        {
            return sourceLinkTilesByLayerAndSource.TryGetValue(key, out var cached)
                ? new Dictionary<Vector2Int, NeoResolvedTileInstance>(cached)
                : new Dictionary<Vector2Int, NeoResolvedTileInstance>();
        }

        private void UpdateCachedSourceProjection(
            NeoTileSourceProjectionKey key,
            Dictionary<Vector2Int, NeoResolvedTileInstance> nextProjection)
        {
            sourceLinkTilesByLayerAndSource.TryGetValue(key, out var previousProjection);
            previousProjection ??= new Dictionary<Vector2Int, NeoResolvedTileInstance>();

            if (!tileCandidateCountsByLayerId.TryGetValue(key.LayerId, out var candidateCounts))
            {
                candidateCounts = new Dictionary<Vector2Int, int>();
                tileCandidateCountsByLayerId[key.LayerId] = candidateCounts;
            }

            foreach (var cell in previousProjection.Keys)
            {
                if (nextProjection.ContainsKey(cell)) continue;
                if (candidateCounts.TryGetValue(cell, out int count) && count > 1)
                {
                    candidateCounts[cell] = count - 1;
                }
                else
                {
                    candidateCounts.Remove(cell);
                }
            }

            foreach (var cell in nextProjection.Keys)
            {
                if (previousProjection.ContainsKey(cell)) continue;
                candidateCounts[cell] = candidateCounts.TryGetValue(cell, out int count)
                    ? count + 1
                    : 1;
            }

            if (nextProjection.Count == 0)
            {
                sourceLinkTilesByLayerAndSource.Remove(key);
                return;
            }
            sourceLinkTilesByLayerAndSource[key] =
                new Dictionary<Vector2Int, NeoResolvedTileInstance>(nextProjection);
        }

        private int GetTileCandidateCount(string layerId, Vector2Int cell)
        {
            return tileCandidateCountsByLayerId.TryGetValue(layerId, out var counts) &&
                counts.TryGetValue(cell, out int count)
                    ? count
                    : 0;
        }

        private IReadOnlyList<NeoTileLayerLinkDependency> GetCachedTileLayerLinkDependencies()
        {
            var dependencies = new List<NeoTileLayerLinkDependency>();
            foreach (var key in sourceLinkTilesByLayerAndSource.Keys)
            {
                dependencies.Add(new NeoTileLayerLinkDependency(
                    key.SourceId,
                    key.LayerId));
            }
            return dependencies;
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
            tilemapsByLayerId[layer.LayerId] = tilemap;
            return tilemap;
        }

        private GameObject CreateObjectLayerRoot(
            Transform parent,
            ReadOnlyNeoObjectLayerRuntime layer)
        {
            var go = new GameObject($"Object Layer - {layer.DisplayName}");
            go.transform.SetParent(parent, false);
            objectLayerRootsByLayerId[layer.LayerId] = go;
            return go;
        }

        private GameObject SpawnObject(
            Transform parent,
            ReadOnlyNeoObjectLayerRuntime layer,
            NeoResolvedObjectInstance instance,
            int layerFallbackSortingOrder)
        {
            var go = new GameObject($"Object - {instance.InstanceId.Value}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = CellToLocalPosition(instance.Cell);

            var sortingOrder =
                (layer.SortingOrder ?? layerFallbackSortingOrder) + instance.Order;
            var renderedChildren = RenderObjectComposition(
                go.transform,
                layer,
                instance.Object,
                sortingOrder,
                new HashSet<string>(),
                0);

            if (TryResolveObjectColliderSpec(instance.Object, out var colliderSpec))
            {
                ApplyBoxCollider(go, colliderSpec);
                if (renderedChildren > 0) return go;
            }

            if (renderedChildren > 0) return go;

            var sprite = ResolveSprite(instance.Object);
            if (sprite == null) return go;

            RenderSpriteChild(
                go.transform,
                layer,
                instance.Object,
                sprite,
                Vector3.zero,
                ReadCellSpan(instance.Object),
                sortingOrder);
            return go;
        }

        private int RenderObjectComposition(
            Transform parent,
            ReadOnlyNeoObjectLayerRuntime layer,
            NeoGeneratedCustomValue value,
            int baseSortingOrder,
            HashSet<string> visitedValueIds,
            int depth)
        {
            if (depth > MaxObjectCompositionDepth) return 0;

            var valueId = value.valueId;
            var hasValueId = !string.IsNullOrEmpty(valueId);
            if (hasValueId && !visitedValueIds.Add(valueId!))
            {
                return 0;
            }

            var rendered = 0;
            var childIndex = 0;
            foreach (var child in ReadEnumerableProperty(value, "Children"))
            {
                rendered += RenderObjectChild(
                    parent,
                    layer,
                    child,
                    baseSortingOrder,
                    childIndex++,
                    visitedValueIds,
                    depth + 1);
            }

            if (hasValueId)
            {
                visitedValueIds.Remove(valueId!);
            }
            return rendered;
        }

        private int RenderObjectChild(
            Transform parent,
            ReadOnlyNeoObjectLayerRuntime layer,
            object child,
            int baseSortingOrder,
            int orderOffset,
            HashSet<string> visitedValueIds,
            int depth)
        {
            if (child == null) return 0;

            var childOffset = ReadCellOffset(child);
            var rendered = RenderTileLayerLinkChild(
                parent,
                layer,
                child,
                childOffset,
                baseSortingOrder + orderOffset);
            if (rendered > 0) return rendered;

            if (child is NeoGeneratedCustomValue generatedChild)
            {
                var childSprite = ResolveSprite(generatedChild);
                if (childSprite != null)
                {
                    RenderSpriteChild(
                        parent,
                        layer,
                        child,
                        childSprite,
                        childOffset,
                        ReadCellSpan(child),
                        baseSortingOrder + orderOffset);
                    return 1;
                }

                var childRoot = new GameObject(ReadObjectName(child, "Object"));
                childRoot.transform.SetParent(parent, false);
                childRoot.transform.localPosition = childOffset;
                var childRendered = RenderObjectComposition(
                    childRoot.transform,
                    layer,
                    generatedChild,
                    baseSortingOrder + orderOffset,
                    visitedValueIds,
                    depth + 1);
                if (childRendered == 0)
                {
                    DestroyCompositionRoot(childRoot);
                }
                return childRendered;
            }

            if (ReadOptionalProperty(child, "Sprite") is Sprite sprite)
            {
                RenderSpriteChild(
                    parent,
                    layer,
                    child,
                    sprite,
                    childOffset,
                    ReadCellSpan(child),
                    baseSortingOrder + orderOffset);
                return 1;
            }

            return rendered;
        }

        private int RenderTileLayerLinkChild(
            Transform parent,
            ReadOnlyNeoObjectLayerRuntime layer,
            object link,
            Vector3 linkOffset,
            int baseSortingOrder)
        {
            var rendered = 0;
            var tileIndex = 0;
            foreach (var tileInstance in ReadEnumerableProperty(link, "Tiles"))
            {
                var rawCell = ReadOptionalProperty(tileInstance, "Cell");
                var cell = NeoGeneratedTypesSupport.ReadVector2IntValue(rawCell);
                if (cell == null) continue;

                var tileValue = ReadOptionalProperty(tileInstance, "Tile")
                    as NeoGeneratedCustomValue;
                if (tileValue == null) continue;

                var sprite = ResolveSprite(tileValue);
                if (sprite == null) continue;

                RenderSpriteChild(
                    parent,
                    layer,
                    tileValue,
                    sprite,
                    linkOffset + CellOffsetToLocalPosition(cell.Value),
                    Vector3.one,
                    baseSortingOrder + tileIndex++);
                rendered++;
            }
            return rendered;
        }

        private void RenderSpriteChild(
            Transform parent,
            ReadOnlyNeoObjectLayerRuntime layer,
            object source,
            Sprite sprite,
            Vector3 localPosition,
            Vector3 cellSpan,
            int sortingOrder)
        {
            var go = new GameObject(ReadObjectName(source, sprite.name));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition + CellSpanCenterOffset(cellSpan);
            ScaleSpriteToCellSpan(go.transform, sprite, cellSpan);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            ApplySorting(renderer, layer.SortingLayerName, sortingOrder);

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
            CellOffsetToLocalPosition(cell);

        private Vector3 CellOffsetToLocalPosition(Vector2Int cell) =>
            new(cell.x * cellSize, cell.y * cellSize, 0f);

        private Vector3 ReadCellOffset(object source)
        {
            var position = NeoGeneratedTypesSupport.ReadVector3Value(
                ReadOptionalProperty(source, "Position"));
            return position == null
                ? Vector3.zero
                : new Vector3(
                    position.Value.x * cellSize,
                    position.Value.y * cellSize,
                    position.Value.z * cellSize);
        }

        private Vector3 ReadCellSpan(object source)
        {
            var size = NeoGeneratedTypesSupport.ReadVector3Value(
                ReadOptionalProperty(source, "Size"));
            if (size == null) return Vector3.one;

            return new Vector3(
                PositiveOrFallback(size.Value.x, 1f),
                PositiveOrFallback(size.Value.y, 1f),
                size.Value.z);
        }

        private Vector3 CellSpanCenterOffset(Vector3 cellSpan) =>
            new(
                PositiveOrFallback(cellSpan.x, 1f) * cellSize * 0.5f,
                PositiveOrFallback(cellSpan.y, 1f) * cellSize * 0.5f,
                0f);

        private void ScaleSpriteToCellSpan(Transform transform, Sprite sprite, Vector3 cellSpan)
        {
            var targetWidth = PositiveOrFallback(cellSpan.x, 1f) * cellSize;
            var targetHeight = PositiveOrFallback(cellSpan.y, 1f) * cellSize;
            var spriteWidth = PositiveOrFallback(sprite.bounds.size.x, 1f);
            var spriteHeight = PositiveOrFallback(sprite.bounds.size.y, 1f);
            transform.localScale = new Vector3(
                targetWidth / spriteWidth,
                targetHeight / spriteHeight,
                1f);
        }

        private static float PositiveOrFallback(float value, float fallback) =>
            value > 0f ? value : fallback;

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

        private void RefreshIfSmartTileChanged(
            Tilemap tilemap,
            Vector3Int position,
            TileBase? previousTile,
            TileBase? nextTile)
        {
            if (RequiresManualRefresh(previousTile) || RequiresManualRefresh(nextTile))
            {
                RefreshTileAndNeighbors(tilemap, position);
                return;
            }

            for (int y = -1; y <= 1; y += 1)
            {
                for (int x = -1; x <= 1; x += 1)
                {
                    var neighborPosition = new Vector3Int(
                        position.x + x,
                        position.y + y,
                        position.z);
                    if (!RequiresManualRefresh(tilemap.GetTile(neighborPosition))) continue;
                    tilemap.RefreshTile(neighborPosition);
                }
            }
        }

        private bool RequiresManualRefresh(TileBase? tileBase)
        {
            return tileBase != null &&
                valuesByTileBase.TryGetValue(tileBase, out var value) &&
                NeoTileAssetFactory.TryResolveSmartTile(value, out _);
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
            var properties = source.GetType().GetProperties(
                BindingFlags.Public | BindingFlags.Instance);
            foreach (var property in properties)
            {
                if (property.GetIndexParameters().Length > 0) continue;
                if (!string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                try
                {
                    return property.GetValue(source);
                }
                catch (TargetInvocationException)
                {
                    return null;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
            return null;
        }

        private static IEnumerable<object> ReadEnumerableProperty(
            object source,
            string propertyName)
        {
            var value = ReadOptionalProperty(source, propertyName);
            if (value is null || value is string) yield break;
            if (value is not IEnumerable enumerable) yield break;
            foreach (var item in enumerable)
            {
                if (item != null) yield return item;
            }
        }

        private static string ReadObjectName(object source, string fallback)
        {
            return ReadOptionalProperty(source, "Name") is string name
                && !string.IsNullOrWhiteSpace(name)
                    ? name
                    : fallback;
        }

        private static void DestroyCompositionRoot(GameObject root)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(root);
                return;
            }
            UnityEngine.Object.DestroyImmediate(root);
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
            NeoRuleTileNeighbor neighbor,
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
                    return MatchesInheritsSmartTileNeighbor(neighbor, other);
                case NeoSmartTileNeighborKind.NotInheritsFromType:
                    return !MatchesInheritsSmartTileNeighbor(neighbor, other);
                default:
                    return false;
            }
        }

        private bool MatchesExactSmartTileNeighbor(
            NeoRuleTileNeighbor neighbor,
            TileBase? other)
        {
            return other != null
                && !string.IsNullOrEmpty(neighbor.TileValueId)
                && TryGetGeneratedValueForTileBase(other, out var value)
                && string.Equals(value.valueId, neighbor.TileValueId, StringComparison.Ordinal);
        }

        private bool MatchesInheritsSmartTileNeighbor(
            NeoRuleTileNeighbor neighbor,
            TileBase? other)
        {
            if (other == null) return false;
            if (string.IsNullOrEmpty(neighbor.TileValueId)) return false;
            if (!TryGetGeneratedValueForTileBase(other, out var otherValue))
            {
                return false;
            }
            var referencedTile = ResolveTileValueById(neighbor.TileValueId!);
            if (referencedTile == null) return false;
            return IsTypeOrSubtype(otherValue, referencedTile.typeId);
        }

        private NeoGeneratedCustomValue? ResolveTileValueById(string tileValueId)
        {
            if (tileBasesByValueId.TryGetValue(tileValueId, out var tileBase)
                && valuesByTileBase.TryGetValue(tileBase, out var cached))
            {
                return cached;
            }
            return renderedPrimitive?.ResolveGeneratedCustomValue(tileValueId);
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
                    UnityEngine.Object.Destroy(child);
                }
                else
                {
                    UnityEngine.Object.DestroyImmediate(child);
                }
            }
        }

        private static void SetTileBatch(
            Tilemap tilemap,
            List<Vector3Int> positions,
            List<TileBase> tiles)
        {
            if (positions.Count == 0) return;
            tilemap.SetTiles(positions.ToArray(), tiles.ToArray());
        }

        private static async Awaitable YieldNextFrameAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (Application.isPlaying)
            {
                await Awaitable.NextFrameAsync(token);
            }
        }

        private static void RefreshTileAndNeighbors(Tilemap tilemap, Vector3Int position)
        {
            for (int y = -1; y <= 1; y += 1)
            {
                for (int x = -1; x <= 1; x += 1)
                {
                    tilemap.RefreshTile(new Vector3Int(
                        position.x + x,
                        position.y + y,
                        position.z));
                }
            }
        }

        private sealed class NeoTileGridRenderSession : IDisposable
        {
            private readonly NeoTileGridRenderer owner;
            private readonly List<IDisposable> subscriptions = new();
            private bool disposed;

            public NeoTileGridRenderSession(
                NeoTileGridRenderer owner,
                INeoTileGridContent content)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
                Content = content ?? throw new ArgumentNullException(nameof(content));
                Content.Primitive.Renderer = owner;

                subscriptions.Add(Content.OnChanged(owner.HandleGridChanged));
                var dependencies = new List<NeoTileLayerLinkDependency>();
                var seenDependencies = new HashSet<string>();
                AddDependencies(Content.Primitive.GetTileLayerLinkDependencies());
                AddDependencies(owner.GetCachedTileLayerLinkDependencies());

                foreach (var dependency in dependencies)
                {
                    var source = Content.Primitive.ResolveGeneratedCustomValue(
                        dependency.SourceValueId);
                    if (source == null) continue;
                    subscriptions.Add(new NeoTileLayerLinkRenderer(
                        owner,
                        Content,
                        dependency,
                        source));
                }

                void AddDependencies(IReadOnlyList<NeoTileLayerLinkDependency> incoming)
                {
                    foreach (var dependency in incoming)
                    {
                        string key = $"{dependency.SourceValueId}\n{dependency.TargetTileLayerId}";
                        if (!seenDependencies.Add(key)) continue;
                        dependencies.Add(dependency);
                    }
                }
            }

            public INeoTileGridContent Content { get; }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                foreach (var subscription in subscriptions)
                {
                    subscription.Dispose();
                }
                subscriptions.Clear();
                if (ReferenceEquals(Content.Primitive.Renderer, owner))
                {
                    Content.Primitive.Renderer = null;
                }
            }
        }

        private sealed class NeoTileLayerLinkRenderer : IDisposable
        {
            private readonly NeoTileGridRenderer owner;
            private readonly INeoTileGridContent content;
            private readonly NeoTileLayerLinkDependency dependency;
            private readonly NeoGeneratedCustomValue source;
            private readonly IDisposable subscription;
            private readonly IDisposable writableValueSubscription;
            private Dictionary<Vector2Int, NeoResolvedTileInstance> previousTilesByCell;
            private bool disposed;

            public NeoTileLayerLinkRenderer(
                NeoTileGridRenderer owner,
                INeoTileGridContent content,
                NeoTileLayerLinkDependency dependency,
                NeoGeneratedCustomValue source)
            {
                this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
                this.content = content ?? throw new ArgumentNullException(nameof(content));
                this.dependency = dependency;
                this.source = source ?? throw new ArgumentNullException(nameof(source));
                var key = new NeoTileSourceProjectionKey(
                    dependency.TargetTileLayerId,
                    dependency.SourceValueId);
                previousTilesByCell = owner.GetCachedSourceProjection(key);
                if (previousTilesByCell.Count == 0)
                {
                    previousTilesByCell = ToCellMap(content.Primitive.GetTileLayerLinkTiles(
                        dependency.TargetTileLayerId,
                        dependency.SourceValueId));
                    owner.UpdateCachedSourceProjection(key, previousTilesByCell);
                }
                subscription = source.WatchAnyChange((_, __, changeSource) =>
                    HandleSourceChanged(changeSource, preferValueRows: false));
                source.Client.OnWritableValueChanged += HandleWritableValueChanged;
                writableValueSubscription = new NeoDisposableSubscription(() =>
                    source.Client.OnWritableValueChanged -= HandleWritableValueChanged);
            }

            private void HandleWritableValueChanged(
                NeoValueOwnership ownership,
                string valueId)
            {
                if (disposed) return;
                if (!IsSourceValueId(valueId)) return;
                HandleSourceChanged(
                    source.Client.CurrentChangeSource,
                    preferValueRows: true);
            }

            private bool IsSourceValueId(string valueId)
            {
                if (string.IsNullOrEmpty(valueId)) return false;
                if (valueId == dependency.SourceValueId || valueId == source.valueId)
                {
                    return true;
                }
                if (string.IsNullOrEmpty(source.valueId) ||
                    !source.Client.TryGetValue(source.valueId!, out ObjectAttributeValue? row) ||
                    row.value is null)
                {
                    return false;
                }
                return IsSourceChildValueId(row, "Tiles", valueId) ||
                    IsSourceChildValueId(row, "TileLayer", valueId) ||
                    IsSourceChildValueId(row, "Position", valueId) ||
                    IsSourceChildValueId(row, "Size", valueId);
            }

            private static bool IsSourceChildValueId(
                ObjectAttributeValue row,
                string key,
                string valueId)
            {
                return row.value is not null &&
                    row.value.TryGetValue(key, out string childValueId) &&
                    childValueId == valueId;
            }

            private void HandleSourceChanged(
                NeoChangeSource changeSource,
                bool preferValueRows)
            {
                if (disposed) return;
                var currentTilesByCell = preferValueRows
                    ? SnapshotSourceTilesFromValueRows()
                    : SnapshotSourceTilesFromWrapper();
                var cellsToClear = new List<Vector2Int>();
                foreach (var cell in previousTilesByCell.Keys)
                {
                    if (currentTilesByCell.ContainsKey(cell)) continue;
                    cellsToClear.Add(cell);
                }

                var cellsToSetOrRefresh = new List<Vector2Int>(
                    currentTilesByCell.Keys);
                previousTilesByCell = currentTilesByCell;
                owner.UpdateCachedSourceProjection(
                    new NeoTileSourceProjectionKey(
                        dependency.TargetTileLayerId,
                        dependency.SourceValueId),
                    currentTilesByCell);
                if (cellsToClear.Count == 0 && cellsToSetOrRefresh.Count == 0)
                {
                    return;
                }

                content.Primitive.NotifyTileLayerChanged(
                    dependency.TargetTileLayerId,
                    cellsToClear,
                    cellsToSetOrRefresh,
                    NeoTileGridChangeSourceKind.TileLayerLink,
                    dependency.SourceValueId,
                    changeSource);
            }

            private Dictionary<Vector2Int, NeoResolvedTileInstance> SnapshotSourceTilesFromWrapper()
            {
                var tiles = new Dictionary<Vector2Int, NeoResolvedTileInstance>();
                var origin = ReadSourceOrigin();
                var order = 0;
                foreach (var tileInstance in ReadEnumerableProperty(source, "Tiles"))
                {
                    var cell = NeoGeneratedTypesSupport.ReadVector2IntValue(
                        ReadOptionalProperty(tileInstance, "Cell"));
                    if (cell == null) continue;
                    var tileValue = ReadOptionalProperty(tileInstance, "Tile")
                        as NeoGeneratedCustomValue;
                    if (tileValue == null) continue;
                    var instanceValue = tileInstance as INeoValueReference;
                    string instanceId = string.IsNullOrEmpty(instanceValue?.valueId)
                        ? $"{dependency.SourceValueId}:{order}"
                        : instanceValue!.valueId!;
                    var projectedCell = origin + cell.Value;
                    tiles[projectedCell] = new NeoResolvedTileInstance(
                        instanceId,
                        dependency.TargetTileLayerId,
                        projectedCell,
                        tileValue,
                        order++,
                        NeoTileOutputSourceKind.TileLayerLink,
                        null,
                        dependency.SourceValueId);
                }
                return tiles;
            }

            private Dictionary<Vector2Int, NeoResolvedTileInstance> SnapshotSourceTilesFromValueRows()
            {
                var tiles = new Dictionary<Vector2Int, NeoResolvedTileInstance>();
                string sourceValueId = source.valueId ?? dependency.SourceValueId;
                if (!source.Client.TryGetValue(sourceValueId, out ObjectAttributeValue? sourceRow) ||
                    sourceRow.value is null ||
                    !sourceRow.value.TryGetValue("Tiles", out string tilesValueId) ||
                    !source.Client.TryGetValue(tilesValueId, out ArrayAttributeValue? tilesRow) ||
                    tilesRow.value is null)
                {
                    return tiles;
                }

                // Inline array ids (ordered lists, legacy factory rows) plus
                // the unordered membership join (rows whose containerId is the
                // Tiles list value).
                var tileInstanceValueIds = new List<string>(tilesRow.value);
                var seenTileInstanceIds = new HashSet<string>(tileInstanceValueIds);
                foreach (var joinedId in source.Client.GetUnorderedListEntryIds(tilesValueId))
                {
                    if (!seenTileInstanceIds.Add(joinedId)) continue;
                    tileInstanceValueIds.Add(joinedId);
                }

                var origin = ReadSourceOrigin(sourceRow);
                var order = 0;
                foreach (var tileInstanceValueId in tileInstanceValueIds)
                {
                    if (!source.Client.TryGetValue(tileInstanceValueId, out ObjectAttributeValue? tileInstanceRow) ||
                        tileInstanceRow.value is null ||
                        !TryReadCell(tileInstanceRow, out Vector2Int localCell) ||
                        !TryReadTileValue(tileInstanceRow, out NeoGeneratedCustomValue? tileValue))
                    {
                        continue;
                    }

                    var projectedCell = origin + localCell;
                    tiles[projectedCell] = new NeoResolvedTileInstance(
                        tileInstanceValueId,
                        dependency.TargetTileLayerId,
                        projectedCell,
                        tileValue!,
                        order++,
                        NeoTileOutputSourceKind.TileLayerLink,
                        null,
                        dependency.SourceValueId);
                }
                return tiles;
            }

            private Vector2Int ReadSourceOrigin()
            {
                var position = NeoGeneratedTypesSupport.ReadVector3Value(
                    ReadOptionalProperty(source, "Position"));
                if (position == null) return Vector2Int.zero;
                return new Vector2Int(
                    Mathf.RoundToInt(position.Value.x),
                    Mathf.RoundToInt(position.Value.y));
            }

            private Vector2Int ReadSourceOrigin(ObjectAttributeValue sourceRow)
            {
                if (sourceRow.value is null ||
                    !sourceRow.value.TryGetValue("Position", out string positionValueId) ||
                    !source.Client.TryGetValue(positionValueId, out Vector3AttributeValue? positionRow) ||
                    positionRow.value is null)
                {
                    return Vector2Int.zero;
                }
                return new Vector2Int(
                    Mathf.RoundToInt(positionRow.value.x),
                    Mathf.RoundToInt(positionRow.value.y));
            }

            private bool TryReadCell(
                ObjectAttributeValue tileInstanceRow,
                out Vector2Int cell)
            {
                cell = default;
                if (tileInstanceRow.value is null ||
                    !tileInstanceRow.value.TryGetValue("Cell", out string cellValueId) ||
                    !source.Client.TryGetValue(cellValueId, out Vector2AttributeValue? cellRow) ||
                    cellRow.value is null)
                {
                    return false;
                }
                cell = new Vector2Int(
                    Mathf.RoundToInt(cellRow.value.x),
                    Mathf.RoundToInt(cellRow.value.y));
                return true;
            }

            private bool TryReadTileValue(
                ObjectAttributeValue tileInstanceRow,
                out NeoGeneratedCustomValue? tile)
            {
                tile = null;
                if (tileInstanceRow.value is null ||
                    !tileInstanceRow.value.TryGetValue("Tile", out string tileLookupValueId) ||
                    !source.Client.TryGetValue(tileLookupValueId, out ArrayAttributeValue? tileLookup) ||
                    tileLookup.value is null ||
                    tileLookup.value.Length == 0)
                {
                    return false;
                }
                tile = content.Primitive.ResolveGeneratedCustomValue(tileLookup.value[0]);
                return tile != null;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                subscription.Dispose();
                writableValueSubscription.Dispose();
                previousTilesByCell.Clear();
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

            public bool Matches(NeoRuleTileNeighbor neighbor, TileBase? other) =>
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
