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

    public sealed class NeoResolvedTileInstance
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
            Tile = tile ?? throw new ArgumentNullException(nameof(tile));
            Order = order;
            SourceKind = sourceKind;
            SourceObjectInstanceId = sourceObjectInstanceId;
            SourceTileLayerLinkId = sourceTileLayerLinkId;
        }

        public NeoTileInstanceId InstanceId { get; }
        public string LayerId { get; }
        public Vector2Int Cell { get; }
        public NeoGeneratedCustomValue Tile { get; }
        public int Order { get; }
        public NeoTileOutputSourceKind SourceKind { get; }
        public string? SourceObjectInstanceId { get; }
        public string? SourceTileLayerLinkId { get; }
    }

    public sealed class NeoResolvedObjectInstance
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
            Object = obj ?? throw new ArgumentNullException(nameof(obj));
            Order = order;
        }

        public NeoObjectInstanceId InstanceId { get; }
        public string LayerId { get; }
        public Vector2Int Cell { get; }
        public IReadOnlyList<Vector2Int> Footprint { get; }
        public NeoGeneratedCustomValue Object { get; }
        public int Order { get; }
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

        public virtual NeoResolvedTileInstance? ResolveTile(Vector2Int cell) => null;

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

        public virtual NeoResolvedObjectInstance? ResolveObject(NeoObjectInstanceId instanceId) =>
            null;

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

        public TTile? GetTile(Vector2Int cell) =>
            primitive.GetTile<TTile>(LayerId, cell, ExpectedTypeId);

        public IReadOnlyList<NeoTileCandidate<TTile>> GetCandidates(Vector2Int cell) =>
            primitive.GetTileCandidates<TTile>(LayerId, cell, ExpectedTypeId);

        public NeoTileConflict<TTile>? GetConflict(Vector2Int cell) =>
            primitive.GetTileConflict<TTile>(LayerId, cell, ExpectedTypeId);

        public override IReadOnlyList<NeoResolvedTileInstance> GetTiles() =>
            primitive.GetTiles(LayerId, ExpectedTypeId);

        internal override NeoTileLayerRenderSnapshot GetRenderSnapshot() =>
            primitive.GetTileLayerRenderSnapshot(LayerId, ExpectedTypeId);

        public override NeoResolvedTileInstance? ResolveTile(Vector2Int cell) =>
            primitive.ResolveTile(LayerId, cell, ExpectedTypeId);

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

        public TObject? GetObject(Vector2Int cell) =>
            primitive.GetObject<TObject>(LayerId, cell, ExpectedTypeId);

        public override IReadOnlyList<NeoResolvedObjectInstance> GetObjects() =>
            primitive.GetObjects(LayerId, ExpectedTypeId);

        public override NeoResolvedObjectInstance? ResolveObject(NeoObjectInstanceId instanceId) =>
            primitive.ResolveObjectInstance(LayerId, instanceId, ExpectedTypeId);

        public override IDisposable OnChanged(Action<NeoObjectLayerChangedArgs> handler) =>
            primitive.OnObjectLayerChanged(LayerId, handler);
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
        private event Action<NeoTileGridChangedArgs>? Changed;

        protected NeoReadOnlyTileGridPrimitive(
            NeoClient client,
            string gridValueId,
            TileGridContent? content,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>? readOnlyFactories = null,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>? writableFactories = null)
        {
            this.client = client ?? throw new ArgumentNullException(nameof(client));
            GridValueId = gridValueId ?? throw new ArgumentNullException(nameof(gridValueId));
            Content = content;
            this.readOnlyFactories = readOnlyFactories ?? EmptyReadOnlyFactories;
            this.writableFactories = writableFactories ?? EmptyWritableFactories;
        }

        public string GridValueId { get; }
        public TileGridContent? Content { get; }
        public NeoTileGridRenderer? Renderer { get; internal set; }

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
            client.tileGridContents.TryGetValue(gridValueId, out TileGridContent? content);
            return new NeoReadOnlyTileGridPrimitive(
                client,
                gridValueId,
                content,
                readOnlyFactories,
                writableFactories);
        }

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
            foreach (var region in ResolveLayerRegions(layerId, "tile"))
            {
                var instances = ReadInstances(region.Data);
                if (instances is null) continue;
                foreach (var rawInstance in instances)
                {
                    if (rawInstance is not JObject instance) continue;
                    if (!TryReadCell(instance["position"], out Vector2Int position)) continue;
                    if (position != cell) continue;
                    string? instanceId = ReadString(instance["id"]);
                    string? tileValueId = ReadString(instance["tileValueId"]);
                    if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(tileValueId)) continue;
                    if (!TileInstanceSourceContainsInstance(instance, layerId, instanceId!)) continue;
                    TTile? tile = ResolveGeneratedValue<TTile>(
                        tileValueId!,
                        expectedTileFamilyTypeId);
                    if (tile is null) continue;
                    ReadTileInstanceSource(
                        instance,
                        layerId,
                        out NeoTileOutputSourceKind sourceKind,
                        out string? sourceTileLayerLinkId);
                    candidates.Add(new NeoTileCandidate<TTile>(
                        instanceId!,
                        cell,
                        tile,
                        ReadInt(instance["order"]) ?? 0,
                        sourceKind,
                        null,
                        sourceTileLayerLinkId));
                }

                SortTileCandidates(candidates, ReadConflictOrder(region.Data));
            }
            var linkedCandidates = new List<NeoTileCandidate<TTile>>(
                ResolveTileLayerLinkCandidates<TTile>(
                    layerId,
                    expectedTileFamilyTypeId,
                    cell));
            linkedCandidates.Sort((left, right) =>
            {
                int order = left.Order.CompareTo(right.Order);
                return order != 0
                    ? order
                    : left.InstanceId.Value.CompareTo(right.InstanceId.Value);
            });
            candidates.AddRange(linkedCandidates);
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
            var byCell = new Dictionary<Vector2Int, List<NeoResolvedTileInstance>>();
            foreach (var region in ResolveLayerRegions(layerId, "tile"))
            {
                var regionCandidates = new List<NeoResolvedTileInstance>();
                var instances = ReadInstances(region.Data);
                if (instances is null) continue;
                foreach (var rawInstance in instances)
                {
                    if (rawInstance is not JObject instance) continue;
                    if (!TryReadCell(instance["position"], out Vector2Int position)) continue;
                    string? instanceId = ReadString(instance["id"]);
                    string? tileValueId = ReadString(instance["tileValueId"]);
                    if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(tileValueId)) continue;
                    if (!TileInstanceSourceContainsInstance(instance, layerId, instanceId!)) continue;
                    var tile = ResolveGeneratedValue<NeoGeneratedCustomValue>(
                        tileValueId!,
                        expectedTileFamilyTypeId);
                    if (tile is null) continue;
                    ReadTileInstanceSource(
                        instance,
                        layerId,
                        out NeoTileOutputSourceKind sourceKind,
                        out string? sourceTileLayerLinkId);
                    regionCandidates.Add(new NeoResolvedTileInstance(
                        instanceId!,
                        layerId,
                        position,
                        tile,
                        ReadInt(instance["order"]) ?? 0,
                        sourceKind,
                        null,
                        sourceTileLayerLinkId));
                }
                SortResolvedTiles(regionCandidates, ReadConflictOrder(region.Data));
                foreach (var candidate in regionCandidates)
                {
                    if (!byCell.TryGetValue(candidate.Cell, out var candidates))
                    {
                        candidates = new List<NeoResolvedTileInstance>();
                        byCell[candidate.Cell] = candidates;
                    }
                    candidates.Add(candidate);
                }
            }
            var linkedTiles = new List<NeoResolvedTileInstance>(
                ResolveTileLayerLinkTiles(
                    layerId,
                    expectedTileFamilyTypeId,
                    null));
            linkedTiles.Sort((left, right) =>
            {
                int order = left.Order.CompareTo(right.Order);
                return order != 0
                    ? order
                    : left.InstanceId.Value.CompareTo(right.InstanceId.Value);
            });
            foreach (var candidate in linkedTiles)
            {
                if (!byCell.TryGetValue(candidate.Cell, out var candidates))
                {
                    candidates = new List<NeoResolvedTileInstance>();
                    byCell[candidate.Cell] = candidates;
                }
                candidates.Add(candidate);
            }

            var winners = new List<NeoResolvedTileInstance>();
            foreach (var candidates in byCell.Values)
            {
                if (candidates.Count == 0) continue;
                winners.Add(candidates[candidates.Count - 1]);
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
            var byCell = new Dictionary<Vector2Int, List<NeoResolvedTileInstance>>();
            foreach (var region in ResolveLayerRegions(layerId, "tile"))
            {
                var regionCandidates = new List<NeoResolvedTileInstance>();
                var instances = ReadInstances(region.Data);
                if (instances is null) continue;
                foreach (var rawInstance in instances)
                {
                    if (rawInstance is not JObject instance) continue;
                    if (!TryReadCell(instance["position"], out Vector2Int position)) continue;
                    string? instanceId = ReadString(instance["id"]);
                    string? tileValueId = ReadString(instance["tileValueId"]);
                    if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(tileValueId)) continue;
                    if (!TileInstanceSourceContainsInstance(instance, layerId, instanceId!)) continue;
                    var tile = ResolveGeneratedValue<NeoGeneratedCustomValue>(
                        tileValueId!,
                        expectedTileFamilyTypeId);
                    if (tile is null) continue;
                    ReadTileInstanceSource(
                        instance,
                        layerId,
                        out NeoTileOutputSourceKind sourceKind,
                        out string? sourceTileLayerLinkId);
                    regionCandidates.Add(new NeoResolvedTileInstance(
                        instanceId!,
                        layerId,
                        position,
                        tile,
                        ReadInt(instance["order"]) ?? 0,
                        sourceKind,
                        null,
                        sourceTileLayerLinkId));
                }
                SortResolvedTiles(regionCandidates, ReadConflictOrder(region.Data));
                foreach (var candidate in regionCandidates)
                {
                    AddCandidate(byCell, candidate);
                }
            }

            var linkedTiles = new List<NeoResolvedTileInstance>(
                ResolveTileLayerLinkTiles(
                    layerId,
                    expectedTileFamilyTypeId,
                    null));
            linkedTiles.Sort((left, right) =>
            {
                int order = left.Order.CompareTo(right.Order);
                return order != 0
                    ? order
                    : left.InstanceId.Value.CompareTo(right.InstanceId.Value);
            });
            foreach (var candidate in linkedTiles)
            {
                AddCandidate(byCell, candidate);
            }

            var winners = new List<NeoResolvedTileInstance>();
            var sourceTiles = new Dictionary<string, List<NeoResolvedTileInstance>>();
            var candidateCounts = new Dictionary<Vector2Int, int>();
            foreach (var pair in byCell)
            {
                var candidates = pair.Value;
                candidateCounts[pair.Key] = candidates.Count;
                if (candidates.Count == 0) continue;
                winners.Add(candidates[candidates.Count - 1]);
                foreach (var candidate in candidates)
                {
                    if (candidate.SourceKind != NeoTileOutputSourceKind.TileLayerLink ||
                        string.IsNullOrEmpty(candidate.SourceTileLayerLinkId))
                    {
                        continue;
                    }
                    if (!sourceTiles.TryGetValue(candidate.SourceTileLayerLinkId!, out var tiles))
                    {
                        tiles = new List<NeoResolvedTileInstance>();
                        sourceTiles[candidate.SourceTileLayerLinkId!] = tiles;
                    }
                    tiles.Add(candidate);
                }
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

        private static void AddCandidate(
            Dictionary<Vector2Int, List<NeoResolvedTileInstance>> byCell,
            NeoResolvedTileInstance candidate)
        {
            if (!byCell.TryGetValue(candidate.Cell, out var candidates))
            {
                candidates = new List<NeoResolvedTileInstance>();
                byCell[candidate.Cell] = candidates;
            }
            candidates.Add(candidate);
        }

        internal virtual NeoResolvedTileInstance? ResolveTile(
            string layerId,
            Vector2Int cell,
            string expectedTileFamilyTypeId = "")
        {
            var candidates = new List<NeoResolvedTileInstance>();
            foreach (var region in ResolveLayerRegions(layerId, "tile"))
            {
                var regionCandidates = new List<NeoResolvedTileInstance>();
                var instances = ReadInstances(region.Data);
                if (instances is null) continue;
                foreach (var rawInstance in instances)
                {
                    if (rawInstance is not JObject instance) continue;
                    if (!TryReadCell(instance["position"], out Vector2Int position)) continue;
                    if (position != cell) continue;
                    string? instanceId = ReadString(instance["id"]);
                    string? tileValueId = ReadString(instance["tileValueId"]);
                    if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(tileValueId)) continue;
                    if (!TileInstanceSourceContainsInstance(instance, layerId, instanceId!)) continue;
                    var tile = ResolveGeneratedValue<NeoGeneratedCustomValue>(
                        tileValueId!,
                        expectedTileFamilyTypeId);
                    if (tile is null) continue;
                    ReadTileInstanceSource(
                        instance,
                        layerId,
                        out NeoTileOutputSourceKind sourceKind,
                        out string? sourceTileLayerLinkId);
                    regionCandidates.Add(new NeoResolvedTileInstance(
                        instanceId!,
                        layerId,
                        position,
                        tile,
                        ReadInt(instance["order"]) ?? 0,
                        sourceKind,
                        null,
                        sourceTileLayerLinkId));
                }
                SortResolvedTiles(regionCandidates, ReadConflictOrder(region.Data));
                candidates.AddRange(regionCandidates);
            }

            var linkedTiles = new List<NeoResolvedTileInstance>(
                ResolveTileLayerLinkTiles(
                    layerId,
                    expectedTileFamilyTypeId,
                    cell));
            linkedTiles.Sort((left, right) =>
            {
                int order = left.Order.CompareTo(right.Order);
                return order != 0
                    ? order
                    : left.InstanceId.Value.CompareTo(right.InstanceId.Value);
            });
            candidates.AddRange(linkedTiles);
            return candidates.Count == 0 ? null : candidates[candidates.Count - 1];
        }

        internal virtual IReadOnlyList<NeoResolvedTileInstance> GetTileLayerLinkTiles(
            string layerId,
            string sourceTileLayerLinkId,
            string expectedTileFamilyTypeId = "")
        {
            var tiles = new List<NeoResolvedTileInstance>();
            if (string.IsNullOrEmpty(sourceTileLayerLinkId)) return tiles;

            foreach (var region in ResolveLayerRegions(layerId, "tile"))
            {
                var instances = ReadInstances(region.Data);
                if (instances is null) continue;
                foreach (var rawInstance in instances)
                {
                    if (rawInstance is not JObject instance) continue;
                    ReadTileInstanceSource(
                        instance,
                        layerId,
                        out NeoTileOutputSourceKind sourceKind,
                        out string? sourceId);
                    if (sourceKind != NeoTileOutputSourceKind.TileLayerLink ||
                        sourceId != sourceTileLayerLinkId)
                    {
                        continue;
                    }
                    if (!TryReadCell(instance["position"], out Vector2Int position)) continue;
                    string? instanceId = ReadString(instance["id"]);
                    string? tileValueId = ReadString(instance["tileValueId"]);
                    if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(tileValueId)) continue;
                    if (!TileInstanceSourceContainsInstance(instance, layerId, instanceId!)) continue;
                    var tile = ResolveGeneratedValue<NeoGeneratedCustomValue>(
                        tileValueId!,
                        expectedTileFamilyTypeId);
                    if (tile is null) continue;
                    tiles.Add(new NeoResolvedTileInstance(
                        instanceId!,
                        layerId,
                        position,
                        tile,
                        ReadInt(instance["order"]) ?? 0,
                        sourceKind,
                        null,
                        sourceId));
                }
            }

            tiles.AddRange(ResolveTileLayerLinkTilesForSource(
                layerId,
                expectedTileFamilyTypeId,
                sourceTileLayerLinkId));
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

        public virtual TObject? GetObject<TObject>(
            string layerId,
            Vector2Int cell,
            string expectedObjectFamilyTypeId)
            where TObject : class, INeoValueReference
        {
            TObject? best = null;
            int bestOrder = int.MinValue;
            foreach (var region in ResolveLayerRegions(layerId, "object"))
            {
                var instances = ReadInstances(region.Data);
                if (instances is null) continue;
                foreach (var rawInstance in instances)
                {
                    if (rawInstance is not JObject instance) continue;
                    if (!ObjectInstanceOccupiesCell(instance, cell)) continue;
                    string? objectValueId = ReadString(instance["objectValueId"]);
                    if (string.IsNullOrEmpty(objectValueId)) continue;
                    TObject? obj = ResolveGeneratedValue<TObject>(
                        objectValueId!,
                        expectedObjectFamilyTypeId);
                    if (obj is null) continue;
                    int order = ReadInt(instance["order"]) ?? 0;
                    if (best is not null && order < bestOrder) continue;
                    best = obj;
                    bestOrder = order;
                }
            }
            return best;
        }

        public virtual IReadOnlyList<NeoResolvedObjectInstance> GetObjects(
            string layerId,
            string expectedObjectFamilyTypeId = "")
        {
            var objects = new List<NeoResolvedObjectInstance>();
            foreach (var region in ResolveLayerRegions(layerId, "object"))
            {
                var instances = ReadInstances(region.Data);
                if (instances is null) continue;
                foreach (var rawInstance in instances)
                {
                    if (rawInstance is not JObject instance) continue;
                    if (!TryReadCell(instance["position"], out Vector2Int position)) continue;
                    string? instanceId = ReadString(instance["id"]);
                    string? objectValueId = ReadString(instance["objectValueId"]);
                    if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(objectValueId)) continue;
                    var obj = ResolveGeneratedValue<NeoGeneratedCustomValue>(
                        objectValueId!,
                        expectedObjectFamilyTypeId);
                    if (obj is null) continue;
                    objects.Add(new NeoResolvedObjectInstance(
                        instanceId!,
                        layerId,
                        position,
                        ReadFootprint(instance, position),
                        obj,
                        ReadInt(instance["order"]) ?? 0));
                }
            }
            objects.Sort((left, right) => left.Order.CompareTo(right.Order));
            return objects;
        }

        internal virtual NeoResolvedObjectInstance? ResolveObjectInstance(
            string layerId,
            NeoObjectInstanceId instanceId,
            string expectedObjectFamilyTypeId = "")
        {
            foreach (var region in ResolveLayerRegions(layerId, "object"))
            {
                var instances = ReadInstances(region.Data);
                if (instances is null) continue;
                foreach (var rawInstance in instances)
                {
                    if (rawInstance is not JObject instance) continue;
                    if (ReadString(instance["id"]) != instanceId.Value) continue;
                    if (!TryReadCell(instance["position"], out Vector2Int position)) continue;
                    string? objectValueId = ReadString(instance["objectValueId"]);
                    if (string.IsNullOrEmpty(objectValueId)) return null;
                    var obj = ResolveGeneratedValue<NeoGeneratedCustomValue>(
                        objectValueId!,
                        expectedObjectFamilyTypeId);
                    if (obj is null) return null;
                    return new NeoResolvedObjectInstance(
                        instanceId.Value,
                        layerId,
                        position,
                        ReadFootprint(instance, position),
                        obj,
                        ReadInt(instance["order"]) ?? 0);
                }
            }
            return null;
        }

        private IEnumerable<NeoTileCandidate<TTile>> ResolveTileLayerLinkCandidates<TTile>(
            string layerId,
            string expectedTileFamilyTypeId,
            Vector2Int? onlyCell)
            where TTile : class, INeoValueReference
        {
            if (Content?.tileLayerLinks is null) yield break;
            foreach (var link in Content.tileLayerLinks)
            {
                if (link is null) continue;
                if (link.targetTileLayerId != layerId) continue;
                if (!TryResolveTileLayerLinkOrigin(link, out Vector2Int origin, out int sourceOrder))
                {
                    continue;
                }
                if (link.tiles is null) continue;
                foreach (var tile in link.tiles)
                {
                    if (tile is null || string.IsNullOrEmpty(tile.tileValueId)) continue;
                    if (!TileLayerLinkSourceContainsTile(link, tile)) continue;
                    Vector2Int cell = origin + ToVector2Int(tile.position);
                    if (onlyCell.HasValue && cell != onlyCell.Value) continue;
                    string instanceId = BuildTileLayerLinkInstanceId(link, tile);
                    if (IsTileLayerLinkOutputRemoved(layerId, cell, instanceId)) continue;
                    TTile? resolvedTile = ResolveGeneratedValue<TTile>(
                        tile.tileValueId,
                        expectedTileFamilyTypeId);
                    if (resolvedTile is null) continue;
                    yield return new NeoTileCandidate<TTile>(
                        instanceId,
                        cell,
                        resolvedTile,
                        CombineTileLayerLinkOrder(sourceOrder, tile.order),
                        NeoTileOutputSourceKind.TileLayerLink,
                        link.objectInstanceId,
                        ResolveTileLayerLinkSourceId(link));
                }
            }
        }

        private IEnumerable<NeoResolvedTileInstance> ResolveTileLayerLinkTiles(
            string layerId,
            string expectedTileFamilyTypeId,
            Vector2Int? onlyCell)
        {
            if (Content?.tileLayerLinks is null) yield break;
            foreach (var link in Content.tileLayerLinks)
            {
                if (link is null) continue;
                if (link.targetTileLayerId != layerId) continue;
                if (!TryResolveTileLayerLinkOrigin(link, out Vector2Int origin, out int sourceOrder))
                {
                    continue;
                }
                if (link.tiles is null) continue;
                foreach (var tile in link.tiles)
                {
                    if (tile is null || string.IsNullOrEmpty(tile.tileValueId)) continue;
                    if (!TileLayerLinkSourceContainsTile(link, tile)) continue;
                    Vector2Int cell = origin + ToVector2Int(tile.position);
                    if (onlyCell.HasValue && cell != onlyCell.Value) continue;
                    string instanceId = BuildTileLayerLinkInstanceId(link, tile);
                    if (IsTileLayerLinkOutputRemoved(layerId, cell, instanceId)) continue;
                    var resolvedTile = ResolveGeneratedValue<NeoGeneratedCustomValue>(
                        tile.tileValueId,
                        expectedTileFamilyTypeId);
                    if (resolvedTile is null) continue;
                    yield return new NeoResolvedTileInstance(
                        instanceId,
                        layerId,
                        cell,
                        resolvedTile,
                        CombineTileLayerLinkOrder(sourceOrder, tile.order),
                        NeoTileOutputSourceKind.TileLayerLink,
                        link.objectInstanceId,
                        ResolveTileLayerLinkSourceId(link));
                }
            }
        }

        private IEnumerable<NeoResolvedTileInstance> ResolveTileLayerLinkTilesForSource(
            string layerId,
            string expectedTileFamilyTypeId,
            string sourceTileLayerLinkId)
        {
            if (Content?.tileLayerLinks is null) yield break;
            foreach (var link in Content.tileLayerLinks)
            {
                if (link is null) continue;
                if (link.targetTileLayerId != layerId) continue;
                if (ResolveTileLayerLinkSourceId(link) != sourceTileLayerLinkId) continue;
                if (!TryResolveTileLayerLinkOrigin(link, out Vector2Int origin, out int sourceOrder))
                {
                    continue;
                }
                if (link.tiles is null) continue;
                foreach (var tile in link.tiles)
                {
                    if (tile is null || string.IsNullOrEmpty(tile.tileValueId)) continue;
                    if (!TileLayerLinkSourceContainsTile(link, tile)) continue;
                    Vector2Int cell = origin + ToVector2Int(tile.position);
                    string instanceId = BuildTileLayerLinkInstanceId(link, tile);
                    if (IsTileLayerLinkOutputRemoved(layerId, cell, instanceId)) continue;
                    var resolvedTile = ResolveGeneratedValue<NeoGeneratedCustomValue>(
                        tile.tileValueId,
                        expectedTileFamilyTypeId);
                    if (resolvedTile is null) continue;
                    yield return new NeoResolvedTileInstance(
                        instanceId,
                        layerId,
                        cell,
                        resolvedTile,
                        CombineTileLayerLinkOrder(sourceOrder, tile.order),
                        NeoTileOutputSourceKind.TileLayerLink,
                        link.objectInstanceId,
                        sourceTileLayerLinkId);
                }
            }
        }

        protected bool TryResolveTileLayerLinkOrigin(
            TileGridLayerLinkPayload link,
            out Vector2Int origin,
            out int order)
        {
            origin = ToVector2Int(link.origin);
            order = link.order;
            if (string.IsNullOrEmpty(link.objectLayerId) ||
                string.IsNullOrEmpty(link.objectInstanceId))
            {
                return true;
            }

            foreach (var region in ResolveLayerRegions(link.objectLayerId, "object"))
            {
                var instances = ReadInstances(region.Data);
                if (instances is null) continue;
                foreach (var rawInstance in instances)
                {
                    if (rawInstance is not JObject instance) continue;
                    if (ReadString(instance["id"]) != link.objectInstanceId) continue;
                    if (TryReadCell(instance["position"], out Vector2Int currentOrigin))
                    {
                        origin = currentOrigin;
                    }
                    order = ReadInt(instance["order"]) ?? order;
                    return true;
                }
            }
            return false;
        }

        private bool IsTileLayerLinkOutputRemoved(
            string layerId,
            Vector2Int cell,
            string instanceId)
        {
            BuildRegionRef(cell, out string regionKey, out _, out _);
            return DeltaRemovesInstance(
                    client.GetTileGridRegionDelta(
                        NeoValueOwnership.Save,
                        GridValueId,
                        layerId,
                        "tile",
                        regionKey),
                    instanceId)
                || DeltaRemovesInstance(
                    client.GetTileGridRegionDelta(
                        NeoValueOwnership.Session,
                        GridValueId,
                        layerId,
                        "tile",
                        regionKey),
                    instanceId);
        }

        private static bool DeltaRemovesInstance(
            TileGridRegionDelta? delta,
            string instanceId)
        {
            if (delta?.delta is null) return false;
            if (delta.delta.entries?.ContainsKey(instanceId) == true) return false;
            if (delta.delta.restoredToAuthored?.Contains(instanceId) == true) return false;
            return delta.delta.removedInstanceIds?.Contains(instanceId) == true;
        }

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

        internal IReadOnlyList<NeoTileLayerLinkDependency> GetTileLayerLinkDependencies()
        {
            if (Content is null)
            {
                return Array.Empty<NeoTileLayerLinkDependency>();
            }

            var dependencies = new List<NeoTileLayerLinkDependency>();
            var seen = new HashSet<string>();
            foreach (var region in Content.regions)
            {
                if (region.deleted ||
                    region.layerKind != "tile" ||
                    string.IsNullOrEmpty(region.layerId))
                {
                    continue;
                }

                var instances = ReadInstances(region.data);
                if (instances is null) continue;
                foreach (var rawInstance in instances)
                {
                    if (rawInstance is not JObject instance) continue;
                    ReadTileInstanceSource(
                        instance,
                        region.layerId,
                        out NeoTileOutputSourceKind sourceKind,
                        out string? sourceTileLayerLinkId);
                    if (sourceKind != NeoTileOutputSourceKind.TileLayerLink ||
                        string.IsNullOrEmpty(sourceTileLayerLinkId))
                    {
                        continue;
                    }

                    AddTileLayerLinkDependency(
                        dependencies,
                        seen,
                        sourceTileLayerLinkId!,
                        region.layerId);
                }
            }

            if (Content.tileLayerLinks is null) return dependencies;

            foreach (var link in Content.tileLayerLinks)
            {
                if (link is null) continue;
                string? sourceId = ResolveTileLayerLinkSourceId(link);
                if (string.IsNullOrEmpty(sourceId) ||
                    string.IsNullOrEmpty(link.targetTileLayerId))
                {
                    continue;
                }

                AddTileLayerLinkDependency(
                    dependencies,
                    seen,
                    sourceId!,
                    link.targetTileLayerId);
            }
            return dependencies;
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

        protected readonly struct ResolvedTileGridRegion
        {
            public string RegionKey { get; }
            public int RegionX { get; }
            public int RegionY { get; }
            public JToken? Data { get; }

            public ResolvedTileGridRegion(
                string regionKey,
                int regionX,
                int regionY,
                JToken? data)
            {
                RegionKey = regionKey;
                RegionX = regionX;
                RegionY = regionY;
                Data = data;
            }
        }

        protected IEnumerable<ResolvedTileGridRegion> ResolveLayerRegions(
            string layerId,
            string layerKind)
        {
            var regionsByKey = new Dictionary<string, ResolvedTileGridRegion>();
            if (Content is not null)
            {
                foreach (var region in Content.regions)
                {
                    if (!RegionMatches(region, layerId, layerKind)) continue;
                    regionsByKey[region.regionKey] = ResolveRegionData(
                        layerId,
                        layerKind,
                        region.regionKey,
                        region.regionX,
                        region.regionY,
                        region.data);
                }
            }

            AddDeltaOnlyRegions(
                regionsByKey,
                client.GetTileGridRegionDeltas(
                    NeoValueOwnership.Save,
                    GridValueId,
                    layerId,
                    layerKind),
                layerId,
                layerKind);
            AddDeltaOnlyRegions(
                regionsByKey,
                client.GetTileGridRegionDeltas(
                    NeoValueOwnership.Session,
                    GridValueId,
                    layerId,
                    layerKind),
                layerId,
                layerKind);

            foreach (var pair in regionsByKey)
            {
                yield return pair.Value;
            }
        }

        private void AddDeltaOnlyRegions(
            Dictionary<string, ResolvedTileGridRegion> regionsByKey,
            IEnumerable<TileGridRegionDelta> deltas,
            string layerId,
            string layerKind)
        {
            foreach (var delta in deltas)
            {
                if (regionsByKey.ContainsKey(delta.regionKey)) continue;
                regionsByKey[delta.regionKey] = ResolveRegionData(
                    layerId,
                    layerKind,
                    delta.regionKey,
                    delta.regionX,
                    delta.regionY,
                    null);
            }
        }

        private ResolvedTileGridRegion ResolveRegionData(
            string layerId,
            string layerKind,
            string regionKey,
            int regionX,
            int regionY,
            JToken? authoredData)
        {
            JObject data = CloneRegionData(authoredData, layerKind, regionX, regionY);
            ApplyDelta(
                data,
                client.GetTileGridRegionDelta(
                    NeoValueOwnership.Save,
                    GridValueId,
                    layerId,
                    layerKind,
                    regionKey),
                authoredData);
            ApplyDelta(
                data,
                client.GetTileGridRegionDelta(
                    NeoValueOwnership.Session,
                    GridValueId,
                    layerId,
                    layerKind,
                    regionKey),
                authoredData);
            return new ResolvedTileGridRegion(regionKey, regionX, regionY, data);
        }

        private static JObject CloneRegionData(
            JToken? source,
            string layerKind,
            int regionX,
            int regionY)
        {
            if (source is JObject obj)
            {
                return (JObject)obj.DeepClone();
            }
            return new JObject
            {
                ["kind"] = layerKind,
                ["regionX"] = regionX,
                ["regionY"] = regionY,
                ["instances"] = new JArray(),
            };
        }

        private static void ApplyDelta(
            JObject data,
            TileGridRegionDelta? regionDelta,
            JToken? authoredData)
        {
            if (regionDelta?.delta is null) return;
            JArray instances = EnsureInstances(data);
            foreach (var id in regionDelta.delta.removedInstanceIds ?? EmptyStringList)
            {
                RemoveInstance(instances, id);
            }
            foreach (var pair in regionDelta.delta.entries ?? EmptyEntries)
            {
                RemoveInstance(instances, pair.Key);
                instances.Add(pair.Value.DeepClone());
            }
            foreach (var id in regionDelta.delta.restoredToAuthored ?? EmptyStringList)
            {
                RemoveInstance(instances, id);
                var authored = FindAuthoredInstance(authoredData, id);
                if (authored is not null)
                {
                    instances.Add(authored.DeepClone());
                }
            }
        }

        private static readonly IReadOnlyList<string> EmptyStringList =
            Array.Empty<string>();

        private static readonly IReadOnlyDictionary<string, JToken> EmptyEntries =
            new Dictionary<string, JToken>();

        private static JArray EnsureInstances(JObject data)
        {
            if (data["instances"] is JArray instances) return instances;
            instances = new JArray();
            data["instances"] = instances;
            return instances;
        }

        protected static JObject? FindAuthoredInstance(JToken? authoredData, string instanceId)
        {
            var instances = ReadInstances(authoredData);
            if (instances is null) return null;
            foreach (var rawInstance in instances)
            {
                if (rawInstance is not JObject instance) continue;
                if (ReadString(instance["id"]) == instanceId) return instance;
            }
            return null;
        }

        protected static bool RemoveInstance(JArray instances, string instanceId)
        {
            for (int index = instances.Count - 1; index >= 0; index -= 1)
            {
                if (instances[index] is not JObject instance) continue;
                if (ReadString(instance["id"]) != instanceId) continue;
                instances.RemoveAt(index);
                return true;
            }
            return false;
        }

        private static bool RegionMatches(
            TileGridRegion region,
            string layerId,
            string layerKind)
        {
            return !region.deleted &&
                region.layerId == layerId &&
                region.layerKind == layerKind &&
                region.data is not null;
        }

        protected static JArray? ReadInstances(JToken? data)
        {
            return data is JObject obj && obj["instances"] is JArray instances
                ? instances
                : null;
        }

        private static List<string> ReadConflictOrder(JToken? data)
        {
            var ids = new List<string>();
            if (data is not JObject obj || obj["conflictOrder"] is not JArray order)
            {
                return ids;
            }
            foreach (var token in order)
            {
                string? id = ReadString(token);
                if (!string.IsNullOrEmpty(id)) ids.Add(id!);
            }
            return ids;
        }

        private static void SortTileCandidates<TTile>(
            List<NeoTileCandidate<TTile>> candidates,
            List<string> conflictOrder)
            where TTile : class, INeoValueReference
        {
            var orderedIds = new Dictionary<string, int>();
            for (int index = 0; index < conflictOrder.Count; index += 1)
            {
                orderedIds[conflictOrder[index]] = index;
            }
            candidates.Sort((left, right) =>
            {
                bool leftOrdered = orderedIds.TryGetValue(left.InstanceId.Value, out int leftIndex);
                bool rightOrdered = orderedIds.TryGetValue(right.InstanceId.Value, out int rightIndex);
                if (leftOrdered && rightOrdered) return leftIndex.CompareTo(rightIndex);
                if (leftOrdered) return -1;
                if (rightOrdered) return 1;
                return left.Order.CompareTo(right.Order);
            });
        }

        private static void SortResolvedTiles(
            List<NeoResolvedTileInstance> candidates,
            List<string> conflictOrder)
        {
            var orderedIds = new Dictionary<string, int>();
            for (int index = 0; index < conflictOrder.Count; index += 1)
            {
                orderedIds[conflictOrder[index]] = index;
            }
            candidates.Sort((left, right) =>
            {
                bool leftOrdered = orderedIds.TryGetValue(left.InstanceId.Value, out int leftIndex);
                bool rightOrdered = orderedIds.TryGetValue(right.InstanceId.Value, out int rightIndex);
                if (leftOrdered && rightOrdered) return leftIndex.CompareTo(rightIndex);
                if (leftOrdered) return -1;
                if (rightOrdered) return 1;
                return left.Order.CompareTo(right.Order);
            });
        }

        protected static bool ObjectInstanceOccupiesCell(
            JObject instance,
            Vector2Int cell)
        {
            if (instance["footprint"] is JArray footprint && footprint.Count > 0)
            {
                foreach (var token in footprint)
                {
                    if (TryReadCell(token, out Vector2Int footprintCell) &&
                        footprintCell == cell)
                    {
                        return true;
                    }
                }
                return false;
            }
            return TryReadCell(instance["position"], out Vector2Int position) &&
                position == cell;
        }

        protected static IReadOnlyList<Vector2Int> ReadFootprint(
            JObject instance,
            Vector2Int fallbackCell)
        {
            var cells = new List<Vector2Int>();
            if (instance["footprint"] is JArray footprint && footprint.Count > 0)
            {
                foreach (var token in footprint)
                {
                    if (TryReadCell(token, out Vector2Int footprintCell))
                    {
                        cells.Add(footprintCell);
                    }
                }
            }
            if (cells.Count == 0) cells.Add(fallbackCell);
            return cells;
        }

        protected static bool TryReadCell(JToken? token, out Vector2Int cell)
        {
            cell = default;
            if (token is not JObject obj) return false;
            int? x = ReadInt(obj["x"]);
            int? y = ReadInt(obj["y"]);
            if (x is null || y is null) return false;
            cell = new Vector2Int(x.Value, y.Value);
            return true;
        }

        protected static string? ReadString(JToken? token)
        {
            return token?.Type == JTokenType.String ? token.Value<string>() : null;
        }

        protected static int? ReadInt(JToken? token)
        {
            if (token is null) return null;
            if (token.Type != JTokenType.Integer) return null;
            return token.Value<int>();
        }

        protected static Vector2Int ToVector2Int(TileGridCell? cell)
        {
            return cell is null
                ? default
                : new Vector2Int(cell.x, cell.y);
        }

        protected static string BuildTileLayerLinkInstanceId(
            TileGridLayerLinkPayload link,
            TileGridLayerLinkTile tile)
        {
            string objectInstanceId = string.IsNullOrEmpty(link.objectInstanceId)
                ? "object"
                : link.objectInstanceId;
            string linkId = string.IsNullOrEmpty(link.id)
                ? string.IsNullOrEmpty(link.tileLayerLinkValueId) ? "link" : link.tileLayerLinkValueId
                : link.id;
            string tileId = string.IsNullOrEmpty(tile.id) ? tile.tileValueId : tile.id;
            return $"{objectInstanceId}:{linkId}:{tileId}";
        }

        protected static string? ResolveTileLayerLinkSourceId(TileGridLayerLinkPayload link)
        {
            return string.IsNullOrEmpty(link.tileLayerLinkValueId)
                ? link.id
                : link.tileLayerLinkValueId;
        }

        protected static void ReadTileInstanceSource(
            JObject instance,
            string layerId,
            out NeoTileOutputSourceKind sourceKind,
            out string? sourceTileLayerLinkId)
        {
            string? linkedSourceId = ReadString(instance["sourceTileLayerLinkId"]);
            if (string.IsNullOrEmpty(linkedSourceId))
            {
                string? storedLayerLinkId = ReadString(instance["layerLinkId"]);
                linkedSourceId = storedLayerLinkId == layerId ? null : storedLayerLinkId;
            }

            if (string.IsNullOrEmpty(linkedSourceId))
            {
                sourceKind = NeoTileOutputSourceKind.Direct;
                sourceTileLayerLinkId = null;
                return;
            }

            sourceKind = NeoTileOutputSourceKind.TileLayerLink;
            sourceTileLayerLinkId = linkedSourceId;
        }

        private bool TileInstanceSourceContainsInstance(
            JObject instance,
            string layerId,
            string instanceId)
        {
            ReadTileInstanceSource(
                instance,
                layerId,
                out NeoTileOutputSourceKind sourceKind,
                out string? sourceTileLayerLinkId);
            if (sourceKind != NeoTileOutputSourceKind.TileLayerLink) return true;
            if (string.IsNullOrEmpty(sourceTileLayerLinkId)) return true;
            if (!client.TryGetValue(sourceTileLayerLinkId!, out ObjectAttributeValue? sourceLink)) return true;
            if (sourceLink.value is null) return true;
            if (!sourceLink.value.TryGetValue("Tiles", out string tilesValueId)) return true;
            if (!client.TryGetValue(tilesValueId, out ArrayAttributeValue? tiles)) return true;
            if (tiles.value is null) return false;
            return Array.IndexOf(tiles.value, instanceId) >= 0;
        }

        private bool TileLayerLinkSourceContainsTile(
            TileGridLayerLinkPayload link,
            TileGridLayerLinkTile tile)
        {
            string? sourceTileLayerLinkId = ResolveTileLayerLinkSourceId(link);
            if (string.IsNullOrEmpty(sourceTileLayerLinkId)) return true;
            if (!client.TryGetValue(sourceTileLayerLinkId!, out ObjectAttributeValue? sourceLink)) return true;
            if (sourceLink.value is null) return true;
            if (!sourceLink.value.TryGetValue("Tiles", out string tilesValueId)) return true;
            if (!client.TryGetValue(tilesValueId, out ArrayAttributeValue? tiles)) return true;
            if (tiles.value is null) return false;

            string tileId = string.IsNullOrEmpty(tile.id) ? tile.tileValueId : tile.id;
            if (Array.IndexOf(tiles.value, tileId) >= 0) return true;

            string instanceId = BuildTileLayerLinkInstanceId(link, tile);
            return Array.IndexOf(tiles.value, instanceId) >= 0;
        }

        protected static int CombineTileLayerLinkOrder(int sourceOrder, int tileOrder)
        {
            long combined = ((long)sourceOrder * 1000L) + tileOrder;
            if (combined > int.MaxValue) return int.MaxValue;
            if (combined < int.MinValue) return int.MinValue;
            return (int)combined;
        }

        protected void BuildRegionRef(
            Vector2Int cell,
            out string regionKey,
            out int regionX,
            out int regionY)
        {
            int regionSize = Content?.manifest?.regionSize ?? 32;
            if (regionSize <= 0) regionSize = 32;
            regionX = FloorDiv(cell.x, regionSize);
            regionY = FloorDiv(cell.y, regionSize);
            regionKey = $"{regionX},{regionY}";
        }

        private static int FloorDiv(int value, int divisor)
        {
            int result = value / divisor;
            int remainder = value % divisor;
            if (remainder != 0 && ((remainder < 0) != (divisor < 0)))
            {
                result -= 1;
            }
            return result;
        }
    }

    public sealed class NeoTileGridPrimitive : NeoReadOnlyTileGridPrimitive
    {
        private readonly NeoValueOwnership writeOwnership;

        private NeoTileGridPrimitive(
            NeoClient client,
            string gridValueId,
            TileGridContent? content,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.ReadOnlyCustomFactory>? readOnlyFactories = null,
            IReadOnlyDictionary<string, NeoGeneratedTypesSupport.WritableCustomFactory>? writableFactories = null,
            NeoValueOwnership writeOwnership = NeoValueOwnership.Save)
            : base(client, gridValueId, content, readOnlyFactories, writableFactories)
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
            client.tileGridContents.TryGetValue(gridValueId, out TileGridContent? content);
            return new NeoTileGridPrimitive(
                client,
                gridValueId,
                content,
                readOnlyFactories,
                writableFactories,
                writeOwnership);
        }

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

            BuildRegionRef(cell, out string regionKey, out int regionX, out int regionY);
            int order = 0;
            string instanceId = Guid.NewGuid().ToString();
            if (TryFindTileInstanceAt(layerId, cell, out var existing))
            {
                instanceId = existing.InstanceId;
                order = ReadInt(existing.Instance["order"]) ?? 0;
            }
            else
            {
                order = NextOrder(layerId, "tile", regionKey);
            }

            Lifecycle?.BeforeSetTile(new NeoTileSetContext(
                this,
                layerId,
                cell,
                instanceId,
                generatedTile!,
                tileValueId!,
                tileTypeId,
                existing.Instance));

            var entry = new JObject
            {
                ["id"] = instanceId,
                ["tileValueId"] = tileValueId,
                ["tileTypeId"] = StringOrNull(tileTypeId),
                ["position"] = CellToken(cell),
                ["layerLinkId"] = layerId,
                ["order"] = order,
            };
            WriteDeltaEntry(
                layerId,
                "tile",
                regionKey,
                regionX,
                regionY,
                instanceId,
                entry);
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
            if (!TryFindInstanceById("tile", instanceId.Value, out var located))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-instance-missing",
                    $"Tile instance '{instanceId.Value}' was not found in grid '{GridValueId}'.");
            }

            Vector2Int cell = TryReadCell(located.Instance["position"], out Vector2Int locatedCell)
                ? locatedCell
                : default;
            Lifecycle?.BeforeConvertTile(new NeoTileConvertContext(
                this,
                located.LayerId,
                cell,
                instanceId.Value,
                generatedTarget!,
                targetValueId!,
                targetTypeId,
                located.Instance));

            var entry = (JObject)located.Instance.DeepClone();
            entry["tileValueId"] = targetValueId;
            entry["tileTypeId"] = StringOrNull(targetTypeId);
            WriteDeltaEntry(
                located.LayerId,
                "tile",
                located.RegionKey,
                located.RegionX,
                located.RegionY,
                instanceId.Value,
                entry);
            NotifyTileLayerChanged(
                located.LayerId,
                Array.Empty<Vector2Int>(),
                new[] { cell },
                NeoTileGridChangeSourceKind.Direct,
                instanceId.Value);
            return NeoPlacementResult.Success();
        }

        public NeoPlacementResult TryResetTile(NeoTileInstanceId instanceId)
        {
            if (!TryFindInstanceById("tile", instanceId.Value, out var located))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-instance-missing",
                    $"Tile instance '{instanceId.Value}' was not found in grid '{GridValueId}'.");
            }
            Vector2Int cell = TryReadCell(located.Instance["position"], out Vector2Int locatedCell)
                ? locatedCell
                : default;
            Lifecycle?.BeforeResetTile(new NeoTileResetContext(
                this,
                located.LayerId,
                cell,
                instanceId.Value,
                located.Instance));
            ResetDelta(
                located.LayerId,
                "tile",
                located.RegionKey,
                located.RegionX,
                located.RegionY,
                instanceId.Value);
            NotifyTileLayerChanged(
                located.LayerId,
                Array.Empty<Vector2Int>(),
                new[] { cell },
                NeoTileGridChangeSourceKind.Direct,
                instanceId.Value);
            return NeoPlacementResult.Success();
        }

        public NeoPlacementResult TryRemoveTile(NeoTileInstanceId instanceId)
        {
            if (!TryFindInstanceById("tile", instanceId.Value, out var located) &&
                !TryFindTileLayerLinkInstanceById(instanceId.Value, out located))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-instance-missing",
                    $"Tile instance '{instanceId.Value}' was not found in grid '{GridValueId}'.");
            }
            Vector2Int cell = TryReadCell(located.Instance["position"], out Vector2Int locatedCell)
                ? locatedCell
                : default;
            Lifecycle?.BeforeRemoveTile(new NeoTileRemoveContext(
                this,
                located.LayerId,
                cell,
                instanceId.Value,
                located.Instance));
            RemoveInstanceDelta(
                located.LayerId,
                "tile",
                located.RegionKey,
                located.RegionX,
                located.RegionY,
                instanceId.Value);
            NotifyTileLayerChanged(
                located.LayerId,
                new[] { cell },
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
            if (TryFindObjectInstanceAt(layerId, cell, out var occupied))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-object-cell-occupied",
                    $"Object layer '{layerId}' already has object instance '{occupied.InstanceId}' at cell ({cell.x}, {cell.y}).");
            }

            BuildRegionRef(cell, out string regionKey, out int regionX, out int regionY);
            string instanceId = Guid.NewGuid().ToString();
            Lifecycle?.BeforeSpawnObject(new NeoObjectSpawnContext(
                this,
                layerId,
                cell,
                instanceId,
                generatedObject!,
                objectValueId!,
                objectTypeId));
            var entry = new JObject
            {
                ["id"] = instanceId,
                ["objectValueId"] = objectValueId,
                ["objectTypeId"] = StringOrNull(objectTypeId),
                ["position"] = CellToken(cell),
                ["objectLayerId"] = layerId,
                ["order"] = NextOrder(layerId, "object", regionKey),
            };
            WriteDeltaEntry(
                layerId,
                "object",
                regionKey,
                regionX,
                regionY,
                instanceId,
                entry);
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
            if (!TryFindInstanceById("object", instanceId.Value, out var located))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-instance-missing",
                    $"Object instance '{instanceId.Value}' was not found in grid '{GridValueId}'.");
            }
            Vector2Int cell = TryReadCell(located.Instance["position"], out Vector2Int locatedCell)
                ? locatedCell
                : default;
            Lifecycle?.BeforeDespawnObject(new NeoObjectDespawnContext(
                this,
                located.LayerId,
                cell,
                instanceId.Value,
                located.Instance));
            RemoveInstanceDelta(
                located.LayerId,
                "object",
                located.RegionKey,
                located.RegionX,
                located.RegionY,
                instanceId.Value);
            NotifyObjectLayerChanged(
                located.LayerId,
                new[] { instanceId },
                Array.Empty<NeoObjectInstanceId>(),
                ReadFootprint(located.Instance, cell),
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
            if (!TryFindInstanceById("object", instanceId.Value, out var located))
            {
                return NeoPlacementResult.Error(
                    "tile-grid-instance-missing",
                    $"Object instance '{instanceId.Value}' was not found in grid '{GridValueId}'.");
            }

            Vector2Int cell = TryReadCell(located.Instance["position"], out Vector2Int locatedCell)
                ? locatedCell
                : default;
            Lifecycle?.BeforeSwapObjectVariant(new NeoObjectVariantSwapContext(
                this,
                located.LayerId,
                cell,
                instanceId.Value,
                generatedVariant!,
                variantValueId!,
                variantTypeId,
                located.Instance));

            var entry = (JObject)located.Instance.DeepClone();
            entry["objectValueId"] = variantValueId;
            entry["objectTypeId"] = StringOrNull(variantTypeId);
            WriteDeltaEntry(
                located.LayerId,
                "object",
                located.RegionKey,
                located.RegionX,
                located.RegionY,
                instanceId.Value,
                entry);
            NotifyObjectLayerChanged(
                located.LayerId,
                Array.Empty<NeoObjectInstanceId>(),
                new[] { instanceId },
                ReadFootprint(located.Instance, cell),
                NeoTileGridChangeSourceKind.Direct,
                instanceId.Value);
            return NeoPlacementResult.Success();
        }

        private readonly struct LocatedInstance
        {
            public string LayerId { get; }
            public string LayerKind { get; }
            public string RegionKey { get; }
            public int RegionX { get; }
            public int RegionY { get; }
            public string InstanceId { get; }
            public JObject Instance { get; }

            public LocatedInstance(
                string layerId,
                string layerKind,
                string regionKey,
                int regionX,
                int regionY,
                string instanceId,
                JObject instance)
            {
                LayerId = layerId;
                LayerKind = layerKind;
                RegionKey = regionKey;
                RegionX = regionX;
                RegionY = regionY;
                InstanceId = instanceId;
                Instance = instance;
            }
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

        private bool TryFindTileInstanceAt(
            string layerId,
            Vector2Int cell,
            out LocatedInstance located)
        {
            located = default;
            foreach (var region in ResolveLayerRegions(layerId, "tile"))
            {
                var instances = ReadInstances(region.Data);
                if (instances is null) continue;
                foreach (var rawInstance in instances)
                {
                    if (rawInstance is not JObject instance) continue;
                    if (!TryReadCell(instance["position"], out Vector2Int position)) continue;
                    if (position != cell) continue;
                    string? id = ReadString(instance["id"]);
                    if (string.IsNullOrEmpty(id)) continue;
                    located = new LocatedInstance(
                        layerId,
                        "tile",
                        region.RegionKey,
                        region.RegionX,
                        region.RegionY,
                        id!,
                        instance);
                }
            }
            return located.Instance is not null;
        }

        private bool TryFindTileLayerLinkInstanceById(
            string instanceId,
            out LocatedInstance located)
        {
            located = default;
            if (Content?.tileLayerLinks is null) return false;
            foreach (var link in Content.tileLayerLinks)
            {
                if (link is null ||
                    string.IsNullOrEmpty(link.targetTileLayerId) ||
                    !TryResolveTileLayerLinkOrigin(link, out Vector2Int origin, out int sourceOrder))
                {
                    continue;
                }

                if (link.tiles is null) continue;
                foreach (var tile in link.tiles)
                {
                    if (tile is null || string.IsNullOrEmpty(tile.tileValueId)) continue;
                    string linkedInstanceId = BuildTileLayerLinkInstanceId(link, tile);
                    if (linkedInstanceId != instanceId) continue;

                    Vector2Int cell = origin + ToVector2Int(tile.position);
                    BuildRegionRef(cell, out string regionKey, out int regionX, out int regionY);
                    var instance = new JObject
                    {
                        ["id"] = linkedInstanceId,
                        ["tileValueId"] = tile.tileValueId,
                        ["tileTypeId"] = StringOrNull(tile.tileTypeId),
                        ["position"] = CellToken(cell),
                        ["layerLinkId"] = link.targetTileLayerId,
                        ["sourceTileLayerLinkId"] = StringOrNull(ResolveTileLayerLinkSourceId(link)),
                        ["order"] = CombineTileLayerLinkOrder(sourceOrder, tile.order),
                    };
                    located = new LocatedInstance(
                        link.targetTileLayerId,
                        "tile",
                        regionKey,
                        regionX,
                        regionY,
                        linkedInstanceId,
                        instance);
                    return true;
                }
            }
            return false;
        }

        private bool TryFindObjectInstanceAt(
            string layerId,
            Vector2Int cell,
            out LocatedInstance located)
        {
            located = default;
            foreach (var region in ResolveLayerRegions(layerId, "object"))
            {
                var instances = ReadInstances(region.Data);
                if (instances is null) continue;
                foreach (var rawInstance in instances)
                {
                    if (rawInstance is not JObject instance) continue;
                    if (!ObjectInstanceOccupiesCell(instance, cell)) continue;
                    string? id = ReadString(instance["id"]);
                    if (string.IsNullOrEmpty(id)) continue;
                    located = new LocatedInstance(
                        layerId,
                        "object",
                        region.RegionKey,
                        region.RegionX,
                        region.RegionY,
                        id!,
                        instance);
                    return true;
                }
            }
            return false;
        }

        private bool TryFindInstanceById(
            string layerKind,
            string instanceId,
            out LocatedInstance located)
        {
            located = default;
            foreach (var layerId in LayerIdsForKind(layerKind))
            {
                foreach (var region in ResolveLayerRegions(layerId, layerKind))
                {
                    var instances = ReadInstances(region.Data);
                    if (instances is null) continue;
                    foreach (var rawInstance in instances)
                    {
                        if (rawInstance is not JObject instance) continue;
                        if (ReadString(instance["id"]) != instanceId) continue;
                        located = new LocatedInstance(
                            layerId,
                            layerKind,
                            region.RegionKey,
                            region.RegionX,
                            region.RegionY,
                            instanceId,
                            instance);
                        return true;
                    }
                }
            }
            return false;
        }

        private IEnumerable<string> LayerIdsForKind(string layerKind)
        {
            var ids = new HashSet<string>();
            if (Content is not null)
            {
                foreach (var region in Content.regions)
                {
                    if (region.layerKind != layerKind) continue;
                    if (!ids.Add(region.layerId)) continue;
                    yield return region.layerId;
                }
            }
            foreach (var id in client.GetTileGridDeltaLayerIds(
                         NeoValueOwnership.Save,
                         GridValueId,
                         layerKind))
            {
                if (!ids.Add(id)) continue;
                yield return id;
            }
            foreach (var id in client.GetTileGridDeltaLayerIds(
                         NeoValueOwnership.Session,
                         GridValueId,
                         layerKind))
            {
                if (!ids.Add(id)) continue;
                yield return id;
            }
        }

        private int NextOrder(string layerId, string layerKind, string regionKey)
        {
            int max = -1;
            foreach (var region in ResolveLayerRegions(layerId, layerKind))
            {
                if (region.RegionKey != regionKey) continue;
                var instances = ReadInstances(region.Data);
                if (instances is null) continue;
                foreach (var rawInstance in instances)
                {
                    if (rawInstance is not JObject instance) continue;
                    int order = ReadInt(instance["order"]) ?? 0;
                    if (order > max) max = order;
                }
            }
            return max + 1;
        }

        private void WriteDeltaEntry(
            string layerId,
            string layerKind,
            string regionKey,
            int regionX,
            int regionY,
            string instanceId,
            JObject entry)
        {
            var delta = EnsureDelta(layerId, layerKind, regionKey, regionX, regionY);
            delta.delta.entries[instanceId] = entry;
            RemoveId(delta.delta.removedInstanceIds, instanceId);
            RemoveId(delta.delta.restoredToAuthored, instanceId);
            Touch(delta);
        }

        private void ResetDelta(
            string layerId,
            string layerKind,
            string regionKey,
            int regionX,
            int regionY,
            string instanceId)
        {
            var delta = EnsureDelta(layerId, layerKind, regionKey, regionX, regionY);
            delta.delta.entries.Remove(instanceId);
            RemoveId(delta.delta.removedInstanceIds, instanceId);
            RemoveId(delta.delta.restoredToAuthored, instanceId);
            Touch(delta);
        }

        private void RemoveInstanceDelta(
            string layerId,
            string layerKind,
            string regionKey,
            int regionX,
            int regionY,
            string instanceId)
        {
            var delta = EnsureDelta(layerId, layerKind, regionKey, regionX, regionY);
            delta.delta.entries.Remove(instanceId);
            RemoveId(delta.delta.restoredToAuthored, instanceId);
            if (!delta.delta.removedInstanceIds.Contains(instanceId))
            {
                delta.delta.removedInstanceIds.Add(instanceId);
            }
            Touch(delta);
        }

        private TileGridRegionDelta EnsureDelta(
            string layerId,
            string layerKind,
            string regionKey,
            int regionX,
            int regionY)
        {
            var delta = client.EnsureTileGridRegionDelta(
                writeOwnership,
                GridValueId,
                layerId,
                layerKind,
                regionKey,
                regionX,
                regionY);
            delta.delta ??= new TileGridRegionDeltaPayload();
            delta.delta.entries ??= new Dictionary<string, JToken>();
            delta.delta.removedInstanceIds ??= new List<string>();
            delta.delta.restoredToAuthored ??= new List<string>();
            return delta;
        }

        private void Touch(TileGridRegionDelta delta)
        {
            delta.contentHash = Guid.NewGuid().ToString("N");
            client.TouchTileGridDelta(writeOwnership, GridValueId);
        }

        private static JObject CellToken(Vector2Int cell)
        {
            return new JObject
            {
                ["x"] = cell.x,
                ["y"] = cell.y,
            };
        }

        private static JToken StringOrNull(string? value)
        {
            return value is null ? JValue.CreateNull() : new JValue(value);
        }

        private static void RemoveId(List<string> ids, string id)
        {
            for (int index = ids.Count - 1; index >= 0; index -= 1)
            {
                if (ids[index] != id) continue;
                ids.RemoveAt(index);
            }
        }
    }
}
