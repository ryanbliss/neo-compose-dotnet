// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using JsonMember = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Tests
{
    [NeoEnum(typeof(P127SeasonOptions))]
    internal enum P127Season
    {
        Spring = 1,
        Summer = 2,
    }

    internal static class P127SeasonOptions
    {
        public static P127Season FromOptionId(string optionId)
        {
            return optionId switch
            {
                "season-spring" => P127Season.Spring,
                "season-summer" => P127Season.Summer,
                _ => NeoUndeclaredEnumOptions<P127Season>.FromOptionId(optionId),
            };
        }

        public static string OptionId(this P127Season value)
        {
            return value switch
            {
                P127Season.Spring => "season-spring",
                P127Season.Summer => "season-summer",
                _ => NeoUndeclaredEnumOptions<P127Season>.OptionId(value),
            };
        }
    }

    /// <summary>
    /// P127 §1.2, §1.3 and §14: an object field has a node, and the
    /// generated-code support writes and reads every kind it holds: a box of
    /// a scalar, enum, tuple, collection, delegate, <c>Type</c>, culture or
    /// built-in comparer, a class value, <c>new object()</c> or null. A
    /// <c>Type</c>, <c>CultureInfo</c> or <c>NumberFormatInfo</c> field
    /// stores its payload directly.
    /// </summary>
    public sealed class P127ObjectMemberTests
    {
        private const string ProjectId = "p127-object";
        private const string RootClass = "root-class";
        private const string PointClass = "point-class";
        private const string CarrierClass = "carrier-class";
        private const string BoxClass = "box-class";
        private const string EqualityComparerId = "system_7e0c3a52-1f0d-4c55-9a3e-5f2b8d1c0e01";
        private const string ComparerId = "system_7e0c3a52-1f0d-4c55-9a3e-5f2b8d1c0e02";
        private const string ReferenceEqualityComparerId = "system_7e0c3a52-1f0d-4c55-9a3e-5f2b8d1c0e03";

        public static IEnumerable<TestCaseData> Boxes()
        {
            yield return new TestCaseData(7, "int").SetName("{m}(int)");
            yield return new TestCaseData(new Vector2(1.5f, -2f), "Vector2").SetName("{m}(Vector2)");
            yield return new TestCaseData((3, 0.5f), "(int,float)").SetName("{m}(tuple)");
            yield return new TestCaseData(((int?)null, 2), "(int?,int)").SetName("{m}(nullable tuple element)");
            yield return new TestCaseData(2.5m, "decimal").SetName("{m}(decimal)");
            yield return new TestCaseData(true, "bool").SetName("{m}(bool)");
            yield return new TestCaseData("plain", "string").SetName("{m}(string)");
            yield return new TestCaseData(new Color(0.25f, 0.5f, 1f, 1f), "Color").SetName("{m}(Color)");
            yield return new TestCaseData(new Vector3Int(1, 2, 3), "Vector3Int").SetName("{m}(Vector3Int)");
        }

        public static IEnumerable<TestCaseData> Collections()
        {
            yield return new TestCaseData(new List<int> { 1, 2 }, "List<int>", typeof(NeoList<int>)).SetName("{m}(List)");
            yield return new TestCaseData(new HashSet<string> { "a", "b" }, "Set<string>", typeof(NeoSet<string>)).SetName("{m}(Set)");
            yield return new TestCaseData(new Dictionary<string, int> { ["a"] = 1 }, "Dictionary<string,int>", typeof(NeoDictionary<int>)).SetName("{m}(Dictionary)");
            yield return new TestCaseData(
                new Dictionary<P127Season, int> { [P127Season.Summer] = 3 },
                "Dictionary<e:season,int>",
                typeof(NeoDictionary<P127Season, int>)).SetName("{m}(enum-keyed Dictionary)");
            yield return new TestCaseData(new HashSet<P127Season> { P127Season.Summer }, "Set<e:season>", typeof(NeoEnumSet<P127Season>)).SetName("{m}(enum Set)");
        }

        [Test]
        public void ObjectFieldsHaveANode()
        {
            using NeoClient client = BuildClient();
            Assert.That(client.save.Get<NeoMember>("Slot"), Is.InstanceOf<NeoMemberObjectWritable>());
            Assert.That(client.TryGetMember("slot-field", out JsonMember? slot), Is.True);
            Assert.That(NeoMember.Create(client, slot!, null), Is.InstanceOf<NeoMemberObject>());
            Assert.That(Read(client.save), Is.Null);
        }

        [TestCaseSource(nameof(Boxes))]
        public void BoxesWriteReadCloneAndReload(object boxed, string key)
        {
            using NeoClient client = BuildClient();
            Write(client, client.save, boxed);

            Assert.That(Read(client.save), Is.EqualTo(boxed));
            MemberValue row = SlotRow(client.save);
            Assert.That(row.valueType, Is.EqualTo(TestTypeBindings.FromKey(key)));
            Assert.That(row.classId, Is.Null);

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            Assert.That(Read(clone), Is.EqualTo(boxed));
            Assert.That(SlotRow(clone).id, Is.Not.EqualTo(row.id));
            Assert.That(SlotRow(clone).valueType, Is.EqualTo(row.valueType));

            using NeoClient reloaded = Reload(client);
            Assert.That(Read(reloaded.save), Is.EqualTo(boxed));
            Assert.That(SlotRow(reloaded.save).valueType, Is.EqualTo(row.valueType));
        }

        [Test]
        public void ClassValuesWriteReadCloneAndReload()
        {
            using NeoClient client = BuildClient();
            client.RegisterGeneratedClassFactories(ReadOnlyFactories(), WritableFactories());
            NeoGeneratedClassValue point = client.ResolveRegisteredGeneratedClassValue("point-asset")!;
            Write(client, client.save, point);

            var read = (NeoGeneratedClassValue)Read(client.save)!;
            Assert.That(read.classId, Is.EqualTo(PointClass));
            Assert.That(read.valueId, Is.Not.EqualTo("point-asset"));
            MemberValue row = SlotRow(client.save);
            Assert.That(row.valueType, Is.Null);
            Assert.That(row.classId, Is.EqualTo(PointClass));
            Assert.That(PointX(client, row), Is.EqualTo(4d));

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            MemberValue cloned = SlotRow(clone);
            Assert.That(cloned.id, Is.Not.EqualTo(row.id));
            Assert.That(((NeoGeneratedClassValue)Read(clone)!).classId, Is.EqualTo(PointClass));
            Assert.That(PointX(client, cloned), Is.EqualTo(4d));

            using NeoClient reloaded = Reload(client);
            reloaded.RegisterGeneratedClassFactories(ReadOnlyFactories(), WritableFactories());
            Assert.That(((NeoGeneratedClassValue)Read(reloaded.save)!).classId, Is.EqualTo(PointClass));
            Assert.That(PointX(reloaded, SlotRow(reloaded.save)), Is.EqualTo(4d));
        }

        [Test]
        public void RewritesReplaceTheHeldValueAndReleaseItsRows()
        {
            using NeoClient client = BuildClient();
            Write(client, client.save, (1, 2f));
            string[] elements = ((ArrayMemberValue)SlotRow(client.save)).value!;

            Write(client, client.save, 5);
            Assert.That(Read(client.save), Is.EqualTo(5));
            Assert.That(SlotRow(client.save).valueType, Is.EqualTo(TestTypeBindings.FromKey("int")));
            foreach (string element in elements)
                Assert.That(client.TryGetValue(NeoValueOwnership.Save, element, out MemberValue? _), Is.False, element);

            Write(client, client.save, new object());
            object identity = Read(client.save)!;
            Assert.That(identity.GetType(), Is.EqualTo(typeof(object)));
            Assert.That(Read(client.save), Is.SameAs(identity));
            var stored = (ObjectMemberValue)SlotRow(client.save);
            Assert.That(stored.valueType, Is.EqualTo(TestTypeBindings.FromKey("object")));
            Assert.That(stored.value, Is.Empty);

            Write(client, client.save, null);
            Assert.That(Read(client.save), Is.Null);
            using NeoClient reloaded = Reload(client);
            Assert.That(Read(reloaded.save), Is.Null);
        }

        [Test]
        public void ObjectValueRefusesWhatTheSlotCantHold()
        {
            using NeoClient client = BuildClient();
            client.RegisterGeneratedClassFactories(ReadOnlyFactories(), WritableFactories());
            NeoMemberObject slot = client.save.Get<NeoMemberObject>("Slot");
            NeoMemberObject valueSlot = client.save.Get<NeoMemberObject>("Held");

            var unsupported = Assert.Throws<NotSupportedException>(() => NeoGeneratedTypesSupport.ObjectValue(client, slot, new long[0]));
            StringAssert.Contains("Object field 'Slot' can't store a long[], which has no NeoScript type.", unsupported!.Message);
            var comparer = Assert.Throws<NotSupportedException>(() => NeoGeneratedTypesSupport.ObjectValue(client, slot, StringComparer.Ordinal));
            StringAssert.Contains("Object field 'Slot' can't store the C# comparer", comparer!.Message);

            NeoGeneratedClassValue point = client.ResolveRegisteredGeneratedClassValue("point-asset")!;
            var classRefused = Assert.Throws<System.InvalidOperationException>(() => NeoGeneratedTypesSupport.ObjectValue(client, valueSlot, point));
            StringAssert.Contains("is a ValueType slot and holds only boxes, not class \"point-class\"", classRefused!.Message);

            var identityRefused = Assert.Throws<System.InvalidOperationException>(() => NeoGeneratedTypesSupport.ObjectValue(client, valueSlot, new object()));
            StringAssert.Contains("holds 'object', but only an object slot holds a new object()", identityRefused!.Message);

            Write(client, client.save, 2, "Held");
            Assert.That(NeoGeneratedTypesSupport.ReadObjectValue(client.save.Get<NeoMemberObject>("Held")), Is.EqualTo(2));
        }

        [TestCase("Slot")]
        [TestCase("Held")]
        [TestCase("EnumSlot")]
        public void EnumsBoxThroughObjectValueTypeAndEnumFields(string key)
        {
            using NeoClient client = BuildClient();
            Write(client, client.save, P127Season.Summer, key);

            object? read = ReadAt(client.save, key);
            Assert.That(read, Is.TypeOf<P127Season>());
            Assert.That((System.Enum)read!, Is.EqualTo(P127Season.Summer));
            Assert.That((ValueType)read, Is.EqualTo(P127Season.Summer));
            Assert.That(ReadAt(client.save, key), Is.SameAs(read));
            MemberValue row = RowAt(client.save, key);
            Assert.That(row.valueType, Is.EqualTo(TestTypeBindings.FromKey("e:season")));

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            Assert.That(ReadAt(clone, key), Is.EqualTo(P127Season.Summer));
            Assert.That(RowAt(clone, key).id, Is.Not.EqualTo(row.id));

            using NeoClient reloaded = Reload(client);
            Assert.That(ReadAt(reloaded.save, key), Is.EqualTo(P127Season.Summer));
        }

        [Test]
        public void EnumFieldsRefuseAnotherValueType()
        {
            using NeoClient client = BuildClient();
            var error = Assert.Throws<InvalidOperationException>(() => Write(client, client.save, 3, "EnumSlot"));
            StringAssert.Contains("Cannot write object field 'EnumSlot'", error!.Message);
        }

        [Test]
        public void RepeatWritesOfOneBoxKeepItsIdentityUntilAReload()
        {
            using NeoClient client = BuildClient();
            object box = 5;
            Write(client, client.save, box);
            Write(client, client.save, box, "Other");
            Assert.That(Read(client.save), Is.SameAs(box));
            Assert.That(ReadAt(client.save, "Other"), Is.SameAs(box));

            Write(client, client.save, 5, "Other");
            Assert.That(ReadAt(client.save, "Other"), Is.Not.SameAs(box));

            using NeoClient reloaded = Reload(client);
            object? slot = Read(reloaded.save);
            Assert.That(slot, Is.EqualTo(5));
            Assert.That(slot, Is.Not.SameAs(box));
            Assert.That(Read(reloaded.save), Is.SameAs(slot));
        }

        [TestCaseSource(nameof(Collections))]
        public void CollectionsBoxAsViewsOverTheirRows(IEnumerable written, string key, Type view)
        {
            using NeoClient client = BuildClient();
            Write(client, client.save, written);

            object? read = Read(client.save);
            Assert.That(read, Is.TypeOf(view));
            Assert.That((IEnumerable)read!, Is.EquivalentTo(written));
            Assert.That(Read(client.save), Is.SameAs(read));
            MemberValue row = SlotRow(client.save);
            Assert.That(row.valueType, Is.EqualTo(TestTypeBindings.FromKey(key)));

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            Assert.That((IEnumerable)Read(clone)!, Is.EquivalentTo(written));
            Assert.That(SlotRow(clone).id, Is.Not.EqualTo(row.id));

            using NeoClient reloaded = Reload(client);
            Assert.That(Read(reloaded.save), Is.TypeOf(view));
            Assert.That((IEnumerable)Read(reloaded.save)!, Is.EquivalentTo(written));
        }

        [Test]
        public void BoxedCollectionWritesPersist()
        {
            using NeoClient client = BuildClient();
            Write(client, client.save, new List<int> { 1 });
            var list = (NeoList<int>)Read(client.save)!;
            list.Add(2);
            Assert.That(Read(client.save), Is.EqualTo(new[] { 1, 2 }));

            string rowId = SlotRow(client.save).id;
            Write(client, client.save, list);
            Assert.That(SlotRow(client.save).id, Is.EqualTo(rowId));

            using NeoClient reloaded = Reload(client);
            Assert.That(Read(reloaded.save), Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void AnOwnedCollectionViewDoesntMoveToAnotherSlot()
        {
            using NeoClient client = BuildClient();
            Write(client, client.save, new List<int> { 1 });
            object list = Read(client.save)!;

            var error = Assert.Throws<InvalidOperationException>(() => Write(client, client.save, list, "Other"));
            StringAssert.Contains("Cannot write object field 'Other': the NeoList<int> already belongs to another field", error!.Message);
            Write(client, client.save, new List<int>((IEnumerable<int>)list), "Other");
            Assert.That(ReadAt(client.save, "Other"), Is.EqualTo(new[] { 1 }));
        }

        [Test]
        public void DelegatesBoxAndCallTheirTarget()
        {
            using NeoClient client = BuildClient();
            NeoDelegate<int> pick = client.save.Get<NeoMemberDelegate>("Pick").Bind<int>(Convert.ToInt32);
            Write(client, client.save, pick);

            var read = (NeoDelegate<int>)Read(client.save)!;
            Assert.That(read(), Is.EqualTo(7));
            Assert.That(Read(client.save), Is.SameAs(read));
            MemberValue row = SlotRow(client.save);
            Assert.That(row, Is.InstanceOf<DelegateMemberValue>());
            Assert.That(row.valueType, Is.EqualTo(TestTypeBindings.FromKey("NeoDelegate<int>")));

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            Assert.That(((NeoDelegate<int>)Read(clone)!)(), Is.EqualTo(7));

            using NeoClient reloaded = Reload(client);
            Assert.That(((NeoDelegate<int>)Read(reloaded.save)!)(), Is.EqualTo(7));

            var unbound = Assert.Throws<ArgumentException>(() => Write(client, client.save, (NeoDelegate<int>)(() => 1)));
            StringAssert.Contains("This delegate was not loaded from a NeoDelegate member", unbound!.Message);
        }

        public static IEnumerable<TestCaseData> Types()
        {
            yield return new TestCaseData(typeof(int)).SetName("{m}(int)");
            yield return new TestCaseData(typeof(int?)).SetName("{m}(int?)");
            yield return new TestCaseData(typeof(List<int>)).SetName("{m}(List<int>)");
            yield return new TestCaseData(typeof(P127Season)).SetName("{m}(enum)");
            yield return new TestCaseData(typeof((int, string))).SetName("{m}(tuple)");
            yield return new TestCaseData(typeof(Dictionary<,>)).SetName("{m}(definition)");
            yield return new TestCaseData(typeof(object)).SetName("{m}(object)");
        }

        [TestCaseSource(nameof(Types))]
        public void TypesStoreTheirBindingAndMapBack(Type type)
        {
            using NeoClient client = BuildClient();
            Write(client, client.save, type, "TypeSlot");
            Assert.That(ReadAt(client.save, "TypeSlot"), Is.SameAs(type));
            MemberValue row = RowAt(client.save, "TypeSlot");
            Assert.That(row, Is.InstanceOf<PayloadMemberValue>());
            Assert.That(row.valueType, Is.Null);

            Write(client, client.save, type);
            Assert.That(Read(client.save), Is.SameAs(type));
            Assert.That(SlotRow(client.save).valueType, Is.EqualTo(TestTypeBindings.FromKey("Type")));

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            Assert.That(ReadAt(clone, "TypeSlot"), Is.SameAs(type));
            Assert.That(Read(clone), Is.SameAs(type));

            using NeoClient reloaded = Reload(client);
            Assert.That(ReadAt(reloaded.save, "TypeSlot"), Is.SameAs(type));
            Assert.That(Read(reloaded.save), Is.SameAs(type));
        }

        [Test]
        public void TypesWithoutANeoScriptTypeAreRefused()
        {
            using NeoClient client = BuildClient();
            var error = Assert.Throws<NotSupportedException>(() => Write(client, client.save, typeof(long), "TypeSlot"));
            StringAssert.Contains("Object field 'TypeSlot' can't store typeof(long), which names no NeoScript type.", error!.Message);
        }

        [Test]
        public void CulturesAndNumberFormatsStoreTheirPayload()
        {
            using NeoClient client = BuildClient();
            Write(client, client.save, CultureInfo.InvariantCulture, "CultureSlot");
            Write(client, client.save, NumberFormatInfo.InvariantInfo, "FormatSlot");
            Assert.That(ReadAt(client.save, "CultureSlot"), Is.SameAs(CultureInfo.InvariantCulture));
            Assert.That(ReadAt(client.save, "FormatSlot"), Is.SameAs(NumberFormatInfo.InvariantInfo));
            Assert.That(((PayloadMemberValue)RowAt(client.save, "CultureSlot")).value!["invariant"]!.Value<bool>(), Is.True);

            var culture = new CultureInfo("");
            culture.NumberFormat.NegativeSign = "~";
            var format = (NumberFormatInfo)NumberFormatInfo.InvariantInfo.Clone();
            format.NaNSymbol = "nope";
            Write(client, client.save, culture, "CultureSlot");
            Write(client, client.save, NumberFormatInfo.ReadOnly(format), "FormatSlot");
            Write(client, client.save, culture);
            Assert.That(ReadAt(client.save, "CultureSlot"), Is.SameAs(culture));
            Assert.That(Read(client.save), Is.SameAs(culture));
            Assert.That(SlotRow(client.save).valueType, Is.EqualTo(TestTypeBindings.FromKey("CultureInfo")));

            using NeoClient reloaded = Reload(client);
            var readCulture = (CultureInfo)ReadAt(reloaded.save, "CultureSlot")!;
            Assert.That(readCulture.Name, Is.Empty);
            Assert.That(readCulture.IsReadOnly, Is.False);
            Assert.That(readCulture.NumberFormat.NegativeSign, Is.EqualTo("~"));
            Assert.That(ReadAt(reloaded.save, "CultureSlot"), Is.SameAs(readCulture));
            var readFormat = (NumberFormatInfo)ReadAt(reloaded.save, "FormatSlot")!;
            Assert.That(readFormat.IsReadOnly, Is.True);
            Assert.That(readFormat.NaNSymbol, Is.EqualTo("nope"));
            Assert.That(((CultureInfo)Read(reloaded.save)!).NumberFormat.NegativeSign, Is.EqualTo("~"));

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(reloaded, new ValueRef(reloaded.save.value!.id));
            Assert.That(((NumberFormatInfo)ReadAt(clone, "FormatSlot")!).NaNSymbol, Is.EqualTo("nope"));
        }

        // §12.1: writes to a stored culture or number format persist through
        // the slot, from what C# wrote or read, in a declared slot or a box.
        [Test]
        public void StoredCultureAndNumberFormatWritesPersist()
        {
            using NeoClient client = BuildClient();
            var written = new CultureInfo("");
            Write(client, client.save, written, "CultureSlot");
            Write(client, client.save, (NumberFormatInfo)NumberFormatInfo.InvariantInfo.Clone(), "FormatSlot");
            Write(client, client.save, new CultureInfo(""));
            written.NumberFormat.NegativeSign = "~";
            ((NumberFormatInfo)ReadAt(client.save, "FormatSlot")!).NaNSymbol = "nope";
            ((CultureInfo)Read(client.save)!).NumberFormat.PercentSymbol = "pct";

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            Assert.That(((CultureInfo)ReadAt(clone, "CultureSlot")!).NumberFormat.NegativeSign, Is.EqualTo("~"));
            Assert.That(ReadAt(clone, "CultureSlot"), Is.Not.SameAs(written));
            Assert.That(((NumberFormatInfo)ReadAt(clone, "FormatSlot")!).NaNSymbol, Is.EqualTo("nope"));
            Assert.That(((CultureInfo)Read(clone)!).NumberFormat.PercentSymbol, Is.EqualTo("pct"));

            using NeoClient reloaded = Reload(client);
            var culture = (CultureInfo)ReadAt(reloaded.save, "CultureSlot")!;
            Assert.That(culture.NumberFormat.NegativeSign, Is.EqualTo("~"));
            Assert.That(((NumberFormatInfo)ReadAt(reloaded.save, "FormatSlot")!).NaNSymbol, Is.EqualTo("nope"));
            Assert.That(((CultureInfo)Read(reloaded.save)!).NumberFormat.PercentSymbol, Is.EqualTo("pct"));

            // A value read after a reload persists its writes too.
            culture.NumberFormat.NegativeSign = "neg";
            using NeoClient again = Reload(reloaded);
            Assert.That(((CultureInfo)ReadAt(again.save, "CultureSlot")!).NumberFormat.NegativeSign, Is.EqualTo("neg"));
        }

        // §12.1: a C# write through an inherited culture is a write to its
        // slot. The read copies the Asset default into Save, as a vector
        // field write through an inherited value does, so a commit carries
        // the change and the authored row keeps its own.
        [Test]
        public async System.Threading.Tasks.Task InheritedCultureWritesCopyIntoSaveAndCommit()
        {
            string corpus;
            using (NeoClient source = BuildClient(InheritCulture))
                corpus = JsonConvert.SerializeObject(source.ProjectDataForRuntime);
            NeoTestSaveStack stack = NeoTestSaveStack.Create(corpus);
            using NeoClient client = Prepared(stack.Load());
            var culture = (CultureInfo)ReadAt(client.save, "CultureSlot")!;
            Assert.That(client.HasWritableValue(NeoValueOwnership.Save, InheritedCultureId), Is.True);
            culture.NumberFormat.NegativeSign = "~";
            Assert.That(ReadAt(client.save, "CultureSlot"), Is.SameAs(culture));
            var authored = (PayloadMemberValue)client.ProjectDataForRuntime.values[InheritedCultureId];
            Assert.That(authored.value!["numberFormat"]!["negativeSign"]!.Value<string>(), Is.EqualTo("-"));

            await client.CommitAsync();
            using NeoClient reloaded = Prepared(NeoTestSaveStack.LoadSynchronously(stack.Reopen()));
            Assert.That(((CultureInfo)ReadAt(reloaded.save, "CultureSlot")!).NumberFormat.NegativeSign, Is.EqualTo("~"));
        }

        // §12.1: a culture read from a member default with no row is bound
        // to a new row, as a vector write through the default binds one, so
        // a C# write through it commits.
        [Test]
        public async System.Threading.Tasks.Task DefaultCultureWritesBindARowAndCommit()
        {
            string corpus;
            using (NeoClient source = BuildClient(data => ((ObjectMember)data.members["culture-field"]).defaultValue = new NullMemberValueBase
            {
                value = NeoStorablePayloads.Encode(new CultureInfo("")),
            }))
                corpus = JsonConvert.SerializeObject(source.ProjectDataForRuntime);
            NeoTestSaveStack stack = NeoTestSaveStack.Create(corpus);
            using NeoClient client = Prepared(stack.Load());
            var culture = (CultureInfo)ReadAt(client.save, "CultureSlot")!;
            Assert.That(culture.IsReadOnly, Is.False);
            Assert.That(ReadAt(client.save, "CultureSlot"), Is.SameAs(culture));
            culture.NumberFormat.NegativeSign = "~";

            await client.CommitAsync();
            using NeoClient reloaded = Prepared(NeoTestSaveStack.LoadSynchronously(stack.Reopen()));
            Assert.That(((CultureInfo)ReadAt(reloaded.save, "CultureSlot")!).NumberFormat.NegativeSign, Is.EqualTo("~"));
        }

        // §12.1: C#'s writes to a culture it already holds mark its row dirty
        // and notify, as a vector field write through an alias does. A read
        // that copies an inherited culture changes no value, so it doesn't.
        [Test]
        public async System.Threading.Tasks.Task HeldCultureWritesMarkTheirRowAndNotifyOnlyOnChange()
        {
            string corpus;
            using (NeoClient source = BuildClient(InheritCulture))
                corpus = JsonConvert.SerializeObject(source.ProjectDataForRuntime);
            NeoTestSaveStack stack = NeoTestSaveStack.Create(corpus);
            using NeoClient client = Prepared(stack.Load());
            var changed = new List<string>();
            client.OnSaveValueChanged += changed.Add;
            int notified = 0;
            NeoMemberObject node = client.save.Get<NeoMemberObject>("CultureSlot");
            node.OnChanged += _ => notified++;

            var culture = (CultureInfo)ReadAt(client.save, "CultureSlot")!;
            Assert.That(notified, Is.Zero);
            Assert.That(changed, Is.Empty);
            await client.CommitAsync();
            changed.Clear();

            culture.NumberFormat.NegativeSign = "~";
            await client.CommitAsync();
            Assert.That(changed, Is.EqualTo(new[] { InheritedCultureId }));
            Assert.That(notified, Is.EqualTo(1));
            Assert.That(ReadAt(client.save, "CultureSlot"), Is.SameAs(culture));

            // A read after a write publishes it too, once.
            culture.NumberFormat.PercentSymbol = "pct";
            ReadAt(client.save, "CultureSlot");
            ReadAt(client.save, "CultureSlot");
            Assert.That(notified, Is.EqualTo(2));
            await client.CommitAsync();
            using NeoClient reloaded = Prepared(NeoTestSaveStack.LoadSynchronously(stack.Reopen()));
            var read = (CultureInfo)ReadAt(reloaded.save, "CultureSlot")!;
            Assert.That(read.NumberFormat.NegativeSign, Is.EqualTo("~"));
            Assert.That(read.NumberFormat.PercentSymbol, Is.EqualTo("pct"));
        }

        // §12.1: a node that can't write its slot reads a writable culture as
        // a read-only copy, so a C# write throws, as a vector write through a
        // read-only owner does, and Asset data keeps its own.
        [Test]
        public void AssetCulturesReadAsReadOnlyCopies()
        {
            using NeoClient client = BuildClient(data =>
            {
                InheritCulture(data);
                ((ObjectMemberValue)data.values["root-assets-value"]).value!["CultureSlot"] = InheritedCultureId;
            });
            var culture = (CultureInfo)ReadAt(client.assets, "CultureSlot")!;
            Assert.That(culture.IsReadOnly, Is.True);
            Assert.Throws<InvalidOperationException>(() => culture.NumberFormat.NegativeSign = "~");
            var authored = (PayloadMemberValue)client.ProjectDataForRuntime.values[InheritedCultureId];
            Assert.That(authored.value!["readOnly"]!.Value<bool>(), Is.False);
            Assert.That(authored.live, Is.Null);

            // The Save slot over the same row still reads it writable.
            var owned = (CultureInfo)ReadAt(client.save, "CultureSlot")!;
            Assert.That(owned.IsReadOnly, Is.False);
            owned.NumberFormat.NegativeSign = "~";
            Assert.That(((CultureInfo)ReadAt(client.assets, "CultureSlot")!).NumberFormat.NegativeSign, Is.EqualTo("-"));
        }

        private const string InheritedCultureId = "culture-inherited";

        private static void InheritCulture(ProjectData data)
        {
            ((ObjectMemberValue)data.values["root-save-value"]).value!["CultureSlot"] = InheritedCultureId;
            data.values[InheritedCultureId] = new PayloadMemberValue
            {
                id = InheritedCultureId,
                value = NeoStorablePayloads.Encode(new CultureInfo("")),
            };
        }

        [Test]
        public void NamedCulturesAreRefused()
        {
            using NeoClient client = BuildClient();
            var error = Assert.Throws<NotSupportedException>(() => Write(client, client.save, CultureInfo.GetCultureInfo("fr-FR"), "CultureSlot"));
            StringAssert.Contains("Object field 'CultureSlot' can't store culture 'fr-FR'; NeoScript has only the invariant culture.", error!.Message);
            var boxed = Assert.Throws<NotSupportedException>(() => Write(client, client.save, CultureInfo.GetCultureInfo("fr-FR")));
            StringAssert.Contains("can't store culture 'fr-FR'", boxed!.Message);
            var wrongKind = Assert.Throws<InvalidOperationException>(() => Write(client, client.save, 4, "TypeSlot"));
            StringAssert.Contains("Cannot write object field 'TypeSlot': a Type slot can't hold a int.", wrongKind!.Message);
        }

        public static IEnumerable<TestCaseData> Comparers()
        {
            yield return new TestCaseData(EqualityComparer<int>.Default, "c:" + EqualityComparerId + "<int>").SetName("{m}(EqualityComparer.Default)");
            yield return new TestCaseData(Comparer<string>.Default, "c:" + ComparerId + "<string>").SetName("{m}(Comparer.Default)");
            yield return new TestCaseData(NeoReferenceEqualityComparer.Instance, "c:" + ReferenceEqualityComparerId).SetName("{m}(ReferenceEqualityComparer)");
        }

        [TestCaseSource(nameof(Comparers))]
        public void BuiltInComparersBoxAsTheirSystemClass(object comparer, string key)
        {
            using NeoClient client = BuildClient();
            Write(client, client.save, comparer);
            Assert.That(Read(client.save), Is.SameAs(comparer));
            MemberValue row = SlotRow(client.save);
            Assert.That(row, Is.InstanceOf<PayloadMemberValue>());
            Assert.That(row.valueType, Is.EqualTo(TestTypeBindings.FromKey(key, client.ProjectDataForRuntime)));

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            Assert.That(Read(clone), Is.SameAs(comparer));

            using NeoClient reloaded = Reload(client);
            Assert.That(Read(reloaded.save), Is.SameAs(comparer));
        }

        [Test]
        public void SetComparersBoxBoundToTheirSet()
        {
            using NeoClient client = BuildClient();
            Write(client, client.save, new NeoSetEqualityComparer<int>());
            Assert.That(SlotRow(client.save).valueType, Is.EqualTo(TestTypeBindings.FromKey("c:" + EqualityComparerId + "<Set<int>>", client.ProjectDataForRuntime)));

            using NeoClient reloaded = Reload(client);
            Assert.That(Read(reloaded.save), Is.EqualTo(new NeoSetEqualityComparer<int>()));
        }

        [Test]
        public void CreatedComparersCallTheirDelegatesAndWriteBackTheirPayload()
        {
            using NeoClient client = BuildClient();
            var payload = new JObject
            {
                ["comparer"] = (int)NeoBuiltInComparerKind.Create,
                ["family"] = (int)NeoComparerFamilyKind.Equality,
                ["type"] = TestTypeBindings.JsonFromKey("int"),
                ["delegates"] = new JArray(JObject.FromObject(new NeoDelegateValue { memberId = "same-native", valueId = null })),
            };
            object created = NeoScriptComparer.Create(typeof(int), client, NeoValueOwnership.Save, payload, NeoComparerFamilyKind.Equality);
            Write(client, client.save, created);
            Assert.That(Read(client.save), Is.SameAs(created));

            using NeoClient reloaded = Reload(client);
            var read = (IEqualityComparer<int>)Read(reloaded.save)!;
            Assert.That(read.Equals(1, 2), Is.True);
            Assert.Throws<NotSupportedException>(() => read.GetHashCode(1));
            Assert.That(read, Is.EqualTo(created));
            Write(reloaded, reloaded.save, read, "Other");
            Assert.That(((PayloadMemberValue)RowAt(reloaded.save, "Other")).value, Is.EqualTo(payload));
        }

        [Test]
        public void FactoriesTakeObjectArguments()
        {
            using NeoClient client = BuildClient();
            NeoMemberClassWritable carrier = NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                CarrierClass,
                new NeoGeneratedConstructorValue("Need", "need-field", new List<int> { 4 }),
                new NeoGeneratedConstructorValue("Maybe", "maybe-field", P127Season.Spring));
            Assert.That(ReadAt(carrier, "Need"), Is.EqualTo(new[] { 4 }));
            Assert.That(ReadAt(carrier, "Maybe"), Is.EqualTo(P127Season.Spring));
            Assert.That(RowAt(carrier, "Maybe").valueType, Is.EqualTo(TestTypeBindings.FromKey("e:season")));

            NeoMemberClassWritable bare = NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                CarrierClass,
                new NeoGeneratedConstructorValue("Need", "need-field", 3),
                new NeoGeneratedConstructorValue("Maybe", "maybe-field", null));
            Assert.That(ReadAt(bare, "Need"), Is.EqualTo(3));
            Assert.That(ReadAt(bare, "Maybe"), Is.Null);

            NeoGeneratedClassValue point = client.ResolveRegisteredGeneratedClassValue("point-asset")!;
            NeoMemberClassWritable holding = NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                CarrierClass,
                new NeoGeneratedConstructorValue("Need", "need-field", point));
            Assert.That(((NeoGeneratedClassValue)ReadAt(holding, "Need")!).classId, Is.EqualTo(PointClass));

            var missing = Assert.Throws<ArgumentNullException>(() => NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                CarrierClass,
                new NeoGeneratedConstructorValue("Need", "need-field", null)));
            Assert.That(missing!.ParamName, Is.EqualTo("value"));
            Assert.That(missing.Message, Does.StartWith("'Carrier.Need' is required and cannot store null."));
        }

        [Test]
        public void ListEntriesOfObjectStoreBoxes()
        {
            using NeoClient client = BuildClient();
            NeoGeneratedClassValue point = client.ResolveRegisteredGeneratedClassValue("point-asset")!;
            NeoList<object> bag = Entries<NeoList<object>>(client, client.save, "Bag");
            bag.Add(7);
            bag.Add("plain");
            bag.Add((1, 2.5f));
            bag.Add(P127Season.Summer);
            bag.Add(null!);
            bag.Add(point);
            bag.Add(new List<int> { 1, 2 });

            void AssertBag(NeoMemberClass root)
            {
                NeoList<object> read = Entries<NeoList<object>>(client, root, "Bag");
                Assert.That(read.Count, Is.EqualTo(7));
                Assert.That(read[0], Is.EqualTo(7));
                Assert.That(read[1], Is.EqualTo("plain"));
                Assert.That(read[2], Is.EqualTo((1, 2.5f)));
                Assert.That(read[3], Is.EqualTo(P127Season.Summer));
                Assert.That(read[4], Is.Null);
                Assert.That(((NeoGeneratedClassValue)read[5]).classId, Is.EqualTo(PointClass));
                Assert.That(read[6], Is.TypeOf<NeoList<int>>());
                Assert.That(read[6], Is.EqualTo(new[] { 1, 2 }));
            }

            AssertBag(client.save);
            Assert.That(Entries<NeoList<object>>(client, client.save, "Bag")[0], Is.SameAs(bag[0]));
            AssertBag(NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id)));
            using NeoClient reloaded = Reload(client);
            NeoList<object> reloadedBag = Entries<NeoList<object>>(reloaded, reloaded.save, "Bag");
            Assert.That(reloadedBag.Count, Is.EqualTo(7));
            Assert.That(reloadedBag[2], Is.EqualTo((1, 2.5f)));
            Assert.That(((NeoGeneratedClassValue)reloadedBag[5]).classId, Is.EqualTo(PointClass));
            Assert.That(reloadedBag[6], Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void SetEntriesOfObjectKeyByTheirBox()
        {
            using NeoClient client = BuildClient();
            NeoSet<object> pool = Entries<NeoSet<object>>(client, client.save, "Pool");
            Assert.That(pool.Add(7), Is.True);
            Assert.That(pool.Add("plain"), Is.True);
            Assert.That(pool.Add(7), Is.False);
            Assert.That(pool.Add(7m), Is.True);
            Assert.That(pool.Add(P127Season.Summer), Is.True);
            Assert.That(pool.Add(null!), Is.True);
            Assert.That(pool.Add(null!), Is.False);
            Assert.That(pool.Count, Is.EqualTo(5));
            Assert.That(pool.Contains(7), Is.True);
            Assert.That(pool.Contains(8), Is.False);

            using NeoClient reloaded = Reload(client);
            NeoSet<object> read = Entries<NeoSet<object>>(reloaded, reloaded.save, "Pool");
            Assert.That(read.Count, Is.EqualTo(5));
            Assert.That(read, Is.EquivalentTo(new object?[] { 7, "plain", 7m, P127Season.Summer, null }));
            Assert.That(read.Contains(P127Season.Summer), Is.True);
            Assert.That(read.Add("plain"), Is.False);
        }

        [Test]
        public void DictionaryEntriesOfObjectStoreBoxes()
        {
            using NeoClient client = BuildClient();
            NeoGeneratedClassValue point = client.ResolveRegisteredGeneratedClassValue("point-asset")!;
            NeoDictionary<object> map = Entries<NeoDictionary<object>>(client, client.save, "Map");
            map.Add("count", 7);
            map.Add("point", point);
            map.Add("list", new List<int> { 1, 2 });
            map.Add("none", null!);

            void AssertMap(NeoClient owner, NeoMemberClass root)
            {
                NeoDictionary<object> read = Entries<NeoDictionary<object>>(owner, root, "Map");
                Assert.That(read.Count, Is.EqualTo(4));
                Assert.That(read["count"], Is.EqualTo(7));
                Assert.That(((NeoGeneratedClassValue)read["point"]).classId, Is.EqualTo(PointClass));
                Assert.That(read["list"], Is.EqualTo(new[] { 1, 2 }));
                Assert.That(read["none"], Is.Null);
            }

            AssertMap(client, client.save);
            AssertMap(client, NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id)));
            using NeoClient reloaded = Reload(client);
            AssertMap(reloaded, reloaded.save);
        }

        [Test]
        public void TupleElementsOfObjectStoreBoxes()
        {
            using NeoClient client = BuildClient();
            NeoMember pair = client.save.Get<NeoMember>("Pair");
            NeoGenericBinding<(int, object?)> codec = NeoGenericBindings.Resolve<(int, object?)>(client, pair);
            codec.Write(pair, (3, P127Season.Summer));
            Assert.That(codec.Read(client.save.Get<NeoMember>("Pair")), Is.EqualTo((3, (object)P127Season.Summer)));
            codec.Write(client.save.Get<NeoMember>("Pair"), (4, "plain"));
            Assert.That(codec.Read(client.save.Get<NeoMember>("Pair")), Is.EqualTo((4, (object)"plain")));
            codec.Write(client.save.Get<NeoMember>("Pair"), (5, null));
            Assert.That(codec.Read(client.save.Get<NeoMember>("Pair")), Is.EqualTo((5, (object?)null)));
            codec.Write(client.save.Get<NeoMember>("Pair"), (6, new Vector2(1f, 2f)));

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            Assert.That(codec.Read(clone.Get<NeoMember>("Pair")), Is.EqualTo((6, (object)new Vector2(1f, 2f))));
            using NeoClient reloaded = Reload(client);
            Assert.That(
                NeoGenericBindings.Resolve<(int, object?)>(reloaded, reloaded.save.Get<NeoMember>("Pair")).Read(reloaded.save.Get<NeoMember>("Pair")),
                Is.EqualTo((6, (object)new Vector2(1f, 2f))));
        }

        [Test]
        public void ABoxOfAnObjectCollectionReadsAsItsView()
        {
            using NeoClient client = BuildClient();
            Write(client, client.save, new List<object?> { 7, "plain", null });
            Assert.That(SlotRow(client.save).valueType, Is.EqualTo(TestTypeBindings.FromKey("List<object>")));
            var read = (NeoList<object>)Read(client.save)!;
            Assert.That(read, Is.EqualTo(new object?[] { 7, "plain", null }));
            read.Add(P127Season.Spring);

            using NeoClient reloaded = Reload(client);
            Assert.That(Read(reloaded.save), Is.EqualTo(new object?[] { 7, "plain", null, P127Season.Spring }));
        }

        [Test]
        public void ObjectEntriesRefuseWhatTheyCantHold()
        {
            using NeoClient client = BuildClient();
            NeoList<object> bag = Entries<NeoList<object>>(client, client.save, "Bag");
            var error = Assert.Throws<NotSupportedException>(() => bag.Add(new System.Text.StringBuilder()));
            StringAssert.Contains("Object field 'Bag' can't store a StringBuilder", error!.Message);
            var mismatch = Assert.Throws<InvalidOperationException>(() => NeoGenericBindings.Resolve<string>(client, client.save.Get<NeoMember>("Slot")));
            StringAssert.Contains("projects 'System.Object'", mismatch!.Message);
        }

        [Test]
        public void FileBoxesWriteReadCloneAndReload()
        {
            var database = ScriptableObject.CreateInstance<NeoAssetDatabase>();
            var texture = new Texture2D(1, 1);
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            AudioClip clip = AudioClip.Create("chime", 1, 1, 44100, false);
            try
            {
                database.SetFile("sprite-file", "sheet.png", "Assets/sheet.png", "", "", "", null, "", "", sprites: new[] { sprite });
                database.SetFile("audio-file", "chime.wav", "Assets/chime.wav", "", "", "", null, "", "", audioClip: clip);
                using NeoClient client = BuildClient();
                client.assetDatabase = database;
                Write(client, client.save, sprite);
                Write(client, client.save, clip, "Other");

                Assert.That(Read(client.save), Is.SameAs(sprite));
                Assert.That(ReadAt(client.save, "Other"), Is.SameAs(clip));
                Assert.That(SlotRow(client.save).valueType, Is.EqualTo(TestTypeBindings.FromKey("SpriteInfo")));
                Assert.That(RowAt(client.save, "Other").valueType, Is.EqualTo(TestTypeBindings.FromKey("AudioClipInfo")));

                NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
                Assert.That(Read(clone), Is.SameAs(sprite));
                Assert.That(ReadAt(clone, "Other"), Is.SameAs(clip));

                using NeoClient reloaded = Reload(client);
                reloaded.assetDatabase = database;
                Assert.That(Read(reloaded.save), Is.SameAs(sprite));
                Assert.That(ReadAt(reloaded.save, "Other"), Is.SameAs(clip));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(sprite);
                UnityEngine.Object.DestroyImmediate(clip);
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEngine.Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void DialogueReferenceBoxesWriteReadCloneAndReload()
        {
            using NeoClient client = BuildClient();
            Write(client, client.save, new NeoDialogueReference("dialogue-a"));
            Assert.That(((NeoDialogueReference)Read(client.save)!).Id, Is.EqualTo("dialogue-a"));
            Assert.That(SlotRow(client.save).valueType, Is.EqualTo(TestTypeBindings.FromKey("NeoDialogueReference")));

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            Assert.That(((NeoDialogueReference)Read(clone)!).Id, Is.EqualTo("dialogue-a"));
            using NeoClient reloaded = Reload(client);
            Assert.That(((NeoDialogueReference)Read(reloaded.save)!).Id, Is.EqualTo("dialogue-a"));
        }

        [Test]
        public void ReferenceBoxesNameTheirTargetAcrossCloneAndReload()
        {
            using NeoClient client = BuildClient();
            var reference = new NeoReference<int>(client, "point-x", null, null);
            Write(client, client.save, reference);
            var read = (NeoReference<int>)Read(client.save)!;
            Assert.That(read, Is.EqualTo(reference));
            Assert.That(read.Value, Is.EqualTo(4));
            Assert.That(SlotRow(client.save).valueType, Is.EqualTo(TestTypeBindings.FromKey("Reference<int>")));

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            Assert.That(Read(clone), Is.EqualTo(reference));
            using NeoClient reloaded = Reload(client);
            Assert.That(((NeoReference<int>)Read(reloaded.save)!).TargetId, Is.EqualTo("point-x"));
            Assert.That(((NeoReference<int>)Read(reloaded.save)!).Value, Is.EqualTo(4));
        }

        [Test]
        public void NestedListBoxesReadAsNestedViews()
        {
            using NeoClient client = BuildClient();
            Write(client, client.save, new List<List<int>> { new() { 1, 2 }, new() { 3 } });
            var read = (NeoList<NeoList<int>>)Read(client.save)!;
            Assert.That(read, Is.EqualTo(new[] { new[] { 1, 2 }, new[] { 3 } }));
            Assert.That(SlotRow(client.save).valueType, Is.EqualTo(TestTypeBindings.FromKey("List<List<int>>")));
            // P127 §12.1: each inner list reads its type from the box row.
            string boxId = SlotRow(client.save).id;
            foreach (string inner in ((ArrayMemberValue)SlotRow(client.save)).value!)
            {
                Assert.That(client.TryGetValue(NeoValueOwnership.Save, inner, out MemberValue? innerRow), Is.True);
                Assert.That(innerRow!.typeSource, Is.EqualTo(NeoTypeSource.AtPath(boxId, new[] { "0" })));
                Assert.That(innerRow.genericBindings, Is.Null);
            }
            read[0].Add(9);

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            Assert.That(Read(clone), Is.EqualTo(new[] { new[] { 1, 2, 9 }, new[] { 3 } }));
            using NeoClient reloaded = Reload(client);
            Assert.That(Read(reloaded.save), Is.TypeOf<NeoList<NeoList<int>>>());
            Assert.That(Read(reloaded.save), Is.EqualTo(new[] { new[] { 1, 2, 9 }, new[] { 3 } }));
            string firstInner = ((ArrayMemberValue)SlotRow(reloaded.save)).value![0];
            Assert.That(reloaded.TryGetValue(NeoValueOwnership.Save, firstInner, out MemberValue? reloadedInner), Is.True);
            Assert.That(reloadedInner!.typeSource, Is.EqualTo(NeoTypeSource.AtPath(boxId, new[] { "0" })));
        }

        [Test]
        public void DictionaryOfClassBoxesOwnTheirEntries()
        {
            using NeoClient client = BuildClient();
            var point = (PointValue)client.ResolveRegisteredGeneratedClassValue("point-asset")!;
            Write(client, client.save, new Dictionary<string, PointValue> { ["p"] = point });
            var read = (NeoDictionary<PointValue>)Read(client.save)!;
            Assert.That(read["p"].classId, Is.EqualTo(PointClass));
            Assert.That(read["p"].valueId, Is.Not.EqualTo("point-asset"));
            Assert.That(SlotRow(client.save).valueType, Is.EqualTo(TestTypeBindings.FromKey("Dictionary<string,c:" + PointClass + ">")));

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            var cloned = (NeoDictionary<PointValue>)Read(clone)!;
            Assert.That(cloned["p"].valueId, Is.Not.EqualTo(read["p"].valueId));
            using NeoClient reloaded = Reload(client);
            var reread = (NeoDictionary<PointValue>)Read(reloaded.save)!;
            Assert.That(reread.Keys, Is.EqualTo(new[] { "p" }));
            Assert.That(reread["p"].valueId, Is.EqualTo(read["p"].valueId));
        }

        [Test]
        public void CreatedComparersCallTheirClosures()
        {
            using NeoClient client = BuildClient();
            FunctionWithReturnType descending = Closure(
                Int(),
                new[] { IntArgument("x"), IntArgument("y") },
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = new OperationPointer
                    {
                        type = PointerKind.Operation,
                        operation = new ArithmeticOperation
                        {
                            type = OperationKind.Arithmetic,
                            arithmetic = new ArithmeticOpInfo
                            {
                                type = ArithmeticOpKind.Subtraction,
                                pointers = new Pointer[] { Variable("__arg_1__"), Variable("__arg_0__") },
                            },
                        },
                    },
                });
            var payload = new JObject
            {
                ["comparer"] = (int)NeoBuiltInComparerKind.Create,
                ["family"] = (int)NeoComparerFamilyKind.Comparison,
                ["type"] = TestTypeBindings.JsonFromKey("int"),
                ["delegates"] = new JArray(JObject.FromObject(new NeoDelegateValue { action = descending })),
            };
            object created = NeoScriptComparer.Create(typeof(int), client, NeoValueOwnership.Save, payload, NeoComparerFamilyKind.Comparison);
            var values = new List<int> { 1, 3, 2 };
            values.Sort((IComparer<int>)created);
            Assert.That(values, Is.EqualTo(new[] { 3, 2, 1 }));
            Write(client, client.save, created);
            Assert.That(Read(client.save), Is.SameAs(created));
            Assert.That(SlotRow(client.save).valueType, Is.EqualTo(TestTypeBindings.FromKey("c:" + ComparerId + "<int>", client.ProjectDataForRuntime)));

            NeoMemberClassWritable clone = NeoGeneratedTypesSupport.CloneClassValue(client, new ValueRef(client.save.value!.id));
            Assert.That(((IComparer<int>)Read(clone)!).Compare(1, 2), Is.EqualTo(1));
            using NeoClient reloaded = Reload(client);
            var read = (IComparer<int>)Read(reloaded.save)!;
            Assert.That(read, Is.Not.SameAs(created));
            Assert.That(read.Compare(1, 2), Is.EqualTo(1));
            Assert.That(read.Compare(5, 2), Is.EqualTo(-3));
        }

        [Test]
        public void CustomCSharpComparersAreRefusedByTypeAndSlot()
        {
            using NeoClient client = BuildClient();
            var error = Assert.Throws<NotSupportedException>(() => Write(client, client.save, new DescendingComparer(), "Other"));
            Assert.That(error!.Message, Is.EqualTo(
                "Object field 'Other' can't store the C# comparer DescendingComparer; it stores only built-in comparers and comparers read from Neo data."));
            Assert.That(ReadAt(client.save, "Other"), Is.Null);
        }

        [Test]
        public void GenericClassTypesMapThroughTheirRegisteredIds()
        {
            using NeoClient client = BuildClient();
            Type definition = typeof(BoxValue<>);
            Type parameter = definition.GetGenericArguments()[0];
            var definitionBinding = TypeBinding.OfDefinition(new TypeDefinition(MemberKind.Class, classId: BoxClass));

            Write(client, client.save, definition, "TypeSlot");
            Assert.That(ReadAt(client.save, "TypeSlot"), Is.EqualTo(definition));
            Assert.That(TypePayload(client.save), Is.EqualTo(definitionBinding));
            Write(client, client.save, parameter, "TypeSlot");
            Assert.That(ReadAt(client.save, "TypeSlot"), Is.EqualTo(parameter));
            Assert.That(TypePayload(client.save), Is.EqualTo(TypeBinding.Open(new TypeDefinition(MemberKind.Class, classId: BoxClass), "box-t")));
            Write(client, client.save, typeof(BoxValue<int>), "TypeSlot");
            Assert.That(TypePayload(client.save), Is.EqualTo(TestTypeBindings.FromKey("c:" + BoxClass + "<int>", client.ProjectDataForRuntime)));

            using NeoClient reloaded = Reload(client);
            Assert.That(ReadAt(reloaded.save, "TypeSlot"), Is.EqualTo(typeof(BoxValue<int>)));

            using NeoClient unregistered = Reload(client);
            unregistered.TypeArguments.Register(new Dictionary<Type, string>(), new Dictionary<Type, string>());
            var error = Assert.Throws<NotSupportedException>(() => ReadAt(unregistered.save, "TypeSlot"));
            Assert.That(error!.Message, Is.EqualTo("Object field 'TypeSlot' holds typeof(Box<int>), which has no C# type in this project."));
            var unnamed = Assert.Throws<NotSupportedException>(() => Write(unregistered, unregistered.save, definition, "TypeSlot"));
            StringAssert.Contains("Object field 'TypeSlot' can't store typeof(", unnamed!.Message);
        }

        [Test]
        public void FactoryBoxedDelegatesFollowTheOwnershipOfTheirDestination()
        {
            using NeoClient client = BuildClient();
            NeoDelegate<int> userBound = client.save.Get<NeoMemberDelegate>("UserPick").Bind<int>(Convert.ToInt32);

            var direct = Assert.Throws<InvalidOperationException>(() => Write(client, client.save, userBound));
            Assert.That(direct!.Message, Does.Contain(NeoClient.SaveHoldsUserReferenceError));

            NeoMemberClassWritable carrier = NeoGeneratedTypesSupport.CreateWritableClassValue(
                client,
                CarrierClass,
                new NeoGeneratedConstructorValue("Need", "need-field", userBound));
            NeoGeneratedClassValue carried = client.ResolveRegisteredGeneratedClassValue(carrier.value!.id)!;
            Assert.That(ReadAt(carrier, "Need"), Is.InstanceOf<NeoDelegate<int>>());

            var attached = Assert.Throws<InvalidOperationException>(() => Write(client, client.save, carried));
            Assert.That(attached!.Message, Does.Contain(NeoClient.SaveHoldsUserReferenceError));
            Assert.That(Read(client.save), Is.Null);

            Write(client, client.session, carried);
            Assert.That(((NeoGeneratedClassValue)Read(client.session)!).classId, Is.EqualTo(CarrierClass));
        }

        private static TypeBinding TypePayload(NeoMemberClass root) =>
            TypeBindings.Read((JObject)((PayloadMemberValue)RowAt(root, "TypeSlot")).value!["type"]!, "test");

        private static FunctionWithReturnType Closure(TypeInfo returnType, FunctionArgumentTypeInfo[] arguments, params Instruction[] instructions)
        {
            var parameters = new Variable[arguments.Length + 2];
            parameters[0] = new Variable { id = "__this__", typeInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = RootClass }, pointer = Variable("__this__") };
            parameters[1] = new Variable { id = "__root__", typeInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = RootClass }, pointer = Variable("__root__") };
            for (int index = 0; index < arguments.Length; index++)
                parameters[index + 2] = new Variable { id = $"__arg_{index}__", typeInfo = arguments[index], pointer = Variable($"__arg_{index}__") };
            return new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = parameters,
                instructions = instructions,
                typeInfo = returnType,
            };
        }

        private static FunctionArgumentTypeInfo IntArgument(string name) => new() { name = name, type = MemberKind.Int, required = true };

        private static VariablePointer Variable(string id) => new() { type = PointerKind.Variable, variableId = id };

        private sealed class DescendingComparer : IComparer<int>
        {
            public int Compare(int x, int y) => y.CompareTo(x);
        }

        private static T Entries<T>(NeoClient client, NeoMemberClass root, string key)
        {
            NeoMember node = root.Get<NeoMember>(key);
            return NeoGenericBindings.Resolve<T>(client, node).Read(node);
        }

        private static void Write(NeoClient client, NeoMemberClassWritable root, object? value, string key = "Slot") =>
            NeoGeneratedTypesSupport.SetValue(root, key, NeoGeneratedTypesSupport.ObjectValue(client, root.Get<NeoMemberObject>(key), value));

        private static object? Read(NeoMemberClass root) => ReadAt(root, "Slot");

        private static object? ReadAt(NeoMemberClass root, string key) =>
            NeoGeneratedTypesSupport.ReadObjectValue(root.Get<NeoMemberObject>(key));

        private static MemberValue SlotRow(NeoMemberClass root) => RowAt(root, "Slot");

        private static MemberValue RowAt(NeoMemberClass root, string key) => root.Get<NeoMemberObject>(key).value!;

        private static double? PointX(NeoClient client, MemberValue pointRow)
        {
            string xId = ((ObjectMemberValue)pointRow).value!["X"];
            Assert.That(client.TryGetValue(xId, out NumberMemberValue? x), Is.True, xId);
            return x!.value;
        }

        private static NeoClient Reload(NeoClient client) =>
            Prepared(NeoTestSaveStack.ClientFromSchema(client.Synchronizer.Schema, loadedSaveContent: client.SerializeSaveData()));

        /// <summary>What a generated project registers: its enum and class tables, its factories and its native functions.</summary>
        private static NeoClient Prepared(NeoClient client)
        {
            client.TypeArguments.Register(
                new Dictionary<Type, string> { [typeof(PointValue)] = PointClass, [typeof(BoxValue<>)] = BoxClass },
                new Dictionary<Type, string> { [typeof(P127Season)] = "season" });
            client.RegisterGeneratedClassFactories(ReadOnlyFactories(), WritableFactories());
            client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
            {
                ["seven-native"] = (_, _, _, _) => 7,
                ["same-native"] = (_, _, _, _) => true,
            });
            return client;
        }

        private sealed class ValueRef : INeoValueReference
        {
            internal ValueRef(string valueId) => this.valueId = valueId;

            public string? valueId
            {
                get;
            }
        }

        private sealed class PointValue : NeoGeneratedClassValue
        {
            internal PointValue(NeoClient client, NeoMemberClass node, bool isReadOnly)
                : base(client, node, PointClass, isReadOnly)
            {
            }

            internal static PointValue Create(NeoClient client, NeoMemberClass node) =>
                (PointValue)NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(client, node, () => new PointValue(client, node, true));

            internal static PointValue CreateWritable(NeoClient client, NeoMemberClassWritable node) =>
                (PointValue)NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(client, node, () => new PointValue(client, node, false));
        }

        private sealed class BoxValue<T>
        {
        }

        private sealed class RootValue : NeoGeneratedClassValue
        {
            internal RootValue(NeoClient client, NeoMemberClass node, bool isReadOnly)
                : base(client, node, RootClass, isReadOnly)
            {
            }
        }

        private sealed class CarrierValue : NeoGeneratedClassValue
        {
            internal CarrierValue(NeoClient client, NeoMemberClass node, bool isReadOnly)
                : base(client, node, CarrierClass, isReadOnly)
            {
            }
        }

        private static Dictionary<string, NeoGeneratedTypesSupport.ReadOnlyClassFactory> ReadOnlyFactories() => new()
        {
            [CarrierClass] = (client, node) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(client, node, () => new CarrierValue(client, node, true)),
            [PointClass] = (client, node) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(client, node, () => new PointValue(client, node, true)),
            [RootClass] = (client, node) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(client, node, () => new RootValue(client, node, true)),
        };

        private static Dictionary<string, NeoGeneratedTypesSupport.WritableClassFactory> WritableFactories() => new()
        {
            [CarrierClass] = (client, node) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(client, node, () => new CarrierValue(client, node, false)),
            [PointClass] = (client, node) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(client, node, () => new PointValue(client, node, false)),
            [RootClass] = (client, node) => NeoGeneratedTypesSupport.GetOrCreateGeneratedClassValue(client, node, () => new RootValue(client, node, false)),
        };

        private static NeoClient BuildClient(Action<ProjectData>? edit = null)
        {
            var data = new ProjectData
            {
                project = new Project
                {
                    id = ProjectId,
                    _id = ProjectId,
                    name = "Object fields",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, JsonMember>
                {
                    ["root-assets"] = Root("root-assets", "root-assets-value"),
                    ["root-save"] = Root("root-save", "root-save-value"),
                    ["root-session"] = Root("root-session", "root-session-value"),
                    ["slot-field"] = new ObjectMember { id = "slot-field", projectId = ProjectId, name = "Slot", kind = MemberKind.Object },
                    ["held-field"] = new ObjectMember { id = "held-field", projectId = ProjectId, name = "Held", kind = MemberKind.Object, objectType = NeoObjectTypeKind.ValueType },
                    ["other-field"] = new ObjectMember { id = "other-field", projectId = ProjectId, name = "Other", kind = MemberKind.Object },
                    ["enum-field"] = new ObjectMember { id = "enum-field", projectId = ProjectId, name = "EnumSlot", kind = MemberKind.Object, objectType = NeoObjectTypeKind.Enum },
                    ["type-field"] = new ObjectMember { id = "type-field", projectId = ProjectId, name = "TypeSlot", kind = MemberKind.Object, objectType = NeoObjectTypeKind.Type },
                    ["culture-field"] = new ObjectMember { id = "culture-field", projectId = ProjectId, name = "CultureSlot", kind = MemberKind.Object, objectType = NeoObjectTypeKind.CultureInfo },
                    ["format-field"] = new ObjectMember { id = "format-field", projectId = ProjectId, name = "FormatSlot", kind = MemberKind.Object, objectType = NeoObjectTypeKind.NumberFormatInfo },
                    ["need-field"] = new ObjectMember { id = "need-field", projectId = ProjectId, name = "Need", kind = MemberKind.Object, Requirement = NeoMemberRequirementKind.Required },
                    ["maybe-field"] = new ObjectMember { id = "maybe-field", projectId = ProjectId, name = "Maybe", kind = MemberKind.Object },
                    ["seven-native"] = Native("seven-native", "Seven", Int()),
                    ["same-native"] = Native("same-native", "Same", new PrimitiveTypeInfo { type = MemberKind.Bool, required = true }, "x", "y"),
                    ["pick-field"] = new DelegateMember
                    {
                        id = "pick-field",
                        projectId = ProjectId,
                        name = "Pick",
                        kind = MemberKind.NSDelegate,
                        Requirement = NeoMemberRequirementKind.Required,
                        returnTypeInfo = Int(),
                        argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                        defaultValue = new DelegateMemberValueBase { value = new NeoDelegateValue { memberId = "seven-native", valueId = null } },
                    },
                    ["bag-field"] = new ListMember { id = "bag-field", projectId = ProjectId, name = "Bag", kind = MemberKind.List, entryMemberId = "bag-entry" },
                    ["bag-entry"] = new ObjectMember { id = "bag-entry", projectId = ProjectId, name = "Bag", kind = MemberKind.Object },
                    ["pool-field"] = new SetMember { id = "pool-field", projectId = ProjectId, name = "Pool", kind = MemberKind.Set, entryMemberId = "pool-entry" },
                    ["pool-entry"] = new ObjectMember { id = "pool-entry", projectId = ProjectId, name = "Pool", kind = MemberKind.Object },
                    ["map-field"] = new DictionaryMember { id = "map-field", projectId = ProjectId, name = "Map", kind = MemberKind.Dictionary, entryMemberId = "map-entry" },
                    ["map-entry"] = new ObjectMember { id = "map-entry", projectId = ProjectId, name = "Map", kind = MemberKind.Object },
                    ["pair-field"] = new TupleMember { id = "pair-field", projectId = ProjectId, name = "Pair", kind = MemberKind.Tuple, elementMemberIds = new[] { "pair-count", "pair-held" } },
                    ["pair-count"] = new IntMember { id = "pair-count", projectId = ProjectId, name = "Item1", kind = MemberKind.Int, Requirement = NeoMemberRequirementKind.Required },
                    ["pair-held"] = new ObjectMember { id = "pair-held", projectId = ProjectId, name = "Item2", kind = MemberKind.Object },
                    ["user-pick-field"] = new DelegateMember
                    {
                        id = "user-pick-field",
                        projectId = ProjectId,
                        name = "UserPick",
                        kind = MemberKind.NSDelegate,
                        Requirement = NeoMemberRequirementKind.Required,
                        returnTypeInfo = Int(),
                        argumentTypes = Array.Empty<FunctionArgumentTypeInfo>(),
                        defaultValue = new DelegateMemberValueBase { value = new NeoDelegateValue { memberId = "seven-native", valueId = NeoTestUserRoot.ValueId } },
                    },
                    ["point-field"] = new ClassMember { id = "point-field", projectId = ProjectId, name = "Point", kind = MemberKind.Class, DeclaredClassId = PointClass },
                    ["x-field"] = new IntMember { id = "x-field", projectId = ProjectId, name = "X", kind = MemberKind.Int, Requirement = NeoMemberRequirementKind.Required },
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = new ObjectMemberValue
                    {
                        id = "root-assets-value",
                        classId = RootClass,
                        value = new Dictionary<string, string> { ["Point"] = "point-asset" },
                    },
                    ["root-save-value"] = new ObjectMemberValue { id = "root-save-value", classId = RootClass, value = new Dictionary<string, string>() },
                    ["root-session-value"] = new ObjectMemberValue { id = "root-session-value", classId = RootClass, value = new Dictionary<string, string>() },
                    ["point-asset"] = new ObjectMemberValue
                    {
                        id = "point-asset",
                        classId = PointClass,
                        value = new Dictionary<string, string> { ["X"] = "point-x" },
                    },
                    ["point-x"] = new NumberMemberValue { id = "point-x", value = 4d },
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [RootClass] = new NeoSchemaClass
                    {
                        id = RootClass,
                        projectId = ProjectId,
                        name = "Root",
                        schema = new Dictionary<string, string>
                        {
                            ["Slot"] = "slot-field",
                            ["Held"] = "held-field",
                            ["Other"] = "other-field",
                            ["EnumSlot"] = "enum-field",
                            ["TypeSlot"] = "type-field",
                            ["CultureSlot"] = "culture-field",
                            ["FormatSlot"] = "format-field",
                            ["Pick"] = "pick-field",
                            ["Point"] = "point-field",
                            ["Bag"] = "bag-field",
                            ["Pool"] = "pool-field",
                            ["Map"] = "map-field",
                            ["Pair"] = "pair-field",
                            ["UserPick"] = "user-pick-field",
                        },
                    },
                    [CarrierClass] = new NeoSchemaClass
                    {
                        id = CarrierClass,
                        projectId = ProjectId,
                        name = "Carrier",
                        schema = new Dictionary<string, string>
                        {
                            ["Need"] = "need-field",
                            ["Maybe"] = "maybe-field",
                            ["Seven"] = "seven-native",
                            ["Same"] = "same-native",
                        },
                    },
                    [EqualityComparerId] = SystemClass(EqualityComparerId, "EqualityComparer", "equality-t"),
                    [ComparerId] = SystemClass(ComparerId, "Comparer", "comparer-t"),
                    [ReferenceEqualityComparerId] = SystemClass(ReferenceEqualityComparerId, "ReferenceEqualityComparer", null),
                    [BoxClass] = new NeoSchemaClass
                    {
                        id = BoxClass,
                        projectId = ProjectId,
                        name = "Box",
                        schema = new Dictionary<string, string>(),
                        genericParams = new List<GenericParamDeclaration> { new() { id = "box-t", name = "T" } },
                    },
                    [PointClass] = new NeoSchemaClass
                    {
                        id = PointClass,
                        projectId = ProjectId,
                        name = "Point",
                        schema = new Dictionary<string, string> { ["X"] = "x-field" },
                    },
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>
                {
                    ["season"] = new NeoCompose.Runtime.Json.Enum
                    {
                        id = "season",
                        projectId = ProjectId,
                        name = "Season",
                        options = new Dictionary<string, EnumOption>
                        {
                            ["season-spring"] = new EnumOption { text = "Spring" },
                            ["season-summer"] = new EnumOption { text = "Summer" },
                        },
                        optionKeyOrder = new List<string> { "season-spring", "season-summer" },
                    },
                },
            };
            NeoSystemComparerClasses.Register(JObject.FromObject(new Dictionary<string, NeoSchemaClass>
            {
                [EqualityComparerId] = data.classes[EqualityComparerId],
                [ComparerId] = data.classes[ComparerId],
                [ReferenceEqualityComparerId] = data.classes[ReferenceEqualityComparerId],
            }));
            edit?.Invoke(data);
            return Prepared(NeoTestSaveStack.ClientFromSchema(data.WithUserRoot()));
        }

        private static NeoSchemaClass SystemClass(string id, string name, string? param) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            schema = new Dictionary<string, string>(),
            genericParams = param is null ? null : new List<GenericParamDeclaration> { new() { id = param, name = "T" } },
        };

        private static PrimitiveTypeInfo Int() => new() { type = MemberKind.Int, required = true };

        private static FunctionMember Native(string id, string name, TypeInfo returnType, params string[] intParameters) => new()
        {
            id = id,
            projectId = ProjectId,
            name = name,
            kind = MemberKind.Function,
            returnTypeInfo = returnType,
            argumentTypes = Array.ConvertAll(intParameters, parameter => new FunctionArgumentTypeInfo { name = parameter, type = MemberKind.Int, required = true }),
            Dispatch = NeoFunctionDispatchKind.Synchronous,
            Modifier = NeoMemberModifierKind.Static,
        };

        private static ClassMember Root(string id, string valueId) => new()
        {
            id = id,
            projectId = ProjectId,
            name = id,
            kind = MemberKind.Class,
            Requirement = NeoMemberRequirementKind.Required,
            valueId = valueId,
            DeclaredClassId = RootClass,
        };
    }
}
