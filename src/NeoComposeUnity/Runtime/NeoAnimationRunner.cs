// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    internal sealed class NeoAnimationCoordinator : IDisposable
    {
        private readonly Dictionary<string, INeoAnimationPlayer> playersByInstance = new();
        private readonly HashSet<INeoAnimationPlayer> activePlayers = new();
        private readonly List<INeoAnimationPlayer> playerSnapshot = new();
        private NeoAnimationRunner? runner;
        private bool isDisposed;

        internal void Activate(INeoAnimationPlayer player)
        {
            if (isDisposed) throw new ObjectDisposedException(nameof(NeoAnimationCoordinator));
            if (playersByInstance.TryGetValue(
                    player.InstanceIdentity,
                    out INeoAnimationPlayer existing)
                && !ReferenceEquals(existing, player))
            {
                existing.StopFromCoordinator();
            }
            playersByInstance[player.InstanceIdentity] = player;
            activePlayers.Add(player);
            EnsureRunner();
        }

        internal void Deactivate(INeoAnimationPlayer player)
        {
            activePlayers.Remove(player);
            if (playersByInstance.TryGetValue(
                    player.InstanceIdentity,
                    out INeoAnimationPlayer existing)
                && ReferenceEquals(existing, player))
            {
                playersByInstance.Remove(player.InstanceIdentity);
            }
        }

        internal void Tick(float scaledDeltaTime)
        {
            if (isDisposed) return;
            playerSnapshot.Clear();
            playerSnapshot.AddRange(activePlayers);
            foreach (INeoAnimationPlayer player in playerSnapshot)
            {
                if (!player.IsPlaying)
                {
                    Deactivate(player);
                    continue;
                }
                try
                {
                    player.Tick(scaledDeltaTime);
                }
                catch (Exception exception)
                {
                    player.StopFromCoordinator();
                    Debug.LogException(exception);
                }
            }
        }

        internal void StopInstance(string instanceIdentity)
        {
            if (playersByInstance.TryGetValue(
                    instanceIdentity,
                    out INeoAnimationPlayer player))
            {
                player.StopFromCoordinator();
            }
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            playerSnapshot.Clear();
            playerSnapshot.AddRange(activePlayers);
            foreach (INeoAnimationPlayer player in playerSnapshot)
            {
                player.StopFromCoordinator();
            }
            playerSnapshot.Clear();
            activePlayers.Clear();
            playersByInstance.Clear();
            if (runner != null)
            {
                GameObject gameObject = runner.gameObject;
                runner = null;
                if (Application.isPlaying) UnityEngine.Object.Destroy(gameObject);
                else UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private void EnsureRunner()
        {
            if (!Application.isPlaying || runner != null) return;
            var gameObject = new GameObject("[Neo Compose Animation Runner]")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            runner = gameObject.AddComponent<NeoAnimationRunner>();
            runner.Initialize(this);
        }
    }

    [DisallowMultipleComponent]
    internal sealed class NeoAnimationRunner : MonoBehaviour
    {
        private NeoAnimationCoordinator? coordinator;

        internal void Initialize(NeoAnimationCoordinator owner)
        {
            coordinator = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        private void Update()
        {
            coordinator?.Tick(Time.deltaTime);
        }
    }
}
