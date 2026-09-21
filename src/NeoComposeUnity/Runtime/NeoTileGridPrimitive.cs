// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Runtime
{
    public readonly struct NeoTileInstanceId : IEquatable<NeoTileInstanceId>
    {
        public string Value { get; }

        public NeoTileInstanceId(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public bool Equals(NeoTileInstanceId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is NeoTileInstanceId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value;
        public static implicit operator string(NeoTileInstanceId id) => id.Value;
        public static implicit operator NeoTileInstanceId(string value) => new(value);
    }

    public readonly struct NeoObjectInstanceId : IEquatable<NeoObjectInstanceId>
    {
        public string Value { get; }

        public NeoObjectInstanceId(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public bool Equals(NeoObjectInstanceId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is NeoObjectInstanceId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value;
        public static implicit operator string(NeoObjectInstanceId id) => id.Value;
        public static implicit operator NeoObjectInstanceId(string value) => new(value);
    }

    public enum NeoTileOutputSourceKind
    {
        Direct = 0,
        TileLayerLink = 1,
    }

    public sealed class NeoPlacementResult
    {
        public bool Ok { get; }
        public string? ErrorCode { get; }
        public string? Message { get; }

        private NeoPlacementResult(bool ok, string? errorCode, string? message)
        {
            Ok = ok;
            ErrorCode = errorCode;
            Message = message;
        }

        public static NeoPlacementResult Success() => new(true, null, null);

        public static NeoPlacementResult Error(string errorCode, string message) =>
            new(false, errorCode, message);
    }

    /// <summary>
    /// Optional gameplay validation hook for writable TileGrid primitives.
    /// Built-in validation failures return <see cref="NeoPlacementResult"/>;
    /// lifecycle hooks run after those checks and before data is written, so
    /// game code may throw to reject domain-specific placements.
    /// </summary>
    public class NeoTileGridLifecycle
    {
        public virtual void OnGridLoaded(NeoTileGridLoadedContext context) {}
        public virtual void OnTileLayerCreated(NeoTileLayerContext context) {}
        public virtual void OnObjectLayerCreated(NeoObjectLayerContext context) {}

        /// <summary>
        /// Render-time filter for object instances. Return false to skip rendering
        /// an instance (e.g. authoring markers like spawn points). Presentation
        /// only — the instance stays in the grid data and in queries.
        /// </summary>
        public virtual bool ShouldRenderObject(NeoObjectRenderContext context) => true;
        public virtual void BeforeSetTile(NeoTileSetContext context) {}
        public virtual void BeforeConvertTile(NeoTileConvertContext context) {}
        public virtual void BeforeResetTile(NeoTileResetContext context) {}
        public virtual void BeforeRemoveTile(NeoTileRemoveContext context) {}
        public virtual void BeforeSpawnObject(NeoObjectSpawnContext context) {}
        public virtual void BeforeDespawnObject(NeoObjectDespawnContext context) {}
    }

    public sealed class NeoTileGridLoadedContext
    {
        public NeoTileGridLoadedContext(NeoTileGridRenderer renderer, NeoReadOnlyTileGridPrimitive primitive)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Primitive = primitive ?? throw new ArgumentNullException(nameof(primitive));
        }

        public NeoTileGridRenderer Renderer { get; }
        public NeoReadOnlyTileGridPrimitive Primitive { get; }
    }

    public sealed class NeoTileLayerContext
    {
        public NeoTileLayerContext(
            NeoTileGridRenderer renderer,
            IReadOnlyNeoTileLayerRuntime layer,
            UnityEngine.Tilemaps.Tilemap tilemap)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            Tilemap = tilemap ?? throw new ArgumentNullException(nameof(tilemap));
        }

        public NeoTileGridRenderer Renderer { get; }
        public IReadOnlyNeoTileLayerRuntime Layer { get; }
        public UnityEngine.Tilemaps.Tilemap Tilemap { get; }
    }

    public sealed class NeoObjectLayerContext
    {
        public NeoObjectLayerContext(
            NeoTileGridRenderer renderer,
            IReadOnlyNeoObjectLayerRuntime layer,
            GameObject root)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            Root = root ?? throw new ArgumentNullException(nameof(root));
        }

        public NeoTileGridRenderer Renderer { get; }
        public IReadOnlyNeoObjectLayerRuntime Layer { get; }
        public GameObject Root { get; }
    }

    public sealed class NeoObjectRenderContext
    {
        public NeoObjectRenderContext(
            NeoTileGridRenderer renderer,
            IReadOnlyNeoObjectLayerRuntime layer,
            NeoGeneratedClassValue instance)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        }

        public NeoTileGridRenderer Renderer { get; }
        public IReadOnlyNeoObjectLayerRuntime Layer { get; }
        public NeoGeneratedClassValue Instance { get; }
    }

    public abstract class NeoTileGridMutationContext
    {
        protected NeoTileGridMutationContext(
            NeoTileGridPrimitive grid,
            string layerId,
            Vector2Int cell,
            string? instanceId,
            JObject? existingInstance)
        {
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            LayerId = layerId ?? throw new ArgumentNullException(nameof(layerId));
            Cell = cell;
            InstanceId = instanceId;
            ExistingInstance = existingInstance;
        }

        public NeoTileGridPrimitive Grid { get; }
        public string LayerId { get; }
        public Vector2Int Cell { get; }
        public string? InstanceId { get; }
        public JObject? ExistingInstance { get; }
    }

    public sealed class NeoTileSetContext : NeoTileGridMutationContext
    {
        public NeoTileSetContext(
            NeoTileGridPrimitive grid,
            string layerId,
            Vector2Int cell,
            string instanceId,
            NeoGeneratedClassValue tile,
            string tileClassId,
            JObject? existingInstance)
            : base(grid, layerId, cell, instanceId, existingInstance)
        {
            Tile = tile ?? throw new ArgumentNullException(nameof(tile));
            TileClassId = tileClassId ?? throw new ArgumentNullException(nameof(tileClassId));
        }

        public NeoGeneratedClassValue Tile { get; }
        public string TileClassId { get; }
    }

    public sealed class NeoTileConvertContext : NeoTileGridMutationContext
    {
        public NeoTileConvertContext(
            NeoTileGridPrimitive grid,
            string layerId,
            Vector2Int cell,
            string instanceId,
            NeoGeneratedClassValue target,
            string targetClassId,
            JObject existingInstance)
            : base(grid, layerId, cell, instanceId, existingInstance)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            TargetClassId = targetClassId ?? throw new ArgumentNullException(nameof(targetClassId));
        }

        public NeoGeneratedClassValue Target { get; }
        public string TargetClassId { get; }
    }

    public sealed class NeoTileResetContext : NeoTileGridMutationContext
    {
        public NeoTileResetContext(
            NeoTileGridPrimitive grid,
            string layerId,
            Vector2Int cell,
            string instanceId,
            JObject existingInstance)
            : base(grid, layerId, cell, instanceId, existingInstance)
        {
        }
    }

    public sealed class NeoTileRemoveContext : NeoTileGridMutationContext
    {
        public NeoTileRemoveContext(
            NeoTileGridPrimitive grid,
            string layerId,
            Vector2Int cell,
            string instanceId,
            JObject existingInstance)
            : base(grid, layerId, cell, instanceId, existingInstance)
        {
        }
    }

    public sealed class NeoObjectSpawnContext : NeoTileGridMutationContext
    {
        public NeoObjectSpawnContext(
            NeoTileGridPrimitive grid,
            string layerId,
            Vector2Int cell,
            string instanceId,
            NeoGeneratedClassValue obj,
            string? assetValueId,
            string objectClassId)
            : base(grid, layerId, cell, instanceId, null)
        {
            Object = obj ?? throw new ArgumentNullException(nameof(obj));
            AssetValueId = assetValueId;
            ObjectClassId = objectClassId ?? throw new ArgumentNullException(nameof(objectClassId));
        }

        public NeoGeneratedClassValue Object { get; }
        public string? AssetValueId { get; }
        public string ObjectClassId { get; }
    }

    public sealed class NeoObjectDespawnContext : NeoTileGridMutationContext
    {
        public NeoObjectDespawnContext(
            NeoTileGridPrimitive grid,
            string layerId,
            Vector2Int cell,
            string instanceId,
            JObject existingInstance)
            : base(grid, layerId, cell, instanceId, existingInstance)
        {
        }
    }

    public sealed class NeoTileCandidate<TTile>
        where TTile : class, INeoValueReference
    {
        public NeoTileInstanceId InstanceId { get; }
        public Vector2Int Cell { get; }
        public TTile Tile { get; }
        public int Order { get; }

        public NeoTileCandidate(
            NeoTileInstanceId instanceId,
            Vector2Int cell,
            TTile tile,
            int order = 0,
            NeoTileOutputSourceKind sourceKind = NeoTileOutputSourceKind.Direct,
            string? sourceObjectInstanceId = null,
            string? sourceTileLayerLinkId = null)
        {
            InstanceId = instanceId;
            Cell = cell;
            Tile = tile ?? throw new ArgumentNullException(nameof(tile));
            Order = order;
            SourceKind = sourceKind;
            SourceObjectInstanceId = sourceObjectInstanceId;
            SourceTileLayerLinkId = sourceTileLayerLinkId;
        }

        public NeoTileOutputSourceKind SourceKind { get; }
        public string? SourceObjectInstanceId { get; }
        public string? SourceTileLayerLinkId { get; }
    }

    public sealed class NeoTileConflict<TTile>
        where TTile : class, INeoValueReference
    {
        public NeoTileConflict(
            string layerId,
            Vector2Int cell,
            IReadOnlyList<NeoTileCandidate<TTile>> candidates)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            LayerId = layerId ?? throw new ArgumentNullException(nameof(layerId));
            Cell = cell;
            Candidates = candidates;
            Winner = candidates.Count == 0 ? null : candidates[candidates.Count - 1];
        }

        public string LayerId { get; }
        public Vector2Int Cell { get; }
        public IReadOnlyList<NeoTileCandidate<TTile>> Candidates { get; }
        public NeoTileCandidate<TTile>? Winner { get; }
        public bool HasConflict => Candidates.Count > 1;
    }

    internal sealed class NeoTileProjection
    {
        public NeoTileProjection(
            NeoTileInstanceId instanceId,
            string layerId,
            Vector2Int cell,
            NeoGeneratedClassValue tile,
            int order,
            NeoTileOutputSourceKind sourceKind = NeoTileOutputSourceKind.Direct,
            string? sourceObjectInstanceId = null,
            string? sourceTileLayerLinkId = null)
        {
            InstanceId = instanceId;
            LayerId = layerId ?? throw new ArgumentNullException(nameof(layerId));
            Cell = cell;
            Info = tile ?? throw new ArgumentNullException(nameof(tile));
            Tile = tile;
            Order = order;
            SourceKind = sourceKind;
            SourceObjectInstanceId = sourceObjectInstanceId;
            SourceTileLayerLinkId = sourceTileLayerLinkId;
        }

        public NeoTileInstanceId InstanceId { get; }
        public string LayerId { get; }
        public Vector2Int Cell { get; }
        public INeoValueReference Info { get; }
        public NeoGeneratedClassValue Tile { get; }
        public int Order { get; }
        public NeoTileOutputSourceKind SourceKind { get; }
        public string? SourceObjectInstanceId { get; }
        public string? SourceTileLayerLinkId { get; }

        public bool TryGetTile<TTile>(out TTile tile)
            where TTile : class, INeoValueReference
        {
            tile = Info as TTile;
            return tile != null;
        }

        public TTile? As<TTile>() where TTile : class, INeoValueReference => Info as TTile;
    }

    internal sealed class NeoObjectProjection
    {
        public NeoObjectProjection(
            NeoObjectInstanceId instanceId,
            string layerId,
            Vector2Int cell,
            IReadOnlyList<Vector2Int> footprint,
            NeoGeneratedClassValue obj,
            int order)
        {
            InstanceId = instanceId;
            LayerId = layerId ?? throw new ArgumentNullException(nameof(layerId));
            Cell = cell;
            Footprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
            Info = obj ?? throw new ArgumentNullException(nameof(obj));
            Object = obj;
            Order = order;
        }

        public NeoObjectInstanceId InstanceId { get; }
        public string LayerId { get; }
        public Vector2Int Cell { get; }
        public IReadOnlyList<Vector2Int> Footprint { get; }
        public INeoValueReference Info { get; }
        public NeoGeneratedClassValue Object { get; }
        public int Order { get; }

        public bool TryGetObject<TObject>(out TObject obj)
            where TObject : class, INeoValueReference
        {
            obj = Info as TObject;
            return obj != null;
        }

        public TObject? As<TObject>() where TObject : class, INeoValueReference => Info as TObject;
    }

    internal sealed class NeoTileLayerRenderSnapshot
    {
        public NeoTileLayerRenderSnapshot(IReadOnlyList<NeoTileProjection> winners)
        {
            Winners = winners ?? throw new ArgumentNullException(nameof(winners));
        }

        public IReadOnlyList<NeoTileProjection> Winners { get; }
    }

    public class ReadOnlyNeoTileLayerRuntime : IReadOnlyNeoTileLayerRuntime
    {
        public string LayerId { get; }
        public string LayerClassId => LayerId;
        public string? LayerOverrideValueId => null;
        public string? valueId => LayerId;
        public string DisplayName { get; }
        public string ExpectedClassId { get; }
        public string? SortingLayerName { get; }
        public int? SortingOrder { get; }

        protected ReadOnlyNeoTileLayerRuntime(
            string layerId,
            string displayName,
            string expectedClassId,
            string? sortingLayerName = null,
            int? sortingOrder = null)
        {
            LayerId = layerId;
            DisplayName = displayName;
            ExpectedClassId = expectedClassId;
            SortingLayerName = string.IsNullOrWhiteSpace(sortingLayerName)
                ? null
                : sortingLayerName;
            SortingOrder = sortingOrder;
        }

        public IReadOnlyList<NeoGeneratedClassValue> GetTiles() => NeoWorldLayerRuntimeSupport.TileValues(GetTileProjections());
        public NeoGeneratedClassValue? GetTile(Vector2Int cell) => GetTileProjection(cell)?.Tile;
        public NeoGeneratedClassValue? ResolveTile(Vector2Int cell) => GetTile(cell);

        internal virtual IReadOnlyList<NeoTileProjection> GetTileProjections() =>
            Array.Empty<NeoTileProjection>();

        internal virtual NeoTileLayerRenderSnapshot GetRenderSnapshot() =>
            new(GetTileProjections());

        internal virtual IEnumerable<NeoTileProjection> EnumerateRenderTiles() =>
            GetRenderSnapshot().Winners;

        internal virtual NeoTileProjection? GetTileProjection(Vector2Int cell) => null;

        public virtual IDisposable OnChanged(Action<NeoTileLayerChangedArgs> handler) =>
            new NeoDisposableSubscription(() => {});
    }

    public class ReadOnlyNeoObjectLayerRuntime : IReadOnlyNeoObjectLayerRuntime
    {
        public string LayerId { get; }
        public string LayerClassId => LayerId;
        public string? LayerOverrideValueId => null;
        public string? valueId => LayerId;
        public string DisplayName { get; }
        public string ExpectedClassId { get; }
        public string? SortingLayerName { get; }
        public int? SortingOrder { get; }

        protected ReadOnlyNeoObjectLayerRuntime(
            string layerId,
            string displayName,
            string expectedClassId,
            string? sortingLayerName = null,
            int? sortingOrder = null)
        {
            LayerId = layerId;
            DisplayName = displayName;
            ExpectedClassId = expectedClassId;
            SortingLayerName = string.IsNullOrWhiteSpace(sortingLayerName)
                ? null
                : sortingLayerName;
            SortingOrder = sortingOrder;
        }

        public IReadOnlyList<NeoGeneratedClassValue> GetObjects() => NeoWorldLayerRuntimeSupport.ObjectValues(GetObjectProjections());
        public NeoGeneratedClassValue? GetObject(NeoObjectInstanceId id) => GetObjectProjection(id)?.Object;
        public NeoGeneratedClassValue? GetObject(Vector2Int cell) => GetObjectProjection(cell)?.Object;
        public IReadOnlyList<NeoGeneratedClassValue> GetObjects(Vector2Int cell) => NeoWorldLayerRuntimeSupport.ObjectValues(GetObjectProjections(cell));
        public NeoGeneratedClassValue? ResolveObject(NeoObjectInstanceId id) => GetObject(id);
        public NeoGeneratedClassValue? ResolveObject(Vector2Int cell) => GetObject(cell);
        public IReadOnlyList<NeoGeneratedClassValue> ResolveObjects(Vector2Int cell) => GetObjects(cell);

        internal virtual IReadOnlyList<NeoObjectProjection> GetObjectProjections() =>
            Array.Empty<NeoObjectProjection>();

        internal virtual NeoObjectProjection? GetObjectProjection(NeoObjectInstanceId instanceId) =>
            null;

        internal virtual NeoObjectProjection? GetObjectProjection(Vector2Int cell) =>
            null;

        internal virtual IReadOnlyList<NeoObjectProjection> GetObjectProjections(Vector2Int cell) =>
            Array.Empty<NeoObjectProjection>();

        public virtual IDisposable OnChanged(Action<NeoObjectLayerChangedArgs> handler) =>
            new NeoDisposableSubscription(() => {});
    }

    public class ReadOnlyNeoGeneratedTileLayer<TTile> : ReadOnlyNeoTileLayerRuntime
        where TTile : class, INeoValueReference
    {
        protected readonly NeoReadOnlyTileGridPrimitive primitive;

        protected ReadOnlyNeoGeneratedTileLayer(
            NeoReadOnlyTileGridPrimitive primitive,
            string layerId,
            string displayName,
            string expectedClassId,
            string? sortingLayerName = null,
            int? sortingOrder = null)
            : base(layerId, displayName, expectedClassId, sortingLayerName, sortingOrder)
        {
            this.primitive = primitive ?? throw new ArgumentNullException(nameof(primitive));
        }

        internal override NeoTileProjection? GetTileProjection(Vector2Int cell) =>
            primitive.ResolveTileCached(LayerId, cell, ExpectedClassId);

        public IReadOnlyList<NeoTileCandidate<TTile>> GetCandidates(Vector2Int cell) =>
            primitive.GetTileCandidates<TTile>(LayerId, cell, ExpectedClassId);

        public NeoTileConflict<TTile>? GetConflict(Vector2Int cell) =>
            primitive.GetTileConflict<TTile>(LayerId, cell, ExpectedClassId);

        internal override IReadOnlyList<NeoTileProjection> GetTileProjections() =>
            primitive.GetTileProjections(LayerId, ExpectedClassId);

        internal override NeoTileLayerRenderSnapshot GetRenderSnapshot() =>
            primitive.GetTileLayerRenderSnapshot(LayerId, ExpectedClassId);

        internal override IEnumerable<NeoTileProjection> EnumerateRenderTiles() =>
            primitive.EnumerateTileProjections(LayerId, ExpectedClassId);

        public override IDisposable OnChanged(Action<NeoTileLayerChangedArgs> handler) =>
            primitive.OnTileLayerChanged(LayerId, handler);
    }

    public class ReadOnlyNeoGeneratedObjectLayer<TObject> : ReadOnlyNeoObjectLayerRuntime
        where TObject : class, INeoValueReference
    {
        protected readonly NeoReadOnlyTileGridPrimitive primitive;

        protected ReadOnlyNeoGeneratedObjectLayer(
            NeoReadOnlyTileGridPrimitive primitive,
            string layerId,
            string displayName,
            string expectedClassId,
            string? sortingLayerName = null,
            int? sortingOrder = null)
            : base(layerId, displayName, expectedClassId, sortingLayerName, sortingOrder)
        {
            this.primitive = primitive ?? throw new ArgumentNullException(nameof(primitive));
        }

        internal override IReadOnlyList<NeoObjectProjection> GetObjectProjections() =>
            primitive.GetObjectProjections(LayerId, ExpectedClassId);

        internal override NeoObjectProjection? GetObjectProjection(NeoObjectInstanceId instanceId) =>
            primitive.ResolveObjectInstance(LayerId, instanceId, ExpectedClassId);

        internal override NeoObjectProjection? GetObjectProjection(Vector2Int cell) =>
            primitive.ResolveObjectAtCellCached(LayerId, cell, ExpectedClassId);

        internal override IReadOnlyList<NeoObjectProjection> GetObjectProjections(Vector2Int cell) =>
            primitive.ResolveObjectsAtCellCached(LayerId, cell, ExpectedClassId);

        public override IDisposable OnChanged(Action<NeoObjectLayerChangedArgs> handler) =>
            primitive.OnObjectLayerChanged(LayerId, handler);
    }

    /// <summary>
    /// A placed tile row joined to its layer link's "Tiles" list via
    /// <see cref="MemberValue.containerId"/>, or a tile composed from an
    /// object-carried tile layer link (stamp/prefab), projected to grid space.
    /// </summary>
    internal sealed class NeoTilePlacementRecord
    {
        public NeoTilePlacementRecord(
            string instanceId,
            string placementValueId,
            Vector2Int cell,
            string assetClassId,
            NeoValueOwnership? ownership,
            int order,
            string sourceTileLayerLinkId,
            string? sourceObjectInstanceId,
            double updatedAtMs,
            string? cellValueId)
        {
            InstanceId = instanceId;
            PlacementValueId = placementValueId;
            Cell = cell;
            AssetClassId = assetClassId;
            Ownership = ownership;
            Order = order;
            SourceTileLayerLinkId = sourceTileLayerLinkId;
            SourceObjectInstanceId = sourceObjectInstanceId;
            UpdatedAtMs = updatedAtMs;
            CellValueId = cellValueId;
        }

        public string InstanceId { get; }
        public string PlacementValueId { get; }
        public Vector2Int Cell { get; }
        public string AssetClassId { get; }
        public NeoValueOwnership? Ownership { get; }
        public int Order { get; }
        public string SourceTileLayerLinkId { get; }
        public string? SourceObjectInstanceId { get; }
        public double UpdatedAtMs { get; }
        public string? CellValueId { get; }
        public bool IsObjectCarried => SourceObjectInstanceId is not null;
    }

    /// <summary>
    /// One resolved placed object: an object value joined to its object layer
    /// link's unordered "Objects" list via <see cref="MemberValue.containerId"/>,
    /// with its footprint translated from the record's authored PlacementTiles.
    /// </summary>
    internal sealed class NeoObjectPlacementRecord
    {
        public NeoObjectPlacementRecord(
            string instanceId,
            Vector2Int cell,
            IReadOnlyList<Vector2Int> footprint,
            int order,
            string assetClassId,
            string? assetValueId,
            NeoValueOwnership? ownership)
        {
            InstanceId = instanceId;
            Cell = cell;
            Footprint = footprint;
            Order = order;
            AssetClassId = assetClassId;
            AssetValueId = assetValueId;
            Ownership = ownership;
        }

        public string InstanceId { get; }
        public Vector2Int Cell { get; }
        public IReadOnlyList<Vector2Int> Footprint { get; }
        public int Order { get; }
        public string AssetClassId { get; }
        public string? AssetValueId { get; }
        public NeoValueOwnership? Ownership { get; }
    }

    /// <summary>One grid-child layer link (TileLayerLink or ObjectLayerLink).</summary>
    internal sealed class NeoGridLayerLinkModel
    {
        public NeoGridLayerLinkModel(
            string linkValueId,
            string linkClassId,
            string layerId,
            string? layerOverrideValueId,
            string listValueId,
            bool isTileLink)
        {
            LinkValueId = linkValueId;
            LinkClassId = linkClassId;
            LayerId = layerId;
            LayerOverrideValueId = layerOverrideValueId;
            ListValueId = listValueId;
            IsTileLink = isTileLink;
        }

        public string LinkValueId { get; }
        public string LinkClassId { get; }
        public string LayerId { get; }
        public string? LayerOverrideValueId { get; }
        /// <summary>The containment list VALUE id ("Tiles" / "Objects").</summary>
        public string ListValueId { get; }
        public bool IsTileLink { get; }
    }

    public class NeoReadOnlyTileGridPrimitive
    {
        protected readonly NeoClient client;
        protected static readonly IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> EmptyReadOnlyFactories =
            new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>();
        protected static readonly IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> EmptyWritableFactories =
            new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>();
        protected static readonly IReadOnlyDictionary<Type, string> EmptyClassIdsByType =
            new Dictionary<Type, string>();
        private readonly IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> readOnlyFactories;
        protected readonly IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> writableFactories;
        private readonly IReadOnlyDictionary<Type, string> classIdsByType;
        protected readonly string bindingInstanceId = Guid.NewGuid().ToString("N");
        private NeoTileGridLookupCache? lookupCache;

        private static readonly string[] ChildrenKeyCandidates = { "Children" };
        private static readonly string[] TilesKeyCandidates = { "Tiles" };
        private static readonly string[] ObjectsKeyCandidates = { "Objects" };
        private static readonly string[] CellKeyCandidates = { "Cell" };
        private static readonly string[] PositionKeyCandidates = { "Position" };
        private static readonly string[] PlacementTilesKeyCandidates = { "PlacementTiles" };

        protected NeoReadOnlyTileGridPrimitive(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>? readOnlyFactories = null,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>? writableFactories = null,
            IReadOnlyDictionary<Type, string>? classIdsByType = null)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            GridValueId = gridValueId ?? throw new ArgumentNullException(nameof(gridValueId));
            this.readOnlyFactories = readOnlyFactories ?? EmptyReadOnlyFactories;
            this.writableFactories = writableFactories ?? EmptyWritableFactories;
            this.classIdsByType = classIdsByType ?? EmptyClassIdsByType;
            client.RegisterGeneratedClassFactories(
                this.readOnlyFactories,
                this.writableFactories);
            // Storage partitions (spec §6): resolving a grid primitive IS the
            // content-access path — lazily merge the grid's
            // `world:<gridClassId>` placement partition (no-op for grids
            // authored in the main partition).
            client.EnsureWorldPartitionLoaded(gridValueId);
        }

        public void RegisterScriptContent(INeoTileGridContent content) => client.ScriptGridQueries.RegisterContent(content, GridValueId);

        public string GridValueId { get; }
        public NeoTileGridRenderer? Renderer { get; internal set; }
        internal NeoClient Client => client;

        internal NeoTileGridLookupCache LookupCache
        {
            get
            {
                // Covers explicit UnloadValuePartition-then-reaccess: the
                // primitive outlives the unload, so every indexed query
                // re-ensures the world partition (two dictionary probes when
                // already loaded).
                client.EnsureWorldPartitionLoaded(GridValueId);
                return lookupCache ??= client.GetGridLookupCache(GridValueId);
            }
        }

        public IDisposable OnChanged(Action<NeoTileGridChangedArgs> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            var tileLayerIds = new HashSet<string>(ResolveTileLayerIds());
            var objectLayerIds = new HashSet<string>(ResolveObjectLayerIds());
            if (client.ResolveValueRow(GridValueId) is ObjectMemberValue { classId: not null } grid)
            {
                tileLayerIds.UnionWith(client.InternalRecordRelations.ResolveTargetIds(
                    InternalRecordRelationKinds.WorldGridTileLayer, grid.classId));
                objectLayerIds.UnionWith(client.InternalRecordRelations.ResolveTargetIds(
                    InternalRecordRelationKinds.WorldGridObjectLayer, grid.classId));
            }
            foreach (string layerId in tileLayerIds) LookupCache.TileRecords(layerId);
            foreach (string layerId in objectLayerIds) LookupCache.ObjectRecords(layerId);
            return SubscribeChanged(handler);
        }

        private IDisposable SubscribeChanged(Action<NeoTileGridChangedArgs> handler)
        {
            return LookupCache.Subscribe(handler);
        }

        internal IDisposable OnTileLayerChanged(
            string layerId,
            Action<NeoTileLayerChangedArgs> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            LookupCache.TileRecords(layerId);
            void Handle(NeoTileGridChangedArgs args)
            {
                foreach (var layer in args.TileLayers)
                {
                    if (layer.LayerId == layerId)
                    {
                        handler(layer);
                    }
                }
            }
            return SubscribeChanged(Handle);
        }

        internal IDisposable OnObjectLayerChanged(
            string layerId,
            Action<NeoObjectLayerChangedArgs> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            LookupCache.ObjectRecords(layerId);
            void Handle(NeoTileGridChangedArgs args)
            {
                foreach (var layer in args.ObjectLayers)
                {
                    if (layer.LayerId == layerId)
                    {
                        handler(layer);
                    }
                }
            }
            return SubscribeChanged(Handle);
        }

        internal void NotifyTileLayerChanged(
            string layerId,
            IReadOnlyList<Vector2Int> cellsToClear,
            IReadOnlyList<Vector2Int> cellsToSetOrRefresh,
            NeoTileGridChangeSourceKind sourceKind,
            string? sourceId,
            NeoChangeSource source = NeoChangeSource.Local)
        {
            if (string.IsNullOrEmpty(layerId)) return;
            NotifyChanged(new NeoTileGridChangedArgs(
                GridValueId,
                new[]
                {
                    new NeoTileLayerChangedArgs(
                        layerId,
                        cellsToClear,
                        cellsToSetOrRefresh,
                        sourceKind,
                        sourceId),
                },
                Array.Empty<NeoObjectLayerChangedArgs>(),
                source));
        }

        internal void NotifyObjectLayerChanged(
            string layerId,
            IReadOnlyList<NeoObjectInstanceId> removedInstances,
            IReadOnlyList<NeoObjectInstanceId> addedOrChangedInstances,
            IReadOnlyList<Vector2Int> changedCells,
            NeoTileGridChangeSourceKind sourceKind,
            string? sourceId,
            NeoChangeSource source = NeoChangeSource.Local)
        {
            if (string.IsNullOrEmpty(layerId)) return;
            NotifyChanged(new NeoTileGridChangedArgs(
                GridValueId,
                Array.Empty<NeoTileLayerChangedArgs>(),
                new[]
                {
                    new NeoObjectLayerChangedArgs(
                        layerId,
                        removedInstances,
                        addedOrChangedInstances,
                        changedCells,
                        sourceKind,
                        sourceId),
                },
                source));
        }

        internal void NotifyChanged(NeoTileGridChangedArgs args)
        {
            LookupCache.Apply(args);
            NotifyDataChanged(args);
        }

        internal void NotifyDataChanged(NeoTileGridChangedArgs args)
        {
            LookupCache.NotifyChanged(args);
        }

        public static NeoReadOnlyTileGridPrimitive Resolve(NeoClient client, string gridValueId)
        {
            return Resolve(client, gridValueId, EmptyReadOnlyFactories, EmptyWritableFactories);
        }

        public static NeoReadOnlyTileGridPrimitive Resolve(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> writableFactories)
        {
            return Resolve(
                client,
                gridValueId,
                readOnlyFactories,
                writableFactories,
                EmptyClassIdsByType);
        }

        public static NeoReadOnlyTileGridPrimitive Resolve(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> writableFactories,
            IReadOnlyDictionary<Type, string> classIdsByType)
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            return new NeoReadOnlyTileGridPrimitive(
                client,
                gridValueId,
                readOnlyFactories,
                writableFactories,
                classIdsByType);
        }

        public TLayer BindReadOnlyTileLayer<TLayer>(
            string layerClassId,
            IReadOnlyCollection<string> importedClassIds)
            where TLayer : class
        {
            string? layerOverrideValueId = ResolveLayerOverrideValueId(
                layerClassId,
                isTileLayer: true);
            object value = CreateReadOnlyLayerValue(
                layerClassId,
                layerOverrideValueId,
                $"__neo_grid_tile_layer_ro:{bindingInstanceId}:{GridValueId}:{layerClassId}");
            if (value is not TLayer typed || value is not NeoGeneratedTileLayerValue layer)
            {
                throw new InvalidOperationException(
                    $"Generated class '{layerClassId}' is not the requested authored tile layer '{typeof(TLayer).Name}'.");
            }
            layer.BindGridLayer(new NeoTileLayerBinding(
                this,
                layerClassId,
                layerOverrideValueId,
                importedClassIds));
            return typed;
        }

        public TLayer BindReadOnlyObjectLayer<TLayer>(
            string layerClassId,
            IReadOnlyCollection<string> importedClassIds)
            where TLayer : class
        {
            string? layerOverrideValueId = ResolveLayerOverrideValueId(
                layerClassId,
                isTileLayer: false);
            object value = CreateReadOnlyLayerValue(
                layerClassId,
                layerOverrideValueId,
                $"__neo_grid_object_layer_ro:{bindingInstanceId}:{GridValueId}:{layerClassId}");
            if (value is not TLayer typed || value is not NeoGeneratedObjectLayerValue layer)
            {
                throw new InvalidOperationException(
                    $"Generated class '{layerClassId}' is not the requested authored object layer '{typeof(TLayer).Name}'.");
            }
            layer.BindGridLayer(new NeoObjectLayerBinding(
                this,
                layerClassId,
                layerOverrideValueId,
                importedClassIds));
            return typed;
        }

        private object CreateReadOnlyLayerValue(
            string classId,
            string? overrideValueId,
            string memberId)
        {
            if (!readOnlyFactories.TryGetValue(classId, out var factory))
            {
                throw new InvalidOperationException(
                    $"No generated read-only factory exists for layer class '{classId}'. Regenerate the project's C# types.");
            }
            var node = new NeoMemberClass(
                client,
                CreateClassDefaultMember(classId, memberId),
                overrideValueId);
            object value = factory(client, node);
            if (overrideValueId is null && value is NeoGeneratedClassValue generated)
            {
                generated.MarkClassDefaultReference();
            }
            return value;
        }

        protected string? ResolveLayerOverrideValueId(
            string layerClassId,
            bool isTileLayer)
        {
            NeoGridLayerLinkModel? overrideOwner = null;
            bool linked = false;
            foreach (var link in ResolveGridLinks(null))
            {
                if (link.IsTileLink != isTileLayer || link.LayerId != layerClassId)
                {
                    continue;
                }
                linked = true;
                if (link.LayerOverrideValueId is null) continue;
                if (overrideOwner is not null)
                {
                    throw new InvalidOperationException(
                        $"Grid '{GridValueId}' has multiple override-owning links for layer class '{layerClassId}'. Source links may share a layer target, but at most one may define layerOverrideValueId.");
                }
                overrideOwner = link;
            }
            // A grid declares its layers through record relations; linking one
            // is what gives it content to hold. A declared layer nothing links
            // — the object layer every grid is seeded with, or one the author
            // has not placed anything on yet — is empty, not broken, and binds
            // to its class defaults like any link that owns no override. Writes
            // still refuse it, through `tile-grid-layer-link-missing`, which
            // names what is actually absent.
            //
            // A layer the grid does not declare AT ALL is the other mistake,
            // and it stays refused here: no link names it and no relation
            // reaches it, so nothing downstream would ever catch it.
            if (!linked && !DeclaresLayer(layerClassId, isTileLayer))
            {
                throw new InvalidOperationException(
                    $"Grid '{GridValueId}' does not declare layer class '{layerClassId}'.");
            }
            return overrideOwner?.LayerOverrideValueId;
        }

        /// <summary>
        /// Whether the grid's class declares <paramref name="layerClassId"/>
        /// through its effective grid-layer relations, which is what codegen
        /// emits a <c>BindWritable*Layer</c> overload for.
        /// </summary>
        private bool DeclaresLayer(string layerClassId, bool isTileLayer)
        {
            if (client.ResolveValueRow(GridValueId) is not ObjectMemberValue gridRow
                || string.IsNullOrEmpty(gridRow.classId))
            {
                return false;
            }
            string relationKind = isTileLayer
                ? InternalRecordRelationKinds.WorldGridTileLayer
                : InternalRecordRelationKinds.WorldGridObjectLayer;
            foreach (string declared in
                     client.InternalRecordRelations.ResolveTargetIds(
                         relationKind,
                         gridRow.classId!))
            {
                if (string.Equals(declared, layerClassId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private protected NeoGridLayerLinkModel? ResolveDirectWriteTargetLink(
            string layerClassId,
            bool isTileLayer,
            NeoValueOwnership writeOwnership,
            out bool ambiguous)
        {
            ambiguous = false;
            NeoGridLayerLinkModel? onlyMatch = null;
            NeoGridLayerLinkModel? overrideOwner = null;
            int matches = 0;
            foreach (var link in ResolveGridLinks(null))
            {
                if (link.IsTileLink != isTileLayer || link.LayerId != layerClassId)
                {
                    continue;
                }
                NeoValueOwnership? declared = ResolveCollectionOwnership(link.ListValueId);
                if (declared is null && client.TryResolveSchemaClassAllowedOwnership(link.LinkClassId, out var classOwnership))
                    declared = classOwnership;
                if (declared is not null && declared != writeOwnership) continue;
                matches += 1;
                onlyMatch ??= link;
                if (link.LayerOverrideValueId is null) continue;
                if (overrideOwner is not null)
                {
                    ambiguous = true;
                    return null;
                }
                overrideOwner = link;
            }
            if (matches <= 1) return onlyMatch;
            if (overrideOwner is not null) return overrideOwner;
            // Children order is authored order, so the first direct link is
            // the canonical write target while later links remain additional
            // aggregated sources (for example Hello World's BlockedPath).
            return onlyMatch;
        }

        internal string ResolveGeneratedClassId(Type generatedType)
        {
            if (generatedType is null) throw new ArgumentNullException(nameof(generatedType));
            if (classIdsByType.TryGetValue(generatedType, out string classId)) return classId;
            throw new InvalidOperationException(
                $"Generated Neo type '{generatedType.FullName}' has no class metadata. Regenerate the project's C# types.");
        }

        internal NeoGeneratedClassValue? ResolveGeneratedClassDefault(string classId)
        {
            if (string.IsNullOrEmpty(classId)) return null;
            return NeoGeneratedTypesSupport.CreateReadOnlyClassDefault(
                client,
                classId,
                readOnlyFactories);
        }

        protected static ClassMember CreateClassDefaultMember(string classId, string memberId)
        {
            var now = NeoTimestamp.Now();
            return new ClassMember
            {
                id = memberId,
                name = "ClassDefault",
                useDeclarationDefaults = true,
                kind = MemberKind.Class,
                classId = classId,
                defaultValue = new ObjectMemberValueBase
                {
                    classId = classId,
                    value = new Dictionary<string, string>(),
                },
                createdAt = now,
                updatedAt = now,
            };
        }

        // -------------------------------------------------------------------
        // Tile queries (values-backed spatial index; O(1) per cell once the
        // per-layer index is built).
        // -------------------------------------------------------------------

        public virtual TTile? GetTile<TTile>(
            string layerId,
            Vector2Int cell,
            string expectedTileFamilyClassId)
            where TTile : class, INeoValueReference
        {
            var candidates = LookupCache.TileCandidatesAt(layerId, cell);
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                var tile = ResolvePlacementAsset<TTile>(candidates[i], expectedTileFamilyClassId);
                if (tile is not null) return tile;
            }
            return null;
        }

        public virtual IReadOnlyList<NeoTileCandidate<TTile>> GetTileCandidates<TTile>(
            string layerId,
            Vector2Int cell,
            string expectedTileFamilyClassId)
            where TTile : class, INeoValueReference
        {
            var candidates = new List<NeoTileCandidate<TTile>>();
            foreach (var record in LookupCache.TileCandidatesAt(layerId, cell))
            {
                TTile? tile = ResolvePlacementAsset<TTile>(
                    record,
                    expectedTileFamilyClassId);
                if (tile is null) continue;
                candidates.Add(new NeoTileCandidate<TTile>(
                    record.InstanceId,
                    record.Cell,
                    tile,
                    record.Order,
                    NeoTileOutputSourceKind.TileLayerLink,
                    record.SourceObjectInstanceId,
                    record.SourceTileLayerLinkId));
            }
            return candidates;
        }

        public virtual NeoTileConflict<TTile>? GetTileConflict<TTile>(
            string layerId,
            Vector2Int cell,
            string expectedTileFamilyClassId)
            where TTile : class, INeoValueReference
        {
            var candidates = GetTileCandidates<TTile>(
                layerId,
                cell,
                expectedTileFamilyClassId);
            return candidates.Count <= 1
                ? null
                : new NeoTileConflict<TTile>(layerId, cell, candidates);
        }

        internal virtual IReadOnlyList<NeoTileProjection> GetTileProjections(
            string layerId,
            string expectedTileFamilyClassId = "")
            => new List<NeoTileProjection>(EnumerateTileProjections(layerId, expectedTileFamilyClassId));

        // Resolve generated values as rendering consumes them, so the async
        // renderer's frame budget covers hydration as well as tilemap writes.
        internal IEnumerable<NeoTileProjection> EnumerateTileProjections(
            string layerId, string expectedTileFamilyClassId = "")
        {
            // Movement mutates the cache's inner candidate lists. Freeze the
            // lightweight records before yielding; generated values remain lazy.
            var cells = LookupCache.TileCandidatesInCellOrder(layerId);
            var snapshot = new NeoTilePlacementRecord[cells.Count][];
            for (int i = 0; i < cells.Count; i++) snapshot[i] = cells[i].ToArray();
            foreach (var cellCandidates in snapshot)
            {
                var winner = ResolveWinner(layerId, cellCandidates, expectedTileFamilyClassId);
                if (winner is not null) yield return winner;
            }
        }

        internal IReadOnlyList<NeoGeneratedClassValue> GetTileValues(
            string layerId, string expectedTileFamilyClassId = "")
        {
            var cells = LookupCache.TileCandidatesInCellOrder(layerId);
            var values = new List<NeoGeneratedClassValue>(cells.Count);
            foreach (var candidates in cells)
                for (int i = candidates.Count - 1; i >= 0; i--)
                {
                    var value = ResolvePlacementAsset<NeoGeneratedClassValue>(
                        candidates[i], expectedTileFamilyClassId);
                    if (value is null) continue;
                    values.Add(value);
                    break;
                }
            return values;
        }

        internal virtual NeoTileLayerRenderSnapshot GetTileLayerRenderSnapshot(
            string layerId,
            string expectedTileFamilyClassId = "") =>
            new(GetTileProjections(layerId, expectedTileFamilyClassId));

        internal virtual NeoTileProjection? ResolveTile(
            string layerId,
            Vector2Int cell,
            string expectedTileFamilyClassId = "")
        {
            return ResolveWinner(
                layerId,
                LookupCache.TileCandidatesAt(layerId, cell),
                expectedTileFamilyClassId);
        }

        internal NeoTileProjection? ResolveTileCached(
            string layerId,
            Vector2Int cell,
            string expectedTileFamilyClassId = "")
        {
            return ResolveTile(layerId, cell, expectedTileFamilyClassId);
        }

        private NeoTileProjection? ResolveWinner(
            string layerId,
            IReadOnlyList<NeoTilePlacementRecord> cellCandidates,
            string expectedTileFamilyClassId)
        {
            // Candidates are stored loser→winner; the last resolvable record
            // (conflict tiebreak: updatedAt desc, id asc) wins the cell.
            for (int index = cellCandidates.Count - 1; index >= 0; index -= 1)
            {
                var resolved = ResolveRecord(layerId, cellCandidates[index], expectedTileFamilyClassId);
                if (resolved is not null) return resolved;
            }
            return null;
        }

        private NeoTileProjection? ResolveRecord(
            string layerId,
            NeoTilePlacementRecord record,
            string expectedTileFamilyClassId)
        {
            var tile = ResolvePlacementAsset<NeoGeneratedClassValue>(
                record,
                expectedTileFamilyClassId);
            if (tile is null) return null;
            return new NeoTileProjection(
                record.InstanceId,
                layerId,
                record.Cell,
                tile,
                record.Order,
                NeoTileOutputSourceKind.TileLayerLink,
                record.SourceObjectInstanceId,
                record.SourceTileLayerLinkId);
        }

        private TGenerated? ResolvePlacementAsset<TGenerated>(
            NeoTilePlacementRecord record,
            string expectedFamilyClassId)
            where TGenerated : class, INeoValueReference
        {
            if (!ClassExtendsClass(record.AssetClassId, expectedFamilyClassId)) return null;
            object? resolved = NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                record.PlacementValueId,
                readOnlyFactories,
                writableFactories,
                record.Ownership);
            if (resolved is not NeoGeneratedClassValue generated
                || !ClassExtendsClass(generated.classId ?? record.AssetClassId, record.AssetClassId))
            {
                return null;
            }
            return resolved as TGenerated;
        }

        // -------------------------------------------------------------------
        // Object queries.
        // -------------------------------------------------------------------

        public virtual TObject? GetObject<TObject>(
            string layerId,
            Vector2Int cell,
            string expectedObjectFamilyClassId)
            where TObject : class, INeoValueReference
        {
            var candidates = LookupCache.ObjectCandidatesAt(layerId, cell);
            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                var record = candidates[i];
                TObject? obj = ResolveObjectPlacementAsset<TObject>(
                    record,
                    expectedObjectFamilyClassId);
                if (obj is null) continue;
                client.ScriptGridQueries.Bind(obj.valueId!, GridValueId, layerId, record.InstanceId);
                return obj;
            }
            return null;
        }

        internal virtual IReadOnlyList<NeoObjectProjection> ResolveObjectsAtCell(
            string layerId,
            Vector2Int cell,
            string expectedObjectFamilyClassId = "")
        {
            var objects = new List<NeoObjectProjection>();
            foreach (var record in LookupCache.ObjectCandidatesAt(layerId, cell))
            {
                var resolved = ResolveObjectRecord(layerId, record, expectedObjectFamilyClassId);
                if (resolved is null) continue;
                objects.Add(resolved);
            }
            objects.Sort((left, right) =>
            {
                int order = left.Order.CompareTo(right.Order);
                return order != 0
                    ? order
                    : left.InstanceId.Value.CompareTo(right.InstanceId.Value);
            });
            return objects;
        }

        internal NeoObjectProjection? ResolveObjectAtCellCached(
            string layerId,
            Vector2Int cell,
            string expectedObjectFamilyClassId = "")
        {
            var objects = ResolveObjectsAtCell(layerId, cell, expectedObjectFamilyClassId);
            return objects.Count == 0 ? null : objects[objects.Count - 1];
        }

        internal IReadOnlyList<NeoObjectProjection> ResolveObjectsAtCellCached(
            string layerId,
            Vector2Int cell,
            string expectedObjectFamilyClassId = "")
        {
            return ResolveObjectsAtCell(layerId, cell, expectedObjectFamilyClassId);
        }

        internal virtual IReadOnlyList<NeoObjectProjection> GetObjectProjections(
            string layerId,
            string expectedObjectFamilyClassId = "")
        {
            var objects = new List<NeoObjectProjection>();
            foreach (var record in LookupCache.ObjectRecords(layerId))
            {
                var resolved = ResolveObjectRecord(layerId, record, expectedObjectFamilyClassId);
                if (resolved is null) continue;
                objects.Add(resolved);
            }
            return objects;
        }

        internal IReadOnlyList<NeoGeneratedClassValue> GetObjectValues(
            string layerId, string expectedObjectFamilyClassId = "") =>
            ResolveObjectValues(layerId, LookupCache.ObjectRecords(layerId), expectedObjectFamilyClassId);

        internal IReadOnlyList<NeoGeneratedClassValue> GetObjectValues(
            string layerId, Vector2Int cell, string expectedObjectFamilyClassId = "") =>
            ResolveObjectValues(layerId, LookupCache.ObjectCandidatesAt(layerId, cell), expectedObjectFamilyClassId);

        private IReadOnlyList<NeoGeneratedClassValue> ResolveObjectValues(
            string layerId, IReadOnlyList<NeoObjectPlacementRecord> records, string expectedFamilyClassId)
        {
            var values = new List<NeoGeneratedClassValue>(records.Count);
            foreach (var record in records)
            {
                var value = ResolveObjectValue(layerId, record, expectedFamilyClassId);
                if (value is not null) values.Add(value);
            }
            return values;
        }

        internal NeoGeneratedClassValue? GetObjectValue(
            string layerId, NeoObjectInstanceId instanceId, string expectedObjectFamilyClassId = "") =>
            LookupCache.ObjectRecord(layerId, instanceId.Value) is NeoObjectPlacementRecord record
                ? ResolveObjectValue(layerId, record, expectedObjectFamilyClassId) : null;

        private NeoGeneratedClassValue? ResolveObjectValue(
            string layerId, NeoObjectPlacementRecord record, string expectedFamilyClassId)
        {
            var value = ResolveObjectPlacementAsset<NeoGeneratedClassValue>(record, expectedFamilyClassId);
            if (value is not null)
                client.ScriptGridQueries.Bind(value.valueId!, GridValueId, layerId, record.InstanceId);
            return value;
        }

        internal virtual NeoObjectProjection? ResolveObjectInstance(
            string layerId,
            NeoObjectInstanceId instanceId,
            string expectedObjectFamilyClassId = "")
        {
            return LookupCache.ObjectRecord(layerId, instanceId.Value) is NeoObjectPlacementRecord record
                ? ResolveObjectRecord(layerId, record, expectedObjectFamilyClassId)
                : null;
        }

        private NeoObjectProjection? ResolveObjectRecord(
            string layerId,
            NeoObjectPlacementRecord record,
            string expectedObjectFamilyClassId)
        {
            var obj = ResolveObjectValue(layerId, record, expectedObjectFamilyClassId);
            if (obj is null) return null;
            return new NeoObjectProjection(
                record.InstanceId,
                layerId,
                record.Cell,
                record.Footprint,
                obj,
                record.Order);
        }

        private TGenerated? ResolveObjectPlacementAsset<TGenerated>(
            NeoObjectPlacementRecord record,
            string expectedFamilyClassId)
            where TGenerated : class, INeoValueReference
        {
            if (!ClassExtendsClass(record.AssetClassId, expectedFamilyClassId)) return null;
            // The placement row is the durable runtime object identity. Its
            // graph overlays authored defaults with placement-owned rows;
            // resolving assetValueId here would return one shared wrapper and
            // make writes from two placements alias each other.
            object? resolved = NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                record.InstanceId,
                readOnlyFactories,
                writableFactories,
                record.Ownership);
            if (resolved is not NeoGeneratedClassValue generated
                || !ClassExtendsClass(
                    generated.classId ?? record.AssetClassId,
                    record.AssetClassId))
            {
                return null;
            }
            return resolved as TGenerated;
        }

        // -------------------------------------------------------------------
        // Generated-value resolution and type checks.
        // -------------------------------------------------------------------

        protected TGenerated? ResolveGeneratedValue<TGenerated>(
            string valueId,
            string expectedFamilyClassId)
            where TGenerated : class, INeoValueReference
        {
            if (!ValueExtendsClass(valueId, expectedFamilyClassId)) return null;
            object? resolved = NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                valueId,
                readOnlyFactories,
                writableFactories);
            return resolved as TGenerated;
        }

        internal NeoGeneratedClassValue? ResolveGeneratedClassValue(string valueId)
        {
            if (string.IsNullOrEmpty(valueId)) return null;
            object? resolved = NeoGeneratedTypesSupport.ResolveClassValue(
                client,
                valueId,
                readOnlyFactories,
                writableFactories);
            return resolved as NeoGeneratedClassValue;
        }

        protected bool ValueExtendsClass(string valueId, string expectedFamilyClassId)
        {
            if (string.IsNullOrEmpty(expectedFamilyClassId)) return true;
            if (!client.TryGetValue(valueId, out ObjectMemberValue? row)) return false;
            string? classId = row.classId;
            if (string.IsNullOrEmpty(classId)) return false;
            return ClassExtendsClass(classId!, expectedFamilyClassId);
        }

        protected bool ClassExtendsClass(string classId, string expectedClassId)
        {
            if (string.IsNullOrEmpty(expectedClassId)) return true;
            var visited = new HashSet<string>();
            string? cursor = classId;
            while (!string.IsNullOrEmpty(cursor))
            {
                if (!visited.Add(cursor!)) return false;
                if (cursor == expectedClassId) return true;
                if (!client.classes.TryGetValue(cursor!, out NeoSchemaClass schemaClass)) return false;
                cursor = schemaClass.extendsClassId;
            }
            return false;
        }

        // -------------------------------------------------------------------
        // Values-native grid model resolution. Grid value → "Children"
        // ordered list → layer link values; each link resolves its target
        // layer (Lookup) and its containment list value ("Tiles"/"Objects").
        // Every id consulted is added to `dependencyIds` so the lookup cache
        // can invalidate on value-change events.
        // -------------------------------------------------------------------

        internal IReadOnlyList<NeoGridLayerLinkModel> ResolveGridLinks(HashSet<string>? dependencyIds)
        {
            var links = new List<NeoGridLayerLinkModel>();
            dependencyIds?.Add(GridValueId);
            if (client.ResolveValueRow(GridValueId) is not ObjectMemberValue gridRow) return links;
            if (gridRow.IsRemoved) return links;
            if (string.IsNullOrEmpty(gridRow.classId)) return links;
            string? childrenKey = FindSchemaKey(gridRow.classId!, ChildrenKeyCandidates);
            if (childrenKey is null) return links;
            if (client.ResolveClassChildRow(gridRow, childrenKey)
                    is not ArrayMemberValue childrenList)
            {
                return links;
            }
            string childrenListId = childrenList.id;

            foreach (var linkValueId in ResolveListEntryIds(childrenListId, dependencyIds))
            {
                dependencyIds?.Add(linkValueId);
                if (client.ResolveValueRow(linkValueId) is not ObjectMemberValue linkRow) continue;
                if (linkRow.IsRemoved) continue;
                if (string.IsNullOrEmpty(linkRow.classId)) continue;
                var link = ResolveLinkModel(linkValueId, linkRow, dependencyIds);
                if (link is not null) links.Add(link);
            }
            return links;
        }

        private NeoGridLayerLinkModel? ResolveLinkModel(
            string linkValueId,
            ObjectMemberValue linkRow,
            HashSet<string>? dependencyIds)
        {
            string classId = linkRow.classId!;
            string? tilesKey = FindSchemaKey(classId, TilesKeyCandidates);
            if (tilesKey is not null
                && client.ResolveClassChildRow(linkRow, tilesKey)
                    is ArrayMemberValue tilesList)
            {
                string tilesListId = tilesList.id;
                string layerId = NeoWorldLayerLinkResolver.ResolveTargetLayerClassId(
                    client,
                    linkValueId,
                    classId,
                    isTileLink: true);
                string? layerOverrideValueId = ResolveLayerOverrideValueId(
                    linkValueId,
                    linkRow,
                    layerId,
                    dependencyIds);
                dependencyIds?.Add(tilesListId);
                return new NeoGridLayerLinkModel(
                    linkValueId,
                    classId,
                    layerId,
                    layerOverrideValueId,
                    tilesListId,
                    isTileLink: true);
            }

            string? objectsKey = FindSchemaKey(classId, ObjectsKeyCandidates);
            if (objectsKey is not null
                && client.ResolveClassChildRow(linkRow, objectsKey)
                    is ArrayMemberValue objectsList)
            {
                string objectsListId = objectsList.id;
                string layerId = NeoWorldLayerLinkResolver.ResolveTargetLayerClassId(
                    client,
                    linkValueId,
                    classId,
                    isTileLink: false);
                string? layerOverrideValueId = ResolveLayerOverrideValueId(
                    linkValueId,
                    linkRow,
                    layerId,
                    dependencyIds);
                dependencyIds?.Add(objectsListId);
                return new NeoGridLayerLinkModel(
                    linkValueId,
                    classId,
                    layerId,
                    layerOverrideValueId,
                    objectsListId,
                    isTileLink: false);
            }
            return null;
        }

        private string? ResolveLayerOverrideValueId(
            string linkValueId,
            ObjectMemberValue linkRow,
            string layerClassId,
            HashSet<string>? dependencyIds)
        {
            string? overrideValueId = linkRow.value is null
                ? null
                : ReadDirectReference(
                    linkRow.value,
                    "layerOverrideValueId");
            if (overrideValueId is null) return null;
            dependencyIds?.Add(overrideValueId);
            if (client.ResolveValueRow(overrideValueId) is not ObjectMemberValue overrideRow)
            {
                throw new InvalidOperationException(
                    $"Layer link '{linkValueId}' references missing layer override value '{overrideValueId}'.");
            }
            if (!string.Equals(
                overrideRow.classId,
                layerClassId,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Layer link '{linkValueId}' targets layer class '{layerClassId}', but override value '{overrideValueId}' has class '{overrideRow.classId ?? "<missing>"}'.");
            }
            if (!string.Equals(
                overrideRow.containerId,
                linkValueId,
                StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Layer override value '{overrideValueId}' must be owned by layer link '{linkValueId}', but its container is '{overrideRow.containerId ?? "<missing>"}'.");
            }
            return overrideValueId;
        }

        /// <summary>
        /// Entry value ids of a containment list resolved through the overlay
        /// cascade: inline array entries first (ordered lists and legacy
        /// factory rows), then the unordered membership join (rows carrying
        /// this list value's id as their <see cref="MemberValue.containerId"/>),
        /// id-sorted. A null container value resolves to no entries.
        /// </summary>
        internal IReadOnlyList<string> ResolveListEntryIds(
            string listValueId,
            HashSet<string>? dependencyIds)
        {
            dependencyIds?.Add(listValueId);
            var ids = new List<string>();
            if (client.ResolveValueRow(listValueId) is not ArrayMemberValue listRow) return ids;
            if (listRow.IsRemoved) return ids;
            if (listRow.value is null) return ids;
            var seen = new HashSet<string>();
            foreach (var entryId in listRow.value)
            {
                if (string.IsNullOrEmpty(entryId)) continue;
                if (!seen.Add(entryId)) continue;
                var entryRow = client.ResolveValueRow(entryId);
                if (entryRow is null || entryRow.IsRemoved) continue;
                ids.Add(entryId);
            }
            foreach (var entryId in client.GetUnorderedListEntryIds(listValueId))
            {
                if (!seen.Add(entryId)) continue;
                ids.Add(entryId);
            }
            return ids;
        }

        internal List<NeoTilePlacementRecord> BuildTileLayerRecords(
            string layerId,
            HashSet<string>? dependencyIds)
        {
            var records = new List<NeoTilePlacementRecord>();
            int order = 0;
            var links = ResolveGridLinks(dependencyIds);
            foreach (var link in links)
            {
                if (!link.IsTileLink || link.LayerId != layerId) continue;
                var ownership = ResolveCollectionOwnership(link.ListValueId);
                foreach (var placementId in ResolveListEntryIds(link.ListValueId, dependencyIds))
                {
                    var record = ResolvePlacementRecord(
                        placementId,
                        placementId,
                        origin: Vector2Int.zero,
                        order,
                        link.LinkValueId,
                        sourceObjectInstanceId: null,
                        CellKeyCandidates,
                        dependencyIds,
                        ownership);
                    if (record is null) continue;
                    records.Add(record);
                    order += 1;
                }
            }

            // Object-carried tile layer links (stamps): each placed object's
            // "Children" may host TileLayerLink values whose tiles project
            // into this layer at the object's origin.
            foreach (var link in links)
            {
                if (link.IsTileLink) continue;
                int objectIndex = 0;
                foreach (var objectValueId in ResolveListEntryIds(link.ListValueId, dependencyIds))
                {
                    AppendObjectCarriedTiles(
                        layerId,
                        objectValueId,
                        objectIndex,
                        records,
                        dependencyIds);
                    objectIndex += 1;
                }
            }
            return records;
        }

        private void AppendObjectCarriedTiles(
            string layerId,
            string objectValueId,
            int objectIndex,
            List<NeoTilePlacementRecord> records,
            HashSet<string>? dependencyIds)
        {
            dependencyIds?.Add(objectValueId);
            if (client.ResolveValueRow(objectValueId) is not ObjectMemberValue objectRow) return;
            if (objectRow.IsRemoved) return;
            if (string.IsNullOrEmpty(objectRow.classId)) return;
            Vector2Int origin = ReadObjectOrigin(objectRow, dependencyIds);

            int linkIndex = -1;
            foreach (var carried in ResolveObjectCarriedLinks(objectValueId, dependencyIds))
            {
                linkIndex = carried.ChildIndex;
                if (carried.LayerId != layerId) continue;
                int sourceOrder = objectIndex * 1000 + linkIndex;
                int tileIndex = 0;
                var ownership = ResolveCollectionOwnership(carried.TilesListValueId);
                foreach (var entryId in ResolveListEntryIds(carried.TilesListValueId, dependencyIds))
                {
                    var record = ResolveObjectCarriedTileRecord(
                        objectValueId,
                        carried.LinkValueId,
                        entryId,
                        origin,
                        sourceOrder,
                        tileIndex,
                        dependencyIds,
                        ownership);
                    tileIndex += 1;
                    if (record is null) continue;
                    records.Add(record);
                }
            }
        }

        private readonly struct ObjectCarriedLink
        {
            public ObjectCarriedLink(string linkValueId, string layerId, string tilesListValueId, int childIndex)
            {
                LinkValueId = linkValueId;
                LayerId = layerId;
                TilesListValueId = tilesListValueId;
                ChildIndex = childIndex;
            }

            public string LinkValueId { get; }
            public string LayerId { get; }
            public string TilesListValueId { get; }
            public int ChildIndex { get; }
        }

        private IEnumerable<ObjectCarriedLink> ResolveObjectCarriedLinks(
            string objectValueId,
            HashSet<string>? dependencyIds)
        {
            dependencyIds?.Add(objectValueId);
            if (client.ResolveValueRow(objectValueId) is not ObjectMemberValue objectRow) yield break;
            if (objectRow.IsRemoved) yield break;
            if (string.IsNullOrEmpty(objectRow.classId)) yield break;
            string? childrenKey = FindSchemaKey(objectRow.classId!, ChildrenKeyCandidates);
            if (childrenKey is null) yield break;
            if (client.ResolveClassChildRow(objectRow, childrenKey)
                    is not ArrayMemberValue childrenList)
            {
                yield break;
            }
            string childrenListId = childrenList.id;

            int childIndex = -1;
            foreach (var childValueId in ResolveListEntryIds(childrenListId, dependencyIds))
            {
                childIndex += 1;
                dependencyIds?.Add(childValueId);
                if (client.ResolveValueRow(childValueId) is not ObjectMemberValue childRow) continue;
                if (childRow.IsRemoved) continue;
                if (string.IsNullOrEmpty(childRow.classId)) continue;
                string? tilesKey = FindSchemaKey(childRow.classId!, TilesKeyCandidates);
                if (tilesKey is null) continue;
                if (client.ResolveClassChildRow(childRow, tilesKey)
                        is not ArrayMemberValue tilesList)
                {
                    continue;
                }
                string tilesListId = tilesList.id;
                string targetLayerId = NeoWorldLayerLinkResolver.ResolveTargetLayerClassId(
                    client,
                    childValueId,
                    childRow.classId!,
                    isTileLink: true);
                dependencyIds?.Add(tilesListId);
                yield return new ObjectCarriedLink(childValueId, targetLayerId, tilesListId, childIndex);
            }
        }

        private NeoTilePlacementRecord? ResolveObjectCarriedTileRecord(
            string objectValueId,
            string linkValueId,
            string entryId,
            Vector2Int origin,
            int sourceOrder,
            int tileIndex,
            HashSet<string>? dependencyIds,
            NeoValueOwnership? ownership)
        {
            return ResolvePlacementRecord(
                $"{objectValueId}:{linkValueId}:{entryId}",
                entryId,
                origin,
                CombineTileLayerLinkOrder(sourceOrder, tileIndex),
                linkValueId,
                objectValueId,
                CellKeyCandidates,
                dependencyIds,
                ownership);
        }

        private NeoTilePlacementRecord? ResolvePlacementRecord(
            string instanceId,
            string placementValueId,
            Vector2Int origin,
            int order,
            string sourceTileLayerLinkId,
            string? sourceObjectInstanceId,
            string[] cellKeyCandidates,
            HashSet<string>? dependencyIds,
            NeoValueOwnership? ownership)
        {
            dependencyIds?.Add(placementValueId);
            if (client.ResolveValueRow(placementValueId) is not ObjectMemberValue placementRow) return null;
            if (placementRow.IsRemoved) return null;
            if (string.IsNullOrEmpty(placementRow.classId)) return null;

            string? cellKey = FindSchemaKey(placementRow.classId!, cellKeyCandidates);
            if (cellKey is null) return null;
            if (client.ResolveClassChildRow(placementRow, cellKey)
                    is not MemberValue cellRow)
            {
                return null;
            }
            string cellValueId = cellRow.id;
            dependencyIds?.Add(cellValueId);
            Vector2Int? cell = ReadCellRow(cellValueId);
            if (cell is null) return null;

            return new NeoTilePlacementRecord(
                instanceId,
                placementValueId,
                origin + cell.Value,
                placementRow.classId!,
                ownership,
                order,
                sourceTileLayerLinkId,
                sourceObjectInstanceId,
                placementRow.updatedAt.EpochMilliseconds,
                cellValueId);
        }

        protected static string? ReadDirectReference(
            IReadOnlyDictionary<string, string> record,
            params string[] keys)
        {
            foreach (string key in keys)
            {
                if (record.TryGetValue(key, out string value)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
            return null;
        }

        private NeoValueOwnership? ResolveCollectionOwnership(string listValueId)
        {
            NeoValueOwnership? ownership = client.TryInferMemberForValueId(listValueId, out Member? member)
                ? client.DeclaredOwnership(member) : null;
            if (ownership is null && client.TryGetValueOwnership(listValueId, out var inherited)
                && inherited != NeoValueOwnership.Asset)
                ownership = inherited;
            return ownership;
        }

        internal List<NeoObjectPlacementRecord> BuildObjectLayerRecords(
            string layerId,
            HashSet<string>? dependencyIds)
        {
            var records = new List<NeoObjectPlacementRecord>();
            int order = 0;
            foreach (var link in ResolveGridLinks(dependencyIds))
            {
                if (link.IsTileLink || link.LayerId != layerId) continue;
                var ownership = ResolveCollectionOwnership(link.ListValueId);
                foreach (var objectValueId in ResolveListEntryIds(link.ListValueId, dependencyIds))
                {
                    dependencyIds?.Add(objectValueId);
                    if (client.ResolveValueRow(objectValueId) is not ObjectMemberValue objectRow) continue;
                    if (objectRow.IsRemoved) continue;
                    if (string.IsNullOrEmpty(objectRow.classId)) continue;
                    string? assetValueId = objectRow.value is null
                        ? null
                        : ReadDirectReference(objectRow.value, "assetValueId");
                    Vector2Int cell = ReadObjectOrigin(objectRow, dependencyIds);
                    var footprint = ReadObjectFootprint(objectRow, cell, dependencyIds);
                    records.Add(new NeoObjectPlacementRecord(
                        objectValueId,
                        cell,
                        footprint,
                        order,
                        objectRow.classId!,
                        assetValueId,
                        ownership));
                    order += 1;
                }
            }
            return records;
        }

        internal Vector2Int ReadObjectOrigin(
            ObjectMemberValue objectRow,
            HashSet<string>? dependencyIds)
        {
            string? positionKey = FindSchemaKey(objectRow.classId!, PositionKeyCandidates);
            if (positionKey is null) return Vector2Int.zero;
            if (client.ResolveClassChildRow(objectRow, positionKey)
                    is not MemberValue positionRow)
            {
                return Vector2Int.zero;
            }
            string positionRowId = positionRow.id;
            dependencyIds?.Add(positionRowId);
            return ReadCellRow(positionRowId) ?? Vector2Int.zero;
        }

        private IReadOnlyList<Vector2Int> ReadObjectFootprint(
            ObjectMemberValue objectRow,
            Vector2Int origin,
            HashSet<string>? dependencyIds)
        {
            string? placementTilesKey = FindSchemaKey(
                objectRow.classId!,
                PlacementTilesKeyCandidates);
            if (placementTilesKey is null
                || client.ResolveClassChildRow(objectRow, placementTilesKey)
                    is not ArrayMemberValue placementTilesList)
            {
                return new[] { origin };
            }
            string placementTilesListId = placementTilesList.id;

            IReadOnlyList<string> placementValueIds = ResolveListEntryIds(
                placementTilesListId,
                dependencyIds);
            return ReadObjectFootprint(
                placementValueIds,
                origin,
                dependencyIds);
        }

        protected IReadOnlyList<Vector2Int> ReadProspectiveObjectFootprint(
            string objectClassId,
            string? objectValueId,
            Vector2Int origin)
        {
            IReadOnlyDictionary<string, string>? authoredValues =
                objectValueId is not null
                && client.ResolveValueRow(objectValueId) is ObjectMemberValue assetRow
                    ? assetRow.value
                    : null;
            MergedSchemaEntry? placementTilesEntry = FindSchemaEntry(
                objectClassId,
                PlacementTilesKeyCandidates);
            if (placementTilesEntry is null)
            {
                return new[] { origin };
            }

            if (authoredValues is not null
                && authoredValues.TryGetValue(
                    placementTilesEntry.schemaKey,
                    out string placementTilesListId))
            {
                return ReadObjectFootprint(
                    ResolveListEntryIds(placementTilesListId, null),
                    origin,
                    null);
            }

            if (!client.TryGetMember(
                    placementTilesEntry.memberId,
                    out Json.Member? placementTilesMember)
                || placementTilesMember is not ListMember listMember)
            {
                return new[] { origin };
            }
            if (listMember.valueId is not null)
            {
                return ReadObjectFootprint(
                    ResolveListEntryIds(listMember.valueId, null),
                    origin,
                    null);
            }
            return ReadObjectFootprint(
                listMember.defaultValue?.value ?? Array.Empty<string>(),
                origin,
                null);
        }

        private IReadOnlyList<Vector2Int> ReadObjectFootprint(
            IReadOnlyList<string> placementValueIds,
            Vector2Int origin,
            HashSet<string>? dependencyIds)
        {
            if (placementValueIds.Count == 0)
            {
                return new[] { origin };
            }

            var cells = new List<Vector2Int>(placementValueIds.Count);
            HashSet<Vector2Int>? seen = null;
            string? lastPlacementClassId = null;
            string? lastCellKey = null;
            foreach (string placementValueId in placementValueIds)
            {
                dependencyIds?.Add(placementValueId);
                if (client.ResolveValueRow(placementValueId)
                    is not ObjectMemberValue placementRow)
                {
                    continue;
                }
                if (placementRow.IsRemoved
                    || string.IsNullOrEmpty(placementRow.classId))
                {
                    continue;
                }

                if (!string.Equals(
                    lastPlacementClassId,
                    placementRow.classId,
                    StringComparison.Ordinal))
                {
                    lastPlacementClassId = placementRow.classId;
                    lastCellKey = FindSchemaKey(
                        placementRow.classId!,
                        CellKeyCandidates);
                }
                if (lastCellKey is null
                    || client.ResolveClassChildRow(placementRow, lastCellKey)
                        is not MemberValue cellRow)
                {
                    continue;
                }
                string cellValueId = cellRow.id;

                dependencyIds?.Add(cellValueId);
                Vector2Int? offset = ReadCellRow(cellValueId);
                if (offset is null) continue;
                Vector2Int cell = origin + offset.Value;
                if (cells.Count == 0)
                {
                    cells.Add(cell);
                    continue;
                }
                seen ??= new HashSet<Vector2Int>(cells);
                if (seen.Add(cell)) cells.Add(cell);
            }

            if (cells.Count == 0) return new[] { origin };
            return cells;
        }

        internal IReadOnlyList<string> ResolveTileLayerIds()
        {
            var ids = new List<string>();
            foreach (var link in ResolveGridLinks(null))
            {
                if (!link.IsTileLink) continue;
                if (!ids.Contains(link.LayerId)) ids.Add(link.LayerId);
            }
            return ids;
        }

        internal IReadOnlyList<string> ResolveObjectLayerIds()
        {
            var ids = new List<string>();
            foreach (var link in ResolveGridLinks(null))
            {
                if (link.IsTileLink) continue;
                if (!ids.Contains(link.LayerId)) ids.Add(link.LayerId);
            }
            return ids;
        }

        // -------------------------------------------------------------------
        // Row-shape helpers.
        // -------------------------------------------------------------------

        private Vector2Int? ReadCellRow(string valueId)
        {
            var vector = ReadVectorRow(valueId);
            if (vector is null) return null;
            return new Vector2Int(
                Mathf.RoundToInt(vector.Value.x),
                Mathf.RoundToInt(vector.Value.y));
        }

        private Vector2? ReadVectorRow(string valueId)
        {
            switch (client.ResolveValueRow(valueId))
            {
                case Vector2MemberValue v2 when v2.value is not null:
                    return new Vector2(v2.value.x, v2.value.y);
                case Vector3MemberValue v3 when v3.value is not null:
                    return new Vector2(v3.value.x, v3.value.y);
                default:
                    return null;
            }
        }

        /// <summary>Case-insensitive schema-key match over a class's merged
        /// inheritance schema, first candidate wins.</summary>
        protected string? FindSchemaKey(string classId, string[] keyCandidates)
        {
            return FindSchemaEntry(classId, keyCandidates)?.schemaKey;
        }

        /// <summary>The member id backing a schema key on a class's merged
        /// schema (first candidate match), or null.</summary>
        protected string? FindSchemaMemberId(string classId, string[] keyCandidates)
        {
            return FindSchemaEntry(classId, keyCandidates)?.memberId;
        }

        private MergedSchemaEntry? FindSchemaEntry(
            string classId,
            string[] keyCandidates)
        {
            var merged = ResolveMergedSchemaEntries(classId);
            if (merged is null) return null;
            foreach (var candidate in keyCandidates)
            {
                foreach (var entry in merged)
                {
                    if (string.Equals(entry.schemaKey, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry;
                    }
                }
            }
            return null;
        }

        private IList<MergedSchemaEntry>? ResolveMergedSchemaEntries(string classId)
        {
            if (!client.classes.ContainsKey(classId)) return null;
            try
            {
                return NeoSchemaClassInheritance.MergeInstanceSchema(
                    NeoSchemaClassInheritance.ResolveChain(
                        classId,
                        id => client.classes.TryGetValue(id, out NeoSchemaClass match) ? match : null),
                    id => client.members.TryGetValue(id, out JsonMember member)
                        ? member
                        : null);
            }
            catch (CircularInheritanceError)
            {
                return null;
            }
        }

        protected static int CombineTileLayerLinkOrder(int sourceOrder, int tileOrder)
        {
            long combined = ((long)sourceOrder * 1000L) + tileOrder;
            if (combined > int.MaxValue) return int.MaxValue;
            if (combined < int.MinValue) return int.MinValue;
            return (int)combined;
        }
    }

    public sealed class NeoTileGridPrimitive : NeoReadOnlyTileGridPrimitive
    {
        private readonly NeoValueOwnership writeOwnership;

        private NeoTileGridPrimitive(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>? readOnlyFactories = null,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>? writableFactories = null,
            IReadOnlyDictionary<Type, string>? classIdsByType = null,
            NeoValueOwnership writeOwnership = NeoValueOwnership.Save)
            : base(client, gridValueId, readOnlyFactories, writableFactories, classIdsByType)
        {
            this.writeOwnership = writeOwnership;
        }

        public NeoValueOwnership WriteOwnership => writeOwnership;
        public NeoTileGridLifecycle? Lifecycle { get; set; }

        public static new NeoTileGridPrimitive Resolve(NeoClient client, string gridValueId)
        {
            return ResolveForSave(client, gridValueId);
        }

        public static new NeoTileGridPrimitive Resolve(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> writableFactories)
        {
            return ResolveForSave(client, gridValueId, readOnlyFactories, writableFactories);
        }

        public static NeoTileGridPrimitive Resolve(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> writableFactories,
            IReadOnlyDictionary<Type, string> classIdsByType)
        {
            return ResolveForSave(
                client,
                gridValueId,
                readOnlyFactories,
                writableFactories,
                classIdsByType);
        }

        public static NeoTileGridPrimitive ResolveForSave(NeoClient client, string gridValueId)
        {
            return ResolveForSave(client, gridValueId, EmptyReadOnlyFactories, EmptyWritableFactories);
        }

        public static NeoTileGridPrimitive ResolveForSave(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> writableFactories)
        {
            return ResolveForSave(
                client,
                gridValueId,
                readOnlyFactories,
                writableFactories,
                EmptyClassIdsByType);
        }

        public static NeoTileGridPrimitive ResolveForSave(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> writableFactories,
            IReadOnlyDictionary<Type, string> classIdsByType)
        {
            return ResolveWithOwnership(
                client,
                gridValueId,
                readOnlyFactories,
                writableFactories,
                classIdsByType,
                NeoValueOwnership.Save);
        }

        public static NeoTileGridPrimitive ResolveForSession(NeoClient client, string gridValueId)
        {
            return ResolveForSession(client, gridValueId, EmptyReadOnlyFactories, EmptyWritableFactories);
        }

        public static NeoTileGridPrimitive ResolveForSession(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> writableFactories)
        {
            return ResolveForSession(
                client,
                gridValueId,
                readOnlyFactories,
                writableFactories,
                EmptyClassIdsByType);
        }

        public static NeoTileGridPrimitive ResolveForSession(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> writableFactories,
            IReadOnlyDictionary<Type, string> classIdsByType)
        {
            return ResolveWithOwnership(
                client,
                gridValueId,
                readOnlyFactories,
                writableFactories,
                classIdsByType,
                NeoValueOwnership.Session);
        }

        private static NeoTileGridPrimitive ResolveWithOwnership(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> writableFactories,
            IReadOnlyDictionary<Type, string> classIdsByType,
            NeoValueOwnership writeOwnership)
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            return new NeoTileGridPrimitive(
                client,
                gridValueId,
                readOnlyFactories,
                writableFactories,
                classIdsByType,
                writeOwnership);
        }

        public TLayer BindWritableTileLayer<TLayer>(
            string layerClassId,
            IReadOnlyCollection<string> importedClassIds)
            where TLayer : class
        {
            string? layerOverrideValueId = ResolveLayerOverrideValueId(
                layerClassId,
                isTileLayer: true);
            object value = CreateWritableLayerValue(
                layerClassId,
                layerOverrideValueId,
                $"__neo_grid_tile_layer_rw:{bindingInstanceId}:{GridValueId}:{layerClassId}");
            if (value is not TLayer typed || value is not NeoGeneratedTileLayerValue layer)
            {
                throw new InvalidOperationException(
                    $"Generated class '{layerClassId}' is not the requested authored tile layer '{typeof(TLayer).Name}'.");
            }
            layer.BindGridLayer(new NeoTileLayerBinding(
                this,
                layerClassId,
                layerOverrideValueId,
                importedClassIds));
            return typed;
        }

        public TLayer BindWritableObjectLayer<TLayer>(
            string layerClassId,
            IReadOnlyCollection<string> importedClassIds)
            where TLayer : class
        {
            string? layerOverrideValueId = ResolveLayerOverrideValueId(
                layerClassId,
                isTileLayer: false);
            object value = CreateWritableLayerValue(
                layerClassId,
                layerOverrideValueId,
                $"__neo_grid_object_layer_rw:{bindingInstanceId}:{GridValueId}:{layerClassId}");
            if (value is not TLayer typed || value is not NeoGeneratedObjectLayerValue layer)
            {
                throw new InvalidOperationException(
                    $"Generated class '{layerClassId}' is not the requested authored object layer '{typeof(TLayer).Name}'.");
            }
            layer.BindGridLayer(new NeoObjectLayerBinding(
                this,
                layerClassId,
                layerOverrideValueId,
                importedClassIds));
            return typed;
        }

        private object CreateWritableLayerValue(
            string classId,
            string? overrideValueId,
            string memberId)
        {
            if (!writableFactories.TryGetValue(classId, out var factory))
            {
                throw new InvalidOperationException(
                    $"No generated writable factory exists for layer class '{classId}'. Regenerate the project's C# types.");
            }
            var node = new NeoMemberClassWritable(
                client,
                CreateClassDefaultMember(classId, memberId),
                overrideValueId,
                writeOwnership);
            object value = factory(client, node);
            if (overrideValueId is null && value is NeoGeneratedClassValue generated)
            {
                generated.MarkClassDefaultReference();
            }
            return value;
        }

        // -------------------------------------------------------------------
        // Mutations — plain value writes against the containment model:
        // placements are rows in the requested ownership carrying the layer
        // link's list value id as their containerId; removal is a tombstone
        // (authored members) or a row drop (overlay-created members).
        // -------------------------------------------------------------------

        public NeoPlacementResult TrySetTile(
            string layerClassId,
            Vector2Int cell,
            string assetClassId,
            IReadOnlyCollection<string> allowedClassIds)
        {
            try
            {
                var validation = ValidateClassBackedAsset(
                    assetClassId,
                    "tile",
                    allowedClassIds,
                    out NeoGeneratedClassValue? generated);
                return validation ?? TrySetTileCore(
                    layerClassId,
                    cell,
                    assetClassId,
                    generated!);
            }
            catch (NeoPlacementValidationException error)
            {
                return NeoPlacementResult.Error(error.ErrorCode, error.Message);
            }
        }

        private NeoPlacementResult TrySetTileCore(
            string layerId,
            Vector2Int cell,
            string assetClassId,
            NeoGeneratedClassValue generatedTile)
        {
            NeoTilePlacementRecord? existing = null;
            foreach (var candidate in LookupCache.TileCandidatesAt(layerId, cell))
            {
                existing = candidate;
            }

            if (existing is not null)
            {
                Lifecycle?.BeforeSetTile(new NeoTileSetContext(
                    this,
                    layerId,
                    cell,
                    existing.InstanceId,
                    generatedTile,
                    assetClassId,
                    BuildTileInstanceJson(existing, layerId)));
                var writeError = WritePlacementTileClass(
                    existing,
                    assetClassId);
                if (writeError is not null) return writeError;
                return NeoPlacementResult.Success();
            }

            NeoGridLayerLinkModel? targetLink = ResolveDirectWriteTargetLink(
                layerId,
                isTileLayer: true,
                writeOwnership,
                out bool ambiguousTarget);
            if (targetLink is null)
            {
                if (ambiguousTarget)
                {
                    return NeoPlacementResult.Error(
                        "tile-grid-layer-link-ambiguous",
                        $"Grid '{GridValueId}' has multiple override-owning tile links targeting layer '{layerId}'; cannot choose where to create a direct tile placement.");
                }
                return NeoPlacementResult.Error(
                    "tile-grid-layer-link-missing",
                    $"Grid '{GridValueId}' has no tile layer link targeting layer '{layerId}'; cannot place a tile.");
            }

            string instanceId = Guid.NewGuid().ToString();
            Lifecycle?.BeforeSetTile(new NeoTileSetContext(
                this,
                layerId,
                cell,
                instanceId,
                generatedTile,
                assetClassId,
                null));

            var createError = CreatePlacementRows(
                targetLink,
                instanceId,
                cell,
                assetClassId);
            if (createError is not null) return createError;
            return NeoPlacementResult.Success();
        }

        public NeoPlacementResult TryConvertTileClass(
            NeoTileInstanceId instanceId,
            string assetClassId,
            IReadOnlyCollection<string> allowedClassIds)
        {
            try
            {
                var validation = ValidateClassBackedAsset(
                    assetClassId,
                    "tile",
                    allowedClassIds,
                    out NeoGeneratedClassValue? generated);
                return validation ?? TryConvertTileCore(
                    instanceId,
                    assetClassId,
                    generated!);
            }
            catch (NeoPlacementValidationException error)
            {
                return NeoPlacementResult.Error(error.ErrorCode, error.Message);
            }
        }

        private NeoPlacementResult TryConvertTileCore(
            NeoTileInstanceId instanceId,
            string assetClassId,
            NeoGeneratedClassValue generatedTarget)
        {
            if (!TryFindTileRecord(instanceId.Value, out var record, out string layerId))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-instance-missing",
                    $"Tile instance '{instanceId.Value}' was not found in grid '{GridValueId}'.");
            }

            Lifecycle?.BeforeConvertTile(new NeoTileConvertContext(
                this,
                layerId,
                record.Cell,
                instanceId.Value,
                generatedTarget,
                assetClassId,
                BuildTileInstanceJson(record, layerId)));

            var writeError = WritePlacementTileClass(
                record,
                assetClassId);
            if (writeError is not null) return writeError;
            return NeoPlacementResult.Success();
        }

        public NeoPlacementResult TryResetTile(NeoTileInstanceId instanceId)
        {
            try
            {
                if (!TryFindTileRecord(instanceId.Value, out var record, out string layerId))
                {
                    return NeoPlacementResult.Error(
                        "tile-grid-instance-missing",
                        $"Tile instance '{instanceId.Value}' was not found in grid '{GridValueId}'.");
                }
                Lifecycle?.BeforeResetTile(new NeoTileResetContext(
                    this,
                    layerId,
                    record.Cell,
                    instanceId.Value,
                    BuildTileInstanceJson(record, layerId)));

                // Restore authored: drop the write-ownership overlay rows (and any
                // tombstones) for the placement subtree so the authored rows —
                // when they exist — resurface. A runtime-created placement has no
                // authored rows to fall back to, so a reset removes it entirely.
                var plan = new NeoWritePlan(client);
                if (client.HasWritableValue(writeOwnership, record.PlacementValueId))
                    plan.Remove(writeOwnership, record.PlacementValueId);
                if (record.CellValueId is not null && client.HasWritableValue(writeOwnership, record.CellValueId))
                    plan.Remove(writeOwnership, record.CellValueId);
                plan.Commit();
                return NeoPlacementResult.Success();
            }
            catch (NeoPlacementValidationException error)
            {
                return NeoPlacementResult.Error(error.ErrorCode, error.Message);
            }
        }

        public NeoPlacementResult TryRemoveTile(NeoTileInstanceId instanceId)
        {
            try
            {
                if (!TryFindTileRecord(instanceId.Value, out var record, out string layerId))
                {
                    return NeoPlacementResult.Error(
                        "tile-grid-instance-missing",
                        $"Tile instance '{instanceId.Value}' was not found in grid '{GridValueId}'.");
                }
                Lifecycle?.BeforeRemoveTile(new NeoTileRemoveContext(
                    this,
                    layerId,
                    record.Cell,
                    instanceId.Value,
                    BuildTileInstanceJson(record, layerId)));

                RemoveMemberRow(record.PlacementValueId);
                return NeoPlacementResult.Success();
            }
            catch (NeoPlacementValidationException error)
            {
                return NeoPlacementResult.Error(error.ErrorCode, error.Message);
            }
        }

        public NeoPlacementResult TrySpawn(
            string layerId,
            Vector2Int cell,
            NeoGeneratedClassValue obj,
            IReadOnlyCollection<string>? allowedClassIds = null)
        {
            try
            {
                if (obj is null)
                    throw new ArgumentNullException(nameof(obj));
                if (!ReferenceEquals(obj.Client, client))
                    throw new ArgumentException("Cannot spawn an object constructed by another client.", nameof(obj));
                if (!TryValidateGeneratedValue(
                        obj, string.Empty, "object",
                        out string? valueId, out string? classId,
                        out NeoGeneratedClassValue? generated,
                        allowedClassIds, out NeoPlacementResult? error))
                    return error!;
                if (!client.HasWorldKind(classId, "object"))
                    throw new ArgumentException($"Class '{classId}' is not a placeable object.", nameof(obj));
                client.AssertConstructedValueAdoptable(writeOwnership, obj);
                return TrySpawnObjectReference(layerId, cell, classId!, valueId, generated!);
            }
            catch (NeoPlacementValidationException error)
            {
                return NeoPlacementResult.Error(error.ErrorCode, error.Message);
            }
        }

        private NeoPlacementResult TrySpawnObjectReference(
            string layerId,
            Vector2Int cell,
            string objectClassId,
            string? objectValueId,
            NeoGeneratedClassValue generatedObject)
        {
            IReadOnlyList<Vector2Int> footprint = ReadProspectiveObjectFootprint(
                objectClassId,
                objectValueId,
                cell);
            if (LookupCache.TryGetObjectCandidateAtAny(
                layerId,
                footprint,
                out NeoObjectPlacementRecord? occupant,
                out Vector2Int occupiedCell))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-object-cell-occupied",
                    $"Object layer '{layerId}' already has object instance '{occupant!.InstanceId}' at cell ({occupiedCell.x}, {occupiedCell.y}).");
            }

            NeoGridLayerLinkModel? targetLink = ResolveDirectWriteTargetLink(
                layerId,
                isTileLayer: false,
                writeOwnership,
                out bool ambiguousTarget);
            if (targetLink is null)
            {
                if (ambiguousTarget)
                {
                    return NeoPlacementResult.Error(
                        "tile-grid-layer-link-ambiguous",
                        $"Grid '{GridValueId}' has multiple override-owning object links targeting layer '{layerId}'; cannot choose where to create a direct object placement.");
                }
                return NeoPlacementResult.Error(
                    "tile-grid-layer-link-missing",
                    $"Grid '{GridValueId}' has no object layer link targeting layer '{layerId}'; cannot spawn an object.");
            }
            if (!client.TryInferMemberForValueId(targetLink.ListValueId, out Json.Member? listMember)
                || listMember is not ListMember objectsMember)
                throw new InvalidOperationException($"Object list '{targetLink.ListValueId}' has no List member declaration.");
            var position = generatedObject.BackingNode.Get<NeoMemberVector3>("Position");
            string instanceId = objectValueId!;
            Lifecycle?.BeforeSpawnObject(new NeoObjectSpawnContext(
                this,
                layerId,
                cell,
                instanceId,
                generatedObject,
                objectValueId,
                objectClassId));

            var plan = new NeoWritePlan(client);
            NeoValueOwnership sourceOwnership = generatedObject.ValueOwnership;
            if (!client.TryGetValue(sourceOwnership, instanceId, out ObjectMemberValue? source))
                throw new InvalidOperationException($"Constructed object '{instanceId}' has no class row.");
            var objectRow = (ObjectMemberValue)client.CloneRowForWrite(source);
            objectRow.value ??= new Dictionary<string, string>();
            MemberValue? boundPosition = client.ResolveClassChildRow(source, "Position", sourceOwnership);
            var positionRow = position.value is Vector3MemberValue priorPosition
                ? (Vector3MemberValue)client.CloneRowForWrite(priorPosition)
                : new Vector3MemberValue { createdAt = NeoTimestamp.Now() };
            if (boundPosition is null) positionRow.id = Guid.NewGuid().ToString();
            positionRow.value = new NeoVector3Value { x = cell.x, y = cell.y, z = position.value?.value?.z ?? 0 };
            positionRow.updatedAt = NeoTimestamp.Now();
            positionRow.mapKey = source.mapKey;
            objectRow.value["Position"] = positionRow.id;
            plan.Set(sourceOwnership, positionRow);
            plan.Set(sourceOwnership, objectRow);
            // Adoption shares the collection's cached child with the caller's
            // constructed object. Keep that node alive with the client graph.
            var objects = (NeoMemberListWritable)NeoMember.CreateWritable(
                client, objectsMember, targetLink.ListValueId, writeOwnership);
            objects.PrepareAddSerialized(plan, NeoValueWritePayload.FromValueReference(instanceId, generatedObject));
            plan.Commit();
            return NeoPlacementResult.Success();
        }

        public NeoPlacementResult TryDespawnObject(NeoObjectInstanceId instanceId)
        {
            try
            {
                if (!TryFindObjectRecord(instanceId.Value, out var record, out string layerId))
                {
                    return NeoPlacementResult.Error(
                        "tile-grid-instance-missing",
                        $"Object instance '{instanceId.Value}' was not found in grid '{GridValueId}'.");
                }
                Lifecycle?.BeforeDespawnObject(new NeoObjectDespawnContext(
                    this,
                    layerId,
                    record.Cell,
                    instanceId.Value,
                    BuildObjectInstanceJson(record, layerId)));

                RemoveMemberRow(record.InstanceId);
                return NeoPlacementResult.Success();
            }
            catch (NeoPlacementValidationException error)
            {
                return NeoPlacementResult.Error(error.ErrorCode, error.Message);
            }
        }

        // -------------------------------------------------------------------
        // Mutation plumbing.
        // -------------------------------------------------------------------

        private bool TryFindTileRecord(
            string instanceId,
            out NeoTilePlacementRecord? record,
            out string layerId)
        {
            foreach (var candidateLayerId in ResolveTileLayerIds())
            {
                foreach (var candidate in LookupCache.TileRecords(candidateLayerId))
                {
                    if (candidate.InstanceId != instanceId) continue;
                    record = candidate;
                    layerId = candidateLayerId;
                    return true;
                }
            }
            record = null;
            layerId = "";
            return false;
        }

        private bool TryFindObjectRecord(
            string instanceId,
            out NeoObjectPlacementRecord? record,
            out string layerId)
        {
            foreach (var candidateLayerId in ResolveObjectLayerIds())
            {
                foreach (var candidate in LookupCache.ObjectRecords(candidateLayerId))
                {
                    if (candidate.InstanceId != instanceId) continue;
                    record = candidate;
                    layerId = candidateLayerId;
                    return true;
                }
            }
            record = null;
            layerId = "";
            return false;
        }

        private NeoPlacementResult? CreatePlacementRows(
            NeoGridLayerLinkModel link,
            string placementValueId,
            Vector2Int cell,
            string assetClassId)
        {
            string? tilesMemberId = FindSchemaMemberId(link.LinkClassId, TilesKeyCandidatesForWrite);
            if (tilesMemberId is null)
            {
                return NeoPlacementResult.Error(
                    "tile-grid-link-tiles-member-missing",
                    $"Tile layer link class '{link.LinkClassId}' has no 'Tiles' schema member; cannot create a placement.");
            }
            if (!client.members.TryGetValue(tilesMemberId, out Json.Member tilesMember)
                || tilesMember is not ListMember tilesList)
            {
                return NeoPlacementResult.Error(
                    "tile-grid-link-tiles-member-invalid",
                    $"Member '{tilesMemberId}' backing the link's Tiles list is not a List member.");
            }
            if (!client.members.TryGetValue(tilesList.entryMemberId, out Json.Member entryMember)
                || entryMember is not ClassMember entryClassMember
                || string.IsNullOrEmpty(entryClassMember.classId))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-placement-entry-member-invalid",
                    $"Entry member '{tilesList.entryMemberId}' of the link's Tiles list does not declare a placement class.");
            }
            string placementClassId = assetClassId;
            string? cellKey = FindSchemaKey(placementClassId, CellKeyCandidatesForWrite);
            if (cellKey is null)
            {
                return NeoPlacementResult.Error(
                    "tile-grid-placement-cell-key-missing",
                    $"Placement class '{placementClassId}' has no 'Cell' schema key.");
            }
            var now = NeoTimestamp.Now();
            // Storage partitions: the placement subtree lives in its
            // container's partition. The placement row would inherit through
            // its containerId; its owned Cell child has no
            // containment edge of their own, so the whole subtree is stamped
            // here at creation.
            string? partitionMapKey = client.ResolveValueRow(link.ListValueId)?.mapKey;
            var cellRow = new Vector2MemberValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = now,
                updatedAt = now,
                mapKey = partitionMapKey,
                value = new NeoVector2Value { x = cell.x, y = cell.y },
            };
            var placementValue = new Dictionary<string, string>
            {
                [cellKey] = cellRow.id,
            };
            var placementRow = new ObjectMemberValue
            {
                id = placementValueId,
                createdAt = now,
                updatedAt = now,
                classId = placementClassId,
                containerId = link.ListValueId,
                mapKey = partitionMapKey,
                value = placementValue,
            };
            client.SetWritableValues(writeOwnership, new MemberValue[] { cellRow, placementRow });
            return null;
        }

        private NeoPlacementResult? WritePlacementTileClass(
            NeoTilePlacementRecord record,
            string assetClassId)
        {
            if (!client.TryGetWritableShadowSource(
                    writeOwnership,
                    record.PlacementValueId,
                    out ObjectMemberValue? existing))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-placement-row-missing",
                    $"Tile placement row '{record.PlacementValueId}' could not be resolved.");
            }
            client.ConvertTile(writeOwnership, existing.id, assetClassId);
            return null;
        }

        /// <summary>
        /// Removes a containment member row per the overlay rules: an
        /// authored member (or a member living in a lower overlay than the
        /// write ownership) gets a removal tombstone in the write ownership;
        /// a member created in the write ownership itself is dropped along
        /// with its owned descendants.
        /// </summary>
        private void RemoveMemberRow(string memberValueId)
        {
            if (client.values.ContainsKey(memberValueId))
            {
                client.WriteRemovalTombstone(writeOwnership, memberValueId);
                return;
            }
            if (client.TryGetWritableValue(writeOwnership, memberValueId, out MemberValue? _))
            {
                var plan = new NeoWritePlan(client);
                var member = client.TryInferMemberForValueId(memberValueId, out var inferred) ? inferred : null;
                client.StageOwnedRemoval(plan, writeOwnership, memberValueId, member);
                plan.Commit();
                return;
            }
            // The row lives in a lower overlay (e.g. session removal of a
            // save-created member): shadow it with a tombstone.
            client.WriteRemovalTombstone(writeOwnership, memberValueId);
        }

        private JObject BuildTileInstanceJson(
            NeoTilePlacementRecord record,
            string layerClassId)
        {
            return new JObject
            {
                ["id"] = record.InstanceId,
                ["classId"] = record.AssetClassId,
                ["position"] = new JObject { ["x"] = record.Cell.x, ["y"] = record.Cell.y },
                ["layerClassId"] = layerClassId,
                ["order"] = record.Order,
            };
        }

        private JObject BuildObjectInstanceJson(NeoObjectPlacementRecord record, string layerId)
        {
            var footprint = new JArray();
            foreach (var cell in record.Footprint)
            {
                footprint.Add(new JObject { ["x"] = cell.x, ["y"] = cell.y });
            }
            return new JObject
            {
                ["id"] = record.InstanceId,
                ["assetValueId"] = record.AssetValueId is null
                    ? JValue.CreateNull()
                    : new JValue(record.AssetValueId),
                ["assetClassId"] = record.AssetClassId,
                ["position"] = new JObject { ["x"] = record.Cell.x, ["y"] = record.Cell.y },
                ["footprint"] = footprint,
                ["layerClassId"] = layerId,
                ["order"] = record.Order,
            };
        }

        private bool TryValidateGeneratedValue(
            INeoValueReference value,
            string expectedFamilyClassId,
            string label,
            out string? valueId,
            out string? classId,
            out NeoGeneratedClassValue? generatedValue,
            IReadOnlyCollection<string>? allowedClassIds,
            out NeoPlacementResult? error)
        {
            valueId = value?.valueId;
            classId = null;
            generatedValue = value as NeoGeneratedClassValue;
            error = null;
            if (generatedValue is null)
            {
                error = NeoPlacementResult.Error(
                    "tile-grid-value-not-generated",
                    $"Cannot place {label}; value is not a generated Neo class value.");
                return false;
            }
            if (string.IsNullOrEmpty(valueId))
            {
                error = NeoPlacementResult.Error(
                    "tile-grid-value-missing-id",
                    $"Cannot place {label}; generated value has no backing value id.");
                return false;
            }
            if (!client.TryGetValue(valueId!, out ObjectMemberValue? row))
            {
                error = NeoPlacementResult.Error(
                    "tile-grid-value-missing",
                    $"Cannot place {label}; value '{valueId}' is not present in the project or save graph.");
                return false;
            }
            classId = row.classId;
            if (!ValueExtendsClass(valueId!, expectedFamilyClassId))
            {
                error = NeoPlacementResult.Error(
                    "tile-grid-value-wrong-class",
                    $"Cannot place {label} value '{valueId}' in this layer; it does not extend expected class '{expectedFamilyClassId}'.");
                return false;
            }
            if (allowedClassIds is not null && !ContainsClass(allowedClassIds, classId!))
            {
                error = NeoPlacementResult.Error(
                    "tile-grid-asset-not-imported",
                    $"Cannot place {label} class '{classId}' in this grid; the class is not in the relation-derived import allow-list.");
                return false;
            }
            return true;
        }

        private NeoPlacementResult? ValidateClassBackedAsset(
            string classId,
            string label,
            IReadOnlyCollection<string> allowedClassIds,
            out NeoGeneratedClassValue? generated)
        {
            generated = null;
            if (string.IsNullOrWhiteSpace(classId) || !client.classes.ContainsKey(classId))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-asset-class-missing",
                    $"Cannot place {label}; class '{classId}' is missing from the project export.");
            }
            if (!ContainsClass(allowedClassIds, classId))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-asset-not-imported",
                    $"Cannot place {label} class '{classId}' in this grid; the class is not in the relation-derived import allow-list.");
            }
            try
            {
                generated = ResolveGeneratedClassDefault(classId);
            }
            catch (InvalidOperationException exception)
            {
                return NeoPlacementResult.Error(
                    "tile-grid-asset-class-not-generated",
                    exception.Message);
            }
            return generated is null
                ? NeoPlacementResult.Error(
                    "tile-grid-asset-class-not-generated",
                    $"Cannot resolve generated defaults for {label} class '{classId}'.")
                : null;
        }

        private static bool ContainsClass(
            IReadOnlyCollection<string> allowedClassIds,
            string classId)
        {
            foreach (string allowed in allowedClassIds)
            {
                if (string.Equals(allowed, classId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // Write paths need the same candidate lists the (protected) read
        // model uses; re-declared privately because the base's arrays are
        // private to it.
        private static readonly string[] TilesKeyCandidatesForWrite = { "Tiles" };
        private static readonly string[] CellKeyCandidatesForWrite = { "Cell", "Position" };
        private static readonly string[] PositionKeyCandidatesForWrite = { "Position" };
        private static readonly string[] SizeKeyCandidatesForWrite = { "Size" };
    }
}
