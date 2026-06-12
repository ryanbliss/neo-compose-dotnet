// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    public sealed class NeoSaveOptions
    {
        public bool DiagnosticsEnabled { get; set; } = true;
        public NeoClient.BuildSaveName? BuildSaveName { get; set; }

        /// <summary>
        /// Live save sessions (<c>specs/live-save-sessions.md</c>): when a
        /// realtime provider is registered, each play session forks the save's
        /// head into one live snapshot and streams throttled per-key patches
        /// into it, instead of appending a snapshot per save. Inbound live
        /// edits (e.g. from the web tool) auto-apply inside the client (it
        /// subscribes to <see cref="INeoLiveContentSource"/> itself), raising
        /// typed change events with
        /// <see cref="NeoChangeSource.External"/>. Set false to
        /// keep classic snapshot-per-commit behavior even while realtime is
        /// connected. Without a realtime provider this flag has no effect.
        /// </summary>
        public bool LiveSessionsEnabled { get; set; } = true;
    }
}
