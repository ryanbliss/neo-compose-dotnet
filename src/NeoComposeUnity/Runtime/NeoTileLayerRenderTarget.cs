// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Optional render-target hooks implemented by generated tile-layer base
    /// classes. Game code overrides the corresponding protected virtual methods
    /// in generated-type partial classes.
    /// </summary>
    public interface INeoTileLayerRenderTargetProvider
    {
        NeoTileLayerRenderTarget? CreateRenderTarget(NeoTileLayerCreateContext context);

        void OnRenderTargetCreated(NeoTileLayerRenderTargetContext context);

        void OnInitiallyRendered(NeoTileLayerRenderTargetContext context);

        void OnRenderTargetChanged(NeoTileLayerRenderTargetChangedContext context);

        void OnRenderTargetDestroying(NeoTileLayerRenderTargetDestroyContext context);

        void OnRenderTargetDestroyed(NeoTileLayerRenderTargetDestroyedContext context);
    }

    public sealed class NeoTileLayerCreateContext
    {
        public NeoTileLayerCreateContext(
            NeoTileGridRenderer renderer,
            IReadOnlyNeoTileLayerRuntime layer,
            INeoTileGridContent? content,
            Transform parent,
            int effectiveSortingOrder)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            Content = content;
            Parent = parent ?? throw new ArgumentNullException(nameof(parent));
            EffectiveSortingOrder = effectiveSortingOrder;
        }

        public NeoTileGridRenderer Renderer { get; }
        public IReadOnlyNeoTileLayerRuntime Layer { get; }
        public INeoTileGridContent? Content { get; }
        public Transform Parent { get; }
        public int EffectiveSortingOrder { get; }
    }

    public sealed class NeoTileLayerRenderTarget
    {
        public NeoTileLayerRenderTarget(GameObject root, Tilemap tilemap)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            Tilemap = tilemap ?? throw new ArgumentNullException(nameof(tilemap));
            Id = Guid.NewGuid().ToString("N");
        }

        public string Id { get; }
        public GameObject Root { get; }
        public Tilemap Tilemap { get; }
    }

    public class NeoTileLayerRenderTargetContext
    {
        public NeoTileLayerRenderTargetContext(
            NeoTileGridRenderer renderer,
            IReadOnlyNeoTileLayerRuntime layer,
            INeoTileGridContent? content,
            NeoTileLayerRenderTarget target)
        {
            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            Content = content;
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        public NeoTileGridRenderer Renderer { get; }
        public IReadOnlyNeoTileLayerRuntime Layer { get; }
        public INeoTileGridContent? Content { get; }
        public NeoTileLayerRenderTarget Target { get; }
    }

    public sealed class NeoTileLayerRenderTargetChangedContext
        : NeoTileLayerRenderTargetContext
    {
        public NeoTileLayerRenderTargetChangedContext(
            NeoTileGridRenderer renderer,
            IReadOnlyNeoTileLayerRuntime layer,
            INeoTileGridContent? content,
            NeoTileLayerRenderTarget target,
            NeoTileLayerChangedArgs change)
            : base(renderer, layer, content, target)
        {
            Change = change ?? throw new ArgumentNullException(nameof(change));
        }

        public NeoTileLayerChangedArgs Change { get; }
    }

    public enum NeoTileLayerRenderTargetDestroyReason
    {
        Replaced,
        RendererCleared,
        RenderCancelled,
        RendererDestroyed,
        ExternallyDestroyed,
    }

    public sealed class NeoTileLayerRenderTargetDestroyContext
        : NeoTileLayerRenderTargetContext
    {
        public NeoTileLayerRenderTargetDestroyContext(
            NeoTileGridRenderer renderer,
            IReadOnlyNeoTileLayerRuntime layer,
            INeoTileGridContent? content,
            NeoTileLayerRenderTarget target,
            NeoTileLayerRenderTargetDestroyReason reason)
            : base(renderer, layer, content, target)
        {
            Reason = reason;
        }

        public NeoTileLayerRenderTargetDestroyReason Reason { get; }
    }

    public sealed class NeoTileLayerRenderTargetDestroyedContext
        : NeoTileLayerRenderTargetContext
    {
        public NeoTileLayerRenderTargetDestroyedContext(
            NeoTileGridRenderer renderer,
            IReadOnlyNeoTileLayerRuntime layer,
            INeoTileGridContent? content,
            NeoTileLayerRenderTarget target,
            NeoTileLayerRenderTargetDestroyReason reason)
            : base(renderer, layer, content, target)
        {
            Reason = reason;
        }

        public NeoTileLayerRenderTargetDestroyReason Reason { get; }
    }
}
