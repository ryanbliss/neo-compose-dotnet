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

        [Test]
        public void DetachedInitializerLambdaCapturesTheObjectBeingConstructed()
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "LK");
            // LX from review item 6: keep X = Apply(() => M(1) + Q()) and
            // its methods, but remove the delegate fields that force row construction.
            owner.schema = owner.schema.Where(pair => schema.members[pair.Value] is not DelegateMember)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            owner.schemaKeyOrder = owner.schemaKeyOrder!.Where(owner.schema.ContainsKey).ToArray();
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            Assert.That(NeoGeneratedTypesSupport.ResolveDetachedClassPlan(client, owner.id), Is.Not.Null);
            var probe = schema.classes.Values.Single(type => type.name == "Probe");
            var function = NeoMemberNSFunction.GetOrCreateStatic(client, probe.schema["DefaultLambdaFallbackInference"]);
            Assert.That(function.InvokeStatic(Array.Empty<object?>()), Is.EqualTo(101));
            Assert.That(function.InvokeStatic(Array.Empty<object?>()), Is.EqualTo(101), "A reused frame must capture the next object.");
        }

        [Test]
        public void InitializerClosureOwnerSurvivesNestedFramesWithoutLeakingIntoReusedFrames()
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)));
            var context = new NSGetterEvaluator.Context(client, null, null);
            var outerOwner = new object();
            var innerOwner = new object();
            context.initializerClosureOwner = outerOwner;
            int outer = context.EnterThis(null);
            Assert.That(context.initializerClosureOwner, Is.Null);
            context.initializerClosureOwner = innerOwner;
            int inner = context.EnterThis(null);
            Assert.That(context.initializerClosureOwner, Is.Null);
            context.ExitFunction(inner);
            Assert.That(context.initializerClosureOwner, Is.SameAs(innerOwner));
            context.ExitFunction(outer);
            Assert.That(context.initializerClosureOwner, Is.SameAs(outerOwner));
            context.ClearDirectInvocation();
            int reused = context.EnterThis(null);
            context.ExitFunction(reused);
            Assert.That(context.initializerClosureOwner, Is.Null);
        }

        public static IEnumerable<TestCaseData> Cases() => JObject.Parse(File.ReadAllText(Root + "p127-overloads-parity-fixture.json"))["returns"]!
            .Children<JProperty>().Select(test => new TestCaseData(test.Name, test.Value).SetName("B4_" + test.Name));

        [Test]
        public void FinalExportLoadsThroughTheFileDataSource()
        {
            string directory = Path.Combine(Path.GetTempPath(), "b4-export-" + Guid.NewGuid().ToString("N"));
            try
            {
                NeoTestExport.Write(directory, File.ReadAllText(Export));
                var schema = NeoJsonProjectDataSource.FromFile(Path.Combine(directory, "project.json")).ReadProjectData();
                Assert.That(schema.classes.Values.Any(type => type.name == "Probe"), Is.True);
            }
            finally
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void MethodTypeArgumentNullabilitySurvivesForwardingAndCachedCalls()
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "NG");
            var outer = (NSFunctionMember)schema.members[owner.schema["G2"]];
            // Let the outer parameter accept null so the forwarded call must
            // enforce its own bare T, using the caller's actual type argument.
            outer.argumentTypes![1].required = false;
            outer.action!.parameters![3].typeInfo.required = false;
            outer.action.instructions = new Instruction[]
            {
                new ReturnInstruction
                {
                    type = InstructionKind.Return,
                    pointer = new CallFunctionPointer
                    {
                        type = PointerKind.CallFunction,
                        callSiteId = "b4-r2-forward-nullability",
                        memberId = owner.schema["G"],
                        receiver = new CallReceiver { kind = CallReceiverKind.Static, memberId = owner.schema["G"] },
                        typeArguments = new TypeInfo[] { new FunctionGenericTypeInfo { type = MemberKind.Generic, required = true, ownerMemberId = outer.id, index = 0 } },
                        args = new Pointer[] { new VariablePointer { type = PointerKind.Variable, variableId = "__arg_1__" } },
                    },
                },
            };
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            var function = NeoMemberNSFunction.GetOrCreateStatic(client, outer.id);
            TypeInfo[] nullable = { new PrimitiveTypeInfo { type = MemberKind.String, required = false } };
            TypeInfo[] required = { new PrimitiveTypeInfo { type = MemberKind.String, required = true } };
            for (int i = 0; i < 2; i++)
            {
                Assert.That(function.InvokeGenericStatic(new object?[] { "s", null }, nullable), Is.EqualTo(1));
                AssertNullArgument(() => function.InvokeGenericStatic(new object?[] { "s", null }, required),
                    "Function 'G' was passed null for non-nullable parameter 'x'.");
            }
        }

        [Test]
        public void BareMethodTypeParameterReturnStillAllowsNull()
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "NG");
            var member = (NSFunctionMember)schema.members[owner.schema["Id"]];
            member.action!.instructions = new Instruction[]
            {
                new ReturnInstruction { type = InstructionKind.Return, pointer = new DefaultPointer { type = PointerKind.Default, typeInfo = member.returnTypeInfo! } },
            };
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            var function = NeoMemberNSFunction.GetOrCreateStatic(client, member.id);
            Assert.That(function.InvokeGenericStatic(new object?[] { "s" }, new TypeInfo[] { new PrimitiveTypeInfo { type = MemberKind.String, required = true } }), Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BareMethodTypeDefaultAcceptsPassedAndOmittedNull(bool omitted)
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "NTF");
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            var function = NeoMemberNSFunction.GetOrCreateStatic(client, owner.schema["F"]);
            Assert.That(function.InvokeGenericStatic(omitted ? Array.Empty<object?>() : new object?[] { null },
                new TypeInfo[] { new PrimitiveTypeInfo { type = MemberKind.String, required = true } }), Is.True);
            var signature = NeoNSFunctionRuntime.ResolveSignature(client, owner.schema["F"]);
            Assert.That(((FunctionArgumentTypeInfo)signature.BoundSignatures!.ArgumentTypes[0]).defaultValue!.isDefault, Is.True);
        }

        [Test]
        public void NullableMethodArgumentsEraseOnlyInRuntimeAndPersistedIdentities()
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "NG");
            string memberId = owner.schema["G"];
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            var context = new NSGetterEvaluator.Context(client, null, null);
            context.BindFunctionTypeArguments(new TypeInfo[] { new PrimitiveTypeInfo { type = MemberKind.String, required = false } });
            var generic = new FunctionGenericTypeInfo { type = MemberKind.Generic, required = true, ownerMemberId = memberId, index = 0 };
            var typeOf = new TypeOfPointer { type = PointerKind.TypeOf, typeInfo = generic };
            var identity = (NeoTypeValue)NSGetterEvaluator.EvaluatePointer(typeOf, new NeoScriptScope(), context)!;
            Assert.That(identity.Binding.Head!.Required, Is.True);
            var typeType = new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = NeoObjectTypeKind.Type };
            var closure = (NeoDelegateValue)NSGetterEvaluator.EvaluatePointer(new DelegateClosurePointer
            {
                type = PointerKind.DelegateClosure,
                site = memberId + ":function:0",
                captures = Array.Empty<Pointer>(),
                action = new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    typeInfo = typeType,
                    parameters = new[]
                    {
                        new Variable { id = "__this__", typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null } },
                        new Variable { id = "__root__", typeInfo = new PrimitiveTypeInfo { type = MemberKind.Null } },
                    },
                    instructions = new Instruction[] { new ReturnInstruction { type = InstructionKind.Return, pointer = typeOf } },
                },
            }, new NeoScriptScope(), context)!;
            Assert.That(closure.lexicalTypeArguments![0].required, Is.False);
            var target = (NeoDelegateValue)NSGetterEvaluator.EvaluatePointer(new MemberTargetPointer
            {
                type = PointerKind.MemberTarget,
                memberId = memberId,
                receiver = new CallReceiver { kind = CallReceiverKind.Static, memberId = memberId },
                typeArguments = new TypeInfo[] { generic },
            }, new NeoScriptScope(), context)!;
            foreach (var value in new[] { closure, target })
            {
                var restored = JsonConvert.DeserializeObject<NeoDelegateValue>(JsonConvert.SerializeObject(value))!;
                Assert.That(restored.typeArguments![0].Head!.Required, Is.True);
            }
            Assert.That(context.functionTypeArguments![0].required, Is.False, "Identity projection must not change the call frame.");
        }

        public static IEnumerable<TestCaseData> Refusals() => JObject.Parse(File.ReadAllText(Root + "p127-overloads-parity-fixture.json"))["refuses"]!
            .Children<JProperty>().Select(test => new TestCaseData(test.Name, test.Value.Value<string>()).SetName("B4_" + test.Name));

        [TestCaseSource(nameof(Refusals))]
        public void RefusesWhatTheSharedFixtureRequires(string name, string expected)
        {
            using var client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(Export)), assumeCurrentSchema: false);
            var probe = client.ProjectDataForRuntime.classes.Values.Single(type => type.name == "Probe");
            AssertNullArgument(() => NeoMemberNSFunction.GetOrCreateStatic(client, probe.schema[name]).InvokeStatic(Array.Empty<object?>()), expected);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NullMemberArgumentHasTheSameErrorThroughHostAndDelegateCalls(bool viaDelegate)
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "NA1");
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            string memberId = owner.schema["S"];
            AssertNullArgument(() => viaDelegate
                ? NSGetterEvaluator.InvokeDelegate(new NeoDelegateValue { memberId = memberId }, new object?[] { null }, new NSGetterEvaluator.Context(client, null, null))
                : NeoMemberNSFunction.GetOrCreateStatic(client, memberId).InvokeStatic(new object?[] { null }),
                "Function 'S' was passed null for non-nullable parameter 's'.");
        }

        [TestCase("NA3", false)]
        [TestCase("NA3", true)]
        [TestCase("NA4", false)]
        [TestCase("NA4", true)]
        public void RowBackedAndGeneratedConstructorsRejectNull(string name, bool generatedCall)
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == name);
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            string constructorId = owner.constructorIds!.Single();
            Func<object?> call;
            if (generatedCall)
            {
                var args = name == "NA3" ? new[] { new NeoDeclaredConstructorArgument("s", null) } : Array.Empty<NeoDeclaredConstructorArgument>();
                call = () => NeoGeneratedTypesSupport.EvaluateDeclaredConstructor(client, owner.id, constructorId, args);
            }
            else
            {
                var fields = Array.Empty<NeoGeneratedTypesSupport.RuntimeConstructorField>();
                var resolved = NeoGeneratedTypesSupport.ResolveDeclaredConstructor(client,
                    new ClassTypeInfo { type = MemberKind.Class, required = true, classId = owner.id },
                    constructorId, name == "NA3" ? new[] { "s" } : Array.Empty<string>(), fields);
                call = () => NeoGeneratedTypesSupport.ConstructDeclaredClassValueData(resolved,
                    name == "NA3" ? new object?[] { null } : Array.Empty<object?>(), fields,
                    new NSGetterEvaluator.Context(client, null, null));
            }
            AssertNullArgument(call, "Constructor 'NA3' was passed null for non-nullable parameter 's'.");
        }

        [TestCase(MemberKind.String)]
        [TestCase(MemberKind.Object)]
        [TestCase(MemberKind.Int)]
        public void NullableParametersStillAcceptNull(MemberKind kind)
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "NA1");
            var member = (NSFunctionMember)schema.members[owner.schema["S"]];
            member.argumentTypes![0].type = kind;
            member.argumentTypes[0].required = false;
            member.action!.parameters![2].typeInfo = new PrimitiveTypeInfo { type = kind, required = false };
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            Assert.That(NeoMemberNSFunction.GetOrCreateStatic(client, member.id).InvokeStatic(new object?[] { null }), Is.EqualTo(5));
        }

        [Test]
        public void NullValueParameterKeepsItsExistingError()
        {
            var schema = NeoTestExport.Read(File.ReadAllText(Export));
            var owner = schema.classes.Values.Single(type => type.name == "NA1");
            var member = (NSFunctionMember)schema.members[owner.schema["S"]];
            member.argumentTypes![0].type = MemberKind.Int;
            member.action!.parameters![2].typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true };
            using var client = NeoTestSaveStack.ClientFromSchema(schema, assumeCurrentSchema: false);
            var error = Assert.Catch<NSGetterRuntimeError>(() => NeoMemberNSFunction.GetOrCreateStatic(client, member.id).InvokeStatic(new object?[] { null }))!;
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoInvalidOperationError));
            Assert.That(error.Message, Is.EqualTo($"NSFunction 'S' ({member.id}) argument 0 's' is incompatible with declared Int; compiled call IR or caller is stale/corrupt: Required argument 0 's' of NSFunction 'S' cannot be null."));
        }

        private static void AssertNullArgument(Func<object?> call, string expected)
        {
            var error = Assert.Catch<NSGetterRuntimeError>(() => call())!;
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoNullReferenceError));
            Assert.That(error.Payload.Message, Is.EqualTo(expected));
            Assert.That(error.Message, Is.EqualTo(expected));
        }

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
