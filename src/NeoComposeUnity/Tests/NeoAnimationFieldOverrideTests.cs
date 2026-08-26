// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P42 §4.3 / §4.4 — field overrides through the REAL animation compiler.
    ///
    /// <para>These stand up a hand-built project carrying an actual animation
    /// clip and drive <see cref="NeoAnimationCompiler"/> and the compiled
    /// writes it produces. The parity fixture's frame-resolution vectors are
    /// deliberately not used: that harness re-implements frame resolution over
    /// JObjects and never touches the compiler, so it cannot observe any of the
    /// behaviour this spec changes. Its <c>partialCompositions</c> section is
    /// the exception and is driven from here — those cases are statements about
    /// what the compiler emits, which is exactly what this file can see.</para>
    /// </summary>
    public class NeoAnimationFieldOverrideTests
    {
        // ------------------------------------------------------------------
        // The headline case: a field write composes against the leaf's value
        // AS IT STANDS, so runtime rebinding is never frozen.
        // ------------------------------------------------------------------

        [Test]
        public void FieldWrite_ComposesAgainstARuntimeReboundSpriteFile()
        {
            using NeoClient client = BuildClient(
                Frame(0, ("Sprite", PartialRow("{\"sliceIndex\":2}"))));
            using var target = OpenPlacement(client, "PlacementA");
            NeoAnimationDefinition definition = NeoAnimationCompiler.Compile(target, "Idle");

            definition.ApplyFrame(0, useResolvedState: false);
            AssertSprite(client, "a-sprite", "sheet-a", 2);

            // Game code rebinds the sheet mid-life, exactly as a variant swap
            // would. The clip must compose against the NEW file on its next
            // pass — a cached composed value would pin 'sheet-a' forever.
            WriteSprite(client, "PlacementA", "sheet-rebound", 0);
            definition.ApplyFrame(0, useResolvedState: false);
            AssertSprite(client, "a-sprite", "sheet-rebound", 2);
        }

        [Test]
        public void TwoPlacementsOnDifferentSheets_PlayOneClipIndependently()
        {
            using NeoClient client = BuildClient(
                Frame(0, ("Sprite", PartialRow("{\"sliceIndex\":2}"))));
            using var a = OpenPlacement(client, "PlacementA");
            using var b = OpenPlacement(client, "PlacementB");
            NeoAnimationDefinition clipA = NeoAnimationCompiler.Compile(a, "Idle");
            NeoAnimationDefinition clipB = NeoAnimationCompiler.Compile(b, "Idle");

            clipA.PreparePlayback();
            clipB.PreparePlayback();
            clipA.ApplyFrame(0, useResolvedState: false);
            clipB.ApplyFrame(0, useResolvedState: false);

            AssertSprite(client, "a-sprite", "sheet-a", 2);
            AssertSprite(client, "b-sprite", "sheet-b", 2);
        }

        [Test]
        public void FieldWrite_LeavesTheComponentGameCodeWroteBetweenFrames()
        {
            using NeoClient client = BuildClient(
                Frame(0, ("Position", PartialRow("{\"y\":0.25}"))),
                Frame(1, ("Position", PartialRow("{\"y\":0.5}"))));
            using var target = OpenPlacement(client, "PlacementA");
            NeoAnimationDefinition definition = NeoAnimationCompiler.Compile(target, "Idle");

            definition.ApplyFrame(0, useResolvedState: false);
            AssertVector3(client, "a-position", 0f, 0.25f, 0f);

            // Movement code advances x between two clip frames.
            WriteVector3(client, "PlacementA", 5f, ReadVector3(client, "a-position").y, 0f);
            definition.ApplyFrame(1, useResolvedState: false);

            // Both are correct: the clip owns y, the game owns x.
            AssertVector3(client, "a-position", 5f, 0.5f, 0f);
        }

        // ------------------------------------------------------------------
        // §1.2 — two field writes to one leaf in one frame are ONE write.
        // ------------------------------------------------------------------

        [Test]
        public void MultipleFieldsInOneFrame_CompileToASingleWrite()
        {
            using NeoClient client = BuildClient(
                Frame(
                    0,
                    ("Sprite", PartialRow("{\"fileId\":\"sheet-b\",\"sliceIndex\":3}")),
                    ("Position", PartialRow("{\"x\":1,\"z\":2}"))));
            using var target = OpenPlacement(client, "PlacementA");
            NeoAnimationDefinition definition = NeoAnimationCompiler.Compile(target, "Idle");

            NeoAnimationCompiledWrite[] writes = definition.SparseWritesForFrame(0);
            // One per LEAF, not one per field.
            Assert.AreEqual(2, writes.Length);
            foreach (NeoAnimationCompiledWrite write in writes)
            {
                Assert.IsTrue(write.IsFieldWrite);
                Assert.AreEqual(2, write.Fields.Count);
            }

            definition.ApplyFrame(0, useResolvedState: false);
            AssertSprite(client, "a-sprite", "sheet-b", 3);
            AssertVector3(client, "a-position", 1f, 0f, 2f);
        }

        // ------------------------------------------------------------------
        // §1.4 — apply-time skips, one warning per (clip, frame, member).
        // ------------------------------------------------------------------

        [Test]
        public void OutOfRangeSliceIndex_SkipsWithExactlyOneWarning()
        {
            using NeoClient client = BuildClient(
                Frame(0, ("Sprite", PartialRow("{\"sliceIndex\":5}"))));
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            try
            {
                // 'sheet-a' is a two-slice sheet, so index 5 cannot resolve.
                database.SetFile(
                    "sheet-a",
                    "sheet-a.png",
                    "Assets/Neo/Files/sheet-a.png",
                    updatedAt: "",
                    lastDownloadedAt: "",
                    fileRecordHash: "",
                    templateId: null,
                    templateRecordHash: "",
                    importSettingsVersion: "",
                    sprites: new Sprite[2]);
                client.assetDatabase = database;

                using var target = OpenPlacement(client, "PlacementA");
                NeoAnimationDefinition definition =
                    NeoAnimationCompiler.Compile(target, "Idle");

                int warnings = CountWarnings(
                    "slice index 5 is outside the slice count",
                    () =>
                    {
                        for (int tick = 0; tick < 5; tick++)
                        {
                            definition.ApplyFrame(0, useResolvedState: false);
                        }
                    });

                // Skipped, so the previous value survives...
                AssertSprite(client, "a-sprite", "sheet-a", 0);
                // ...and five ticks produced exactly one warning.
                Assert.AreEqual(1, warnings);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void NullLeafAtApplyTime_SkipsWithExactlyOneWarning()
        {
            // PlacementB's Tint row is present but null-valued.
            using NeoClient client = BuildClient(
                Frame(0, ("Tint", PartialRow("{\"a\":0.5}"))));
            using var target = OpenPlacement(client, "PlacementB");
            NeoAnimationDefinition definition = NeoAnimationCompiler.Compile(target, "Idle");

            int warnings = CountWarnings(
                "its current value is null",
                () =>
                {
                    for (int tick = 0; tick < 4; tick++)
                    {
                        definition.ApplyFrame(0, useResolvedState: false);
                    }
                });

            Assert.IsNull(((ColorMemberValue)Row(client, "b-tint")).value);
            Assert.AreEqual(1, warnings);
        }

        // ------------------------------------------------------------------
        // §4.3 — backward / boomerang over a mixed field + whole-leaf clip.
        // ------------------------------------------------------------------

        [Test]
        public void ResolvedState_LetsALaterWholeLeafWriteSupersedeAnEarlierFieldWrite()
        {
            using NeoClient client = BuildClient(
                Frame(0, ("Sprite", PartialRow("{\"sliceIndex\":1}"))),
                Frame(2, ("Sprite", WholeSpriteRow("sheet-b", 7))));
            using var target = OpenPlacement(client, "PlacementA");
            NeoAnimationDefinition definition = NeoAnimationCompiler.Compile(target, "Idle");
            definition.PreparePlayback();

            // Backward traversal: 3 -> 2 -> 1 -> 0. The whole-leaf write at
            // frame 2 must still win at frames 2 and 3, and the field write at
            // frame 0 must reappear (composed against the ROOT snapshot, not
            // against the frame-2 value) at frames 1 and 0.
            definition.ApplyFrame(3, useResolvedState: true);
            AssertSprite(client, "a-sprite", "sheet-b", 7);
            definition.ApplyFrame(2, useResolvedState: true);
            AssertSprite(client, "a-sprite", "sheet-b", 7);
            definition.ApplyFrame(1, useResolvedState: true);
            AssertSprite(client, "a-sprite", "sheet-a", 1);
            definition.ApplyFrame(0, useResolvedState: true);
            AssertSprite(client, "a-sprite", "sheet-a", 1);
        }

        [Test]
        public void Boomerang_ReturnsTheSameValuesOnTheWayBack()
        {
            using NeoClient client = BuildClient(
                Frame(0, ("Sprite", PartialRow("{\"sliceIndex\":1}"))),
                Frame(2, ("Sprite", WholeSpriteRow("sheet-b", 7))));
            using var target = OpenPlacement(client, "PlacementA");
            NeoAnimationDefinition definition = NeoAnimationCompiler.Compile(target, "Idle");
            definition.PreparePlayback();

            var forward = new List<string>();
            foreach (int frame in new[] { 0, 1, 2, 3 })
            {
                definition.ApplyFrame(frame, useResolvedState: false);
                forward.Add(DescribeSprite(client, "a-sprite"));
            }
            CollectionAssert.AreEqual(
                new[] { "sheet-a:1", "sheet-a:1", "sheet-b:7", "sheet-b:7" },
                forward);

            var backward = new List<string>();
            foreach (int frame in new[] { 2, 1, 0 })
            {
                definition.ApplyFrame(frame, useResolvedState: true);
                backward.Add(DescribeSprite(client, "a-sprite"));
            }
            CollectionAssert.AreEqual(
                new[] { "sheet-b:7", "sheet-a:1", "sheet-a:1" },
                backward);
        }

        [Test]
        public void FieldOnlyLeaf_RootSnapshotRestoresOnlyTheKeysTheClipWrites()
        {
            using NeoClient client = BuildClient(
                Frame(1, ("Sprite", PartialRow("{\"sliceIndex\":3}"))));
            using var target = OpenPlacement(client, "PlacementA");
            NeoAnimationDefinition definition = NeoAnimationCompiler.Compile(target, "Idle");

            // Rebind before the pass starts; the snapshot must not undo it.
            WriteSprite(client, "PlacementA", "sheet-rebound", 0);
            definition.PreparePlayback();

            definition.ApplyFrame(1, useResolvedState: true);
            AssertSprite(client, "a-sprite", "sheet-rebound", 3);
            // Reverse past the writing frame: sliceIndex reverts to the
            // prepare-time value, and fileId — which the clip never writes — is
            // left exactly as the game set it.
            definition.ApplyFrame(0, useResolvedState: true);
            AssertSprite(client, "a-sprite", "sheet-rebound", 0);
        }

        // ------------------------------------------------------------------
        // §4.4 — export validation (runs inside the NeoClient constructor).
        // ------------------------------------------------------------------

        [Test]
        public void Validation_RejectsAFieldKeyThatIsNotOnTheMembersKind()
        {
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                BuildClient(Frame(0, ("Sprite", PartialRow("{\"tint\":1}")))));
            StringAssert.Contains(
                "writes field 'tint', which is not a field of a Sprite member",
                error!.Message);
            StringAssert.Contains("fileId, sliceIndex", error.Message);
        }

        [Test]
        public void Validation_RejectsAColourChannelOutsideZeroToOne()
        {
            // P42 decision D2: rejected, never clamped.
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                BuildClient(Frame(0, ("Tint", PartialRow("{\"a\":2}")))));
            StringAssert.Contains("colour channel 'a' must be within [0, 1]", error!.Message);
        }

        [Test]
        public void Validation_RejectsANonIntegerIntVectorComponent()
        {
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                BuildClient(Frame(0, ("Grid", PartialRow("{\"x\":0.5}")))));
            StringAssert.Contains(
                "field 'x' must be an integer on a Vector2Int member",
                error!.Message);
        }

        [Test]
        public void Validation_RejectsAnEnvelopeOnAMemberWithNoAddressableFields()
        {
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                BuildClient(Frame(0, ("Count", PartialRow("{\"x\":1}")))));
            StringAssert.Contains("has no addressable fields", error!.Message);
        }

        [Test]
        public void Validation_RejectsAFieldOverrideOnAnIneligibleLeaf()
        {
            // 'Locked' is Sprite-typed but immutable storage: eligibility is
            // evaluated on the ENCLOSING leaf, so the field write is rejected.
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                BuildClient(Frame(0, ("Locked", PartialRow("{\"sliceIndex\":1}")))));
            StringAssert.Contains("resolves Immutable storage", error!.Message);
        }

        [Test]
        public void Validation_RejectsAFieldPathDeeperThanOneLevel()
        {
            // A Class-shaped override record parked on a structured leaf is the
            // only way to express Sprite.fileId.anything on the wire.
            var deeper = new ObjectMemberValue
            {
                value = new Dictionary<string, string> { ["fileId"] = "some-value" },
            };
            var error = Assert.Throws<System.InvalidOperationException>(() =>
                BuildClient(Frame(0, ("Sprite", deeper))));
            StringAssert.Contains("field path deeper than one level", error!.Message);
        }

        [Test]
        public void Validation_AcceptsAnEmptyEnvelopeAsNoChange()
        {
            using NeoClient client = BuildClient(
                Frame(0, ("Sprite", PartialRow("{}"))));
            using var target = OpenPlacement(client, "PlacementA");
            NeoAnimationDefinition definition = NeoAnimationCompiler.Compile(target, "Idle");

            Assert.AreEqual(0, definition.SparseWritesForFrame(0).Length);
            definition.ApplyFrame(0, useResolvedState: false);
            AssertSprite(client, "a-sprite", "sheet-a", 0);
        }

        /// <summary>
        /// The cross-runtime `partialCompositions` cases, run through the REAL
        /// compiler rather than through the fixture harness's JObject mirror.
        /// Two separate claims, and the fixture states both:
        ///
        /// <list type="bullet">
        /// <item><c>authored</c> — an empty envelope produces NO write, so it
        /// can never become the frame the leaf's value is attributed to, while
        /// a declared field write produces exactly one. This is the .NET half
        /// of the web resolver's "skip the empty envelope at collection".</item>
        /// <item>An undeclared field is refused by name at export validation,
        /// which is the layer an author hears about. The composer's ignore rule
        /// (asserted in <c>NeoAnimationClipTests</c>) is the second layer, for
        /// a field list that reaches apply time anyway.</item>
        /// </list>
        ///
        /// <para>This is the one place the parity fixture and the compiler
        /// meet: the frame-resolution vectors cannot, because their harness
        /// re-implements resolution over JObjects, but these cases are about
        /// what the compiler emits and so they can.</para>
        /// </summary>
        [Test]
        public void ParityFixture_EmptyEnvelopeAuthorsNoWriteAndUndeclaredFieldsAreRefused()
        {
            var fixture = Newtonsoft.Json.Linq.JObject.Parse(
                NeoAnimationFrameResolutionParityFixture.Json);
            var compositions =
                (Newtonsoft.Json.Linq.JArray)fixture["partialCompositions"]!;
            int compiled = 0;
            int refused = 0;
            int silent = 0;

            foreach (Newtonsoft.Json.Linq.JObject composition in compositions
                         .OfType<Newtonsoft.Json.Linq.JObject>())
            {
                string kindName = composition.Value<string>("kind")!;
                string label = $"{kindName}: {composition.Value<string>("label")}";
                var current = (Newtonsoft.Json.Linq.JObject)composition["current"]!;
                var fields = (Newtonsoft.Json.Linq.JObject)composition["fields"]!;
                bool authored = composition.Value<bool>("authored");
                string memberName = ParityMemberName(kindName);
                string envelope = fields.ToString(Formatting.None);

                if (fields.Properties().Any(field => current.Property(field.Name) is null))
                {
                    var error = Assert.Throws<System.InvalidOperationException>(() =>
                        BuildClient(Frame(0, (memberName, PartialRow(envelope)))));
                    StringAssert.Contains(
                        "which is not a field of a",
                        error!.Message,
                        label);
                    refused += 1;
                    continue;
                }

                using NeoClient client = BuildClient(
                    Frame(0, (memberName, PartialRow(envelope))));
                using var target = OpenPlacement(client, "PlacementA");
                NeoAnimationDefinition definition =
                    NeoAnimationCompiler.Compile(target, "Idle");

                Assert.AreEqual(
                    authored ? 1 : 0,
                    definition.SparseWritesForFrame(0).Length,
                    label);
                compiled += 1;
                if (!authored) silent += 1;
            }

            Assert.Greater(compiled, 0, "No fixture case reached the compiler.");
            Assert.Greater(silent, 0, "No fixture case carries an empty envelope.");
            Assert.Greater(refused, 0, "No fixture case carries an undeclared field.");
        }

        /// <summary>
        /// Which member of this file's hand-built target class stands for each
        /// structured-leaf kind. Declared rather than inferred: the whole point
        /// of decision D1's envelope is that a record's shape does not name its
        /// kind.
        /// </summary>
        private static string ParityMemberName(string kindName)
        {
            return kindName switch
            {
                "Sprite" => "Sprite",
                "Vector2" => "Offset",
                "Vector2Int" => "Grid",
                "Vector3" => "Position",
                "Vector3Int" => "Cell",
                "Color" => "Tint",
                _ => throw new AssertionException(
                    $"The fixture names kind '{kindName}', which is not a structured leaf kind."),
            };
        }

        [Test]
        public void Payload_RefusesBothPathSegmentShapesByName()
        {
            var classRow = new ObjectMemberValue
            {
                id = "x",
                value = new Dictionary<string, string>(),
            };
            var classError = Assert.Throws<System.InvalidOperationException>(
                () => NeoAnimationCompiler.Payload(classRow));
            StringAssert.Contains("Class value rows are path segments", classError!.Message);

            var partialRow = (PartialLeafMemberValue)JsonConvert
                .DeserializeObject<MemberValue>(
                    "{\"id\":\"y\",\"value\":{\"~partial\":{\"sliceIndex\":1}}}")!;
            var partialError = Assert.Throws<System.InvalidOperationException>(
                () => NeoAnimationCompiler.Payload(partialRow));
            StringAssert.Contains(
                "'~partial' structured-leaf rows are path segments",
                partialError!.Message);
        }

        // ------------------------------------------------------------------
        // Reading and writing the fixture's values.
        // ------------------------------------------------------------------

        /// <summary>
        /// Counts warnings whose text contains <paramref name="fragment"/> while
        /// <paramref name="body"/> runs. Used instead of <c>LogAssert</c>, which
        /// only fails on unexpected ERROR-level logs and so cannot prove a
        /// warning was emitted exactly once.
        /// </summary>
        private static int CountWarnings(string fragment, System.Action body)
        {
            int count = 0;
            void Handler(string condition, string stackTrace, LogType type)
            {
                if (type != LogType.Warning) return;
                if (condition.Contains(fragment)) count += 1;
            }
            Application.logMessageReceived += Handler;
            try
            {
                body();
            }
            finally
            {
                Application.logMessageReceived -= Handler;
            }
            return count;
        }

        private static MemberValue Row(NeoClient client, string valueId)
        {
            MemberValue? row = client.ResolveEffectiveRow(valueId);
            Assert.IsNotNull(row, $"No value row '{valueId}'.");
            return row!;
        }

        private static void AssertSprite(
            NeoClient client,
            string valueId,
            string fileId,
            int sliceIndex)
        {
            SpriteValue? value = ((SpriteMemberValue)Row(client, valueId)).value;
            Assert.IsNotNull(value, $"'{valueId}' has no sprite value.");
            Assert.AreEqual(fileId, value!.fileId, $"{valueId}.fileId");
            Assert.AreEqual(sliceIndex, value.sliceIndex, $"{valueId}.sliceIndex");
        }

        private static string DescribeSprite(NeoClient client, string valueId)
        {
            SpriteValue value = ((SpriteMemberValue)Row(client, valueId)).value!;
            return $"{value.fileId}:{value.sliceIndex}";
        }

        private static NeoVector3Value ReadVector3(NeoClient client, string valueId)
        {
            NeoVector3Value? value = ((Vector3MemberValue)Row(client, valueId)).value;
            Assert.IsNotNull(value, $"'{valueId}' has no vector value.");
            return value!;
        }

        private static void AssertVector3(
            NeoClient client,
            string valueId,
            float x,
            float y,
            float z)
        {
            NeoVector3Value value = ReadVector3(client, valueId);
            Assert.AreEqual(x, value.x, 1e-5f, $"{valueId}.x");
            Assert.AreEqual(y, value.y, 1e-5f, $"{valueId}.y");
            Assert.AreEqual(z, value.z, 1e-5f, $"{valueId}.z");
        }

        private static void WriteSprite(
            NeoClient client,
            string placementKey,
            string fileId,
            int sliceIndex)
        {
            NeoGeneratedTypesSupport.SetValue(
                WritablePlacement(client, placementKey),
                "Sprite",
                NeoValueWritePayload.FromValue(
                    new SpriteValue { fileId = fileId, sliceIndex = sliceIndex }));
        }

        private static void WriteVector3(
            NeoClient client,
            string placementKey,
            float x,
            float y,
            float z)
        {
            NeoGeneratedTypesSupport.SetValue(
                WritablePlacement(client, placementKey),
                "Position",
                NeoValueWritePayload.FromValue(new NeoVector3Value { x = x, y = y, z = z }));
        }

        private static NeoMemberClassWritable WritablePlacement(
            NeoClient client,
            string placementKey)
        {
            return NeoGeneratedTypesSupport.AsWritable(
                client.assets.Get<NeoMemberClass>(placementKey));
        }

        private static PlacementTarget OpenPlacement(NeoClient client, string placementKey)
        {
            return new PlacementTarget(client, client.assets.Get<NeoMemberClass>(placementKey));
        }

        private sealed class PlacementTarget : NeoGeneratedClassValue
        {
            internal PlacementTarget(NeoClient client, NeoMemberClass node)
                : base(client, node, node.member.classId, isReadOnly: false)
            {
            }
        }

        // ------------------------------------------------------------------
        // Fixture: a project with one animation clip on a placement class that
        // carries every structured-leaf kind.
        // ------------------------------------------------------------------

        private sealed class FrameSpec
        {
            internal int Index;
            internal List<KeyValuePair<string, MemberValue>> Overrides = new();
        }

        private static FrameSpec Frame(
            int index,
            params (string key, MemberValue row)[] overrides)
        {
            var spec = new FrameSpec { Index = index };
            foreach ((string key, MemberValue row) in overrides)
            {
                spec.Overrides.Add(new KeyValuePair<string, MemberValue>(key, row));
            }
            return spec;
        }

        private static MemberValue PartialRow(string innerJson)
        {
            return JsonConvert.DeserializeObject<MemberValue>(
                "{\"id\":\"pending\",\"value\":{\"~partial\":" + innerJson + "}}")!;
        }

        private static MemberValue WholeSpriteRow(string fileId, int sliceIndex)
        {
            return new SpriteMemberValue
            {
                value = new SpriteValue { fileId = fileId, sliceIndex = sliceIndex },
            };
        }

        private static NeoClient BuildClient(params FrameSpec[] frames)
        {
            return NeoTestSaveStack.ClientFromSchema(BuildProjectData(frames));
        }

        private static ProjectData BuildProjectData(FrameSpec[] frames)
        {
            const string project = "project-p42";
            var members = new Dictionary<string, Member>();
            var values = new Dictionary<string, MemberValue>();

            // --- classes -------------------------------------------------
            var clipClass = new NeoSchemaClass
            {
                id = "clip-class",
                projectId = project,
                name = "Clip",
                system = Newtonsoft.Json.Linq.JObject.Parse(
                    "{\"worldKind\":\"animationClip\"}"),
                schema = new Dictionary<string, string>
                {
                    ["FPS"] = "fps-member",
                    ["Duration"] = "duration-member",
                    ["Frames"] = "frames-member",
                },
            };
            var frameClass = new NeoSchemaClass
            {
                id = "frame-class",
                projectId = project,
                name = "Frame",
                schema = new Dictionary<string, string>
                {
                    ["Index"] = "index-member",
                    ["Overrides"] = "overrides-member",
                },
            };
            var targetClass = new NeoSchemaClass
            {
                id = "target-class",
                projectId = project,
                name = "Target",
                schema = new Dictionary<string, string>
                {
                    ["Sprite"] = "sprite-member",
                    ["Locked"] = "locked-member",
                    ["Position"] = "position-member",
                    ["Offset"] = "offset-member",
                    ["Grid"] = "grid-member",
                    ["Cell"] = "cell-member",
                    ["Tint"] = "tint-member",
                    ["Count"] = "count-member",
                    ["Idle"] = "idle-member",
                },
            };
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = project,
                name = "Root",
                schema = new Dictionary<string, string>
                {
                    ["PlacementA"] = "placement-a-member",
                    ["PlacementB"] = "placement-b-member",
                },
            };
            var emptyClass = new NeoSchemaClass
            {
                id = "empty-class",
                projectId = project,
                name = "Empty",
                schema = new Dictionary<string, string>(),
            };

            // --- members -------------------------------------------------
            members["root-assets"] = ClassMemberDefinition(
                "root-assets", "Assets", rootClass.id, "root-assets-value");
            members["root-save"] = ClassMemberDefinition(
                "root-save", "Save", emptyClass.id, "root-save-value");
            members["root-session"] = ClassMemberDefinition(
                "root-session", "Session", emptyClass.id, "root-session-value");
            members["placement-a-member"] = ClassMemberDefinition(
                "placement-a-member", "PlacementA", targetClass.id, valueId: null);
            members["placement-b-member"] = ClassMemberDefinition(
                "placement-b-member", "PlacementB", targetClass.id, valueId: null);
            members["idle-member"] = ClassMemberDefinition(
                "idle-member", "Idle", clipClass.id, "idle-clip-value");
            members["overrides-member"] = ClassMemberDefinition(
                "overrides-member", "Overrides", targetClass.id, valueId: null);
            ((ClassMember)members["overrides-member"]).partial = true;
            members["frame-entry-member"] = ClassMemberDefinition(
                "frame-entry-member", "Frame", frameClass.id, valueId: null);

            members["fps-member"] = new IntMember
            {
                id = "fps-member",
                projectId = project,
                name = "FPS",
                kind = MemberKind.Int,
                required = true,
                valueId = "fps-value",
            };
            members["duration-member"] = new IntMember
            {
                id = "duration-member",
                projectId = project,
                name = "Duration",
                kind = MemberKind.Int,
                required = true,
                valueId = "duration-value",
            };
            members["index-member"] = new IntMember
            {
                id = "index-member",
                projectId = project,
                name = "Index",
                kind = MemberKind.Int,
                required = true,
            };
            members["frames-member"] = new ListMember
            {
                id = "frames-member",
                projectId = project,
                name = "Frames",
                kind = MemberKind.List,
                required = true,
                valueId = "frames-value",
                entryMemberId = "frame-entry-member",
            };
            members["sprite-member"] = new SpriteMember
            {
                id = "sprite-member",
                projectId = project,
                name = "Sprite",
                kind = MemberKind.Sprite,
                storage = "save",
            };
            members["locked-member"] = new SpriteMember
            {
                id = "locked-member",
                projectId = project,
                name = "Locked",
                kind = MemberKind.Sprite,
                storage = "immutable",
            };
            members["position-member"] = new Vector3Member
            {
                id = "position-member",
                projectId = project,
                name = "Position",
                kind = MemberKind.Vector3,
                storage = "save",
            };
            members["offset-member"] = new Vector2Member
            {
                id = "offset-member",
                projectId = project,
                name = "Offset",
                kind = MemberKind.Vector2,
                storage = "save",
            };
            members["cell-member"] = new Vector3IntMember
            {
                id = "cell-member",
                projectId = project,
                name = "Cell",
                kind = MemberKind.Vector3Int,
                storage = "save",
            };
            members["grid-member"] = new Vector2IntMember
            {
                id = "grid-member",
                projectId = project,
                name = "Grid",
                kind = MemberKind.Vector2Int,
                storage = "save",
            };
            members["tint-member"] = new ColorMember
            {
                id = "tint-member",
                projectId = project,
                name = "Tint",
                kind = MemberKind.Color,
                storage = "save",
            };
            members["count-member"] = new IntMember
            {
                id = "count-member",
                projectId = project,
                name = "Count",
                kind = MemberKind.Int,
                storage = "save",
            };

            // --- clip values ---------------------------------------------
            int duration = 1;
            var frameValueIds = new List<string>();
            foreach (FrameSpec frame in frames)
            {
                if (frame.Index + 1 > duration) duration = frame.Index + 1;
            }
            // A little headroom so backward/boomerang tests have frames past
            // the last authored one.
            duration += 1;

            values["fps-value"] = Number("fps-value", 8);
            values["duration-value"] = Number("duration-value", duration);

            foreach (FrameSpec frame in frames)
            {
                string frameId = $"frame-{frame.Index}-value";
                string indexId = $"frame-{frame.Index}-index";
                string overridesId = $"frame-{frame.Index}-overrides";
                var overrideMap = new Dictionary<string, string>();
                foreach (KeyValuePair<string, MemberValue> pair in frame.Overrides)
                {
                    string rowId = $"frame-{frame.Index}-ov-{pair.Key}";
                    pair.Value.id = rowId;
                    values[rowId] = pair.Value;
                    overrideMap[pair.Key] = rowId;
                }
                values[indexId] = Number(indexId, frame.Index);
                values[overridesId] = ObjectValue(overridesId, targetClass.id, overrideMap);
                values[frameId] = ObjectValue(
                    frameId,
                    frameClass.id,
                    new Dictionary<string, string>
                    {
                        ["Index"] = indexId,
                        ["Overrides"] = overridesId,
                    });
                frameValueIds.Add(frameId);
            }
            values["frames-value"] = new ArrayMemberValue
            {
                id = "frames-value",
                value = frameValueIds.ToArray(),
            };
            values["idle-clip-value"] = ObjectValue(
                "idle-clip-value",
                clipClass.id,
                new Dictionary<string, string>
                {
                    ["FPS"] = "fps-value",
                    ["Duration"] = "duration-value",
                    ["Frames"] = "frames-value",
                });

            // --- placements ----------------------------------------------
            AddPlacement(values, targetClass.id, "a", "sheet-a", tintIsNull: false);
            AddPlacement(values, targetClass.id, "b", "sheet-b", tintIsNull: true);
            values["root-assets-value"] = ObjectValue(
                "root-assets-value",
                rootClass.id,
                new Dictionary<string, string>
                {
                    ["PlacementA"] = "placement-a-value",
                    ["PlacementB"] = "placement-b-value",
                });
            values["root-save-value"] = ObjectValue(
                "root-save-value",
                emptyClass.id,
                new Dictionary<string, string>());
            values["root-session-value"] = ObjectValue(
                "root-session-value",
                emptyClass.id,
                new Dictionary<string, string>());

            return new ProjectData
            {
                project = new Project
                {
                    id = project,
                    _id = project,
                    name = "P42 Animation Fields",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = members,
                values = values,
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [clipClass.id] = clipClass,
                    [frameClass.id] = frameClass,
                    [targetClass.id] = targetClass,
                    [rootClass.id] = rootClass,
                    [emptyClass.id] = emptyClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static void AddPlacement(
            Dictionary<string, MemberValue> values,
            string targetClassId,
            string suffix,
            string fileId,
            bool tintIsNull)
        {
            values[$"{suffix}-sprite"] = new SpriteMemberValue
            {
                id = $"{suffix}-sprite",
                value = new SpriteValue { fileId = fileId, sliceIndex = 0 },
            };
            values[$"{suffix}-locked"] = new SpriteMemberValue
            {
                id = $"{suffix}-locked",
                value = new SpriteValue { fileId = fileId, sliceIndex = 0 },
            };
            values[$"{suffix}-position"] = new Vector3MemberValue
            {
                id = $"{suffix}-position",
                value = new NeoVector3Value { x = 0f, y = 0f, z = 0f },
            };
            values[$"{suffix}-offset"] = new Vector2MemberValue
            {
                id = $"{suffix}-offset",
                value = new NeoVector2Value { x = 0f, y = 0f },
            };
            values[$"{suffix}-cell"] = new Vector3MemberValue
            {
                id = $"{suffix}-cell",
                value = new NeoVector3Value { x = 0f, y = 0f, z = 0f },
            };
            values[$"{suffix}-grid"] = new Vector2MemberValue
            {
                id = $"{suffix}-grid",
                value = new NeoVector2Value { x = 0f, y = 0f },
            };
            values[$"{suffix}-tint"] = new ColorMemberValue
            {
                id = $"{suffix}-tint",
                value = tintIsNull
                    ? null
                    : new NeoColorValue { r = 1f, g = 1f, b = 1f, a = 1f },
            };
            values[$"{suffix}-count"] = Number($"{suffix}-count", 0);
            values[$"placement-{suffix}-value"] = ObjectValue(
                $"placement-{suffix}-value",
                targetClassId,
                new Dictionary<string, string>
                {
                    ["Sprite"] = $"{suffix}-sprite",
                    ["Locked"] = $"{suffix}-locked",
                    ["Position"] = $"{suffix}-position",
                    ["Offset"] = $"{suffix}-offset",
                    ["Grid"] = $"{suffix}-grid",
                    ["Cell"] = $"{suffix}-cell",
                    ["Tint"] = $"{suffix}-tint",
                    ["Count"] = $"{suffix}-count",
                });
        }

        private static ClassMember ClassMemberDefinition(
            string id,
            string name,
            string classId,
            string? valueId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-p42",
                name = name,
                kind = MemberKind.Class,
                required = true,
                classId = classId,
                valueId = valueId,
            };
        }

        private static NumberMemberValue Number(string id, double value)
        {
            return new NumberMemberValue { id = id, value = value };
        }

        private static ObjectMemberValue ObjectValue(
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
    }
}
