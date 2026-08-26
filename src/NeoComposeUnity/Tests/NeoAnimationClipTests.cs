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
using NeoJson = NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public sealed class NeoAnimationClipTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        /// <summary>
        /// NUnit test-case metadata only accepts literal values, and
        /// NeoPlayDirection is the SDK-shipped option-id wrapper class — so
        /// cases carry the member name and resolve it here.
        /// </summary>
        private static NeoPlayDirection DirectionByName(string name)
        {
            if (name == "Forward") return NeoPlayDirection.Forward;
            if (name == "Reverse") return NeoPlayDirection.Reverse;
            throw new ArgumentException($"Unknown direction name '{name}'.", nameof(name));
        }

        [TestCase("Forward", new[] { 0, 1, 2, 3 })]
        [TestCase("Reverse", new[] { 3, 2, 1, 0 })]
        public void PlayOnce_TraversesInRequestedDirection(
            string directionName,
            int[] expected)
        {
            NeoPlayDirection direction = DirectionByName(directionName);
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<int>();
            var clip = CreateClip(target, "object-a", 4, entered);

            clip.PlayOnce(direction);
            Tick(clip, 4);

            CollectionAssert.AreEqual(expected, entered);
            Assert.IsFalse(clip.IsPlaying);
        }

        [TestCase("Forward", new[] { 0, 1, 2, 3, 0, 1 })]
        [TestCase("Reverse", new[] { 3, 2, 1, 0, 3, 2 })]
        public void PlayLoop_RepeatWrapsWithoutStopping(
            string directionName,
            int[] expected)
        {
            NeoPlayDirection direction = DirectionByName(directionName);
            using NeoClient client = CreateClient();
            TestTarget target = new(client);
            var entered = new List<int>();
            var clip = CreateClip(target, "object-a", 4, entered);

            clip.PlayLoop(NeoPlayMode.Repeat, direction);
            Tick(clip, 5);

            CollectionAssert.AreEqual(expected, entered);
            Assert.IsTrue(clip.IsPlaying);
        }

        [TestCase("Forward", new[] { 0, 1, 2, 3, 2, 1, 0, 1 })]
        [TestCase("Reverse", new[] { 3, 2, 1, 0, 1, 2, 3, 2 })]
        public void PlayLoop_BoomerangReversesWithoutDuplicatingEnds(
            string directionName,
            int[] expected)
        {
            NeoPlayDirection direction = DirectionByName(directionName);
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
            clip.PlayOnce(NeoPlayDirection.Reverse);

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

            clip.PlayOnce(NeoPlayDirection.Reverse);

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
                clip.PlayOnce(NeoPlayDirection.Reverse));
            AssertTraversalVector(traversals, "repeatForwardWrap", clip =>
                clip.PlayLoop(NeoPlayMode.Repeat, NeoPlayDirection.Forward));
            AssertTraversalVector(traversals, "repeatBackwardWrap", clip =>
                clip.PlayLoop(NeoPlayMode.Repeat, NeoPlayDirection.Reverse));
            AssertTraversalVector(traversals, "boomerangForward", clip =>
                clip.PlayLoop(NeoPlayMode.Boomerang, NeoPlayDirection.Forward));
            AssertTraversalVector(traversals, "boomerangBackward", clip =>
                clip.PlayLoop(NeoPlayMode.Boomerang, NeoPlayDirection.Reverse));
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
            // `Offset.x` moved without `~partial` appearing: the envelope is
            // unwrapped inside the recursion, not only at the top level.
            Assert.IsNull(
                lastCollider!["Offset"]![
                    NeoJson.NeoPartialLeafValue.EnvelopeKey],
                "the ~partial envelope leaked into a resolved value");

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

        /// <summary>
        /// P42 section 7.1: every case runs across all six structured-leaf
        /// kinds. `Cell` (Vector2Int) and `Grid` (Vector3Int) merge through
        /// exactly the same path as their float counterparts and are therefore
        /// invisible in `resolvedFrames` — an int vector resolved as floats
        /// produces identical frames right up until a component lands on a
        /// fraction. `intFieldWrites` is the vector that separates them, and it
        /// is deliberately paired: the same fractional value is accepted on the
        /// float kind and rejected on the int one, so a runtime that reads an
        /// int component with <c>NeoPartialLeafValue.TryGetSingle</c> fails
        /// here rather than truncating a frame in the field.
        ///
        /// <para>Mirrors the web harness's assertions in
        /// `animation-frame-parity-fixture-coverage.test.ts`. Only the verdict
        /// is shared across the two runtimes; the diagnostic wording is
        /// not.</para>
        /// </summary>
        [Test]
        public void ParityFixture_PinsIntComponentVerdicts()
        {
            JObject fixture = JObject.Parse(
                NeoAnimationFrameResolutionParityFixture.Json);
            var writes = (JArray)fixture["intFieldWrites"]!;
            Assert.Greater(writes.Count, 0, "intFieldWrites went missing.");

            bool contested = false;
            foreach (JObject write in writes.OfType<JObject>())
            {
                string kind = write.Value<string>("kind")!;
                string field = write.Value<string>("field")!;
                bool accepted = write.Value<bool>("accepted");
                string label = $"{kind}: {write.Value<string>("label")}";

                var envelope = new JObject
                {
                    [NeoJson.NeoPartialLeafValue
                        .EnvelopeKey] = new JObject
                    {
                        [field] = write["value"]!.DeepClone(),
                    },
                };
                var leafPartial = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<
                        NeoJson.NeoPartialLeafValue>(
                            envelope.ToString(
                                Newtonsoft.Json.Formatting.None))!;

                // The int kinds are the ones whose reader rejects a fraction;
                // every other kind reads its component as a float, which is
                // why the same payload has two verdicts.
                bool readable = kind.EndsWith("Int", StringComparison.Ordinal)
                    ? leafPartial.TryGetInt32(field, out _)
                    : leafPartial.TryGetSingle(field, out _);
                Assert.AreEqual(accepted, readable, label);

                contested = contested || writes
                    .OfType<JObject>()
                    .Any(other =>
                        other.Value<string>("field") == field
                        && JToken.DeepEquals(other["value"], write["value"])
                        && other.Value<bool>("accepted") != accepted);
            }

            Assert.IsTrue(
                contested,
                "No fixture value is accepted on one kind and rejected on "
                + "another, so the verdicts prove nothing the float vectors "
                + "do not already prove.");
        }

        /// <summary>
        /// P42 sections 1.4 and 2.1 — the two composition rules the resolved
        /// frame vectors cannot express, because both describe an envelope
        /// whose composed value is indistinguishable from its base:
        ///
        /// <list type="number">
        /// <item><c>{"~partial":{}}</c> composes to the base AND authors
        /// nothing, so it must never become the frame the leaf's value is
        /// attributed to.</item>
        /// <item>A field the leaf does not carry is <b>ignored</b>, never
        /// applied — a composer that falls through to its last component
        /// writes a value nobody authored, which is why every fixture case
        /// pairs an undeclared key with a declared one on the same leaf.</item>
        /// </list>
        ///
        /// <para>Each case runs twice: through the real composer
        /// (<see cref="NeoAnimationLeafFields.Compose"/>, which is what a
        /// played clip uses) and through this file's JObject merge, which is
        /// the fixture harness's mirror of the web resolver and has to agree
        /// with the composer to be worth anything. Mirrors the web harness in
        /// `animation-frame-parity-fixture-coverage.test.ts` and
        /// `world-grid-animation-model.test.ts`.</para>
        /// </summary>
        [Test]
        public void ParityFixture_ComposesPartialFieldsLikeTheWebResolver()
        {
            JObject fixture = JObject.Parse(
                NeoAnimationFrameResolutionParityFixture.Json);
            var compositions = (JArray)fixture["partialCompositions"]!;
            Assert.Greater(compositions.Count, 0, "partialCompositions went missing.");

            var kinds = new HashSet<string>(StringComparer.Ordinal);
            bool anyUndeclared = false;
            foreach (JObject composition in compositions.OfType<JObject>())
            {
                string kindName = composition.Value<string>("kind")!;
                string label = $"{kindName}: {composition.Value<string>("label")}";
                var current = (JObject)composition["current"]!;
                var fields = (JObject)composition["fields"]!;
                var expected = (JObject)composition["composed"]!;
                bool authored = composition.Value<bool>("authored");
                kinds.Add(kindName);
                anyUndeclared = anyUndeclared || fields
                    .Properties()
                    .Any(field => current.Property(field.Name) is null);

                NeoAnimationLeafKind kind = ParityLeafKind(kindName);
                object? composed = NeoAnimationLeafFields.Compose(
                    kind,
                    ParityLeafFields(kind, fields),
                    ParityLeafRow(kind, current),
                    out string? skipReason);
                Assert.IsNull(skipReason, label);
                Assert.IsNotNull(composed, label);
                AssertLeafMatches(expected, DescribeComposedLeaf(composed!), label);

                // The harness's own merge must reach the same answer, or the
                // frame vectors above and these cases are describing two
                // different runtimes.
                JToken? merged = MergeFixtureValue(
                    current.DeepClone(),
                    new JObject
                    {
                        [NeoJson.NeoPartialLeafValue
                            .EnvelopeKey] = fields.DeepClone(),
                    });
                Assert.IsTrue(
                    JToken.DeepEquals(expected, merged),
                    $"{label}: harness merge produced {merged}");

                // An empty envelope is the "no change" form, and the compiler
                // drops it before it can claim a frame — see
                // `NeoAnimationFieldOverrideTests.ParityFixture_EmptyEnvelopeAuthorsNoWrite`
                // for that half against the real compiler.
                var envelope = new JObject
                {
                    [NeoJson.NeoPartialLeafValue
                        .EnvelopeKey] = fields.DeepClone(),
                };
                Assert.AreEqual(
                    authored,
                    !NeoJson.NeoPartialLeafValue
                        .FromEnvelope(envelope).IsEmpty,
                    label);
            }

            Assert.IsTrue(
                anyUndeclared,
                "No fixture case writes a field the leaf does not carry, so the "
                + "section no longer pins the rule it exists for.");
            Assert.AreEqual(
                6,
                kinds.Count,
                "P42 section 7.1 wants every case across all six structured-leaf kinds.");
        }

        private static NeoAnimationLeafKind ParityLeafKind(string kindName)
        {
            return kindName switch
            {
                "Sprite" => NeoAnimationLeafKind.Sprite,
                "Vector2" => NeoAnimationLeafKind.Vector2,
                "Vector2Int" => NeoAnimationLeafKind.Vector2Int,
                "Vector3" => NeoAnimationLeafKind.Vector3,
                "Vector3Int" => NeoAnimationLeafKind.Vector3Int,
                "Color" => NeoAnimationLeafKind.Color,
                _ => throw new AssertionException(
                    $"The fixture names kind '{kindName}', which is not a structured leaf kind."),
            };
        }

        /// <summary>
        /// Builds the field list the composer receives, deliberately WITHOUT
        /// going through <see cref="NeoAnimationLeafFields.Compile"/>: Compile
        /// rejects an undeclared key at export-validation time, which is the
        /// first layer and is asserted separately. These cases are about the
        /// second layer — what the composer does with a field list that
        /// reached apply time anyway.
        /// </summary>
        private static List<NeoAnimationLeafFieldValue> ParityLeafFields(
            NeoAnimationLeafKind kind,
            JObject fields)
        {
            var compiled = new List<NeoAnimationLeafFieldValue>(fields.Count);
            foreach (JProperty field in fields.Properties())
            {
                bool isFileId = kind == NeoAnimationLeafKind.Sprite
                    && string.Equals(
                        field.Name,
                        NeoAnimationLeafFields.FileIdKey,
                        StringComparison.Ordinal);
                compiled.Add(isFileId
                    ? NeoAnimationLeafFieldValue.OfText(
                        field.Name, field.Value.Value<string>())
                    : NeoAnimationLeafFieldValue.OfNumber(
                        field.Name, field.Value.Value<double>()));
            }
            return compiled;
        }

        private static NeoJson.MemberValue ParityLeafRow(NeoAnimationLeafKind kind, JObject current)
        {
            switch (kind)
            {
                case NeoAnimationLeafKind.Sprite:
                    return new NeoJson.SpriteMemberValue
                    {
                        value = new NeoJson.SpriteValue
                        {
                            fileId = current.Value<string>("fileId")!,
                            sliceIndex = current.Value<int>("sliceIndex"),
                        },
                    };
                case NeoAnimationLeafKind.Vector2:
                case NeoAnimationLeafKind.Vector2Int:
                    return new NeoJson.Vector2MemberValue
                    {
                        value = new NeoJson.NeoVector2Value
                        {
                            x = current.Value<float>("x"),
                            y = current.Value<float>("y"),
                        },
                    };
                case NeoAnimationLeafKind.Vector3:
                case NeoAnimationLeafKind.Vector3Int:
                    return new NeoJson.Vector3MemberValue
                    {
                        value = new NeoJson.NeoVector3Value
                        {
                            x = current.Value<float>("x"),
                            y = current.Value<float>("y"),
                            z = current.Value<float>("z"),
                        },
                    };
                default:
                    return new NeoJson.ColorMemberValue
                    {
                        value = new NeoJson.NeoColorValue
                        {
                            r = current.Value<float>("r"),
                            g = current.Value<float>("g"),
                            b = current.Value<float>("b"),
                            a = current.Value<float>("a"),
                        },
                    };
            }
        }

        /// <summary>
        /// The composed value as a JSON record, so the fixture's expectation
        /// can be compared key by key. <see cref="NeoJson.NeoVector3Value"/> derives
        /// from <see cref="NeoJson.NeoVector2Value"/>, so it has to be matched first.
        /// </summary>
        private static JObject DescribeComposedLeaf(object composed)
        {
            switch (composed)
            {
                case NeoJson.SpriteValue sprite:
                    return new JObject
                    {
                        ["fileId"] = sprite.fileId,
                        ["sliceIndex"] = sprite.sliceIndex,
                    };
                case NeoJson.NeoVector3Value vector3:
                    return new JObject
                    {
                        ["x"] = vector3.x,
                        ["y"] = vector3.y,
                        ["z"] = vector3.z,
                    };
                case NeoJson.NeoVector2Value vector2:
                    return new JObject { ["x"] = vector2.x, ["y"] = vector2.y };
                case NeoJson.NeoColorValue color:
                    return new JObject
                    {
                        ["r"] = color.r,
                        ["g"] = color.g,
                        ["b"] = color.b,
                        ["a"] = color.a,
                    };
                default:
                    throw new AssertionException(
                        $"Composed an unexpected leaf type '{composed.GetType().Name}'.");
            }
        }

        /// <summary>
        /// Compares key by key rather than with <c>DeepEquals</c>: the fixture
        /// writes whole components as JSON integers and the composer produces
        /// floats, and <c>DeepEquals</c> calls those two different values.
        /// </summary>
        private static void AssertLeafMatches(JObject expected, JObject actual, string label)
        {
            Assert.AreEqual(expected.Count, actual.Count, $"{label}: key count");
            foreach (JProperty property in expected.Properties())
            {
                JToken? got = actual[property.Name];
                Assert.IsNotNull(got, $"{label}: composed leaf has no '{property.Name}'");
                if (property.Value.Type == JTokenType.String)
                {
                    Assert.AreEqual(
                        property.Value.Value<string>(),
                        got!.Value<string>(),
                        $"{label}: {property.Name}");
                    continue;
                }
                Assert.AreEqual(
                    property.Value.Value<double>(),
                    got!.Value<double>(),
                    1e-5,
                    $"{label}: {property.Name}");
            }
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
                clip.PlayOnce(NeoPlayDirection.Reverse));
            AssertResolvedFrameVector(fixture, traversals, rootKey, expectedFramesKey, "repeatForwardWrap", clip =>
                clip.PlayLoop(NeoPlayMode.Repeat, NeoPlayDirection.Forward));
            AssertResolvedFrameVector(fixture, traversals, rootKey, expectedFramesKey, "repeatBackwardWrap", clip =>
                clip.PlayLoop(NeoPlayMode.Repeat, NeoPlayDirection.Reverse));
            AssertResolvedFrameVector(fixture, traversals, rootKey, expectedFramesKey, "boomerangForward", clip =>
                clip.PlayLoop(NeoPlayMode.Boomerang, NeoPlayDirection.Forward));
            AssertResolvedFrameVector(fixture, traversals, rootKey, expectedFramesKey, "boomerangBackward", clip =>
                clip.PlayLoop(NeoPlayMode.Boomerang, NeoPlayDirection.Reverse));
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
        /// `JObject.Merge` when P42 put the `~partial` envelope in the fixture:
        /// Newtonsoft's merge has no idea what the envelope means and would
        /// leave a literal `~partial` key sitting in the resolved state, so the
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
                    // A field write can only overwrite a key the leaf already
                    // carries. A valid whole leaf carries every field its kind
                    // declares, so "already present" and "declared by the kind"
                    // name the same set — and this mirror needs no kind, just
                    // like the web `applyStructuredLeafPartial` it copies.
                    if (leaf.Property(field.Name) is null) continue;
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
        /// exactly one key, named <c>~partial</c>, whose value is an object.
        /// Anything else is an ordinary record and merges as one.
        /// </summary>
        private static JObject? PartialEnvelopeFields(JToken value)
        {
            if (value is not JObject envelope) return null;
            if (envelope.Count != 1) return null;
            return envelope[
                NeoJson.NeoPartialLeafValue.EnvelopeKey]
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
