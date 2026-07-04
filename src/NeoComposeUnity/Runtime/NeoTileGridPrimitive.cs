// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

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
        public virtual void BeforeSwapObjectVariant(NeoObjectVariantSwapContext context) {}
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
            ReadOnlyNeoTileLayerRuntime layer,
            UnityEngine.Tilemaps.Tilemap tilemap)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            Tilemap = tilemap ?? throw new ArgumentNullException(nameof(tilemap));
        }

        public NeoTileGridRenderer Renderer { get; }
        public ReadOnlyNeoTileLayerRuntime Layer { get; }
        public UnityEngine.Tilemaps.Tilemap Tilemap { get; }
    }

    public sealed class NeoObjectLayerContext
    {
        public NeoObjectLayerContext(
            NeoTileGridRenderer renderer,
            ReadOnlyNeoObjectLayerRuntime layer,
            GameObject root)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            Root = root ?? throw new ArgumentNullException(nameof(root));
        }

        public NeoTileGridRenderer Renderer { get; }
        public ReadOnlyNeoObjectLayerRuntime Layer { get; }
        public GameObject Root { get; }
    }

    public sealed class NeoObjectRenderContext
    {
        public NeoObjectRenderContext(
            NeoTileGridRenderer renderer,
            ReadOnlyNeoObjectLayerRuntime layer,
            NeoResolvedObjectInstance instance)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
        }

        public NeoTileGridRenderer Renderer { get; }
        public ReadOnlyNeoObjectLayerRuntime Layer { get; }
        public NeoResolvedObjectInstance Instance { get; }
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
            NeoGeneratedCustomValue tile,
            string tileValueId,
            string? tileTypeId,
            JObject? existingInstance)
            : base(grid, layerId, cell, instanceId, existingInstance)
        {
            Tile = tile ?? throw new ArgumentNullException(nameof(tile));
            TileValueId = tileValueId ?? throw new ArgumentNullException(nameof(tileValueId));
            TileTypeId = tileTypeId;
        }

        public NeoGeneratedCustomValue Tile { get; }
        public string TileValueId { get; }
        public string? TileTypeId { get; }
    }

    public sealed class NeoTileConvertContext : NeoTileGridMutationContext
    {
        public NeoTileConvertContext(
            NeoTileGridPrimitive grid,
            string layerId,
            Vector2Int cell,
            string instanceId,
            NeoGeneratedCustomValue target,
            string targetValueId,
            string? targetTypeId,
            JObject existingInstance)
            : base(grid, layerId, cell, instanceId, existingInstance)
        {
            Target = target ?? throw new ArgumentNullException(nameof(target));
            TargetValueId = targetValueId ?? throw new ArgumentNullException(nameof(targetValueId));
            TargetTypeId = targetTypeId;
        }

        public NeoGeneratedCustomValue Target { get; }
        public string TargetValueId { get; }
        public string? TargetTypeId { get; }
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
            NeoGeneratedCustomValue obj,
            string objectValueId,
            string? objectTypeId)
            : base(grid, layerId, cell, instanceId, null)
        {
            Object = obj ?? throw new ArgumentNullException(nameof(obj));
            ObjectValueId = objectValueId ?? throw new ArgumentNullException(nameof(objectValueId));
            ObjectTypeId = objectTypeId;
        }

        public NeoGeneratedCustomValue Object { get; }
        public string ObjectValueId { get; }
        public string? ObjectTypeId { get; }
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

    public sealed class NeoObjectVariantSwapContext : NeoTileGridMutationContext
    {
        public NeoObjectVariantSwapContext(
            NeoTileGridPrimitive grid,
            string layerId,
            Vector2Int cell,
            string instanceId,
            NeoGeneratedCustomValue variant,
            string variantValueId,
            string? variantTypeId,
            JObject existingInstance)
            : base(grid, layerId, cell, instanceId, existingInstance)
        {
            Variant = variant ?? throw new ArgumentNullException(nameof(variant));
            VariantValueId = variantValueId ?? throw new ArgumentNullException(nameof(variantValueId));
            VariantTypeId = variantTypeId;
        }

        public NeoGeneratedCustomValue Variant { get; }
        public string VariantValueId { get; }
        public string? VariantTypeId { get; }
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

    public class NeoResolvedTileInstance
    {
        public NeoResolvedTileInstance(
            NeoTileInstanceId instanceId,
            string layerId,
            Vector2Int cell,
            NeoGeneratedCustomValue tile,
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
        public NeoGeneratedCustomValue Tile { get; }
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

        public NeoResolvedTileInstance<TTile>? As<TTile>()
            where TTile : class, INeoValueReference
        {
            return Info is TTile tile
                ? new NeoResolvedTileInstance<TTile>(
                    InstanceId,
                    LayerId,
                    Cell,
                    tile,
                    Order,
                    SourceKind,
                    SourceObjectInstanceId,
                    SourceTileLayerLinkId)
                : null;
        }
    }

    public sealed class NeoResolvedTileInstance<TTile> : NeoResolvedTileInstance
        where TTile : class, INeoValueReference
    {
        public NeoResolvedTileInstance(
            NeoTileInstanceId instanceId,
            string layerId,
            Vector2Int cell,
            TTile info,
            int order,
            NeoTileOutputSourceKind sourceKind = NeoTileOutputSourceKind.Direct,
            string? sourceObjectInstanceId = null,
            string? sourceTileLayerLinkId = null)
            : base(
                instanceId,
                layerId,
                cell,
                info as NeoGeneratedCustomValue
                    ?? throw new ArgumentException(
                        "Resolved tile info must be a generated Neo custom value.",
                        nameof(info)),
                order,
                sourceKind,
                sourceObjectInstanceId,
                sourceTileLayerLinkId)
        {
            Info = info;
        }

        public new TTile Info { get; }
    }

    public class NeoResolvedObjectInstance
    {
        public NeoResolvedObjectInstance(
            NeoObjectInstanceId instanceId,
            string layerId,
            Vector2Int cell,
            IReadOnlyList<Vector2Int> footprint,
            NeoGeneratedCustomValue obj,
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
        public NeoGeneratedCustomValue Object { get; }
        public int Order { get; }

        public bool TryGetObject<TObject>(out TObject obj)
            where TObject : class, INeoValueReference
        {
            obj = Info as TObject;
            return obj != null;
        }

        public NeoResolvedObjectInstance<TObject>? As<TObject>()
            where TObject : class, INeoValueReference
        {
            return Info is TObject obj
                ? new NeoResolvedObjectInstance<TObject>(
                    InstanceId,
                    LayerId,
                    Cell,
                    Footprint,
                    obj,
                    Order)
                : null;
        }
    }

    public sealed class NeoResolvedObjectInstance<TObject> : NeoResolvedObjectInstance
        where TObject : class, INeoValueReference
    {
        public NeoResolvedObjectInstance(
            NeoObjectInstanceId instanceId,
            string layerId,
            Vector2Int cell,
            IReadOnlyList<Vector2Int> footprint,
            TObject info,
            int order)
            : base(
                instanceId,
                layerId,
                cell,
                footprint,
                info as NeoGeneratedCustomValue
                    ?? throw new ArgumentException(
                        "Resolved object info must be a generated Neo custom value.",
                        nameof(info)),
                order)
        {
            Info = info;
        }

        public new TObject Info { get; }
    }

    internal sealed class NeoTileLayerRenderSnapshot
    {
        public NeoTileLayerRenderSnapshot(
            IReadOnlyList<NeoResolvedTileInstance> winners,
            IReadOnlyDictionary<string, IReadOnlyList<NeoResolvedTileInstance>> tileLayerLinkTilesBySourceId,
            IReadOnlyDictionary<Vector2Int, int> candidateCountsByCell)
        {
            Winners = winners ?? throw new ArgumentNullException(nameof(winners));
            TileLayerLinkTilesBySourceId = tileLayerLinkTilesBySourceId
                ?? throw new ArgumentNullException(nameof(tileLayerLinkTilesBySourceId));
            CandidateCountsByCell = candidateCountsByCell
                ?? throw new ArgumentNullException(nameof(candidateCountsByCell));
        }

        public IReadOnlyList<NeoResolvedTileInstance> Winners { get; }
        public IReadOnlyDictionary<string, IReadOnlyList<NeoResolvedTileInstance>> TileLayerLinkTilesBySourceId { get; }
        public IReadOnlyDictionary<Vector2Int, int> CandidateCountsByCell { get; }
    }

    public class ReadOnlyNeoTileLayerRuntime
    {
        public string LayerId { get; }
        public string DisplayName { get; }
        public string ExpectedTypeId { get; }
        public string? SortingLayerName { get; }
        public int? SortingOrder { get; }

        protected ReadOnlyNeoTileLayerRuntime(
            string layerId,
            string displayName,
            string expectedTypeId,
            string? sortingLayerName = null,
            int? sortingOrder = null)
        {
            LayerId = layerId;
            DisplayName = displayName;
            ExpectedTypeId = expectedTypeId;
            SortingLayerName = string.IsNullOrWhiteSpace(sortingLayerName)
                ? null
                : sortingLayerName;
            SortingOrder = sortingOrder;
        }

        public virtual IReadOnlyList<NeoResolvedTileInstance> GetTiles() =>
            Array.Empty<NeoResolvedTileInstance>();

        internal virtual NeoTileLayerRenderSnapshot GetRenderSnapshot()
        {
            var winners = GetTiles();
            var sources = new Dictionary<string, IReadOnlyList<NeoResolvedTileInstance>>();
            var candidateCounts = new Dictionary<Vector2Int, int>();
            foreach (var tile in winners)
            {
                candidateCounts[tile.Cell] = 1;
                if (tile.SourceKind != NeoTileOutputSourceKind.TileLayerLink ||
                    string.IsNullOrEmpty(tile.SourceTileLayerLinkId))
                {
                    continue;
                }
                sources[tile.SourceTileLayerLinkId!] =
                    new[] { tile };
            }
            return new NeoTileLayerRenderSnapshot(winners, sources, candidateCounts);
        }

        public virtual NeoResolvedTileInstance? GetTile(Vector2Int cell) => null;

        public virtual NeoResolvedTileInstance? ResolveTile(Vector2Int cell) =>
            GetTile(cell);

        public virtual IDisposable OnChanged(Action<NeoTileLayerChangedArgs> handler) =>
            new NeoDisposableSubscription(() => {});
    }

    public class ReadOnlyNeoObjectLayerRuntime
    {
        public string LayerId { get; }
        public string DisplayName { get; }
        public string ExpectedTypeId { get; }
        public string? SortingLayerName { get; }
        public int? SortingOrder { get; }

        protected ReadOnlyNeoObjectLayerRuntime(
            string layerId,
            string displayName,
            string expectedTypeId,
            string? sortingLayerName = null,
            int? sortingOrder = null)
        {
            LayerId = layerId;
            DisplayName = displayName;
            ExpectedTypeId = expectedTypeId;
            SortingLayerName = string.IsNullOrWhiteSpace(sortingLayerName)
                ? null
                : sortingLayerName;
            SortingOrder = sortingOrder;
        }

        public virtual IReadOnlyList<NeoResolvedObjectInstance> GetObjects() =>
            Array.Empty<NeoResolvedObjectInstance>();

        public virtual NeoResolvedObjectInstance? GetObject(NeoObjectInstanceId instanceId) =>
            null;

        public virtual NeoResolvedObjectInstance? GetObject(Vector2Int cell) =>
            null;

        public virtual IReadOnlyList<NeoResolvedObjectInstance> GetObjects(Vector2Int cell) =>
            Array.Empty<NeoResolvedObjectInstance>();

        public virtual NeoResolvedObjectInstance? ResolveObject(NeoObjectInstanceId instanceId) =>
            GetObject(instanceId);

        public virtual NeoResolvedObjectInstance? ResolveObject(Vector2Int cell) =>
            GetObject(cell);

        public virtual IReadOnlyList<NeoResolvedObjectInstance> ResolveObjects(Vector2Int cell) =>
            GetObjects(cell);

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
            string expectedTypeId,
            string? sortingLayerName = null,
            int? sortingOrder = null)
            : base(layerId, displayName, expectedTypeId, sortingLayerName, sortingOrder)
        {
            this.primitive = primitive ?? throw new ArgumentNullException(nameof(primitive));
        }

        public override NeoResolvedTileInstance? GetTile(Vector2Int cell) =>
            primitive.ResolveTileCached(LayerId, cell, ExpectedTypeId);

        public IReadOnlyList<NeoTileCandidate<TTile>> GetCandidates(Vector2Int cell) =>
            primitive.GetTileCandidates<TTile>(LayerId, cell, ExpectedTypeId);

        public NeoTileConflict<TTile>? GetConflict(Vector2Int cell) =>
            primitive.GetTileConflict<TTile>(LayerId, cell, ExpectedTypeId);

        public override IReadOnlyList<NeoResolvedTileInstance> GetTiles() =>
            primitive.GetTiles(LayerId, ExpectedTypeId);

        internal override NeoTileLayerRenderSnapshot GetRenderSnapshot() =>
            primitive.GetTileLayerRenderSnapshot(LayerId, ExpectedTypeId);

        public override NeoResolvedTileInstance? ResolveTile(Vector2Int cell) =>
            GetTile(cell);

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
            string expectedTypeId,
            string? sortingLayerName = null,
            int? sortingOrder = null)
            : base(layerId, displayName, expectedTypeId, sortingLayerName, sortingOrder)
        {
            this.primitive = primitive ?? throw new ArgumentNullException(nameof(primitive));
        }

        public override IReadOnlyList<NeoResolvedObjectInstance> GetObjects() =>
            primitive.GetObjects(LayerId, ExpectedTypeId);

        public override NeoResolvedObjectInstance? GetObject(NeoObjectInstanceId instanceId) =>
            primitive.ResolveObjectInstance(LayerId, instanceId, ExpectedTypeId);

        public override NeoResolvedObjectInstance? GetObject(Vector2Int cell) =>
            primitive.ResolveObjectAtCellCached(LayerId, cell, ExpectedTypeId);

        public override IReadOnlyList<NeoResolvedObjectInstance> GetObjects(Vector2Int cell) =>
            primitive.ResolveObjectsAtCellCached(LayerId, cell, ExpectedTypeId);

        public override NeoResolvedObjectInstance? ResolveObject(NeoObjectInstanceId instanceId) =>
            GetObject(instanceId);

        public override NeoResolvedObjectInstance? ResolveObject(Vector2Int cell) =>
            GetObject(cell);

        public override IReadOnlyList<NeoResolvedObjectInstance> ResolveObjects(Vector2Int cell) =>
            GetObjects(cell);

        public override IDisposable OnChanged(Action<NeoObjectLayerChangedArgs> handler) =>
            primitive.OnObjectLayerChanged(LayerId, handler);
    }

    /// <summary>
    /// One resolved tile placement: a TilePlacement custom value (record keys
    /// "Cell" → Vector2Int row, "Tile" → single-select Lookup row) joined to
    /// its tile layer link's unordered "Tiles" list via
    /// <see cref="AttributeValue.containerId"/>, or a tile composed from an
    /// object-carried tile layer link (stamp/prefab), projected to grid space.
    /// </summary>
    internal sealed class NeoTilePlacementRecord
    {
        public NeoTilePlacementRecord(
            string instanceId,
            string placementValueId,
            Vector2Int cell,
            string tileValueId,
            int order,
            string sourceTileLayerLinkId,
            string? sourceObjectInstanceId,
            double updatedAtMs,
            string? cellValueId,
            string? tileLookupValueId)
        {
            InstanceId = instanceId;
            PlacementValueId = placementValueId;
            Cell = cell;
            TileValueId = tileValueId;
            Order = order;
            SourceTileLayerLinkId = sourceTileLayerLinkId;
            SourceObjectInstanceId = sourceObjectInstanceId;
            UpdatedAtMs = updatedAtMs;
            CellValueId = cellValueId;
            TileLookupValueId = tileLookupValueId;
        }

        public string InstanceId { get; }
        public string PlacementValueId { get; }
        public Vector2Int Cell { get; }
        public string TileValueId { get; }
        public int Order { get; }
        public string SourceTileLayerLinkId { get; }
        public string? SourceObjectInstanceId { get; }
        public double UpdatedAtMs { get; }
        public string? CellValueId { get; }
        public string? TileLookupValueId { get; }
        public bool IsObjectCarried => SourceObjectInstanceId is not null;
    }

    /// <summary>
    /// One resolved placed object: an object value joined to its object layer
    /// link's unordered "Objects" list via <see cref="AttributeValue.containerId"/>,
    /// with its footprint expanded from the record's Position/Size.
    /// </summary>
    internal sealed class NeoObjectPlacementRecord
    {
        public NeoObjectPlacementRecord(
            string instanceId,
            Vector2Int cell,
            IReadOnlyList<Vector2Int> footprint,
            int order)
        {
            InstanceId = instanceId;
            Cell = cell;
            Footprint = footprint;
            Order = order;
        }

        public string InstanceId { get; }
        public Vector2Int Cell { get; }
        public IReadOnlyList<Vector2Int> Footprint { get; }
        public int Order { get; }
    }

    /// <summary>One grid-child layer link (TileLayerLink or ObjectLayerLink).</summary>
    internal sealed class NeoGridLayerLinkModel
    {
        public NeoGridLayerLinkModel(
            string linkValueId,
            string linkTypeId,
            string layerId,
            string listValueId,
            bool isTileLink)
        {
            LinkValueId = linkValueId;
            LinkTypeId = linkTypeId;
            LayerId = layerId;
            ListValueId = listValueId;
            IsTileLink = isTileLink;
        }

        public string LinkValueId { get; }
        public string LinkTypeId { get; }
        public string LayerId { get; }
        /// <summary>The containment list VALUE id ("Tiles" / "Objects").</summary>
        public string ListValueId { get; }
        public bool IsTileLink { get; }
    }

    public class NeoReadOnlyTileGridPrimitive
    {
        protected readonly NeoClient client;
        protected static readonly IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory> EmptyReadOnlyFactories =
            new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>();
        protected static readonly IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory> EmptyWritableFactories =
            new Dictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>();
        private readonly IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory> readOnlyFactories;
        private readonly IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory> writableFactories;
        private NeoTileGridLookupCache? lookupCache;
        private event Action<NeoTileGridChangedArgs>? Changed;

        private static readonly string[] ChildrenKeyCandidates = { "Children", "tileLayerLinks" };
        private static readonly string[] TileLayerKeyCandidates = { "TileLayer", "targetTileLayer", "targetLayer" };
        private static readonly string[] TilesKeyCandidates = { "Tiles", "tileInstances" };
        private static readonly string[] ObjectLayerKeyCandidates = { "ObjectLayer", "targetObjectLayer", "targetLayer" };
        private static readonly string[] ObjectsKeyCandidates = { "Objects", "objectInstances" };
        private static readonly string[] CellKeyCandidates = { "Cell", "Position" };
        private static readonly string[] TileKeyCandidates = { "Tile", "tileValue", "tileValueId" };
        private static readonly string[] LinkTilePositionKeyCandidates = { "Position", "offset", "Cell" };
        private static readonly string[] OrderKeyCandidates = { "Order" };
        private static readonly string[] PositionKeyCandidates = { "Position" };
        private static readonly string[] SizeKeyCandidates = { "Size" };

        protected NeoReadOnlyTileGridPrimitive(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>? readOnlyFactories = null,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>? writableFactories = null)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            GridValueId = gridValueId ?? throw new ArgumentNullException(nameof(gridValueId));
            this.readOnlyFactories = readOnlyFactories ?? EmptyReadOnlyFactories;
            this.writableFactories = writableFactories ?? EmptyWritableFactories;
            // Storage partitions (spec §6): resolving a grid primitive IS the
            // content-access path — lazily merge the grid's `world:<id>`
            // partition (no-op for grids authored in the main partition).
            client.EnsureWorldPartitionLoaded(gridValueId);
        }

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
                return lookupCache ??= new NeoTileGridLookupCache(this);
            }
        }

        public IDisposable OnChanged(Action<NeoTileGridChangedArgs> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
            Changed += handler;
            return new NeoDisposableSubscription(() => Changed -= handler);
        }

        internal IDisposable OnTileLayerChanged(
            string layerId,
            Action<NeoTileLayerChangedArgs> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
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
            return OnChanged(Handle);
        }

        internal IDisposable OnObjectLayerChanged(
            string layerId,
            Action<NeoObjectLayerChangedArgs> handler)
        {
            if (handler is null) throw new ArgumentNullException(nameof(handler));
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
            return OnChanged(Handle);
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
            lookupCache?.Apply(args);
            Changed?.Invoke(args);
        }

        public static NeoReadOnlyTileGridPrimitive Resolve(NeoClient client, string gridValueId)
        {
            return Resolve(client, gridValueId, EmptyReadOnlyFactories, EmptyWritableFactories);
        }

        public static NeoReadOnlyTileGridPrimitive Resolve(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory> writableFactories)
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            return new NeoReadOnlyTileGridPrimitive(
                client,
                gridValueId,
                readOnlyFactories,
                writableFactories);
        }

        // -------------------------------------------------------------------
        // Tile queries (values-backed spatial index; O(1) per cell once the
        // per-layer index is built).
        // -------------------------------------------------------------------

        public virtual TTile? GetTile<TTile>(
            string layerId,
            Vector2Int cell,
            string expectedTileFamilyTypeId)
            where TTile : class, INeoValueReference
        {
            IReadOnlyList<NeoTileCandidate<TTile>> candidates =
                GetTileCandidates<TTile>(layerId, cell, expectedTileFamilyTypeId);
            return candidates.Count == 0 ? null : candidates[candidates.Count - 1].Tile;
        }

        public virtual IReadOnlyList<NeoTileCandidate<TTile>> GetTileCandidates<TTile>(
            string layerId,
            Vector2Int cell,
            string expectedTileFamilyTypeId)
            where TTile : class, INeoValueReference
        {
            var candidates = new List<NeoTileCandidate<TTile>>();
            foreach (var record in LookupCache.TileCandidatesAt(layerId, cell))
            {
                TTile? tile = ResolveGeneratedValue<TTile>(
                    record.TileValueId,
                    expectedTileFamilyTypeId);
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
            string expectedTileFamilyTypeId)
            where TTile : class, INeoValueReference
        {
            var candidates = GetTileCandidates<TTile>(
                layerId,
                cell,
                expectedTileFamilyTypeId);
            return candidates.Count <= 1
                ? null
                : new NeoTileConflict<TTile>(layerId, cell, candidates);
        }

        public virtual IReadOnlyList<NeoResolvedTileInstance> GetTiles(
            string layerId,
            string expectedTileFamilyTypeId = "")
        {
            var winners = new List<NeoResolvedTileInstance>();
            foreach (var cellCandidates in LookupCache.TileCandidatesByCell(layerId).Values)
            {
                var winner = ResolveWinner(layerId, cellCandidates, expectedTileFamilyTypeId);
                if (winner is not null) winners.Add(winner);
            }
            winners.Sort((left, right) =>
            {
                int y = left.Cell.y.CompareTo(right.Cell.y);
                return y != 0 ? y : left.Cell.x.CompareTo(right.Cell.x);
            });
            return winners;
        }

        internal virtual NeoTileLayerRenderSnapshot GetTileLayerRenderSnapshot(
            string layerId,
            string expectedTileFamilyTypeId = "")
        {
            var winners = new List<NeoResolvedTileInstance>();
            var sourceTiles = new Dictionary<string, List<NeoResolvedTileInstance>>();
            var candidateCounts = new Dictionary<Vector2Int, int>();
            foreach (var pair in LookupCache.TileCandidatesByCell(layerId))
            {
                NeoResolvedTileInstance? winner = null;
                int resolvableCount = 0;
                foreach (var record in pair.Value)
                {
                    var resolved = ResolveRecord(layerId, record, expectedTileFamilyTypeId);
                    if (resolved is null) continue;
                    resolvableCount += 1;
                    winner = resolved;
                    if (string.IsNullOrEmpty(resolved.SourceTileLayerLinkId)) continue;
                    if (!sourceTiles.TryGetValue(resolved.SourceTileLayerLinkId!, out var tiles))
                    {
                        tiles = new List<NeoResolvedTileInstance>();
                        sourceTiles[resolved.SourceTileLayerLinkId!] = tiles;
                    }
                    tiles.Add(resolved);
                }
                if (resolvableCount == 0) continue;
                candidateCounts[pair.Key] = resolvableCount;
                winners.Add(winner!);
            }
            winners.Sort((left, right) =>
            {
                int y = left.Cell.y.CompareTo(right.Cell.y);
                return y != 0 ? y : left.Cell.x.CompareTo(right.Cell.x);
            });

            var readonlySourceTiles =
                new Dictionary<string, IReadOnlyList<NeoResolvedTileInstance>>();
            foreach (var pair in sourceTiles)
            {
                readonlySourceTiles[pair.Key] = pair.Value;
            }
            return new NeoTileLayerRenderSnapshot(
                winners,
                readonlySourceTiles,
                candidateCounts);
        }

        internal virtual NeoResolvedTileInstance? ResolveTile(
            string layerId,
            Vector2Int cell,
            string expectedTileFamilyTypeId = "")
        {
            return ResolveWinner(
                layerId,
                LookupCache.TileCandidatesAt(layerId, cell),
                expectedTileFamilyTypeId);
        }

        internal NeoResolvedTileInstance? ResolveTileCached(
            string layerId,
            Vector2Int cell,
            string expectedTileFamilyTypeId = "")
        {
            return ResolveTile(layerId, cell, expectedTileFamilyTypeId);
        }

        private NeoResolvedTileInstance? ResolveWinner(
            string layerId,
            IReadOnlyList<NeoTilePlacementRecord> cellCandidates,
            string expectedTileFamilyTypeId)
        {
            // Candidates are stored loser→winner; the last resolvable record
            // (conflict tiebreak: updatedAt desc, id asc) wins the cell.
            for (int index = cellCandidates.Count - 1; index >= 0; index -= 1)
            {
                var resolved = ResolveRecord(layerId, cellCandidates[index], expectedTileFamilyTypeId);
                if (resolved is not null) return resolved;
            }
            return null;
        }

        private NeoResolvedTileInstance? ResolveRecord(
            string layerId,
            NeoTilePlacementRecord record,
            string expectedTileFamilyTypeId)
        {
            var tile = ResolveGeneratedValue<NeoGeneratedCustomValue>(
                record.TileValueId,
                expectedTileFamilyTypeId);
            if (tile is null) return null;
            return new NeoResolvedTileInstance(
                record.InstanceId,
                layerId,
                record.Cell,
                tile,
                record.Order,
                NeoTileOutputSourceKind.TileLayerLink,
                record.SourceObjectInstanceId,
                record.SourceTileLayerLinkId);
        }

        internal virtual IReadOnlyList<NeoResolvedTileInstance> GetTileLayerLinkTiles(
            string layerId,
            string sourceTileLayerLinkId,
            string expectedTileFamilyTypeId = "")
        {
            var tiles = new List<NeoResolvedTileInstance>();
            if (string.IsNullOrEmpty(sourceTileLayerLinkId)) return tiles;
            foreach (var record in LookupCache.TileRecords(layerId))
            {
                if (record.SourceTileLayerLinkId != sourceTileLayerLinkId) continue;
                var resolved = ResolveRecord(layerId, record, expectedTileFamilyTypeId);
                if (resolved is null) continue;
                tiles.Add(resolved);
            }
            tiles.Sort((left, right) =>
            {
                int cellY = left.Cell.y.CompareTo(right.Cell.y);
                if (cellY != 0) return cellY;
                int cellX = left.Cell.x.CompareTo(right.Cell.x);
                if (cellX != 0) return cellX;
                int order = left.Order.CompareTo(right.Order);
                return order != 0
                    ? order
                    : left.InstanceId.Value.CompareTo(right.InstanceId.Value);
            });
            return tiles;
        }

        // -------------------------------------------------------------------
        // Object queries.
        // -------------------------------------------------------------------

        public virtual TObject? GetObject<TObject>(
            string layerId,
            Vector2Int cell,
            string expectedObjectFamilyTypeId)
            where TObject : class, INeoValueReference
        {
            TObject? best = null;
            int bestOrder = int.MinValue;
            foreach (var record in LookupCache.ObjectCandidatesAt(layerId, cell))
            {
                TObject? obj = ResolveGeneratedValue<TObject>(
                    record.InstanceId,
                    expectedObjectFamilyTypeId);
                if (obj is null) continue;
                if (best is not null && record.Order < bestOrder) continue;
                best = obj;
                bestOrder = record.Order;
            }
            return best;
        }

        internal virtual IReadOnlyList<NeoResolvedObjectInstance> ResolveObjectsAtCell(
            string layerId,
            Vector2Int cell,
            string expectedObjectFamilyTypeId = "")
        {
            var objects = new List<NeoResolvedObjectInstance>();
            foreach (var record in LookupCache.ObjectCandidatesAt(layerId, cell))
            {
                var resolved = ResolveObjectRecord(layerId, record, expectedObjectFamilyTypeId);
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

        internal NeoResolvedObjectInstance? ResolveObjectAtCellCached(
            string layerId,
            Vector2Int cell,
            string expectedObjectFamilyTypeId = "")
        {
            var objects = ResolveObjectsAtCell(layerId, cell, expectedObjectFamilyTypeId);
            return objects.Count == 0 ? null : objects[objects.Count - 1];
        }

        internal IReadOnlyList<NeoResolvedObjectInstance> ResolveObjectsAtCellCached(
            string layerId,
            Vector2Int cell,
            string expectedObjectFamilyTypeId = "")
        {
            return ResolveObjectsAtCell(layerId, cell, expectedObjectFamilyTypeId);
        }

        public virtual IReadOnlyList<NeoResolvedObjectInstance> GetObjects(
            string layerId,
            string expectedObjectFamilyTypeId = "")
        {
            var objects = new List<NeoResolvedObjectInstance>();
            foreach (var record in LookupCache.ObjectRecords(layerId))
            {
                var resolved = ResolveObjectRecord(layerId, record, expectedObjectFamilyTypeId);
                if (resolved is null) continue;
                objects.Add(resolved);
            }
            objects.Sort((left, right) => left.Order.CompareTo(right.Order));
            return objects;
        }

        internal virtual NeoResolvedObjectInstance? ResolveObjectInstance(
            string layerId,
            NeoObjectInstanceId instanceId,
            string expectedObjectFamilyTypeId = "")
        {
            foreach (var record in LookupCache.ObjectRecords(layerId))
            {
                if (record.InstanceId != instanceId.Value) continue;
                return ResolveObjectRecord(layerId, record, expectedObjectFamilyTypeId);
            }
            return null;
        }

        private NeoResolvedObjectInstance? ResolveObjectRecord(
            string layerId,
            NeoObjectPlacementRecord record,
            string expectedObjectFamilyTypeId)
        {
            var obj = ResolveGeneratedValue<NeoGeneratedCustomValue>(
                record.InstanceId,
                expectedObjectFamilyTypeId);
            if (obj is null) return null;
            return new NeoResolvedObjectInstance(
                record.InstanceId,
                layerId,
                record.Cell,
                record.Footprint,
                obj,
                record.Order);
        }

        // -------------------------------------------------------------------
        // Tile-layer-link dependencies (renderer live sync).
        // -------------------------------------------------------------------

        internal IReadOnlyList<NeoTileLayerLinkDependency> GetTileLayerLinkDependencies()
        {
            var dependencies = new List<NeoTileLayerLinkDependency>();
            var seen = new HashSet<string>();
            foreach (var link in ResolveGridLinks(null))
            {
                if (link.IsTileLink)
                {
                    AddTileLayerLinkDependency(dependencies, seen, link.LinkValueId, link.LayerId);
                    continue;
                }
                foreach (var objectValueId in ResolveListEntryIds(link.ListValueId, null))
                {
                    foreach (var carried in ResolveObjectCarriedLinks(objectValueId, null))
                    {
                        AddTileLayerLinkDependency(
                            dependencies,
                            seen,
                            carried.LinkValueId,
                            carried.LayerId);
                    }
                }
            }
            return dependencies;
        }

        internal IReadOnlyList<NeoTileLayerLinkDependency> GetAuthoredTileLayerLinkDependencies()
        {
            return GetTileLayerLinkDependencies();
        }

        private static void AddTileLayerLinkDependency(
            List<NeoTileLayerLinkDependency> dependencies,
            HashSet<string> seen,
            string sourceValueId,
            string targetTileLayerId)
        {
            string key = $"{sourceValueId}\n{targetTileLayerId}";
            if (!seen.Add(key)) return;
            dependencies.Add(new NeoTileLayerLinkDependency(
                sourceValueId,
                targetTileLayerId));
        }

        // -------------------------------------------------------------------
        // Generated-value resolution and type checks.
        // -------------------------------------------------------------------

        protected TGenerated? ResolveGeneratedValue<TGenerated>(
            string valueId,
            string expectedFamilyTypeId)
            where TGenerated : class, INeoValueReference
        {
            if (!ValueExtendsType(valueId, expectedFamilyTypeId)) return null;
            object? resolved = NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                valueId,
                readOnlyFactories,
                writableFactories);
            return resolved as TGenerated;
        }

        internal NeoGeneratedCustomValue? ResolveGeneratedCustomValue(string valueId)
        {
            if (string.IsNullOrEmpty(valueId)) return null;
            object? resolved = NeoGeneratedTypesSupport.ResolveCustomValue(
                client,
                valueId,
                readOnlyFactories,
                writableFactories);
            return resolved as NeoGeneratedCustomValue;
        }

        protected bool ValueExtendsType(string valueId, string expectedFamilyTypeId)
        {
            if (string.IsNullOrEmpty(expectedFamilyTypeId)) return true;
            if (!client.TryGetValue(valueId, out ObjectAttributeValue? row)) return false;
            string? typeId = row.typeId;
            if (string.IsNullOrEmpty(typeId)) return false;
            return TypeExtendsType(typeId!, expectedFamilyTypeId);
        }

        private bool TypeExtendsType(string typeId, string expectedTypeId)
        {
            var visited = new HashSet<string>();
            string? cursor = typeId;
            while (!string.IsNullOrEmpty(cursor))
            {
                if (!visited.Add(cursor!)) return false;
                if (cursor == expectedTypeId) return true;
                if (!client.types.TryGetValue(cursor!, out CustomType type)) return false;
                cursor = type.extendsTypeId;
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
            if (client.ResolveEffectiveRow(GridValueId) is not ObjectAttributeValue gridRow) return links;
            if (gridRow.IsRemoved) return links;
            if (string.IsNullOrEmpty(gridRow.typeId)) return links;
            if (gridRow.value is null) return links;
            string? childrenKey = FindSchemaKey(gridRow.typeId!, ChildrenKeyCandidates);
            if (childrenKey is null) return links;
            if (!gridRow.value.TryGetValue(childrenKey, out string childrenListId)) return links;

            foreach (var linkValueId in ResolveListEntryIds(childrenListId, dependencyIds))
            {
                dependencyIds?.Add(linkValueId);
                if (client.ResolveEffectiveRow(linkValueId) is not ObjectAttributeValue linkRow) continue;
                if (linkRow.IsRemoved) continue;
                if (string.IsNullOrEmpty(linkRow.typeId)) continue;
                if (linkRow.value is null) continue;
                var link = ResolveLinkModel(linkValueId, linkRow, dependencyIds);
                if (link is not null) links.Add(link);
            }
            return links;
        }

        private NeoGridLayerLinkModel? ResolveLinkModel(
            string linkValueId,
            ObjectAttributeValue linkRow,
            HashSet<string>? dependencyIds)
        {
            string typeId = linkRow.typeId!;
            string? tileLayerKey = FindSchemaKey(typeId, TileLayerKeyCandidates);
            string? tilesKey = FindSchemaKey(typeId, TilesKeyCandidates);
            if (tileLayerKey is not null && tilesKey is not null
                && linkRow.value!.TryGetValue(tileLayerKey, out string tileLayerLookupId)
                && linkRow.value.TryGetValue(tilesKey, out string tilesListId))
            {
                string? layerId = ResolveLookupFirstId(tileLayerLookupId, dependencyIds);
                if (layerId is null) return null;
                dependencyIds?.Add(tilesListId);
                return new NeoGridLayerLinkModel(linkValueId, typeId, layerId, tilesListId, isTileLink: true);
            }

            string? objectLayerKey = FindSchemaKey(typeId, ObjectLayerKeyCandidates);
            string? objectsKey = FindSchemaKey(typeId, ObjectsKeyCandidates);
            if (objectLayerKey is not null && objectsKey is not null
                && linkRow.value!.TryGetValue(objectLayerKey, out string objectLayerLookupId)
                && linkRow.value.TryGetValue(objectsKey, out string objectsListId))
            {
                string? layerId = ResolveLookupFirstId(objectLayerLookupId, dependencyIds);
                if (layerId is null) return null;
                dependencyIds?.Add(objectsListId);
                return new NeoGridLayerLinkModel(linkValueId, typeId, layerId, objectsListId, isTileLink: false);
            }
            return null;
        }

        /// <summary>
        /// Entry value ids of a containment list resolved through the overlay
        /// cascade: inline array entries first (ordered lists and legacy
        /// factory rows), then the unordered membership join (rows carrying
        /// this list value's id as their <see cref="AttributeValue.containerId"/>),
        /// id-sorted. A null container value resolves to no entries.
        /// </summary>
        internal IReadOnlyList<string> ResolveListEntryIds(
            string listValueId,
            HashSet<string>? dependencyIds)
        {
            dependencyIds?.Add(listValueId);
            var ids = new List<string>();
            if (client.ResolveEffectiveRow(listValueId) is not ArrayAttributeValue listRow) return ids;
            if (listRow.IsRemoved) return ids;
            if (listRow.value is null) return ids;
            var seen = new HashSet<string>();
            foreach (var entryId in listRow.value)
            {
                if (string.IsNullOrEmpty(entryId)) continue;
                if (!seen.Add(entryId)) continue;
                var entryRow = client.ResolveEffectiveRow(entryId);
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
                        dependencyIds);
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
            if (client.ResolveEffectiveRow(objectValueId) is not ObjectAttributeValue objectRow) return;
            if (objectRow.IsRemoved) return;
            if (string.IsNullOrEmpty(objectRow.typeId)) return;
            if (objectRow.value is null) return;
            Vector2Int origin = ReadObjectOrigin(objectRow, dependencyIds);

            int linkIndex = -1;
            foreach (var carried in ResolveObjectCarriedLinks(objectValueId, dependencyIds))
            {
                linkIndex = carried.ChildIndex;
                if (carried.LayerId != layerId) continue;
                int sourceOrder = objectIndex * 1000 + linkIndex;
                int tileIndex = 0;
                foreach (var entryId in ResolveListEntryIds(carried.TilesListValueId, dependencyIds))
                {
                    var record = ResolveObjectCarriedTileRecord(
                        objectValueId,
                        carried.LinkValueId,
                        entryId,
                        origin,
                        sourceOrder,
                        tileIndex,
                        dependencyIds);
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
            if (client.ResolveEffectiveRow(objectValueId) is not ObjectAttributeValue objectRow) yield break;
            if (objectRow.IsRemoved) yield break;
            if (string.IsNullOrEmpty(objectRow.typeId)) yield break;
            if (objectRow.value is null) yield break;
            string? childrenKey = FindSchemaKey(objectRow.typeId!, ChildrenKeyCandidates);
            if (childrenKey is null) yield break;
            if (!objectRow.value.TryGetValue(childrenKey, out string childrenListId)) yield break;

            int childIndex = -1;
            foreach (var childValueId in ResolveListEntryIds(childrenListId, dependencyIds))
            {
                childIndex += 1;
                dependencyIds?.Add(childValueId);
                if (client.ResolveEffectiveRow(childValueId) is not ObjectAttributeValue childRow) continue;
                if (childRow.IsRemoved) continue;
                if (string.IsNullOrEmpty(childRow.typeId)) continue;
                if (childRow.value is null) continue;
                string? tileLayerKey = FindSchemaKey(childRow.typeId!, TileLayerKeyCandidates);
                string? tilesKey = FindSchemaKey(childRow.typeId!, TilesKeyCandidates);
                if (tileLayerKey is null || tilesKey is null) continue;
                if (!childRow.value.TryGetValue(tileLayerKey, out string layerLookupId)) continue;
                if (!childRow.value.TryGetValue(tilesKey, out string tilesListId)) continue;
                string? targetLayerId = ResolveLookupFirstId(layerLookupId, dependencyIds);
                if (targetLayerId is null) continue;
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
            HashSet<string>? dependencyIds)
        {
            dependencyIds?.Add(entryId);
            if (client.ResolveEffectiveRow(entryId) is not ObjectAttributeValue entryRow) return null;
            if (entryRow.IsRemoved) return null;
            // Parity with the retired export derivation: entries without a
            // typeId are not composed.
            if (string.IsNullOrEmpty(entryRow.typeId)) return null;

            string instanceId = $"{objectValueId}:{linkValueId}:{entryId}";
            string? tileKey = entryRow.value is null
                ? null
                : FindSchemaKey(entryRow.typeId!, TileKeyCandidates);
            if (tileKey is not null && entryRow.value!.TryGetValue(tileKey, out string tileLookupId))
            {
                dependencyIds?.Add(tileLookupId);
                string? tileValueId = ResolveLookupFirstId(tileLookupId, dependencyIds);
                if (tileValueId is null) return null;
                Vector2Int localCell = Vector2Int.zero;
                string? cellValueId = null;
                string? positionKey = FindSchemaKey(entryRow.typeId!, LinkTilePositionKeyCandidates);
                if (positionKey is not null
                    && entryRow.value.TryGetValue(positionKey, out string positionRowId))
                {
                    cellValueId = positionRowId;
                    dependencyIds?.Add(positionRowId);
                    localCell = ReadCellRow(positionRowId) ?? Vector2Int.zero;
                }
                int tileOrder = tileIndex;
                string? orderKey = FindSchemaKey(entryRow.typeId!, OrderKeyCandidates);
                if (orderKey is not null
                    && entryRow.value.TryGetValue(orderKey, out string orderRowId))
                {
                    dependencyIds?.Add(orderRowId);
                    if (client.ResolveEffectiveRow(orderRowId) is NumberAttributeValue orderRow
                        && orderRow.value is not null)
                    {
                        tileOrder = (int)orderRow.value.Value;
                    }
                }
                return new NeoTilePlacementRecord(
                    instanceId,
                    entryId,
                    origin + localCell,
                    tileValueId,
                    CombineTileLayerLinkOrder(sourceOrder, tileOrder),
                    linkValueId,
                    objectValueId,
                    entryRow.updatedAt.EpochMilliseconds,
                    cellValueId,
                    tileLookupId);
            }

            // The entry IS a tile value: it projects at the object's origin.
            return new NeoTilePlacementRecord(
                instanceId,
                entryId,
                origin,
                entryId,
                CombineTileLayerLinkOrder(sourceOrder, tileIndex),
                linkValueId,
                objectValueId,
                entryRow.updatedAt.EpochMilliseconds,
                cellValueId: null,
                tileLookupValueId: null);
        }

        private NeoTilePlacementRecord? ResolvePlacementRecord(
            string instanceId,
            string placementValueId,
            Vector2Int origin,
            int order,
            string sourceTileLayerLinkId,
            string? sourceObjectInstanceId,
            string[] cellKeyCandidates,
            HashSet<string>? dependencyIds)
        {
            dependencyIds?.Add(placementValueId);
            if (client.ResolveEffectiveRow(placementValueId) is not ObjectAttributeValue placementRow) return null;
            if (placementRow.IsRemoved) return null;
            if (string.IsNullOrEmpty(placementRow.typeId)) return null;
            if (placementRow.value is null) return null;

            string? cellKey = FindSchemaKey(placementRow.typeId!, cellKeyCandidates);
            string? tileKey = FindSchemaKey(placementRow.typeId!, TileKeyCandidates);
            if (cellKey is null || tileKey is null) return null;
            if (!placementRow.value.TryGetValue(cellKey, out string cellValueId)) return null;
            if (!placementRow.value.TryGetValue(tileKey, out string tileLookupValueId)) return null;
            dependencyIds?.Add(cellValueId);
            dependencyIds?.Add(tileLookupValueId);
            Vector2Int? cell = ReadCellRow(cellValueId);
            if (cell is null) return null;
            string? tileValueId = ResolveLookupFirstId(tileLookupValueId, dependencyIds);
            if (tileValueId is null) return null;

            return new NeoTilePlacementRecord(
                instanceId,
                placementValueId,
                origin + cell.Value,
                tileValueId,
                order,
                sourceTileLayerLinkId,
                sourceObjectInstanceId,
                placementRow.updatedAt.EpochMilliseconds,
                cellValueId,
                tileLookupValueId);
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
                foreach (var objectValueId in ResolveListEntryIds(link.ListValueId, dependencyIds))
                {
                    dependencyIds?.Add(objectValueId);
                    if (client.ResolveEffectiveRow(objectValueId) is not ObjectAttributeValue objectRow) continue;
                    if (objectRow.IsRemoved) continue;
                    if (string.IsNullOrEmpty(objectRow.typeId)) continue;
                    Vector2Int cell = ReadObjectOrigin(objectRow, dependencyIds);
                    var footprint = ReadObjectFootprint(objectRow, cell, dependencyIds);
                    records.Add(new NeoObjectPlacementRecord(
                        objectValueId,
                        cell,
                        footprint,
                        order));
                    order += 1;
                }
            }
            return records;
        }

        private Vector2Int ReadObjectOrigin(
            ObjectAttributeValue objectRow,
            HashSet<string>? dependencyIds)
        {
            if (objectRow.value is null) return Vector2Int.zero;
            string? positionKey = FindSchemaKey(objectRow.typeId!, PositionKeyCandidates);
            if (positionKey is null) return Vector2Int.zero;
            if (!objectRow.value.TryGetValue(positionKey, out string positionRowId)) return Vector2Int.zero;
            dependencyIds?.Add(positionRowId);
            return ReadCellRow(positionRowId) ?? Vector2Int.zero;
        }

        private IReadOnlyList<Vector2Int> ReadObjectFootprint(
            ObjectAttributeValue objectRow,
            Vector2Int origin,
            HashSet<string>? dependencyIds)
        {
            int width = 1;
            int height = 1;
            string? sizeKey = objectRow.value is null
                ? null
                : FindSchemaKey(objectRow.typeId!, SizeKeyCandidates);
            if (sizeKey is not null && objectRow.value!.TryGetValue(sizeKey, out string sizeRowId))
            {
                dependencyIds?.Add(sizeRowId);
                var size = ReadVectorRow(sizeRowId);
                if (size is not null)
                {
                    width = Mathf.Max(1, (int)size.Value.x);
                    height = Mathf.Max(1, (int)size.Value.y);
                }
            }
            var cells = new List<Vector2Int>(width * height);
            for (int y = 0; y < height; y += 1)
            {
                for (int x = 0; x < width; x += 1)
                {
                    cells.Add(new Vector2Int(origin.x + x, origin.y + y));
                }
            }
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

        /// <summary>First selected id of a Lookup row (single select), or the
        /// referenced id itself when no row resolves at that id.</summary>
        private string? ResolveLookupFirstId(string lookupValueId, HashSet<string>? dependencyIds)
        {
            dependencyIds?.Add(lookupValueId);
            var row = client.ResolveEffectiveRow(lookupValueId);
            if (row is null) return lookupValueId;
            if (row.IsRemoved) return null;
            if (row is ArrayAttributeValue arrayRow)
            {
                if (arrayRow.value is null) return null;
                foreach (var id in arrayRow.value)
                {
                    if (!string.IsNullOrEmpty(id)) return id;
                }
                return null;
            }
            if (row is StringAttributeValue stringRow) return stringRow.value;
            return lookupValueId;
        }

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
            switch (client.ResolveEffectiveRow(valueId))
            {
                case Vector2AttributeValue v2 when v2.value is not null:
                    return new Vector2(v2.value.x, v2.value.y);
                case Vector3AttributeValue v3 when v3.value is not null:
                    return new Vector2(v3.value.x, v3.value.y);
                default:
                    return null;
            }
        }

        /// <summary>Case-insensitive schema-key match over a type's merged
        /// inheritance schema, first candidate wins.</summary>
        protected string? FindSchemaKey(string typeId, string[] keyCandidates)
        {
            var merged = ResolveMergedSchemaEntries(typeId);
            if (merged is null) return null;
            foreach (var candidate in keyCandidates)
            {
                foreach (var entry in merged)
                {
                    if (string.Equals(entry.schemaKey, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.schemaKey;
                    }
                }
            }
            return null;
        }

        /// <summary>The attribute id backing a schema key on a type's merged
        /// schema (first candidate match), or null.</summary>
        protected string? FindSchemaAttributeId(string typeId, string[] keyCandidates)
        {
            var merged = ResolveMergedSchemaEntries(typeId);
            if (merged is null) return null;
            foreach (var candidate in keyCandidates)
            {
                foreach (var entry in merged)
                {
                    if (string.Equals(entry.schemaKey, candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.attributeId;
                    }
                }
            }
            return null;
        }

        private IList<MergedSchemaEntry>? ResolveMergedSchemaEntries(string typeId)
        {
            if (!client.types.ContainsKey(typeId)) return null;
            try
            {
                return CustomTypeInheritance.MergeSchemas(
                    CustomTypeInheritance.ResolveChain(
                        typeId,
                        id => client.types.TryGetValue(id, out CustomType match) ? match : null));
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
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>? readOnlyFactories = null,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>? writableFactories = null,
            NeoValueOwnership writeOwnership = NeoValueOwnership.Save)
            : base(client, gridValueId, readOnlyFactories, writableFactories)
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
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory> writableFactories)
        {
            return ResolveForSave(client, gridValueId, readOnlyFactories, writableFactories);
        }

        public static NeoTileGridPrimitive ResolveForSave(NeoClient client, string gridValueId)
        {
            return ResolveForSave(client, gridValueId, EmptyReadOnlyFactories, EmptyWritableFactories);
        }

        public static NeoTileGridPrimitive ResolveForSave(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory> writableFactories)
        {
            return ResolveWithOwnership(
                client,
                gridValueId,
                readOnlyFactories,
                writableFactories,
                NeoValueOwnership.Save);
        }

        public static NeoTileGridPrimitive ResolveForSession(NeoClient client, string gridValueId)
        {
            return ResolveForSession(client, gridValueId, EmptyReadOnlyFactories, EmptyWritableFactories);
        }

        public static NeoTileGridPrimitive ResolveForSession(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory> writableFactories)
        {
            return ResolveWithOwnership(
                client,
                gridValueId,
                readOnlyFactories,
                writableFactories,
                NeoValueOwnership.Session);
        }

        private static NeoTileGridPrimitive ResolveWithOwnership(
            NeoClient client,
            string gridValueId,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory> readOnlyFactories,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory> writableFactories,
            NeoValueOwnership writeOwnership)
        {
            if (client is null) throw new ArgumentNullException(nameof(client));
            return new NeoTileGridPrimitive(
                client,
                gridValueId,
                readOnlyFactories,
                writableFactories,
                writeOwnership);
        }

        // -------------------------------------------------------------------
        // Mutations — plain value writes against the containment model:
        // placements are rows in the requested ownership carrying the layer
        // link's list value id as their containerId; removal is a tombstone
        // (authored members) or a row drop (overlay-created members).
        // -------------------------------------------------------------------

        public NeoPlacementResult TrySetTile(
            string layerId,
            Vector2Int cell,
            INeoValueReference tile,
            string expectedTileFamilyTypeId)
        {
            if (!TryValidateGeneratedValue(
                    tile,
                    expectedTileFamilyTypeId,
                    "tile",
                    out string? tileValueId,
                    out string? tileTypeId,
                    out NeoGeneratedCustomValue? generatedTile,
                    out NeoPlacementResult? error))
            {
                return error!;
            }

            NeoTilePlacementRecord? existing = null;
            foreach (var candidate in LookupCache.TileCandidatesAt(layerId, cell))
            {
                if (candidate.IsObjectCarried) continue;
                existing = candidate;
            }

            if (existing is not null)
            {
                Lifecycle?.BeforeSetTile(new NeoTileSetContext(
                    this,
                    layerId,
                    cell,
                    existing.InstanceId,
                    generatedTile!,
                    tileValueId!,
                    tileTypeId,
                    BuildTileInstanceJson(existing)));
                var writeError = WritePlacementTileLookup(existing, tileValueId!);
                if (writeError is not null) return writeError;
                NotifyTileLayerChanged(
                    layerId,
                    Array.Empty<Vector2Int>(),
                    new[] { cell },
                    NeoTileGridChangeSourceKind.Direct,
                    existing.InstanceId);
                return NeoPlacementResult.Success();
            }

            NeoGridLayerLinkModel? targetLink = null;
            foreach (var link in ResolveGridLinks(null))
            {
                if (!link.IsTileLink || link.LayerId != layerId) continue;
                targetLink = link;
                break;
            }
            if (targetLink is null)
            {
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
                generatedTile!,
                tileValueId!,
                tileTypeId,
                null));

            var createError = CreatePlacementRows(targetLink, instanceId, cell, tileValueId!);
            if (createError is not null) return createError;
            NotifyTileLayerChanged(
                layerId,
                Array.Empty<Vector2Int>(),
                new[] { cell },
                NeoTileGridChangeSourceKind.Direct,
                instanceId);
            return NeoPlacementResult.Success();
        }

        public NeoPlacementResult TryConvertTile(
            NeoTileInstanceId instanceId,
            INeoValueReference target,
            string expectedTileFamilyTypeId)
        {
            if (!TryValidateGeneratedValue(
                    target,
                    expectedTileFamilyTypeId,
                    "tile",
                    out string? targetValueId,
                    out string? targetTypeId,
                    out NeoGeneratedCustomValue? generatedTarget,
                    out NeoPlacementResult? error))
            {
                return error!;
            }
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
                generatedTarget!,
                targetValueId!,
                targetTypeId,
                BuildTileInstanceJson(record)));

            var writeError = WritePlacementTileLookup(record, targetValueId!);
            if (writeError is not null) return writeError;
            NotifyTileLayerChanged(
                layerId,
                Array.Empty<Vector2Int>(),
                new[] { record.Cell },
                NeoTileGridChangeSourceKind.Direct,
                instanceId.Value);
            return NeoPlacementResult.Success();
        }

        public NeoPlacementResult TryResetTile(NeoTileInstanceId instanceId)
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
                BuildTileInstanceJson(record)));

            // Restore authored: drop the write-ownership overlay rows (and any
            // tombstones) for the placement subtree so the authored rows —
            // when they exist — resurface. A runtime-created placement has no
            // authored rows to fall back to, so a reset removes it entirely.
            client.RemoveWritableShadow(writeOwnership, record.PlacementValueId);
            if (record.CellValueId is not null)
            {
                client.RemoveWritableShadow(writeOwnership, record.CellValueId);
            }
            if (record.TileLookupValueId is not null)
            {
                client.RemoveWritableShadow(writeOwnership, record.TileLookupValueId);
            }
            NotifyTileLayerChanged(
                layerId,
                Array.Empty<Vector2Int>(),
                new[] { record.Cell },
                NeoTileGridChangeSourceKind.Direct,
                instanceId.Value);
            return NeoPlacementResult.Success();
        }

        public NeoPlacementResult TryRemoveTile(NeoTileInstanceId instanceId)
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
                BuildTileInstanceJson(record)));

            RemoveMemberRow(record.PlacementValueId);
            NotifyTileLayerChanged(
                layerId,
                new[] { record.Cell },
                Array.Empty<Vector2Int>(),
                NeoTileGridChangeSourceKind.Direct,
                instanceId.Value);
            return NeoPlacementResult.Success();
        }

        public NeoPlacementResult TrySpawnObject(
            string layerId,
            Vector2Int cell,
            INeoValueReference obj,
            string expectedObjectFamilyTypeId)
        {
            if (!TryValidateGeneratedValue(
                    obj,
                    expectedObjectFamilyTypeId,
                    "object",
                    out string? objectValueId,
                    out string? objectTypeId,
                    out NeoGeneratedCustomValue? generatedObject,
                    out NeoPlacementResult? error))
            {
                return error!;
            }
            var occupants = LookupCache.ObjectCandidatesAt(layerId, cell);
            if (occupants.Count > 0)
            {
                return NeoPlacementResult.Error(
                    "tile-grid-object-cell-occupied",
                    $"Object layer '{layerId}' already has object instance '{occupants[occupants.Count - 1].InstanceId}' at cell ({cell.x}, {cell.y}).");
            }

            NeoGridLayerLinkModel? targetLink = null;
            foreach (var link in ResolveGridLinks(null))
            {
                if (link.IsTileLink || link.LayerId != layerId) continue;
                targetLink = link;
                break;
            }
            if (targetLink is null)
            {
                return NeoPlacementResult.Error(
                    "tile-grid-layer-link-missing",
                    $"Grid '{GridValueId}' has no object layer link targeting layer '{layerId}'; cannot spawn an object.");
            }
            if (string.IsNullOrEmpty(objectTypeId))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-object-missing-type",
                    $"Object value '{objectValueId}' has no typeId; cannot spawn an instance of it.");
            }

            string instanceId = Guid.NewGuid().ToString();
            Lifecycle?.BeforeSpawnObject(new NeoObjectSpawnContext(
                this,
                layerId,
                cell,
                instanceId,
                generatedObject!,
                objectValueId!,
                objectTypeId));

            var createError = CreateObjectRows(targetLink, instanceId, cell, objectTypeId!);
            if (createError is not null) return createError;
            NotifyObjectLayerChanged(
                layerId,
                Array.Empty<NeoObjectInstanceId>(),
                new[] { new NeoObjectInstanceId(instanceId) },
                new[] { cell },
                NeoTileGridChangeSourceKind.Direct,
                instanceId);
            return NeoPlacementResult.Success();
        }

        public NeoPlacementResult TryDespawnObject(NeoObjectInstanceId instanceId)
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
            NotifyObjectLayerChanged(
                layerId,
                new[] { instanceId },
                Array.Empty<NeoObjectInstanceId>(),
                record.Footprint,
                NeoTileGridChangeSourceKind.Direct,
                instanceId.Value);
            return NeoPlacementResult.Success();
        }

        public NeoPlacementResult TrySwapVariant(
            NeoObjectInstanceId instanceId,
            INeoValueReference variant,
            string expectedObjectFamilyTypeId)
        {
            if (!TryValidateGeneratedValue(
                    variant,
                    expectedObjectFamilyTypeId,
                    "object",
                    out string? variantValueId,
                    out string? variantTypeId,
                    out NeoGeneratedCustomValue? generatedVariant,
                    out NeoPlacementResult? error))
            {
                return error!;
            }
            if (!TryFindObjectRecord(instanceId.Value, out var record, out string layerId))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-instance-missing",
                    $"Object instance '{instanceId.Value}' was not found in grid '{GridValueId}'.");
            }
            if (string.IsNullOrEmpty(variantTypeId))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-object-missing-type",
                    $"Variant value '{variantValueId}' has no typeId; cannot swap instance '{instanceId.Value}' to it.");
            }

            Lifecycle?.BeforeSwapObjectVariant(new NeoObjectVariantSwapContext(
                this,
                layerId,
                record.Cell,
                instanceId.Value,
                generatedVariant!,
                variantValueId!,
                variantTypeId,
                BuildObjectInstanceJson(record, layerId)));

            if (!client.TryGetOverlaidValue(
                    writeOwnership, record.InstanceId, out ObjectAttributeValue? objectRow))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-instance-row-missing",
                    $"Object instance row '{record.InstanceId}' could not be resolved for a variant swap.");
            }
            var shadow = (ObjectAttributeValue)client.CloneRowForWrite(objectRow);
            shadow.typeId = variantTypeId;
            shadow.updatedAt = NeoTimestamp.Now();
            client.SetWritableValue(writeOwnership, shadow);
            NotifyObjectLayerChanged(
                layerId,
                Array.Empty<NeoObjectInstanceId>(),
                new[] { instanceId },
                record.Footprint,
                NeoTileGridChangeSourceKind.Direct,
                instanceId.Value);
            return NeoPlacementResult.Success();
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
            string tileValueId)
        {
            string? tilesAttributeId = FindSchemaAttributeId(link.LinkTypeId, TilesKeyCandidatesForWrite);
            if (tilesAttributeId is null)
            {
                return NeoPlacementResult.Error(
                    "tile-grid-link-tiles-attribute-missing",
                    $"Tile layer link type '{link.LinkTypeId}' has no 'Tiles' schema attribute; cannot create a placement.");
            }
            if (!client.attributes.TryGetValue(tilesAttributeId, out Json.Attribute tilesAttribute)
                || tilesAttribute is not ListAttribute tilesList)
            {
                return NeoPlacementResult.Error(
                    "tile-grid-link-tiles-attribute-invalid",
                    $"Attribute '{tilesAttributeId}' backing the link's Tiles list is not a List attribute.");
            }
            if (!client.attributes.TryGetValue(tilesList.entryAttributeId, out Json.Attribute entryAttribute)
                || entryAttribute is not CustomAttribute entryCustom
                || string.IsNullOrEmpty(entryCustom.customTypeId))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-placement-entry-attribute-invalid",
                    $"Entry attribute '{tilesList.entryAttributeId}' of the link's Tiles list does not declare a placement custom type.");
            }
            string placementTypeId = entryCustom.customTypeId;
            string? cellKey = FindSchemaKey(placementTypeId, CellKeyCandidatesForWrite);
            string? tileKey = FindSchemaKey(placementTypeId, TileKeyCandidatesForWrite);
            if (cellKey is null)
            {
                return NeoPlacementResult.Error(
                    "tile-grid-placement-cell-key-missing",
                    $"Placement type '{placementTypeId}' has no 'Cell' schema key.");
            }
            if (tileKey is null)
            {
                return NeoPlacementResult.Error(
                    "tile-grid-placement-tile-key-missing",
                    $"Placement type '{placementTypeId}' has no 'Tile' schema key.");
            }

            var now = NeoTimestamp.Now();
            // Storage partitions: the placement subtree lives in its
            // container's partition. The placement row would inherit through
            // its containerId; its owned Cell/Tile children have no
            // containment edge of their own, so the whole subtree is stamped
            // here at creation.
            string? partitionMapKey = client.ResolveEffectiveRow(link.ListValueId)?.mapKey;
            var cellRow = new Vector2AttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = now,
                updatedAt = now,
                mapKey = partitionMapKey,
                value = new NeoVector2Value { x = cell.x, y = cell.y },
            };
            var tileRow = new ArrayAttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = now,
                updatedAt = now,
                mapKey = partitionMapKey,
                value = new[] { tileValueId },
            };
            var placementRow = new ObjectAttributeValue
            {
                id = placementValueId,
                createdAt = now,
                updatedAt = now,
                typeId = placementTypeId,
                containerId = link.ListValueId,
                mapKey = partitionMapKey,
                value = new Dictionary<string, string>
                {
                    [cellKey] = cellRow.id,
                    [tileKey] = tileRow.id,
                },
            };
            client.SetWritableValue(writeOwnership, cellRow);
            client.SetWritableValue(writeOwnership, tileRow);
            client.SetWritableValue(writeOwnership, placementRow);
            return null;
        }

        private NeoPlacementResult? CreateObjectRows(
            NeoGridLayerLinkModel link,
            string objectRowId,
            Vector2Int cell,
            string objectTypeId)
        {
            string? positionKey = FindSchemaKey(objectTypeId, PositionKeyCandidatesForWrite);
            if (positionKey is null)
            {
                return NeoPlacementResult.Error(
                    "tile-grid-object-position-key-missing",
                    $"Object type '{objectTypeId}' has no 'Position' schema key; cannot spawn it at a cell.");
            }
            string? sizeKey = FindSchemaKey(objectTypeId, SizeKeyCandidatesForWrite);

            var now = NeoTimestamp.Now();
            // Storage partitions: the spawned object's subtree lives in its
            // container's partition (see CreatePlacementRows).
            string? partitionMapKey = client.ResolveEffectiveRow(link.ListValueId)?.mapKey;
            var record = new Dictionary<string, string>();
            var positionRow = new Vector3AttributeValue
            {
                id = Guid.NewGuid().ToString(),
                createdAt = now,
                updatedAt = now,
                mapKey = partitionMapKey,
                value = new NeoVector3Value { x = cell.x, y = cell.y, z = 0 },
            };
            record[positionKey] = positionRow.id;
            Vector3AttributeValue? sizeRow = null;
            if (sizeKey is not null)
            {
                sizeRow = new Vector3AttributeValue
                {
                    id = Guid.NewGuid().ToString(),
                    createdAt = now,
                    updatedAt = now,
                    mapKey = partitionMapKey,
                    value = new NeoVector3Value { x = 1, y = 1, z = 0 },
                };
                record[sizeKey] = sizeRow.id;
            }
            var objectRow = new ObjectAttributeValue
            {
                id = objectRowId,
                createdAt = now,
                updatedAt = now,
                typeId = objectTypeId,
                containerId = link.ListValueId,
                mapKey = partitionMapKey,
                value = record,
            };
            client.SetWritableValue(writeOwnership, positionRow);
            if (sizeRow is not null) client.SetWritableValue(writeOwnership, sizeRow);
            client.SetWritableValue(writeOwnership, objectRow);
            return null;
        }

        /// <summary>Edits a placement's single-select "Tile" lookup row in the
        /// write ownership (clone-on-write at the stable id).</summary>
        private NeoPlacementResult? WritePlacementTileLookup(
            NeoTilePlacementRecord record,
            string tileValueId)
        {
            if (record.TileLookupValueId is null)
            {
                return NeoPlacementResult.Error(
                    "tile-grid-placement-tile-lookup-missing",
                    $"Tile instance '{record.InstanceId}' has no backing Tile lookup row to edit.");
            }
            var now = NeoTimestamp.Now();
            ArrayAttributeValue next;
            if (client.TryGetOverlaidValue(
                    writeOwnership, record.TileLookupValueId, out ArrayAttributeValue? existing))
            {
                next = (ArrayAttributeValue)client.CloneRowForWrite(existing);
            }
            else
            {
                next = new ArrayAttributeValue
                {
                    id = record.TileLookupValueId,
                    createdAt = now,
                    // A fresh lookup row belongs to its placement's partition.
                    mapKey = client.ResolveEffectiveRow(record.PlacementValueId)?.mapKey,
                };
            }
            next.value = new[] { tileValueId };
            next.updatedAt = now;
            client.SetWritableValue(writeOwnership, next);
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
            if (client.TryGetWritableValue(writeOwnership, memberValueId, out AttributeValue? _))
            {
                client.RemoveWritableValueAndDescendants(writeOwnership, memberValueId);
                return;
            }
            // The row lives in a lower overlay (e.g. session removal of a
            // save-created member): shadow it with a tombstone.
            client.WriteRemovalTombstone(writeOwnership, memberValueId);
        }

        private JObject BuildTileInstanceJson(NeoTilePlacementRecord record)
        {
            string? tileTypeId = client.ResolveEffectiveRow(record.TileValueId) is ObjectAttributeValue tileRow
                ? tileRow.typeId
                : null;
            return new JObject
            {
                ["id"] = record.InstanceId,
                ["tileValueId"] = record.TileValueId,
                ["tileTypeId"] = tileTypeId is null ? JValue.CreateNull() : new JValue(tileTypeId),
                ["position"] = new JObject { ["x"] = record.Cell.x, ["y"] = record.Cell.y },
                ["layerLinkId"] = record.SourceTileLayerLinkId,
                ["order"] = record.Order,
            };
        }

        private JObject BuildObjectInstanceJson(NeoObjectPlacementRecord record, string layerId)
        {
            string? objectTypeId = client.ResolveEffectiveRow(record.InstanceId) is ObjectAttributeValue objectRow
                ? objectRow.typeId
                : null;
            var footprint = new JArray();
            foreach (var cell in record.Footprint)
            {
                footprint.Add(new JObject { ["x"] = cell.x, ["y"] = cell.y });
            }
            return new JObject
            {
                ["id"] = record.InstanceId,
                ["objectValueId"] = record.InstanceId,
                ["objectTypeId"] = objectTypeId is null ? JValue.CreateNull() : new JValue(objectTypeId),
                ["position"] = new JObject { ["x"] = record.Cell.x, ["y"] = record.Cell.y },
                ["footprint"] = footprint,
                ["objectLayerId"] = layerId,
                ["order"] = record.Order,
            };
        }

        private bool TryValidateGeneratedValue(
            INeoValueReference value,
            string expectedFamilyTypeId,
            string label,
            out string? valueId,
            out string? typeId,
            out NeoGeneratedCustomValue? generatedValue,
            out NeoPlacementResult? error)
        {
            valueId = value?.valueId;
            typeId = null;
            generatedValue = value as NeoGeneratedCustomValue;
            error = null;
            if (generatedValue is null)
            {
                error = NeoPlacementResult.Error(
                    "tile-grid-value-not-generated",
                    $"Cannot place {label}; value is not a generated Neo custom value.");
                return false;
            }
            if (string.IsNullOrEmpty(valueId))
            {
                error = NeoPlacementResult.Error(
                    "tile-grid-value-missing-id",
                    $"Cannot place {label}; generated value has no backing value id.");
                return false;
            }
            if (!client.TryGetValue(valueId!, out ObjectAttributeValue? row))
            {
                error = NeoPlacementResult.Error(
                    "tile-grid-value-missing",
                    $"Cannot place {label}; value '{valueId}' is not present in the project or save graph.");
                return false;
            }
            typeId = row.typeId;
            if (!ValueExtendsType(valueId!, expectedFamilyTypeId))
            {
                error = NeoPlacementResult.Error(
                    "tile-grid-value-wrong-type",
                    $"Cannot place {label} value '{valueId}' in this layer; it does not extend expected type '{expectedFamilyTypeId}'.");
                return false;
            }
            return true;
        }

        // Write paths need the same candidate lists the (protected) read
        // model uses; re-declared privately because the base's arrays are
        // private to it.
        private static readonly string[] TilesKeyCandidatesForWrite = { "Tiles", "tileInstances" };
        private static readonly string[] CellKeyCandidatesForWrite = { "Cell", "Position" };
        private static readonly string[] TileKeyCandidatesForWrite = { "Tile", "tileValue", "tileValueId" };
        private static readonly string[] PositionKeyCandidatesForWrite = { "Position" };
        private static readonly string[] SizeKeyCandidatesForWrite = { "Size" };
    }
}
