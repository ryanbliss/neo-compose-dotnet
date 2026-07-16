// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public interface IReadOnlyNeoTileLayerRuntime : INeoValueReference
    {
        string LayerId { get; }
        string LayerClassId { get; }
        string? LayerOverrideValueId { get; }
        string DisplayName { get; }
        string ExpectedClassId { get; }
        string? SortingLayerName { get; }
        int? SortingOrder { get; }
        IReadOnlyList<NeoResolvedTileInstance> GetTiles();
        NeoResolvedTileInstance? GetTile(Vector2Int cell);
        NeoResolvedTileInstance? ResolveTile(Vector2Int cell);
        IDisposable OnChanged(Action<NeoTileLayerChangedArgs> handler);
    }

    public interface IReadOnlyNeoObjectLayerRuntime : INeoValueReference
    {
        string LayerId { get; }
        string LayerClassId { get; }
        string? LayerOverrideValueId { get; }
        string DisplayName { get; }
        string ExpectedClassId { get; }
        string? SortingLayerName { get; }
        int? SortingOrder { get; }
        IReadOnlyList<NeoResolvedObjectInstance> GetObjects();
        NeoResolvedObjectInstance? GetObject(NeoObjectInstanceId instanceId);
        NeoResolvedObjectInstance? GetObject(Vector2Int cell);
        IReadOnlyList<NeoResolvedObjectInstance> GetObjects(Vector2Int cell);
        NeoResolvedObjectInstance? ResolveObject(NeoObjectInstanceId instanceId);
        NeoResolvedObjectInstance? ResolveObject(Vector2Int cell);
        IReadOnlyList<NeoResolvedObjectInstance> ResolveObjects(Vector2Int cell);
        IDisposable OnChanged(Action<NeoObjectLayerChangedArgs> handler);
    }

    // These are deliberately interfaces. An authored layer already has one
    // C# base class (its generated Neo class value), so a second runtime base
    // class would recreate the per-grid wrapper problem this model removes.
    public interface NeoTileLayerRuntime : IReadOnlyNeoTileLayerRuntime { }
    public interface NeoObjectLayerRuntime : IReadOnlyNeoObjectLayerRuntime { }

    internal sealed class NeoTileLayerBinding
    {
        internal NeoTileLayerBinding(
            NeoReadOnlyTileGridPrimitive primitive,
            string layerClassId,
            string? layerOverrideValueId,
            IReadOnlyCollection<string> importedClassIds)
        {
            Primitive = primitive;
            LayerClassId = layerClassId;
            LayerOverrideValueId = layerOverrideValueId;
            ImportedClassIds = new HashSet<string>(importedClassIds, StringComparer.Ordinal);
        }

        internal NeoReadOnlyTileGridPrimitive Primitive { get; }
        internal string LayerClassId { get; }
        internal string? LayerOverrideValueId { get; }
        internal HashSet<string> ImportedClassIds { get; }
    }

    internal sealed class NeoObjectLayerBinding
    {
        internal NeoObjectLayerBinding(
            NeoReadOnlyTileGridPrimitive primitive,
            string layerClassId,
            string? layerOverrideValueId,
            IReadOnlyCollection<string> importedClassIds)
        {
            Primitive = primitive;
            LayerClassId = layerClassId;
            LayerOverrideValueId = layerOverrideValueId;
            ImportedClassIds = new HashSet<string>(importedClassIds, StringComparer.Ordinal);
        }

        internal NeoReadOnlyTileGridPrimitive Primitive { get; }
        internal string LayerClassId { get; }
        internal string? LayerOverrideValueId { get; }
        internal HashSet<string> ImportedClassIds { get; }
    }

    public abstract class NeoGeneratedTileLayerValue
        : NeoGeneratedClassValue, NeoTileLayerRuntime
    {
        private NeoTileLayerBinding? binding;

        protected NeoGeneratedTileLayerValue(
            NeoClient client,
            NeoMemberClass node,
            string fallbackClassId,
            bool isReadOnly = true,
            NeoValueOwnership inheritedStorageOwnership = NeoValueOwnership.Asset)
            : base(client, node, fallbackClassId, isReadOnly, inheritedStorageOwnership)
        {
        }

        internal void BindGridLayer(NeoTileLayerBinding next)
        {
            if (binding is not null
                && (!ReferenceEquals(binding.Primitive, next.Primitive)
                    || binding.LayerClassId != next.LayerClassId))
            {
                throw new InvalidOperationException(
                    $"Layer value '{GetType().Name}' is already bound to another grid instance.");
            }
            binding = next;
        }

        private NeoTileLayerBinding Binding => binding ?? throw new InvalidOperationException(
            $"Authored tile layer '{GetType().Name}' is not bound to a grid instance.");

        public string LayerId => Binding.LayerClassId;
        public string LayerClassId => Binding.LayerClassId;
        public string? LayerOverrideValueId => Binding.LayerOverrideValueId;
        public string DisplayName => NeoWorldLayerReflection.ReadString(this, "Name")
            ?? GetType().Name;
        public string ExpectedClassId => string.Empty;
        public string? SortingLayerName =>
            NeoWorldLayerReflection.ReadSortingLayerName(this);
        public int? SortingOrder => NeoWorldLayerReflection.ReadInt(this, "Order");

        public IReadOnlyList<NeoResolvedTileInstance> GetTiles() =>
            Binding.Primitive.GetTiles(LayerClassId);

        public NeoResolvedTileInstance? GetTile(Vector2Int cell) =>
            Binding.Primitive.ResolveTileCached(LayerClassId, cell);

        public NeoResolvedTileInstance? ResolveTile(Vector2Int cell) => GetTile(cell);

        public IDisposable OnChanged(Action<NeoTileLayerChangedArgs> handler) =>
            Binding.Primitive.OnTileLayerChanged(LayerClassId, handler);

        internal NeoTileLayerRenderSnapshot GetRenderSnapshot() =>
            Binding.Primitive.GetTileLayerRenderSnapshot(LayerClassId);

        protected NeoPlacementResult TrySetTileClass<TAsset>(Vector2Int cell)
            where TAsset : class =>
            WritablePrimitive().TrySetTileClass(
                LayerClassId,
                cell,
                Binding.Primitive.ResolveGeneratedClassId(typeof(TAsset)),
                Binding.ImportedClassIds);

        protected NeoPlacementResult TrySetTileClass<TAsset>(
            Vector2Int cell,
            NeoClassRef<TAsset> tile)
            where TAsset : class =>
            WritablePrimitive().TrySetTileClass(
                LayerClassId,
                cell,
                tile.ClassId,
                Binding.ImportedClassIds);

        protected NeoPlacementResult TrySetTileValue(
            Vector2Int cell,
            INeoValueReference tile) =>
            WritablePrimitive().TrySetTile(
                LayerClassId,
                cell,
                tile,
                string.Empty,
                Binding.ImportedClassIds);

        protected NeoPlacementResult TryConvertTileClass<TAsset>(NeoTileInstanceId instanceId)
            where TAsset : class =>
            WritablePrimitive().TryConvertTileClass(
                instanceId,
                Binding.Primitive.ResolveGeneratedClassId(typeof(TAsset)),
                Binding.ImportedClassIds);

        protected NeoPlacementResult TryConvertTileValue(
            NeoTileInstanceId instanceId,
            INeoValueReference target) =>
            WritablePrimitive().TryConvertTile(
                instanceId,
                target,
                string.Empty,
                Binding.ImportedClassIds);

        protected NeoPlacementResult TryResetBoundTile(NeoTileInstanceId instanceId) =>
            WritablePrimitive().TryResetTile(instanceId);

        protected NeoPlacementResult TryRemoveBoundTile(NeoTileInstanceId instanceId) =>
            WritablePrimitive().TryRemoveTile(instanceId);

        private NeoTileGridPrimitive WritablePrimitive()
        {
            if (IsReadOnly || Binding.Primitive is not NeoTileGridPrimitive writable)
            {
                throw new InvalidOperationException(
                    $"Tile layer '{GetType().Name}' is read-only.");
            }
            return writable;
        }
    }

    public abstract class NeoGeneratedObjectLayerValue
        : NeoGeneratedClassValue, NeoObjectLayerRuntime
    {
        private NeoObjectLayerBinding? binding;

        protected NeoGeneratedObjectLayerValue(
            NeoClient client,
            NeoMemberClass node,
            string fallbackClassId,
            bool isReadOnly = true,
            NeoValueOwnership inheritedStorageOwnership = NeoValueOwnership.Asset)
            : base(client, node, fallbackClassId, isReadOnly, inheritedStorageOwnership)
        {
        }

        internal void BindGridLayer(NeoObjectLayerBinding next)
        {
            if (binding is not null
                && (!ReferenceEquals(binding.Primitive, next.Primitive)
                    || binding.LayerClassId != next.LayerClassId))
            {
                throw new InvalidOperationException(
                    $"Layer value '{GetType().Name}' is already bound to another grid instance.");
            }
            binding = next;
        }

        private NeoObjectLayerBinding Binding => binding ?? throw new InvalidOperationException(
            $"Authored object layer '{GetType().Name}' is not bound to a grid instance.");

        public string LayerId => Binding.LayerClassId;
        public string LayerClassId => Binding.LayerClassId;
        public string? LayerOverrideValueId => Binding.LayerOverrideValueId;
        public string DisplayName => NeoWorldLayerReflection.ReadString(this, "Name")
            ?? GetType().Name;
        public string ExpectedClassId => string.Empty;
        public string? SortingLayerName =>
            NeoWorldLayerReflection.ReadSortingLayerName(this);
        public int? SortingOrder => NeoWorldLayerReflection.ReadInt(this, "Order");

        public IReadOnlyList<NeoResolvedObjectInstance> GetObjects() =>
            Binding.Primitive.GetObjects(LayerClassId);

        public NeoResolvedObjectInstance? GetObject(NeoObjectInstanceId instanceId) =>
            Binding.Primitive.ResolveObjectInstance(LayerClassId, instanceId);

        public NeoResolvedObjectInstance? GetObject(Vector2Int cell) =>
            Binding.Primitive.ResolveObjectAtCellCached(LayerClassId, cell);

        public IReadOnlyList<NeoResolvedObjectInstance> GetObjects(Vector2Int cell) =>
            Binding.Primitive.ResolveObjectsAtCellCached(LayerClassId, cell);

        public NeoResolvedObjectInstance? ResolveObject(NeoObjectInstanceId instanceId) =>
            GetObject(instanceId);

        public NeoResolvedObjectInstance? ResolveObject(Vector2Int cell) => GetObject(cell);

        public IReadOnlyList<NeoResolvedObjectInstance> ResolveObjects(Vector2Int cell) =>
            GetObjects(cell);

        public IDisposable OnChanged(Action<NeoObjectLayerChangedArgs> handler) =>
            Binding.Primitive.OnObjectLayerChanged(LayerClassId, handler);

        protected NeoPlacementResult TrySpawnClass<TAsset>(Vector2Int cell)
            where TAsset : class =>
            WritablePrimitive().TrySpawnObjectClass(
                LayerClassId,
                cell,
                Binding.Primitive.ResolveGeneratedClassId(typeof(TAsset)),
                Binding.ImportedClassIds);

        protected NeoPlacementResult TrySpawnClass<TAsset>(
            Vector2Int cell,
            NeoClassRef<TAsset> obj)
            where TAsset : class =>
            WritablePrimitive().TrySpawnObjectClass(
                LayerClassId,
                cell,
                obj.ClassId,
                Binding.ImportedClassIds);

        protected NeoPlacementResult TrySpawnValue(
            Vector2Int cell,
            INeoValueReference obj) =>
            WritablePrimitive().TrySpawnObject(
                LayerClassId,
                cell,
                obj,
                string.Empty,
                Binding.ImportedClassIds);

        protected NeoPlacementResult TrySwapVariantClass<TAsset>(NeoObjectInstanceId instanceId)
            where TAsset : class =>
            WritablePrimitive().TrySwapVariantClass(
                instanceId,
                Binding.Primitive.ResolveGeneratedClassId(typeof(TAsset)),
                Binding.ImportedClassIds);

        protected NeoPlacementResult TrySwapVariantValue(
            NeoObjectInstanceId instanceId,
            INeoValueReference variant) =>
            WritablePrimitive().TrySwapVariant(
                instanceId,
                variant,
                string.Empty,
                Binding.ImportedClassIds);

        protected NeoPlacementResult TryDespawnBoundObject(NeoObjectInstanceId instanceId) =>
            WritablePrimitive().TryDespawnObject(instanceId);

        private NeoTileGridPrimitive WritablePrimitive()
        {
            if (IsReadOnly || Binding.Primitive is not NeoTileGridPrimitive writable)
            {
                throw new InvalidOperationException(
                    $"Object layer '{GetType().Name}' is read-only.");
            }
            return writable;
        }
    }

    internal static class NeoWorldLayerReflection
    {
        internal static string? ReadString(object target, string propertyName)
        {
            object? value = target.GetType().GetProperty(propertyName)?.GetValue(target);
            return value as string;
        }

        internal static int? ReadInt(object target, string propertyName)
        {
            object? value = target.GetType().GetProperty(propertyName)?.GetValue(target);
            return value switch
            {
                int integer => integer,
                long integer => checked((int)integer),
                float number => (int)number,
                double number => (int)number,
                _ => null,
            };
        }

        internal static string? ReadSortingLayerName(object target)
        {
            object? value = target.GetType().GetProperty("SortingLayer")?.GetValue(target);
            if (value is null) return null;
            if (value is string name) return name;
            return value.GetType().GetProperty("Name")?.GetValue(value) as string;
        }
    }

    internal static class NeoWorldLayerRuntimeSupport
    {
        internal static NeoTileLayerRenderSnapshot GetRenderSnapshot(
            IReadOnlyNeoTileLayerRuntime layer)
        {
            return layer switch
            {
                NeoGeneratedTileLayerValue generated => generated.GetRenderSnapshot(),
                ReadOnlyNeoTileLayerRuntime legacy => legacy.GetRenderSnapshot(),
                _ => new NeoTileLayerRenderSnapshot(
                    layer.GetTiles(),
                    new Dictionary<string, IReadOnlyList<NeoResolvedTileInstance>>(),
                    new Dictionary<Vector2Int, int>()),
            };
        }
    }
}
