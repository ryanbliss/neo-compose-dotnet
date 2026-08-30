// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using JsonEnum = NeoCompose.Runtime.Json.Enum;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P48 §2.3 and §3.1 through the REAL compiler and the real apply path.
    ///
    /// <para>The pipeline half is driven from
    /// <see cref="NeoAnimationPlaybackParityFixture"/> — the same table the web
    /// answers through <c>src/models/animation/animation-playback.ts</c>, which
    /// is what P48 acceptance 7 ("bit-identical across web and .NET for the
    /// full test table") means. The fixture is runtime-agnostic: its values are
    /// opaque labels and its tracks name no child, so this harness maps each
    /// case onto a rig — a parent object <c>P</c>, its child <c>C</c>, and
    /// <c>C</c>'s own child <c>G</c> — and each label onto a sprite file id.
    /// </para>
    ///
    /// <para>Where the two runtimes are structurally different, and why the
    /// mapping is what it is:</para>
    /// <list type="bullet">
    /// <item>A <b>segment</b> track writes a member of the child it names, so a
    /// case's tracks all target <c>C.Sprite</c>.</item>
    /// <item>A <b>child clip</b> track delegates to a clip on <c>C</c>. That
    /// clip's own frames and its nested tracks have to write the same member
    /// for the fixture's composed timeline to be observable, so the child
    /// clip's frames write <c>G.Sprite</c> through <c>ChildOverrides</c> and its
    /// nested segment track names <c>G</c>. The case is then read off
    /// <c>G.Sprite</c>.</item>
    /// </list>
    ///
    /// <para>Both expectation columns are asserted, in two passes.
    /// <c>values</c> is read after each applied frame. <c>writes</c> needs a
    /// probe pass — the member is set to a sentinel before each frame, so an
    /// empty write list is observable as "the sentinel survived" rather than
    /// being indistinguishable from a track replaying its last value. The probe
    /// pass runs for segment cases only: a child clip track deliberately does
    /// not re-apply an unchanged child frame (the <c>lastAppliedChildFrame</c>
    /// dedupe), which is invisible to <c>values</c> and to any consumer, but
    /// would read as a missing write here.</para>
    /// </summary>
    public class NeoAnimationSegmentTrackTests
    {
        private const string ProjectId = "project-p48";
        private const string RigClassId = "rig-class";
        private const string ClipClassId = "clip-class";
        private const string FrameClassId = "frame-class";
        private const string ChildOverrideClassId = "child-override-class";
        private const string TrackBaseClassId = "track-base-class";
        private const string ChildTrackClassId = "child-track-class";
        private const string SegmentTrackBaseClassId = "segment-track-base-class";
        private const string SpriteSegmentTrackClassId = "sprite-segment-track-class";
        private const string LookupSegmentTrackClassId = "lookup-segment-track-class";
        private const string SegmentClassId = "segment-class";
        private const string SegmentFrameClassId = "segment-frame-class";
        private const string RootClassId = "root-class";
        private const string EmptyClassId = "empty-class";

        /// <summary>The real P48 §2.1 enum id, so the option ids below are the shipped ones.</summary>
        private const string PlayDirectionEnumId =
            "system_705ccc39-e46e-4c9f-af3e-3ec8fd818709";

        private const string ProbeFileId = "__probe__";

        // ------------------------------------------------------------------
        // P48 §2.3 — the cross-runtime pipeline table.
        // ------------------------------------------------------------------

        [TestCaseSource(nameof(ParityCaseLabels))]
        public void PlaybackPipeline_MatchesTheCrossRuntimeFixture(string label)
        {
            JObject fixtureCase = RequireCase(label);
            var tracks = (JArray)fixtureCase["tracks"]!;
            bool hasChildClipTrack = false;
            foreach (JToken track in tracks)
            {
                Assert.AreEqual(
                    "Sprite",
                    (string?)track["target"],
                    "this harness maps exactly one target member; a second one needs a second child");
                if ((string?)track["content"]!["kind"] == "clip") hasChildClipTrack = true;
            }

            ProjectData data = BuildParityProject(fixtureCase);
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            client.RegisterGeneratedClassFactories(ReadOnlyFactories(), WritableFactories());
            using var target = OpenRig(client);
            using NeoAnimationDefinition definition =
                NeoAnimationCompiler.Compile(target, "Clip");

            string observedId = hasChildClipTrack ? "g-sprite" : "c-sprite";
            var frames = (JArray)fixtureCase["frames"]!;

            definition.PreparePlayback();
            foreach (JToken frame in frames)
            {
                int clipFrame = (int)frame["clipFrame"]!;
                definition.ApplyFrame(clipFrame, useResolvedState: false);
                Assert.AreEqual(
                    (string?)frame["values"]!["Sprite"],
                    ReadLabel(client, observedId),
                    $"[{label}] values at clip frame {clipFrame}");
            }

            if (hasChildClipTrack) return;

            ResetObservedMember(client, observedId, fixtureCase);
            definition.PreparePlayback();
            foreach (JToken frame in frames)
            {
                int clipFrame = (int)frame["clipFrame"]!;
                WriteLabel(client, observedId, ProbeFileId);
                definition.ApplyFrame(clipFrame, useResolvedState: false);
                var writes = (JArray)frame["writes"]!;
                if (writes.Count == 0)
                {
                    Assert.AreEqual(
                        ProbeFileId,
                        ReadLabel(client, observedId),
                        $"[{label}] clip frame {clipFrame} must write NOTHING, not replay its last value");
                    continue;
                }
                Assert.AreEqual(
                    (string?)writes[writes.Count - 1]["value"],
                    ReadLabel(client, observedId),
                    $"[{label}] last write at clip frame {clipFrame} (Tracks order, last write wins)");
            }
        }

        /// <summary>
        /// The fixture's <c>requiredCoverage</c> is the web coverage test's
        /// contract; asserting it here too is what stops the .NET side quietly
        /// running a narrowed table after a re-vendor.
        /// </summary>
        [Test]
        public void ParityFixture_CoversEveryScenarioTheSpecRequires()
        {
            JObject fixture = JObject.Parse(NeoAnimationPlaybackParityFixture.Json);
            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (JToken fixtureCase in (JArray)fixture["cases"]!)
            {
                foreach (JToken covers in (JArray)fixtureCase["covers"]!)
                {
                    covered.Add((string)covers!);
                }
            }
            var required = new List<string>();
            foreach (JToken entry in (JArray)fixture["requiredCoverage"]!)
            {
                required.Add((string)entry!);
            }
            CollectionAssert.AreEquivalent(required, covered);
        }

        // ------------------------------------------------------------------
        // P48 §3.1 — re-resolution. The property the whole design leans on.
        // ------------------------------------------------------------------

        /// <summary>
        /// Spec §10's resolution test: write the lookup between two frames and
        /// the next applied frame plays the new asset. "Resolve once at clip
        /// start" passes every other test in this file and fails this one.
        /// </summary>
        [Test]
        public void EquipMidClip_ChangesTheNextAppliedFrame()
        {
            using NeoClient client = BuildEquipClient();
            using var target = OpenRig(client);
            using NeoAnimationDefinition definition =
                NeoAnimationCompiler.Compile(target, "Clip");

            definition.PreparePlayback();
            definition.ApplyFrame(0, useResolvedState: false);
            Assert.AreEqual("a0", ReadLabel(client, "c-sprite"));

            Equip(client, "seg-b");

            definition.ApplyFrame(1, useResolvedState: false);
            Assert.AreEqual(
                "b1",
                ReadLabel(client, "c-sprite"),
                "an equip mid-animation must change the art on the next applied frame");
        }

        [Test]
        public void DelegateSelector_ResolvesAuthoredReferenceToPlacedChildByProvenance()
        {
            ProjectData data = BuildEquipProjectData();
            data.classes[TrackBaseClassId].schema.Remove("Child");
            data.classes[TrackBaseClassId].schema["Selector"] =
                "track-selector-member";
            data.members["track-selector-member"] = new DelegateMember
            {
                id = "track-selector-member",
                projectId = ProjectId,
                name = "Selector",
                kind = MemberKind.NSDelegate,
                required = true,
                returnTypeInfo = new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = true,
                    classId = RigClassId,
                },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                createdAt = "x",
                updatedAt = "x",
            };
            ObjectMemberValue track = (ObjectMemberValue)data.values["track-0"];
            track.value!.Remove("Child");
            track.value["Selector"] = "track-selector-value";
            data.values["authored-c-value"] = ObjectValue(
                "authored-c-value",
                RigClassId,
                new Dictionary<string, string>());
            data.values["c-value"].sourceValueId = "authored-c-value";
            data.values["track-selector-value"] = new DelegateMemberValue
            {
                id = "track-selector-value",
                createdAt = "x",
                updatedAt = "x",
                value = new NeoDelegateValue
                {
                    code = "() => Reference<Rig>(id: \"authored-c-value\", withProvenance: true)",
                    action = new FunctionWithReturnType
                    {
                        compilerRevision = 7,
                        parameters = new[]
                        {
                            new Variable
                            {
                                id = "__this__",
                                typeInfo = new ClassTypeInfo
                                {
                                    type = MemberKind.Class,
                                    required = true,
                                    classId = RigClassId,
                                },
                                pointer = new VariablePointer
                                {
                                    type = PointerKind.Variable,
                                    variableId = "__this__",
                                },
                            },
                            new Variable
                            {
                                id = "__root__",
                                typeInfo = new ClassTypeInfo
                                {
                                    type = MemberKind.Class,
                                    required = true,
                                    classId = RootClassId,
                                },
                                pointer = new VariablePointer
                                {
                                    type = PointerKind.Variable,
                                    variableId = "__root__",
                                },
                            },
                        },
                        instructions = new Instruction[]
                        {
                            new ReturnInstruction
                            {
                                type = InstructionKind.Return,
                                pointer = new ReferencePointer
                                {
                                    type = PointerKind.Reference,
                                    valueId = "authored-c-value",
                                    withProvenance = true,
                                },
                            },
                        },
                        typeInfo = new ClassTypeInfo
                        {
                            type = MemberKind.Class,
                            required = true,
                            classId = RigClassId,
                        },
                    },
                },
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            using var target = OpenRig(client);
            using NeoAnimationDefinition definition =
                NeoAnimationCompiler.Compile(target, "Clip");

            definition.PreparePlayback();
            definition.ApplyFrame(0, useResolvedState: false);

            Assert.AreEqual("a0", ReadLabel(client, "c-sprite"));
        }

        [Test]
        public void TypedDelegateSetter_PersistsTheBoundNeoValue()
        {
            ProjectData data = BuildEquipProjectData();
            var member = new DelegateMember
            {
                id = "track-selector-member",
                projectId = ProjectId,
                name = "Selector",
                kind = MemberKind.NSDelegate,
                required = true,
                returnTypeInfo = new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = true,
                    classId = RigClassId,
                },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                createdAt = "x",
                updatedAt = "x",
                defaultValue = new DelegateMemberValueBase
                {
                    value = new NeoDelegateValue
                    {
                        memberId = "callable-member",
                        valueId = "c-value",
                    },
                },
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            using var source = new NeoMemberDelegate(client, member, null);
            using var destination = new NeoMemberDelegateWritable(
                client,
                member,
                null,
                NeoValueOwnership.Session);
            NeoDelegate<object?> bound = source.Bind<object?>(result => result);

            destination.Set(bound);
            NeoDelegateValue? persisted =
                NeoGeneratedTypesSupport.DelegateValue(
                    destination.Bind<object?>(result => result));

            Assert.NotNull(persisted);
            Assert.AreEqual("callable-member", persisted!.memberId);
            Assert.AreEqual("c-value", persisted.valueId);
            Assert.AreNotSame(
                member.defaultValue.value,
                persisted,
                "typed assignment must copy only the persisted binding shape");
        }

        [Test]
        public void NullableDelegateSetter_NullDoesNotResurfaceDeclarationDefault()
        {
            ProjectData data = BuildEquipProjectData();
            DelegateMember member = DelegateMemberWithDefault(required: false);

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            using var destination = new NeoMemberDelegateWritable(
                client,
                member,
                null,
                NeoValueOwnership.Session);

            destination.Set((Delegate?)null);

            Assert.IsNull(destination.value?.value);
            NSGetterRuntimeError error = Assert.Throws<NSGetterRuntimeError>(
                () => destination.Bind<object?>(result => result))!;
            StringAssert.Contains("has no bound value", error.Message);
        }

        [Test]
        public void RequiredDelegateSetter_RejectsNull()
        {
            ProjectData data = BuildEquipProjectData();
            DelegateMember member = DelegateMemberWithDefault(required: true);

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            using var destination = new NeoMemberDelegateWritable(
                client,
                member,
                null,
                NeoValueOwnership.Session);

            Assert.Throws<ArgumentNullException>(
                () => destination.Set((Delegate?)null));
        }

        private static DelegateMember DelegateMemberWithDefault(bool required) => new()
        {
            id = "nullable-track-selector-member",
            projectId = ProjectId,
            name = "Selector",
            kind = MemberKind.NSDelegate,
            required = required,
            returnTypeInfo = new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = RigClassId,
            },
            argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
            createdAt = "x",
            updatedAt = "x",
            defaultValue = new DelegateMemberValueBase
            {
                value = new NeoDelegateValue
                {
                    memberId = "callable-member",
                    valueId = "c-value",
                },
            },
        };

        /// <summary>
        /// The first Session write over an <b>authored</b> NSDelegate row is
        /// a clone-on-write shadow: the setter must clone the row instead of
        /// throwing, write the new binding onto the shadow, and leave the
        /// shared authored row untouched. The cloned <i>payload</i> is not
        /// this test's subject — <c>Set</c> overwrites it immediately after
        /// shadowing — see
        /// <see cref="ShadowClone_OfAnAuthoredDelegateRow_CopiesThePayload"/>.
        /// </summary>
        [Test]
        public void TypedDelegateSetter_ShadowsAnAuthoredRowWithAPersistedCopy()
        {
            ProjectData data = BuildEquipProjectData();
            var authored = new DelegateMemberValue
            {
                id = "track-selector-value",
                createdAt = "x",
                updatedAt = "x",
                value = new NeoDelegateValue
                {
                    memberId = "callable-member",
                    valueId = "c-value",
                },
            };
            data.values[authored.id] = authored;
            var member = new DelegateMember
            {
                id = "track-selector-member",
                projectId = ProjectId,
                name = "Selector",
                kind = MemberKind.NSDelegate,
                required = true,
                returnTypeInfo = new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = true,
                    classId = RigClassId,
                },
                argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                valueId = authored.id,
                storage = "session",
                createdAt = "x",
                updatedAt = "x",
            };

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            using var source = new NeoMemberDelegate(client, member, null);
            using var destination = new NeoMemberDelegateWritable(
                client,
                member,
                null,
                NeoValueOwnership.Session);
            NeoDelegate<object?> bound = source.Bind<object?>(result => result);

            destination.Set(bound);

            Assert.IsTrue(
                client.TryGetWritableValue(
                    NeoValueOwnership.Session,
                    authored.id,
                    out DelegateMemberValue? shadow),
                "the first Session write over an authored delegate row is a clone-on-write shadow");
            Assert.AreEqual("callable-member", shadow!.value!.memberId);
            Assert.AreEqual("c-value", shadow.value.valueId);
            Assert.AreNotSame(
                authored,
                shadow,
                "the write lands on a cloned row, never on the authored row");
            // The authored asset row is untouched by the write.
            Assert.AreSame(authored, data.values[authored.id]);
            Assert.AreEqual("callable-member", authored.value!.memberId);
        }

        /// <summary>
        /// The delegate clone arm itself must copy the payload, not alias
        /// it. <c>NeoMemberDelegateWritable.Set</c> overwrites the cloned
        /// row's payload right after shadowing, so the setter test above
        /// cannot see an aliasing clone — this one shadows through
        /// <c>EnsureWritableShadow</c>, which leaves the cloned payload
        /// exactly as the arm produced it.
        /// </summary>
        [Test]
        public void ShadowClone_OfAnAuthoredDelegateRow_CopiesThePayload()
        {
            ProjectData data = BuildEquipProjectData();
            var authored = new DelegateMemberValue
            {
                id = "track-selector-value",
                createdAt = "x",
                updatedAt = "x",
                value = new NeoDelegateValue
                {
                    memberId = "callable-member",
                    valueId = "c-value",
                },
            };
            data.values[authored.id] = authored;

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            Assert.IsTrue(
                client.EnsureWritableShadow(NeoValueOwnership.Session, authored.id),
                "an authored delegate row must shadow instead of throwing");
            Assert.IsTrue(
                client.TryGetWritableValue(
                    NeoValueOwnership.Session,
                    authored.id,
                    out DelegateMemberValue? shadow));

            // Same row identity, independent payload.
            Assert.AreEqual(authored.id, shadow!.id);
            Assert.AreNotSame(
                authored.value,
                shadow.value,
                "the clone arm persists a copy, never the shared authored payload");
            Assert.AreEqual("callable-member", shadow.value!.memberId);
            Assert.AreEqual("c-value", shadow.value.valueId);
        }

        [Test]
        public void NativeDelegate_CannotBePersistedAsANeoBinding()
        {
            NeoDelegate<object?> native = () => new object();

            var error = Assert.Throws<ArgumentException>(
                () => NeoGeneratedTypesSupport.DelegateValue(native));

            StringAssert.Contains("was not loaded from a NeoDelegate member", error!.Message);
        }

        /// <summary>
        /// P48 §3.2: an unequipped layer resolves nothing, so the track writes
        /// nothing and the member keeps its last value. Silent and legal at
        /// runtime — the badge is a web-preview concern.
        /// </summary>
        [Test]
        public void NullLookup_WritesNothingAndKeepsTheLastValue()
        {
            using NeoClient client = BuildEquipClient();
            using var target = OpenRig(client);
            using NeoAnimationDefinition definition =
                NeoAnimationCompiler.Compile(target, "Clip");

            definition.PreparePlayback();
            definition.ApplyFrame(0, useResolvedState: false);
            Assert.AreEqual("a0", ReadLabel(client, "c-sprite"));

            Equip(client, null);

            definition.ApplyFrame(1, useResolvedState: false);
            Assert.AreEqual(
                "a0",
                ReadLabel(client, "c-sprite"),
                "nothing equipped resolves nothing, and not-writing is how holding works");
        }

        /// <summary>
        /// P48 §1.2 and §3.1: a Session-stored segment <c>Duration</c> written
        /// at runtime changes the resolved window on the next frame — the case
        /// the spec names to show that the dependency set is "everything the
        /// pipeline reads", not just the segment reference.
        /// </summary>
        [Test]
        public void SessionSegmentDurationWrittenMidClip_ChangesTheResolvedWindow()
        {
            ProjectData data = BuildEquipProjectData();
            // Storage is a per-member author choice (P48 §1.2): a game that
            // wants a runtime-writable segment length simply declares one.
            data.members["segment-duration-member"].storage = "session";
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            using var target = OpenRig(client);
            using NeoAnimationDefinition definition =
                NeoAnimationCompiler.Compile(target, "Clip");

            definition.PreparePlayback();
            definition.ApplyFrame(0, useResolvedState: false);
            Assert.AreEqual("a0", ReadLabel(client, "c-sprite"));

            // Shrink the equipped segment to one frame. Clip frame 1 now sits
            // past the resolved window, so the track writes nothing.
            var segment = new NeoMemberClassWritable(
                client,
                "catalog-entry-member",
                "seg-a",
                NeoValueOwnership.Session);
            NeoGeneratedTypesSupport.SetValue(
                segment,
                "Duration",
                NeoValueWritePayload.FromValue(1d));

            definition.ApplyFrame(1, useResolvedState: false);
            Assert.AreEqual(
                "a0",
                ReadLabel(client, "c-sprite"),
                "a shorter resolved segment ends the window early, from the next frame");
            segment.Dispose();
        }

        /// <summary>
        /// P75: a segment placement is a collapse-stamped sparse instance root,
        /// so a <c>Duration</c> the construction supplied and nothing overrode
        /// is absent from the stored body and lives only at its virtual id.
        /// Reading the body alone makes the whole segment resolve empty and the
        /// clip silently plays nothing — the shape more than half the
        /// <c>ThreeFrameSpriteAnimationSegment</c> rows in the production
        /// corpus have.
        /// </summary>
        [Test]
        public void SparseSegmentResolvesItsConstructedDuration()
        {
            ProjectData data = BuildEquipProjectData();
            ((IntMember)data.members["segment-duration-member"]).defaultValue =
                new NumberMemberValueBase { value = 2 };
            // The replay reconstructs the whole instance, so every required
            // member has to be satisfiable from the declaration. Frames stays
            // materialized in the stored body and wins over this default.
            ((ListMember)data.members["segment-frames-member"]).defaultValue =
                new ArrayMemberValueBase { value = Array.Empty<string>() };
            var segmentRow = (ObjectMemberValue)data.values["seg-a"];
            segmentRow.value!.Remove("Duration");
            segmentRow.constructorArgs = new Dictionary<string, JToken?>();
            segmentRow.instanceConstructorId = null;

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            using var target = OpenRig(client);
            using NeoAnimationDefinition definition =
                NeoAnimationCompiler.Compile(target, "Clip");

            definition.PreparePlayback();
            definition.ApplyFrame(0, useResolvedState: false);

            Assert.AreEqual(
                "a0",
                ReadLabel(client, "c-sprite"),
                "a sparse segment must resolve its constructed Duration, not read as empty");
        }

        /// <summary>
        /// P48 §3.2 and P41: a disabled child is a visibility fact, not a
        /// lifecycle one. Resolution and writes proceed, so enabling a layer
        /// mid-clip shows the current frame rather than a stale one.
        /// </summary>
        [Test]
        public void DisabledChild_StillReceivesSegmentTrackWrites()
        {
            using NeoClient client = BuildEquipClient();
            var childEnabled = new NeoMemberClassWritable(
                client,
                "child-entry-member",
                "c-value",
                NeoValueOwnership.Session);
            NeoGeneratedTypesSupport.SetValue(
                childEnabled,
                "Enabled",
                NeoValueWritePayload.FromValue(false));

            using var target = OpenRig(client);
            using NeoAnimationDefinition definition =
                NeoAnimationCompiler.Compile(target, "Clip");
            definition.PreparePlayback();
            definition.ApplyFrame(0, useResolvedState: false);

            Assert.AreEqual(
                "a0",
                ReadLabel(client, "c-sprite"),
                "a visibility flag must never gate value writes");
            childEnabled.Dispose();
        }

        // ------------------------------------------------------------------
        // P48 §7 — the load-time half of the target rule.
        // ------------------------------------------------------------------

        [Test]
        public void SegmentTrackTargetingAMemberTheChildDoesNotDeclare_FailsDuringLoad()
        {
            ProjectData data = BuildEquipProjectData();
            // The clip's row is a LookupSegmentTrackClassId instance, so its
            // OWN class metadata is what load validation resolves.
            data.classes[LookupSegmentTrackClassId].targetMemberId = "missing-member";
            data.members["missing-member"] = new SpriteMember
            {
                id = "missing-member",
                projectId = ProjectId,
                name = "Missing",
                kind = MemberKind.Sprite,
                createdAt = "x",
                updatedAt = "x",
            };

            var error = Assert.Throws<InvalidOperationException>(
                () => NeoTestSaveStack.ClientFromSchema(data));

            StringAssert.Contains("targets member 'missing-member'", error!.Message);
            StringAssert.Contains("does not declare", error.Message);
        }

        [Test]
        public void SegmentTrackClassWithoutATarget_FailsDuringLoad()
        {
            ProjectData data = BuildEquipProjectData();
            // Null the row's own class AND the base, so nothing on the chain
            // answers and the "declares no target" error is genuinely earned.
            data.classes[LookupSegmentTrackClassId].targetMemberId = null;
            data.classes[SegmentTrackBaseClassId].targetMemberId = null;

            var error = Assert.Throws<InvalidOperationException>(
                () => NeoTestSaveStack.ClientFromSchema(data));

            StringAssert.Contains("declares no target member", error!.Message);
        }

        /// <summary>
        /// The target is class metadata resolved through <c>extendsClassId</c>,
        /// so a project's own subclass of a shipped track inherits it rather
        /// than restating it.
        /// </summary>
        [Test]
        public void SegmentTrackTarget_IsInheritedThroughTheClassChain()
        {
            ProjectData data = BuildEquipProjectData();
            // The scheduled row's class stops declaring a target of its own,
            // so resolution must walk to the base to find one.
            data.classes[LookupSegmentTrackClassId].targetMemberId = null;
            data.classes[SegmentTrackBaseClassId].targetMemberId = "sprite-member";

            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            using var target = OpenRig(client);
            using NeoAnimationDefinition definition =
                NeoAnimationCompiler.Compile(target, "Clip");
            definition.PreparePlayback();
            definition.ApplyFrame(0, useResolvedState: false);

            Assert.AreEqual("a0", ReadLabel(client, "c-sprite"));
        }

        // ------------------------------------------------------------------
        // Fixture helpers
        // ------------------------------------------------------------------

        private static IEnumerable<string> ParityCaseLabels()
        {
            JObject fixture = JObject.Parse(NeoAnimationPlaybackParityFixture.Json);
            foreach (JToken fixtureCase in (JArray)fixture["cases"]!)
            {
                yield return (string)fixtureCase["label"]!;
            }
        }

        private static JObject RequireCase(string label)
        {
            JObject fixture = JObject.Parse(NeoAnimationPlaybackParityFixture.Json);
            foreach (JToken fixtureCase in (JArray)fixture["cases"]!)
            {
                if ((string?)fixtureCase["label"] == label) return (JObject)fixtureCase;
            }
            throw new InvalidOperationException($"No parity case labelled '{label}'.");
        }

        private static void ResetObservedMember(
            NeoClient client,
            string observedId,
            JObject fixtureCase)
        {
            WriteLabel(
                client,
                observedId,
                (string?)fixtureCase["initialValues"]!["Sprite"]);
        }

        private static string? ReadLabel(NeoClient client, string valueId)
        {
            MemberValue? row = client.ResolveEffectiveRow(valueId);
            Assert.IsNotNull(row, $"No value row '{valueId}'.");
            return ((SpriteMemberValue)row!).value?.fileId;
        }

        private static void WriteLabel(NeoClient client, string valueId, string? label)
        {
            // Asset ownership on purpose — the same layer the track's own
            // writes land in. A Session node here would create an overlay row
            // that shadows every subsequent track write, so the probe would
            // "survive" frames the track genuinely wrote. The registry is
            // last-write-wins with a guarded remove, so the transient node is
            // safe beside the compile's own view of the same row.
            string key = valueId == "c-sprite" ? "c-value" : "g-value";
            var node = new NeoMemberClassWritable(
                client,
                "child-entry-member",
                key,
                NeoValueOwnership.Asset);
            NeoGeneratedTypesSupport.SetValue(
                node,
                "Sprite",
                NeoValueWritePayload.FromValue(
                    label is null ? null : new SpriteValue { fileId = label, sliceIndex = 0 }));
            node.Dispose();
        }

        private static void Equip(NeoClient client, string? segmentValueId)
        {
            var track = new NeoMemberClassWritable(
                client,
                "track-entry-member",
                "track-0",
                NeoValueOwnership.Session);
            track.GetOrCreateLookup("Segment").Set(
                segmentValueId is null ? Array.Empty<string>() : new[] { segmentValueId });
            track.Dispose();
        }

        /// <summary>
        /// The animation target, opened through the assets root so its node has
        /// a real parent chain — the same shape a placed instance has. The
        /// write helpers below deliberately build their nodes at Session
        /// ownership instead, so their registry keys never collide with the
        /// Asset-owned nodes the compile walks.
        /// </summary>
        private static RigValue OpenRig(NeoClient client)
        {
            return new RigValue(client, client.assets.Get<NeoMemberClass>("Rig"));
        }

        private sealed class RigValue : NeoGeneratedClassValue
        {
            internal RigValue(NeoClient client, NeoMemberClass node)
                : base(client, node, RigClassId, isReadOnly: false)
            {
            }
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            ReadOnlyFactories()
        {
            return new Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory>
            {
                [RigClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new RigValue(resolvedClient, node)),
            };
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
            WritableFactories()
        {
            return new Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory>
            {
                [RigClassId] = (resolvedClient, node) =>
                    NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(
                        resolvedClient,
                        node,
                        () => new RigValue(resolvedClient, node)),
            };
        }

        // ------------------------------------------------------------------
        // Project construction
        // ------------------------------------------------------------------

        private static NeoClient BuildEquipClient()
        {
            return NeoTestSaveStack.ClientFromSchema(BuildEquipProjectData());
        }

        /// <summary>
        /// A two-frame clip whose one segment track resolves its
        /// <c>Segment</c> through a Session lookup into a two-row catalog: the
        /// P48 §8 rig idiom, at the smallest size that can show an equip.
        /// </summary>
        private static ProjectData BuildEquipProjectData()
        {
            ProjectData data = BuildBaseProject();
            data.values["clip-fps"] = Number("clip-fps", 8);
            data.values["clip-duration"] = Number("clip-duration", 2);
            data.values["parent-frames"] = ArrayValue("parent-frames");
            data.values["parent-tracks"] = ArrayValue("parent-tracks", "track-0");

            data.values["track-0"] = ObjectValue(
                "track-0",
                LookupSegmentTrackClassId,
                new Dictionary<string, string>
                {
                    ["Child"] = "track-0-child",
                    ["StartFrame"] = "track-0-start",
                    ["Segment"] = "track-0-segment",
                });
            data.values["track-0-child"] = ArrayValue("track-0-child", "c-value");
            data.values["track-0-start"] = Number("track-0-start", 0);
            data.values["track-0-segment"] = ArrayValue("track-0-segment", "seg-a");

            data.values["catalog-value"] = ArrayValue("catalog-value", "seg-a", "seg-b");
            AddSegment(data, "seg-a", 2, ("a0", 0), ("a1", 1));
            AddSegment(data, "seg-b", 2, ("b0", 0), ("b1", 1));

            // An unused-but-valid child clip: the member is declared on the rig
            // class, so whole-project validation walks it whether or not this
            // case schedules it.
            AddIdleChildClip(data);
            return data;
        }

        private static ProjectData BuildParityProject(JObject fixtureCase)
        {
            ProjectData data = BuildBaseProject();
            data.values["clip-fps"] = Number("clip-fps", (int)fixtureCase["clipFps"]!);
            data.values["clip-duration"] =
                Number("clip-duration", (int)fixtureCase["clipDuration"]!);
            data.values["parent-frames"] = ArrayValue("parent-frames");

            string? initial = (string?)fixtureCase["initialValues"]!["Sprite"];
            data.values["c-sprite"] = Sprite("c-sprite", initial);
            data.values["g-sprite"] = Sprite("g-sprite", initial);

            var trackIds = new List<string>();
            bool wroteChildClip = false;
            var tracks = (JArray)fixtureCase["tracks"]!;
            for (int index = 0; index < tracks.Count; index++)
            {
                var track = (JObject)tracks[index];
                string trackId = $"track-{index}";
                trackIds.Add(trackId);
                var content = (JObject)track["content"]!;
                if ((string?)content["kind"] == "clip")
                {
                    Assert.IsFalse(
                        wroteChildClip,
                        "the rig has one child clip member, so a case may schedule at most one");
                    wroteChildClip = true;
                    AddChildClipTrack(data, trackId, track, content);
                    continue;
                }
                AddSegmentTrack(
                    data,
                    trackId,
                    track,
                    content,
                    childValueId: "c-value",
                    segmentId: $"{trackId}-segment-value");
            }
            data.values["parent-tracks"] = ArrayValue("parent-tracks", trackIds.ToArray());
            if (!wroteChildClip) AddIdleChildClip(data);
            return data;
        }

        private static void AddChildClipTrack(
            ProjectData data,
            string trackId,
            JObject track,
            JObject content)
        {
            var row = new Dictionary<string, string>
            {
                ["Child"] = $"{trackId}-child",
                ["ClipKey"] = $"{trackId}-key",
                ["StartFrame"] = $"{trackId}-start",
                ["Direction"] = $"{trackId}-direction",
            };
            data.values[$"{trackId}-child"] = ArrayValue($"{trackId}-child", "c-value");
            data.values[$"{trackId}-key"] = new StringMemberValue
            {
                id = $"{trackId}-key",
                value = "ChildClip",
            };
            data.values[$"{trackId}-start"] =
                Number($"{trackId}-start", (int)track["startFrame"]!);
            data.values[$"{trackId}-direction"] = DirectionValue(
                $"{trackId}-direction",
                (string)track["direction"]!);
            AddCropWindow(data, trackId, track, row);
            data.values[trackId] = ObjectValue(trackId, ChildTrackClassId, row);

            // The child clip's own frames write G.Sprite through
            // ChildOverrides, and its nested tracks name G, so the fixture's
            // composed child timeline is observable on one member.
            var frameIds = new List<string>();
            var frames = (JArray)content["frames"]!;
            for (int index = 0; index < frames.Count; index++)
            {
                var frame = (JObject)frames[index];
                string frameId = $"child-frame-{index}";
                data.values[$"{frameId}-index"] =
                    Number($"{frameId}-index", (int)frame["index"]!);
                data.values[$"{frameId}-sprite"] =
                    Sprite($"{frameId}-sprite", (string?)frame["value"]);
                data.values[$"{frameId}-overrides"] = ObjectValue(
                    $"{frameId}-overrides",
                    RigClassId,
                    new Dictionary<string, string> { ["Sprite"] = $"{frameId}-sprite" });
                data.values[$"{frameId}-child-override"] = ObjectValue(
                    $"{frameId}-child-override",
                    ChildOverrideClassId,
                    new Dictionary<string, string>
                    {
                        ["Child"] = $"{frameId}-child-lookup",
                        ["Overrides"] = $"{frameId}-overrides",
                    });
                data.values[$"{frameId}-child-lookup"] =
                    ArrayValue($"{frameId}-child-lookup", "g-value");
                data.values[$"{frameId}-child-overrides"] =
                    ArrayValue($"{frameId}-child-overrides", $"{frameId}-child-override");
                data.values[frameId] = ObjectValue(
                    frameId,
                    FrameClassId,
                    new Dictionary<string, string>
                    {
                        ["Index"] = $"{frameId}-index",
                        ["ChildOverrides"] = $"{frameId}-child-overrides",
                    });
                frameIds.Add(frameId);
            }

            var nestedIds = new List<string>();
            var nested = (JArray)content["tracks"]!;
            for (int index = 0; index < nested.Count; index++)
            {
                var nestedTrack = (JObject)nested[index];
                string nestedId = $"nested-track-{index}";
                nestedIds.Add(nestedId);
                AddSegmentTrack(
                    data,
                    nestedId,
                    nestedTrack,
                    (JObject)nestedTrack["content"]!,
                    childValueId: "g-value",
                    segmentId: $"{nestedId}-segment-value");
            }

            data.values["child-clip-fps"] = Number("child-clip-fps", (int)content["fps"]!);
            data.values["child-clip-duration"] =
                Number("child-clip-duration", (int)content["duration"]!);
            data.values["child-clip-frames"] =
                ArrayValue("child-clip-frames", frameIds.ToArray());
            data.values["child-clip-tracks"] =
                ArrayValue("child-clip-tracks", nestedIds.ToArray());
        }

        private static void AddSegmentTrack(
            ProjectData data,
            string trackId,
            JObject track,
            JObject content,
            string childValueId,
            string segmentId)
        {
            var row = new Dictionary<string, string>
            {
                ["Child"] = $"{trackId}-child",
                ["StartFrame"] = $"{trackId}-start",
                ["Direction"] = $"{trackId}-direction",
                ["Segment"] = segmentId,
            };
            data.values[$"{trackId}-child"] = ArrayValue($"{trackId}-child", childValueId);
            data.values[$"{trackId}-start"] =
                Number($"{trackId}-start", (int)track["startFrame"]!);
            data.values[$"{trackId}-direction"] = DirectionValue(
                $"{trackId}-direction",
                (string)track["direction"]!);
            AddCropWindow(data, trackId, track, row);
            data.values[trackId] = ObjectValue(trackId, SpriteSegmentTrackClassId, row);

            var frames = new List<(string label, int index)>();
            foreach (JToken frame in (JArray)content["frames"]!)
            {
                frames.Add(((string?)frame["value"] ?? "", (int)frame["index"]!));
            }
            AddSegment(data, segmentId, (int)content["duration"]!, frames.ToArray());
        }

        private static void AddCropWindow(
            ProjectData data,
            string trackId,
            JObject track,
            Dictionary<string, string> row)
        {
            JToken? start = track["offsetStartIndex"];
            if (start is not null && start.Type != JTokenType.Null)
            {
                data.values[$"{trackId}-crop-start"] =
                    Number($"{trackId}-crop-start", (int)start);
                row["OffsetStartIndex"] = $"{trackId}-crop-start";
            }
            JToken? end = track["offsetEndIndex"];
            if (end is not null && end.Type != JTokenType.Null)
            {
                data.values[$"{trackId}-crop-end"] =
                    Number($"{trackId}-crop-end", (int)end);
                row["OffsetEndIndex"] = $"{trackId}-crop-end";
            }
        }

        private static void AddSegment(
            ProjectData data,
            string segmentId,
            int duration,
            params (string label, int index)[] frames)
        {
            var frameIds = new List<string>();
            for (int index = 0; index < frames.Length; index++)
            {
                string frameId = $"{segmentId}-frame-{index}";
                data.values[$"{frameId}-index"] =
                    Number($"{frameId}-index", frames[index].index);
                data.values[$"{frameId}-value"] =
                    Sprite($"{frameId}-value", frames[index].label);
                data.values[frameId] = ObjectValue(
                    frameId,
                    SegmentFrameClassId,
                    new Dictionary<string, string>
                    {
                        ["Index"] = $"{frameId}-index",
                        ["Value"] = $"{frameId}-value",
                    });
                frameIds.Add(frameId);
            }
            data.values[$"{segmentId}-duration"] =
                Number($"{segmentId}-duration", duration);
            data.values[$"{segmentId}-frames"] =
                ArrayValue($"{segmentId}-frames", frameIds.ToArray());
            data.values[segmentId] = ObjectValue(
                segmentId,
                SegmentClassId,
                new Dictionary<string, string>
                {
                    ["Duration"] = $"{segmentId}-duration",
                    ["Frames"] = $"{segmentId}-frames",
                });
        }

        private static void AddIdleChildClip(ProjectData data)
        {
            data.values["child-clip-fps"] = Number("child-clip-fps", 8);
            data.values["child-clip-duration"] = Number("child-clip-duration", 1);
            data.values["child-clip-frames"] = ArrayValue("child-clip-frames");
            data.values["child-clip-tracks"] = ArrayValue("child-clip-tracks");
        }

        /// <summary>
        /// The schema every case shares: one rig class used for all three
        /// objects, the clip family, and the two track kinds under one
        /// <c>NeoAnimationTrackBase</c> — which is what makes <c>Tracks</c>
        /// polymorphic rather than a list of child tracks.
        /// </summary>
        private static ProjectData BuildBaseProject()
        {
            var classes = new Dictionary<string, NeoSchemaClass>
            {
                [RigClassId] = Class(RigClassId, "Rig", "object", new()
                {
                    ["Children"] = "children-member",
                    ["Sprite"] = "sprite-member",
                    ["Enabled"] = "enabled-member",
                    ["Clip"] = "clip-member",
                    ["ChildClip"] = "child-clip-member",
                }),
                [ClipClassId] = Class(ClipClassId, "Clip", "animationClip", new()
                {
                    ["FPS"] = "fps-member",
                    ["Duration"] = "duration-member",
                    ["Frames"] = "frames-member",
                    ["Tracks"] = "tracks-member",
                }),
                [FrameClassId] = Class(FrameClassId, "Frame", "animationFrame", new()
                {
                    ["Index"] = "index-member",
                    ["ChildOverrides"] = "child-overrides-member",
                }),
                [ChildOverrideClassId] = Class(
                    ChildOverrideClassId,
                    "ChildOverride",
                    "animationChildOverride",
                    new()
                    {
                        ["Child"] = "co-child-member",
                        ["Overrides"] = "co-overrides-member",
                    }),
                [TrackBaseClassId] = Class(
                    TrackBaseClassId,
                    "TrackBase",
                    "animationTrack",
                    new()
                    {
                        ["Child"] = "track-child-member",
                        ["StartFrame"] = "track-start-member",
                        ["Direction"] = "track-direction-member",
                        ["OffsetStartIndex"] = "track-offset-start-member",
                        ["OffsetEndIndex"] = "track-offset-end-member",
                    },
                    isAbstract: true),
                [ChildTrackClassId] = Class(
                    ChildTrackClassId,
                    "ChildTrack",
                    "animationChildTrack",
                    new() { ["ClipKey"] = "track-clip-key-member" },
                    extendsClassId: TrackBaseClassId),
                [SegmentTrackBaseClassId] = Class(
                    SegmentTrackBaseClassId,
                    "SegmentTrackBase",
                    "animationSegmentTrack",
                    new(),
                    extendsClassId: TrackBaseClassId,
                    isAbstract: true),
                [SpriteSegmentTrackClassId] = Class(
                    SpriteSegmentTrackClassId,
                    "SpriteSegmentTrack",
                    null,
                    new() { ["Segment"] = "track-segment-member" },
                    extendsClassId: SegmentTrackBaseClassId,
                    targetMemberId: "sprite-member"),
                [LookupSegmentTrackClassId] = Class(
                    LookupSegmentTrackClassId,
                    "LookupSegmentTrack",
                    null,
                    new() { ["Segment"] = "track-segment-lookup-member" },
                    extendsClassId: SegmentTrackBaseClassId,
                    targetMemberId: "sprite-member"),
                [SegmentClassId] = Class(SegmentClassId, "Segment", "animationSegment", new()
                {
                    ["Duration"] = "segment-duration-member",
                    ["Frames"] = "segment-frames-member",
                }),
                [SegmentFrameClassId] = Class(
                    SegmentFrameClassId,
                    "SegmentFrame",
                    "animationSegmentFrame",
                    new()
                    {
                        ["Index"] = "segment-index-member",
                        ["Value"] = "segment-value-member",
                    }),
                [RootClassId] = Class(RootClassId, "Root", null, new()
                {
                    ["Rig"] = "rig-member",
                    ["Catalog"] = "catalog-member",
                }),
                [EmptyClassId] = Class(EmptyClassId, "Empty", null, new()),
            };

            var members = new Dictionary<string, Member>
            {
                ["root-assets"] = ClassMemberOf("root-assets", "Assets", RootClassId, "root-assets-value"),
                ["root-save"] = ClassMemberOf("root-save", "Save", EmptyClassId, "root-save-value"),
                ["root-session"] = ClassMemberOf("root-session", "Session", EmptyClassId, "root-session-value"),
                ["rig-member"] = ClassMemberOf("rig-member", "Rig", RigClassId, "p-value"),
                ["child-entry-member"] = ClassMemberOf("child-entry-member", "Child", RigClassId, null),
                ["children-member"] = ListMemberOf("children-member", "Children", "child-entry-member"),
                ["sprite-member"] = SpriteMemberOf("sprite-member", "Sprite"),
                ["enabled-member"] = new BoolMember
                {
                    id = "enabled-member",
                    projectId = ProjectId,
                    name = "Enabled",
                    kind = MemberKind.Bool,
                    storage = "session",
                    createdAt = "x",
                    updatedAt = "x",
                },
                ["clip-member"] = ClipMemberOf("clip-member", "Clip", "parent-clip-value"),
                ["child-clip-member"] = ClipMemberOf("child-clip-member", "ChildClip", "child-clip-value"),
                ["fps-member"] = IntMemberOf("fps-member", "FPS"),
                ["duration-member"] = IntMemberOf("duration-member", "Duration"),
                ["frames-member"] = ListMemberOf("frames-member", "Frames", "frame-entry-member"),
                ["frame-entry-member"] = ClassMemberOf("frame-entry-member", "Frame", FrameClassId, null),
                ["tracks-member"] = ListMemberOf("tracks-member", "Tracks", "track-entry-member"),
                ["track-entry-member"] = ClassMemberOf("track-entry-member", "Track", TrackBaseClassId, null),
                ["index-member"] = IntMemberOf("index-member", "Index"),
                ["child-overrides-member"] = ListMemberOf(
                    "child-overrides-member",
                    "ChildOverrides",
                    "child-override-entry-member"),
                ["child-override-entry-member"] = ClassMemberOf(
                    "child-override-entry-member",
                    "ChildOverride",
                    ChildOverrideClassId,
                    null),
                ["co-child-member"] = LookupMemberOf("co-child-member", "Child", "children-member"),
                ["co-overrides-member"] = PartialClassMemberOf("co-overrides-member", "Overrides", RigClassId),
                ["track-child-member"] = LookupMemberOf("track-child-member", "Child", "children-member"),
                ["track-start-member"] = IntMemberOf("track-start-member", "StartFrame"),
                ["track-offset-start-member"] = IntMemberOf("track-offset-start-member", "OffsetStartIndex"),
                ["track-offset-end-member"] = IntMemberOf("track-offset-end-member", "OffsetEndIndex"),
                ["track-direction-member"] = new EnumMember
                {
                    id = "track-direction-member",
                    projectId = ProjectId,
                    name = "Direction",
                    kind = MemberKind.Enum,
                    enumId = PlayDirectionEnumId,
                    multiselect = false,
                    createdAt = "x",
                    updatedAt = "x",
                },
                ["track-clip-key-member"] = new StringMember
                {
                    id = "track-clip-key-member",
                    projectId = ProjectId,
                    name = "ClipKey",
                    kind = MemberKind.String,
                    localizable = false,
                    createdAt = "x",
                    updatedAt = "x",
                },
                ["track-segment-member"] = ClassMemberOf(
                    "track-segment-member",
                    "Segment",
                    SegmentClassId,
                    null),
                ["track-segment-lookup-member"] = LookupMemberOf(
                    "track-segment-lookup-member",
                    "Segment",
                    "catalog-member",
                    storage: "session"),
                ["catalog-member"] = ListMemberOf(
                    "catalog-member",
                    "Catalog",
                    "catalog-entry-member",
                    valueId: "catalog-value"),
                ["catalog-entry-member"] = ClassMemberOf(
                    "catalog-entry-member",
                    "Segment",
                    SegmentClassId,
                    null),
                ["segment-duration-member"] = IntMemberOf("segment-duration-member", "Duration"),
                ["segment-frames-member"] = ListMemberOf(
                    "segment-frames-member",
                    "Frames",
                    "segment-frame-entry-member"),
                ["segment-frame-entry-member"] = ClassMemberOf(
                    "segment-frame-entry-member",
                    "Frame",
                    SegmentFrameClassId,
                    null),
                ["segment-index-member"] = IntMemberOf("segment-index-member", "Index"),
                ["segment-value-member"] = SpriteMemberOf("segment-value-member", "Value"),
            };

            var values = new Dictionary<string, MemberValue>
            {
                ["root-assets-value"] = ObjectValue("root-assets-value", RootClassId, new()
                {
                    ["Rig"] = "p-value",
                    ["Catalog"] = "catalog-value",
                }),
                ["root-save-value"] = ObjectValue("root-save-value", EmptyClassId, new()),
                ["root-session-value"] = ObjectValue("root-session-value", EmptyClassId, new()),
                ["catalog-value"] = ArrayValue("catalog-value"),
                ["p-value"] = ObjectValue("p-value", RigClassId, new()
                {
                    ["Children"] = "p-children",
                    ["Sprite"] = "p-sprite",
                    ["Enabled"] = "p-enabled",
                }),
                ["p-children"] = ArrayValue("p-children", "c-value"),
                ["p-sprite"] = Sprite("p-sprite", null),
                ["p-enabled"] = new BoolMemberValue { id = "p-enabled", value = true },
                ["c-value"] = ObjectValue("c-value", RigClassId, new()
                {
                    ["Children"] = "c-children",
                    ["Sprite"] = "c-sprite",
                    ["Enabled"] = "c-enabled",
                }),
                ["c-children"] = ArrayValue("c-children", "g-value"),
                ["c-sprite"] = Sprite("c-sprite", null),
                ["c-enabled"] = new BoolMemberValue { id = "c-enabled", value = true },
                ["g-value"] = ObjectValue("g-value", RigClassId, new()
                {
                    ["Children"] = "g-children",
                    ["Sprite"] = "g-sprite",
                    ["Enabled"] = "g-enabled",
                }),
                ["g-children"] = ArrayValue("g-children"),
                ["g-sprite"] = Sprite("g-sprite", null),
                ["g-enabled"] = new BoolMemberValue { id = "g-enabled", value = true },
                ["parent-clip-value"] = ObjectValue("parent-clip-value", ClipClassId, new()
                {
                    ["FPS"] = "clip-fps",
                    ["Duration"] = "clip-duration",
                    ["Frames"] = "parent-frames",
                    ["Tracks"] = "parent-tracks",
                }),
                ["child-clip-value"] = ObjectValue("child-clip-value", ClipClassId, new()
                {
                    ["FPS"] = "child-clip-fps",
                    ["Duration"] = "child-clip-duration",
                    ["Frames"] = "child-clip-frames",
                    ["Tracks"] = "child-clip-tracks",
                }),
            };
            // The clip's Child lookup matches on P44 provenance, and an
            // authored graph played in place IS its own authored source.
            values["c-value"].sourceValueId = "c-value";
            values["g-value"].sourceValueId = "g-value";

            return new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    _id = ProjectId,
                    name = "P48 Segment Tracks",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = members,
                values = values,
                classes = classes,
                enums = new Dictionary<string, JsonEnum>
                {
                    [PlayDirectionEnumId] = new JsonEnum
                    {
                        id = PlayDirectionEnumId,
                        projectId = ProjectId,
                        name = "NeoPlayDirection",
                        options = new Dictionary<string, EnumOption>
                        {
                            [NeoPlayDirection.Forward.optionId] = new EnumOption { text = "Forward" },
                            [NeoPlayDirection.Reverse.optionId] = new EnumOption { text = "Reverse" },
                        },
                        optionKeyOrder = new List<string>
                        {
                            NeoPlayDirection.Forward.optionId,
                            NeoPlayDirection.Reverse.optionId,
                        },
                        createdAt = "x",
                        updatedAt = "x",
                    },
                },
            };
        }

        // ------------------------------------------------------------------
        // Record constructors
        // ------------------------------------------------------------------

        private static NeoSchemaClass Class(
            string id,
            string name,
            string? worldKind,
            Dictionary<string, string> schema,
            string? extendsClassId = null,
            bool isAbstract = false,
            string? targetMemberId = null)
        {
            return new NeoSchemaClass
            {
                id = id,
                projectId = ProjectId,
                name = name,
                schema = schema,
                extendsClassId = extendsClassId,
                isAbstract = isAbstract,
                targetMemberId = targetMemberId,
                system = worldKind is null
                    ? null
                    : JObject.Parse($"{{\"worldKind\":\"{worldKind}\"}}"),
            };
        }

        private static ClassMember ClassMemberOf(
            string id,
            string name,
            string classId,
            string? valueId) => new()
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Class,
                classId = classId,
                valueId = valueId,
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };

        private static ClassMember PartialClassMemberOf(
            string id,
            string name,
            string classId)
        {
            ClassMember member = ClassMemberOf(id, name, classId, null);
            member.partial = true;
            return member;
        }

        private static ClassMember ClipMemberOf(string id, string name, string valueId)
        {
            ClassMember member = ClassMemberOf(id, name, ClipClassId, valueId);
            member.storage = "immutable";
            return member;
        }

        private static ListMember ListMemberOf(
            string id,
            string name,
            string entryMemberId,
            string? valueId = null) => new()
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.List,
                entryMemberId = entryMemberId,
                valueId = valueId,
                required = true,
                createdAt = "x",
                updatedAt = "x",
            };

        private static LookupMember LookupMemberOf(
            string id,
            string name,
            string collectionMemberId,
            string? storage = null) => new()
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Lookup,
                collectionMemberId = collectionMemberId,
                storage = storage,
                createdAt = "x",
                updatedAt = "x",
            };

        private static IntMember IntMemberOf(
            string id,
            string name,
            string? storage = null) => new()
            {
                id = id,
                projectId = ProjectId,
                name = name,
                kind = MemberKind.Int,
                storage = storage,
                createdAt = "x",
                updatedAt = "x",
            };

        private static SpriteMember SpriteMemberOf(string id, string name) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Sprite,
            storage = "session",
            createdAt = "x",
            updatedAt = "x",
        };

        private static ObjectMemberValue ObjectValue(
            string id,
            string classId,
            Dictionary<string, string> record) => new()
            {
                id = id,
                classId = classId,
                value = record,
            };

        private static ArrayMemberValue ArrayValue(string id, params string[] entries) => new()
        {
            id = id,
            value = entries,
        };

        private static NumberMemberValue Number(string id, double value) => new()
        {
            id = id,
            value = value,
        };

        private static SpriteMemberValue Sprite(string id, string? label) => new()
        {
            id = id,
            value = label is null ? null : new SpriteValue { fileId = label, sliceIndex = 0 },
        };

        private static ArrayMemberValue DirectionValue(string id, string direction)
        {
            return new ArrayMemberValue
            {
                id = id,
                value = new[]
                {
                    direction == "reverse"
                        ? NeoPlayDirection.Reverse.optionId
                        : NeoPlayDirection.Forward.optionId,
                },
            };
        }
    }
}
