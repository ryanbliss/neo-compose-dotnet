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

        // IEnumerable<T> and IEnumerator<T> are covariant: a class element
        // converts to its base, a value element and a base element don't.
        [TestCase("CastCat", "CastPet", true)]
        [TestCase("CastCat", "object", true)]
        [TestCase("CastPet", "CastCat", false)]
        [TestCase("int", "object", false)]
        public void ACollectionIsAnEnumerableOfAConvertibleElement(string element, string expected, bool converts)
        {
            object?[] list = new object?[0];
            client.SequenceCollectionElements.Add(NSGetterEvaluator.SequenceCollectionIdentity(list), Type(element));
            var enumerable = new InterfaceTypeInfo
            {
                type = MemberKind.Interface,
                required = true,
                interfaceId = NeoSequenceContract.Enumerable,
                typeArguments = new Dictionary<string, TypeInfo> { [NeoSequenceContract.EnumerableElement] = Type(expected) },
            };

            Assert.That(Checks(list, enumerable), Is.EqualTo(converts));
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

        private TypeInfo Type(string name) => name switch
        {
            "int" => new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
            "object" => new ObjectTypeInfo { type = MemberKind.Object, required = true },
            _ => new ClassTypeInfo { type = MemberKind.Class, required = true, classId = ClassId(name) },
        };

        private string ClassId(string name) => client.ProjectDataForRuntime.classes.Values.Single(schemaClass => schemaClass.name == name).id;
    }
}
