// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>P127 §4.8 and §5.3: what <c>is</c> and casts accept over the types parity export's classes.</summary>
    public sealed class P127RuntimeTypeCheckTests
    {
        private NeoClient client = null!;
        private NSGetterEvaluator.Context ctx = null!;

        [SetUp]
        public void SetUp()
        {
            client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/p127-types-parity-export.json")), assumeCurrentSchema: false);
            ctx = new NSGetterEvaluator.Context(client, null, null);
        }

        [TearDown]
        public void TearDown() => client.Dispose();

        [Test]
        public void StoredCollectionViewsKeepTheirOwnTypeAsEnumerables()
        {
            using NeoClient local = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/sequence-generated-export.json")), assumeCurrentSchema: false);
            NeoMemberClassWritable kinds = NeoGeneratedTypesSupport.CreateWritableClassValue(local, "12500000-0000-4000-8000-000000000085");
            var numbers = new NeoReadOnlySet<NeoMember>(local, kinds.Get<NeoMemberListWritable>("Numbers"), (_, item) => item, _ => null);
            var children = new NeoReadOnlyList<NeoMember>(local, kinds.Get<NeoMemberListWritable>("Children"), (_, item) => item);
            var moods = new NeoReadOnlyEnumSet<SequenceGenerated.SequenceMood>(local, kinds.Get<NeoMemberEnum>("Moods"),
                SequenceGenerated.SequenceMoodOptions.FromOptionId, SequenceGenerated.SequenceMoodOptions.OptionId);
            var picks = new NeoReadOnlyLookupSet<NeoMember>(local, kinds.Get<NeoMemberLookup>("Picks"), (NeoMember item) => item);
            var byName = new NeoReadOnlyDictionary<NeoMember>(local, kinds.Get<NeoMemberDictionaryWritable>("ByName"), (_, item) => item);
            var child = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "12500000-0000-4000-8000-000000000084" };
            // Each view passes as the parameter's IEnumerable of its element.
            string Kinds<T>(string functionId, IEnumerable<T> view, TypeInfo element, System.Func<T, object?> write) =>
                (string)NeoMemberNSFunction.GetOrCreateStatic(local, functionId)
                    .InvokeStatic(NeoSequences.WriteEnumerable(local, view, element, write))!;
            Assert.That(Kinds("12500000-0000-4000-8000-000000000082", numbers,
                new PrimitiveTypeInfo { type = MemberKind.Int, required = true }, item => item), Is.EqualTo("Set<int>,True,False"));
            Assert.That(Kinds("12500000-0000-4000-8000-000000000083", children,
                new ClassTypeInfo { type = MemberKind.Class, required = true, classId = "12500000-0000-4000-8000-000000000069" }, item => item),
                Is.EqualTo("List<SequenceChild>,True,False"));
            Assert.That(Kinds("12500000-0000-4000-8000-000000000093", moods,
                new EnumTypeInfo { type = MemberKind.Enum, required = true, enumId = "12500000-0000-4000-8000-000000000088" }, SequenceGenerated.SequenceMoodOptions.OptionId),
                Is.EqualTo("Set<SequenceMood>,True,False"));
            Assert.That(Kinds("12500000-0000-4000-8000-000000000094", picks, child, item => item), Is.EqualTo("Set<SequenceChild>,True,False,False"));
            Assert.That(Kinds("12500000-0000-4000-8000-000000000094", byName, child, item => item),
                Is.EqualTo("Dictionary<string, SequenceChild>,False,False,True"));
            // A generic class's stored List<T> takes its owner's type argument.
            Assert.That(NeoMemberNSFunction.GetOrCreateStatic(local, "12500000-0000-4000-8000-000000000099").InvokeStatic(new SequenceGenerated.SequenceKinds(local, kinds, false)),
                Is.EqualTo("True:List<int>,False:List<SequenceChild>,False:SequenceChild"));
            // So does an inner List<T> of its List<List<T>>.
            var grid = new NeoReadOnlyList<NeoMember>(local, kinds.Get<NeoMemberClassWritable>("Ints").GetOrCreateCollection<NeoMemberListWritable>("Grid"), (_, item) => item);
            Assert.That(Kinds("12500000-0000-4000-8000-0000000000a1", grid,
                new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true } }, item => item),
                Is.EqualTo("List<List<int>>,True"));
        }

        // IEnumerable<T> and IEnumerator<T> are covariant: a class element
        // converts to its base, a value element and a base element don't.
        [TestCase("CastCat", "CastPet", true)]
        [TestCase("CastCat", "object", true)]
        [TestCase("CastPet", "CastCat", false)]
        [TestCase("int", "object", false)]
        // Nested: a collection element converts through the same rule, and
        // List, Set and Dictionary stay invariant themselves.
        [TestCase("List<CastCat>", "IEnumerable<CastPet>", true)]
        [TestCase("List<CastCat>", "IEnumerable<CastCat>", true)]
        [TestCase("Set<CastCat>", "IEnumerable<CastPet>", true)]
        [TestCase("Dictionary<CastCat>", "IEnumerable<CastPet>", true)]
        [TestCase("IEnumerable<CastCat>", "IEnumerable<CastPet>", true)]
        [TestCase("List<CastCat>", "List<CastPet>", false)]
        [TestCase("List<CastPet>", "IEnumerable<CastCat>", false)]
        [TestCase("List<int>", "IEnumerable<object>", false)]
        public void ACollectionIsAnEnumerableOfAConvertibleElement(string element, string expected, bool converts)
        {
            object?[] list = new object?[0];
            Record(list, $"List<{element}>");
            Assert.That(Checks(list, Enumerable(Type(expected))), Is.EqualTo(converts));
        }

        // The sequence interfaces are covariant by contract, so a project
        // without their interface rows converts the same.
        [TestCase("CastCat", "CastPet")]
        [TestCase("List<CastCat>", "IEnumerable<CastPet>")]
        [TestCase("IEnumerable<CastCat>", "IEnumerable<CastPet>")]
        public void SequenceVarianceNeedsNoInterfaceRows(string element, string expected)
        {
            ProjectData data = NeoTestExport.Read(File.ReadAllText("Packages/com.ryanbliss.neocompose/Tests/p127-types-parity-export.json"));
            data.interfaces.Remove(NeoSequenceContract.Enumerable);
            data.interfaces.Remove(NeoSequenceContract.Enumerator);
            client.Dispose();
            client = NeoTestSaveStack.ClientFromSchema(data, assumeCurrentSchema: false);
            ctx = new NSGetterEvaluator.Context(client, null, null);
            object?[] list = new object?[0];
            Record(list, $"List<{element}>");

            Assert.That(Checks(list, Enumerable(Type(expected))), Is.True);
        }

        // An inner collection read through an IEnumerable<IEnumerable<CastCat>>
        // keeps its element type, so passing, returning and `is` all accept it.
        [TestCase("List", false)]
        [TestCase("Dictionary", false)]
        [TestCase("List", true)]
        public void AnInnerCollectionReadThroughAnEnumerableKeepsItsElementType(string source, bool explicitCursor)
        {
            object?[] inner = new object?[0];
            object outer = source == "List"
                ? new object?[] { inner }
                : new Dictionary<string, object?> { ["a"] = inner };
            Record(outer, $"{source}<List<CastCat>>");
            TypeInfo expected = Enumerable(Type("CastCat"));
            object? cursor = NSGetterEvaluator.AcquireSequenceCursor(outer, expected, ctx);
            if (explicitCursor)
            {
                var call = new CallFunctionPointer { type = PointerKind.CallFunction, memberId = NeoSequenceContract.GetEnumerator };
                Assert.That(NSGetterEvaluator.TrySequenceCall(call, outer, ctx, out cursor), Is.True);
            }

            Assert.That(NSGetterEvaluator.AdvanceSequenceCursor(cursor, ctx), Is.True);
            object? element = NSGetterEvaluator.ReadSequenceCurrent(cursor, ctx);

            Assert.That(Checks(element, expected), Is.True);
            // Arguments and returns both bind through Normalize; a fitting sequence passes as itself.
            Assert.That(NeoScriptValueMarshaller.Normalize(client, ctx.valueOwnership, element, expected, ctx, "argument"), Is.SameAs(element));
        }

        // List, Set and Dictionary are invariant: a List<CastCat> held as
        // IEnumerable<CastPet> isn't a List<CastPet>, so `is`, `as` and a
        // cast all refuse it, and its own element still matches.
        [TestCase("List", "CastPet", false)]
        [TestCase("List", "CastCat", true)]
        [TestCase("Set", "CastPet", false)]
        [TestCase("Set", "CastCat", true)]
        [TestCase("Dictionary", "CastPet", false)]
        [TestCase("Dictionary", "CastCat", true)]
        public void ACollectionThroughAnEnumerableMatchesOnlyItsOwnElement(string collection, string element, bool matches)
        {
            object value = collection == "Dictionary" ? new Dictionary<string, object?>() : new object?[0];
            Record(value, $"{collection}<CastCat>");

            AssertConverts(value, Type($"{collection}<{element}>"), matches);
        }

        // The recorded type is the whole collection type, so a widened List
        // isn't a Set, a Set isn't a List, and a Dictionary's key counts.
        [TestCase("List<int>", "Set<int>", false)]
        [TestCase("Set<int>", "List<int>", false)]
        [TestCase("Dictionary<CastCat>", "Dictionary<CastWeather, CastCat>", false)]
        [TestCase("Dictionary<CastWeather, CastCat>", "Dictionary<CastCat>", false)]
        [TestCase("List<int>", "List<int>", true)]
        [TestCase("Set<int>", "Set<int>", true)]
        [TestCase("Dictionary<CastWeather, CastCat>", "Dictionary<CastWeather, CastCat>", true)]
        public void AWidenedCollectionMatchesOnlyItsOwnKindAndKey(string held, string target, bool matches)
        {
            object value = held.StartsWith("Dictionary") ? new Dictionary<string, object?>() : new object?[0];
            Record(value, held);

            AssertConverts(value, Type(target), matches);
        }

        // A refused cast names the type the collection holds, not the static one.
        [TestCase("List<CastCat>", "List<CastPet>", "List<CastCat>")]
        [TestCase("Set<CastCat>", "Set<CastPet>", "Set<CastCat>")]
        [TestCase("Dictionary<CastCat>", "Dictionary<CastPet>", "Dictionary<string, CastCat>")]
        public void ARefusedCastNamesTheWidenedCollectionsOwnType(string held, string target, string heldName)
        {
            object value = held.StartsWith("Dictionary") ? new Dictionary<string, object?>() : new object?[0];
            Record(value, held);

            Assert.That(() => Cast(value, Type(target), CastModeKind.Cast), Throws.TypeOf<NeoRuntimeError>()
                .With.Message.EqualTo($"Unable to cast object of type '{heldName}' to type '{target.Replace("Dictionary<", "Dictionary<string, ")}'."));
        }

        // A reference type argument's `?` isn't part of the runtime type; a value type's is.
        [TestCase(MemberKind.String, false, true, true)]
        [TestCase(MemberKind.String, true, false, true)]
        [TestCase(MemberKind.Int, false, true, false)]
        [TestCase(MemberKind.Int, true, false, false)]
        public void AClassTypeArgumentComparesWithReferenceNullabilityErased(MemberKind argument, bool rowRequired, bool checkRequired, bool matches)
        {
            string holder = ClassId("Holder");
            string param = client.ProjectDataForRuntime.classes[holder].genericParams![0].id;
            client.SetWritableValue(NeoValueOwnership.Session, new ObjectMemberValue
            {
                id = "holder-row",
                classId = holder,
                value = new Dictionary<string, string>(),
                genericBindings = TestTypeBindings.Stamp((param, TestTypeBindings.Of(argument, rowRequired))),
            });
            object? row = NSGetterEvaluator.ResolveValueIfId("holder-row", ctx, NeoValueOwnership.Session);
            var check = new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = holder,
                typeArguments = new Dictionary<string, TypeInfo> { [param] = new PrimitiveTypeInfo { type = argument, required = checkRequired } },
            };

            Assert.That(Checks(row, check), Is.EqualTo(matches));
        }

        private void Record(object value, string type) => NSGetterEvaluator.TagSequenceCollection(value, Type(type), ctx);

        private void AssertConverts(object value, TypeInfo target, bool matches)
        {
            Assert.That(Checks(value, target), Is.EqualTo(matches));
            Assert.That(Cast(value, target, CastModeKind.As), matches ? Is.SameAs(value) : Is.Null);
            if (matches)
                Assert.That(Cast(value, target, CastModeKind.Cast), Is.SameAs(value));
            else
                Assert.That(() => Cast(value, target, CastModeKind.Cast), Throws.TypeOf<NeoRuntimeError>().With.Message.StartsWith("Unable to cast"));
        }

        private bool Checks(object? value, TypeInfo checkType)
        {
            var scope = new NeoScriptScope(new Dictionary<string, object?> { ["value"] = value });
            return (bool)NSGetterEvaluator.EvalPointer(new IsCheckPointer
            {
                type = PointerKind.IsCheck,
                pointer = new VariablePointer { type = PointerKind.Variable, variableId = "value" },
                checkType = checkType,
            }, scope, ctx)!;
        }

        private object? Cast(object value, TypeInfo target, string mode)
        {
            var scope = new NeoScriptScope(new Dictionary<string, object?> { ["value"] = value });
            return NSGetterEvaluator.EvalPointer(new CastPointer
            {
                type = PointerKind.Cast,
                pointer = new VariablePointer { type = PointerKind.Variable, variableId = "value" },
                sourceType = Enumerable(Type("CastPet")),
                targetType = target,
                mode = mode,
            }, scope, ctx);
        }

        private TypeInfo Type(string name)
        {
            int open = name.IndexOf('<');
            if (open < 0)
            {
                return name switch
                {
                    "int" => new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                    "object" => new ObjectTypeInfo { type = MemberKind.Object, required = true },
                    _ => new ClassTypeInfo { type = MemberKind.Class, required = true, classId = ClassId(name) },
                };
            }
            string arguments = name.Substring(open + 1, name.Length - open - 2);
            // Dictionary<Key, Value> names an enum key; Dictionary<Value> is string-keyed.
            int comma = arguments.IndexOf(", ");
            string? keyEnumId = comma < 0 ? null : client.ProjectDataForRuntime.enums.Values.Single(enumType => enumType.name == arguments.Substring(0, comma)).id;
            TypeInfo element = Type(keyEnumId is null ? arguments : arguments.Substring(comma + 2));
            return name.Substring(0, open) switch
            {
                "List" => new CollectionTypeInfo { type = MemberKind.List, required = true, entryTypeInfo = element },
                "Set" => new LookupTypeInfo { type = MemberKind.Lookup, required = true, entryTypeInfo = element },
                // A dictionary enumerates its values.
                "Dictionary" => new CollectionTypeInfo { type = MemberKind.Dictionary, required = true, keyEnumId = keyEnumId, entryTypeInfo = element },
                _ => Enumerable(element),
            };
        }

        private static InterfaceTypeInfo Enumerable(TypeInfo element) => new()
        {
            type = MemberKind.Interface,
            required = true,
            interfaceId = NeoSequenceContract.Enumerable,
            typeArguments = new Dictionary<string, TypeInfo> { [NeoSequenceContract.EnumerableElement] = element },
        };

        private string ClassId(string name) => client.ProjectDataForRuntime.classes.Values.Single(schemaClass => schemaClass.name == name).id;
    }
}
