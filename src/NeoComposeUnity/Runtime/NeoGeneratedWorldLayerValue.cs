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
        IReadOnlyList<NeoGeneratedClassValue> GetTiles();
        NeoGeneratedClassValue? GetTile(Vector2Int cell);
        NeoGeneratedClassValue? ResolveTile(Vector2Int cell);
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
        IReadOnlyList<NeoGeneratedClassValue> GetObjects();
        NeoGeneratedClassValue? GetObject(NeoObjectInstanceId instanceId);
        NeoGeneratedClassValue? GetObject(Vector2Int cell);
        IReadOnlyList<NeoGeneratedClassValue> GetObjects(Vector2Int cell);
        NeoGeneratedClassValue? ResolveObject(NeoObjectInstanceId instanceId);
        NeoGeneratedClassValue? ResolveObject(Vector2Int cell);
        IReadOnlyList<NeoGeneratedClassValue> ResolveObjects(Vector2Int cell);
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
        public int? SortingOrder => NeoWorldLayerMembers.ReadSortingOrder(node);

        internal IReadOnlyList<NeoTileProjection> GetTileProjections() =>
            Binding.Primitive.GetTileProjections(LayerClassId);

        public IReadOnlyList<NeoGeneratedClassValue> GetTiles() => Binding.Primitive.GetTileValues(LayerClassId);
        public NeoGeneratedClassValue? GetTile(Vector2Int cell) => Binding.Primitive.GetTile<NeoGeneratedClassValue>(LayerClassId, cell, string.Empty);
        public NeoGeneratedClassValue? ResolveTile(Vector2Int cell) => GetTile(cell);

        internal NeoTileProjection? GetTileProjection(Vector2Int cell) =>
            Binding.Primitive.ResolveTileCached(LayerClassId, cell);


        public IDisposable OnChanged(Action<NeoTileLayerChangedArgs> handler) =>
            Binding.Primitive.OnTileLayerChanged(LayerClassId, handler);

        internal NeoTileLayerRenderSnapshot GetRenderSnapshot() =>
            Binding.Primitive.GetTileLayerRenderSnapshot(LayerClassId);

        protected NeoPlacementResult TrySetTileClass<TAsset>(Vector2Int cell)
            where TAsset : class =>
            WritablePrimitive().TrySetTile(
                LayerClassId,
                cell,
                Binding.Primitive.ResolveGeneratedClassId(typeof(TAsset)),
                Binding.ImportedClassIds);

        protected NeoPlacementResult TrySetTileClass<TAsset>(
            Vector2Int cell,
            NeoClassRef<TAsset> tile)
            where TAsset : class =>
            WritablePrimitive().TrySetTile(
                LayerClassId,
                cell,
                tile.ClassId,
                Binding.ImportedClassIds);

        protected NeoPlacementResult TryConvertTileClass<TAsset>(NeoTileInstanceId instanceId)
            where TAsset : class =>
            WritablePrimitive().TryConvertTileClass(
                instanceId,
                Binding.Primitive.ResolveGeneratedClassId(typeof(TAsset)),
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
        public int? SortingOrder => NeoWorldLayerMembers.ReadSortingOrder(node);

        internal IReadOnlyList<NeoObjectProjection> GetObjectProjections() => Binding.Primitive.GetObjectProjections(LayerClassId);
        internal NeoObjectProjection? GetObjectProjection(NeoObjectInstanceId id) => Binding.Primitive.ResolveObjectInstance(LayerClassId, id);
        internal NeoObjectProjection? GetObjectProjection(Vector2Int cell) => Binding.Primitive.ResolveObjectAtCellCached(LayerClassId, cell);
        internal IReadOnlyList<NeoObjectProjection> GetObjectProjections(Vector2Int cell) => Binding.Primitive.ResolveObjectsAtCellCached(LayerClassId, cell);

        public IReadOnlyList<NeoGeneratedClassValue> GetObjects() => Binding.Primitive.GetObjectValues(LayerClassId);
        public NeoGeneratedClassValue? GetObject(NeoObjectInstanceId id) => Binding.Primitive.GetObjectValue(LayerClassId, id);
        public NeoGeneratedClassValue? GetObject(Vector2Int cell) => Binding.Primitive.GetObject<NeoGeneratedClassValue>(LayerClassId, cell, string.Empty);
        public IReadOnlyList<NeoGeneratedClassValue> GetObjects(Vector2Int cell) => Binding.Primitive.GetObjectValues(LayerClassId, cell);
        public NeoGeneratedClassValue? ResolveObject(NeoObjectInstanceId id) => GetObject(id);
        public NeoGeneratedClassValue? ResolveObject(Vector2Int cell) => GetObject(cell);
        public IReadOnlyList<NeoGeneratedClassValue> ResolveObjects(Vector2Int cell) => GetObjects(cell);

        public IDisposable OnChanged(Action<NeoObjectLayerChangedArgs> handler) =>
            Binding.Primitive.OnObjectLayerChanged(LayerClassId, handler);

        protected NeoPlacementResult TrySpawnConstructedObject(
            Vector2Int cell,
            INeoValueReference obj)
        {
            if (obj is not NeoGeneratedClassValue generated)
            {
                throw new ArgumentException(
                    "Cannot spawn an object that is not a generated class value.", nameof(obj));
            }
            return WritablePrimitive().TrySpawn(
                LayerClassId, cell, generated, Binding.ImportedClassIds);
        }

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

    /// <summary>
    /// Authored world layer members read straight off the value node. A
    /// generated layer class lives in the consumer's namespace, so these bases
    /// cannot name its properties — but a schema key is stable contract, so
    /// the node read resolves the authored value where a property-name lookup
    /// silently resolved nothing.
    /// </summary>
    internal static class NeoWorldLayerMembers
    {
        internal const string SortingOrderSchemaKey = "SortingOrder";

        internal static int? ReadSortingOrder(NeoMemberClass node)
        {
            return node.TryGet<NeoMemberInt>(SortingOrderSchemaKey, out var member)
                ? NeoGeneratedTypesSupport.ReadInt(member)
                : null;
        }
    }

    internal static class NeoWorldLayerReflection
    {
        internal static string? ReadString(object target, string propertyName)
        {
            object? value = target.GetType().GetProperty(propertyName)?.GetValue(target);
            return value as string;
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
        internal static IReadOnlyList<NeoGeneratedClassValue> TileValues(IReadOnlyList<NeoTileProjection> tiles)
        {
            var values = new NeoGeneratedClassValue[tiles.Count];
            for (int i = 0; i < values.Length; i++) values[i] = tiles[i].Tile;
            return values;
        }
        internal static IReadOnlyList<NeoGeneratedClassValue> ObjectValues(IReadOnlyList<NeoObjectProjection> objects)
        {
            var values = new NeoGeneratedClassValue[objects.Count];
            for (int i = 0; i < values.Length; i++) values[i] = objects[i].Object;
            return values;
        }
        internal static NeoTileLayerRenderSnapshot GetRenderSnapshot(IReadOnlyNeoTileLayerRuntime layer) => layer switch
        {
            NeoGeneratedTileLayerValue generated => generated.GetRenderSnapshot(),
            ReadOnlyNeoTileLayerRuntime runtime => runtime.GetRenderSnapshot(),
            _ => throw new InvalidOperationException($"Tile layer '{layer.GetType().Name}' does not provide a render projection."),
        };
        internal static NeoTileProjection? GetTile(IReadOnlyNeoTileLayerRuntime layer, Vector2Int cell) => layer switch
        {
            NeoGeneratedTileLayerValue generated => generated.GetTileProjection(cell),
            ReadOnlyNeoTileLayerRuntime runtime => runtime.GetTileProjection(cell),
            _ => throw new InvalidOperationException($"Tile layer '{layer.GetType().Name}' does not provide a render projection."),
        };
        internal static IReadOnlyList<NeoObjectProjection> GetObjects(IReadOnlyNeoObjectLayerRuntime layer) => layer switch
        {
            NeoGeneratedObjectLayerValue generated => generated.GetObjectProjections(),
            ReadOnlyNeoObjectLayerRuntime runtime => runtime.GetObjectProjections(),
            _ => throw new InvalidOperationException($"Object layer '{layer.GetType().Name}' does not provide a render projection."),
        };
        internal static NeoObjectProjection? GetObject(IReadOnlyNeoObjectLayerRuntime layer, NeoObjectInstanceId id) => layer switch
        {
            NeoGeneratedObjectLayerValue generated => generated.GetObjectProjection(id),
            ReadOnlyNeoObjectLayerRuntime runtime => runtime.GetObjectProjection(id),
            _ => throw new InvalidOperationException($"Object layer '{layer.GetType().Name}' does not provide a render projection."),
        };
    }
}
