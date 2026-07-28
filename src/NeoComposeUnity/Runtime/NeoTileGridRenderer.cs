// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
        private readonly Dictionary<NeoGeneratedClassValue, TileBase> generatedTileBases = new();
        private readonly Dictionary<string, TileBase> tileBasesByValueId = new();
        private readonly Dictionary<string, TileBase> tileBasesByClassId = new();
        private readonly Dictionary<TileBase, NeoGeneratedClassValue> valuesByTileBase = new();
        private readonly Dictionary<string, Tilemap> tilemapsByLayerId = new();
        private readonly Dictionary<string, TileLayerTargetRegistration>
            tileTargetsByLayerId = new();
        private readonly Dictionary<GameObject, TileLayerTargetRegistration>
            tileTargetsByRoot = new(ReferenceComparer<GameObject>.Instance);
        private readonly Dictionary<Tilemap, TileLayerTargetRegistration>
            tileTargetsByTilemap = new(ReferenceComparer<Tilemap>.Instance);
        private readonly Dictionary<string, Dictionary<Vector2Int, TileBase>> renderedTilesByLayerId = new();
        private readonly Dictionary<string, Dictionary<Vector2Int, int>> tileCandidateCountsByLayerId = new();
        private readonly Dictionary<string, Dictionary<Vector2Int, string>> renderedTileSourceIdsByLayerId = new();
        private readonly Dictionary<NeoTileSourceProjectionKey, Dictionary<Vector2Int, NeoResolvedTileInstance>>
            sourceLinkTilesByLayerAndSource = new();
        private readonly Dictionary<string, IReadOnlyNeoTileLayerRuntime> tileLayersByLayerId = new();
        private readonly Dictionary<string, IReadOnlyNeoObjectLayerRuntime> objectLayersByLayerId = new();
        private readonly Dictionary<string, GameObject> objectLayerRootsByLayerId = new();
        private readonly Dictionary<NeoObjectInstanceId, GameObject> objectRootsByInstanceId = new();
        private readonly Dictionary<NeoObjectInstanceId, IDisposable>
            objectPositionSubscriptionsByInstanceId = new();
        // Every GameObject an instance's object graph rendered, paired with the
        // value it came from, so a runtime Enabled write can toggle it without
        // re-walking the composition. A tile-layer-link child contributes one
        // entry per tile because its tiles are bare siblings with no root.
        private readonly Dictionary<NeoObjectInstanceId, List<RenderedObjectVisibility>>
            objectVisibilityByInstanceId = new();
        private readonly Dictionary<string, int> objectLayerFallbackSortingOrdersByLayerId = new();
        private RendererSmartTileNeighborMatcher? smartTileMatcher;
        private NeoTileGridRenderSession? liveSession;
        private INeoTileGridContent? currentContent;
        private NeoReadOnlyTileGridPrimitive? renderedPrimitive;
        private CancellationTokenSource? activeRenderCancellation;
        private List<TileLayerTargetRegistration>? activeRenderTargets;

        /// <summary>
        /// One rendered GameObject and the world object value whose
        /// <see cref="INeoWorldObjectValue.Enabled"/> decides whether it is
        /// active. Deactivating a composition root hides its whole subtree, so
        /// a nested part needs only its own entry.
        /// </summary>
        private readonly struct RenderedObjectVisibility
        {
            public RenderedObjectVisibility(INeoWorldObjectValue value, GameObject gameObject)
            {
                Value = value;
                GameObject = gameObject;
            }

            public INeoWorldObjectValue Value { get; }

            public GameObject GameObject { get; }
        }

        private sealed class TileLayerTargetRegistration
        {
            public TileLayerTargetRegistration(
                NeoTileGridRenderer renderer,
                IReadOnlyNeoTileLayerRuntime layer,
                INeoTileGridContent? content,
                NeoTileLayerRenderTarget target,
                INeoTileLayerRenderTargetProvider? provider)
            {
                Renderer = renderer;
                Layer = layer;
                Content = content;
                Target = target;
                Provider = provider;
            }

            public NeoTileGridRenderer Renderer { get; }
            public IReadOnlyNeoTileLayerRuntime Layer { get; }
            public INeoTileGridContent? Content { get; }
            public NeoTileLayerRenderTarget Target { get; }
            public INeoTileLayerRenderTargetProvider? Provider { get; }
            public bool DidNotifyDestroying { get; set; }
            public bool DidNotifyDestroyed { get; set; }
            public NeoTileLayerRenderTargetDestroyReason DestroyReason { get; set; }
        }

        private sealed class ReferenceComparer<T> : IEqualityComparer<T>
            where T : class
        {
            public static readonly ReferenceComparer<T> Instance = new();

            public bool Equals(T? left, T? right) => ReferenceEquals(left, right);

            public int GetHashCode(T value) => RuntimeHelpers.GetHashCode(value);
        }

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
                content.ObjectLayersInOrder,
                content);
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
            var renderTargets = activeRenderTargets!;
            try
            {
                await RenderOneShotAsync(
                    content.Primitive,
                    content.TileLayersInOrder,
                    content.ObjectLayersInOrder,
                    content,
                    renderTargets,
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
            IEnumerable<IReadOnlyNeoTileLayerRuntime> tileLayers,
            IEnumerable<IReadOnlyNeoObjectLayerRuntime>? objectLayers = null)
        {
            CancelInFlightRender();
            StopLiveSync();
            currentContent = null;
            RenderOneShot(primitive, tileLayers, objectLayers, null);
        }

        private void RenderOneShot(
            NeoReadOnlyTileGridPrimitive primitive,
            IEnumerable<IReadOnlyNeoTileLayerRuntime> tileLayers,
            IEnumerable<IReadOnlyNeoObjectLayerRuntime>? objectLayers,
            INeoTileGridContent? content)
        {
            if (primitive == null) throw new ArgumentNullException(nameof(primitive));
            if (tileLayers == null) throw new ArgumentNullException(nameof(tileLayers));

            renderedPrimitive = primitive;
            var grid = EnsureGrid();
            var createdTargets = new List<TileLayerTargetRegistration>();
            try
            {
                if (clearBeforeRender)
                {
                    DestroyAllTileTargets(NeoTileLayerRenderTargetDestroyReason.Replaced);
                    ClearChildren(grid.transform);
                    ClearRenderedIndexes();
                }

                int sortingOrder = 0;
                foreach (var layer in tileLayers)
                {
                    tileLayersByLayerId[layer.LayerId] = layer;
                    var registration = CreateTileLayerTarget(
                        grid.transform,
                        layer,
                        content,
                        sortingOrder++ * FallbackSortingOrderStride);
                    createdTargets.Add(registration);
                    var tilemap = registration.Target.Tilemap;
                    registration.Provider?.OnRenderTargetCreated(
                        CreateTargetContext(registration));
                    Lifecycle?.OnTileLayerCreated(new NeoTileLayerContext(this, layer, tilemap));
                    var positions = new List<Vector3Int>();
                    var tiles = new List<TileBase>();
                    var renderedTiles = new Dictionary<Vector2Int, TileBase>();
                    var snapshot = NeoWorldLayerRuntimeSupport.GetRenderSnapshot(layer);
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
                    }
                    tilemap.CompressBounds();
                    registration.Provider?.OnInitiallyRendered(
                        CreateTargetContext(registration));
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
            catch
            {
                DestroyTileTargets(
                    createdTargets,
                    NeoTileLayerRenderTargetDestroyReason.RenderCancelled);
                throw;
            }
        }

        /// <summary>
        /// Renders the layers over multiple frames. A new render on this renderer
        /// supersedes an in-flight one: the newest call wins and the superseded call
        /// observes <see cref="OperationCanceledException"/>, so callers never need
        /// their own generation counters.
        /// </summary>
        public async Awaitable RenderAsync(
            NeoReadOnlyTileGridPrimitive primitive,
            IEnumerable<IReadOnlyNeoTileLayerRuntime> tileLayers,
            IEnumerable<IReadOnlyNeoObjectLayerRuntime>? objectLayers = null,
            NeoTileGridRenderOptions? options = null)
        {
            options ??= new NeoTileGridRenderOptions();
            StopLiveSync();
            currentContent = null;
            var renderScope = BeginRenderScope(options.CancellationToken);
            var renderTargets = activeRenderTargets!;
            try
            {
                await RenderOneShotAsync(
                    primitive,
                    tileLayers,
                    objectLayers,
                    null,
                    renderTargets,
                    options,
                    renderScope.Token);
            }
            finally
            {
                EndRenderScope(renderScope);
            }
        }

        /// <summary>
        /// Cancels the render scope of any in-flight <c>RenderAsync</c>. The superseded
        /// call still owns (and disposes) its own scope; targets it already created are
        /// rolled back here before the replacement render begins.
        /// </summary>
        private void CancelInFlightRender()
        {
            var inFlight = activeRenderCancellation;
            if (inFlight == null) return;
            var targets = activeRenderTargets;
            activeRenderCancellation = null;
            activeRenderTargets = null;
            inFlight.Cancel();
            if (targets != null)
            {
                DestroyTileTargets(
                    targets,
                    NeoTileLayerRenderTargetDestroyReason.RenderCancelled);
            }
        }

        private CancellationTokenSource BeginRenderScope(CancellationToken callerToken)
        {
            CancelInFlightRender();
            var renderScope = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
            activeRenderCancellation = renderScope;
            activeRenderTargets = new List<TileLayerTargetRegistration>();
            return renderScope;
        }

        private void EndRenderScope(CancellationTokenSource renderScope)
        {
            if (activeRenderCancellation == renderScope)
            {
                activeRenderCancellation = null;
                activeRenderTargets = null;
            }
            renderScope.Dispose();
        }

        private async Awaitable RenderOneShotAsync(
            NeoReadOnlyTileGridPrimitive primitive,
            IEnumerable<IReadOnlyNeoTileLayerRuntime> tileLayers,
            IEnumerable<IReadOnlyNeoObjectLayerRuntime>? objectLayers,
            INeoTileGridContent? content,
            List<TileLayerTargetRegistration> renderScopeTargets,
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
            var createdTargets = new List<TileLayerTargetRegistration>();
            try
            {
                if (clearBeforeRender)
                {
                    DestroyAllTileTargets(NeoTileLayerRenderTargetDestroyReason.Replaced);
                    ClearChildren(grid.transform);
                    ClearRenderedIndexes();
                    await YieldNextFrameAsync(token);
                }

                int sortingOrder = 0;
                foreach (var layer in tileLayers)
                {
                    token.ThrowIfCancellationRequested();
                    tileLayersByLayerId[layer.LayerId] = layer;
                    var registration = CreateTileLayerTarget(
                        grid.transform,
                        layer,
                        content,
                        sortingOrder++ * FallbackSortingOrderStride);
                    createdTargets.Add(registration);
                    renderScopeTargets.Add(registration);
                    var tilemap = registration.Target.Tilemap;
                    registration.Provider?.OnRenderTargetCreated(
                        CreateTargetContext(registration));
                    Lifecycle?.OnTileLayerCreated(new NeoTileLayerContext(this, layer, tilemap));
                    token.ThrowIfCancellationRequested();

                    var positions = new List<Vector3Int>(options.NormalizedMaxTilesPerFrame);
                    var tiles = new List<TileBase>(options.NormalizedMaxTilesPerFrame);
                    var renderedTiles = new Dictionary<Vector2Int, TileBase>();
                    var snapshot = NeoWorldLayerRuntimeSupport.GetRenderSnapshot(layer);
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
                    registration.Provider?.OnInitiallyRendered(
                        CreateTargetContext(registration));
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
            catch
            {
                DestroyTileTargets(
                    createdTargets,
                    NeoTileLayerRenderTargetDestroyReason.RenderCancelled);
                throw;
            }
        }

        public void Clear()
        {
            CancelInFlightRender();
            StopLiveSync();
            currentContent = null;
            renderedPrimitive = null;
            DestroyAllTileTargets(NeoTileLayerRenderTargetDestroyReason.RendererCleared);
            ClearChildren(EnsureGrid().transform);
            ClearRenderedIndexes();
            spriteTiles.Clear();
            generatedTileBases.Clear();
            tileBasesByValueId.Clear();
            tileBasesByClassId.Clear();
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
            DestroyAllTileTargets(NeoTileLayerRenderTargetDestroyReason.RendererDestroyed);
            DisposeObjectPositionSubscriptions();
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

        /// <summary>
        /// Looks up the root GameObject for the first rendered object whose
        /// generated info is <typeparamref name="TInfo"/> — the presentation-side
        /// counterpart of <c>content.GetObject&lt;TInfo&gt;()</c> (e.g. binding a
        /// camera to the SDK-created PlayerSpawnObject). False when no such
        /// instance is rendered.
        /// </summary>
        public bool TryGetObjectRoot<TInfo>(out GameObject root, out TInfo info)
            where TInfo : class
        {
            var content = currentContent;
            if (content != null)
            {
                foreach (var layer in content.ObjectLayersInOrder)
                {
                    foreach (var instance in layer.GetObjects())
                    {
                        if (instance.Info is not TInfo typed) continue;
                        if (!TryGetObjectRoot(instance.InstanceId, out root)) continue;
                        info = typed;
                        return true;
                    }
                }
            }

            root = null!;
            info = null!;
            return false;
        }

        private bool ShouldRenderObjectInstance(
            IReadOnlyNeoObjectLayerRuntime layer,
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
            IReadOnlyNeoTileLayerRuntime layer,
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

            if (tileTargetsByLayerId.TryGetValue(layer.LayerId, out var registration))
            {
                registration.Provider?.OnRenderTargetChanged(
                    new NeoTileLayerRenderTargetChangedContext(
                        this,
                        layer,
                        registration.Content,
                        registration.Target,
                        change));
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
            IReadOnlyNeoTileLayerRuntime layer,
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
            IReadOnlyNeoObjectLayerRuntime layer,
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
            DisposeObjectPositionSubscription(instanceId);
            objectVisibilityByInstanceId.Remove(instanceId);
            if (!objectRootsByInstanceId.TryGetValue(instanceId, out var root) ||
                root == null)
            {
                objectRootsByInstanceId.Remove(instanceId);
                return;
            }

            objectRootsByInstanceId.Remove(instanceId);
            // Explicit despawn notification while the root is still intact;
            // NeoObjectBehaviour.OnDestroy is only the fallback for destroy
            // paths that bypass the renderer.
            if (root.TryGetComponent(out NeoObjectBehaviour behaviour))
            {
                behaviour.NotifyDespawned();
            }
            DestroyCompositionRoot(root);
        }

        /// <summary>
        /// Keeps a rendered object's transform and visibility in sync with its
        /// value model: runtime writes (e.g. a Session-storage Position on the
        /// player spawn, or an Enabled write that equips a hat) move or reveal
        /// the GameObject, so the Neo data model stays the single source of
        /// truth for placement.
        /// </summary>
        private void TrackObjectPosition(NeoResolvedObjectInstance instance)
        {
            var instanceId = instance.InstanceId;
            var value = instance.Object;
            DisposeObjectPositionSubscription(instanceId);
            objectPositionSubscriptionsByInstanceId[instanceId] = value.WatchAnyChange(
                (changedValue, _, _) =>
                {
                    if (!objectRootsByInstanceId.TryGetValue(instanceId, out var root) ||
                        root == null)
                    {
                        return;
                    }

                    if (changedValue is not INeoWorldObjectValue worldObject) return;
                    root.transform.localPosition =
                        CellOffsetToLocalPosition(worldObject.Position);
                    // Descendant writes bubble to this one root subscription, so
                    // a nested part's Enabled arrives here too. The handler is
                    // told which object changed, not which member, so the whole
                    // instance is reconciled — cheap, because it compares
                    // against activeSelf and only writes on a real difference.
                    SyncObjectVisibility(instanceId);
                });
        }

        /// <summary>
        /// Reapplies every rendered GameObject's active state from the value
        /// that governs it. Deactivating a composition root already hides its
        /// descendants, so a child whose own Enabled is true stays true and is
        /// restored the moment its parent comes back.
        /// <para>
        /// The value model is the single source of truth for visibility, the
        /// same way it already is for Position: calling
        /// <see cref="GameObject.SetActive"/> directly on a renderer-spawned
        /// object is reverted the next time any member on that placement
        /// changes. Write <c>Enabled</c> instead.
        /// </para>
        /// </summary>
        private void SyncObjectVisibility(NeoObjectInstanceId instanceId)
        {
            if (!objectVisibilityByInstanceId.TryGetValue(instanceId, out var visibility))
            {
                return;
            }

            foreach (var entry in visibility)
            {
                var gameObject = entry.GameObject;
                if (gameObject == null) continue;
                var enabled = entry.Value.Enabled;
                if (gameObject.activeSelf == enabled) continue;
                gameObject.SetActive(enabled);
            }
        }

        private void DisposeObjectPositionSubscription(NeoObjectInstanceId instanceId)
        {
            if (!objectPositionSubscriptionsByInstanceId.TryGetValue(
                instanceId, out var subscription))
            {
                return;
            }

            objectPositionSubscriptionsByInstanceId.Remove(instanceId);
            subscription.Dispose();
        }

        private void DisposeObjectPositionSubscriptions()
        {
            foreach (var subscription in objectPositionSubscriptionsByInstanceId.Values)
            {
                subscription.Dispose();
            }
            objectPositionSubscriptionsByInstanceId.Clear();
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
            tileTargetsByLayerId.Clear();
            renderedTilesByLayerId.Clear();
            tileCandidateCountsByLayerId.Clear();
            renderedTileSourceIdsByLayerId.Clear();
            sourceLinkTilesByLayerAndSource.Clear();
            tileLayersByLayerId.Clear();
            objectLayersByLayerId.Clear();
            objectLayerRootsByLayerId.Clear();
            DisposeObjectPositionSubscriptions();
            objectRootsByInstanceId.Clear();
            objectVisibilityByInstanceId.Clear();
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

        private TileLayerTargetRegistration CreateTileLayerTarget(
            Transform parent,
            IReadOnlyNeoTileLayerRuntime layer,
            INeoTileGridContent? content,
            int fallbackSortingOrder)
        {
            if (tileTargetsByLayerId.TryGetValue(layer.LayerId, out var previous))
            {
                DestroyTileTarget(
                    previous,
                    NeoTileLayerRenderTargetDestroyReason.Replaced);
            }

            int effectiveSortingOrder = layer.SortingOrder ?? fallbackSortingOrder;
            var provider = layer as INeoTileLayerRenderTargetProvider;
            var createContext = new NeoTileLayerCreateContext(
                this,
                layer,
                content,
                parent,
                effectiveSortingOrder);
            NeoTileLayerRenderTarget? target = null;
            try
            {
                target = provider?.CreateRenderTarget(createContext);
                target ??= CreateDefaultTileLayerTarget(parent, layer);
                ValidateTileLayerTarget(layer, parent, target);

                var renderer = target.Tilemap.GetComponent<TilemapRenderer>();
                ApplySorting(renderer, layer.SortingLayerName, effectiveSortingOrder);

                var registration = new TileLayerTargetRegistration(
                    this,
                    layer,
                    content,
                    target,
                    provider);
                var lifetime = target.Root.AddComponent<NeoTileLayerRenderTargetLifetime>();
                lifetime.Initialize(() => OnTileLayerTargetDestroyed(registration));

                tileTargetsByLayerId[layer.LayerId] = registration;
                tileTargetsByRoot[target.Root] = registration;
                tileTargetsByTilemap[target.Tilemap] = registration;
                tilemapsByLayerId[layer.LayerId] = target.Tilemap;
                return registration;
            }
            catch
            {
                if (target != null && target.Root != null &&
                    !tileTargetsByRoot.ContainsKey(target.Root))
                {
                    DestroyTargetRoot(target.Root);
                }
                throw;
            }
        }

        private static NeoTileLayerRenderTarget CreateDefaultTileLayerTarget(
            Transform parent,
            IReadOnlyNeoTileLayerRuntime layer)
        {
            var root = new GameObject($"Tile Layer - {layer.DisplayName}");
            root.transform.SetParent(parent, false);
            var tilemap = root.AddComponent<Tilemap>();
            root.AddComponent<TilemapRenderer>();
            return new NeoTileLayerRenderTarget(root, tilemap);
        }

        private void ValidateTileLayerTarget(
            IReadOnlyNeoTileLayerRuntime layer,
            Transform parent,
            NeoTileLayerRenderTarget target)
        {
            string description =
                $"tile layer '{layer.GetType().Name}' (id '{layer.LayerId}')";
            if (target.Root == null)
            {
                throw new InvalidOperationException(
                    $"Render target for {description} has a destroyed root GameObject.");
            }
            if (target.Tilemap == null)
            {
                throw new InvalidOperationException(
                    $"Render target for {description} has a destroyed Tilemap.");
            }
            if (target.Root.transform.parent != parent)
            {
                throw new InvalidOperationException(
                    $"Render target root for {description} must be parented directly beneath " +
                    $"the renderer Grid '{parent.name}'.");
            }
            if (target.Tilemap.transform != target.Root.transform &&
                !target.Tilemap.transform.IsChildOf(target.Root.transform))
            {
                throw new InvalidOperationException(
                    $"Render target Tilemap for {description} must be on the target root or one " +
                    "of its descendants.");
            }
            if (!target.Tilemap.TryGetComponent<TilemapRenderer>(out _))
            {
                throw new InvalidOperationException(
                    $"Render target Tilemap for {description} must have a TilemapRenderer on " +
                    "the same GameObject.");
            }

            if (tileTargetsByTilemap.TryGetValue(target.Tilemap, out var tilemapOwner))
            {
                throw new InvalidOperationException(
                    $"Render target Tilemap for {description} is already registered to tile layer " +
                    $"'{tilemapOwner.Layer.GetType().Name}' (id '{tilemapOwner.Layer.LayerId}').");
            }

            if (tileTargetsByRoot.TryGetValue(target.Root, out var rootOwner))
            {
                throw new InvalidOperationException(
                    $"Render target root for {description} is already registered to tile layer " +
                    $"'{rootOwner.Layer.GetType().Name}' (id '{rootOwner.Layer.LayerId}').");
            }
        }

        private static NeoTileLayerRenderTargetContext CreateTargetContext(
            TileLayerTargetRegistration registration) =>
            new(
                registration.Renderer,
                registration.Layer,
                registration.Content,
                registration.Target);

        private void DestroyAllTileTargets(NeoTileLayerRenderTargetDestroyReason reason)
        {
            DestroyTileTargets(
                new List<TileLayerTargetRegistration>(tileTargetsByLayerId.Values),
                reason);
        }

        private void DestroyTileTargets(
            IEnumerable<TileLayerTargetRegistration> registrations,
            NeoTileLayerRenderTargetDestroyReason reason)
        {
            foreach (var registration in registrations)
            {
                DestroyTileTarget(registration, reason);
            }
        }

        private void DestroyTileTarget(
            TileLayerTargetRegistration registration,
            NeoTileLayerRenderTargetDestroyReason reason)
        {
            NotifyTileLayerTargetDestroying(registration, reason);
            RemoveActiveTileTargetIndexes(registration);
            if (registration.Target.Root != null)
            {
                DestroyTargetRoot(registration.Target.Root);
            }
        }

        private void NotifyTileLayerTargetDestroying(
            TileLayerTargetRegistration registration,
            NeoTileLayerRenderTargetDestroyReason reason)
        {
            if (registration.DidNotifyDestroying) return;
            registration.DidNotifyDestroying = true;
            registration.DestroyReason = reason;
            try
            {
                registration.Provider?.OnRenderTargetDestroying(
                    new NeoTileLayerRenderTargetDestroyContext(
                        this,
                        registration.Layer,
                        registration.Content,
                        registration.Target,
                        reason));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void OnTileLayerTargetDestroyed(TileLayerTargetRegistration registration)
        {
            if (registration.DidNotifyDestroyed) return;
            if (!registration.DidNotifyDestroying)
            {
                NotifyTileLayerTargetDestroying(
                    registration,
                    NeoTileLayerRenderTargetDestroyReason.ExternallyDestroyed);
            }

            registration.DidNotifyDestroyed = true;
            RemoveActiveTileTargetIndexes(registration);
            tileTargetsByRoot.Remove(registration.Target.Root);
            tileTargetsByTilemap.Remove(registration.Target.Tilemap);
            try
            {
                registration.Provider?.OnRenderTargetDestroyed(
                    new NeoTileLayerRenderTargetDestroyedContext(
                        this,
                        registration.Layer,
                        registration.Content,
                        registration.Target,
                        registration.DestroyReason));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }

        private void RemoveActiveTileTargetIndexes(
            TileLayerTargetRegistration registration)
        {
            if (tileTargetsByLayerId.TryGetValue(registration.Layer.LayerId, out var current) &&
                ReferenceEquals(current, registration))
            {
                tileTargetsByLayerId.Remove(registration.Layer.LayerId);
            }
            if (tilemapsByLayerId.TryGetValue(registration.Layer.LayerId, out var tilemap) &&
                ReferenceEquals(tilemap, registration.Target.Tilemap))
            {
                tilemapsByLayerId.Remove(registration.Layer.LayerId);
            }
        }

        private static void DestroyTargetRoot(GameObject root)
        {
            if (root == null) return;
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(root);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private GameObject CreateObjectLayerRoot(
            Transform parent,
            IReadOnlyNeoObjectLayerRuntime layer)
        {
            var go = new GameObject($"Object Layer - {layer.DisplayName}");
            go.transform.SetParent(parent, false);
            objectLayerRootsByLayerId[layer.LayerId] = go;
            return go;
        }

        private GameObject SpawnObject(
            Transform parent,
            IReadOnlyNeoObjectLayerRuntime layer,
            NeoResolvedObjectInstance instance,
            int layerFallbackSortingOrder)
        {
            var go = BuildObjectRoot(parent, layer, instance, layerFallbackSortingOrder);
            // Attached last so the spawn hook observes a fully-built root
            // (composition children, authored collider, sprite fallback) —
            // e.g. an added Rigidbody2D composes with the BoxCollider2D.
            var behaviour = go.AddComponent<NeoObjectBehaviour>();
            behaviour.Initialize(this, layer, instance);
            // Applied only after Initialize, so the spawn hook still sees an
            // active, fully-built root as its contract promises. The root's own
            // collider follows GameObject activity for free.
            SyncObjectVisibility(instance.InstanceId);
            return go;
        }

        private GameObject BuildObjectRoot(
            Transform parent,
            IReadOnlyNeoObjectLayerRuntime layer,
            NeoResolvedObjectInstance instance,
            int layerFallbackSortingOrder)
        {
            var go = new GameObject($"Object - {instance.InstanceId.Value}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = CellToLocalPosition(instance.Cell);
            TrackObjectPosition(instance);

            var visibility = new List<RenderedObjectVisibility>();
            objectVisibilityByInstanceId[instance.InstanceId] = visibility;
            // Registered without applying: the placed root must stay active
            // until the spawn hook has observed it, so SpawnObject applies the
            // whole index once Initialize has run.
            if (instance.Object is INeoWorldObjectValue rootObject)
            {
                visibility.Add(new RenderedObjectVisibility(rootObject, go));
            }

            var sortingOrder =
                (layer.SortingOrder ?? layerFallbackSortingOrder) + instance.Order;
            // Attached before children render so every descendant renderer is
            // parented under a group that already exists.
            AttachSortingGroup(go, layer, instance.Object, sortingOrder);
            var renderedChildren = RenderObjectComposition(
                go.transform,
                layer,
                instance.Object,
                sortingOrder,
                new HashSet<string>(),
                0,
                visibility);

            if (instance.Object is INeoColliderSource colliderSource
                && TryResolveObjectColliderSpec(colliderSource, out var colliderSpec))
            {
                // Authored collider values are grid cells; BoxCollider2D wants
                // local units. Offset needs no re-anchoring — it shares the
                // root transform's origin (the placement cell's corner), the
                // same contract the web editor renders.
                ApplyBoxCollider(go, new NeoBoxColliderSpec(
                    colliderSpec.Size * cellSize,
                    colliderSpec.Offset * cellSize,
                    colliderSpec.IsTrigger));
                if (renderedChildren > 0) return go;
            }

            if (renderedChildren > 0) return go;

            if (instance.Object is not INeoSpriteObjectValue spriteObject) return go;

            RenderSpriteChild(
                go.transform,
                layer,
                spriteObject.Name,
                spriteObject,
                spriteObject.Sprite,
                Vector3.zero,
                CellSpanFromSize(spriteObject.Size),
                sortingOrder);
            return go;
        }

        /// <summary>
        /// Adds a Unity SortingGroup when the value authored one, so the
        /// object and its children sort as a single unit. Sorting layer and
        /// order come from the object layer group, exactly as they do for a
        /// SpriteRenderer, which is what makes the group take the object's
        /// computed position in the layer.
        /// </summary>
        private static void AttachSortingGroup(
            GameObject target,
            IReadOnlyNeoObjectLayerRuntime layer,
            INeoValueReference value,
            int sortingOrder)
        {
            if (value is not INeoSortingGroupSource source) return;
            if (source.SortingGroup is not { } group) return;

            var sortingGroup = target.AddComponent<UnityEngine.Rendering.SortingGroup>();
            sortingGroup.sortAtRoot = group.SortAtRoot;
            ApplySorting(sortingGroup, layer.SortingLayerName, sortingOrder);
        }

        private int RenderObjectComposition(
            Transform parent,
            IReadOnlyNeoObjectLayerRuntime layer,
            INeoValueReference value,
            int baseSortingOrder,
            HashSet<string> visitedValueIds,
            int depth,
            List<RenderedObjectVisibility> visibility)
        {
            if (depth > MaxObjectCompositionDepth) return 0;
            if (value is not INeoObjectCompositionSource composition) return 0;

            var valueId = value.valueId;
            var hasValueId = !string.IsNullOrEmpty(valueId);
            if (hasValueId && !visitedValueIds.Add(valueId!))
            {
                return 0;
            }

            var rendered = 0;
            var childIndex = 0;
            foreach (var child in composition.Children)
            {
                if (child == null) continue;
                rendered += RenderObjectChild(
                    parent,
                    layer,
                    child,
                    baseSortingOrder,
                    childIndex++,
                    visitedValueIds,
                    depth + 1,
                    visibility);
            }

            if (hasValueId)
            {
                visitedValueIds.Remove(valueId!);
            }
            return rendered;
        }

        private int RenderObjectChild(
            Transform parent,
            IReadOnlyNeoObjectLayerRuntime layer,
            INeoWorldObjectValue child,
            int baseSortingOrder,
            int orderOffset,
            HashSet<string> visitedValueIds,
            int depth,
            List<RenderedObjectVisibility> visibility)
        {
            var childOffset = CellOffsetToLocalPosition(child.Position);
            var childSortingOrder = baseSortingOrder + orderOffset;
            var rendered = RenderTileLayerLinkChild(
                parent,
                layer,
                child,
                childOffset,
                childSortingOrder,
                visibility);
            if (rendered > 0) return rendered;

            if (child is INeoSpriteObjectValue spriteChild)
            {
                var spriteGo = RenderSpriteChild(
                    parent,
                    layer,
                    spriteChild.Name,
                    spriteChild,
                    spriteChild.Sprite,
                    childOffset,
                    CellSpanFromSize(spriteChild.Size),
                    childSortingOrder);
                RegisterVisibility(visibility, child, spriteGo);
                return 1;
            }

            var childRoot = new GameObject(
                string.IsNullOrWhiteSpace(child.Name) ? "Object" : child.Name);
            childRoot.transform.SetParent(parent, false);
            childRoot.transform.localPosition = childOffset;
            AttachSortingGroup(childRoot, layer, child, childSortingOrder);
            // The subtree is built even when the child is disabled, so a
            // runtime write can toggle it back on and so a clip playing through
            // it keeps resolving. Deactivation happens after, in
            // RegisterVisibility.
            var childRendered = RenderObjectComposition(
                childRoot.transform,
                layer,
                child,
                childSortingOrder,
                visitedValueIds,
                depth + 1,
                visibility);
            if (childRendered == 0)
            {
                DestroyCompositionRoot(childRoot);
                return childRendered;
            }
            RegisterVisibility(visibility, child, childRoot);
            return childRendered;
        }

        /// <summary>
        /// Records a rendered GameObject against the value that governs its
        /// visibility and applies that value's current state. Registration and
        /// the initial deactivation are one step so no render path can add a
        /// GameObject to the index while forgetting to honour
        /// <see cref="INeoWorldObjectValue.Enabled"/>.
        /// </summary>
        private static void RegisterVisibility(
            List<RenderedObjectVisibility> visibility,
            INeoWorldObjectValue value,
            GameObject gameObject)
        {
            visibility.Add(new RenderedObjectVisibility(value, gameObject));
            if (!value.Enabled)
            {
                gameObject.SetActive(false);
            }
        }

        private int RenderTileLayerLinkChild(
            Transform parent,
            IReadOnlyNeoObjectLayerRuntime layer,
            INeoValueReference link,
            Vector3 linkOffset,
            int baseSortingOrder,
            List<RenderedObjectVisibility> visibility)
        {
            if (link is not INeoTileLayerLinkValue tileLayerLink) return 0;

            var rendered = 0;
            var tileIndex = 0;
            foreach (var tileInstance in tileLayerLink.GetTiles())
            {
                var tileValue = tileInstance.Tile;

                // Tiles are not world objects, so they carry no sprite
                // contract: the tile asset factory stays the sprite source
                // here, and the child takes the sprite's own name.
                var sprite = NeoTileAssetFactory.ResolveSprite(tileValue);
                if (sprite == null) continue;

                // A link's tiles are bare siblings under the shared parent with
                // no root of their own, so each one is registered against the
                // link value that governs them.
                var tileGo = RenderSpriteChild(
                    parent,
                    layer,
                    sprite.name,
                    spriteObject: null,
                    sprite,
                    linkOffset + CellOffsetToLocalPosition(tileInstance.Cell),
                    Vector3.one,
                    baseSortingOrder + tileIndex++);
                if (link is INeoWorldObjectValue linkObject)
                {
                    RegisterVisibility(visibility, linkObject, tileGo);
                }
                rendered++;
            }
            return rendered;
        }

        /// <summary>
        /// Renders one sprite child. <paramref name="spriteObject"/> is the
        /// authored sprite state and is null for tile-layer-link tiles, which
        /// are not world objects and carry no renderer metadata.
        /// </summary>
        /// <returns>
        /// The GameObject created, so the caller can register it for
        /// visibility. Always non-null.
        /// </returns>
        private GameObject RenderSpriteChild(
            Transform parent,
            IReadOnlyNeoObjectLayerRuntime layer,
            string name,
            INeoSpriteObjectValue? spriteObject,
            Sprite sprite,
            Vector3 localPosition,
            Vector3 cellSpan,
            int sortingOrder)
        {
            var go = new GameObject(
                string.IsNullOrWhiteSpace(name) ? sprite.name : name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition + CellSpanCenterOffset(cellSpan);
            ScaleSpriteToCellSpan(go.transform, sprite, cellSpan);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            ApplySpriteState(renderer, spriteObject);
            // The authored order is an offset on the order derived from the
            // object's layer group, so an object layer's sorting order still
            // moves the sprite with it.
            ApplySorting(
                renderer,
                layer.SortingLayerName,
                sortingOrder + (spriteObject?.SortingOrder ?? 0));

            if (addSpriteBoundsColliders)
            {
                ApplyBoxCollider(go, new NeoBoxColliderSpec(
                    sprite.bounds.size,
                    sprite.bounds.center,
                    isTrigger: false));
            }

            return go;
        }

        private static void ApplySpriteState(
            SpriteRenderer renderer,
            INeoSpriteObjectValue? spriteObject)
        {
            if (spriteObject == null) return;

            renderer.flipX = spriteObject.FlipX;
            renderer.flipY = spriteObject.FlipY;
            renderer.maskInteraction =
                NeoSpriteMaskInteractionIds.Parse(spriteObject.MaskInteraction);
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

        // SortingGroup is a Component, not a Renderer, so it needs its own
        // overload rather than a widened one — the tilemap path shares the
        // Renderer signature above.
        private static void ApplySorting(
            UnityEngine.Rendering.SortingGroup sortingGroup,
            string? sortingLayerName,
            int sortingOrder)
        {
            if (!string.IsNullOrWhiteSpace(sortingLayerName))
            {
                sortingGroup.sortingLayerName = sortingLayerName;
            }

            sortingGroup.sortingOrder = sortingOrder;
        }

        private Vector3 CellToLocalPosition(Vector2Int cell) =>
            CellOffsetToLocalPosition(cell);

        private Vector3 CellOffsetToLocalPosition(Vector2Int cell) =>
            new(cell.x * cellSize, cell.y * cellSize, 0f);

        private Vector3 CellOffsetToLocalPosition(NeoReadOnlyVector3 position)
        {
            var cells = position.Value;
            return new Vector3(
                cells.x * cellSize,
                cells.y * cellSize,
                cells.z * cellSize);
        }

        private static Vector3 CellSpanFromSize(NeoReadOnlyVector3 size)
        {
            var cells = size.Value;
            return new Vector3(
                PositiveOrFallback(cells.x, 1f),
                PositiveOrFallback(cells.y, 1f),
                cells.z);
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

        private TileBase? TileBaseFor(NeoGeneratedClassValue value)
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

            string? classId = value.classId;
            if (string.IsNullOrEmpty(valueId)
                && !string.IsNullOrEmpty(classId)
                && tileBasesByClassId.TryGetValue(classId!, out var classTile))
            {
                generatedTileBases[value] = classTile;
                valuesByTileBase[classTile] = value;
                return classTile;
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
            else if (!string.IsNullOrEmpty(classId))
            {
                var databaseTile = assetDatabase?.TryGetTileBaseForClass(classId!);
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

        private void RegisterTileBase(NeoGeneratedClassValue value, TileBase tileBase)
        {
            generatedTileBases[value] = tileBase;
            string? valueId = value.valueId;
            if (!string.IsNullOrEmpty(valueId))
            {
                tileBasesByValueId[valueId!] = tileBase;
            }
            else if (!string.IsNullOrEmpty(value.classId))
            {
                tileBasesByClassId[value.classId!] = tileBase;
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

        /// <summary>
        /// Reads the authored collider off an object value. Size is in cells
        /// and must be positive on both axes; a collider with no usable size
        /// renders no <c>BoxCollider2D</c>.
        /// </summary>
        internal static bool TryResolveObjectColliderSpec(
            INeoColliderSource? source,
            out NeoBoxColliderSpec spec)
        {
            spec = default;
            if (source?.Collider is not { } collider) return false;

            var size = collider.Size.Value;
            if (size.x <= 0f || size.y <= 0f) return false;

            spec = new NeoBoxColliderSpec(
                size,
                collider.Offset?.Value ?? Vector2.zero,
                collider.IsTrigger ?? false);
            return true;
        }

        private static void ApplyBoxCollider(GameObject target, NeoBoxColliderSpec spec)
        {
            var collider = target.AddComponent<BoxCollider2D>();
            collider.size = spec.Size;
            collider.offset = spec.Offset;
            collider.isTrigger = spec.IsTrigger;
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
                case NeoSmartTileNeighborKind.InheritsFromClass:
                    return MatchesInheritsSmartTileNeighbor(neighbor, other);
                case NeoSmartTileNeighborKind.NotInheritsFromClass:
                    return !MatchesInheritsSmartTileNeighbor(neighbor, other);
                default:
                    return false;
            }
        }

        private bool MatchesExactSmartTileNeighbor(
            NeoRuleTileNeighbor neighbor,
            TileBase? other)
        {
            if (other == null || !TryGetGeneratedValueForTileBase(other, out var value))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(neighbor.TileClassId))
            {
                return string.Equals(
                    value.classId,
                    neighbor.TileClassId,
                    StringComparison.Ordinal);
            }
            return false;
        }

        private bool MatchesInheritsSmartTileNeighbor(
            NeoRuleTileNeighbor neighbor,
            TileBase? other)
        {
            if (other == null) return false;
            if (!TryGetGeneratedValueForTileBase(other, out var otherValue))
            {
                return false;
            }
            if (!string.IsNullOrEmpty(neighbor.TileClassId))
            {
                return IsClassOrSubclass(otherValue, neighbor.TileClassId!);
            }
            return false;
        }

        private bool TryGetGeneratedValueForTileBase(
            TileBase tileBase,
            out NeoGeneratedClassValue value)
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

        private static bool IsClassOrSubclass(
            NeoGeneratedClassValue value,
            string? requiredClassId)
        {
            if (string.IsNullOrEmpty(requiredClassId)) return false;
            string? currentClassId = value.classId;
            while (!string.IsNullOrEmpty(currentClassId))
            {
                if (string.Equals(currentClassId, requiredClassId, StringComparison.Ordinal))
                {
                    return true;
                }
                if (!value.Client.TryGetClass(currentClassId!, out var schemaClass))
                {
                    return false;
                }
                currentClassId = schemaClass.extendsClassId;
            }
            return false;
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
                    var source = Content.Primitive.ResolveGeneratedClassValue(
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
            private readonly NeoGeneratedClassValue source;
            private readonly IDisposable subscription;
            private readonly IDisposable writableValueSubscription;
            private Dictionary<Vector2Int, NeoResolvedTileInstance> previousTilesByCell;
            private bool disposed;

            public NeoTileLayerLinkRenderer(
                NeoTileGridRenderer owner,
                INeoTileGridContent content,
                NeoTileLayerLinkDependency dependency,
                NeoGeneratedClassValue source)
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
                    HandleSourceChanged(changeSource));
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
                    source.Client.CurrentChangeSource);
            }

            private bool IsSourceValueId(string valueId)
            {
                if (string.IsNullOrEmpty(valueId)) return false;
                if (valueId == dependency.SourceValueId || valueId == source.valueId)
                {
                    return true;
                }
                if (string.IsNullOrEmpty(source.valueId) ||
                    !source.Client.TryGetValue(source.valueId!, out ObjectMemberValue? row) ||
                    row.value is null)
                {
                    return false;
                }
                return IsSourceChildValueId(row, "Tiles", valueId) ||
                    IsSourceChildValueId(row, "Position", valueId) ||
                    IsSourceChildValueId(row, "Size", valueId);
            }

            private static bool IsSourceChildValueId(
                ObjectMemberValue row,
                string key,
                string valueId)
            {
                return row.value is not null &&
                    row.value.TryGetValue(key, out string childValueId) &&
                    childValueId == valueId;
            }

            private void HandleSourceChanged(NeoChangeSource changeSource)
            {
                if (disposed) return;
                var currentTilesByCell = SnapshotSourceTilesFromValueRows();
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

            private Dictionary<Vector2Int, NeoResolvedTileInstance> SnapshotSourceTilesFromValueRows()
            {
                var tiles = new Dictionary<Vector2Int, NeoResolvedTileInstance>();
                string sourceValueId = source.valueId ?? dependency.SourceValueId;
                if (!source.Client.TryGetValue(sourceValueId, out ObjectMemberValue? sourceRow) ||
                    sourceRow.value is null ||
                    !sourceRow.value.TryGetValue("Tiles", out string tilesValueId) ||
                    !source.Client.TryGetValue(tilesValueId, out ArrayMemberValue? tilesRow) ||
                    tilesRow.value is null)
                {
                    return tiles;
                }

                // Ordered lists store ids inline. Unordered lists use the
                // containment join.
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
                    if (!source.Client.TryGetValue(tileInstanceValueId, out ObjectMemberValue? tileInstanceRow) ||
                        tileInstanceRow.value is null ||
                        !TryReadCell(tileInstanceRow, out Vector2Int localCell) ||
                        !TryReadTileValue(tileInstanceRow, out NeoGeneratedClassValue? tileValue))
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

            private Vector2Int ReadSourceOrigin(ObjectMemberValue sourceRow)
            {
                if (sourceRow.value is null ||
                    !sourceRow.value.TryGetValue("Position", out string positionValueId) ||
                    !source.Client.TryGetValue(positionValueId, out Vector3MemberValue? positionRow) ||
                    positionRow.value is null)
                {
                    return Vector2Int.zero;
                }
                return new Vector2Int(
                    Mathf.RoundToInt(positionRow.value.x),
                    Mathf.RoundToInt(positionRow.value.y));
            }

            private bool TryReadCell(
                ObjectMemberValue tileInstanceRow,
                out Vector2Int cell)
            {
                cell = default;
                if (tileInstanceRow.value is null ||
                    !tileInstanceRow.value.TryGetValue("Cell", out string cellValueId) ||
                    !source.Client.TryGetValue(cellValueId, out Vector2MemberValue? cellRow) ||
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
                ObjectMemberValue tileInstanceRow,
                out NeoGeneratedClassValue? tile)
            {
                tile = null;
                if (tileInstanceRow.value is null)
                {
                    return false;
                }
                string? assetClassId = ReadDirectReference(
                    tileInstanceRow.value,
                    "assetClassId");
                if (assetClassId is null) return false;
                string? assetValueId = ReadDirectReference(
                    tileInstanceRow.value,
                    "assetValueId");
                tile = source.Client.ResolveRegisteredGeneratedAsset(
                    assetClassId,
                    assetValueId);
                return tile != null;
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
