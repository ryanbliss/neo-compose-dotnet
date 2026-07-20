// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NeoCompose.Runtime;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    public sealed class NeoPostSynchronizeTaskContext
    {
        internal NeoPostSynchronizeTaskContext(
            NeoPostSynchronizeTaskDescriptor descriptor)
        {
            GenerationId = descriptor.GenerationId;
            ProjectId = descriptor.ProjectId;
            VersionId = descriptor.VersionId;
            ProjectJsonPath = descriptor.ProjectJsonPath;
            Kind = descriptor.Kind;
            OwnerValueId = descriptor.OwnerValueId;
            Name = descriptor.Name;
            Attempt = descriptor.Attempt;
        }

        public string GenerationId { get; }
        public string ProjectId { get; }
        public string VersionId { get; }
        public string ProjectJsonPath { get; }
        public string Kind { get; }
        public string OwnerValueId { get; }
        public string Name { get; }
        public int Attempt { get; }
    }

    public static class NeoPostSynchronizeTaskHandlers
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<string, HandlerRegistration> Registrations = new();

        public static IDisposable Register(
            string kind,
            Func<NeoPostSynchronizeTaskContext, CancellationToken, Awaitable> handler)
        {
            NeoPostSynchronizeTasks.ValidateKind(kind, nameof(kind));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            string normalizedKind = kind.Trim();

            lock (Gate)
            {
                if (Registrations.ContainsKey(normalizedKind))
                {
                    throw new InvalidOperationException(
                        $"A post-sync task handler is already registered for kind " +
                        $"'{normalizedKind}'.");
                }

                var registration = new HandlerRegistration(normalizedKind, handler);
                Registrations.Add(normalizedKind, registration);
                return registration;
            }
        }

        internal static bool TryGet(
            string kind,
            out Func<NeoPostSynchronizeTaskContext, CancellationToken, Awaitable> handler)
        {
            lock (Gate)
            {
                if (Registrations.TryGetValue(kind, out var registration))
                {
                    handler = registration.Handler;
                    return true;
                }
            }

            handler = null!;
            return false;
        }

        private sealed class HandlerRegistration : IDisposable
        {
            private bool isDisposed;

            internal HandlerRegistration(
                string kind,
                Func<NeoPostSynchronizeTaskContext, CancellationToken, Awaitable> handler)
            {
                Kind = kind;
                Handler = handler;
            }

            internal string Kind { get; }
            internal Func<NeoPostSynchronizeTaskContext, CancellationToken, Awaitable>
                Handler { get; }

            public void Dispose()
            {
                lock (Gate)
                {
                    if (isDisposed) return;
                    isDisposed = true;
                    if (Registrations.TryGetValue(Kind, out var current) &&
                        ReferenceEquals(current, this))
                    {
                        Registrations.Remove(Kind);
                    }
                }
            }
        }
    }

    [Serializable]
    internal enum NeoPostSynchronizeGenerationStatus
    {
        Pending,
        Running,
        Failed,
    }

    [Serializable]
    internal enum NeoPostSynchronizeTaskState
    {
        Pending,
        Running,
        Succeeded,
        Failed,
    }

    [Serializable]
    internal sealed class NeoPostSynchronizeTaskDescriptor
    {
        public string GenerationId { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string VersionId { get; set; } = "";
        public string ProjectJsonPath { get; set; } = "";
        public string Kind { get; set; } = "";
        public string OwnerValueId { get; set; } = "";
        public string Name { get; set; } = "";
        public NeoPostSynchronizeTaskState State { get; set; }
        public int Attempt { get; set; }
        public int Order { get; set; }
        public string? Error { get; set; }
    }

    [Serializable]
    internal sealed class NeoPostSynchronizeGenerationState
    {
        public string GenerationId { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string VersionId { get; set; } = "";
        public string ProjectJsonPath { get; set; } = "";
        public string GeneratedTypesPath { get; set; } = "";
        public string AssetDatabasePath { get; set; } = "";
        public string GeneratedNamespace { get; set; } = "";
        public NeoPostSynchronizeGenerationStatus Status { get; set; }
        public int ProcessorAttempts { get; set; }
        public int NextTaskOrder { get; set; }
        public string? Error { get; set; }
        public List<NeoPostSynchronizeTaskDescriptor> Tasks { get; set; } = new();
    }

    internal interface INeoPostSynchronizeTaskPersistence
    {
        NeoPostSynchronizeGenerationState? Load();
        void Save(NeoPostSynchronizeGenerationState state);
        void Clear();
    }

    internal sealed class NeoSessionStatePostSynchronizeTaskPersistence
        : INeoPostSynchronizeTaskPersistence
    {
        private const string StateKey = "NeoCompose.PostSynchronize.GenerationState";

        public NeoPostSynchronizeGenerationState? Load()
        {
            string json = SessionState.GetString(StateKey, "");
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonConvert.DeserializeObject<NeoPostSynchronizeGenerationState>(json);
        }

        public void Save(NeoPostSynchronizeGenerationState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            SessionState.SetString(StateKey, JsonConvert.SerializeObject(state));
        }

        public void Clear()
        {
            SessionState.EraseString(StateKey);
        }
    }

    internal sealed class NeoPostSynchronizeTaskCoordinator
    {
        private readonly INeoPostSynchronizeTaskPersistence persistence;

        internal NeoPostSynchronizeTaskCoordinator(
            INeoPostSynchronizeTaskPersistence persistence)
        {
            this.persistence = persistence
                ?? throw new ArgumentNullException(nameof(persistence));
        }

        internal IDisposable BeginCollection(NeoPostSynchronizeGenerationState generation)
        {
            if (generation == null) throw new ArgumentNullException(nameof(generation));
            return NeoPostSynchronizeTasks.BeginCollection(request =>
                Collect(generation, request));
        }

        internal void RecoverInterrupted(NeoPostSynchronizeGenerationState generation)
        {
            if (generation == null) throw new ArgumentNullException(nameof(generation));
            bool changed = false;
            if (generation.Status == NeoPostSynchronizeGenerationStatus.Running)
            {
                generation.Status = NeoPostSynchronizeGenerationStatus.Pending;
                generation.Error = null;
                changed = true;
            }

            foreach (var descriptor in generation.Tasks)
            {
                if (descriptor.State != NeoPostSynchronizeTaskState.Running) continue;
                descriptor.State = NeoPostSynchronizeTaskState.Pending;
                descriptor.Error = null;
                changed = true;
            }

            if (changed) persistence.Save(generation);
        }

        internal async Awaitable DispatchAsync(
            NeoPostSynchronizeGenerationState generation,
            CancellationToken cancellationToken,
            Action<NeoPostSynchronizeTaskDescriptor>? onStarted = null)
        {
            if (generation == null) throw new ArgumentNullException(nameof(generation));
            var ordered = generation.Tasks
                .OrderBy(descriptor => descriptor.Order)
                .ThenBy(descriptor => descriptor.Kind, StringComparer.Ordinal)
                .ThenBy(descriptor => descriptor.OwnerValueId, StringComparer.Ordinal)
                .ToArray();

            foreach (var descriptor in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireAuthoritative(generation.GenerationId, cancellationToken);
                if (descriptor.State == NeoPostSynchronizeTaskState.Succeeded) continue;
                if (descriptor.State == NeoPostSynchronizeTaskState.Failed)
                {
                    throw TaskFailure(descriptor, descriptor.Error ?? "Task previously failed.");
                }

                if (!NeoPostSynchronizeTaskHandlers.TryGet(descriptor.Kind, out var handler))
                {
                    descriptor.State = NeoPostSynchronizeTaskState.Failed;
                    descriptor.Error =
                        $"No post-sync task handler is registered for kind '{descriptor.Kind}'.";
                    persistence.Save(generation);
                    throw TaskFailure(descriptor, descriptor.Error);
                }

                descriptor.State = NeoPostSynchronizeTaskState.Running;
                descriptor.Attempt += 1;
                descriptor.Error = null;
                persistence.Save(generation);
                onStarted?.Invoke(descriptor);

                try
                {
                    await handler(
                        new NeoPostSynchronizeTaskContext(descriptor),
                        cancellationToken);
                    cancellationToken.ThrowIfCancellationRequested();
                    RequireAuthoritative(generation.GenerationId, cancellationToken);
                    descriptor.State = NeoPostSynchronizeTaskState.Succeeded;
                    persistence.Save(generation);
                }
                catch (OperationCanceledException)
                {
                    if (IsAuthoritative(generation.GenerationId))
                    {
                        descriptor.State = NeoPostSynchronizeTaskState.Pending;
                        descriptor.Error = null;
                        persistence.Save(generation);
                    }
                    throw;
                }
                catch (Exception exception)
                {
                    if (IsAuthoritative(generation.GenerationId))
                    {
                        descriptor.State = NeoPostSynchronizeTaskState.Failed;
                        descriptor.Error = exception.GetBaseException().Message;
                        persistence.Save(generation);
                    }
                    throw TaskFailure(descriptor, exception.GetBaseException().Message, exception);
                }
            }
        }

        private void Collect(
            NeoPostSynchronizeGenerationState generation,
            NeoPostSynchronizeTaskRequest request)
        {
            RequireAuthoritative(generation.GenerationId, CancellationToken.None);
            var existing = generation.Tasks.FirstOrDefault(descriptor =>
                descriptor.GenerationId == generation.GenerationId &&
                descriptor.Kind == request.Kind &&
                descriptor.OwnerValueId == request.OwnerValueId);
            if (existing != null)
            {
                existing.Name = request.Name;
                persistence.Save(generation);
                return;
            }

            generation.Tasks.Add(new NeoPostSynchronizeTaskDescriptor
            {
                GenerationId = generation.GenerationId,
                ProjectId = generation.ProjectId,
                VersionId = generation.VersionId,
                ProjectJsonPath = generation.ProjectJsonPath,
                Kind = request.Kind,
                OwnerValueId = request.OwnerValueId,
                Name = request.Name,
                State = NeoPostSynchronizeTaskState.Pending,
                Order = generation.NextTaskOrder++,
            });
            persistence.Save(generation);
        }

        private void RequireAuthoritative(
            string generationId,
            CancellationToken cancellationToken)
        {
            if (IsAuthoritative(generationId)) return;
            throw new OperationCanceledException(
                $"Post-sync generation '{generationId}' was superseded.",
                null,
                cancellationToken);
        }

        private bool IsAuthoritative(string generationId) =>
            persistence.Load()?.GenerationId == generationId;

        private static InvalidOperationException TaskFailure(
            NeoPostSynchronizeTaskDescriptor descriptor,
            string cause,
            Exception? inner = null) =>
            new(
                $"Post-sync generation '{descriptor.GenerationId}' task " +
                $"'{descriptor.Name}' (kind '{descriptor.Kind}', owner value " +
                $"'{descriptor.OwnerValueId}', attempt {descriptor.Attempt}) failed: {cause}",
                inner);
    }

    /// <summary>
    /// Orders durable artifact tasks before TileGrid preview refresh. Keeping the
    /// refresh operation as a constructor dependency makes the ordering a normal
    /// production seam and lets integrations exercise the same pipeline without
    /// replacing global editor behavior.
    /// </summary>
    internal sealed class NeoPostSynchronizeCompletionPipeline
    {
        private readonly INeoPostSynchronizeTaskPersistence persistence;
        private readonly NeoPostSynchronizeTaskCoordinator taskCoordinator;
        private readonly Func<string, string, CancellationToken, Awaitable>
            refreshBindingsAsync;

        internal NeoPostSynchronizeCompletionPipeline(
            INeoPostSynchronizeTaskPersistence persistence,
            NeoPostSynchronizeTaskCoordinator taskCoordinator,
            Func<string, string, CancellationToken, Awaitable> refreshBindingsAsync)
        {
            this.persistence = persistence
                ?? throw new ArgumentNullException(nameof(persistence));
            this.taskCoordinator = taskCoordinator
                ?? throw new ArgumentNullException(nameof(taskCoordinator));
            this.refreshBindingsAsync = refreshBindingsAsync
                ?? throw new ArgumentNullException(nameof(refreshBindingsAsync));
        }

        internal async Awaitable RunAsync(
            NeoPostSynchronizeGenerationState generation,
            CancellationToken cancellationToken,
            Action<NeoPostSynchronizeTaskDescriptor>? onTaskStarted = null,
            Action? onBeforePreviewRefresh = null)
        {
            if (generation == null) throw new ArgumentNullException(nameof(generation));

            await taskCoordinator.DispatchAsync(
                generation,
                cancellationToken,
                onTaskStarted);
            cancellationToken.ThrowIfCancellationRequested();
            RequireAuthoritative(generation.GenerationId, cancellationToken);
            onBeforePreviewRefresh?.Invoke();

            await refreshBindingsAsync(
                generation.ProjectId,
                generation.VersionId,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            RequireAuthoritative(generation.GenerationId, cancellationToken);
        }

        private void RequireAuthoritative(
            string generationId,
            CancellationToken cancellationToken)
        {
            if (persistence.Load()?.GenerationId == generationId) return;
            throw new OperationCanceledException(
                $"Post-sync generation '{generationId}' was superseded.",
                null,
                cancellationToken);
        }
    }
}
