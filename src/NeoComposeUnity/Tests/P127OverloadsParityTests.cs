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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>The SDK consumer of the B4 source/export parity pair.</summary>
    public class P127OverloadsParityTests
    {
        private const string Root = "Packages/com.ryanbliss.neocompose/Tests/";
        private const string Export = Root + "p127-overloads-parity-export.json";

        [TestCase("X", 1)]
        [TestCase("Y", 2)]
        [TestCase("Z", 21)]
        public void StaticReadOnlyStorageRejectsHostAndScriptWrites(string name, int expected)
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "SR");
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            var binding = NeoGeneratedTypesSupport.StaticBinding(client, owner.schema[name], NeoValueOwnership.Session);
            AssertReadOnly(client, binding, new StaticMemberPointer { memberId = binding.MemberId }, expected);
        }

        [Test]
        public void GenericReadOnlyStorageInitializesAndGuardsEachConstruction()
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "GS");
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            var ids = new HashSet<string>();
            foreach (var (argument, expected) in new[] { ("Pm", 10), ("Pn", 30) })
            {
                var construction = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: owner.id),
                    new Dictionary<string, TypeBinding>
                    {
                        [owner.genericParams![0].id] = TypeBinding.Closed(new TypeHead(MemberKind.Class, true,
                            classId: schema.classes.Values.Single(type => type.name == argument).id)),
                    });
                var binding = client.ConstructedStaticBinding(owner.schema["P"], construction);
                AssertReadOnly(client, binding, new StaticMemberPointer
                {
                    memberId = binding.MemberId,
                    staticConstruction = NeoTypeBindings.ToTypeInfo(construction),
                }, expected);
                Assert.That(ids.Add(binding.ValueId!), Is.True, "Each construction owns its initialized row.");
            }
        }

        [Test]
        public void InterfaceGetOnlyStorageLoadsWithoutClassPlacement()
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Root + "p127-interfaces-parity-export.json"));
            var owner = schema.interfaces.Values.Single(type => type.name == "IPropS");
            var member = schema.members[owner.members["P"].implementationMemberId!];
            member.DeclaredMutability = NeoMemberMutabilityKind.ReadOnly;
            owner.members["P"].Accessors = NeoPropertyAccessorsKind.Get;
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            foreach (var kind in new[] { MemberKind.Int, MemberKind.String })
            {
                var construction = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: owner.id),
                    new Dictionary<string, TypeBinding>
                    {
                        [owner.genericParams![0].id] = TypeBinding.Closed(new TypeHead(kind, true)),
                    });
                var binding = client.ConstructedStaticBinding(member.id, construction);
                AssertReadOnly(client, binding, new StaticMemberPointer
                {
                    memberId = member.id,
                    staticConstruction = NeoTypeBindings.ToTypeInfo(construction),
                }, 5);
            }
        }

        [Test]
        public void InitializerCannotWriteAnotherReadOnlySlot()
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "GS");
            var construction = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: owner.id),
                new Dictionary<string, TypeBinding>
                {
                    [owner.genericParams![0].id] = TypeBinding.Closed(new TypeHead(MemberKind.Class, true,
                        classId: schema.classes.Values.Single(type => type.name == "Pm").id)),
                });
            var member = schema.members[owner.schema["P"]];
            var initializer = schema.values[member.valueId!].init!.compiled!;
            var assignment = WriteReadOnly(new StaticMemberPointer
            {
                memberId = owner.schema["Q"],
                staticConstruction = NeoTypeBindings.ToTypeInfo(construction),
            }).instructions[0];
            initializer.instructions = new[] { assignment }.Concat(initializer.instructions).ToArray();
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            var error = Assert.Catch(() => client.ConstructedStaticBinding(member.id, construction));
            StringAssert.Contains("initializer", error!.ToString());
            StringAssert.Contains("read-only", error.ToString());
        }

        private static void AssertReadOnly(NeoClient client, NeoStaticBinding binding, StaticMemberPointer pointer, int expected)
        {
            pointer.type = PointerKind.StaticMember;
            Assert.That(NeoGeneratedTypesSupport.ReadInt(binding.GetRequiredNode<NeoMemberInt>()), Is.EqualTo(expected));
            var writes = new TestDelegate[]
            {
                () => binding.SetValue(NeoGeneratedTypesSupport.Value(99)),
                () => binding.Clear(),
                () => binding.RestoreAuthored(),
                () => binding.BindValue(null),
                () => binding.GetOrCreateWritableNode<NeoMemberIntWritable>(99),
                () => binding.GetRequiredNode<NeoMemberIntWritable>().Set(99),
            };
            foreach (var write in writes)
                StringAssert.Contains("read-only", Assert.Throws<InvalidOperationException>(write)!.Message);
            // Even a stale or forged writable target must respect the stored member's mutability.
            var error = Assert.Catch(() => NSGetterEvaluator.Evaluate(WriteReadOnly(pointer),
                new NSGetterEvaluator.Context(client, null, null)));
            StringAssert.Contains("read-only", error!.ToString());
            Assert.That(NeoGeneratedTypesSupport.ReadInt(binding.GetRequiredNode<NeoMemberInt>()), Is.EqualTo(expected));
        }

        private static FunctionWithReturnType WriteReadOnly(StaticMemberPointer pointer)
        {
            pointer.type = PointerKind.StaticMember;
            var type = new PrimitiveTypeInfo { type = MemberKind.Int, required = true };
            return new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = Array.Empty<Variable>(),
                typeInfo = type,
                instructions = new Instruction[]
                {
                    new AssignInstruction
                    {
                        target = new WriteTarget { pointer = pointer, typeInfo = type, writability = WritabilityKind.Runtime },
                        operatorValue = "=",
                        pointer = new ValuePointer { value = new Value { typeInfo = type, value = new JValue(99) } },
                    },
                    new ReturnInstruction { pointer = pointer },
                },
            };
        }

        public static IEnumerable<TestCaseData> Cases() => JObject.Parse(File.ReadAllText(Root + "p127-overloads-parity-fixture.json"))["returns"]!
            .Children<JProperty>().Select(test => new TestCaseData(test.Name, test.Value).SetName("B4_" + test.Name));

        [TestCaseSource(nameof(Cases))]
        public void ReturnsWhatTheSharedFixtureRequires(string name, JToken expected)
        {
            Assert.That(File.Exists(Export), Is.True, "Vendor the validated B4 export before running parity.");
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            var probe = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == "Probe");
            var function = (NSFunctionMember)client.ProjectDataForRuntime.members[probe.schema[name]];
            object? value = NeoMemberNSFunction.GetOrCreateStatic(client, function.id).InvokeStatic(Array.Empty<object?>());
            JToken actual = value is null ? JValue.CreateNull() : JToken.FromObject(value);
            if (expected.Type is JTokenType.Integer or JTokenType.Float)
                Assert.That(actual.Value<double>(), Is.EqualTo(expected.Value<double>()), name);
            else
                Assert.That(JToken.DeepEquals(actual, expected), Is.True, $"{name}: {actual}");
        }

    }
}
