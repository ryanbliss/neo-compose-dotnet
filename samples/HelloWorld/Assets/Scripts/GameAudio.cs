// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using UnityEngine;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// One-shot sound effects for the HUD and dialogue flow. Clips are
    /// authored project files referenced by the <c>Assets.Audio</c> schema
    /// container and synced into Resources — the game never hard-codes an
    /// asset path. The ship's looping thrust lives in <see cref="SystemMapUI"/>
    /// (it follows the flight lifecycle); everything else funnels through here.
    /// </summary>
    public sealed class GameAudio : IDisposable
    {
        private GameObject root;
        private AudioSource oneShot;

        public void Play(AudioClip clip)
        {
            if (clip == null) return;
            if (root == null)
            {
                root = new GameObject("GameAudio");
                // Edit-mode tests drive the same gameplay flow; DontDestroyOnLoad
                // is a play-mode-only API.
                if (Application.isPlaying)
                {
                    UnityEngine.Object.DontDestroyOnLoad(root);
                }
                oneShot = root.AddComponent<AudioSource>();
                oneShot.playOnAwake = false;
            }
            oneShot.PlayOneShot(clip);
        }

        public void Dispose()
        {
            if (root == null) return;
            UnityEngine.Object.Destroy(root);
            root = null;
            oneShot = null;
        }
    }
}
