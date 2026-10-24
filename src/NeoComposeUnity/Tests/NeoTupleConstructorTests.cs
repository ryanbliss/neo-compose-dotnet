// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public class NeoTupleConstructorTests
    {
        private static ProjectData Data(bool optional = false, bool classes = false)
        {
            ProjectData data = NeoGenericTestFixture.BuildProjectData();
            data.classes["tuple-host"] = new NeoSchemaClass
            {
                id = "tuple-host",
                projectId = "project-a",
                name = "TupleHost",
                schema = new Dictionary<string, string> { ["Pair"] = "tuple-member" },
            };
            data.members["tuple-int"] = new IntMember
            {
                id = "tuple-int",
                projectId = "project-a",
                name = "Number",
                kind = MemberKind.Int,
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.members["tuple-class"] = new ClassMember
            {
                id = "tuple-class",
                projectId = "project-a",
                name = "Card",
                kind = MemberKind.Class,
                DeclaredClassId = "class-card-base",
                Requirement = NeoMemberRequirementKind.Required,
            };
            data.members["tuple-member"] = new TupleMember
            {
                id = "tuple-member",
                projectId = "project-a",
                name = "Pair",
                kind = MemberKind.Tuple,
                Requirement = optional ? NeoMemberRequirementKind.Optional : NeoMemberRequirementKind.Required,
                elementMemberIds = new[] { classes ? "tuple-class" : "tuple-int", "tuple-int" },
            };
            return data;
        }

        private static NeoMemberClassWritable Construct(NeoClient client, params MemberValue[] rows) =>
            NeoGeneratedTypesSupport.CreateWritableClassValue(client, "tuple-host",
                new Dictionary<string, string> { ["Pair"] = "tuple-row" }, rows);

        private static ArrayMemberValue Pair(params string[] entries) => new() { id = "tuple-row", value = entries };
        private static NumberMemberValue Number(string id, double? value) => new() { id = id, value = value };

        [Test]
        public void SuppliedTuplePublishesTypedEntries()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(Data());
            using var holder = Construct(client, Pair("one", "two"), Number("one", 7), Number("two", 9));
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, "tuple-row", out ArrayMemberValue? pair));
            CollectionAssert.AreEqual(new[] { "one", "two" }, pair!.value);
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, "one", out NumberMemberValue? one));
            Assert.AreEqual(7, one!.value);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OptionalTupleDefaultAndSuppliedNullStayNull(bool supplied)
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(Data(optional: true));
            using var holder = supplied
                ? Construct(client, new ArrayMemberValue { id = "tuple-row", value = null })
                : NeoGeneratedTypesSupport.CreateWritableClassValue(client, "tuple-host");
            Assert.IsNull(holder.Get<NeoMemberTuple>("Pair").value?.value);
        }

        [Test]
        public void TupleDeclarationDefaultClonesItsEntries()
        {
            ProjectData data = Data();
            ((TupleMember)data.members["tuple-member"]).defaultValue = new ArrayMemberValueBase { value = new[] { "one", "two" } };
            data.values["one"] = Number("one", 7);
            data.values["two"] = Number("two", 9);
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            using var first = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "tuple-host");
            using var second = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "tuple-host");
            var a = first.Get<NeoMemberTuple>("Pair").value!;
            var b = second.Get<NeoMemberTuple>("Pair").value!;
            Assert.AreNotEqual(a.id, b.id);
            Assert.AreNotEqual(a.value![0], b.value![0]);
            Assert.IsTrue(client.TryGetValue(a.value[0], out NumberMemberValue? one));
            Assert.AreEqual(7, one!.value);
        }

        [TestCase("arity", "arity")]
        [TestCase("shape", "row shape")]
        [TestCase("entry", "row shape")]
        [TestCase("null-entry", "null value")]
        [TestCase("null-tuple", "null value")]
        [TestCase("missing", "unstaged")]
        [TestCase("duplicate", "two owned parents")]
        public void InvalidTupleIsRejectedBeforePublication(string failure, string diagnostic)
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(Data());
            var rows = new List<MemberValue> { Pair("one", "two"), Number("one", 7), Number("two", 9) };
            switch (failure)
            {
                case "arity":
                    rows[0] = Pair("one");
                    break;
                case "shape":
                    rows[0] = Number("tuple-row", 0);
                    break;
                case "entry":
                    rows[2] = new StringMemberValue { id = "two", value = "wrong" };
                    break;
                case "null-entry":
                    rows[2] = Number("two", null);
                    break;
                case "null-tuple":
                    rows[0] = new ArrayMemberValue { id = "tuple-row", value = null };
                    break;
                case "missing":
                    rows.RemoveAt(2);
                    break;
                case "duplicate":
                    rows[0] = Pair("one", "one");
                    rows.RemoveAt(2);
                    break;
            }
            int before = client.sessionValues.Count;
            StringAssert.Contains(diagnostic, Assert.Throws<InvalidOperationException>(() => Construct(client, rows.ToArray()))!.Message);
            Assert.AreEqual(before, client.sessionValues.Count);
        }

        [Test]
        public void NestedTupleAndListEntriesAreVisited()
        {
            ProjectData data = Data();
            data.members["tuple-list"] = new ListMember
            {
                id = "tuple-list",
                projectId = "project-a",
                name = "List",
                kind = MemberKind.List,
                Requirement = NeoMemberRequirementKind.Required,
                entryMemberId = "tuple-int",
            };
            data.members["inner-tuple"] = new TupleMember
            {
                id = "inner-tuple",
                projectId = "project-a",
                name = "Inner",
                kind = MemberKind.Tuple,
                Requirement = NeoMemberRequirementKind.Required,
                elementMemberIds = new[] { "tuple-list", "tuple-int" },
            };
            ((TupleMember)data.members["tuple-member"]).elementMemberIds[0] = "inner-tuple";
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            using var holder = Construct(client, Pair("inner", "two"),
                new ArrayMemberValue { id = "inner", value = new[] { "list", "three" } },
                new ArrayMemberValue { id = "list", value = new[] { "one" } },
                Number("one", 7), Number("two", 9), Number("three", 11));
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, "one", out NumberMemberValue? one));
            Assert.AreEqual(7, one!.value);
            var node = holder.Get<NeoMember>("Pair");
            var projected = NeoGenericBindings.Resolve<((IReadOnlyList<int>, int), int)>(client, node).Read(node);
            Assert.AreEqual(7, projected.Item1.Item1[0]);
            Assert.AreEqual(11, projected.Item1.Item2);
            Assert.AreEqual(9, projected.Item2);
        }

        [Test]
        public void GenericTupleCodecReadsWritesAndSerializesThroughTheSlot()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(Data());
            using var holder = Construct(client, Pair("one", "two"), Number("one", 7), Number("two", 9));
            var node = holder.Get<NeoMember>("Pair");
            var codec = NeoGenericBindings.Resolve<(int, int)>(client, node);
            Assert.AreEqual((7, 9), codec.Read(node));
            codec.Write(node, (11, 13));
            Assert.AreEqual((11, 13), codec.Read(holder.Get<NeoMember>("Pair")));
            holder.SetSerializedValue("Pair", codec.Serialize((17, 19)));
            Assert.AreEqual((17, 19), codec.Read(holder.Get<NeoMember>("Pair")));
            Assert.Throws<InvalidOperationException>(() => NeoGenericBindings.Resolve<int>(client, node));
            Assert.Throws<NeoTypeArgumentException>(() => NeoGenericBindings.Resolve<(int, int, int)>(client, node).Read(node));
        }

        [Test]
        public void OptionalGenericTupleNullAndValueRoundTrip()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(Data(optional: true));
            using var holder = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "tuple-host");
            var node = holder.Get<NeoMember>("Pair");
            var codec = NeoGenericBindings.Resolve<(int, int)?>(client, node);
            Assert.IsNull(codec.Read(node));
            Assert.Throws<NeoTypeArgumentException>(() => NeoGenericBindings.Resolve<(int, int)>(client, node).Read(node));
            codec.Write(node, (3, 5));
            Assert.AreEqual((3, 5), codec.Read(holder.Get<NeoMember>("Pair")));
            codec.Write(holder.Get<NeoMember>("Pair"), null);
            Assert.IsNull(codec.Read(holder.Get<NeoMember>("Pair")));
        }

        [Test]
        public void NestedDictionaryAndNullableListReadValuesInsteadOfRowIds()
        {
            ProjectData data = Data();
            ((IntMember)data.members["tuple-int"]).Requirement = NeoMemberRequirementKind.Optional;
            data.members["tuple-list"] = new ListMember
            {
                id = "tuple-list",
                projectId = "project-a",
                name = "Items",
                kind = MemberKind.List,
                Requirement = NeoMemberRequirementKind.Required,
                entryMemberId = "tuple-int",
            };
            data.members["tuple-dictionary"] = new DictionaryMember
            {
                id = "tuple-dictionary",
                projectId = "project-a",
                name = "ItemsByKey",
                kind = MemberKind.Dictionary,
                Requirement = NeoMemberRequirementKind.Required,
                entryMemberId = "tuple-list",
            };
            ((TupleMember)data.members["tuple-member"]).elementMemberIds = new[] { "tuple-dictionary", "tuple-int" };
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            using var holder = Construct(client, Pair("dictionary", "two"),
                new ObjectMemberValue { id = "dictionary", value = new Dictionary<string, string> { ["a"] = "list" } },
                new ArrayMemberValue { id = "list", value = new[] { "one", "null" } },
                Number("one", 7), Number("null", null), Number("two", 9));
            var node = holder.Get<NeoMember>("Pair");
            var codec = NeoGenericBindings.Resolve<(IReadOnlyDictionary<string, IReadOnlyList<int?>>, int?)>(client, node);
            var read = codec.Read(node);
            Assert.AreEqual(9, read.Item2);
            CollectionAssert.AreEqual(new int?[] { 7, null }, read.Item1["a"]);
            codec.Write(node, read);
            CollectionAssert.AreEqual(new int?[] { 7, null }, codec.Read(holder.Get<NeoMember>("Pair")).Item1["a"]);
        }

        [Test]
        public void LongGenericTupleUsesTheClrRestWithoutLosingPositions()
        {
            ProjectData data = Data();
            ((TupleMember)data.members["tuple-member"]).elementMemberIds = new[]
            { "tuple-int", "tuple-int", "tuple-int", "tuple-int", "tuple-int", "tuple-int", "tuple-int", "tuple-int", "tuple-int" };
            var rows = new List<MemberValue>();
            var ids = new string[9];
            for (int index = 0; index < ids.Length; index++)
            {
                ids[index] = "number-" + index;
                rows.Add(Number(ids[index], index));
            }
            rows.Add(Pair(ids));
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(data);
            using var holder = Construct(client, rows.ToArray());
            var node = holder.Get<NeoMember>("Pair");
            var codec = NeoGenericBindings.Resolve<(int, int, int, int, int, int, int, int, int)>(client, node);
            Assert.AreEqual((0, 1, 2, 3, 4, 5, 6, 7, 8), codec.Read(node));
        }

        [Test]
        public void ClassTupleEntryRetainsOwnershipAndLateFailureIsAtomic()
        {
            using NeoClient client = NeoTestSaveStack.ClientFromSchema(Data(classes: true));
            using var card = NeoGeneratedTypesSupport.CreateWritableClassValue(client, "class-card-base");
            string cardId = card.value!.id;
            int before = client.sessionValues.Count;
            StringAssert.Contains("row shape", Assert.Throws<InvalidOperationException>(() => Construct(client,
                Pair(cardId, "bad"), new StringMemberValue { id = "bad", value = "wrong" }))!.Message);
            Assert.AreEqual(before, client.sessionValues.Count);
            using var first = Construct(client, Pair(cardId, "two"), Number("two", 9));
            int afterFirst = client.sessionValues.Count;
            StringAssert.Contains("already owned", Assert.Throws<InvalidOperationException>(() =>
                NeoGeneratedTypesSupport.CreateWritableClassValue(client, "tuple-host",
                    new Dictionary<string, string> { ["Pair"] = "second-tuple" },
                    new MemberValue[] { new ArrayMemberValue { id = "second-tuple", value = new[] { cardId, "second-int" } }, Number("second-int", 1) }))!.Message);
            Assert.AreEqual(afterFirst, client.sessionValues.Count);
        }
        [TestCase("list")]
        [TestCase("dictionary")]
        [TestCase("set")]
        [TestCase("readonly-set")]
        public void TupleCollectionGenericCodecRoundTrip(string kind)
        {
            var d = Data();
            Member m = kind == "dictionary" ? new DictionaryMember { id = "collection", name = "Items", projectId = "project-a", kind = MemberKind.Dictionary, entryMemberId = "tuple-member", Requirement = NeoMemberRequirementKind.Required }
                : kind.Contains("set") ? new SetMember { id = "collection", name = "Items", projectId = "project-a", kind = MemberKind.Set, entryMemberId = "tuple-member", Requirement = NeoMemberRequirementKind.Required }
                : new ListMember { id = "collection", name = "Items", projectId = "project-a", kind = MemberKind.List, entryMemberId = "tuple-member", Requirement = NeoMemberRequirementKind.Required };
            d.members[m.id] = m;
            d.classes["tuple-host"].schema = new Dictionary<string, string> { { "Items", m.id } };
            using var c = NeoTestSaveStack.ClientFromSchema(d);
            MemberValue row = kind == "dictionary" ? new ObjectMemberValue { id = "items", value = new Dictionary<string, string>() } : new ArrayMemberValue { id = "items", value = Array.Empty<string>() };
            using var holder = NeoGeneratedTypesSupport.CreateWritableClassValue(c, "tuple-host", new Dictionary<string, string> { { "Items", "items" } }, new[] { row });
            var node = holder.Get<NeoMember>("Items");
            if (kind == "list")
            {
                var xs = NeoGenericBindings.Resolve<NeoList<(int, int)>>(c, node).Read(node);
                xs.Add((7, 9));
                Assert.AreEqual((7, 9), xs[0]);
                xs[0] = (11, 13);
                Assert.AreEqual((11, 13), xs[0]);
                xs.RemoveAt(0);
                Assert.Zero(xs.Count);
            }
            if (kind == "dictionary")
            {
                var xs = NeoGenericBindings.Resolve<NeoDictionary<(int, int)>>(c, node).Read(node);
                xs.Add("a", (7, 9));
                Assert.AreEqual((7, 9), xs["a"]);
                xs["a"] = (11, 13);
                Assert.AreEqual((11, 13), xs["a"]);
                Assert.IsTrue(xs.Remove("a"));
            }
            if (kind == "set")
            {
                var xs = NeoGenericBindings.Resolve<NeoSet<(int, int)>>(c, node).Read(node);
                Assert.IsTrue(xs.Add((7, 9)));
                Assert.IsTrue(xs.Contains((7, 9)));
                Assert.IsFalse(xs.Add((7, 9)));
                Assert.IsTrue(xs.Remove((7, 9)));
                Assert.Zero(xs.Count);
            }
            if (kind == "readonly-set")
            {
                var xs = NeoGenericBindings.Resolve<NeoReadOnlySet<(int, int)>>(c, node).Read(node);
                Assert.IsFalse(xs.Contains((7, 9)));
                var writable = NeoGenericBindings.Resolve<NeoSet<(int, int)>>(c, node).Read(node);
                Assert.IsTrue(writable.Add((7, 9)));
                Assert.IsTrue(xs.Contains((7, 9)));
                Assert.IsTrue(writable.Remove((7, 9)));
                Assert.IsFalse(xs.Contains((7, 9)));
            }
        }
        private static ProjectData GenericData(bool optional)
        {
            var d = Data();
            d.classes["tuple-host"].genericParams = new List<GenericParamDeclaration> { new() { id = "p-review", name = "T" }, new() { id = "q-review", name = "U" } };
            d.classes["closed-review"] = new NeoSchemaClass { id = "closed-review", projectId = "project-a", name = "Closed", extendsClassId = "tuple-host", schema = new Dictionary<string, string>(), extendsGenericBindings = new Dictionary<string, GenericBinding> { ["p-review"] = new() { kind = NeoGenericBindingKind.Member, memberId = "tuple-int" }, ["q-review"] = new() { kind = NeoGenericBindingKind.Member, memberId = "review-string" } } };
            d.members["review-string"] = new StringMember { id = "review-string", name = "Text", projectId = "project-a", kind = MemberKind.String, Requirement = NeoMemberRequirementKind.Optional };
            d.members["p-member"] = new GenericMember { id = "p-member", name = "P", projectId = "project-a", kind = MemberKind.Generic, genericParamId = "p-review" };
            d.members["q-member"] = new GenericMember { id = "q-member", name = "Q", projectId = "project-a", kind = MemberKind.Generic, genericParamId = "q-review" };
            d.members["nested-list"] = new ListMember { id = "nested-list", name = "List", projectId = "project-a", kind = MemberKind.List, Requirement = NeoMemberRequirementKind.Required, entryMemberId = "q-member" };
            var tuple = (TupleMember)d.members["tuple-member"];
            tuple.elementMemberIds = new[] { "p-member", "nested-list" };
            tuple.Requirement = optional ? NeoMemberRequirementKind.Optional : NeoMemberRequirementKind.Required;
            return d;
        }
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ClosedTupleEnvironmentSurvivesFactoryAndWholeSlotWrites(bool optional, bool serialize)
        {
            using var c = NeoTestSaveStack.ClientFromSchema(GenericData(optional));
            using var holder = optional
                ? NeoGeneratedTypesSupport.CreateWritableClassValue(c, "closed-review")
                : NeoGeneratedTypesSupport.CreateWritableClassValue(c, "closed-review", new NeoGeneratedConstructorValue("Pair", "tuple-member", (7, new string?[] { "a", null })));
            var node = holder.Get<NeoMember>("Pair");
            var codec = NeoGenericBindings.Resolve<(int, IReadOnlyList<string?>)?>(c, node);
            if (optional)
                Assert.IsNull(codec.Read(node));
            else
            {
                Assert.AreEqual(7, codec.Read(node)!.Value.Item1);
                CollectionAssert.AreEqual(new string?[] { "a", null }, codec.Read(node)!.Value.Item2);
            }
            if (serialize)
                holder.SetSerializedValue("Pair", codec.Serialize((11, new string?[] { null, "b" })));
            else
                codec.Write(node, (11, new string?[] { null, "b" }));
            var read = codec.Read(holder.Get<NeoMember>("Pair"))!.Value;
            Assert.AreEqual(11, read.Item1);
            CollectionAssert.AreEqual(new string?[] { null, "b" }, read.Item2);
        }
        [Test]
        public void GenericTupleRequiredEntryFailurePublishesNothing()
        {
            using var c = NeoTestSaveStack.ClientFromSchema(GenericData(false));
            int before = c.sessionValues.Count;
            Assert.Throws<InvalidOperationException>(() => NeoGeneratedTypesSupport.CreateWritableClassValue(c, "closed-review", new NeoGeneratedConstructorValue("Pair", "tuple-member", ((object?)null, new string?[] { "a" }))));
            Assert.AreEqual(before, c.sessionValues.Count);
        }

        [Test]
        public void OptionalGenericTupleCanClearAndRewrite()
        {
            using var c = NeoTestSaveStack.ClientFromSchema(GenericData(true));
            using var holder = NeoGeneratedTypesSupport.CreateWritableClassValue(c, "closed-review", new NeoGeneratedConstructorValue("Pair", "tuple-member", (7, new string?[] { "a", null })));
            var node = holder.Get<NeoMember>("Pair");
            var codec = NeoGenericBindings.Resolve<(int, IReadOnlyList<string?>)?>(c, node);
            codec.Write(node, null);
            Assert.IsNull(codec.Read(holder.Get<NeoMember>("Pair")));
            codec.Write(holder.Get<NeoMember>("Pair"), (11, new string?[] { "b" }));
            Assert.AreEqual(11, codec.Read(holder.Get<NeoMember>("Pair"))!.Value.Item1);
        }

        private static NeoMemberClassWritable CreateTupleSetOwner(NeoClient client, string factoryName = "CreateInt")
        {
            string factory = client.members.Values.Single(x => x.name == factoryName).id;
            var created = (INeoValueReference)NeoMemberNSFunction.GetOrCreateStatic(client, factory).InvokeStatic()!;
            string id = created.valueId!;
            Assert.IsTrue(client.TryGetValue(NeoValueOwnership.Session, id, out ObjectMemberValue? owner));
            return new NeoMemberClassWritable(client, new ClassMember
            {
                id = "review-g-slot",
                projectId = "project-id",
                name = "G",
                kind = MemberKind.Class,
                DeclaredClassId = owner!.classId!,
                Requirement = NeoMemberRequirementKind.Required
            }, id, NeoValueOwnership.Session);
        }
        private static void ScriptAdd(NeoClient client, NeoMemberClassWritable parent)
        {
            string method = client.members.Values.Single(x => x.name == "AddValue").id;
            using var invoke = new NeoMemberNSFunction(client, method, null, NeoValueOwnership.Session);
            Assert.AreEqual(true, invoke.Invoke(parent.value!.id, new object[] { 7d }));
        }
        [TestCase("add-duplicate")]
        [TestCase("contains-empty")]
        [TestCase("contains-existing")]
        [TestCase("remove-existing")]
        [TestCase("nested-add-duplicate")]
        public void ClosedGenericTupleSetWrapper(string operation)
        {
            using var client = NeoSourceParityFixture.CreateClient("generic-tuple-set-parity.json");
            using var parent = CreateTupleSetOwner(client);
            var node = parent.Get<NeoMember>("Items");
            Assert.That(node.value!.genericBindings, Is.Not.Null.And.Not.Empty);
            var writable = NeoGenericBindings.Resolve<NeoSet<(int, int)>>(client, node).Read(node);
            var view = NeoGenericBindings.Resolve<NeoReadOnlySet<(int, int)>>(client, node).Read(node);
            if (operation.EndsWith("existing"))
            {
                ScriptAdd(client, parent);
                CollectionAssert.AreEqual(new[] { (7, 1) }, view);
            }
            if (operation == "add-duplicate")
            {
                Assert.IsTrue(writable.Add((7, 1)));
                Assert.IsFalse(writable.Add((7, 1)));
            }
            if (operation == "contains-empty")
                Assert.IsFalse(view.Contains((7, 1)));
            if (operation == "contains-existing")
                Assert.IsTrue(view.Contains((7, 1)));
            if (operation == "remove-existing")
                Assert.IsTrue(writable.Remove((7, 1)));
            if (operation == "nested-add-duplicate")
            {
                var nested = parent.Get<NeoMember>("Nested");
                var xs = NeoGenericBindings.Resolve<NeoSet<((int, int), string)>>(client, nested).Read(nested);
                Assert.IsTrue(xs.Add(((7, 1), "a")));
                Assert.IsFalse(xs.Add(((7, 1), "a")));
            }
        }

        [Test]
        public void TupleSetKeysKeepDifferentClosedBindingsInOneClient()
        {
            using var client = NeoSourceParityFixture.CreateClient("generic-tuple-set-parity.json");
            using var integers = CreateTupleSetOwner(client);
            using var strings = CreateTupleSetOwner(client, "CreateString");
            var intNode = integers.Get<NeoMember>("Items");
            var stringNode = strings.Get<NeoMember>("Items");
            var numbers = NeoGenericBindings.Resolve<NeoSet<(int, int)>>(client, intNode).Read(intNode);
            var texts = NeoGenericBindings.Resolve<NeoSet<(string, int)>>(client, stringNode).Read(stringNode);
            Assert.IsTrue(numbers.Add((7, 1)));
            Assert.IsTrue(texts.Add(("7", 1)));
            Assert.IsFalse(numbers.Add((7, 1)));
            Assert.IsFalse(texts.Add(("7", 1)));
            Assert.IsTrue(numbers.Contains((7, 1)));
            Assert.IsTrue(texts.Contains(("7", 1)));
            Assert.IsFalse(numbers.Contains((8, 1)));
            Assert.IsFalse(texts.Contains(("8", 1)));
            Assert.IsTrue(numbers.Remove((7, 1)));
            Assert.IsTrue(texts.Contains(("7", 1)));
            Assert.IsTrue(texts.Remove(("7", 1)));
            Assert.Zero(numbers.Count);
            Assert.Zero(texts.Count);
        }

    }
}
