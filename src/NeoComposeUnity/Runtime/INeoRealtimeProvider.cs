// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Optional realtime transport plugged into the project store (see
    /// <c>specs/convex-realtime-sync.md</c>). Implementations are explicitly
    /// registered — core never discovers one on its own — and everything is
    /// expressed in core DTOs so core stays transport-agnostic. All callbacks
    /// are invoked on the Unity main thread.
    /// </summary>
    /// <remarks>
    /// Everything degrades: when the provider is absent, disconnected, or
    /// denied, the store and synchronizers behave exactly as the REST/local
    /// build does. <see cref="NeoRealtimeConnectionState.Denied"/> is terminal
    /// until an explicit <see cref="ConnectAsync"/> after e.g. a fresh sign-in.
    /// </remarks>
    public interface INeoRealtimeProvider : IDisposable
    {
        NeoRealtimeConnectionState State { get; }

        event Action<NeoRealtimeConnectionState>? OnConnectionStateChanged;

        Awaitable ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sign-out teardown: best-effort remote auth clear, then an
        /// unconditional local teardown. Call before clearing the token store.
        /// </summary>
        Awaitable DisconnectAsync();

        /// <summary>
        /// Live save list for the configured project + channel. Returns an
        /// inert subscription while not <see cref="NeoRealtimeConnectionState.Connected"/>.
        /// </summary>
        IDisposable SubscribeSaveList(
            string? targetReleaseChannelId, Action<NeoSaveFileList> onChanged);

        /// <summary>
        /// Payload-free live head signal for one save. Record descriptors and
        /// states are fetched explicitly through the paginated read API.
        /// </summary>
        IDisposable SubscribeSaveRevision(
            string customId,
            Action<GameSaveSnapshotRevisionSignal> onChanged);

        /// <summary>
        /// True when the provider is connected and can commit. The synchronizer
        /// checks per commit; a disconnect between the check and the call falls
        /// back to REST.
        /// </summary>
        bool CanCommit { get; }

        /// <summary>
        /// Commit through the realtime transport. Same contract as
        /// <see cref="INeoApiClient.CommitAsync"/>, including the typed
        /// conflict result.
        /// </summary>
        Awaitable<NeoCommitResult> CommitAsync(NeoSaveCommitRequest request, bool replaceSnapshot);

        /// <summary>
        /// First flush of a live session: forks the save's head into a new
        /// live snapshot with the patch applied (see
        /// <c>specs/live-save-sessions.md</c>). Same typed conflict contract
        /// as <see cref="CommitAsync"/>. No REST fallback exists — live mode
        /// requires the socket; offline flushes queue and compose.
        /// </summary>
        Awaitable<NeoCommitResult> ForkLiveAsync(NeoLiveForkRequest request);

        /// <summary>
        /// Record-scoped merge into the live head snapshot. A conflict is
        /// limited to the one stale logical record and includes its current
        /// descriptor for targeted rebase.
        /// </summary>
        Awaitable<NeoLivePatchResult> PatchLiveAsync(NeoLivePatchRequest request);
    }
}
