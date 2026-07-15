// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>Lifecycle state of a single active-file <see cref="NeoSaveSynchronizer"/>.</summary>
    public enum NeoSaveSynchronizerState
    {
        Idle,
        Loading,

        /// <summary>Awaiting a developer decision (conflict / migration / clone).</summary>
        Resolving,
        Ready,
        Committing,
        Error,
    }

    /// <summary>Lifecycle state of a <see cref="NeoProjectStore"/>.</summary>
    public enum NeoProjectStoreState
    {
        /// <summary>Constructed but <c>LoadAsync</c> has not been called yet.</summary>
        Idle,
        Loading,
        Ready,
        Errored,
    }

    /// <summary>
    /// A row in the project store's browsable save list. Aggregates local and
    /// cloud knowledge of one save: whether it exists only locally, whether it must
    /// be cloned to load on the target channel, and whether its values failed to
    /// deserialize (needs migration).
    /// </summary>
    public sealed class NeoSaveListEntry
    {
        public string customId = "";
        public string name = "";
        public string releaseChannelId = "";
        public string? snapshotHash;
        public bool isLocalOnly;
        public bool existsRemotely;
        public bool requiresClone;
        public bool needsMigration;
        public double? archivedAt;

        public bool IsArchived => archivedAt.HasValue;
    }

    public enum NeoSaveConflictResolution
    {
        KeepLocal,
        KeepRemote,
    }

    /// <summary>
    /// Describes a divergence between the local and cloud heads of the same save
    /// (different <see cref="RemoteGameSave.snapshotHash"/>, neither an ancestor).
    /// </summary>
    public sealed class NeoSaveConflict
    {
        public NeoSaveConflict(LocalGameSave local, RemoteGameSave remote)
        {
            Local = local;
            Remote = remote;
        }

        public LocalGameSave Local { get; }
        public RemoteGameSave Remote { get; }
    }

    /// <summary>
    /// The developer's resolution channel for a <see cref="NeoSaveConflict"/>.
    /// Awaitable so a handler may resolve immediately or after an async UI prompt.
    /// Choosing <see cref="KeepLocal"/> writes a new head snapshot (never a
    /// destructive overwrite), so no data is lost.
    /// </summary>
    public sealed class NeoSaveConflictContinuation
    {
        private readonly AwaitableCompletionSource<NeoSaveConflictResolution> completion =
            new AwaitableCompletionSource<NeoSaveConflictResolution>();

        public Awaitable<NeoSaveConflictResolution> Completion => completion.Awaitable;

        public void KeepLocal() => completion.TrySetResult(NeoSaveConflictResolution.KeepLocal);

        public void KeepRemote() => completion.TrySetResult(NeoSaveConflictResolution.KeepRemote);
    }

    /// <summary>
    /// Raised only when the SDK's internal <c>TryDeserialize</c> fails: the save's
    /// values cannot be read against the current schema. The save remains browsable
    /// with opaque values and is not auto-loaded until the developer resolves it.
    /// </summary>
    public sealed class NeoSaveMigration
    {
        public NeoSaveMigration(string customId, NeoSaveValues opaqueValues, ProjectData schema)
            : this(
                customId,
                opaqueValues,
                new Dictionary<string, string?>(),
                schema)
        {
        }

        public NeoSaveMigration(
            string customId,
            NeoSaveValues opaqueValues,
            IReadOnlyDictionary<string, string?> staticBindings,
            ProjectData schema)
        {
            CustomId = customId;
            OpaqueValues = opaqueValues;
            StaticBindings = staticBindings;
            Schema = schema;
        }

        public string CustomId { get; }
        public NeoSaveValues OpaqueValues { get; }
        public IReadOnlyDictionary<string, string?> StaticBindings { get; }
        public ProjectData Schema { get; }
    }

    /// <summary>
    /// Resolution channel for a <see cref="NeoSaveMigration"/>: provide migrated
    /// save content to continue loading, or skip (the load is abandoned).
    /// </summary>
    public sealed class NeoSaveMigrationContinuation
    {
        private readonly AwaitableCompletionSource<string?> completion =
            new AwaitableCompletionSource<string?>();

        public Awaitable<string?> Completion => completion.Awaitable;

        /// <summary>Supply migrated save content (a JSON envelope) to load instead.</summary>
        public void ResolveWith(string migratedContent)
        {
            if (string.IsNullOrWhiteSpace(migratedContent))
            {
                throw new ArgumentException("Migrated content cannot be empty.", nameof(migratedContent));
            }

            completion.TrySetResult(migratedContent);
        }

        /// <summary>Abandon the load; the save stays browsable but is not loaded.</summary>
        public void Skip() => completion.TrySetResult(null);
    }

    /// <summary>
    /// Resolution channel for loading a save bound to a different release channel
    /// than the target: <see cref="Approve"/> clones + loads, <see cref="Deny"/>
    /// is a no-op.
    /// </summary>
    public sealed class NeoCloneContinuation
    {
        private readonly AwaitableCompletionSource<NeoCloneDecision> completion =
            new AwaitableCompletionSource<NeoCloneDecision>();

        public Awaitable<NeoCloneDecision> Completion => completion.Awaitable;

        public void Approve(string? newName = null) =>
            completion.TrySetResult(new NeoCloneDecision(true, newName));

        public void Deny() => completion.TrySetResult(new NeoCloneDecision(false, null));
    }

    public readonly struct NeoCloneDecision
    {
        public NeoCloneDecision(bool approved, string? newName)
        {
            Approved = approved;
            NewName = newName;
        }

        public bool Approved { get; }
        public string? NewName { get; }
    }

    /// <summary>
    /// Thrown when a cloud commit conflicts but no <c>OnConflict</c> resolver is
    /// attached. Cloud sync requires a resolver so the developer never silently
    /// loses data.
    /// </summary>
    public sealed class NeoSaveConflictUnresolvedException : Exception
    {
        public NeoSaveConflictUnresolvedException(string message)
            : base(message)
        {
        }
    }

    /// <summary>
    /// Active-file abstraction the generated client consumes. Surfaces the project
    /// schema (from the owning store) plus async load/commit of the active save's
    /// serialized content. A developer wanting a fully custom save stack implements
    /// this directly; normally it is a <see cref="NeoSaveSynchronizer"/>.
    /// </summary>
    public interface INeoSaveLoader
    {
        /// <summary>The authored project schema, owned by the project store.</summary>
        ProjectData Schema { get; }

        /// <summary>The active save's stable <c>customId</c>.</summary>
        string CustomId { get; }

        /// <summary>
        /// Resolves the active save's serialized content to load (conflict /
        /// migration / clone handled along the way), or null when there is nothing
        /// to load (a brand-new local draft).
        /// </summary>
        Awaitable<string?> LoadSaveContentAsync();

        /// <summary>
        /// Persists serialized save content locally and (when cloud sync is active)
        /// to the cloud.
        /// </summary>
        Awaitable CommitSaveContentAsync(string content, bool replaceSnapshot);
    }

    /// <summary>
    /// Optional capability of an <see cref="INeoSaveLoader"/>: it can deliver
    /// externally-merged save content while a session runs (live save
    /// sessions). The client subscribes on construction and applies inbound
    /// content itself, raising typed change events with
    /// <see cref="NeoChangeSource.External"/> — games never wire this up.
    /// </summary>
    public interface INeoLiveContentSource
    {
        /// <summary>
        /// Raised with the merged, serialized save content whenever an
        /// external co-editor patched the active save.
        /// </summary>
        event Action<string>? OnLiveContentChanged;
    }
}
