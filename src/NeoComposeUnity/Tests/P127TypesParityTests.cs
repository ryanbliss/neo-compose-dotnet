// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Parity = NeoCompose.Tests.P127TypesParity;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 B2 types core: the Unity half of the parity gate. The cases and
    /// their source are a verbatim copy of neo-compose's
    /// <c>src/models/neoscript/p127-types-parity-fixture.json</c>; the export
    /// is that source's <c>neo export</c>, which neo-compose's
    /// <c>cli/src/commands/p127-types-parity.test.ts</c> writes when
    /// <c>NEO_P127_TYPES_PARITY_EXPORT</c> names this package's copy.
    /// </summary>
    public class P127TypesParityTests
    {
        private const string PackageRoot = "Packages/com.ryanbliss.neocompose/Tests";

        public static IEnumerable<string> Returns() =>
            Fixture()["returns"]!.Children<JProperty>().Select(property => property.Name);

        public static IEnumerable<string> Refuses() =>
            Fixture()["refuses"]!.Children<JProperty>().Select(property => property.Name);

        public static IEnumerable<TestCaseData> AssetIdentities() =>
            Fixture()["assetIdentity"]!.Children<JObject>().Select(test => new TestCaseData(test).SetName("AssetIdentity_" + test["name"]!.Value<string>()));

        [TestCaseSource(nameof(AssetIdentities))]
        public void AssetIdentityMatchesSharedFixture(JObject test)
        {
            using NeoClient client = Client();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var kind = (MemberKind)System.Enum.Parse(typeof(MemberKind), test["kind"]!.Value<string>()!);
            TypeBinding type = TypeBinding.Closed(new TypeHead(kind, true));
            NeoBox Box(string side) => NeoBox.Of(type, test[side]!.ToObject<Dictionary<string, object?>>()!);
            NeoBox left = Box("left");
            NeoBox right = Box("right");
            Assert.That(ReferenceEquals(left.Payload, right.Payload), Is.False);
            Assert.That(NSGetterEvaluator.ReferenceEqual(left, right, ctx), Is.EqualTo(test["equal"]!.Value<bool>()));
        }

        [TestCaseSource(nameof(Returns))]
        public void ProbeReturns(string probe)
        {
            using NeoClient client = Client();
            NSFunctionMember function = Probe(client, probe);
            object? value = NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(Array.Empty<object?>());
            JToken actual = value is null ? JValue.CreateNull() : JToken.FromObject(value);
            JToken expected = Fixture()["returns"]![probe]!;
            // A float probe's result is the single nearest the fixture's decimal spelling.
            if (function.returnTypeInfo.type == MemberKind.Float)
                Assert.That((float)actual.Value<double>(), Is.EqualTo((float)expected.Value<double>()), probe);
            else if (expected.Type is JTokenType.Integer or JTokenType.Float)
                Assert.That(actual.Value<double>(), Is.EqualTo(expected.Value<double>()), probe);
            else
                Assert.That(JToken.DeepEquals(expected, actual), Is.True, $"{probe}: {actual}");
            if (NestedSetConstructorReturns().Contains(probe))
                AssertNestedConstructorPayload(client, probe, NeoValueOwnership.Session);
        }

        public static IEnumerable<string> ReviewFixReturns() => Returns().Where(name =>
            name.StartsWith("GenericTargetBox", StringComparison.Ordinal)
            || name is "StoredBoxTupleReplay" or "StoredBoxNestedTupleReplay" or "StoredBoxDeepTupleReplay"
                or "StoredBoxReplacementReplay" or "StoredBoxTupleIdentity");

        [TestCaseSource(nameof(ReviewFixReturns))]
        public void ReviewFixReturnsAfterSerializedSaveReplay(string probe)
        {
            ProjectData Schema()
            {
                ProjectData data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "p127-types-parity-export.json")));
                NeoSchemaClass statics = data.classes.Values.Single(schema => schema.name == "ReviewStoredBoxes");
                // Exercise the same exported constructors and probe IR with durable static storage.
                foreach (string memberId in statics.schema.Values)
                    data.members[memberId].Storage = NeoMemberStorage.Save;
                return data;
            }
            string serialized;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(Schema(), assumeCurrentSchema: false))
            {
                NeoSchemaClass statics = client.ProjectDataForRuntime.classes.Values.Single(schema => schema.name == "ReviewStoredBoxes");
                var ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session);
                try
                {
                    TypeBinding integer = TypeBinding.Closed(new TypeHead(MemberKind.Int, true));
                    TypeBinding list = TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding> { ["0"] = integer });
                    TypeBinding Pair(TypeBinding a, TypeBinding b) => TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true),
                        new Dictionary<string, TypeBinding> { ["0"] = a, ["1"] = b });
                    RuntimeTuple Tuple(TypeBinding type, object a, object b) => new(new[] { a, b }, (TupleTypeInfo)NeoTypeBindings.ToTypeInfo(type));
                    TypeBinding ints = Pair(integer, integer);
                    foreach (string name in new[] { "Tuple", "Nested", "Deep", "Replacement" })
                    {
                        var member = (ClassMember)client.members[statics.schema[name]];
                        TypeBinding type = name == "Tuple" ? ints : name == "Deep" ? Pair(ints, list) : Pair(list, integer);
                        RuntimeTuple payload = name switch
                        {
                            "Tuple" => Tuple(type, 1d, 2d),
                            "Nested" => Tuple(type, new object?[] { 1d, 2d }, 3d),
                            "Deep" => Tuple(type, Tuple(ints, 1d, 2d), new object?[] { 3d, 4d }),
                            _ => Tuple(type, new object?[] { 5d, 6d }, 7d),
                        };
                        NeoSchemaClass taker = client.ProjectDataForRuntime.classes[member.DeclaredClassId!];
                        var fields = Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>();
                        var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(client,
                            new ClassTypeInfo { type = MemberKind.Class, required = true, classId = taker.id },
                            taker.constructorIds!.Single(), new[] { "item" }, fields);
                        var created = NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(resolved,
                            new object?[] { NeoBox.Of(type, payload) }, fields, ctx);
                        NeoGeneratedTypesSupport.StaticBinding(client, member.id, NeoValueOwnership.Save)
                            .SetValue(NeoValueWritePayload.FromValueReference(created.value.id));
                    }
                    AssertReviewReturn(client, probe);
                    serialized = client.SerializeSaveData();
                }
                finally { client.ReturnDirectFunctionContext(ctx, null); }
            }
            var stored = Newtonsoft.Json.JsonConvert.DeserializeObject<ProjectSaveData>(serialized)!;
            Assert.That(stored.staticBindings, Has.Count.EqualTo(4));
            var visited = new HashSet<string>();
            void AssertDurableChildren(string id)
            {
                Assert.That(stored.values.ContainsKey(id), Is.True, id);
                if (!visited.Add(id) || stored.values[id] is not ArrayMemberValue array)
                    return;
                Assert.That(array.value, Is.Not.Null);
                foreach (string child in array.value!)
                    AssertDurableChildren(child);
            }
            foreach (string? id in stored.staticBindings.Values)
            {
                MemberValue root = stored.values[id!];
                string argumentId = root.constructorArgs!.Values.Single()!.Value<string>()!;
                var argument = (ArrayMemberValue)stored.values[argumentId];
                Assert.That(argument.valueType!.Head!.Type, Is.EqualTo(MemberKind.Tuple));
                Assert.That(argument.value, Has.Length.EqualTo(2));
                AssertDurableChildren(argumentId);
            }
            // Reopen exactly the SDK's serialized output, without warming or repairing its graph.
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(Schema(), loadedSaveContent: serialized, assumeCurrentSchema: false);
            AssertReviewReturn(reopened, probe);
            NeoSchemaClass savedStatics = reopened.ProjectDataForRuntime.classes.Values.Single(schema => schema.name == "ReviewStoredBoxes");
            foreach (string name in new[] { "Tuple", "Nested", "Deep", "Replacement" })
            {
                var item = NeoGeneratedTypesSupport.StaticBinding(reopened, savedStatics.schema[name], NeoValueOwnership.Save)
                    .GetRequiredNode<NeoMemberClass>().Get<NeoMemberObject>("Item");
                object? first = NeoGeneratedTypesSupport.ReadObjectValue(item);
                Assert.That(NeoGeneratedTypesSupport.ReadObjectValue(item), Is.SameAs(first), name);
            }
        }

        public static IEnumerable<string> AggregateConstructorReturns() => Returns().Where(name =>
            name.StartsWith("ConstructorAggregate", StringComparison.Ordinal)
            || name is "ConstructorNullableTupleType" or "ConstructorNestedNullableValues");

        [TestCaseSource(nameof(AggregateConstructorReturns))]
        public void AggregateConstructorReturnsAfterSerializedSaveReplay(string probe)
        {
            ProjectData Schema()
            {
                ProjectData data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "p127-types-parity-export.json")));
                foreach (NeoSchemaClass store in data.classes.Values.Where(schema => schema.name.StartsWith("R3", StringComparison.Ordinal) && schema.name.EndsWith("Store", StringComparison.Ordinal)))
                    data.members[store.schema["Value"]].Storage = NeoMemberStorage.Save;
                return data;
            }
            string serialized;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(Schema(), assumeCurrentSchema: false))
            {
                var ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session);
                try
                {
                    TypeBinding integer = TypeBinding.Closed(new TypeHead(MemberKind.Int, true));
                    TypeBinding obj = TypeBinding.Closed(new TypeHead(MemberKind.Object, true));
                    TypeBinding text = TypeBinding.Closed(new TypeHead(MemberKind.String, true));
                    TypeBinding list = TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding> { ["0"] = obj });
                    TypeBinding Pair(TypeBinding a, TypeBinding b) => TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true),
                        new Dictionary<string, TypeBinding> { ["0"] = a, ["1"] = b });
                    RuntimeTuple Tuple(TypeBinding type, object? a, object? b) => new(new[] { a, b }, (TupleTypeInfo)NeoTypeBindings.ToTypeInfo(type));
                    TypeBinding ints = Pair(integer, integer);
                    NeoBox BoxInt(int value) => NeoBox.Of(integer, (double)value);
                    NeoBox BoxTuple() => NeoBox.Of(ints, Tuple(ints, 1d, 2d));
                    NeoBox repeated = BoxTuple();
                    TypeBinding nullableTuple = Pair(text, integer);
                    TypeBinding objectText = Pair(obj, text);
                    TypeBinding nested = Pair(nullableTuple, objectText);
                    var inputs = new Dictionary<string, object?>
                    {
                        ["R3ListStore"] = new object?[] { BoxInt(3), BoxTuple() },
                        ["R3AliasStore"] = new object?[] { repeated, repeated },
                        ["R3TupleStore"] = Tuple(Pair(obj, integer), BoxInt(3), 2d),
                        ["R3DictionaryStore"] = new Dictionary<string, object?> { ["a"] = BoxInt(3), ["b"] = BoxTuple() },
                        ["R3SetStore"] = new object?[] { BoxInt(3), BoxTuple() },
                        ["R3SetReadStore"] = new object?[] { BoxInt(3), BoxTuple() },
                        ["R3NestedStore"] = new object?[] { Tuple(Pair(obj, list), BoxInt(3), new object?[] { BoxTuple(), BoxInt(4) }) },
                        ["R3NullTupleStore"] = NeoBox.Of(nullableTuple, Tuple(nullableTuple, null, 2d)),
                        ["R3NullNestedStore"] = NeoBox.Of(nested, Tuple(nested, Tuple(nullableTuple, null, 2d), Tuple(objectText, BoxInt(3), null))),
                    };
                    foreach (var input in inputs)
                    {
                        NeoSchemaClass store = client.ProjectDataForRuntime.classes.Values.Single(schema => schema.name == input.Key);
                        var member = (ClassMember)client.members[store.schema["Value"]];
                        NeoSchemaClass taker = client.ProjectDataForRuntime.classes[member.DeclaredClassId!];
                        var fields = Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>();
                        var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(client,
                            new ClassTypeInfo { type = MemberKind.Class, required = true, classId = taker.id },
                            taker.constructorIds!.Single(), new[] { "item" }, fields);
                        var created = NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(resolved, new[] { input.Value }, fields, ctx);
                        NeoGeneratedTypesSupport.StaticBinding(client, member.id, NeoValueOwnership.Save)
                            .SetValue(NeoValueWritePayload.FromValueReference(created.value.id));
                    }
                    AssertReviewReturn(client, probe);
                    serialized = client.SerializeSaveData();
                }
                finally { client.ReturnDirectFunctionContext(ctx, null); }
            }
            var stored = Newtonsoft.Json.JsonConvert.DeserializeObject<ProjectSaveData>(serialized)!;
            Assert.That(stored.staticBindings, Has.Count.EqualTo(9));
            foreach (string? id in stored.staticBindings.Values)
            {
                string argumentId = stored.values[id!].constructorArgs!.Values.Single()!.Value<string>()!;
                Assert.That(stored.values.ContainsKey(argumentId), Is.True, "The constructor argument must survive even when its body only reads Count.");
            }
            // The second client gets the untouched serialized SDK Save, with no live object caches.
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(Schema(), loadedSaveContent: serialized, assumeCurrentSchema: false);
            AssertReviewReturn(reopened, probe);
        }

        [Test]
        public void ConstructorEnumSetRetainsSelectionIdsAfterSerializedSaveReplay()
        {
            const string constructorId = "enum-set-constructor";
            ProjectData Schema()
            {
                ProjectData data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "p127-types-parity-export.json")));
                NeoSchemaClass taker = data.classes.Values.Single(schema => schema.name == "R3SetReadTake");
                ConstructorRecord constructor = Newtonsoft.Json.JsonConvert.DeserializeObject<ConstructorRecord>(
                    Newtonsoft.Json.JsonConvert.SerializeObject(data.constructors[taker.constructorIds!.Single()]))!;
                constructor.id = constructorId;
                var entry = new EnumTypeInfo { type = MemberKind.Enum, required = true, enumId = data.enums.Values.Single(item => item.name == "Planet").id };
                constructor.argumentTypes[0].entryTypeInfo = entry;
                constructor.action.parameters[2].typeInfo = new LookupTypeInfo { type = MemberKind.Lookup, required = true, entryTypeInfo = entry };
                data.constructors[constructorId] = constructor;
                taker.constructorIds = taker.constructorIds.Append(constructorId).ToArray();
                NeoSchemaClass store = data.classes.Values.Single(schema => schema.name == "R3SetReadStore");
                data.members[store.schema["Value"]].Storage = NeoMemberStorage.Save;
                return data;
            }
            string serialized;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(Schema(), assumeCurrentSchema: false))
            {
                NeoSchemaClass store = client.ProjectDataForRuntime.classes.Values.Single(schema => schema.name == "R3SetReadStore");
                var member = (ClassMember)client.members[store.schema["Value"]];
                var fields = Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>();
                var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(client,
                    new ClassTypeInfo { type = MemberKind.Class, required = true, classId = member.DeclaredClassId! },
                    constructorId, new[] { "item" }, fields);
                var ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session);
                try
                {
                    object?[] options = client.ProjectDataForRuntime.enums.Values.Single(item => item.name == "Planet").options.Keys.Take(2).Cast<object?>().ToArray();
                    var created = NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(resolved, new object?[] { options }, fields, ctx);
                    NeoGeneratedTypesSupport.StaticBinding(client, member.id, NeoValueOwnership.Save)
                        .SetValue(NeoValueWritePayload.FromValueReference(created.value.id));
                    AssertReviewReturn(client, "ConstructorAggregateSetReadOnly");
                    serialized = client.SerializeSaveData();
                }
                finally { client.ReturnDirectFunctionContext(ctx, null); }
            }
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(Schema(), loadedSaveContent: serialized, assumeCurrentSchema: false);
            AssertReviewReturn(reopened, "ConstructorAggregateSetReadOnly");
        }

        public static IEnumerable<string> NestedSetConstructorReturns() => new[]
        {
            "ConstructorTupleSet", "ConstructorTupleSetDictionary", "ConstructorListSet",
            "ConstructorDictionarySet", "ConstructorTupleListBoxes", "ConstructorTupleDictionaryBox",
        };

        [TestCaseSource(nameof(NestedSetConstructorReturns))]
        public void NestedSetConstructorReturnsAfterSerializedSaveReplay(string probe)
        {
            ProjectData Schema()
            {
                ProjectData data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "p127-types-parity-export.json")));
                foreach (NeoSchemaClass store in data.classes.Values.Where(schema => schema.name.StartsWith("R4", StringComparison.Ordinal) && schema.name.EndsWith("Store", StringComparison.Ordinal)))
                    data.members[store.schema["Value"]].Storage = NeoMemberStorage.Save;
                return data;
            }
            string serialized;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(Schema(), assumeCurrentSchema: false))
            {
                var ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session);
                try
                {
                    TypeBinding integer = TypeBinding.Closed(new TypeHead(MemberKind.Int, true));
                    TypeBinding obj = TypeBinding.Closed(new TypeHead(MemberKind.Object, true));
                    TypeBinding text = TypeBinding.Closed(new TypeHead(MemberKind.String, true));
                    TypeBinding Pair(TypeBinding a, TypeBinding b) => TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true),
                        new Dictionary<string, TypeBinding> { ["0"] = a, ["1"] = b });
                    TypeBinding Collection(MemberKind kind) => TypeBinding.Closed(new TypeHead(kind, true),
                        new Dictionary<string, TypeBinding> { ["0"] = obj });
                    TypeBinding dictionary = TypeBinding.Closed(new TypeHead(MemberKind.Dictionary, true),
                        new Dictionary<string, TypeBinding> { ["0"] = text, ["1"] = obj });
                    RuntimeTuple Tuple(TypeBinding type, object? a, object? b) => new(new[] { a, b }, (TupleTypeInfo)NeoTypeBindings.ToTypeInfo(type));
                    TypeBinding ints = Pair(integer, integer);
                    object?[] Boxes() => new object?[] { NeoBox.Of(integer, 3d), NeoBox.Of(ints, Tuple(ints, 1d, 2d)) };
                    Dictionary<string, object?> Dictionary() => new()
                    {
                        ["x"] = NeoBox.Of(integer, 4d)
                    };
                    var inputs = new Dictionary<string, object?>
                    {
                        ["R4TupleSetStore"] = Tuple(Pair(Collection(MemberKind.Set), integer), Boxes(), 2d),
                        ["R4TupleSetDictionaryStore"] = Tuple(Pair(Collection(MemberKind.Set), dictionary), Boxes(), Dictionary()),
                        ["R4ListSetStore"] = new object?[] { Boxes() },
                        ["R4DictionarySetStore"] = new Dictionary<string, object?> { ["x"] = Boxes() },
                        ["R4TupleListBoxesStore"] = Tuple(Pair(Collection(MemberKind.List), integer), Boxes(), 2d),
                        ["R4TupleDictionaryBoxStore"] = Tuple(Pair(dictionary, integer), Dictionary(), 2d),
                    };
                    foreach (var input in inputs)
                    {
                        NeoSchemaClass store = client.ProjectDataForRuntime.classes.Values.Single(schema => schema.name == input.Key);
                        var member = (ClassMember)client.members[store.schema["Value"]];
                        NeoSchemaClass taker = client.ProjectDataForRuntime.classes[member.DeclaredClassId!];
                        var fields = Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>();
                        var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(client,
                            new ClassTypeInfo { type = MemberKind.Class, required = true, classId = taker.id },
                            taker.constructorIds!.Single(), new[] { "item" }, fields);
                        var created = NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(resolved, new[] { input.Value }, fields, ctx);
                        NeoGeneratedTypesSupport.StaticBinding(client, member.id, NeoValueOwnership.Save)
                            .SetValue(NeoValueWritePayload.FromValueReference(created.value.id));
                    }
                    AssertReviewReturn(client, probe);
                    AssertNestedConstructorPayload(client, probe, NeoValueOwnership.Save);
                    serialized = client.SerializeSaveData();
                }
                finally { client.ReturnDirectFunctionContext(ctx, null); }
            }
            var stored = Newtonsoft.Json.JsonConvert.DeserializeObject<ProjectSaveData>(serialized)!;
            Assert.That(stored.staticBindings, Has.Count.EqualTo(6));
            ProjectData schema = Schema();
            var containerEntries = stored.values.Values.Where(row => row.containerId is not null).ToLookup(row => row.containerId!);
            foreach (var binding in stored.staticBindings)
            {
                var root = (ObjectMemberValue)stored.values[binding.Value!];
                var member = (ClassMember)schema.members[binding.Key];
                Assert.That(root.classId, Is.EqualTo(member.DeclaredClassId));
                Assert.That(root.instanceConstructorId, Is.EqualTo(schema.classes[member.DeclaredClassId!].constructorIds!.Single()));
                Assert.That(root.constructorArgs, Has.Count.EqualTo(1));
                string argumentId = root.constructorArgs!.Values.Single()!.Value<string>()!;
                var visited = new HashSet<string>();
                var numbers = new List<double>();
                var boxes = new List<MemberKind>();
                void ReadPayload(string id)
                {
                    Assert.That(stored.values.ContainsKey(id), Is.True, id);
                    if (!visited.Add(id))
                        return;
                    MemberValue row = stored.values[id];
                    if (row.valueType?.Head is { } held)
                        boxes.Add(held.Type);
                    if (row is NumberMemberValue number)
                        numbers.Add(number.value!.Value);
                    if (row is ArrayMemberValue array)
                        foreach (string child in array.value ?? Array.Empty<string>())
                            ReadPayload(child);
                    if (row is ObjectMemberValue dictionaryRow && dictionaryRow.value is not null)
                        foreach (string child in dictionaryRow.value.Values)
                            ReadPayload(child);
                    foreach (MemberValue child in containerEntries[id])
                        ReadPayload(child.id);
                }
                ReadPayload(argumentId);
                string name = schema.classes[root.classId!].name;
                double[] expected = name switch
                {
                    "R4TupleSetTake" or "R4TupleListBoxesTake" => new[] { 3d, 1d, 2d, 2d },
                    "R4TupleSetDictionaryTake" => new[] { 3d, 1d, 2d, 4d },
                    "R4TupleDictionaryBoxTake" => new[] { 4d, 2d },
                    _ => new[] { 3d, 1d, 2d },
                };
                Assert.That(numbers, Is.EquivalentTo(expected), name);
                Assert.That(boxes, Does.Contain(MemberKind.Int), name);
                if (name != "R4TupleDictionaryBoxTake")
                    Assert.That(boxes, Does.Contain(MemberKind.Tuple), name);
            }
            // Reopen the untouched SDK output with a new client and no live value caches.
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(schema, loadedSaveContent: serialized, assumeCurrentSchema: false);
            AssertReviewReturn(reopened, probe);
            AssertNestedConstructorPayload(reopened, probe, NeoValueOwnership.Save);
        }

        private static void AssertNestedConstructorPayload(NeoClient client, string probe, NeoValueOwnership ownership)
        {
            string storeName = "R4" + probe.Substring("Constructor".Length) + "Store";
            NeoSchemaClass store = client.ProjectDataForRuntime.classes.Values.Single(schema => schema.name == storeName);
            var item = NeoGeneratedTypesSupport.StaticBinding(client, store.schema["Value"], ownership)
                .GetRequiredNode<NeoMemberClass>().Get<NeoMember>("Item");
            var ctx = client.RentDirectFunctionContext(item.ownership);
            try
            {
                object? payload = NSGetterEvaluator.ResolveHostCollection(
                    NSGetterEvaluator.UnwrapRow(item.value!, ctx, item.ownership, item.member), ctx);
                var numbers = new List<double>();
                var boxes = new List<MemberKind>();
                void Read(object? value)
                {
                    switch (value)
                    {
                        case NeoBox box:
                            boxes.Add(box.ValueType.Head!.Type);
                            Read(NSGetterEvaluator.ResolveHostCollection(box.Payload, ctx));
                            break;
                        case Dictionary<string, object?> dictionary:
                            foreach (object? child in dictionary.Values)
                                Read(child);
                            break;
                        case IEnumerable<object?> sequence:
                            foreach (object? child in sequence)
                                Read(child);
                            break;
                        case double or int:
                            numbers.Add(Convert.ToDouble(value));
                            break;
                        default:
                            Assert.Fail($"{probe}: unexpected constructor payload {value} ({value?.GetType()}).");
                            break;
                    }
                }
                Read(payload);
                double[] expected = probe switch
                {
                    "ConstructorTupleSet" or "ConstructorTupleListBoxes" => new[] { 3d, 1d, 2d, 2d },
                    "ConstructorTupleSetDictionary" => new[] { 3d, 1d, 2d, 4d },
                    "ConstructorTupleDictionaryBox" => new[] { 4d, 2d },
                    _ => new[] { 3d, 1d, 2d },
                };
                Assert.That(numbers, Is.EquivalentTo(expected), probe);
                MemberKind[] expectedBoxes = probe switch
                {
                    "ConstructorTupleSetDictionary" => new[] { MemberKind.Int, MemberKind.Tuple, MemberKind.Int },
                    "ConstructorTupleDictionaryBox" => new[] { MemberKind.Int },
                    _ => new[] { MemberKind.Int, MemberKind.Tuple },
                };
                Assert.That(boxes, Is.EquivalentTo(expectedBoxes), probe);
            }
            finally { client.ReturnDirectFunctionContext(ctx, null); }
        }

        public static IEnumerable<string> BoxedSetConstructorReturns() => new[]
        {
            "ConstructorBoxedTupleSetPayload", "ConstructorBoxedListSetPayload", "ConstructorBoxedDictionarySetPayload",
            "ConstructorNestedBoxedTupleSetPayload", "ConstructorNestedTupleSetPayload", "ConstructorBoxedSetPayload",
            "ConstructorBoxedTupleSetDictionaryPayload", "ConstructorBoxedNullableSetListPayload", "ConstructorBoxedEmptySetPayload",
        };

        [TestCaseSource(nameof(BoxedSetConstructorReturns))]
        public void BoxedSetConstructorReturnsAfterSerializedSaveReplay(string probe)
        {
            string storeName = "R5" + probe.Substring("Constructor".Length).Replace("Payload", "Store");
            ProjectData Schema()
            {
                ProjectData data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "p127-types-parity-export.json")));
                NeoSchemaClass store = data.classes.Values.Single(schema => schema.name == storeName);
                data.members[store.schema["Value"]].Storage = NeoMemberStorage.Save;
                return data;
            }
            string serialized;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(Schema(), assumeCurrentSchema: false))
            {
                var ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session);
                try
                {
                    TypeBinding integer = TypeBinding.Closed(new TypeHead(MemberKind.Int, true));
                    TypeBinding obj = TypeBinding.Closed(new TypeHead(MemberKind.Object, true));
                    TypeBinding text = TypeBinding.Closed(new TypeHead(MemberKind.String, true));
                    TypeBinding Pair(TypeBinding a, TypeBinding b) => TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true),
                        new Dictionary<string, TypeBinding> { ["0"] = a, ["1"] = b });
                    TypeBinding Collection(MemberKind kind, TypeBinding entry) => TypeBinding.Closed(new TypeHead(kind, true),
                        new Dictionary<string, TypeBinding> { ["0"] = entry });
                    TypeBinding DictionaryType(TypeBinding entry) => TypeBinding.Closed(new TypeHead(MemberKind.Dictionary, true),
                        new Dictionary<string, TypeBinding> { ["0"] = text, ["1"] = entry });
                    RuntimeTuple Tuple(TypeBinding type, object? a, object? b) => new(new[] { a, b }, (TupleTypeInfo)NeoTypeBindings.ToTypeInfo(type));
                    TypeBinding ints = Pair(integer, integer);
                    TypeBinding set = Collection(MemberKind.Set, obj);
                    TypeBinding setTuple = Pair(set, integer);
                    RuntimeSet Set(bool empty = false) => NSGetterEvaluator.MakeRuntimeSet(empty ? Array.Empty<object?>()
                        : new object?[] { NeoBox.Of(integer, 3d), NeoBox.Of(ints, Tuple(ints, 1d, 2d)) }, false, false, ctx);
                    RuntimeTuple TupleSet() => Tuple(setTuple, Set(), 7d);
                    TypeBinding objectDictionary = DictionaryType(obj);
                    NeoBox input = probe switch
                    {
                        "ConstructorBoxedTupleSetPayload" => NeoBox.Of(setTuple, TupleSet()),
                        "ConstructorBoxedListSetPayload" => NeoBox.Of(Collection(MemberKind.List, set), new object?[] { Set() }),
                        "ConstructorBoxedDictionarySetPayload" => NeoBox.Of(DictionaryType(set), new Dictionary<string, object?> { ["x"] = Set() }),
                        "ConstructorNestedBoxedTupleSetPayload" => NeoBox.Of(Pair(obj, integer), Tuple(Pair(obj, integer), NeoBox.Of(setTuple, TupleSet()), 9d)),
                        "ConstructorNestedTupleSetPayload" => NeoBox.Of(Pair(setTuple, integer), Tuple(Pair(setTuple, integer), TupleSet(), 9d)),
                        "ConstructorBoxedSetPayload" => NeoBox.Of(set, Set()),
                        "ConstructorBoxedTupleSetDictionaryPayload" => NeoBox.Of(Pair(set, objectDictionary), Tuple(Pair(set, objectDictionary), Set(), new Dictionary<string, object?> { ["x"] = NeoBox.Of(integer, 4d) })),
                        // Box type identity erases reference annotations; its payload view still admits null Set entries.
                        "ConstructorBoxedNullableSetListPayload" => NeoBox.Of(Collection(MemberKind.List, set), new object?[] { Set(), null }),
                        "ConstructorBoxedEmptySetPayload" => NeoBox.Of(set, Set(empty: true)),
                        _ => throw new ArgumentOutOfRangeException(nameof(probe)),
                    };
                    NeoSchemaClass store = client.ProjectDataForRuntime.classes.Values.Single(schema => schema.name == storeName);
                    var member = (ClassMember)client.members[store.schema["Value"]];
                    NeoSchemaClass taker = client.ProjectDataForRuntime.classes[member.DeclaredClassId!];
                    var fields = Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>();
                    var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(client,
                        new ClassTypeInfo { type = MemberKind.Class, required = true, classId = taker.id },
                        taker.constructorIds!.Single(), new[] { "item" }, fields);
                    var created = NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(resolved, new object?[] { input }, fields, ctx);
                    NeoGeneratedTypesSupport.StaticBinding(client, member.id, NeoValueOwnership.Save)
                        .SetValue(NeoValueWritePayload.FromValueReference(created.value.id));
                    AssertReviewReturn(client, probe);
                    serialized = client.SerializeSaveData();
                }
                finally { client.ReturnDirectFunctionContext(ctx, null); }
            }
            var stored = Newtonsoft.Json.JsonConvert.DeserializeObject<ProjectSaveData>(serialized)!;
            Assert.That(stored.staticBindings, Has.Count.EqualTo(1));
            var root = (ObjectMemberValue)stored.values[stored.staticBindings.Values.Single()!];
            Assert.That(root.instanceConstructorId, Is.Not.Null.And.Not.Empty);
            Assert.That(root.constructorArgs, Has.Count.EqualTo(1));
            string argumentId = root.constructorArgs!.Values.Single()!.Value<string>()!;
            Assert.That(stored.values[argumentId].valueType, Is.Not.Null);
            var entries = stored.values.Values.Where(row => row.containerId is not null).ToLookup(row => row.containerId!);
            var visited = new HashSet<string>();
            var numbers = new List<double>();
            void ReadPayload(string id)
            {
                Assert.That(stored.values.ContainsKey(id), Is.True, id);
                if (!visited.Add(id))
                    return;
                MemberValue row = stored.values[id];
                if (row is NumberMemberValue number)
                    numbers.Add(number.value!.Value);
                if (row is ArrayMemberValue array)
                    foreach (string child in array.value ?? Array.Empty<string>())
                        ReadPayload(child);
                if (row is ObjectMemberValue dictionary && dictionary.value is not null)
                    foreach (string child in dictionary.value.Values)
                        ReadPayload(child);
                foreach (MemberValue child in entries[id])
                    ReadPayload(child.id);
            }
            ReadPayload(argumentId);
            double[] expected = probe switch
            {
                "ConstructorBoxedTupleSetPayload" => new[] { 3d, 1d, 2d, 7d },
                "ConstructorNestedBoxedTupleSetPayload" or "ConstructorNestedTupleSetPayload" => new[] { 3d, 1d, 2d, 7d, 9d },
                "ConstructorBoxedTupleSetDictionaryPayload" => new[] { 3d, 1d, 2d, 4d },
                "ConstructorBoxedEmptySetPayload" => Array.Empty<double>(),
                _ => new[] { 3d, 1d, 2d },
            };
            Assert.That(numbers, Is.EquivalentTo(expected), probe);
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(Schema(), loadedSaveContent: serialized, assumeCurrentSchema: false);
            AssertReviewReturn(reopened, probe);
        }

        private static void AssertReviewReturn(NeoClient client, string probe)
        {
            object? value = InvokeProbe(client, probe);
            JToken actual = value is null ? JValue.CreateNull() : JToken.FromObject(value);
            JToken expected = Fixture()["returns"]![probe]!;
            if (expected.Type is JTokenType.Integer or JTokenType.Float)
                Assert.That(actual.Value<double>(), Is.EqualTo(expected.Value<double>()), probe);
            else
                Assert.That(JToken.DeepEquals(expected, actual), Is.True, $"{probe}: {actual}");
        }

        [TestCaseSource(nameof(Refuses))]
        public void ProbeRefuses(string probe)
        {
            using NeoClient client = Client();
            StringAssert.Contains(Fixture()["refuses"]![probe]!.Value<string>(), Assert.Catch(() => InvokeProbe(client, probe))!.Message);
        }

        [Test]
        public void CSharpBoxesAnIntArgumentToAnObjectParameter()
        {
            using var app = new Parity.TestProjectNeo(Client());

            Assert.AreEqual(5, Parity.Probe.Unbox(5));
        }

        [Test]
        public void StoredBoxConstructorArgumentRecordsTheExistingRow()
        {
            using NeoClient client = Client();
            NeoSchemaClass taker = client.ProjectDataForRuntime.classes.Values.Single(schema => schema.name == "Taker");
            var row = new NumberMemberValue
            {
                id = "stored-constructor-box",
                value = 7,
                valueType = TypeBinding.Closed(new TypeHead(MemberKind.Int, true)),
            };
            client.SetWritableValue(NeoValueOwnership.Session, row);
            NSGetterEvaluator.Context ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session);
            try
            {
                var slot = new ObjectMember { id = "box-slot", name = "Box", kind = MemberKind.Object };
                object? box = NSGetterEvaluator.UnwrapRow(row, ctx, NeoValueOwnership.Session, slot);
                var fields = Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>();
                var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(client,
                    new ClassTypeInfo { type = MemberKind.Class, required = true, classId = taker.id },
                    taker.constructorIds!.Single(), new[] { "item" }, fields);
                var created = NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(resolved, new[] { box }, fields, ctx);
                Assert.That(created.value.constructorArgs!.Values.Single()!.Value<string>(), Is.EqualTo(row.id));
            }
            finally
            {
                client.ReturnDirectFunctionContext(ctx, null);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ConstructorArgumentsSurviveSerializedSaveReplay(bool outArgument)
        {
            ProjectData Schema()
            {
                ProjectData data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "p127-types-parity-export.json")));
                NeoSchemaClass taker = data.classes.Values.Single(schema => schema.name == "Taker");
                if (outArgument)
                {
                    ConstructorRecord constructor = data.constructors[taker.constructorIds!.Single()];
                    constructor.argumentTypes[0].mode = NeoParameterModeKind.Out;
                    var assignment = new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        operatorValue = "=",
                        target = new WriteTarget { pointer = new VariablePointer { type = PointerKind.Variable, variableId = "__arg_0__" }, typeInfo = new ObjectTypeInfo { type = MemberKind.Object, required = true }, writability = WritabilityKind.Local },
                        pointer = new BoxPointer
                        {
                            type = PointerKind.Box,
                            sourceType = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                            pointer = new ValuePointer { type = PointerKind.Value, value = new Value { typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true }, value = new JValue(7) } },
                        },
                    };
                    constructor.action.instructions = new Instruction[] { assignment }.Concat(constructor.action.instructions).ToArray();
                    foreach (ObjectMemberValue row in data.values.Values.OfType<ObjectMemberValue>())
                        if (row.instanceConstructorId == constructor.id)
                            row.constructorArgs!["__arg_0__"] = new JObject { ["out"] = true };
                }
                var save = (ClassMember)data.members[data.project.rootSaveFileMemberId];
                data.classes[save.DeclaredClassId!].schema["SavedTaker"] = "saved-taker";
                data.members["saved-taker"] = new ClassMember
                {
                    id = "saved-taker",
                    name = "SavedTaker",
                    projectId = data.project.id,
                    kind = MemberKind.Class,
                    DeclaredClassId = taker.id,
                    Requirement = NeoMemberRequirementKind.Optional,
                };
                return data;
            }
            string serialized;
            using (NeoClient client = NeoTestSaveStack.ClientFromSchema(Schema(), assumeCurrentSchema: false))
            {
                NeoSchemaClass taker = client.ProjectDataForRuntime.classes.Values.Single(schema => schema.name == "Taker");
                var ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session);
                try
                {
                    var list = TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding>
                    {
                        ["0"] = TypeBinding.Closed(new TypeHead(MemberKind.Int, true)),
                    });
                    var fields = Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>();
                    var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(client,
                        new ClassTypeInfo { type = MemberKind.Class, required = true, classId = taker.id },
                        taker.constructorIds!.Single(), new[] { "item" }, fields);
                    NeoOutReference output = NeoOutReference.Discard();
                    var created = NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(resolved,
                        new object?[] { outArgument ? (object)output : NeoBox.Of(list, new object?[] { 1d, 2d }) }, fields, ctx);
                    if (outArgument)
                    {
                        Assert.That(((NeoBox)output.Read()!).Payload, Is.EqualTo(7d));
                        Assert.That(JToken.DeepEquals(created.value.constructorArgs!["__arg_0__"], new JObject { ["out"] = true }), Is.True);
                    }
                    NeoGeneratedTypesSupport.SetValue(client.save, "SavedTaker", NeoValueWritePayload.FromValueReference(created.value.id));
                    serialized = client.SerializeSaveData();
                }
                finally { client.ReturnDirectFunctionContext(ctx, null); }
            }
            using NeoClient reopened = NeoTestSaveStack.ClientFromSchema(Schema(), loadedSaveContent: serialized, assumeCurrentSchema: false);
            var item = reopened.save.Get<NeoMemberClass>("SavedTaker").Get<NeoMemberObject>("Item");
            if (outArgument)
                Assert.That(NeoGeneratedTypesSupport.ReadObjectValue(item), Is.EqualTo(7));
            else
            {
                var values = (System.Collections.IEnumerable)NeoGeneratedTypesSupport.ReadObjectValue(item)!;
                Assert.That(values.Cast<object>().ToArray(), Is.EqualTo(new object[] { 1, 2 }));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TupleConstructorArgumentsAdoptFreshChildrenAndRefuseOwnedChildren(bool owned)
        {
            using NeoClient client = Client();
            NeoSchemaClass taker = client.ProjectDataForRuntime.classes.Values.Single(schema => schema.name == "Taker");
            var ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session);
            try
            {
                TypeBinding integer = TypeBinding.Closed(new TypeHead(MemberKind.Int, true));
                TypeBinding list = TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding> { ["0"] = integer });
                TypeBinding tuple = TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true), new Dictionary<string, TypeBinding> { ["0"] = list, ["1"] = integer });
                var fields = Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>();
                var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(client,
                    new ClassTypeInfo { type = MemberKind.Class, required = true, classId = taker.id },
                    taker.constructorIds!.Single(), new[] { "item" }, fields);
                var child = new object?[] { 1d };
                if (owned)
                    NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(resolved, new object?[] { NeoBox.Of(list, child) }, fields, ctx);
                var box = NeoBox.Of(tuple, new RuntimeTuple(new object?[] { child, 2d }, (TupleTypeInfo)NeoTypeBindings.ToTypeInfo(tuple)));
                if (owned)
                {
                    int before = client.sessionValues.Count;
                    var error = Assert.Throws<NSGetterRuntimeError>(() => NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(resolved, new object?[] { box }, fields, ctx))!;
                    Assert.That(error.Message, Is.EqualTo("Writing a boxed (List<int>, int) into constructor argument '__arg_0__' would share a collection already owned by another slot. Call Clone() or use Reference."));
                    Assert.That(client.sessionValues.Count, Is.EqualTo(before));
                }
                else
                {
                    NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(resolved, new object?[] { box }, fields, ctx);
                    Assert.That(NSGetterEvaluator.FindRowReference(child, ctx), Is.Not.Null);
                }
            }
            finally
            {
                client.ReturnDirectFunctionContext(ctx, null);
            }
        }

        private static NeoClient Client()
        {
            ProjectData data = NeoTestExport.Read(File.ReadAllText(Path.Combine(PackageRoot, "p127-types-parity-export.json")));
            return NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
        }

        private static JObject Fixture() =>
            JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "p127-types-parity-fixture.json")));

        private static NSFunctionMember Probe(NeoClient client, string name) =>
            (NSFunctionMember)client.members.Values.Single(member => member is NSFunctionMember && member.name == name);

        private static object? InvokeProbe(NeoClient client, string name) =>
            NeoMemberNSFunction.GetOrCreateStatic(client, Probe(client, name).id).InvokeStatic(Array.Empty<object?>());
    }
}
