// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §15 "Local saves": a save below revision 3 is refused, with no
    /// load-time upgrade, and a save at revision 3 is validated strictly.
    /// </summary>
    public class P127SaveFormatRevisionTests
    {
        private const string ProjectId = "p127-save";
        private const string BoxParam = "box-t";

        [Test]
        public void FreshSavesCarryRevisionThree()
        {
            using NeoClient client = BuildClient();
            Assert.That(Revision(client), Is.EqualTo(NeoSaveFormat.P127Revision));
            Assert.That(NeoSaveFormat.SupportedRevision, Is.EqualTo(NeoSaveFormat.P127Revision));
        }

        [TestCase(NeoSaveFormat.LegacyRevision)]
        [TestCase(NeoSaveFormat.ListenerRevision)]
        public void OlderSavesAreRefusedNamingBothRevisions(int revision)
        {
            // No load-time upgrade (P127 §15): even a save every value of
            // which revision 3 accepts is refused, and nothing is written.
            JObject save = SaveWith(revision, new NumberMemberValue { id = "count", value = 3_000_000_000d });
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(save));
            Assert.That(error!.RequiredRevision, Is.EqualTo(revision));
            Assert.That(error.Message, Is.EqualTo(
                $"This save has format revision {revision}, and this SDK reads only revision 3 saves; it doesn't upgrade older ones. Delete the save to start a new one. The save was not loaded."));
            Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(SaveWith(revision)));
        }

        [Test]
        public void UnmarkedSavesAreRefusedAsRevisionOne()
        {
            JObject save = SaveWith(null, new NumberMemberValue { id = "count", value = 5 });
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(save));
            Assert.That(error!.RequiredRevision, Is.EqualTo(NeoSaveFormat.LegacyRevision));
            StringAssert.Contains("This save has format revision 1, and this SDK reads only revision 3 saves", error.Message);
        }

        [Test]
        public void SavesAboveTheSupportedRevisionStayRefused()
        {
            JObject save = SaveWith(NeoSaveFormat.SupportedRevision + 1);
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(save));
            StringAssert.Contains("This save requires format revision 4. Upgrade the Neo Compose SDK", error!.Message);
        }

        [Test]
        public void NullInANullableSlotIsLeftAlone()
        {
            JObject save = SaveWith(NeoSaveFormat.P127Revision, new NullMemberValue { id = "maybe" });
            using NeoClient client = BuildClient(save);
            Assert.That(Row<MemberValue>(client, "maybe"), Is.InstanceOf<NullMemberValue>());
        }

        [Test]
        public void RevisionThreeSavesRefuseNullInEveryNonNullableSlot()
        {
            JObject save = SaveWith(NeoSaveFormat.P127Revision, new NullMemberValue { id = "count" });
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(save));
            StringAssert.Contains("Save value 'count' (Count) holds null in a non-nullable slot.", error!.Message);
        }

        [Test]
        public void FloatSlotsDecodeNegativeZeroAndDecimalSlotsKeepIt()
        {
            JObject save = SaveWith(
                NeoSaveFormat.P127Revision,
                new StringMemberValue { id = "ratio", value = "-0" },
                new StringMemberValue { id = "price", value = "-0" },
                new ArrayMemberValue { id = "ratios", value = new[] { "ratios-0" } },
                new StringMemberValue { id = "ratios-0", value = "-0" },
                new ObjectMemberValue { id = "weights", value = new Dictionary<string, string> { ["a"] = "weights-a" } },
                new StringMemberValue { id = "weights-a", value = "NaN" });
            using NeoClient client = BuildClient(save);
            AssertNegativeZero(Row<NumberMemberValue>(client, "ratio"));
            AssertNegativeZero(Row<NumberMemberValue>(client, "ratios-0"));
            Assert.That(Row<NumberMemberValue>(client, "weights-a").value, Is.NaN);
            Assert.That(Row<StringMemberValue>(client, "price").value, Is.EqualTo("-0"));
        }

        [Test]
        public void AuthoredFloatSlotsDecodeNegativeZeroAndDecimalSlotsKeepIt()
        {
            using NeoClient client = BuildClient(configure: data =>
            {
                data.values["ratio"] = new StringMemberValue { id = "ratio", value = "-0" };
                data.values["price"] = new StringMemberValue { id = "price", value = "-0" };
            });
            AssertNegativeZero(Row<NumberMemberValue>(client, "ratio"));
            Assert.That(Row<StringMemberValue>(client, "price").value, Is.EqualTo("-0"));
        }

        [Test]
        public void FloatNegativeZeroWritesTheStringAndRoundTrips()
        {
            JObject written;
            using (NeoClient client = BuildClient(configure: AddFloatPositions))
            {
                client.save.Get<NeoMemberFloatWritable>("Ratio").Set(-0f);
                client.save.Get<NeoMemberVector2Writable>("Aim").Set(new Vector2(-0f, 1.5f));
                client.save.Get<NeoMemberColorWritable>("Tint").Set(new Color(-0f, 0.5f, 1f, 1f));
                client.save.Get<NeoMemberListWritable>("Ratios").AddSerialized(NeoValueWritePayload.FromValue(-0d));
                client.save.Get<NeoMemberDictionaryWritable>("Weights").SetSerialized("a", NeoValueWritePayload.FromValue(-0d));
                written = JObject.Parse(client.SerializeSaveData());
            }

            var values = (JObject)written["values"]!;
            string entryId = values["ratios"]!["value"]![0]!.Value<string>()!;
            string weightId = values["weights"]!["value"]!["a"]!.Value<string>()!;
            AssertNegativeZeroString(values["ratio"]!["value"]);
            AssertNegativeZeroString(values["aim"]!["value"]!["x"]);
            Assert.That(values["aim"]!["value"]!["y"]!.Type, Is.EqualTo(JTokenType.Float));
            AssertNegativeZeroString(values["tint"]!["value"]!["r"]);
            AssertNegativeZeroString(values[entryId]!["value"]);
            AssertNegativeZeroString(values[weightId]!["value"]);

            using NeoClient reloaded = BuildClient(written, AddFloatPositions);
            AssertNegativeZero(Row<NumberMemberValue>(reloaded, "ratio"));
            AssertNegativeZero(Row<NumberMemberValue>(reloaded, entryId));
            AssertNegativeZero(Row<NumberMemberValue>(reloaded, weightId));
            Assert.That(double.IsNegative(Row<Vector2MemberValue>(reloaded, "aim").value!.x), Is.True);
            Assert.That(float.IsNegative(Row<ColorMemberValue>(reloaded, "tint").value!.r), Is.True);
            Assert.That(reloaded.SerializeSaveData(), Is.EqualTo(written.ToString(Formatting.None)));
        }

        [Test]
        public void GenericStaticStateWritesNegativeZeroAsTheString()
        {
            JObject save = SaveWith(
                NeoSaveFormat.P127Revision,
                new StringMemberValue { id = "shared", value = "-0" });
            save["staticConstructions"] = Constructions(("box-shared-field", BoxOf(TestTypeBindings.Of(MemberKind.Float)), "shared"));
            using NeoClient client = BuildClient(save);
            AssertNegativeZero(Row<NumberMemberValue>(client, "shared"));
            JObject written = JObject.Parse(client.SerializeSaveData());
            AssertNegativeZeroString(written["values"]!["shared"]!["value"]);
            Assert.That(JToken.DeepEquals(written["staticConstructions"], save["staticConstructions"]), Is.True);
        }

        [Test]
        public void BoxedFloatsWriteNegativeZeroAsTheString()
        {
            var row = JsonConvert.DeserializeObject<MemberValue>("{'id':'held','valueType':{'kind':0,'type':{'type':4,'required':true}},'value':'-0'}")!;
            AssertNegativeZero((NumberMemberValue)row);
            JObject written = JObject.Parse(JsonConvert.SerializeObject(row));
            AssertNegativeZeroString(written["value"]);
            Assert.That(JToken.DeepEquals(written["valueType"], TestTypeBindings.JsonFromKey("float")), Is.True);
        }

        [Test]
        public void ConstructorArgumentsRoundTripFloatsThroughTheCodec()
        {
            TypeInfo floats = ListOf(Primitive(MemberKind.Float));
            AssertNegativeZeroString(NeoClient.ConstructorArgumentToken(-0f, Primitive(MemberKind.Float), "ratio"));
            Assert.That(NeoClient.ConstructorArgumentToken(double.NaN, Primitive(MemberKind.Float), "ratio")!.Value<string>(), Is.EqualTo("NaN"));
            Assert.That(NeoClient.ConstructorArgumentToken(1.5f, Primitive(MemberKind.Float), "ratio")!.Type, Is.EqualTo(JTokenType.Float));
            var entries = (JArray)NeoClient.ConstructorArgumentToken(new object?[] { -0d, 2d }, floats, "ratios")!;
            AssertNegativeZeroString(entries[0]);
            Assert.That(entries[1].Value<double>(), Is.EqualTo(2d));
            // An int's -0 is a number: only a float position writes the symbol.
            Assert.That(NeoClient.ConstructorArgumentToken(0d * -1d, Primitive(MemberKind.Int), "count")!.Type, Is.Not.EqualTo(JTokenType.String));
            var ints = (JArray)NeoClient.ConstructorArgumentToken(new object?[] { 0d * -1d }, ListOf(Primitive(MemberKind.Int)), "counts")!;
            Assert.That(ints[0].Type, Is.Not.EqualTo(JTokenType.String));

            Assert.That(double.IsNegative(NeoClient.StoredFloatArgument(NeoClient.ConstructorArgumentToken(-0f, Primitive(MemberKind.Float), "ratio")!)), Is.True);
            Assert.That(NeoClient.StoredFloatArgument(new JValue("NaN")), Is.NaN);
            var decoded = (object?[])NeoStoredNumberJsonConverter.ReadTyped(NeoJsonRuntimeValue.From(entries), floats)!;
            Assert.That(double.IsNegative((double)decoded[0]!), Is.True);
            Assert.That(decoded[1], Is.EqualTo(2d));
            Assert.Throws<System.InvalidOperationException>(() => NeoClient.StoredFloatArgument(new JValue("zero")));
        }

        [TestCase("79228162514264337593543950335")]
        [TestCase("-79228162514264337593543950335")]
        [TestCase("0.1234567890123456789012345678")]
        [TestCase("-0.1234567890123456789012345678")]
        [TestCase("-0")]
        [TestCase("-0.00")]
        public void ConstructorArgumentsRoundTripDecimalsAtTheEnvelopeEdges(string text)
        {
            decimal exact = NeoDecimalValues.Parse(text);
            Assert.That(NeoDecimalValues.Format(exact), Is.EqualTo(text));
            TypeInfo decimalType = Primitive(MemberKind.Decimal);

            JToken scalar = NeoClient.ConstructorArgumentToken(exact, decimalType, "amount")!;
            Assert.That(scalar.Type, Is.EqualTo(JTokenType.String));
            Assert.That(NeoClient.StoredDecimalArgument(scalar), Is.EqualTo(text));
            // The evaluator's own form, a canonical string, writes as itself.
            Assert.That(NeoClient.ConstructorArgumentToken(text, decimalType, "amount")!.Value<string>(), Is.EqualTo(text));

            TypeInfo decimals = ListOf(decimalType);
            var entries = (JArray)NeoClient.ConstructorArgumentToken(new object?[] { exact, 1.5m }, decimals, "amounts")!;
            Assert.That(entries.ToString(Formatting.None), Is.EqualTo($"[\"{text}\",\"1.5\"]"));
            CollectionAssert.AreEqual(
                new object?[] { text, "1.5" },
                (object?[])NeoStoredNumberJsonConverter.ReadTyped(NeoJsonRuntimeValue.From(entries), decimals)!);

            TypeInfo nested = ListOf(ListOf(decimalType));
            var rows = (JArray)NeoClient.ConstructorArgumentToken(new List<List<decimal>> { new() { exact } }, nested, "rows")!;
            Assert.That(rows[0]![0]!.Value<string>(), Is.EqualTo(text));

            var byName = (JObject)NeoClient.ConstructorArgumentToken(
                new Dictionary<string, decimal> { ["edge"] = exact },
                new CollectionTypeInfo { type = MemberKind.Dictionary, required = true, entryTypeInfo = decimalType },
                "byName")!;
            Assert.That(byName.Value<string>("edge"), Is.EqualTo(text));
        }

        [Test]
        public void StoredDecimalArgumentsRefuseNonCanonicalTextAndReadLegacyNumbers()
        {
            Assert.That(NeoClient.StoredDecimalArgument(new JValue(2.5d)), Is.EqualTo("2.5"));
            Assert.That(NeoClient.StoredDecimalArgument(new JValue(3L)), Is.EqualTo("3"));
            Assert.Throws<System.InvalidOperationException>(() => NeoClient.StoredDecimalArgument(new JValue("1e3")));
            Assert.Throws<System.InvalidOperationException>(() => NeoClient.StoredDecimalArgument(new JValue("79228162514264337593543950336")));
            Assert.Throws<System.InvalidOperationException>(() => NeoClient.StoredDecimalArgument(new JValue(true)));
            // A legacy number inside a list reads back as its canonical string too.
            CollectionAssert.AreEqual(
                new object?[] { "2.5", "3" },
                (object?[])NeoStoredNumberJsonConverter.ReadTyped(new object?[] { 2.5d, 3L }, ListOf(Primitive(MemberKind.Decimal)))!);
        }

        private static PrimitiveTypeInfo Primitive(MemberKind kind) => new()
        {
            type = kind,
            required = true,
        };

        private static CollectionTypeInfo ListOf(TypeInfo entry) => new()
        {
            type = MemberKind.List,
            required = true,
            entryTypeInfo = entry,
        };

        [Test]
        public void RevisionThreeSavesRefuseAnOutOfRangeGenericStaticConstruction()
        {
            JObject save = SaveWith(NeoSaveFormat.P127Revision, new NumberMemberValue { id = "shared", value = 3_000_000_000d });
            save["staticConstructions"] = Constructions(("box-shared-field", BoxOf(TestTypeBindings.Of(MemberKind.Int)), "shared"));
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(save));
            StringAssert.Contains("Save value 'shared' (Shared) holds 3000000000, which is outside the int range.", error!.Message);
        }

        // Hold<int>'s Wrap<List<H>> Listed holds a ListWrap<L> : Wrap<List<L>>,
        // so the row's L Item is an int through its owner, whether the
        // ListWrap row is authored or only saved.
        [TestCase("listed")]
        [TestCase("listed-9")]
        public void RevisionThreeSavesRefuseAnIntItsOwnerCloses(string listedId)
        {
            var held = new ObjectMemberValue
            {
                id = "held",
                classId = "hold-class",
                value = new Dictionary<string, string> { ["Listed"] = listedId },
            };
            var listed = new ObjectMemberValue
            {
                id = listedId,
                classId = "list-wrap-class",
                value = new Dictionary<string, string> { ["Item"] = "item-9" },
            };
            var item = new NumberMemberValue { id = "item-9", value = 3_000_000_000d };
            JObject save = SaveWith(NeoSaveFormat.P127Revision, AddWrappedHold, held, listed, item);
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(save, AddWrappedHold));
            StringAssert.Contains("Save value 'item-9' (Item) holds 3000000000, which is outside the int range.", error!.Message);
        }

        [Test]
        public void RevisionThreeSavesRefuseAnOutOfRangePartitionRowWhenItsPartitionLoads()
        {
            JObject save = SaveWith(NeoSaveFormat.P127Revision, new NumberMemberValue { id = "late", value = 3_000_000_000d });
            using NeoClient client = BuildClient(save, AddLatePartition);
            var error = Assert.Throws<NeoSaveValueRefusedException>(() => client.LoadValuePartition("garden"));
            StringAssert.Contains("Save value 'late' (Late) holds 3000000000, which is outside the int range.", error!.Message);
            StringAssert.Contains("Value partition 'garden' was not loaded.", error.Message);
            StringAssert.DoesNotContain("The save was not loaded.", error.Message);
        }

        [Test]
        public void ARefusedPartitionLoadChangesNothingAndOtherPartitionsStillLoad()
        {
            JObject save = SaveWith(NeoSaveFormat.P127Revision, new NumberMemberValue { id = "late", value = 3_000_000_000d });
            using NeoClient client = BuildClient(save, AddGardenAndOrchard);
            var changed = new List<string>();
            client.OnValuePartitionChanged += changed.Add;

            Assert.Throws<NeoSaveValueRefusedException>(() => client.LoadValuePartition("garden"));
            Assert.That(client.IsValuePartitionLoaded("garden"), Is.False);
            Assert.That(client.LoadedValuePartitions, Is.Empty);
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, "plot", out _), Is.False);
            Assert.That(Row<NumberMemberValue>(client, "late").value, Is.EqualTo(3_000_000_000d));
            Assert.That(changed, Is.Empty);

            client.LoadValuePartition("orchard");
            Assert.That(client.IsValuePartitionLoaded("orchard"), Is.True);
            Assert.That(Row<NumberMemberValue>(client, "late-2").value, Is.EqualTo(0d));
            Assert.That(changed, Is.EqualTo(new[] { "orchard" }));

            // The refused row still waits for its partition, which refuses again.
            Assert.Throws<NeoSaveValueRefusedException>(() => client.LoadValuePartition("garden"));
            Assert.That(client.IsValuePartitionLoaded("garden"), Is.False);
            Assert.That(Revision(client), Is.EqualTo(NeoSaveFormat.P127Revision));
        }

        [TestCase(NeoSaveFormat.ListenerRevision)]
        [TestCase(null)]
        public void ExternalContentBelowRevisionThreeIsRefused(int? revision)
        {
            using NeoClient client = BuildClient();
            JObject content = SaveWith(revision, new NumberMemberValue { id = "count", value = 7 });
            var error = Assert.Throws<NeoSaveValueRefusedException>(
                () => client.ApplyExternalSaveContent(content.ToString(Formatting.None)));
            Assert.That(error!.RequiredRevision, Is.EqualTo(revision ?? NeoSaveFormat.LegacyRevision));
            StringAssert.Contains(
                $"This save has format revision {revision ?? NeoSaveFormat.LegacyRevision}, and this SDK reads only revision 3 saves",
                error.Message);
            StringAssert.Contains("The incoming save content was not applied.", error.Message);
            Assert.That(Row<NumberMemberValue>(client, "count").value, Is.EqualTo(0d));
        }

        [Test]
        public void ExternalRevisionThreeContentDecodesItsSlotForms()
        {
            using NeoClient client = BuildClient();
            JObject content = SaveWith(
                NeoSaveFormat.P127Revision,
                new NumberMemberValue { id = "count", value = 7 },
                new StringMemberValue { id = "ratio", value = "-0" });
            client.ApplyExternalSaveContent(content.ToString(Formatting.None));
            Assert.That(Row<NumberMemberValue>(client, "count").value, Is.EqualTo(7d));
            AssertNegativeZero(Row<NumberMemberValue>(client, "ratio"));
            Assert.That(Revision(client), Is.EqualTo(NeoSaveFormat.P127Revision));
        }

        [Test]
        public void ExternalRevisionThreeContentIsValidatedStrictly()
        {
            using NeoClient client = BuildClient();
            JObject content = SaveWith(NeoSaveFormat.P127Revision, new NullMemberValue { id = "count" });
            var error = Assert.Throws<NeoSaveValueRefusedException>(
                () => client.ApplyExternalSaveContent(content.ToString(Formatting.None)));
            StringAssert.Contains("Save value 'count' (Count) holds null in a non-nullable slot.", error!.Message);
            StringAssert.Contains("The incoming save content was not applied.", error.Message);
            Assert.That(Row<NumberMemberValue>(client, "count").value, Is.EqualTo(0d));
        }

        [Test]
        public void ASavedRowIsPlacedThroughItsParentsField()
        {
            // A plot the player added lives only in the save, so its rows have
            // no authored slot; the plot's class declares them. The optional
            // Spare holds null, which is left alone, and nothing waits.
            MemberValue[] Plot(double late) => new MemberValue[]
            {
                new ArrayMemberValue { id = "plots", value = new[] { "plot-9" } },
                new ObjectMemberValue
                {
                    id = "plot-9",
                    classId = "plot-class",
                    containerId = "plots",
                    value = new Dictionary<string, string> { ["Late"] = "late-9", ["Spare"] = "spare-9" },
                },
                new NumberMemberValue { id = "late-9", value = late },
                new NullMemberValue { id = "spare-9" },
            };
            using (NeoClient client = BuildClient(SaveWith(NeoSaveFormat.P127Revision, AddLatePartition, Plot(-4)), AddLatePartition))
            {
                Assert.That(Row<NumberMemberValue>(client, "late-9").value, Is.EqualTo(-4d));
                Assert.That(Row<MemberValue>(client, "spare-9"), Is.InstanceOf<NullMemberValue>());
            }
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(
                () => BuildClient(SaveWith(NeoSaveFormat.P127Revision, AddLatePartition, Plot(-3_000_000_000d)), AddLatePartition));
            StringAssert.Contains("Save value 'late-9' (Late) holds -3000000000, which is outside the int range.", error!.Message);
        }

        [Test]
        public void IntSlotsLoadNegativeZeroAsZero()
        {
            JObject save = SaveWith(NeoSaveFormat.P127Revision, AddCell, new StringMemberValue { id = "count", value = "-0" });
            save["values"]!["maybe"] = JObject.Parse("{\"id\":\"maybe\",\"value\":-0.0}");
            save["values"]!["cell"] = JObject.Parse("{\"id\":\"cell\",\"value\":{\"x\":\"-0\",\"y\":-0.0}}");
            using NeoClient client = BuildClient(save, AddCell);

            foreach (string id in new[] { "count", "maybe" })
            {
                double value = Row<NumberMemberValue>(client, id).value!.Value;
                Assert.That(value, Is.EqualTo(0d), id);
                Assert.That(double.IsNegative(value), Is.False, id);
            }
            NeoVector2Value cell = Row<Vector2MemberValue>(client, "cell").value!;
            Assert.That(double.IsNegative(cell.x) || double.IsNegative(cell.y), Is.False);
            Assert.That(Revision(client), Is.EqualTo(NeoSaveFormat.P127Revision));

            string written = client.SerializeSaveData();
            var values = (JObject)JObject.Parse(written)["values"]!;
            Assert.That(values["count"]!["value"]!.Type, Is.Not.EqualTo(JTokenType.String));
            Assert.That(values["cell"]!["value"]!["x"]!.Type, Is.Not.EqualTo(JTokenType.String));
            StringAssert.DoesNotContain("-0", values["count"]!.ToString(Formatting.None) + values["maybe"]!.ToString(Formatting.None) + values["cell"]!.ToString(Formatting.None));
        }

        [Test]
        public void RevisionThreeSavesRefuseOutOfRangeInts()
        {
            JObject save = SaveWith(NeoSaveFormat.P127Revision, new NumberMemberValue { id = "count", value = 3_000_000_000d });
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(save));
            StringAssert.Contains("Save value 'count' (Count) holds 3000000000, which is outside the int range.", error!.Message);
            StringAssert.Contains("The save was not loaded.", error.Message);
        }

        [Test]
        public void RevisionThreeSavesRefuseAnOutOfRangeSetEntry()
        {
            JObject save = SaveWith(
                NeoSaveFormat.P127Revision,
                AddTags,
                new ArrayMemberValue { id = "tags", value = new[] { "tag-five", "tag-big" } },
                new NumberMemberValue { id = "tag-five", value = 5 },
                new NumberMemberValue { id = "tag-big", value = 3_000_000_000d });
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(save, AddTags));
            StringAssert.Contains("Save value 'tag-big' (Tag) holds 3000000000, which is outside the int range.", error!.Message);
        }

        [Test]
        public void RevisionThreeSavesRefuseAnOutOfRangeIntVectorComponent()
        {
            JObject save = SaveWith(NeoSaveFormat.P127Revision, AddCell);
            save["values"]!["cell"] = JObject.Parse("{\"id\":\"cell\",\"value\":{\"x\":1,\"y\":3000000000}}");
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(save, AddCell));
            StringAssert.Contains("Save value 'cell' (Cell) field 'y' holds 3000000000, which is outside the int range.", error!.Message);
        }

        [TestCase(false, "3000000000", "Save value 'box' constructor argument 'seed' holds 3000000000, which is outside the int range.")]
        [TestCase(true, "{\"x\":0,\"y\":0,\"z\":-3000000000}", "Save value 'box' constructor argument 'seed' field 'z' holds -3000000000, which is outside the int range.")]
        public void RevisionThreeSavesRefuseAnOutOfRangeIntConstructorArgument(bool vector, string payload, string expected)
        {
            System.Action<ProjectData> configure = vector ? AddVectorSeedConstructor : AddBoxConstructor;
            var box = new ObjectMemberValue
            {
                id = "box",
                classId = "box-class",
                value = new Dictionary<string, string> { ["Item"] = "box-item" },
                genericBindings = TestTypeBindings.Stamp((BoxParam, TestTypeBindings.Of(MemberKind.Int))),
                instanceConstructorId = "box-constructor",
                constructorArgs = new Dictionary<string, JToken?> { ["__arg_0__"] = JToken.Parse(payload) },
            };
            JObject save = SaveWith(NeoSaveFormat.P127Revision, configure, box);
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(save, configure));
            StringAssert.Contains(expected, error!.Message);
        }

        [Test]
        public void RevisionThreeSavesLeaveOutOfRangeFloatsAlone()
        {
            JObject save = SaveWith(NeoSaveFormat.P127Revision, AddFloatPositions, new NumberMemberValue { id = "ratio", value = 3_000_000_000.5 });
            save["values"]!["aim"] = JObject.Parse("{\"id\":\"aim\",\"value\":{\"x\":0.25,\"y\":3000000000.5}}");
            using NeoClient client = BuildClient(save, AddFloatPositions);
            Assert.That(Row<NumberMemberValue>(client, "ratio").value, Is.EqualTo(3_000_000_000.5));
            Assert.That(Row<Vector2MemberValue>(client, "aim").value!.y, Is.EqualTo(3_000_000_000.5));
            Assert.That(Revision(client), Is.EqualTo(NeoSaveFormat.P127Revision));
        }

        [Test]
        public void RevisionThreeSavesRefuseNullInANonNullableSlot()
        {
            JObject save = SaveWith(NeoSaveFormat.P127Revision, new NullMemberValue { id = "box-item" });
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(save));
            StringAssert.Contains("Save value 'box-item' (Item) holds null in a non-nullable slot.", error!.Message);
        }

        [Test]
        public void RevisionThreeSavesRefuseConstructionKeysInStaticBindings()
        {
            JObject save = SaveWith(NeoSaveFormat.P127Revision);
            save["staticBindings"] = new JObject { ["box-shared-field<int>"] = "shared" };
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(save));
            StringAssert.Contains("Static binding key 'box-shared-field<int>' is not a member id; a generic static's construction binds in 'staticConstructions'.", error!.Message);
        }

        [Test]
        public void RevisionThreeSavesRefuseInvalidStaticConstructions()
        {
            TypeBinding boxOfInt = BoxOf(TestTypeBindings.Of(MemberKind.Int));
            AssertConstructionRefused(
                "Static construction 0 has no memberId.",
                ("", boxOfInt, null));
            AssertConstructionRefused(
                "Static construction 0 of member 'box-shared-field': its construction is int, but a construction names a Class or Interface.",
                ("box-shared-field", TestTypeBindings.Of(MemberKind.Int), null));
            AssertConstructionRefused(
                "Static construction 0 of member 'box-shared-field': its construction doesn't bind Box's type parameter T.",
                ("box-shared-field", TestTypeBindings.Class("box-class"), null));
            AssertConstructionRefused(
                "Static construction 0 of member 'box-shared-field': its construction at \"box-t\" is string?; a runtime type has no nullable reference.",
                ("box-shared-field", BoxOf(TestTypeBindings.Of(MemberKind.String, required: false)), null));
            AssertConstructionRefused(
                "Save holds two entries for static 'box-shared-field' of construction 'Box<int>'.",
                ("box-shared-field", boxOfInt, "shared"),
                ("box-shared-field", boxOfInt, null));
            AssertConstructionRefused(
                "Static construction 0 of member 'count-field': its construction Box<int> doesn't name the member's class",
                ("count-field", boxOfInt, null));
        }

        [Test]
        public void RevisionThreeSavesCarryStaticConstructionsAndTombstones()
        {
            using (NeoClient fresh = BuildClient())
                Assert.That(JObject.Parse(fresh.SerializeSaveData()).ContainsKey("staticConstructions"), Is.False);
            JObject save = SaveWith(NeoSaveFormat.P127Revision);
            save["staticConstructions"] = Constructions(
                ("box-shared-field", BoxOf(TestTypeBindings.Of(MemberKind.Int)), null),
                ("box-shared-field", BoxOf(TestTypeBindings.List(TestTypeBindings.Of(MemberKind.String))), null));
            using NeoClient client = BuildClient(save);
            JToken written = JObject.Parse(client.SerializeSaveData())["staticConstructions"]!;
            Assert.That(JToken.DeepEquals(written, save["staticConstructions"]), Is.True, written.ToString());
            Assert.That(written[0]!["value"]!.Type, Is.EqualTo(JTokenType.Null));
        }

        private static void AssertConstructionRefused(string expected, params (string memberId, TypeBinding construction, string? value)[] entries)
        {
            JObject save = SaveWith(NeoSaveFormat.P127Revision, new NumberMemberValue { id = "shared", value = 1d });
            save["staticConstructions"] = Constructions(entries);
            var error = Assert.Throws<NeoUnsupportedSaveFormatException>(() => BuildClient(save));
            StringAssert.Contains(expected, error!.Message);
        }

        private static TypeBinding BoxOf(TypeBinding argument) =>
            TestTypeBindings.Class("box-class", true, (BoxParam, argument));

        private static JArray Constructions(params (string memberId, TypeBinding construction, string? value)[] entries)
        {
            var constructions = new JArray();
            foreach (var entry in entries)
            {
                constructions.Add(new JObject
                {
                    ["memberId"] = entry.memberId,
                    ["construction"] = JObject.FromObject(entry.construction),
                    // A null string makes a String-typed JValue; a tombstone is a JSON null.
                    ["value"] = entry.value is null ? JValue.CreateNull() : entry.value,
                });
            }
            return constructions;
        }

        private static void AssertNegativeZero(NumberMemberValue row)
        {
            Assert.That(row.value, Is.EqualTo(0d));
            Assert.That(double.IsNegative(row.value!.Value), Is.True, row.id);
        }

        private static void AssertNegativeZeroString(JToken? token)
        {
            Assert.That(token!.Type, Is.EqualTo(JTokenType.String), token.Path);
            Assert.That(token.Value<string>(), Is.EqualTo("-0"), token.Path);
        }

        /// <summary>A Vector2 Aim and a Color Tint on the save root.</summary>
        private static void AddFloatPositions(ProjectData data)
        {
            data.members["aim-field"] = new Vector2Member
            {
                id = "aim-field",
                projectId = ProjectId,
                name = "Aim",
                kind = MemberKind.Vector2,
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.members["tint-field"] = new ColorMember
            {
                id = "tint-field",
                projectId = ProjectId,
                name = "Tint",
                kind = MemberKind.Color,
                Requirement = NeoMemberRequirementKind.Required,
            };
            NeoSchemaClass root = data.classes["save-root-class"];
            root.schema["Aim"] = "aim-field";
            root.schema["Tint"] = "tint-field";
            Dictionary<string, string> fields = ((ObjectMemberValue)data.values["save"]).value!;
            fields["Aim"] = "aim";
            fields["Tint"] = "tint";
            data.values["aim"] = new Vector2MemberValue { id = "aim", value = new NeoVector2Value() };
            data.values["tint"] = new ColorMemberValue { id = "tint", value = new NeoColorValue { a = 1f } };
        }

        private static void AddBoxConstructor(ProjectData data)
        {
            var seed = new FunctionArgumentTypeInfo
            {
                name = "seed",
                type = MemberKind.Generic,
                required = true,
                ownerClassId = "box-class",
                genericParamId = BoxParam,
            };
            data.classes["box-class"].constructorIds = new[] { "box-constructor" };
            data.constructors["box-constructor"] = new ConstructorRecord
            {
                id = "box-constructor",
                projectId = ProjectId,
                classId = "box-class",
                argumentTypes = new[] { seed },
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null, required = true },
                    parameters = new[]
                    {
                        new Variable { id = "__this__" },
                        new Variable { id = "__root__" },
                        new Variable { id = "__arg_0__", typeInfo = seed },
                    },
                    instructions = System.Array.Empty<Instruction>(),
                },
            };
        }

        /// <summary>
        /// A Hold&lt;int&gt; Held on the save root, whose Wrap&lt;List&lt;H&gt;&gt;
        /// Listed holds a ListWrap&lt;L&gt; : Wrap&lt;List&lt;L&gt;&gt; with an
        /// L Item.
        /// </summary>
        private static void AddWrappedHold(ProjectData data)
        {
            data.members["hold-listed-field"] = new ClassMember
            {
                id = "hold-listed-field",
                projectId = ProjectId,
                name = "Listed",
                kind = MemberKind.Class,
                DeclaredClassId = "wrap-class",
                classArguments = new Dictionary<string, TypeBinding>
                {
                    ["wrap-w"] = TestTypeBindings.List(TestTypeBindings.Open("hold-class", "hold-h")),
                },
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.members["held-field"] = new ClassMember
            {
                id = "held-field",
                projectId = ProjectId,
                name = "Held",
                kind = MemberKind.Class,
                DeclaredClassId = "hold-class",
                classArguments = new Dictionary<string, TypeBinding>
                {
                    ["hold-h"] = TestTypeBindings.Of(MemberKind.Int),
                },
                Requirement = NeoMemberRequirementKind.Required,
            };
            NeoSchemaClass wrap = Class("wrap-class", "Wrap", new Dictionary<string, string>());
            wrap.genericParams = new List<GenericParamDeclaration> { new() { id = "wrap-w", name = "W" } };
            data.members["list-wrap-item"] = new GenericMember
            {
                id = "list-wrap-item",
                projectId = ProjectId,
                name = "Item",
                kind = MemberKind.Generic,
                genericParamId = "list-wrap-l",
                Requirement = NeoMemberRequirementKind.Optional,
            };
            NeoSchemaClass listWrap = Class("list-wrap-class", "ListWrap", new Dictionary<string, string> { ["Item"] = "list-wrap-item" });
            listWrap.genericParams = new List<GenericParamDeclaration> { new() { id = "list-wrap-l", name = "L" } };
            listWrap.extendsClassId = "wrap-class";
            listWrap.extendsGenericBindings = new Dictionary<string, TypeBinding>
            {
                ["wrap-w"] = TestTypeBindings.List(TestTypeBindings.Open("list-wrap-class", "list-wrap-l")),
            };
            NeoSchemaClass hold = Class("hold-class", "Hold", new Dictionary<string, string> { ["Listed"] = "hold-listed-field" });
            hold.genericParams = new List<GenericParamDeclaration> { new() { id = "hold-h", name = "H" } };
            data.classes["wrap-class"] = wrap;
            data.classes["list-wrap-class"] = listWrap;
            data.classes["hold-class"] = hold;
            data.classes["save-root-class"].schema["Held"] = "held-field";
            ((ObjectMemberValue)data.values["save"]).value!["Held"] = "held";
            data.values["held"] = new ObjectMemberValue
            {
                id = "held",
                classId = "hold-class",
                value = new Dictionary<string, string> { ["Listed"] = "listed" },
            };
            data.values["listed"] = new ObjectMemberValue
            {
                id = "listed",
                classId = "list-wrap-class",
                value = new Dictionary<string, string> { ["Item"] = "listed-item" },
            };
            data.values["listed-item"] = new NumberMemberValue { id = "listed-item", value = 0 };
        }

        /// <summary>The box constructor with a Vector3Int seed in place of its generic one.</summary>
        private static void AddVectorSeedConstructor(ProjectData data)
        {
            AddBoxConstructor(data);
            ConstructorRecord constructor = data.constructors["box-constructor"];
            FunctionArgumentTypeInfo seed = constructor.argumentTypes[0];
            seed.type = MemberKind.Vector3Int;
            seed.ownerClassId = null;
            seed.genericParamId = null;
        }

        /// <summary>A Vector2Int Cell on the save root.</summary>
        private static void AddCell(ProjectData data)
        {
            data.members["cell-field"] = new Vector2IntMember
            {
                id = "cell-field",
                projectId = ProjectId,
                name = "Cell",
                kind = MemberKind.Vector2Int,
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.classes["save-root-class"].schema["Cell"] = "cell-field";
            ((ObjectMemberValue)data.values["save"]).value!["Cell"] = "cell";
            data.values["cell"] = new Vector2MemberValue { id = "cell", value = new NeoVector2Value() };
        }

        /// <summary>A Set&lt;int&gt; Tags on the save root.</summary>
        private static void AddTags(ProjectData data)
        {
            AddTagMembers(data);
            data.classes["save-root-class"].schema["Tags"] = "tags-field";
            ((ObjectMemberValue)data.values["save"]).value!["Tags"] = "tags";
            data.values["tags"] = new ArrayMemberValue { id = "tags", value = System.Array.Empty<string>() };
        }

        private static void AddTagMembers(ProjectData data)
        {
            data.members["tags-field"] = new SetMember
            {
                id = "tags-field",
                projectId = ProjectId,
                name = "Tags",
                kind = MemberKind.Set,
                entryMemberId = "tags-entry",
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.members["tags-entry"] = new IntMember
            {
                id = "tags-entry",
                projectId = ProjectId,
                name = "Tag",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
            };
        }

        private static void AddLatePartition(ProjectData data) => AddPartitions(data, orchard: false);

        private static void AddGardenAndOrchard(ProjectData data) => AddPartitions(data, orchard: true);

        /// <summary>
        /// The "garden" partition holds a plot in the save root's unordered
        /// Plots list, and the plot's Late field; "orchard" holds a second
        /// plot. A plot's Spare is optional.
        /// </summary>
        private static void AddPartitions(ProjectData data, bool orchard)
        {
            data.members["plots-field"] = new ListMember
            {
                id = "plots-field",
                projectId = ProjectId,
                name = "Plots",
                kind = MemberKind.List,
                entryMemberId = "plot-entry",
                ListKind = NeoListKind.Unordered,
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.members["plot-entry"] = new ClassMember
            {
                id = "plot-entry",
                projectId = ProjectId,
                name = "Plot",
                kind = MemberKind.Class,
                DeclaredClassId = "plot-class",
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.members["late-field"] = new IntMember
            {
                id = "late-field",
                projectId = ProjectId,
                name = "Late",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.members["spare-field"] = new IntMember
            {
                id = "spare-field",
                projectId = ProjectId,
                name = "Spare",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Optional,
            };
            data.classes["plot-class"] = Class("plot-class", "Plot", new Dictionary<string, string>
            {
                ["Late"] = "late-field",
                ["Spare"] = "spare-field",
            });
            data.classes["save-root-class"].schema["Plots"] = "plots-field";
            ((ObjectMemberValue)data.values["save"]).value!["Plots"] = "plots";
            data.values["plots"] = new ArrayMemberValue { id = "plots", value = System.Array.Empty<string>() };
            var partitions = new Dictionary<string, JToken> { ["garden"] = Plot("plot", "late") };
            if (orchard)
                partitions["orchard"] = Plot("plot-2", "late-2");
            data.valuePartitions = NeoTestExport.Partitions(partitions);
        }

        private static JObject Plot(string plotId, string lateId) => JObject.FromObject(new Dictionary<string, MemberValue>
        {
            [plotId] = new ObjectMemberValue
            {
                id = plotId,
                classId = "plot-class",
                containerId = "plots",
                value = new Dictionary<string, string> { ["Late"] = lateId },
            },
            [lateId] = new NumberMemberValue { id = lateId, value = 0 },
        });

        private static int? Revision(NeoClient client) =>
            (int?)JObject.Parse(client.SerializeSaveData())["requiredSaveFormatRevision"];

        private static T Row<T>(NeoClient client, string valueId) where T : MemberValue
        {
            Assert.That(client.TryGetValue(NeoValueOwnership.Save, valueId, out MemberValue? row), Is.True, valueId);
            return (T)row!;
        }

        private static JObject SaveWith(int? revision, params MemberValue[] rows) => SaveWith(revision, null, rows);

        private static JObject SaveWith(int? revision, System.Action<ProjectData>? configure, params MemberValue[] rows)
        {
            using NeoClient fresh = BuildClient(configure: configure);
            JObject save = JObject.Parse(fresh.SerializeSaveData());
            if (revision is null)
                save.Remove("requiredSaveFormatRevision");
            else
                save["requiredSaveFormatRevision"] = revision.Value;
            var values = (JObject?)save["values"] ?? new JObject();
            foreach (MemberValue row in rows)
                values[row.id] = JObject.Parse(JsonConvert.SerializeObject(row));
            save["values"] = values;
            return save;
        }

        private static NeoClient BuildClient(JObject? save = null, System.Action<ProjectData>? configure = null) =>
            NeoTestSaveStack.ClientFromSchema(Schema(configure), loadedSaveContent: save?.ToString(Formatting.None));

        private static ProjectData Schema(System.Action<ProjectData>? configure)
        {
            var members = new Dictionary<string, Member>
            {
                ["assets-member"] = Root("assets-member", "assets", NeoMemberStorage.Immutable),
                ["save-member"] = Root("save-member", "save", NeoMemberStorage.Save),
                ["session-member"] = Root("session-member", "session", NeoMemberStorage.Session),
                ["count-field"] = new IntMember
                {
                    id = "count-field",
                    projectId = ProjectId,
                    name = "Count",
                    kind = MemberKind.Int,
                    Requirement = NeoMemberRequirementKind.Required,
                },
                ["box-field"] = new ClassMember
                {
                    id = "box-field",
                    projectId = ProjectId,
                    name = "Box",
                    kind = MemberKind.Class,
                    DeclaredClassId = "box-class",
                    Requirement = NeoMemberRequirementKind.Required,
                },
                ["box-item-field"] = new GenericMember
                {
                    id = "box-item-field",
                    projectId = ProjectId,
                    name = "Item",
                    kind = MemberKind.Generic,
                    genericParamId = BoxParam,
                    Requirement = NeoMemberRequirementKind.Optional,
                },
                ["box-binding"] = new IntMember
                {
                    id = "box-binding",
                    projectId = ProjectId,
                    name = "T",
                    kind = MemberKind.Int,
                    Requirement = NeoMemberRequirementKind.Required,
                },
                ["box-shared-field"] = new GenericMember
                {
                    id = "box-shared-field",
                    projectId = ProjectId,
                    name = "Shared",
                    kind = MemberKind.Generic,
                    genericParamId = BoxParam,
                    Requirement = NeoMemberRequirementKind.Required,
                    Modifier = NeoMemberModifierKind.Static,
                    Storage = NeoMemberStorage.Save,
                },
                ["maybe-field"] = new IntMember
                {
                    id = "maybe-field",
                    projectId = ProjectId,
                    name = "Maybe",
                    kind = MemberKind.Int,
                    Requirement = NeoMemberRequirementKind.Optional,
                },
                ["ratio-field"] = new FloatMember
                {
                    id = "ratio-field",
                    projectId = ProjectId,
                    name = "Ratio",
                    kind = MemberKind.Float,
                    Requirement = NeoMemberRequirementKind.Required,
                },
                ["price-field"] = new DecimalMember
                {
                    id = "price-field",
                    projectId = ProjectId,
                    name = "Price",
                    kind = MemberKind.Decimal,
                    Requirement = NeoMemberRequirementKind.Required,
                },
                ["ratios-field"] = new ListMember
                {
                    id = "ratios-field",
                    projectId = ProjectId,
                    name = "Ratios",
                    kind = MemberKind.List,
                    entryMemberId = "ratios-entry",
                    Requirement = NeoMemberRequirementKind.Required,
                },
                ["ratios-entry"] = new FloatMember
                {
                    id = "ratios-entry",
                    projectId = ProjectId,
                    name = "Ratio",
                    kind = MemberKind.Float,
                    Requirement = NeoMemberRequirementKind.Required,
                },
                ["weights-field"] = new DictionaryMember
                {
                    id = "weights-field",
                    projectId = ProjectId,
                    name = "Weights",
                    kind = MemberKind.Dictionary,
                    entryMemberId = "weights-entry",
                    Requirement = NeoMemberRequirementKind.Required,
                },
                ["weights-entry"] = new FloatMember
                {
                    id = "weights-entry",
                    projectId = ProjectId,
                    name = "Weight",
                    kind = MemberKind.Float,
                    Requirement = NeoMemberRequirementKind.Required,
                },
            };
            var values = new Dictionary<string, MemberValue>
            {
                ["assets"] = new ObjectMemberValue { id = "assets", classId = "empty-class", value = new Dictionary<string, string>() },
                ["session"] = new ObjectMemberValue { id = "session", classId = "empty-class", value = new Dictionary<string, string>() },
                ["save"] = new ObjectMemberValue
                {
                    id = "save",
                    classId = "save-root-class",
                    value = new Dictionary<string, string>
                    {
                        ["Count"] = "count",
                        ["Box"] = "box",
                        ["Maybe"] = "maybe",
                        ["Ratio"] = "ratio",
                        ["Price"] = "price",
                        ["Ratios"] = "ratios",
                        ["Weights"] = "weights",
                    },
                },
                ["box"] = new ObjectMemberValue
                {
                    id = "box",
                    classId = "box-class",
                    value = new Dictionary<string, string> { ["Item"] = "box-item" },
                    genericBindings = TestTypeBindings.Stamp((BoxParam, TestTypeBindings.Of(MemberKind.Int))),
                },
                ["count"] = new NumberMemberValue { id = "count", value = 0 },
                ["box-item"] = new NumberMemberValue { id = "box-item", value = 0 },
                ["maybe"] = new NumberMemberValue { id = "maybe", value = 0 },
                ["ratio"] = new NumberMemberValue { id = "ratio", value = 1 },
                ["price"] = new StringMemberValue { id = "price", value = "1" },
                ["ratios"] = new ArrayMemberValue { id = "ratios", value = System.Array.Empty<string>() },
                ["weights"] = new ObjectMemberValue { id = "weights", value = new Dictionary<string, string>() },
            };
            var data = new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    name = "P127 save revision",
                    rootAssetsMemberId = "assets-member",
                    rootSaveFileMemberId = "save-member",
                    rootSessionMemberId = "session-member",
                },
                members = members,
                values = values,
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    ["empty-class"] = Class("empty-class", "Empty", new Dictionary<string, string>()),
                    ["save-root-class"] = Class("save-root-class", "SaveRoot", new Dictionary<string, string>
                    {
                        ["Count"] = "count-field",
                        ["Box"] = "box-field",
                        ["Maybe"] = "maybe-field",
                        ["Ratio"] = "ratio-field",
                        ["Price"] = "price-field",
                        ["Ratios"] = "ratios-field",
                        ["Weights"] = "weights-field",
                    }),
                    ["box-class"] = Class("box-class", "Box", new Dictionary<string, string>
                    {
                        ["Item"] = "box-item-field",
                        ["Shared"] = "box-shared-field",
                    }),
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            }.WithUserRoot();
            data.classes["box-class"].genericParams = new List<GenericParamDeclaration> { new() { id = BoxParam, name = "T" } };
            configure?.Invoke(data);
            return data;
        }

        private static ClassMember Root(string id, string valueId, NeoMemberStorage storage) => new()
        {
            id = id,
            projectId = ProjectId,
            name = id,
            kind = MemberKind.Class,
            DeclaredClassId = valueId == "save" ? "save-root-class" : "empty-class",
            valueId = valueId,
            Storage = storage,
        };

        private static NeoSchemaClass Class(string id, string name, Dictionary<string, string> schema) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            schema = schema,
        };
    }
}
