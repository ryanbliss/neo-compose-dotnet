// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Connection state of an optional realtime provider (see
    /// <c>specs/convex-realtime-sync.md</c>). Core code treats anything other
    /// than <see cref="Connected"/> as "use the REST/local path".
    /// </summary>
    public enum NeoRealtimeConnectionState
    {
        Disconnected,

        Connecting,

        Connected,

        /// <summary>
        /// The server rejected the credential or a subscription's scope. The
        /// provider does not retry on its own; an explicit reconnect (after
        /// e.g. a fresh sign-in) is required.
        /// </summary>
        Denied,
    }
}
