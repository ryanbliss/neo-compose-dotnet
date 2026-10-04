// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Hears one Physics2D message on a hooked GameObject and queues it for
    /// the drain (P98 §3.5). Unity calls only the messages a component
    /// defines, so each message has its own sealed relay.
    /// </summary>
    [AddComponentMenu("")]
    internal abstract class NeoLifecycleRelay : MonoBehaviour
    {
        private NeoLifecycleEntry? entry;
        private NeoLifecycleHooks message;

        internal void Bind(NeoLifecycleEntry entry, NeoLifecycleHooks message)
        {
            this.entry = entry;
            this.message = message;
        }

        protected void Relay(Collider2D other) =>
            entry?.QueueMessage(message, other, default, default);

        // The normal comes from the first contact, and the velocity in cells per second.
        protected void Relay(Collision2D collision)
        {
            if (entry is null)
                return;
            Vector2 normal = collision.contactCount > 0 ? collision.GetContact(0).normal : default;
            entry.QueueMessage(message, collision.collider, normal, collision.relativeVelocity / entry.CellSize);
        }
    }
}
