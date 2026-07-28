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
            AssertParityResolvedFrames("root", "resolvedFrames");
        }

        /// <summary>
        /// P42 section 1.4: "as it stands" means at apply time on the played
        /// instance. The same `frames` chain resolved against a second
        /// placement — a different sprite sheet, a `Position.z`, a
        /// `Collider.Offset` and a `Tint` the clip never writes — must land on
        /// `boundResolvedFrames`, not on the authored default's frames. A
        /// runtime that composes a field override against the authored default
        /// instead of the current value passes the test above and fails this
        /// one.
        /// </summary>
        [Test]
        public void Playback_MatchesCrossRuntimeParityBoundPlacementFrames()
        {
            AssertParityResolvedFrames("boundRoot", "boundResolvedFrames");
        }

        /// <summary>
        /// Pins what the resolved-frame vectors exist to prove. `DeepEquals`
        /// over the whole state would still pass if the fixture quietly lost
        /// the rows that carry the P42 semantics, so name them: the nested
        /// class merges key-wise, the envelope is unwrapped at depth 2 and
        /// never survives into a resolved value, and the two placements
        /// disagree exactly where the clip never wrote. This mirrors the web
        /// harness's assertions in
        /// `world-grid-animation-model.test.ts`.
        /// </summary>
        [Test]
        public void ParityFixture_PinsFieldOverrideAndBoundPlacementVectors()
        {
            JObject fixture = JObject.Parse(
                NeoAnimationFrameResolutionParityFixture.Json);
            var resolved = (JArray)fixture["resolvedFrames"]!;
            var bound = (JArray)fixture["boundResolvedFrames"]!;
            JObject boundRoot = (JObject)fixture["boundRoot"]!;

            // Frame 1 writes only `Collider.Offset` and frame 3 only
            // `Collider.Enabled`, so both keys surviving at frame 3 is the
            // subset-record merge a nested CLASS is entitled to (decision D1
            // bans bare subsets for structured LEAVES only).
            JToken? lastCollider = resolved[3]["Collider"];
            Assert.IsTrue(
                JToken.DeepEquals(
                    JObject.Parse(@"{ ""Enabled"": false, ""Offset"": { ""x"": 1, ""y"": 0 } }"),
                    lastCollider),
                $"resolvedFrames[3].Collider was {lastCollider}");
            // `Offset.x` moved without `$partial` appearing: the envelope is
            // unwrapped inside the recursion, not only at the top level.
            Assert.IsNull(
                lastCollider!["Offset"]![
                    global::NeoCompose.Runtime.Json.NeoPartialLeafValue.EnvelopeKey],
                "the $partial envelope leaked into a resolved value");

            // The bound sheet survives the slice-only frame, and the z the clip
            // never touches stays the placement's.
            Assert.AreEqual(
                boundRoot["Sprite"]!.Value<string>("fileId"),
                bound[1]!["Sprite"]!.Value<string>("fileId"));
            Assert.AreNotEqual(
                resolved[1]!["Sprite"]!.Value<string>("fileId"),
                bound[1]!["Sprite"]!.Value<string>("fileId"));
            Assert.AreEqual(
                boundRoot["Position"]!.Value<float>("z"),
                bound[3]!["Position"]!.Value<float>("z"));
            // Same rule two levels down: the nested field write moves `x` on
            // both placements and leaves each placement's own `y` alone.
            Assert.AreEqual(
                1f,
                bound[1]!["Collider"]!["Offset"]!.Value<float>("x"));
            Assert.AreEqual(
                boundRoot["Collider"]!["Offset"]!.Value<float>("y"),
                bound[1]!["Collider"]!["Offset"]!.Value<float>("y"));
            Assert.AreNotEqual(
                resolved[1]!["Collider"]!["Offset"]!.Value<float>("y"),
                bound[1]!["Collider"]!["Offset"]!.Value<float>("y"));
        }

        private static void AssertParityResolvedFrames(
            string rootKey,
            string expectedFramesKey)
        {
            JObject fixture = JObject.Parse(
                NeoAnimationFrameResolutionParityFixture.Json);
            JObject traversals = (JObject)fixture["traversals"]!;

            AssertResolvedFrameVector(fixture, traversals, rootKey, expectedFramesKey, "onceForward", clip =>
                clip.PlayOnce(NeoPlayDirection.Forward));
            AssertResolvedFrameVector(fixture, traversals, rootKey, expectedFramesKey, "onceBackward", clip =>
                clip.PlayOnce(NeoPlayDirection.Backward));
            AssertResolvedFrameVector(fixture, traversals, rootKey, expectedFramesKey, "repeatForwardWrap", clip =>
                clip.PlayLoop(NeoPlayMode.Repeat, NeoPlayDirection.Forward));
            AssertResolvedFrameVector(fixture, traversals, rootKey, expectedFramesKey, "repeatBackwardWrap", clip =>
                clip.PlayLoop(NeoPlayMode.Repeat, NeoPlayDirection.Backward));
            AssertResolvedFrameVector(fixture, traversals, rootKey, expectedFramesKey, "boomerangForward", clip =>
                clip.PlayLoop(NeoPlayMode.Boomerang, NeoPlayDirection.Forward));
            AssertResolvedFrameVector(fixture, traversals, rootKey, expectedFramesKey, "boomerangBackward", clip =>
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
            string rootKey,
            string expectedFramesKey,
            string key,
            Action<NeoAnimationClip<TestTarget>> play)
        {
            int[] traversal = traversals[key]!.ToObject<int[]>()!;
            var expectedFrames = (JArray)fixture[expectedFramesKey]!;
            var frames = (JArray)fixture["frames"]!;
            JObject root = (JObject)fixture[rootKey]!;
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
                        $"{rootKey}/{key} frame {frameIndex} resolved to {state}");
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

        /// <summary>
        /// Deep record merge, mirroring the web resolver's `mergeSparseValue`
        /// (`world-grid-animation-model.ts`) key for key. This replaced
        /// `JObject.Merge` when P42 put the `$partial` envelope in the fixture:
        /// Newtonsoft's merge has no idea what the envelope means and would
        /// leave a literal `$partial` key sitting in the resolved state, so the
        /// two runtimes would stop agreeing about what the vectors even say.
        /// </summary>
        private static void MergeFixtureState(JObject state, JObject sparse)
        {
            foreach (JProperty property in sparse.Properties())
            {
                JToken? merged = MergeFixtureValue(
                    state[property.Name], property.Value);
                // P42 section 1.4, "a null leaf at apply time": a field write
                // onto something that is not a record has nothing to compose
                // against, so the write is skipped and the previous value
                // stands. Assigning here would invent a base value.
                if (merged is null) continue;
                state[property.Name] = merged;
            }
        }

        private static JToken? MergeFixtureValue(JToken? current, JToken value)
        {
            JObject? partialFields = PartialEnvelopeFields(value);
            if (partialFields is not null)
            {
                // P42 section 1.2: a field override is a read-modify-write of
                // the WHOLE leaf, and "the rest" comes from the value as it
                // stands on the played instance.
                if (current is not JObject leaf) return null;
                var patched = (JObject)leaf.DeepClone();
                foreach (JProperty field in partialFields.Properties())
                {
                    patched[field.Name] = field.Value.DeepClone();
                }
                return patched;
            }

            // A full leaf value is not an envelope: every key present means
            // every key is replaced, which is the whole-leaf override P42
            // section 1.3 promises. Nested CLASS records still merge key-wise.
            if (value is not JObject record) return value.DeepClone();
            JObject next = current is JObject baseRecord
                ? (JObject)baseRecord.DeepClone()
                : new JObject();
            foreach (JProperty property in record.Properties())
            {
                JToken? merged = MergeFixtureValue(
                    next[property.Name], property.Value);
                if (merged is null) continue;
                next[property.Name] = merged;
            }
            return next;
        }

        /// <summary>
        /// The envelope probe, matching the web `isStructuredLeafPartialValue`:
        /// exactly one key, named <c>$partial</c>, whose value is an object.
        /// Anything else is an ordinary record and merges as one.
        /// </summary>
        private static JObject? PartialEnvelopeFields(JToken value)
        {
            if (value is not JObject envelope) return null;
            if (envelope.Count != 1) return null;
            return envelope[
                global::NeoCompose.Runtime.Json.NeoPartialLeafValue.EnvelopeKey]
                as JObject;
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
