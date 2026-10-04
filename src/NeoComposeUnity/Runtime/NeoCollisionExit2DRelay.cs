// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>Relays <c>OnCollisionExit2D</c> to the GameObject's NeoScript hook.</summary>
    [AddComponentMenu("")]
    internal sealed class NeoCollisionExit2DRelay : NeoLifecycleRelay
    {
        private void OnCollisionExit2D(Collision2D collision) => Relay(collision);
    }
}
