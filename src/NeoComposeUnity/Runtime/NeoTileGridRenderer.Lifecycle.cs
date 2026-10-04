// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    // P98 Unity hooks. The renderer never calls NeoScript: it keeps an entry
    // per hooked GameObject, queues it at spawn, activity changes and
    // despawn, and runs the per-frame phases. The client's drain runs the rest.
    public sealed partial class NeoTileGridRenderer
    {
        // Created for the first hooked GameObject; null while nothing is hooked.
        private NeoLifecyclePhases? lifecyclePhases;

        private void FixedUpdate() => RunLifecyclePhase(NeoLifecyclePhases.FixedUpdate, Time.fixedDeltaTime);

        private void Update() => RunLifecyclePhase(NeoLifecyclePhases.Update, Time.deltaTime);

        private void OnApplicationPause(bool paused)
        {
            if (CanRunLifecyclePhase(NeoLifecyclePhases.ApplicationPause))
                lifecyclePhases!.Run(NeoLifecyclePhases.ApplicationPause, paused);
        }

        private void OnApplicationQuit()
        {
            if (CanRunLifecyclePhase(NeoLifecyclePhases.ApplicationQuit))
                lifecyclePhases!.Run(NeoLifecyclePhases.ApplicationQuit, null);
        }

        // Boxes the delta time once for every call in the phase, as the
        // double a NeoScript Float is.
        private void RunLifecyclePhase(int phase, double deltaTime)
        {
            if (CanRunLifecyclePhase(phase))
                lifecyclePhases!.Run(phase, deltaTime);
        }

        private bool CanRunLifecyclePhase(int phase) =>
            lifecyclePhases is { } phases && !phases.IsEmpty(phase) && phases.Client.UnityHooksActive;

        /// <summary>
        /// Holds getter changes for a renderer step while Unity hooks are
        /// live, so everything it queues drains once, when it releases (P98 §3.2).
        /// </summary>
        /// <returns>The held client to release, or null when nothing was held.</returns>
        private NeoClient? HoldLifecycle()
        {
            NeoClient? client = tileCacheClient;
            if (client is null || !client.UnityHooksActive || !Application.isPlaying)
                return null;
            client.HoldGetterChanges();
            return client;
        }

        /// <summary>Gives a spawned instance's hooked GameObjects entries and queues them, parents first.</summary>
        private void QueueSpawnedLifecycle(NeoObjectInstanceId instanceId)
        {
            if (tileCacheClient is not { UnityHooksActive: true } client || !Application.isPlaying)
                return;
            if (lifecyclePhases?.Client != client)
                lifecyclePhases = new NeoLifecyclePhases(this, client);
            foreach (var bucket in objectVisibilityByInstanceId[instanceId].Buckets)
            {
                if (bucket.Value is not NeoGeneratedClassValue value
                    || NeoLifecycleEntry.Create(lifecyclePhases, value, bucket.GameObject) is not { } entry)
                    continue;
                bucket.Lifecycle = entry;
                client.QueueUnityTransition(entry);
            }
            if (client.PhysicsHooksActive)
                IndexColliders(client, instanceId);
        }

        /// <summary>Queues every hooked GameObject of an instance whose visibility flipped.</summary>
        private void QueueLifecycleTransitions(ObjectVisibilityIndex visibility)
        {
            NeoClient? held = HoldLifecycle();
            if (held is null)
                return;
            try
            {
                foreach (var bucket in visibility.Buckets)
                    if (bucket.Lifecycle is { } entry)
                        held.QueueUnityTransition(entry);
            }
            finally
            {
                held.ReleaseGetterChanges();
            }
        }

        /// <summary>
        /// Queues an instance's hooked GameObjects as despawned, children
        /// first. While a class has a physics hook it then disables the
        /// instance's colliders, so the exits Unity raises still name it (P98 §3.3).
        /// </summary>
        private void DespawnLifecycle(
            ObjectVisibilityIndex? visibility,
            List<RenderedObjectSprite>? sprites,
            List<RenderedObjectCollider>? colliders)
        {
            if (visibility is null || tileCacheClient is not { UnityHooksActive: true } client)
                return;
            for (int i = visibility.Buckets.Count - 1; i >= 0; i--)
                visibility.Buckets[i].Lifecycle?.Despawn();
            if (!client.PhysicsHooksActive)
                return;
            if (colliders is not null)
                foreach (var binding in colliders)
                    if (binding.Collider != null)
                        binding.Collider.enabled = false;
            if (sprites is not null)
                foreach (var sprite in sprites)
                    if (sprite.BoundsCollider != null)
                        sprite.BoundsCollider.enabled = false;
        }

        private void IndexColliders(NeoClient client, NeoObjectInstanceId instanceId)
        {
            if (objectCollidersByInstanceId.TryGetValue(instanceId, out var colliders))
                foreach (var binding in colliders)
                    IndexCollider(client, binding);
            if (objectSpritesByInstanceId.TryGetValue(instanceId, out var sprites))
                foreach (var sprite in sprites)
                    if (sprite.BoundsCollider != null)
                        client.IndexCollider(sprite.BoundsCollider, sprite.Value);
        }

        private static void IndexCollider(NeoClient client, RenderedObjectCollider binding)
        {
            if (binding.Collider != null && binding.Source is INeoWorldObjectValue value)
                client.IndexCollider(binding.Collider, value);
        }

        private void UnindexColliders(List<RenderedObjectSprite>? sprites, List<RenderedObjectCollider>? colliders)
        {
            if (tileCacheClient is not { PhysicsHooksActive: true } client)
                return;
            if (colliders is not null)
                foreach (var binding in colliders)
                    client.UnindexCollider(binding.Collider);
            if (sprites is not null)
                foreach (var sprite in sprites)
                    client.UnindexCollider(sprite.BoundsCollider);
        }

        /// <summary>
        /// The reload pass (P98 §4.3): every rendered GameObject rereads its
        /// class's hooks. One that gains its first hook gets an entry marked
        /// with its current state, so nothing runs.
        /// </summary>
        internal void RefreshLifecycleHooks()
        {
            if (!Application.isPlaying || tileCacheClient is not { } client)
                return;
            bool hooked = client.UnityHooksActive;
            if (hooked && lifecyclePhases?.Client != client)
                lifecyclePhases = new NeoLifecyclePhases(this, client);
            foreach (var pair in objectVisibilityByInstanceId)
            {
                foreach (var bucket in pair.Value.Buckets)
                {
                    if (bucket.Lifecycle is { } entry)
                        entry.Refresh();
                    else if (hooked && bucket.Value is NeoGeneratedClassValue value)
                        bucket.Lifecycle = NeoLifecycleEntry.CreateCurrent(lifecyclePhases!, value, bucket.GameObject);
                }
                if (client.PhysicsHooksActive)
                    IndexColliders(client, pair.Key);
            }
        }
    }
}
