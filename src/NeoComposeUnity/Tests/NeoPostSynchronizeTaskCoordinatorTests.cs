// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using NeoCompose.Unity.Editor;
using Newtonsoft.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public sealed class NeoPostSynchronizeTaskCoordinatorTests
    {
        [Test]
        public void Request_ValidatesEachFieldAndRequiresCollectionPhase()
        {
            StringAssert.Contains(
                "only be called",
                Assert.Throws<InvalidOperationException>(() =>
                    NeoPostSynchronizeTasks.Request(
                        "tests.artifact",
                        "owner",
                        "Generate artifact"))!.Message);
            Assert.Throws<ArgumentException>(() =>
                NeoPostSynchronizeTasks.Request("", "owner", "name"));
            Assert.Throws<ArgumentException>(() =>
                NeoPostSynchronizeTasks.Request("Tests.Artifact", "owner", "name"));
            Assert.Throws<ArgumentException>(() =>
                NeoPostSynchronizeTasks.Request("tests.artifact", "", "name"));
            Assert.Throws<ArgumentException>(() =>
                NeoPostSynchronizeTasks.Request("tests.artifact", "owner", ""));
        }

        [Test]
        public void Collection_PersistsBeforeReturnAndCoalescesInRequestOrder()
        {
            var persistence = new MemoryPersistence();
            var generation = CreateGeneration();
            persistence.Save(generation);
            var coordinator = new NeoPostSynchronizeTaskCoordinator(persistence);

            using (coordinator.BeginCollection(generation))
            {
                NeoPostSynchronizeTasks.Request(
                    "tests.artifact",
                    "owner-b",
                    "First display name");
                Assert.AreEqual(
                    1,
                    persistence.Load()!.Tasks.Count,
                    "Request must persist its descriptor before returning.");
                NeoPostSynchronizeTasks.Request(
                    "tests.artifact",
                    "owner-a",
                    "Second task");
                NeoPostSynchronizeTasks.Request(
                    "tests.artifact",
                    "owner-b",
                    "Latest display name");
            }

            var persisted = persistence.Load()!;
            Assert.AreEqual(2, persisted.Tasks.Count);
            CollectionAssert.AreEqual(
                new[] { "owner-b", "owner-a" },
                persisted.Tasks.OrderBy(task => task.Order)
                    .Select(task => task.OwnerValueId));
            Assert.AreEqual("Latest display name", persisted.Tasks[0].Name);
        }

        [Test]
        public void CompletionPipeline_PersistsSucceededBeforeAwaitingPreview()
        {
            var persistence = new MemoryPersistence();
            var generation = CollectTwoTasks(persistence);
            var coordinator = new NeoPostSynchronizeTaskCoordinator(persistence);
            var handledOwners = new List<string>();
            using var registration = NeoPostSynchronizeTaskHandlers.Register(
                "tests.artifact",
                (context, _) =>
                {
                    handledOwners.Add(context.OwnerValueId);
                    return NeoAwaitable.Completed();
                });
            bool previewRan = false;
            var pipeline = new NeoPostSynchronizeCompletionPipeline(
                persistence,
                coordinator,
                (projectId, versionId, _) =>
                {
                    previewRan = true;
                    Assert.AreEqual("project", projectId);
                    Assert.AreEqual("version", versionId);
                    Assert.That(
                        persistence.Load()!.Tasks,
                        Has.All.Property("State")
                            .EqualTo(NeoPostSynchronizeTaskState.Succeeded));
                    return NeoAwaitable.Completed();
                });

            pipeline.RunAsync(generation, CancellationToken.None)
                .GetAwaiter()
                .GetResult();

            CollectionAssert.AreEqual(new[] { "owner-b", "owner-a" }, handledOwners);
            Assert.IsTrue(previewRan);
        }

        [Test]
        public void CompletionPipeline_PreviewFailurePropagatesAndKeepsDurableState()
        {
            var persistence = new MemoryPersistence();
            var generation = CreateGeneration();
            persistence.Save(generation);
            var coordinator = new NeoPostSynchronizeTaskCoordinator(persistence);
            using (coordinator.BeginCollection(generation))
            {
                NeoPostSynchronizeTasks.Request(
                    "tests.artifact",
                    "owner",
                    "Generate artifact");
            }
            using var registration = NeoPostSynchronizeTaskHandlers.Register(
                "tests.artifact",
                (_, _) => NeoAwaitable.Completed());
            var pipeline = new NeoPostSynchronizeCompletionPipeline(
                persistence,
                coordinator,
                (_, _, _) => throw new InvalidOperationException("preview failed"));

            var error = Assert.Throws<InvalidOperationException>(() =>
                pipeline.RunAsync(generation, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

            StringAssert.Contains("preview failed", error!.Message);
            Assert.AreEqual(
                NeoPostSynchronizeTaskState.Succeeded,
                persistence.Load()!.Tasks.Single().State);
            Assert.IsFalse(persistence.WasCleared);
        }

        [Test]
        public async Task CompletionPipeline_AwaitsPreviewBeforeCompleting()
        {
            var persistence = new MemoryPersistence();
            var generation = CreateGeneration();
            persistence.Save(generation);
            var coordinator = new NeoPostSynchronizeTaskCoordinator(persistence);
            var previewCompletion = new UnityEngine.AwaitableCompletionSource();
            bool previewStarted = false;
            var pipeline = new NeoPostSynchronizeCompletionPipeline(
                persistence,
                coordinator,
                (_, _, _) =>
                {
                    previewStarted = true;
                    return previewCompletion.Awaitable;
                });

            Task observedPipeline = Observe(pipeline.RunAsync(
                generation,
                CancellationToken.None));

            Assert.IsTrue(previewStarted);
            Assert.IsFalse(observedPipeline.IsCompleted);
            previewCompletion.SetResult();
            await observedPipeline;
        }

        [Test]
        public void Dispatch_MissingHandlerPersistsPinpointedFailure()
        {
            var persistence = new MemoryPersistence();
            var generation = CreateGeneration();
            persistence.Save(generation);
            var coordinator = new NeoPostSynchronizeTaskCoordinator(persistence);
            using (coordinator.BeginCollection(generation))
            {
                NeoPostSynchronizeTasks.Request(
                    "tests.missing",
                    "owner-42",
                    "Generate missing artifact");
            }

            var error = Assert.Throws<InvalidOperationException>(() =>
                coordinator.DispatchAsync(generation, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

            StringAssert.Contains("tests.missing", error!.Message);
            StringAssert.Contains("owner-42", error.Message);
            Assert.AreEqual(
                NeoPostSynchronizeTaskState.Failed,
                persistence.Load()!.Tasks.Single().State);
        }

        [Test]
        public void Dispatch_HandlerFailurePersistsContextAndFailedState()
        {
            var persistence = new MemoryPersistence();
            var generation = CreateGeneration();
            persistence.Save(generation);
            var coordinator = new NeoPostSynchronizeTaskCoordinator(persistence);
            using (coordinator.BeginCollection(generation))
            {
                NeoPostSynchronizeTasks.Request(
                    "tests.artifact",
                    "owner-17",
                    "Generate fixture artifact");
            }
            using var registration = NeoPostSynchronizeTaskHandlers.Register(
                "tests.artifact",
                (_, _) => throw new InvalidOperationException("fixture exploded"));

            var error = Assert.Throws<InvalidOperationException>(() =>
                coordinator.DispatchAsync(generation, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());

            StringAssert.Contains("fixture exploded", error!.Message);
            StringAssert.Contains("owner-17", error.Message);
            var persisted = persistence.Load()!.Tasks.Single();
            Assert.AreEqual(NeoPostSynchronizeTaskState.Failed, persisted.State);
            Assert.AreEqual("fixture exploded", persisted.Error);
        }

        [Test]
        public async Task Dispatch_ObservesCancellationAtHandlerAwaitBoundary()
        {
            var persistence = new MemoryPersistence();
            var generation = CreateGeneration();
            persistence.Save(generation);
            var coordinator = new NeoPostSynchronizeTaskCoordinator(persistence);
            using (coordinator.BeginCollection(generation))
            {
                NeoPostSynchronizeTasks.Request(
                    "tests.artifact",
                    "owner",
                    "Generate artifact");
            }
            var handlerCompletion = new UnityEngine.AwaitableCompletionSource();
            using var registration = NeoPostSynchronizeTaskHandlers.Register(
                "tests.artifact",
                (_, _) => handlerCompletion.Awaitable);
            using var cancellation = new CancellationTokenSource();

            UnityEngine.Awaitable dispatch = coordinator.DispatchAsync(
                generation,
                cancellation.Token);
            cancellation.Cancel();
            handlerCompletion.SetResult();

            await AssertCanceled(dispatch);
            Assert.AreEqual(
                NeoPostSynchronizeTaskState.Pending,
                persistence.Load()!.Tasks.Single().State);
        }

        [Test]
        public void Dispatch_SupersededGenerationCannotPublishSuccess()
        {
            var persistence = new MemoryPersistence();
            var generation = CreateGeneration();
            persistence.Save(generation);
            var coordinator = new NeoPostSynchronizeTaskCoordinator(persistence);
            using (coordinator.BeginCollection(generation))
            {
                NeoPostSynchronizeTasks.Request(
                    "tests.artifact",
                    "owner",
                    "Generate artifact");
            }
            using var registration = NeoPostSynchronizeTaskHandlers.Register(
                "tests.artifact",
                (_, _) =>
                {
                    persistence.Save(new NeoPostSynchronizeGenerationState
                    {
                        GenerationId = "new-generation",
                        ProjectId = "project",
                        VersionId = "version",
                    });
                    return NeoAwaitable.Completed();
                });

            Assert.Throws<OperationCanceledException>(() =>
                coordinator.DispatchAsync(generation, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult());
            Assert.AreEqual("new-generation", persistence.Load()!.GenerationId);
        }

        [Test]
        public void HandlerRegistration_RejectsDuplicateAndLeaseIsIdempotent()
        {
            var first = NeoPostSynchronizeTaskHandlers.Register(
                "tests.artifact",
                (_, _) => NeoAwaitable.Completed());
            Assert.Throws<InvalidOperationException>(() =>
                NeoPostSynchronizeTaskHandlers.Register(
                    "tests.artifact",
                    (_, _) => NeoAwaitable.Completed()));

            first.Dispose();
            using var replacement = NeoPostSynchronizeTaskHandlers.Register(
                "tests.artifact",
                (_, _) => NeoAwaitable.Completed());
            first.Dispose();
            Assert.IsTrue(NeoPostSynchronizeTaskHandlers.TryGet(
                "tests.artifact",
                out _));
        }

        private static NeoPostSynchronizeGenerationState CollectTwoTasks(
            MemoryPersistence persistence)
        {
            var generation = CreateGeneration();
            persistence.Save(generation);
            var coordinator = new NeoPostSynchronizeTaskCoordinator(persistence);
            using (coordinator.BeginCollection(generation))
            {
                NeoPostSynchronizeTasks.Request(
                    "tests.artifact",
                    "owner-b",
                    "First artifact");
                NeoPostSynchronizeTasks.Request(
                    "tests.artifact",
                    "owner-a",
                    "Second artifact");
            }
            return generation;
        }

        private static NeoPostSynchronizeGenerationState CreateGeneration() => new()
        {
            GenerationId = "generation",
            ProjectId = "project",
            VersionId = "version",
            ProjectJsonPath = "Assets/project.neo.json",
            Status = NeoPostSynchronizeGenerationStatus.Pending,
        };

        private static async Task Observe(UnityEngine.Awaitable awaitable)
        {
            await awaitable;
        }

        private static async Task AssertCanceled(UnityEngine.Awaitable awaitable)
        {
            try
            {
                await awaitable;
                Assert.Fail("Expected post-sync work to be canceled.");
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        private sealed class MemoryPersistence : INeoPostSynchronizeTaskPersistence
        {
            private string? serialized;

            internal bool WasCleared { get; private set; }

            public NeoPostSynchronizeGenerationState? Load() =>
                string.IsNullOrEmpty(serialized)
                    ? null
                    : JsonConvert.DeserializeObject<NeoPostSynchronizeGenerationState>(
                        serialized!);

            public void Save(NeoPostSynchronizeGenerationState state)
            {
                serialized = JsonConvert.SerializeObject(state);
            }

            public void Clear()
            {
                WasCleared = true;
                serialized = null;
            }
        }
    }
}
