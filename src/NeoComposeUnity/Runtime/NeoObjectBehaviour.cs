// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// GameObject lifecycle hooks for generated world object values. The Unity
    /// codegen implements this on the generated <c>NeoObject</c> base class,
    /// delegating to <c>protected virtual OnObjectSpawned/OnObjectDespawned</c>
    /// methods that game code overrides in partial classes — the same extension
    /// pattern as <see cref="NeoGeneratedCustomValue.LazyInitialize"/>.
    /// </summary>
    public interface INeoObjectSpawnHooks
    {
        /// <summary>
        /// Called by <see cref="NeoTileGridRenderer"/> after an object
        /// instance's root GameObject is fully built (composition children,
        /// authored collider, sprite fallback) — safe to add components such
        /// as Rigidbody2D. Play mode only, and NOT once-per-instance: instance
        /// data changes despawn and respawn the GameObject, so this can run
        /// again with a fresh <see cref="NeoObjectBehaviour"/> for the same
        /// <see cref="NeoObjectBehaviour.InstanceId"/>.
        /// </summary>
        void OnObjectSpawned(NeoObjectBehaviour behaviour);

        /// <summary>
        /// Called before the renderer destroys the instance's root GameObject
        /// (instance removed/changed, re-render, or scene teardown). Play mode
        /// only; fires at most once per spawned GameObject.
        /// </summary>
        void OnObjectDespawned(NeoObjectBehaviour behaviour);
    }

    /// <summary>
    /// Bridge between a renderer-spawned object GameObject and its Neo data
    /// model. <see cref="NeoTileGridRenderer"/> attaches one to every object
    /// instance root it spawns, so Unity-side code (e.g. physics callbacks)
    /// can resolve the typed Neo object:
    /// <c>other.GetComponentInParent&lt;NeoObjectBehaviour&gt;()?.TryGetObject(out MyObject obj)</c>.
    /// Deliberately a sealed data holder — game logic belongs in generated-type
    /// partials via <see cref="INeoObjectSpawnHooks"/>, not in subclasses.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NeoObjectBehaviour : MonoBehaviour
    {
        private bool isInitialized;
        private bool didNotifyDespawn;

        /// <summary>The generated Neo value this GameObject renders.</summary>
        public NeoGeneratedCustomValue Object { get; private set; } = null!;

        /// <summary>
        /// The placed instance's stable identity. Survives despawn/respawn
        /// cycles (the GameObject and this behaviour do not), so key any
        /// external per-instance state on this.
        /// </summary>
        public NeoObjectInstanceId InstanceId { get; private set; }

        /// <summary>The object layer the instance belongs to.</summary>
        public ReadOnlyNeoObjectLayerRuntime Layer { get; private set; } = null!;

        /// <summary>The renderer that spawned this GameObject.</summary>
        public NeoTileGridRenderer Renderer { get; private set; } = null!;

        /// <summary>The instance's placement cell at spawn time.</summary>
        public Vector2Int Cell { get; private set; }

        /// <summary>Typed access to <see cref="Object"/>.</summary>
        public bool TryGetObject<TObject>(out TObject obj)
            where TObject : NeoGeneratedCustomValue
        {
            if (!isInitialized)
            {
                throw new InvalidOperationException(
                    "Cannot read NeoObjectBehaviour.TryGetObject before the renderer initializes the behaviour.");
            }

            obj = (Object as TObject)!;
            return obj != null;
        }

        internal void Initialize(
            NeoTileGridRenderer renderer,
            ReadOnlyNeoObjectLayerRuntime layer,
            NeoResolvedObjectInstance instance)
        {
            if (isInitialized)
            {
                throw new InvalidOperationException(
                    $"NeoObjectBehaviour for instance '{InstanceId.Value}' is already initialized.");
            }

            Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            Layer = layer ?? throw new ArgumentNullException(nameof(layer));
            if (instance is null) throw new ArgumentNullException(nameof(instance));
            Object = instance.Object;
            InstanceId = instance.InstanceId;
            Cell = instance.Cell;
            isInitialized = true;
            InvokeHook(static (hooks, behaviour) => hooks.OnObjectSpawned(behaviour));
        }

        /// <summary>
        /// Fires the despawn hook exactly once. The renderer calls this before
        /// tearing a rendered object down; <see cref="OnDestroy"/> is the
        /// safety net for destruction paths that bypass the renderer (scene
        /// unload, parent teardown).
        /// </summary>
        internal void NotifyDespawned()
        {
            if (!isInitialized || didNotifyDespawn) return;
            didNotifyDespawn = true;
            InvokeHook(static (hooks, behaviour) => hooks.OnObjectDespawned(behaviour));
        }

        private void OnDestroy()
        {
            NotifyDespawned();
        }

        private void InvokeHook(Action<INeoObjectSpawnHooks, NeoObjectBehaviour> invoke)
        {
            // Hooks are gameplay extension points; never run authored gameplay
            // code from edit-mode authoring syncs.
            if (!Application.isPlaying) return;
            if (Object is not INeoObjectSpawnHooks hooks) return;
            try
            {
                invoke(hooks, this);
            }
            catch (Exception exception)
            {
                // A throwing user hook must not abort the render pass that is
                // spawning/despawning sibling instances.
                Debug.LogException(exception, this);
            }
        }
    }
}
