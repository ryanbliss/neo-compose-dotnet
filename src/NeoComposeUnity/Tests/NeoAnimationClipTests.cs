// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public sealed class NeoAnimationClipTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        [TestCase(NeoPlayDirection.Forward, new[] { 0, 1, 2, 3 })]
        [TestCase(NeoPlayDirection.Backward, new[] { 3, 2, 1, 0 })]
        public void PlayOnce_TraversesInRequestedDirection(
            NeoPlayDirection direction,
            int[] expected)
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<int>();
            var clip = CreateClip(target, "object-a", 4, entered);

            clip.PlayOnce(direction);
            Tick(clip, 4);

            CollectionAssert.AreEqual(expected, entered);
            Assert.IsFalse(clip.IsPlaying);
        }

        [TestCase(NeoPlayDirection.Forward, new[] { 0, 1, 2, 3, 0, 1 })]
        [TestCase(NeoPlayDirection.Backward, new[] { 3, 2, 1, 0, 3, 2 })]
        public void PlayLoop_RepeatWrapsWithoutStopping(
            NeoPlayDirection direction,
            int[] expected)
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<int>();
            var clip = CreateClip(target, "object-a", 4, entered);

            clip.PlayLoop(NeoPlayMode.Repeat, direction);
            Tick(clip, 5);

            CollectionAssert.AreEqual(expected, entered);
            Assert.IsTrue(clip.IsPlaying);
        }

        [TestCase(NeoPlayDirection.Forward, new[] { 0, 1, 2, 3, 2, 1, 0, 1 })]
        [TestCase(NeoPlayDirection.Backward, new[] { 3, 2, 1, 0, 1, 2, 3, 2 })]
        public void PlayLoop_BoomerangReversesWithoutDuplicatingEnds(
            NeoPlayDirection direction,
            int[] expected)
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<int>();
            var clip = CreateClip(target, "object-a", 4, entered);

            clip.PlayLoop(NeoPlayMode.Boomerang, direction);
            Tick(clip, 7);

            CollectionAssert.AreEqual(expected, entered);
            Assert.IsTrue(clip.IsPlaying);
        }

        [Test]
        public void PlayFixedLoop_RepeatCountsCompletePasses()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<int>();
            var clip = CreateClip(target, "object-a", 3, entered);

            clip.PlayFixedLoop(2);
            Tick(clip, 6);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 0, 1, 2 }, entered);
            Assert.IsFalse(clip.IsPlaying);
        }

        [Test]
        public void PlayFixedLoop_BoomerangCountsThereAndBackAsOneLoop()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<int>();
            var clip = CreateClip(target, "object-a", 3, entered);

            clip.PlayFixedLoop(1, NeoPlayMode.Boomerang);
            Tick(clip, 4);

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 1, 0 }, entered);
            Assert.IsFalse(clip.IsPlaying);
        }

        [Test]
        public void SingleFrameFixedLoop_EntersOncePerLoopAndCompletes()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<int>();
            var clip = CreateClip(target, "object-a", 1, entered);

            clip.PlayFixedLoop(2, NeoPlayMode.Boomerang);
            Tick(clip, 2);

            CollectionAssert.AreEqual(new[] { 0, 0 }, entered);
            Assert.IsFalse(clip.IsPlaying);
        }

        [Test]
        public void PauseResumeStop_ManageClockAndEvents()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<int>();
            var events = new List<string>();
            var clip = CreateClip(target, "object-a", 3, entered);
            clip.OnPlay += () => events.Add("play");
            clip.OnPause += () => events.Add("pause");
            clip.OnResume += () => events.Add("resume");
            clip.OnStop += () => events.Add("stop");

            clip.PlayLoop();
            clip.Pause();
            clip.Tick(1f);
            Assert.AreEqual(0, clip.CurrentFrame);
            clip.Resume();
            clip.Tick(0.1f);
            clip.Stop();

            CollectionAssert.AreEqual(new[] { 0, 1 }, entered);
            CollectionAssert.AreEqual(new[] { "play", "pause", "resume", "stop" }, events);
            Assert.IsFalse(clip.IsPlaying);
            Assert.IsFalse(clip.IsPaused);
        }

        [Test]
        public async Task PlayOnceAsync_CompletesNaturally()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var clip = CreateClip(target, "object-a", 2, new List<int>());

            Task completion = clip.PlayOnceAsync();
            Tick(clip, 2);

            await completion;
            Assert.IsTrue(completion.IsCompletedSuccessfully);
        }

        [Test]
        public void PlayOnceAsync_StopAndCallerCancellationCancel()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var clip = CreateClip(target, "object-a", 2, new List<int>());

            Task stopped = clip.PlayOnceAsync();
            clip.Stop();
            Assert.IsTrue(stopped.IsCanceled);

            using var cancellation = new CancellationTokenSource();
            Task canceled = clip.PlayOnceAsync(cancellationToken: cancellation.Token);
            cancellation.Cancel();
            Assert.IsFalse(canceled.IsCanceled);
            Assert.IsTrue(clip.IsPlaying);
            clip.Tick(0f);
            Assert.IsTrue(canceled.IsCanceled);
            Assert.IsFalse(clip.IsPlaying);
        }

        [Test]
        public void PlayOnceAsync_CancellationOnlyStopsDuringCoordinatorTick()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var clip = CreateClip(target, "object-a", 2, new List<int>());
            using var cancellation = new CancellationTokenSource();
            int callerThread = Thread.CurrentThread.ManagedThreadId;
            int stopThread = -1;
            clip.OnStop += () => stopThread = Thread.CurrentThread.ManagedThreadId;

            Task completion = clip.PlayOnceAsync(cancellationToken: cancellation.Token);
            Task.Run(cancellation.Cancel).GetAwaiter().GetResult();

            Assert.IsTrue(clip.IsPlaying);
            Assert.IsFalse(completion.IsCompleted);
            Assert.AreEqual(-1, stopThread);

            clip.Tick(0f);

            Assert.IsFalse(clip.IsPlaying);
            Assert.IsTrue(completion.IsCanceled);
            Assert.AreEqual(callerThread, stopThread);
        }

        [Test]
        public void PlayOnceAsync_OnPlayStops_ReturnsCanceledTaskWithoutNullReference()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<int>();
            var clip = CreateClip(target, "object-a", 2, entered);
            clip.OnPlay += clip.Stop;

            Task completion = null!;
            Assert.DoesNotThrow(() => completion = clip.PlayOnceAsync());

            Assert.IsTrue(completion.IsCanceled);
            Assert.IsFalse(clip.IsPlaying);
            CollectionAssert.IsEmpty(entered);
        }

        [Test]
        public void PlayOnceAsync_NestedRestartOwnsItsCancellationRegistration()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<int>();
            var clip = CreateClip(target, "object-a", 2, entered);
            using var outerCancellation = new CancellationTokenSource();
            using var nestedCancellation = new CancellationTokenSource();
            Task? nested = null;
            int playCount = 0;
            clip.OnPlay += () =>
            {
                if (playCount++ == 0)
                {
                    nested = clip.PlayOnceAsync(
                        cancellationToken: nestedCancellation.Token);
                }
            };

            Task outer = clip.PlayOnceAsync(
                cancellationToken: outerCancellation.Token);

            Assert.IsTrue(outer.IsCanceled);
            Assert.IsNotNull(nested);
            Assert.IsTrue(clip.IsPlaying);
            CollectionAssert.AreEqual(new[] { 0 }, entered);

            outerCancellation.Cancel();
            clip.Tick(0f);
            Assert.IsTrue(clip.IsPlaying);
            Assert.IsFalse(nested!.IsCompleted);

            nestedCancellation.Cancel();
            clip.Tick(0f);
            Assert.IsFalse(clip.IsPlaying);
            Assert.IsTrue(nested!.IsCanceled);
        }

        [Test]
        public void Playback_RefreshesRootFallbackBeforeEveryStart()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            int refreshes = 0;
            var entered = new List<(int frame, bool resolved)>();
            var clip = new NeoAnimationClip<TestTarget>(
                target,
                "object-a",
                fps: 10,
                duration: 2,
                target.Client.AnimationCoordinator,
                preparePlayback: () => refreshes += 1,
                applyFrame: (frame, resolved) => entered.Add((frame, resolved)));

            clip.PlayOnce();
            clip.Stop();
            clip.PlayOnce(NeoPlayDirection.Backward);

            Assert.AreEqual(2, refreshes);
            CollectionAssert.AreEqual(
                new[] { (0, false), (1, true) },
                entered);
        }

        [Test]
        public void BackwardSingleFrame_UsesResolvedState()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<(int frame, bool resolved)>();
            var clip = new NeoAnimationClip<TestTarget>(
                target,
                "object-a",
                fps: 10,
                duration: 1,
                target.Client.AnimationCoordinator,
                (frame, resolved) => entered.Add((frame, resolved)));

            clip.PlayOnce(NeoPlayDirection.Backward);

            CollectionAssert.AreEqual(new[] { (0, true) }, entered);
        }

        [Test]
        public void Boomerang_UsesResolvedFramesOnlyWhileTraversingBackward()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<(int frame, bool resolved)>();
            var clip = new NeoAnimationClip<TestTarget>(
                target,
                "object-a",
                fps: 10,
                duration: 3,
                target.Client.AnimationCoordinator,
                (frame, resolved) => entered.Add((frame, resolved)));

            clip.PlayLoop(NeoPlayMode.Boomerang);
            Tick(clip, 5);

            CollectionAssert.AreEqual(
                new[]
                {
                    (0, false),
                    (1, false),
                    (2, false),
                    (1, true),
                    (0, true),
                    (1, false),
                },
                entered);
        }

        [Test]
        public void Playback_MatchesCrossRuntimeParityTraversalVectors()
        {
            JObject fixture = JObject.Parse(
                NeoAnimationFrameResolutionParityFixture.Json);
            JObject traversals = (JObject)fixture["traversals"]!;

            AssertTraversalVector(traversals, "onceForward", clip =>
                clip.PlayOnce(NeoPlayDirection.Forward));
            AssertTraversalVector(traversals, "onceBackward", clip =>
                clip.PlayOnce(NeoPlayDirection.Backward));
            AssertTraversalVector(traversals, "repeatForwardWrap", clip =>
                clip.PlayLoop(NeoPlayMode.Repeat, NeoPlayDirection.Forward));
            AssertTraversalVector(traversals, "repeatBackwardWrap", clip =>
                clip.PlayLoop(NeoPlayMode.Repeat, NeoPlayDirection.Backward));
            AssertTraversalVector(traversals, "boomerangForward", clip =>
                clip.PlayLoop(NeoPlayMode.Boomerang, NeoPlayDirection.Forward));
            AssertTraversalVector(traversals, "boomerangBackward", clip =>
                clip.PlayLoop(NeoPlayMode.Boomerang, NeoPlayDirection.Backward));
        }

        [Test]
        public void Playback_MatchesCrossRuntimeParityResolvedFrames()
        {
            JObject fixture = JObject.Parse(
                NeoAnimationFrameResolutionParityFixture.Json);
            JObject traversals = (JObject)fixture["traversals"]!;

            AssertResolvedFrameVector(fixture, traversals, "onceForward", clip =>
                clip.PlayOnce(NeoPlayDirection.Forward));
            AssertResolvedFrameVector(fixture, traversals, "onceBackward", clip =>
                clip.PlayOnce(NeoPlayDirection.Backward));
            AssertResolvedFrameVector(fixture, traversals, "repeatForwardWrap", clip =>
                clip.PlayLoop(NeoPlayMode.Repeat, NeoPlayDirection.Forward));
            AssertResolvedFrameVector(fixture, traversals, "repeatBackwardWrap", clip =>
                clip.PlayLoop(NeoPlayMode.Repeat, NeoPlayDirection.Backward));
            AssertResolvedFrameVector(fixture, traversals, "boomerangForward", clip =>
                clip.PlayLoop(NeoPlayMode.Boomerang, NeoPlayDirection.Forward));
            AssertResolvedFrameVector(fixture, traversals, "boomerangBackward", clip =>
                clip.PlayLoop(NeoPlayMode.Boomerang, NeoPlayDirection.Backward));
        }

        [Test]
        public void Coordinator_SupersedesOnlyTheSameInstanceIdentity()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var first = CreateClip(target, "object-a", 2, new List<int>());
            var replacement = CreateClip(target, "object-a", 2, new List<int>());
            var otherInstance = CreateClip(target, "object-b", 2, new List<int>());

            Task firstTask = first.PlayOnceAsync();
            otherInstance.PlayLoop();
            replacement.PlayLoop();

            Assert.IsTrue(firstTask.IsCanceled);
            Assert.IsFalse(first.IsPlaying);
            Assert.IsTrue(replacement.IsPlaying);
            Assert.IsTrue(otherInstance.IsPlaying);
        }

        [Test]
        public void FrameEvent_DisposableRegistrationStopsCallbacks()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var clip = CreateClip(target, "object-a", 3, new List<int>());
            int calls = 0;
            IDisposable registration = clip.AddFrameEvent(1, () => calls += 1);

            clip.PlayLoop();
            clip.Tick(0.1f);
            registration.Dispose();
            Tick(clip, 3);

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void FrameEvent_DisposalDuringDispatchUsesStableAllocationFreeSnapshot()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var clip = CreateClip(target, "object-a", 2, new List<int>());
            var calls = new List<string>();
            IDisposable? second = null;
            using IDisposable first = clip.AddFrameEvent(0, () =>
            {
                calls.Add("first");
                second!.Dispose();
            });
            second = clip.AddFrameEvent(0, () => calls.Add("second"));

            clip.PlayOnce();

            CollectionAssert.AreEqual(new[] { "first", "second" }, calls);
            second.Dispose();
        }

        [Test]
        public void InvalidLoopCountAndFrameEventIndexThrow()
        {
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var clip = CreateClip(target, "object-a", 3, new List<int>());

            Assert.Throws<ArgumentOutOfRangeException>(() => clip.PlayFixedLoop(0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                clip.AddFrameEvent(3, () => { }));
        }

        private static NeoAnimationClip<TestTarget> CreateClip(
            TestTarget target,
            string instanceIdentity,
            int duration,
            List<int> entered)
        {
            return new NeoAnimationClip<TestTarget>(
                target,
                instanceIdentity,
                fps: 10,
                duration,
                target.Client.AnimationCoordinator,
                entered.Add);
        }

        private static void AssertTraversalVector(
            JObject traversals,
            string key,
            Action<NeoAnimationClip<TestTarget>> play)
        {
            int[] expected = traversals[key]!.ToObject<int[]>()!;
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<int>();
            var clip = CreateClip(target, key, duration: 4, entered);

            play(clip);
            Tick(clip, expected.Length - 1);

            CollectionAssert.AreEqual(expected, entered, key);
        }

        private static void AssertResolvedFrameVector(
            JObject fixture,
            JObject traversals,
            string key,
            Action<NeoAnimationClip<TestTarget>> play)
        {
            int[] traversal = traversals[key]!.ToObject<int[]>()!;
            var expectedFrames = (JArray)fixture["resolvedFrames"]!;
            var frames = (JArray)fixture["frames"]!;
            JObject root = (JObject)fixture["root"]!;
            JObject state = (JObject)root.DeepClone();
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            int entered = 0;
            var clip = new NeoAnimationClip<TestTarget>(
                target,
                key,
                fps: 10,
                duration: expectedFrames.Count,
                target.Client.AnimationCoordinator,
                preparePlayback: () => state = (JObject)root.DeepClone(),
                applyFrame: (frameIndex, useResolvedState) =>
                {
                    if (useResolvedState)
                    {
                        state = ResolveFixtureFrame(root, frames, frameIndex);
                    }
                    else
                    {
                        JObject? sparse = frames
                            .OfType<JObject>()
                            .FirstOrDefault(frame =>
                                frame.Value<int>("index") == frameIndex)?["overrides"]
                            as JObject;
                        if (sparse is not null) MergeFixtureState(state, sparse);
                    }
                    Assert.IsTrue(
                        JToken.DeepEquals(expectedFrames[frameIndex], state),
                        $"{key} frame {frameIndex} resolved to {state}");
                    entered += 1;
                });

            play(clip);
            Tick(clip, traversal.Length - 1);

            Assert.AreEqual(traversal.Length, entered, key);
        }

        private static JObject ResolveFixtureFrame(
            JObject root,
            JArray frames,
            int frameIndex)
        {
            var state = (JObject)root.DeepClone();
            foreach (JObject frame in frames.OfType<JObject>()
                         .Where(frame => frame.Value<int>("index") <= frameIndex)
                         .OrderBy(frame => frame.Value<int>("index")))
            {
                if (frame["overrides"] is JObject sparse)
                {
                    MergeFixtureState(state, sparse);
                }
            }
            return state;
        }

        private static void MergeFixtureState(JObject state, JObject sparse)
        {
            state.Merge(
                sparse,
                new JsonMergeSettings
                {
                    MergeArrayHandling = MergeArrayHandling.Replace,
                });
        }

        private static void Tick(NeoAnimationClip<TestTarget> clip, int count)
        {
            for (int i = 0; i < count; i++) clip.Tick(0.1f);
        }

        private static NeoClient CreateClient()
        {
            string json = File.ReadAllText(Path.Combine(PackageRoot, "synth-example.json"));
            return NeoTestSaveStack.LoadClient(json);
        }

        private sealed class TestTarget : NeoGeneratedClassValue
        {
            internal TestTarget(NeoClient client)
                : base(client, client.AssetsRoot, client.AssetsRoot.member.classId) { }
        }
    }
}
