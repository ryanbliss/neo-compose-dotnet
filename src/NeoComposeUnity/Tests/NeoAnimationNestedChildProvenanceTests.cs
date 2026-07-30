// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P44 §5.4 — a clip declared on a class resolves onto that class when it is
    /// used as a NESTED <c>Children</c> row, at any depth.
    ///
    /// <para>The runtime already supported this: <c>ResolvePlacedChild</c> scans
    /// one node's <c>Children</c> and matches <c>sourceValueId</c> exactly, and
    /// <c>CompileTracks</c> recurses into the resolved row's own graph.
    /// What P44 adds is the stamp — the web and CLI materialization paths now
    /// write <c>sourceValueId</c> on every row they copy out of a class default,
    /// so the class-default ids a clip addresses finally exist on nested
    /// instances. These fixtures stamp the rows by hand exactly as those paths
    /// now emit them, and assert the resolution they light up.</para>
    ///
    /// <para>The fixture is hand-built rather than spawned through the tile grid:
    /// P44's case is authoring-time nesting, which never goes through the
    /// placement clone. One class, <c>part-class</c>, plays every level — its
    /// <c>Children</c> entry member is itself <c>part-class</c> — and it carries
    /// three clips so a track can descend a level per clip key.</para>
    /// </summary>
    public class NeoAnimationNestedChildProvenanceTests
    {
        // ------------------------------------------------------------------
        // The headline case: a clip declared on a class, played on a nested row.
        // ------------------------------------------------------------------

        [Test]
        public void NestedPartClip_ResolvesItsOwnChildOverridesTwoLevelsDeep()
        {
            using NeoClient client = BuildClient(
                Part(
                    "placement-value",
                    0f,
                    stamp: null,
                    Part(
                        "eye",
                        0f,
                        DefaultChildA,
                        Part("sclera", 1f, DefaultChildA),
                        Part("iris", 2f, DefaultChildB))));
            using PartTarget eye = OpenPart(client, 0);

            NeoAnimationDefinition definition = NeoAnimationCompiler.Compile(eye, "Blink");
            definition.PreparePlayback();
            definition.ApplyFrame(0, useResolvedState: false);

            // 'Blink' addresses the CLASS-DEFAULT row id; the stamp is what makes
            // it land on this nested copy of it.
            Assert.AreEqual(9f, PositionOf(client, "sclera"), 1e-5f);
            Assert.AreEqual(2f, PositionOf(client, "iris"), 1e-5f);
            Assert.AreEqual(
                DefaultChildAPosition,
                PositionOf(client, DefaultChildA),
                1e-5f,
                "the class default itself must never be written");
        }

        [Test]
        public void NestedPartClip_ResolvesThroughThreeLevelsOfChildTracks()
        {
            // placement -> eyes -> eye -> sclera, every level stamped.
            using NeoClient client = BuildClient(
                Part(
                    "placement-value",
                    0f,
                    stamp: null,
                    Part(
                        "eyes",
                        0f,
                        DefaultChildA,
                        Part(
                            "eye",
                            0f,
                            DefaultChildA,
                            Part("sclera", 1f, DefaultChildA),
                            Part("iris", 2f, DefaultChildB)))));
            using PartTarget placement = OpenPart(client);

            // 'Twitch' tracks 'Wink' on the child, which tracks 'Blink' on ITS
            // child, whose child override finally writes a leaf.
            NeoAnimationDefinition definition = NeoAnimationCompiler.Compile(placement, "Twitch");
            definition.PreparePlayback();
            definition.ApplyFrame(0, useResolvedState: false);

            Assert.AreEqual(9f, PositionOf(client, "sclera"), 1e-5f);
            Assert.AreEqual(2f, PositionOf(client, "iris"), 1e-5f);
        }

        [Test]
        public void SiblingRowsFromOneClassDefault_AnimateIndependently()
        {
            // Both nested rows were materialized from the same class default, so
            // their children carry IDENTICAL stamps. Spec §2: that is not an
            // ambiguity, because resolution is scoped to one node's Children.
            using NeoClient client = BuildClient(
                Part(
                    "placement-value",
                    0f,
                    stamp: null,
                    Part(
                        "left",
                        0f,
                        DefaultChildA,
                        Part("left-sclera", 1f, DefaultChildA),
                        Part("left-iris", 2f, DefaultChildB)),
                    Part(
                        "right",
                        0f,
                        DefaultChildB,
                        Part("right-sclera", 3f, DefaultChildA),
                        Part("right-iris", 4f, DefaultChildB))));
            using PartTarget left = OpenPart(client, 0);
            using PartTarget right = OpenPart(client, 1);

            NeoAnimationDefinition leftBlink = NeoAnimationCompiler.Compile(left, "Blink");
            leftBlink.PreparePlayback();
            leftBlink.ApplyFrame(0, useResolvedState: false);

            // One side animated; the other must be untouched at this point.
            Assert.AreEqual(9f, PositionOf(client, "left-sclera"), 1e-5f);
            Assert.AreEqual(3f, PositionOf(client, "right-sclera"), 1e-5f);

            NeoAnimationDefinition rightBlink = NeoAnimationCompiler.Compile(right, "Blink");
            rightBlink.PreparePlayback();
            rightBlink.ApplyFrame(0, useResolvedState: false);

            Assert.AreEqual(9f, PositionOf(client, "left-sclera"), 1e-5f);
            Assert.AreEqual(9f, PositionOf(client, "right-sclera"), 1e-5f);
            Assert.AreEqual(2f, PositionOf(client, "left-iris"), 1e-5f);
            Assert.AreEqual(4f, PositionOf(client, "right-iris"), 1e-5f);
            Assert.AreEqual(
                DefaultChildAPosition,
                PositionOf(client, DefaultChildA),
                1e-5f,
                "the shared class default must never be written");
        }

        [Test]
        public void TwoIdenticallyStampedRowsOnOneNode_StillThrowAsAmbiguous()
        {
            using NeoClient client = BuildClient(
                Part(
                    "placement-value",
                    0f,
                    stamp: null,
                    Part(
                        "eye",
                        0f,
                        DefaultChildA,
                        Part("sclera", 1f, DefaultChildA),
                        Part("sclera-duplicate", 2f, DefaultChildA))));
            using PartTarget eye = OpenPart(client, 0);

            var error = Assert.Throws<InvalidOperationException>(
                () => NeoAnimationCompiler.Compile(eye, "Blink"));

            StringAssert.Contains("maps to multiple placed Children rows", error!.Message);
        }

        // ------------------------------------------------------------------
        // P44 decision D6 — the legacy heuristic narrows to "not one row is
        // stamped", because mixed nodes are now a normal steady state.
        // ------------------------------------------------------------------

        [Test]
        public void NodeWhereNoRowCarriesProvenance_StillThrowsTheLegacyMigrationError()
        {
            using NeoClient client = BuildClient(
                Part(
                    "placement-value",
                    0f,
                    stamp: null,
                    Part(
                        "eye",
                        0f,
                        stamp: null,
                        Part("sclera", 1f, stamp: null),
                        Part("iris", 2f, stamp: null))));
            using PartTarget eye = OpenPart(client, 0);

            var error = Assert.Throws<InvalidOperationException>(
                () => NeoAnimationCompiler.Compile(eye, "Blink"));

            StringAssert.Contains("legacy pre-0.7 placement", error!.Message);
            StringAssert.Contains(
                "none of its Children rows carry sourceValueId",
                error.Message);
            StringAssert.Contains("Migrate or recreate", error.Message);
        }

        [Test]
        public void MixedStampedAndUnstampedNode_SkipsAndWarnsInsteadOfThrowingLegacy()
        {
            // One materialized row plus one explicitly authored row, which P44
            // §1.2 leaves unstamped on purpose. 'Blink' addresses a slot this
            // node does not have, so P41's skip applies — the node is not legacy
            // data just because one of its rows carries no stamp.
            using NeoClient client = BuildClient(
                Part(
                    "placement-value",
                    0f,
                    stamp: null,
                    Part(
                        "eye",
                        0f,
                        DefaultChildA,
                        Part("iris", 1f, DefaultChildB),
                        Part("authored-extra", 2f, stamp: null))));
            using PartTarget eye = OpenPart(client, 0);
            LogAssert.Expect(
                LogType.Warning,
                new Regex("child override skipped: no placed Children row"));

            NeoAnimationDefinition definition = NeoAnimationCompiler.Compile(eye, "Blink");
            definition.PreparePlayback();
            definition.ApplyFrame(0, useResolvedState: false);

            Assert.AreEqual(1f, PositionOf(client, "iris"), 1e-5f);
            Assert.AreEqual(2f, PositionOf(client, "authored-extra"), 1e-5f);
            // Compile-time, once per reference: ticking again logs nothing more.
            definition.ApplyFrame(0, useResolvedState: false);
            LogAssert.NoUnexpectedReceived();
        }

        // ------------------------------------------------------------------
        // Opening a node as an animation target.
        // ------------------------------------------------------------------

        /// <summary>
        /// Wraps an arbitrary <see cref="NeoMemberClass"/> as an animation
        /// target, standing in for the generated class the codegen emits.
        /// </summary>
        private sealed class PartTarget : NeoGeneratedClassValue
        {
            internal PartTarget(NeoClient client, NeoMemberClass node, bool isReadOnly = false)
                : base(client, node, node.member.classId, isReadOnly)
            {
            }
        }

        /// <summary>
        /// Opens the placement, then descends <paramref name="childIndexes"/>
        /// through <c>Children</c> — the nesting P44 is about.
        /// </summary>
        private static PartTarget OpenPart(NeoClient client, params int[] childIndexes)
        {
            NeoMemberClass node = client.assets.Get<NeoMemberClass>("Placement");
            foreach (int index in childIndexes)
            {
                NeoMemberList children = node.Get<NeoMemberList>("Children");
                if (children[index] is not NeoMemberClass child)
                {
                    throw new AssertionException(
                        $"Children[{index}] on '{node.value?.id}' is not a Class row.");
                }
                node = child;
            }
            return new PartTarget(client, node);
        }

        private static float PositionOf(NeoClient client, string partId)
        {
            MemberValue? row = client.ResolveEffectiveRow(PositionId(partId));
            if (row is not Vector3MemberValue vector || vector.value is null)
            {
                throw new AssertionException($"'{partId}' has no Vector3 Position row.");
            }
            return vector.value.x;
        }

        // ------------------------------------------------------------------
        // Fixture: one composable class nested into itself, carrying three
        // clips so a child track can descend one level per clip key.
        // ------------------------------------------------------------------

        private const string ProjectId = "project-p44";
        private const string PartClassId = "part-class";
        private const string ClipClassId = "clip-class";
        private const string FrameClassId = "frame-class";
        private const string ChildOverrideClassId = "child-override-class";
        private const string TrackClassId = "track-class";
        private const string RootClassId = "root-class";
        private const string EmptyClassId = "empty-class";

        /// <summary>The class-default <c>Children</c> rows every stamp names.</summary>
        private const string DefaultChildA = "part-default-child-a";
        private const string DefaultChildB = "part-default-child-b";
        private const float DefaultChildAPosition = 100f;
        private const float DefaultChildBPosition = 200f;

        /// <summary>The x every clip writes, so an assertion cannot pass by accident.</summary>
        private const float AnimatedPosition = 9f;

        private sealed class PartSpec
        {
            internal string Id = "";
            internal float Position;
            internal string? Stamp;
            internal PartSpec[] Children = Array.Empty<PartSpec>();
        }

        private static PartSpec Part(
            string id,
            float position,
            string? stamp,
            params PartSpec[] children)
        {
            return new PartSpec
            {
                Id = id,
                Position = position,
                Stamp = stamp,
                Children = children,
            };
        }

        private static string PositionId(string partId) => $"{partId}-position";

        private static string ChildrenId(string partId) => $"{partId}-children";

        private static NeoClient BuildClient(PartSpec placement)
        {
            NeoClient client = NeoTestSaveStack.ClientFromSchema(BuildProjectData(placement));
            // A child TRACK resolves the nested row through the generated
            // factory registry, so it must be populated before compiling one.
            client.RegisterGeneratedClassFactories(
                new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
                {
                    [PartClassId] = (resolved, node) =>
                        NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                            resolved,
                            node,
                            () => new PartTarget(resolved, node, isReadOnly: true)),
                },
                new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
                {
                    [PartClassId] = (resolved, node) =>
                        NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                            resolved,
                            node,
                            () => new PartTarget(resolved, node)),
                });
            return client;
        }

        private static ProjectData BuildProjectData(PartSpec placement)
        {
            var members = new Dictionary<string, Member>();
            var values = new Dictionary<string, MemberValue>();

            // --- classes -------------------------------------------------
            var partClass = new NeoSchemaClass
            {
                id = PartClassId,
                projectId = ProjectId,
                name = "Part",
                schema = new Dictionary<string, string>
                {
                    ["Position"] = "part-position-member",
                    ["Children"] = "part-children-member",
                    ["Blink"] = "part-blink-member",
                    ["Wink"] = "part-wink-member",
                    ["Twitch"] = "part-twitch-member",
                },
            };
            var clipClass = new NeoSchemaClass
            {
                id = ClipClassId,
                projectId = ProjectId,
                name = "Clip",
                system = Newtonsoft.Json.Linq.JObject.Parse(
                    "{\"worldKind\":\"animationClip\"}"),
                schema = new Dictionary<string, string>
                {
                    ["FPS"] = "clip-fps-member",
                    ["Duration"] = "clip-duration-member",
                    ["Frames"] = "clip-frames-member",
                    ["Tracks"] = "clip-tracks-member",
                },
            };
            var frameClass = new NeoSchemaClass
            {
                id = FrameClassId,
                projectId = ProjectId,
                name = "Frame",
                schema = new Dictionary<string, string>
                {
                    ["Index"] = "frame-index-member",
                    ["Overrides"] = "frame-overrides-member",
                    ["ChildOverrides"] = "frame-child-overrides-member",
                },
            };
            var childOverrideClass = new NeoSchemaClass
            {
                id = ChildOverrideClassId,
                projectId = ProjectId,
                name = "Child Override",
                schema = new Dictionary<string, string>
                {
                    ["Child"] = "child-override-child-member",
                    ["Overrides"] = "child-override-values-member",
                },
            };
            var trackClass = new NeoSchemaClass
            {
                id = TrackClassId,
                projectId = ProjectId,
                name = "Child Track",
                // P48 §2.2 dispatches a Tracks row by its own class's world
                // kind, so a child track has to say it is one.
                system = Newtonsoft.Json.Linq.JObject.Parse(
                    "{\"worldKind\":\"animationChildTrack\"}"),
                schema = new Dictionary<string, string>
                {
                    ["Child"] = "track-child-member",
                    ["ClipKey"] = "track-key-member",
                    ["StartFrame"] = "track-start-member",
                },
            };
            var rootClass = new NeoSchemaClass
            {
                id = RootClassId,
                projectId = ProjectId,
                name = "Root",
                schema = new Dictionary<string, string>
                {
                    ["Placement"] = "placement-member",
                },
            };
            var emptyClass = new NeoSchemaClass
            {
                id = EmptyClassId,
                projectId = ProjectId,
                name = "Empty",
                schema = new Dictionary<string, string>(),
            };

            // --- members -------------------------------------------------
            members["root-assets"] = ClassMemberOf(
                "root-assets", "Assets", RootClassId, "root-assets-value");
            members["root-save"] = ClassMemberOf(
                "root-save", "Save", EmptyClassId, "root-save-value");
            members["root-session"] = ClassMemberOf(
                "root-session", "Session", EmptyClassId, "root-session-value");
            members["placement-member"] = ClassMemberOf(
                "placement-member", "Placement", PartClassId, valueId: null);

            members["part-position-member"] = new Vector3Member
            {
                id = "part-position-member",
                projectId = ProjectId,
                name = "Position",
                kind = MemberKind.Vector3,
                storage = "save",
            };
            members["part-children-member"] = new ListMember
            {
                id = "part-children-member",
                projectId = ProjectId,
                name = "Children",
                kind = MemberKind.List,
                entryMemberId = "part-child-entry-member",
                valueId = "part-default-children",
            };
            members["part-child-entry-member"] = ClassMemberOf(
                "part-child-entry-member", "Child", PartClassId, valueId: null);
            members["part-blink-member"] = ClipMemberOf(
                "part-blink-member", "Blink", "blink-clip");
            members["part-wink-member"] = ClipMemberOf(
                "part-wink-member", "Wink", "wink-clip");
            members["part-twitch-member"] = ClipMemberOf(
                "part-twitch-member", "Twitch", "twitch-clip");

            members["clip-fps-member"] = IntMemberOf("clip-fps-member", "FPS");
            members["clip-duration-member"] = IntMemberOf("clip-duration-member", "Duration");
            members["clip-frames-member"] = ListMemberOf(
                "clip-frames-member", "Frames", "clip-frame-entry-member");
            members["clip-frame-entry-member"] = ClassMemberOf(
                "clip-frame-entry-member", "Frame", FrameClassId, valueId: null);
            members["clip-tracks-member"] = ListMemberOf(
                "clip-tracks-member", "Tracks", "clip-track-entry-member");
            members["clip-track-entry-member"] = ClassMemberOf(
                "clip-track-entry-member", "Track", TrackClassId, valueId: null);

            members["frame-index-member"] = IntMemberOf("frame-index-member", "Index");
            members["frame-overrides-member"] = PartialClassMemberOf(
                "frame-overrides-member", "Overrides");
            members["frame-child-overrides-member"] = ListMemberOf(
                "frame-child-overrides-member",
                "ChildOverrides",
                "frame-child-override-entry-member");
            members["frame-child-override-entry-member"] = ClassMemberOf(
                "frame-child-override-entry-member",
                "ChildOverride",
                ChildOverrideClassId,
                valueId: null);

            members["child-override-child-member"] = LookupMemberOf(
                "child-override-child-member", "Child");
            members["child-override-values-member"] = PartialClassMemberOf(
                "child-override-values-member", "Overrides");

            members["track-child-member"] = LookupMemberOf("track-child-member", "Child");
            members["track-key-member"] = new StringMember
            {
                id = "track-key-member",
                projectId = ProjectId,
                name = "ClipKey",
                kind = MemberKind.String,
                localizable = false,
            };
            members["track-start-member"] = IntMemberOf("track-start-member", "StartFrame");

            // --- the class default a nested row is a copy of ---------------
            values["part-default-children"] = ArrayRow(
                "part-default-children",
                DefaultChildA,
                DefaultChildB);
            values["part-default-empty-children"] = ArrayRow("part-default-empty-children");
            AddDefaultChild(values, DefaultChildA, DefaultChildAPosition);
            AddDefaultChild(values, DefaultChildB, DefaultChildBPosition);

            // --- clips ----------------------------------------------------
            // 'Blink' writes one of its own class-default children directly.
            AddClip(values, "blink-clip", fps: 10, duration: 1, hasFrame: true, trackClipKey: null);
            // 'Wink' tracks 'Blink' on a child, and 'Twitch' tracks 'Wink' on a
            // child — one level of nesting per clip key.
            AddClip(values, "wink-clip", fps: 10, duration: 2, hasFrame: false, trackClipKey: "Blink");
            AddClip(values, "twitch-clip", fps: 10, duration: 3, hasFrame: false, trackClipKey: "Wink");

            // --- the placement graph under test ---------------------------
            AddPart(values, placement);
            values["root-assets-value"] = Record(
                "root-assets-value",
                RootClassId,
                new Dictionary<string, string> { ["Placement"] = placement.Id });
            values["root-save-value"] = Record(
                "root-save-value",
                EmptyClassId,
                new Dictionary<string, string>());
            values["root-session-value"] = Record(
                "root-session-value",
                EmptyClassId,
                new Dictionary<string, string>());

            return new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    _id = ProjectId,
                    name = "P44 Nested Child Provenance",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = members,
                values = values,
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [partClass.id] = partClass,
                    [clipClass.id] = clipClass,
                    [frameClass.id] = frameClass,
                    [childOverrideClass.id] = childOverrideClass,
                    [trackClass.id] = trackClass,
                    [rootClass.id] = rootClass,
                    [emptyClass.id] = emptyClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        /// <summary>
        /// Writes one part row and its descendants. Every row maps
        /// <c>Children</c> explicitly, so nothing falls back to the class
        /// default — which is what eager materialization guarantees in real
        /// data, and what P44 §5.3 pins.
        /// </summary>
        private static void AddPart(Dictionary<string, MemberValue> values, PartSpec spec)
        {
            values[PositionId(spec.Id)] = Vector(PositionId(spec.Id), spec.Position);
            var entryIds = new List<string>(spec.Children.Length);
            foreach (PartSpec child in spec.Children)
            {
                AddPart(values, child);
                entryIds.Add(child.Id);
            }
            values[ChildrenId(spec.Id)] = ArrayRow(ChildrenId(spec.Id), entryIds.ToArray());
            values[spec.Id] = new ObjectMemberValue
            {
                id = spec.Id,
                classId = PartClassId,
                sourceValueId = spec.Stamp,
                value = new Dictionary<string, string>
                {
                    ["Position"] = PositionId(spec.Id),
                    ["Children"] = ChildrenId(spec.Id),
                },
            };
        }

        private static void AddDefaultChild(
            Dictionary<string, MemberValue> values,
            string id,
            float position)
        {
            values[PositionId(id)] = Vector(PositionId(id), position);
            values[id] = Record(
                id,
                PartClassId,
                new Dictionary<string, string>
                {
                    ["Position"] = PositionId(id),
                    ["Children"] = "part-default-empty-children",
                });
        }

        /// <summary>
        /// One clip: either a single frame whose child override writes
        /// <see cref="DefaultChildA"/>'s Position, or a single track that plays
        /// <paramref name="trackClipKey"/> on that same child.
        /// </summary>
        private static void AddClip(
            Dictionary<string, MemberValue> values,
            string clipId,
            int fps,
            int duration,
            bool hasFrame,
            string? trackClipKey)
        {
            values[$"{clipId}-fps"] = Number($"{clipId}-fps", fps);
            values[$"{clipId}-duration"] = Number($"{clipId}-duration", duration);

            var frameIds = new List<string>();
            if (hasFrame)
            {
                values[$"{clipId}-frame-index"] = Number($"{clipId}-frame-index", 0);
                values[$"{clipId}-child-lookup"] = ArrayRow(
                    $"{clipId}-child-lookup",
                    DefaultChildA);
                values[$"{clipId}-child-position"] = Vector(
                    $"{clipId}-child-position",
                    AnimatedPosition);
                values[$"{clipId}-child-values"] = Record(
                    $"{clipId}-child-values",
                    PartClassId,
                    new Dictionary<string, string>
                    {
                        ["Position"] = $"{clipId}-child-position",
                    });
                values[$"{clipId}-child-override"] = Record(
                    $"{clipId}-child-override",
                    ChildOverrideClassId,
                    new Dictionary<string, string>
                    {
                        ["Child"] = $"{clipId}-child-lookup",
                        ["Overrides"] = $"{clipId}-child-values",
                    });
                values[$"{clipId}-child-overrides"] = ArrayRow(
                    $"{clipId}-child-overrides",
                    $"{clipId}-child-override");
                values[$"{clipId}-frame"] = Record(
                    $"{clipId}-frame",
                    FrameClassId,
                    new Dictionary<string, string>
                    {
                        ["Index"] = $"{clipId}-frame-index",
                        ["ChildOverrides"] = $"{clipId}-child-overrides",
                    });
                frameIds.Add($"{clipId}-frame");
            }
            values[$"{clipId}-frames"] = ArrayRow($"{clipId}-frames", frameIds.ToArray());

            var trackIds = new List<string>();
            if (trackClipKey is not null)
            {
                values[$"{clipId}-track-lookup"] = ArrayRow(
                    $"{clipId}-track-lookup",
                    DefaultChildA);
                values[$"{clipId}-track-key"] = new StringMemberValue
                {
                    id = $"{clipId}-track-key",
                    value = trackClipKey,
                };
                values[$"{clipId}-track-start"] = Number($"{clipId}-track-start", 0);
                values[$"{clipId}-track"] = Record(
                    $"{clipId}-track",
                    TrackClassId,
                    new Dictionary<string, string>
                    {
                        ["Child"] = $"{clipId}-track-lookup",
                        ["ClipKey"] = $"{clipId}-track-key",
                        ["StartFrame"] = $"{clipId}-track-start",
                    });
                trackIds.Add($"{clipId}-track");
            }
            values[$"{clipId}-tracks"] = ArrayRow($"{clipId}-tracks", trackIds.ToArray());

            values[clipId] = Record(
                clipId,
                ClipClassId,
                new Dictionary<string, string>
                {
                    ["FPS"] = $"{clipId}-fps",
                    ["Duration"] = $"{clipId}-duration",
                    ["Frames"] = $"{clipId}-frames",
                    ["Tracks"] = $"{clipId}-tracks",
                });
        }

        private static ClassMember ClassMemberOf(
            string id,
            string name,
            string classId,
            string? valueId)
        {
            return new ClassMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Class,
                classId = classId,
                valueId = valueId,
            };
        }

        private static ClassMember PartialClassMemberOf(string id, string name)
        {
            return new ClassMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Class,
                classId = PartClassId,
                partial = true,
            };
        }

        private static ClassMember ClipMemberOf(string id, string name, string valueId)
        {
            return new ClassMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Class,
                classId = ClipClassId,
                valueId = valueId,
                storage = "immutable",
            };
        }

        private static IntMember IntMemberOf(string id, string name)
        {
            return new IntMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Int,
            };
        }

        private static ListMember ListMemberOf(string id, string name, string entryMemberId)
        {
            return new ListMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.List,
                entryMemberId = entryMemberId,
            };
        }

        private static LookupMember LookupMemberOf(string id, string name)
        {
            return new LookupMember
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Lookup,
                collectionMemberId = "part-children-member",
            };
        }

        private static ObjectMemberValue Record(
            string id,
            string classId,
            Dictionary<string, string> record)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = record,
            };
        }

        private static ArrayMemberValue ArrayRow(string id, params string[] entries)
        {
            return new ArrayMemberValue { id = id, value = entries };
        }

        private static NumberMemberValue Number(string id, double value)
        {
            return new NumberMemberValue { id = id, value = value };
        }

        private static Vector3MemberValue Vector(string id, float x)
        {
            return new Vector3MemberValue
            {
                id = id,
                value = new NeoVector3Value { x = x, y = 0f, z = 0f },
            };
        }
    }
}
