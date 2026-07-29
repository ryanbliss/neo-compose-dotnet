// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P42 row layer — the <c>$partial</c> structured-leaf envelope
    /// (decision D1).
    ///
    /// <para>Covers the four things the JSON row layer owes the rest of P42:
    /// the envelope resolves to <see cref="PartialLeafMemberValue"/> ahead of
    /// every other object probe; no existing whole-value shape changed
    /// meaning; the envelope round-trips byte-stably; and a malformed
    /// envelope is rejected here by name rather than falling through to
    /// <see cref="ObjectMemberValue"/> and failing later with a Newtonsoft
    /// message about dictionaries.</para>
    ///
    /// <para>Per-kind key legality (Sprite = fileId/sliceIndex; Vector2(Int) =
    /// x/y; Vector3(Int) = x/y/z; Color = r/g/b/a) and the colour channel
    /// <c>[0, 1]</c> rule are deliberately NOT tested here — the member kind
    /// is not available at this layer, so those belong to the kind-aware
    /// consumer.</para>
    /// </summary>
    public class NeoPartialLeafValueTests
    {
        // ------------------------------------------------------------------
        // Round-trip: read then re-write is byte-identical, per kind.
        // ------------------------------------------------------------------

        private static void AssertRoundTripsByteStably(string json)
        {
            var partial = JsonConvert.DeserializeObject<NeoPartialLeafValue>(json);
            Assert.IsNotNull(partial, $"'{json}' did not deserialize.");
            Assert.AreEqual(json, JsonConvert.SerializeObject(partial));
        }

        [Test]
        public void Envelope_RoundTripsSpriteFields()
        {
            AssertRoundTripsByteStably("{\"$partial\":{\"sliceIndex\":1}}");
            AssertRoundTripsByteStably("{\"$partial\":{\"fileId\":\"file-a\"}}");
            AssertRoundTripsByteStably(
                "{\"$partial\":{\"fileId\":\"file-a\",\"sliceIndex\":3}}");
        }

        [Test]
        public void Envelope_RoundTripsVectorFields()
        {
            AssertRoundTripsByteStably("{\"$partial\":{\"y\":0.25}}");
            AssertRoundTripsByteStably("{\"$partial\":{\"x\":1,\"y\":2}}");
            AssertRoundTripsByteStably("{\"$partial\":{\"x\":1,\"y\":2,\"z\":3}}");
            AssertRoundTripsByteStably("{\"$partial\":{\"z\":-1.5}}");
        }

        [Test]
        public void Envelope_RoundTripsColorFields()
        {
            AssertRoundTripsByteStably("{\"$partial\":{\"a\":0.5}}");
            AssertRoundTripsByteStably(
                "{\"$partial\":{\"r\":0.1,\"g\":0.2,\"b\":0.3,\"a\":1}}");
        }

        [Test]
        public void Envelope_RoundTripsEmptyEnvelope()
        {
            // {"$partial":{}} is the wire form of "no change" and is legal.
            AssertRoundTripsByteStably("{\"$partial\":{}}");
            var partial = JsonConvert.DeserializeObject<NeoPartialLeafValue>(
                "{\"$partial\":{}}")!;
            Assert.IsTrue(partial.IsEmpty);
            Assert.AreEqual(0, partial.FieldCount);
            Assert.AreEqual(0, partial.FieldKeys.Count);
        }

        [Test]
        public void Envelope_RoundTripsThroughTheStoredRow()
        {
            var row = JsonConvert.DeserializeObject<MemberValue>(
                "{\"id\":\"v1\",\"value\":{\"$partial\":{\"fileId\":\"file-a\",\"sliceIndex\":2}}}");
            Assert.IsInstanceOf<PartialLeafMemberValue>(row);

            var written = JObject.Parse(JsonConvert.SerializeObject(row));
            Assert.AreEqual(
                "{\"$partial\":{\"fileId\":\"file-a\",\"sliceIndex\":2}}",
                written["value"]!.ToString(Formatting.None));
        }

        [Test]
        public void Envelope_PreservesIntegerTokensAcrossRoundTrip()
        {
            // A double round-trip would surface any int → double widening
            // (1 becoming 1.0), which would break byte stability.
            const string json = "{\"$partial\":{\"sliceIndex\":1}}";
            var once = JsonConvert.SerializeObject(
                JsonConvert.DeserializeObject<NeoPartialLeafValue>(json));
            var twice = JsonConvert.SerializeObject(
                JsonConvert.DeserializeObject<NeoPartialLeafValue>(once));
            Assert.AreEqual(json, once);
            Assert.AreEqual(json, twice);
        }

        // ------------------------------------------------------------------
        // Shape resolution — the envelope probe runs FIRST and nothing else
        // can claim an envelope.
        // ------------------------------------------------------------------

        [Test]
        public void ShapeSniffing_EnvelopeResolvesPartialLeafMemberValue()
        {
            foreach (var inner in new[]
                     {
                         "{}",
                         "{\"sliceIndex\":1}",
                         "{\"fileId\":\"file-a\"}",
                         "{\"fileId\":\"file-a\",\"sliceIndex\":1}",
                         "{\"x\":1,\"y\":2}",
                         "{\"x\":1,\"y\":2,\"z\":3}",
                         "{\"r\":1,\"g\":1,\"b\":1,\"a\":1}",
                     })
            {
                var row = JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v1\",\"value\":{\"$partial\":" + inner + "}}");
                Assert.IsInstanceOf<PartialLeafMemberValue>(
                    row,
                    $"envelope with inner {inner} resolved to {row?.GetType().Name}");
                Assert.IsNotInstanceOf<FileMemberValue>(row);
                Assert.IsNotInstanceOf<SpriteMemberValue>(row);
                Assert.IsNotInstanceOf<Vector2MemberValue>(row);
                Assert.IsNotInstanceOf<Vector3MemberValue>(row);
                Assert.IsNotInstanceOf<ColorMemberValue>(row);
                Assert.IsNotInstanceOf<ObjectMemberValue>(row);
            }
        }

        // ------------------------------------------------------------------
        // Position — decision D10. A MemberValueBase is only ever a
        // Member.defaultValue, which is never an animation override graph, so
        // an envelope there is rejected rather than swallowed.
        // ------------------------------------------------------------------

        [Test]
        public void DeclarationDefault_RejectsAnEnvelopeInAnUntypedCarrier()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<MemberValueBase>(
                    "{\"value\":{\"$partial\":{\"sliceIndex\":1}}}"));
            StringAssert.Contains("member declaration default", error!.ToString());
            StringAssert.Contains("animation override graph", error.ToString());
        }

        [Test]
        public void DeclarationDefault_RejectsAnEnvelopeInATypedCarrier()
        {
            // The typed path picks its concrete from the declared kind, not
            // the wire shape, so before D10 was enforced this envelope was fed
            // straight into SpriteValue and silently became "no value".
            var error = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<MemberValueBase<SpriteValue?>>(
                    "{\"value\":{\"$partial\":{\"sliceIndex\":1}}}"));
            StringAssert.Contains("member declaration default", error!.ToString());
        }

        [Test]
        public void DeclarationDefault_RejectsAnEnvelopeNamingTheMemberAndKind()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<NeoCompose.Runtime.Json.Member>(
                    "{\"id\":\"portrait-member\",\"projectId\":\"p\",\"name\":\"Portrait\","
                    + "\"kind\":11,\"isStatic\":false,\"accessModifierKind\":\"public\","
                    + "\"defaultValue\":{\"value\":{\"$partial\":{\"sliceIndex\":1}}}}"));
            StringAssert.Contains("SpriteMember 'Portrait' (portrait-member)", error!.ToString());
            StringAssert.Contains("animation override graph", error.ToString());
        }

        [Test]
        public void DeclarationDefault_LeavesWholeValueDefaultsAlone()
        {
            var member = JsonConvert.DeserializeObject<NeoCompose.Runtime.Json.Member>(
                "{\"id\":\"portrait-member\",\"projectId\":\"p\",\"name\":\"Portrait\","
                + "\"kind\":11,\"isStatic\":false,\"accessModifierKind\":\"public\","
                + "\"defaultValue\":{\"value\":{\"fileId\":\"file-a\",\"sliceIndex\":1}}}");
            var sprite = (SpriteMember)member!;
            Assert.AreEqual("file-a", sprite.defaultValue!.value!.fileId);
            Assert.AreEqual(1, sprite.defaultValue.value.sliceIndex);
        }

        [Test]
        public void DeclarationDefault_LeavesADictionaryDefaultWithADollarPartialEntryAlone()
        {
            // A Dictionary default is Dictionary<string, string>, so a
            // '$partial' entry there is a string beside other strings, not an
            // envelope — exactly the distinction IsEnvelope draws.
            var carrier = JsonConvert.DeserializeObject<MemberValueBase>(
                "{\"value\":{\"$partial\":\"child-value\",\"Other\":\"x\"}}");
            Assert.IsInstanceOf<ObjectMemberValueBase>(carrier);
        }

        [Test]
        public void ShapeSniffing_WholeValueShapesAreUnchanged()
        {
            // The whole point of the envelope: a bare {"fileId":"…"} is still
            // an Audio/File value, exactly as before P42.
            Assert.IsInstanceOf<FileMemberValue>(
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v1\",\"value\":{\"fileId\":\"x\"}}"));
            Assert.IsInstanceOf<SpriteMemberValue>(
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v2\",\"value\":{\"fileId\":\"x\",\"sliceIndex\":0}}"));
            Assert.IsInstanceOf<Vector2MemberValue>(
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v3\",\"value\":{\"x\":1,\"y\":2}}"));
            Assert.IsInstanceOf<Vector3MemberValue>(
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v4\",\"value\":{\"x\":1,\"y\":2,\"z\":3}}"));
            Assert.IsInstanceOf<ColorMemberValue>(
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v5\",\"value\":{\"r\":0,\"g\":0,\"b\":0,\"a\":1}}"));
            Assert.IsInstanceOf<ObjectMemberValue>(
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v6\",\"value\":{\"Child\":\"child-value\"}}"));
        }

        [Test]
        public void ShapeSniffing_DictionaryRowWithADollarPartialEntryIsUnaffected()
        {
            // Class / Dictionary rows are Dictionary<string, string>, so a
            // '$partial' entry there is a string next to other strings — never
            // an envelope. It must keep resolving to ObjectMemberValue.
            var row = JsonConvert.DeserializeObject<MemberValue>(
                "{\"id\":\"v1\",\"value\":{\"$partial\":\"child-value\",\"Other\":\"x\"}}");
            Assert.IsInstanceOf<ObjectMemberValue>(row);
            Assert.AreEqual("child-value", ((ObjectMemberValue)row!).value!["$partial"]);
        }

        [Test]
        public void ShapeSniffing_WholeValueProbesRejectAnEnvelope()
        {
            var envelope = JObject.Parse("{\"$partial\":{\"x\":1,\"y\":2}}");
            Assert.IsFalse(NeoVector2ValueConverter.LooksLikeVector2Value(envelope));
            Assert.IsFalse(NeoVector3ValueConverter.LooksLikeVector3Value(envelope));
            Assert.IsFalse(NeoColorValueConverter.LooksLikeColorValue(envelope));
        }

        // ------------------------------------------------------------------
        // Shape validation — what this layer CAN check, it checks.
        // ------------------------------------------------------------------

        [Test]
        public void Envelope_RejectsSiblingKeys()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v1\",\"value\":{\"$partial\":{\"x\":1},\"y\":2}}"));
            StringAssert.Contains("exactly one '$partial' key", error!.ToString());
        }

        [Test]
        public void Envelope_RejectsNonObjectPayload()
        {
            foreach (var payload in new[] { "5", "\"x\"", "null", "[]", "true" })
            {
                var error = Assert.Throws<JsonSerializationException>(() =>
                    JsonConvert.DeserializeObject<MemberValue>(
                        "{\"id\":\"v1\",\"value\":{\"$partial\":" + payload + "}}"));
                StringAssert.Contains(
                    "'$partial' must be an object of scalar field values",
                    error!.ToString());
            }
        }

        [Test]
        public void Envelope_RejectsNonScalarField()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v1\",\"value\":{\"$partial\":{\"x\":{\"nested\":1}}}}"));
            StringAssert.Contains("field 'x' must be a string or a number", error!.ToString());

            var arrayError = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v1\",\"value\":{\"$partial\":{\"y\":[1]}}}"));
            StringAssert.Contains("field 'y' must be a string or a number", arrayError!.ToString());

            var boolError = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v1\",\"value\":{\"$partial\":{\"z\":true}}}"));
            StringAssert.Contains("field 'z' must be a string or a number", boolError!.ToString());
        }

        [Test]
        public void Envelope_RejectsNonFiniteField()
        {
            var error = Assert.Throws<JsonSerializationException>(() =>
                JsonConvert.DeserializeObject<MemberValue>(
                    "{\"id\":\"v1\",\"value\":{\"$partial\":{\"x\":NaN}}}"));
            StringAssert.Contains("field 'x' must be a finite number", error!.ToString());
        }

        [Test]
        public void Envelope_AllowsExplicitNullField()
        {
            // An absent field is "leave alone"; an explicit null is a write.
            // Whether null is legal for a given field is a kind-aware question.
            var partial = JsonConvert.DeserializeObject<NeoPartialLeafValue>(
                "{\"$partial\":{\"fileId\":null}}")!;
            Assert.IsTrue(partial.HasField("fileId"));
            Assert.IsTrue(partial.IsNullField("fileId"));
            Assert.IsFalse(partial.TryGetString("fileId", out _));
        }

        // ------------------------------------------------------------------
        // The two questions the animation compiler asks.
        // ------------------------------------------------------------------

        [Test]
        public void FieldKeys_ReportWrittenFieldsInWireOrder()
        {
            var partial = JsonConvert.DeserializeObject<NeoPartialLeafValue>(
                "{\"$partial\":{\"z\":3,\"x\":1}}")!;
            Assert.AreEqual(2, partial.FieldCount);
            Assert.IsFalse(partial.IsEmpty);
            CollectionAssert.AreEqual(new[] { "z", "x" }, partial.FieldKeys);
            Assert.IsTrue(partial.HasField("x"));
            Assert.IsFalse(partial.HasField("y"));
        }

        [Test]
        public void FieldAccessors_ReadTypedValues()
        {
            var partial = JsonConvert.DeserializeObject<NeoPartialLeafValue>(
                "{\"$partial\":{\"fileId\":\"file-a\",\"sliceIndex\":4,\"y\":0.25}}")!;

            Assert.IsTrue(partial.TryGetString("fileId", out var fileId));
            Assert.AreEqual("file-a", fileId);

            Assert.IsTrue(partial.TryGetInt32("sliceIndex", out var sliceIndex));
            Assert.AreEqual(4, sliceIndex);

            Assert.IsTrue(partial.TryGetSingle("y", out var y));
            Assert.AreEqual(0.25f, y);

            Assert.IsTrue(partial.TryGetDouble("y", out var yDouble));
            Assert.AreEqual(0.25d, yDouble);

            // Wrong type / absent both report false rather than throwing.
            Assert.IsFalse(partial.TryGetString("sliceIndex", out _));
            Assert.IsFalse(partial.TryGetDouble("fileId", out _));
            Assert.IsFalse(partial.TryGetSingle("x", out _));
            Assert.IsFalse(partial.TryGetInt32("y", out _), "0.25 is not an int");
        }

        [Test]
        public void Setters_BuildAnEnvelopeThatSerializesInWriteOrder()
        {
            var partial = new NeoPartialLeafValue();
            Assert.IsTrue(partial.IsEmpty);

            partial.SetString("fileId", "file-a");
            partial.SetInt32("sliceIndex", 2);
            CollectionAssert.AreEqual(new[] { "fileId", "sliceIndex" }, partial.FieldKeys);
            Assert.AreEqual(
                "{\"$partial\":{\"fileId\":\"file-a\",\"sliceIndex\":2}}",
                JsonConvert.SerializeObject(partial));

            partial.SetInt32("sliceIndex", 7);
            Assert.AreEqual(2, partial.FieldCount);
            Assert.IsTrue(partial.TryGetInt32("sliceIndex", out var slice));
            Assert.AreEqual(7, slice);

            Assert.IsTrue(partial.RemoveField("fileId"));
            Assert.IsFalse(partial.RemoveField("fileId"));
            CollectionAssert.AreEqual(new[] { "sliceIndex" }, partial.FieldKeys);
        }

        [Test]
        public void Clone_IsIndependent()
        {
            var partial = JsonConvert.DeserializeObject<NeoPartialLeafValue>(
                "{\"$partial\":{\"x\":1}}")!;
            var clone = partial.Clone();
            clone.SetDouble("x", 9d);

            Assert.IsTrue(partial.TryGetDouble("x", out var original));
            Assert.AreEqual(1d, original);
            Assert.IsTrue(clone.TryGetDouble("x", out var copied));
            Assert.AreEqual(9d, copied);
        }

        [Test]
        public void SetDouble_RejectsNonFinite()
        {
            var partial = new NeoPartialLeafValue();
            Assert.Throws<System.ArgumentException>(
                () => partial.SetDouble("x", double.NaN));
            Assert.Throws<System.ArgumentException>(
                () => partial.SetDouble("x", double.PositiveInfinity));
        }

        // ------------------------------------------------------------------
        // Reachability — a partial row under a Sprite-declared member does
        // not throw and does not disappear.
        // ------------------------------------------------------------------

        [Test]
        public void PartialRowUnderSpriteMember_IsReachableWithoutCastFailure()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());

            NeoMemberSprite? sprite = null;
            Assert.DoesNotThrow(
                () => sprite = client.assets.Get<NeoMemberSprite>("Portrait"));
            Assert.IsNotNull(sprite);

            // The typed whole-value accessor cannot hold a partial row, so it
            // stays null — the untyped accessor is where the row lives.
            Assert.IsNull(sprite!.value);
            Assert.IsTrue(sprite.hasPartialLeafValue);
            Assert.IsNotNull(sprite.partialLeafValue);

            var partial = sprite.partialLeafValue!.value!;
            CollectionAssert.AreEqual(new[] { "sliceIndex" }, partial.FieldKeys);
            Assert.IsTrue(partial.TryGetInt32("sliceIndex", out var sliceIndex));
            Assert.AreEqual(3, sliceIndex);
        }

        [Test]
        public void WholeValueRowUnderSpriteMember_LeavesPartialAccessorNull()
        {
            var client = NeoTestSaveStack.ClientFromSchema(BuildProjectData());
            var sprite = client.assets.Get<NeoMemberSprite>("Banner");

            Assert.IsNotNull(sprite.value);
            Assert.AreEqual("file-b", sprite.value!.value!.fileId);
            Assert.IsFalse(sprite.hasPartialLeafValue);
            Assert.IsNull(sprite.partialLeafValue);
        }

        // ------------------------------------------------------------------
        // Fixture.
        // ------------------------------------------------------------------

        private static ProjectData BuildProjectData()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>
                {
                    ["Portrait"] = "portrait-member",
                    ["Banner"] = "banner-member",
                },
            };
            var emptyClass = new NeoSchemaClass
            {
                id = "empty-class",
                projectId = "project-a",
                name = "Empty",
                schema = new Dictionary<string, string>(),
            };

            return new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Partial Leaf Rows",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", emptyClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", emptyClass.id),
                    ["portrait-member"] = SpriteMemberDefinition("portrait-member", "Portrait"),
                    ["banner-member"] = SpriteMemberDefinition("banner-member", "Banner"),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue(
                        "root-assets-value",
                        rootClass.id,
                        new Dictionary<string, string>
                        {
                            ["Portrait"] = "portrait-value",
                            ["Banner"] = "banner-value",
                        }),
                    ["root-save-value"] = ObjectValue(
                        "root-save-value",
                        emptyClass.id,
                        new Dictionary<string, string>()),
                    ["root-session-value"] = ObjectValue(
                        "root-session-value",
                        emptyClass.id,
                        new Dictionary<string, string>()),
                    ["portrait-value"] = (PartialLeafMemberValue)JsonConvert
                        .DeserializeObject<MemberValue>(
                            "{\"id\":\"portrait-value\",\"value\":{\"$partial\":{\"sliceIndex\":3}}}")!,
                    ["banner-value"] = new SpriteMemberValue
                    {
                        id = "banner-value",
                        value = new SpriteValue { fileId = "file-b", sliceIndex = 0 },
                    },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [emptyClass.id] = emptyClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            };
        }

        private static ClassMember RootMember(string id, string valueId, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-a",
                name = id,
                kind = MemberKind.Class,
                required = true,
                valueId = valueId,
                classId = classId,
            };
        }

        private static SpriteMember SpriteMemberDefinition(string id, string name)
        {
            return new SpriteMember
            {
                id = id,
                projectId = "project-a",
                name = name,
                kind = MemberKind.Sprite,
                required = false,
            };
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
