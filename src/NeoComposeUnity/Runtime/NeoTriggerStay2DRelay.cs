// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>Relays <c>OnTriggerStay2D</c> to the GameObject's NeoScript hook.</summary>
    [AddComponentMenu("")]
    internal sealed class NeoTriggerStay2DRelay : NeoLifecycleRelay
    {
        private void OnTriggerStay2D(Collider2D other) => Relay(other);
    }
}
