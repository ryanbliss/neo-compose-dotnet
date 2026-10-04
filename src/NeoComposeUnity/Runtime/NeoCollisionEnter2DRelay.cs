// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>Relays <c>OnCollisionEnter2D</c> to the GameObject's NeoScript hook.</summary>
    [AddComponentMenu("")]
    internal sealed class NeoCollisionEnter2DRelay : NeoLifecycleRelay
    {
        private void OnCollisionEnter2D(Collision2D collision) => Relay(collision);
    }
}
