// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §1.2, §2 and §3: boxing into <c>object</c>, casts, <c>as</c>,
    /// conversions, <c>default</c>, <c>typeof</c>, the <c>object</c> members
    /// and the <c>Type</c> members, over hand-built B2 IR.
    /// </summary>
    public class P127TypesObjectTests
    {
        private const string Planet = "planet";
        private const string Shape = "shape-class";
        private const string Circle = "circle-class";
        private const string Thing = "thing-interface";
        private const string Round = "round-interface";

        [TestCase(MemberKind.List, "T", 0)]
        [TestCase(MemberKind.Set, "T", 0)]
        [TestCase(MemberKind.Dictionary, "TValue", 1)]
        public void BuiltInParametersRetainTheirOwnerThroughReflection(MemberKind kind, string name, int position)
        {
            using NeoClient client = BuildClient();
            client.ProjectDataForRuntime.interfaces[NeoSequenceContract.Enumerable] = new Interface
            {
                id = NeoSequenceContract.Enumerable,
                name = "IEnumerable",
                members = new Dictionary<string, InterfaceMember>(),
                genericParams = new List<GenericParamDeclaration> { new GenericParamDeclaration { id = NeoSequenceContract.EnumerableElement, name = "T" } },
            };
            client.InvalidateSchemaResolutionCaches();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var scope = new NeoScriptScope();
            object? Eval(JObject pointer) => NSGetterEvaluator.EvalPointer(pointer.ToObject<Pointer>()!, scope, ctx);
            var interfaces = (object?[])Eval(TypeMember("GetInterfaces", TypeOfDefinition(kind)))!;
            scope.SetLocal("interface", interfaces[0]);
            var parameters = (object?[])Eval(TypeMember("GenericTypeArguments", Local("interface")))!;
            scope.SetLocal("parameter", parameters[0]);
            var expected = NeoTypeValue.Of(TypeBinding.Open(new TypeDefinition(kind), position.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Assert.That(parameters[0], Is.SameAs(expected));
            Assert.That(Eval(TypeMember("Name", Local("parameter"))), Is.EqualTo(name));
            Assert.That(Eval(TypeMember("FullName", Local("parameter"))), Is.EqualTo(name));
            Assert.That(Eval(TypeMember("Name", TypeMember("BaseType", Local("parameter")))), Is.EqualTo("object"));
            Assert.That((object?[])Eval(TypeMember("GetInterfaces", Local("parameter")))!, Is.Empty);
            Assert.That(Eval(TypeMember("GenericParameterPosition", Local("parameter"))), Is.EqualTo((double)position));
            Assert.That(expected, Is.Not.SameAs(NeoTypeValue.Of(TypeBinding.Open(new TypeDefinition(MemberKind.Reference), "0"))));
            // A nested List<Collection<T>> must preserve the original owner's T.
            var inner = ((NeoTypeValue)interfaces[0]!).Binding;
            scope.SetLocal("nested", NeoTypeValue.Of(TypeBinding.Closed(new TypeHead(MemberKind.List, true), new Dictionary<string, TypeBinding> { ["0"] = inner })));
            var nested = (object?[])Eval(TypeMember("GenericTypeArguments", Local("nested")))!;
            scope.SetLocal("inner", nested[0]);
            Assert.That(((object?[])Eval(TypeMember("GenericTypeArguments", Local("inner")))!)[0], Is.SameAs(expected));
        }

        [TestCase("IsAssignableFrom")]
        [TestCase("IsInstanceOfType")]
        [TestCase("IsSubclassOf")]
        public void TypeMethodsEvaluateArgumentsBeforeOrdinaryNullFailure(string member)
        {
            using NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var scope = new NeoScriptScope(new Dictionary<string, object?> { ["receiverCalls"] = 0d, ["argumentCalls"] = 0d });
            object? Eval(JObject pointer) => NSGetterEvaluator.EvalPointer(pointer.ToObject<Pointer>()!, scope, ctx);
            JObject receiver = CountEvaluation("receiverCalls", Null());
            JObject argument = CountEvaluation("argumentCalls", TypeOf(IntType()));
            Assert.That(Assert.Throws<NSGetterRuntimeError>(() => Eval(TypeMember(member, receiver, argument)))!.Payload.ClassId,
                Is.EqualTo(NeoErrorContract.NeoNullReferenceError));
            Assert.That(Eval(Local("receiverCalls")), Is.EqualTo(1d));
            Assert.That(Eval(Local("argumentCalls")), Is.EqualTo(1d));
            JObject throwing = TypeMember("GetGenericTypeDefinition", TypeOf(IntType()));
            Assert.That(Assert.Throws<NeoRuntimeError>(() => Eval(TypeMember(member, Null(), throwing)))!.Message,
                Is.EqualTo("This operation is only valid on generic types."));
            JObject optional = TypeMember(member, receiver, throwing);
            optional["optional"] = true;
            Assert.That(Eval(optional), Is.Null);
            Assert.That(Eval(Local("receiverCalls")), Is.EqualTo(2d));
            Assert.That(Eval(Local("argumentCalls")), Is.EqualTo(1d));
            Assert.Throws<NeoRuntimeError>(() => Eval(TypeMember(member, throwing, argument)));
            Assert.That(Eval(Local("argumentCalls")), Is.EqualTo(1d));
            Eval(TypeMember(member, CountEvaluation("receiverCalls", TypeOf(ObjectType())), argument));
            Assert.That(Eval(Local("receiverCalls")), Is.EqualTo(3d));
            Assert.That(Eval(Local("argumentCalls")), Is.EqualTo(2d));
        }

        [Test]
        public void GenericMethodTypeOfClosesNestedBuiltInArguments()
        {
            using NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            ctx.BindFunctionTypeArguments(new[] { IntType().ToObject<TypeInfo>()! });
            var type = new TypeOfPointer
            {
                type = PointerKind.TypeOf,
                typeInfo = new CollectionTypeInfo
                {
                    type = MemberKind.List,
                    required = true,
                    entryTypeInfo = new CollectionTypeInfo
                    {
                        type = MemberKind.List,
                        required = true,
                        entryTypeInfo = new FunctionGenericTypeInfo { type = MemberKind.Generic, required = true, ownerMemberId = "method", index = 0 },
                    },
                },
            };
            var actual = (NeoTypeValue)NSGetterEvaluator.EvalPointer(type, new NeoScriptScope(), ctx)!;
            Assert.That(actual.Binding.At(0).At(0), Is.EqualTo(TypeBinding.Closed(new TypeHead(MemberKind.Int, true))));
            Assert.That(NeoTypeNames.Of(actual.Binding, client.ProjectDataForRuntime), Is.EqualTo("List<List<int>>"));
        }

        [TestCase(MemberKind.Unknown, 1, 0, "T")]
        [TestCase(MemberKind.Dictionary, 2, 0, "TKey")]
        [TestCase(MemberKind.Reference, 1, 0, "T")]
        [TestCase(MemberKind.Tuple, 2, 1, "T2")]
        [TestCase(MemberKind.Tuple, 8, 7, "TRest")]
        [TestCase(MemberKind.NSDelegate, 3, 0, "TResult")]
        [TestCase(MemberKind.NSDelegate, 3, 2, "T2")]
        [TestCase(MemberKind.NSAction, 2, 1, "T2")]
        public void BuiltInParameterNamesUseDefinitionPositions(MemberKind kind, int arity, int position, string expected)
        {
            using NeoClient client = BuildClient();
            var owner = new TypeDefinition(kind, arity: kind is MemberKind.Tuple or MemberKind.NSDelegate or MemberKind.NSAction ? arity : null, nullable: kind == MemberKind.Unknown);
            var parameter = TypeBinding.Open(owner, position.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.That(NeoTypeNames.Of(parameter, client.ProjectDataForRuntime), Is.EqualTo(expected));
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var scope = new NeoScriptScope(new Dictionary<string, object?> { ["parameter"] = NeoTypeValue.Of(parameter) });
            object? Eval(string name) => NSGetterEvaluator.EvalPointer(TypeMember(name, Local("parameter")).ToObject<Pointer>()!, scope, ctx);
            bool valueConstraint = owner.Nullable || kind == MemberKind.Tuple && arity == 8 && position == 7;
            Assert.That(NeoTypeNames.Of(((NeoTypeValue)Eval("BaseType")!).Binding, client.ProjectDataForRuntime),
                Is.EqualTo(valueConstraint ? "ValueType" : "object"));
            Assert.That(Eval("GetInterfaces"), Is.Empty);
            Assert.That(Eval("GenericParameterPosition"), Is.EqualTo((double)position));
            Assert.That(Eval("IsClass"), Is.EqualTo(!valueConstraint));
            Assert.That(Eval("IsValueType"), Is.EqualTo(valueConstraint));
            Assert.That(Eval("IsSealed"), Is.False);
        }

        [TestCase("method")]
        [TestCase("property")]
        [TestCase("indexer")]
        [TestCase("extension")]
        [TestCase("static")]
        public void NullCallFormsPreserveReceiverAndArgumentOrder(string form)
        {
            using NeoClient client = BuildClient();
            TypeInfo integer = IntType().ToObject<TypeInfo>()!;
            var action = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[]
                {
                    new Variable { id = "__this__", typeInfo = ClassType(Circle).ToObject<TypeInfo>()! },
                    new Variable { id = "__root__", typeInfo = ClassType("root-class").ToObject<TypeInfo>()! },
                    new Variable { id = "__arg_0__", typeInfo = integer },
                    new Variable { id = "__arg_1__", typeInfo = integer },
                },
                typeInfo = integer,
                instructions = new[] { new JObject { ["type"] = "return", ["pointer"] = Local("__arg_1__") }.ToObject<Instruction>()! },
            };
            client.ProjectDataForRuntime.members["call-method"] = new NSFunctionMember
            {
                id = "call-method",
                name = "Method",
                kind = MemberKind.NSFunction,
                returnTypeInfo = integer,
                action = action,
                argumentTypes = new[]
                {
                    new FunctionArgumentTypeInfo { name = "a", type = MemberKind.Int, required = true },
                    new FunctionArgumentTypeInfo { name = "b", type = MemberKind.Int, required = true },
                },
                extension = form == "extension" ? new ExtensionMemberDescriptor { receiverType = Nullable(ClassType(Circle)).ToObject<TypeInfo>()! } : null,
            };
            var getter = new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new[] { action.parameters[0], action.parameters[1] },
                typeInfo = integer,
                instructions = new[] { new JObject { ["type"] = "return", ["pointer"] = Int(7) }.ToObject<Instruction>()! },
            };
            client.ProjectDataForRuntime.members["call-property"] = new NSPropertyMember
            {
                id = "call-property",
                name = "Property",
                kind = MemberKind.NSProperty,
                returnTypeInfo = integer,
                getter = getter,
            };
            client.ProjectDataForRuntime.classes[Circle].schema["Method"] = "call-method";
            client.ProjectDataForRuntime.classes[Circle].schema["Property"] = "call-property";
            client.InvalidateSchemaResolutionCaches();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var scope = new NeoScriptScope(new Dictionary<string, object?> { ["receiverCalls"] = 0d, ["argumentCalls"] = 0d });
            object? Eval(JObject pointer) => NSGetterEvaluator.EvalPointer(pointer.ToObject<Pointer>()!, scope, ctx);
            JObject Make(JObject receiver, JObject argument) => form switch
            {
                "property" => New("callGetter", new JObject { ["memberId"] = "call-property", ["receiver"] = new JObject { ["kind"] = "instance", ["pointer"] = receiver } }),
                "indexer" => New("keyOf", new JObject { ["keyOf"] = new JObject { ["pointer"] = receiver, ["key"] = argument } }),
                "static" => ObjectStatic("ReferenceEquals", receiver, argument),
                _ => New("callFunction", new JObject
                {
                    ["memberId"] = "call-method",
                    ["callSiteId"] = "fix2-call",
                    ["receiver"] = new JObject { ["kind"] = "instance", ["pointer"] = receiver },
                    ["args"] = new JArray(argument, CountEvaluation("argumentCalls", Int(2))),
                }),
            };
            JObject receiver = CountEvaluation("receiverCalls", Null());
            JObject call = Make(receiver, CountEvaluation("argumentCalls", Int(1)));
            if (form == "extension")
                Assert.That(Eval(call), Is.EqualTo(2d));
            else if (form == "static")
                Assert.That(Eval(call), Is.False);
            else
                Assert.That(Assert.Throws<NSGetterRuntimeError>(() => Eval(call))!.Payload.ClassId,
                    Is.EqualTo(NeoErrorContract.NeoNullReferenceError));
            Assert.That(Eval(Local("receiverCalls")), Is.EqualTo(1d));
            double expectedArguments = form == "property" ? 0d : form is "method" or "extension" ? 2d : 1d;
            Assert.That(Eval(Local("argumentCalls")), Is.EqualTo(expectedArguments));
            if (form != "property")
            {
                JObject failure = TypeMember("GetGenericTypeDefinition", TypeOf(IntType()));
                Assert.That(Assert.Catch(() => Eval(Make(receiver, failure)))!.Message,
                    Is.EqualTo("This operation is only valid on generic types."));
            }
            Assert.That(Eval(Local("argumentCalls")), Is.EqualTo(expectedArguments));
            Assert.That(Assert.Catch(() => Eval(Make(TypeMember("GetGenericTypeDefinition", TypeOf(IntType())), CountEvaluation("argumentCalls", Int(1)))))!.Message,
                Is.EqualTo("This operation is only valid on generic types."));
            Assert.That(Eval(Local("argumentCalls")), Is.EqualTo(expectedArguments));
            if (form != "static")
            {
                JObject optional = Make(receiver, CountEvaluation("argumentCalls", Int(1)));
                optional["optional"] = true;
                Assert.That(Eval(optional), Is.Null);
                Assert.That(Eval(Local("argumentCalls")), Is.EqualTo(expectedArguments));
            }
        }

        private static JObject CountEvaluation(string variable, JObject result) => New("increment", new JObject
        {
            ["assign"] = new JObject
            {
                ["type"] = "assign",
                ["operator"] = "=",
                ["bindings"] = new JArray(new JObject
                {
                    ["id"] = variable + "_result",
                    ["typeInfo"] = result["type"]!.Value<string>() == "typeOf" ? ObjectType(NeoObjectTypeKind.Type) : result["value"]!["typeInfo"]!.DeepClone(),
                    ["pointer"] = result,
                }),
                ["target"] = new JObject { ["pointer"] = Local(variable), ["typeInfo"] = IntType(), ["writability"] = "local" },
                ["pointer"] = New("operation", new JObject { ["operation"] = new JObject { ["type"] = "arithmetic", ["arithmetic"] = new JObject { ["type"] = "+", ["numeric"] = "int", ["pointers"] = new JArray(Local(variable), Int(1)) } } }),
            },
            ["result"] = Local(variable + "_result"),
        });

        [TestCase(false)]
        [TestCase(true)]
        public void ValueBoxInterfaceChecksDistinguishAuthoredFromProtected(bool system)
        {
            using NeoClient client = BuildClient();
            // Use JSON so the test also verifies preservation of export metadata.
            client.ProjectDataForRuntime.interfaces[Thing] = new JObject
            {
                ["id"] = Thing,
                ["name"] = "IThing",
                ["members"] = new JObject(),
                ["system"] = system ? new JObject { ["key"] = "IThing" } : null,
            }.ToObject<Interface>()!;
            client.InvalidateSchemaResolutionCaches();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            object? Eval(JObject pointer) => NSGetterEvaluator.EvalPointer(pointer.ToObject<Pointer>()!, new NeoScriptScope(), ctx);
            JObject boxed = Box(Int(1), IntType());
            if (system)
            {
                foreach (JObject pointer in new[] { IsCheck(boxed, InterfaceType(Thing)), Cast(boxed, InterfaceType(Thing), "as"), Cast(boxed, InterfaceType(Thing)), TypeMember("IsInstanceOfType", TypeOf(InterfaceType(Thing)), boxed) })
                {
                    var error = Assert.Throws<NSGetterRuntimeError>(() => Eval(pointer))!;
                    Assert.That(error.Message, Is.EqualTo("Interface conformance for boxed int isn't supported yet (P127 B5)."));
                }
            }
            else
            {
                Assert.That(Eval(IsCheck(boxed, InterfaceType(Thing))), Is.False);
                Assert.That(Eval(TypeMember("IsInstanceOfType", TypeOf(InterfaceType(Thing)), boxed)), Is.False);
                Assert.That(Eval(Cast(boxed, InterfaceType(Thing), "as")), Is.Null);
                Assert.That(Assert.Throws<NeoRuntimeError>(() => Eval(Cast(boxed, InterfaceType(Thing))))!.Message,
                    Is.EqualTo("Unable to cast object of type 'int' to type 'IThing'."));
            }
        }

        [Test]
        public void OpenParameterReflectsItsClassAndInterfaceConstraints()
        {
            using NeoClient client = BuildClient();
            // class Derived<T extends Shape & IThing> : Base<T>
            var owner = new TypeDefinition(MemberKind.Class, classId: Circle);
            client.ProjectDataForRuntime.classes[Circle].genericParams = new List<GenericParamDeclaration>
            {
                new GenericParamDeclaration { id = "T", name = "T", constraints = new List<GenericParamConstraint>
                {
                    new GenericParamConstraint { kind = NeoGenericParamConstraintKind.Class, classId = Shape },
                    new GenericParamConstraint { kind = NeoGenericParamConstraintKind.Interface, interfaceId = Thing },
                } },
            };
            client.ProjectDataForRuntime.classes["open-base"] = new NeoSchemaClass
            {
                id = "open-base",
                name = "Base",
                schema = new Dictionary<string, string>(),
                genericParams = new List<GenericParamDeclaration> { new GenericParamDeclaration { id = "U", name = "U" } },
            };
            client.ProjectDataForRuntime.classes[Circle].extendsClassId = "open-base";
            client.ProjectDataForRuntime.classes[Circle].extendsGenericBindings = new Dictionary<string, TypeBinding> { ["U"] = TypeBinding.Open(owner, "T") };
            client.InvalidateSchemaResolutionCaches();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var scope = new NeoScriptScope(new Dictionary<string, object?> { ["definition"] = NeoTypeValue.Of(TypeBinding.OfDefinition(owner)) });
            var arguments = (object?[])NSGetterEvaluator.EvalPointer(TypeMember("GenericTypeArguments", TypeMember("BaseType", Local("definition"))).ToObject<Pointer>()!, scope, ctx)!;
            scope.SetLocal("T", arguments[0]);
            object? Eval(string name) => NSGetterEvaluator.EvalPointer(TypeMember(name, Local("T")).ToObject<Pointer>()!, scope, ctx);
            Assert.That(((NeoTypeValue)Eval("BaseType")!).Binding.Head!.ClassId, Is.EqualTo(Shape));
            Assert.That((object?[])Eval("GetInterfaces")!, Has.Length.EqualTo(2));
        }

        [Test]
        public void OpenEnumConstraintRefusesTheB3FormPrecisely()
        {
            using NeoClient client = BuildClient();
            client.ProjectDataForRuntime.classes[Circle].genericParams = new List<GenericParamDeclaration>
            {
                new GenericParamDeclaration { id = "T", name = "T", constraints = new List<GenericParamConstraint>
                {
                    new GenericParamConstraint { kind = NeoGenericParamConstraintKind.AnyEnum },
                } },
            };
            client.InvalidateSchemaResolutionCaches();
            var scope = new NeoScriptScope(new Dictionary<string, object?> { ["T"] = NeoTypeValue.Of(TypeBinding.Open(new TypeDefinition(MemberKind.Class, classId: Circle), "T")) });
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            foreach (string member in new[] { "BaseType", "GetInterfaces" })
            {
                var error = Assert.Throws<NSGetterRuntimeError>(() => NSGetterEvaluator.EvalPointer(TypeMember(member, Local("T")).ToObject<Pointer>()!, scope, ctx))!;
                Assert.That(error.Message, Is.EqualTo("Reflection on the AnyEnum constraint of 'T' isn't supported yet (P127 B3)."));
            }
        }

        [TestCase(32)]
        [TestCase(64)]
        public void InterfaceClosureIsCachedAndVisitsEachEdgeOnce(int width)
        {
            using NeoClient client = BuildClient();
            var data = client.ProjectDataForRuntime;
            data.classes[Circle].implementsInterfaces = new List<InterfaceConstruction>();
            for (int index = 0; index < width; index++)
            {
                string id = "wide-" + index;
                data.interfaces[id] = new Interface
                {
                    id = id,
                    name = id,
                    members = new Dictionary<string, InterfaceMember>(),
                    extendsInterfaces = new List<InterfaceConstruction> { new InterfaceConstruction { interfaceId = Thing } }
                };
                data.classes[Circle].implementsInterfaces.Add(new InterfaceConstruction { interfaceId = id });
            }
            client.InvalidateSchemaResolutionCaches();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var method = typeof(NSGetterEvaluator).GetMethod("InterfacesOf", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            var binding = TypeBinding.Closed(new TypeHead(MemberKind.Class, true, classId: Circle));
            object first = method.Invoke(null, new object[] { binding, ctx })!;
            Assert.That(method.Invoke(null, new object[] { binding, ctx }), Is.SameAs(first));
            var counter = typeof(NSGetterEvaluator).GetMethod("InterfaceClosureVisits", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            Assert.That(counter, Is.Not.Null);
            Assert.That(counter.Invoke(null, new object[] { client }), Is.EqualTo(width * 2 + 2));
            for (int index = 0; index < 10; index++)
                Assert.That(NSGetterEvaluator.EvalPointer(TypeMember("IsAssignableFrom", TypeOf(InterfaceType(Thing)), TypeOf(ClassType(Circle))).ToObject<Pointer>()!, new NeoScriptScope(), ctx), Is.True);
            Assert.That(counter.Invoke(null, new object[] { client }), Is.EqualTo(width * 2 + 2));
            client.InvalidateSchemaResolutionCaches();
            Assert.That(method.Invoke(null, new object[] { binding, ctx }), Is.Not.SameAs(first));
        }

        [Test]
        public void DirectToStringPreservesNullWhileFormattingUsesEmptyText()
        {
            using NeoClient client = BuildClient();
            var action = new JObject
            {
                ["parameters"] = new JArray(),
                ["typeInfo"] = StringType(nullable: true),
                ["instructions"] = new JArray(new JObject { ["type"] = "return", ["pointer"] = Null() })
            }.ToObject<FunctionWithReturnType>()!;
            action.compilerRevision = FunctionWithReturnType.CurrentCompilerRevision;
            action.parameters = new[]
            {
                new Variable { id = "__this__", typeInfo = ClassType(Circle).ToObject<TypeInfo>()! },
                new Variable { id = "__root__", typeInfo = ClassType("root-class").ToObject<TypeInfo>()! },
            };
            client.ProjectDataForRuntime.members["null-text"] = new NSFunctionMember
            {
                id = "null-text",
                name = "ToString",
                kind = MemberKind.NSFunction,
                argumentTypes = System.Array.Empty<FunctionArgumentTypeInfo>(),
                returnTypeInfo = StringType(nullable: true).ToObject<TypeInfo>()!,
                action = action,
            };
            client.ProjectDataForRuntime.classes[Circle].schema["ToString"] = "null-text";
            client.InvalidateSchemaResolutionCaches();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            var row = new ObjectMemberValue { id = "receiver", classId = Circle, value = new Dictionary<string, string>() };
            object? value = NSGetterEvaluator.UnwrapRow(row, ctx, NeoValueOwnership.Session);
            var scope = new NeoScriptScope(new Dictionary<string, object?> { ["o"] = value });
            object? Eval(JObject pointer) => NSGetterEvaluator.EvalPointer(pointer.ToObject<Pointer>()!, scope, ctx);
            Assert.That(Eval(ObjectMember("ToString", Local("o"), ObjectType())), Is.Null);
            foreach (string mode in new[] { "interpolation", "concatenation" })
                Assert.That(Eval(New("format", new JObject { ["pointer"] = Local("o"), ["sourceType"] = ObjectType(), ["mode"] = mode })), Is.EqualTo(""));
            JObject tupleType = TupleType(ObjectType(), IntType());
            Assert.That(Eval(ObjectMember("ToString", Tuple(tupleType, Local("o"), Int(1)), tupleType)), Is.EqualTo("(, 1)"));
        }

        [TestCase(MemberKind.Vector2)]
        [TestCase(MemberKind.Vector3)]
        [TestCase(MemberKind.Vector2Int)]
        [TestCase(MemberKind.Vector3Int)]
        public void VectorObjectEqualityMatchesUnity(MemberKind kind)
        {
            bool integer = kind is MemberKind.Vector2Int or MemberKind.Vector3Int;
            JObject type = Primitive(kind);
            JObject left = Value(type, new JObject { ["x"] = integer ? new JValue(16777216) : new JValue("NaN"), ["y"] = 0, ["z"] = 0 });
            JObject right = Value(type, new JObject { ["x"] = integer ? new JValue(16777217) : new JValue("NaN"), ["y"] = 0, ["z"] = 0 });
            if (kind is MemberKind.Vector2 or MemberKind.Vector2Int)
            {
                ((JObject)left["value"]!["value"]!).Remove("z");
                ((JObject)right["value"]!["value"]!).Remove("z");
            }
            Assert.That(Run(ObjectMember("Equals", left, type, Box(right, type)), BoolType()), Is.False);
            Assert.That(Run(ObjectMember("Equals", Box(left, type), ObjectType(), Box(right, type)), BoolType()), Is.False);
            Assert.That(new UnityEngine.Vector2(float.NaN, 0).Equals(new UnityEngine.Vector2(float.NaN, 0)), Is.False);
            Assert.That(new UnityEngine.Vector3(float.NaN, 0, 0).Equals(new UnityEngine.Vector3(float.NaN, 0, 0)), Is.False);
            Assert.That(new UnityEngine.Vector2Int(16777216, 0).Equals(new UnityEngine.Vector2Int(16777217, 0)), Is.False);
            Assert.That(new UnityEngine.Vector3Int(16777216, 0, 0).Equals(new UnityEngine.Vector3Int(16777217, 0, 0)), Is.False);
        }

        [TestCase(MemberKind.Sprite)]
        [TestCase(MemberKind.Audio)]
        public void AssetIdentityAlsoWorksThroughCollectionReferenceKeys(MemberKind kind)
        {
            using NeoClient client = BuildClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);
            TypeBinding type = TypeBinding.Closed(new TypeHead(kind, true));
            NeoBox Asset() => NeoBox.Of(type, new Dictionary<string, object?> { ["fileId"] = "same-asset", ["sliceIndex"] = 2 });
            NeoBox first = Asset();
            NeoBox second = Asset();
            Assert.That(NSGetterEvaluator.ReferenceEqual(first, second, ctx), Is.True);
            Assert.That(NSGetterEvaluator.ReferenceKeyOf(first, ctx), Is.EqualTo(NSGetterEvaluator.ReferenceKeyOf(second, ctx)));
        }

        [TestCase(MemberKind.Sprite)]
        [TestCase(MemberKind.Audio)]
        public void AssetIdentityUsesTheAssetKey(MemberKind kind)
        {
            JObject type = Primitive(kind);
            JObject Asset(string id, int slice) => Box(Value(type, new JObject { ["fileId"] = id, ["sliceIndex"] = slice }), type);
            Assert.That(Run(ObjectStatic("ReferenceEquals", Asset("a", 2), Asset("a", 2)), BoolType()), Is.True);
            Assert.That(Run(ObjectStatic("ReferenceEquals", Asset("a", 2), Asset("b", 2)), BoolType()), Is.False);
            if (kind == MemberKind.Sprite)
                Assert.That(Run(ObjectStatic("ReferenceEquals", Asset("a", 2), Asset("a", 3)), BoolType()), Is.False);
        }

        [Test]
        public void BoxedIntUnboxesToItsPayload()
        {
            Assert.That(Run(Cast(Box(Int(3), IntType()), IntType()), IntType()), Is.EqualTo(3d));
        }

        [Test]
        public void UnboxingToAnotherValueTypeFails()
        {
            NSGetterRuntimeError error = Fails(() => Run(Cast(Box(Int(3), IntType()), FloatType()), FloatType()));
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoError));
            Assert.That(error.Message, Is.EqualTo("Unable to cast object of type 'int' to type 'float'."));
        }

        [Test]
        public void AsToAnotherTypeIsNull()
        {
            Assert.That(Run(Cast(Box(Int(3), IntType()), Nullable(IntType()), "as"), Nullable(IntType())), Is.EqualTo(3d));
            Assert.That(Run(Cast(Box(Int(3), IntType()), StringType(nullable: true), "as"), StringType(nullable: true)), Is.Null);
            Assert.That(Run(Cast(Box(Text("x"), StringType()), Nullable(IntType()), "as"), Nullable(IntType())), Is.Null);
        }

        [Test]
        public void UnboxingNullFails()
        {
            NSGetterRuntimeError error = Fails(() => Run(Cast(Null(), IntType()), IntType()));
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoNullReferenceError));
            Assert.That(error.Message, Is.EqualTo("Object reference not set to an instance of an object."));
        }

        [Test]
        public void ValueBoxesAreFreshButStringBoxesShareIdentity()
        {
            Assert.That(Run(ObjectStatic("ReferenceEquals", Box(Int(3), IntType()), Box(Int(3), IntType())), BoolType()), Is.EqualTo(false));
            Assert.That(Run(ObjectStatic("ReferenceEquals", Box(Text("x"), StringType()), Box(Text("x"), StringType())), BoolType()), Is.EqualTo(true));
            Assert.That(Run(ObjectStatic("StaticEquals", Box(Int(3), IntType()), Box(Int(3), IntType())), BoolType()), Is.EqualTo(true));
            Assert.That(Run(ObjectStatic("StaticEquals", Box(Int(3), IntType()), Box(Float(3d), FloatType())), BoolType()), Is.EqualTo(false));
        }

        [Test]
        public void OneBoxKeepsItsIdentity()
        {
            object? result = Run(
                ObjectStatic("ReferenceEquals", Local("a"), Local("a")),
                BoolType(),
                Declare("a", ObjectType(), Box(Int(3), IntType())));
            Assert.That(result, Is.EqualTo(true));
        }

        [Test]
        public void NewObjectIsAFreshObjectBox()
        {
            JObject newObject = New("newObject", new JObject());
            object? result = Run(newObject, ObjectType());
            Assert.That(result, Is.InstanceOf<NeoBox>());
            Assert.That(((NeoBox)result!).Payload, Is.Null);
            Assert.That(Run(ObjectStatic("ReferenceEquals", newObject, New("newObject", new JObject())), BoolType()), Is.EqualTo(false));
            Assert.That(Run(ObjectMember("GetType", newObject, ObjectType()), ObjectType(NeoObjectTypeKind.Type)), Is.SameAs(Run(TypeOf(ObjectType()), ObjectType(NeoObjectTypeKind.Type))));
        }

        [Test]
        public void IsCheckOnABoxMatchesTheHeldType()
        {
            Assert.That(Run(IsCheck(Box(Int(3), IntType()), IntType()), BoolType()), Is.EqualTo(true));
            Assert.That(Run(IsCheck(Box(Int(3), IntType()), FloatType()), BoolType()), Is.EqualTo(false));
            Assert.That(Run(IsCheck(Box(Int(3), IntType()), ObjectType(NeoObjectTypeKind.ValueType)), BoolType()), Is.EqualTo(true));
            Assert.That(Run(IsCheck(Box(Text("x"), StringType()), ObjectType(NeoObjectTypeKind.ValueType)), BoolType()), Is.EqualTo(false));
        }

        [Test]
        public void BoxedTupleUnboxes()
        {
            JObject tupleType = TupleType(IntType(), IntType());
            object? result = Run(Cast(Box(Tuple(tupleType, Int(1), Int(2)), tupleType), tupleType), tupleType);
            Assert.That(result, Is.InstanceOf<RuntimeTuple>());
            Assert.That(((RuntimeTuple)result!)[1], Is.EqualTo(2d));
        }

        [Test]
        public void BoxedEnumUnboxesThroughEnum()
        {
            JObject asEnum = Cast(Box(Option("mars"), EnumType()), ObjectType(NeoObjectTypeKind.Enum));
            object? result = Run(Cast(asEnum, EnumType()), EnumType());
            Assert.That(result, Is.EqualTo(new object?[] { "mars" }));
            NSGetterRuntimeError error = Fails(() => Run(Cast(Box(Int(1), IntType()), ObjectType(NeoObjectTypeKind.Enum)), ObjectType(NeoObjectTypeKind.Enum)));
            Assert.That(error.Message, Is.EqualTo("Unable to cast object of type 'int' to type 'Enum'."));
        }

        [TestCase(3.7d, false, 3d)]
        [TestCase(-3.7d, false, -3d)]
        [TestCase(double.NaN, false, 0d)]
        [TestCase(1e10d, false, 2147483647d)]
        public void FloatToIntTruncatesAndSaturates(double value, bool isChecked, double expected)
        {
            Assert.That(Run(Convert(Float(value), FloatType(), IntType(), isChecked), IntType()), Is.EqualTo(expected));
        }

        [Test]
        public void CheckedFloatToIntOverflows()
        {
            NSGetterRuntimeError error = Fails(() => Run(Convert(Float(1e10d), FloatType(), IntType(), isChecked: true), IntType()));
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoArithmeticError));
            Assert.That(error.Message, Is.EqualTo("Arithmetic operation resulted in an overflow."));
        }

        [Test]
        public void DecimalConversions()
        {
            Assert.That(Run(Convert(Decimal("3.9"), DecimalType(), IntType()), IntType()), Is.EqualTo(3d));
            NSGetterRuntimeError error = Fails(() => Run(Convert(Decimal("10000000000"), DecimalType(), IntType()), IntType()));
            Assert.That(error.Message, Is.EqualTo("Value was either too large or too small for an Int32."));
            Assert.That(Run(Convert(Float(0.1d), FloatType(), DecimalType()), DecimalType()), Is.EqualTo("0.1"));
            Assert.That(Run(Convert(Decimal("0.1"), DecimalType(), FloatType()), FloatType()), Is.EqualTo((double)0.1f));
        }

        [Test]
        public void EnumConversionsUseDeclarationOrder()
        {
            Assert.That(Run(Convert(Option("mars"), EnumType(), IntType()), IntType()), Is.EqualTo(1d));
            Assert.That(Run(Convert(Int(2), IntType(), EnumType()), EnumType()), Is.EqualTo(new object?[] { "venus" }));
            NSGetterRuntimeError error = Fails(() => Run(Convert(Int(7), IntType(), EnumType()), EnumType()));
            Assert.That(error.Message, Is.EqualTo("Unable to cast object of type 'int' to type 'Planet'."));
        }

        [Test]
        public void NullToAValueTypeConversionFails()
        {
            NSGetterRuntimeError error = Fails(() => Run(Convert(Null(), Nullable(FloatType()), IntType()), IntType()));
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoInvalidOperationError));
            Assert.That(error.Message, Is.EqualTo("Nullable object must have a value."));
        }

        [Test]
        public void DefaultsFollowTheType()
        {
            Assert.That(Run(Default(IntType()), IntType()), Is.EqualTo(0d));
            Assert.That(Run(Default(DecimalType()), DecimalType()), Is.EqualTo("0"));
            Assert.That(Run(Default(EnumType()), EnumType()), Is.EqualTo(new object?[] { "earth" }));
            Assert.That(Run(Default(StringType(nullable: true)), StringType(nullable: true)), Is.Null);
            Assert.That(Run(Default(Nullable(IntType())), Nullable(IntType())), Is.Null);
            object? vector = Run(Default(Primitive(MemberKind.Vector2)), Primitive(MemberKind.Vector2));
            Assert.That(((NeoVector2Value)vector!).y, Is.EqualTo(0d));
        }

        [Test]
        public void FloatSymbolLiteralsDecode()
        {
            object? result = Run(Value(FloatType(), "NaN"), FloatType());
            Assert.That(double.IsNaN((double)result!), Is.True);
            Assert.That(Run(Value(FloatType(), "-Infinity"), FloatType()), Is.EqualTo(double.NegativeInfinity));
        }

        [Test]
        public void TypeOfValuesAreIdentities()
        {
            Assert.That(Run(ObjectStatic("ReferenceEquals", TypeOf(IntType()), TypeOf(IntType())), BoolType()), Is.EqualTo(true));
            Assert.That(Run(ObjectStatic("ReferenceEquals", TypeOf(IntType()), TypeOf(FloatType())), BoolType()), Is.EqualTo(false));
            object? getType = Run(ObjectMember("GetType", Box(Int(3), IntType()), ObjectType()), ObjectType(NeoObjectTypeKind.Type));
            Assert.That(getType, Is.SameAs(Run(TypeOf(IntType()), ObjectType(NeoObjectTypeKind.Type))));
        }

        [Test]
        public void ObjectMembersOnBoxes()
        {
            Assert.That(Run(ObjectMember("ToString", Box(Int(3), IntType()), ObjectType()), StringType()), Is.EqualTo("3"));
            Assert.That(Run(ObjectMember("ToString", Box(Option("mars"), EnumType()), ObjectType()), StringType()), Is.EqualTo("Mars"));
            Assert.That(Run(ObjectMember("ToString", New("box", new JObject { ["pointer"] = Vector2(1d, 2.5d), ["sourceType"] = Primitive(MemberKind.Vector2) }), ObjectType()), StringType()), Is.EqualTo("(1.00, 2.50)"));
            Assert.That(Run(ObjectMember("Equals", Box(Int(3), IntType()), ObjectType(), Box(Int(3), IntType())), BoolType()), Is.EqualTo(true));
            Assert.That(Run(ObjectMember("GetHashCode", Box(Int(3), IntType()), ObjectType()), IntType()), Is.EqualTo(3d));
            Assert.That(Run(Stringify(Box(Float(1.5d), FloatType()), ObjectType()), StringType()), Is.EqualTo("1.5"));
        }

        [Test]
        public void AssetsPrintTheirNameAndUnityType()
        {
            JObject slice = Value(Primitive(MemberKind.Sprite), new JObject { ["fileId"] = "tree-file", ["sliceIndex"] = 2 });
            JObject clip = Value(Primitive(MemberKind.Audio), new JObject { ["fileId"] = "song-file" });
            Assert.That(Run(ObjectMember("ToString", slice, Primitive(MemberKind.Sprite)), StringType()), Is.EqualTo("tree_2 (UnityEngine.Sprite)"));
            Assert.That(Run(ObjectMember("ToString", clip, Primitive(MemberKind.Audio)), StringType()), Is.EqualTo("song.ogg (UnityEngine.AudioClip)"));
        }

        [TestCase(null, "tree.png (UnityEngine.Sprite)")]
        [TestCase(0, "tree_0 (UnityEngine.Sprite)")]
        [TestCase(2, "tree_2 (UnityEngine.Sprite)")]
        public void StoredSpriteTextPreservesAnAbsentSlice(int? slice, string expected)
        {
            var json = new JObject { ["fileId"] = "tree-file" };
            if (slice.HasValue)
                json["sliceIndex"] = slice.Value;
            SpriteValue sprite = json.ToObject<SpriteValue>()!;
            Assert.That(JToken.DeepEquals(JObject.FromObject(sprite), json), Is.True, "Sprite wire round trip");
            using NeoClient client = BuildClient();
            NSGetterEvaluator.Context ctx = client.RentDirectFunctionContext(NeoValueOwnership.Session);
            try
            {
                var row = new SpriteMemberValue { id = "stored-sprite", value = sprite };
                var member = new SpriteMember { id = "sprite-slot", name = "Sprite", kind = MemberKind.Sprite };
                object? value = NSGetterEvaluator.UnwrapRow(row, ctx, NeoValueOwnership.Session, member);
                var scope = new NeoScriptScope(new Dictionary<string, object?> { ["sprite"] = value });
                Pointer pointer = ObjectMember("ToString", Local("sprite"), Primitive(MemberKind.Sprite)).ToObject<Pointer>()!;
                Assert.That(NSGetterEvaluator.EvalPointer(pointer, scope, ctx), Is.EqualTo(expected));
            }
            finally
            {
                client.ReturnDirectFunctionContext(ctx, null);
            }
        }

        [TestCase(MemberKind.Object, null, "object")]
        [TestCase(MemberKind.Object, NeoObjectTypeKind.ValueType, "ValueType")]
        [TestCase(MemberKind.Object, NeoObjectTypeKind.Enum, "Enum")]
        [TestCase(MemberKind.Interface, null, "IThing")]
        public void ReferenceParameterDefaultsRefuseNonNullStoredValues(MemberKind kind, NeoObjectTypeKind? objectType, string name)
        {
            using NeoClient client = BuildClient();
            var parameter = new FunctionArgumentTypeInfo
            {
                name = "item",
                type = kind,
                required = true,
                objectType = objectType,
                interfaceId = kind == MemberKind.Interface ? Thing : null,
                defaultValue = new ParameterDefaultValue { value = 3L },
            };
            var error = Assert.Throws<NSGetterRuntimeError>(() => NSGetterEvaluator.ParameterDefault(client, parameter, parameter, "NSFunction 'Bad' (bad)"))!;
            Assert.That(error.Message, Is.EqualTo($"NSFunction 'Bad' (bad) parameter 'item' of declared type '{name}' has a non-null default. Reference-type parameter defaults must be null or default."));
            parameter.defaultValue = new ParameterDefaultValue { value = null };
            Assert.That(NSGetterEvaluator.ParameterDefault(client, parameter, parameter, "NSFunction 'Bad' (bad)"), Is.Null);
            parameter.defaultValue = new ParameterDefaultValue { isDefault = true };
            Assert.That(NSGetterEvaluator.ParameterDefault(client, parameter, parameter, "NSFunction 'Bad' (bad)"), Is.Null);
        }

        [Test]
        public void NullReceiverFailsUnlessOptional()
        {
            NSGetterRuntimeError error = Fails(() => Run(ObjectMember("ToString", Null(), ObjectType(nullable: true)), StringType()));
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoNullReferenceError));
            JObject optional = ObjectMember("ToString", Null(), ObjectType(nullable: true));
            optional["optional"] = true;
            Assert.That(Run(optional, StringType(nullable: true)), Is.Null);
        }

        [Test]
        public void TypeNames()
        {
            JObject listOfInt = new JObject { ["type"] = (int)MemberKind.List, ["required"] = true, ["entryTypeInfo"] = IntType() };
            Assert.That(Run(TypeMember("Name", TypeOf(listOfInt)), StringType()), Is.EqualTo("List<int>"));
            Assert.That(Run(TypeMember("FullName", TypeOf(TupleType(IntType(), StringType()))), StringType()), Is.EqualTo("(int, string)"));
            Assert.That(Run(TypeMember("Name", TypeOfDefinition(MemberKind.List)), StringType()), Is.EqualTo("List<T>"));
            Assert.That(Run(TypeMember("Namespace", TypeOf(IntType())), StringType(nullable: true)), Is.Null);
            Assert.That(Run(TypeMember("Name", TypeMember("BaseType", TypeOf(ObjectType(NeoObjectTypeKind.Type)))), StringType()), Is.EqualTo("MemberInfo"));
            Assert.That(Run(TypeMember("Namespace", TypeMember("BaseType", TypeOf(ObjectType(NeoObjectTypeKind.Type)))), StringType()), Is.EqualTo("System.Reflection"));
        }

        [Test]
        public void TypeFactsFollowDotNet()
        {
            Assert.That(Run(TypeMember("IsPrimitive", TypeOf(IntType())), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("IsPrimitive", TypeOf(DecimalType())), BoolType()), Is.EqualTo(false));
            Assert.That(Run(TypeMember("IsValueType", TypeOf(EnumType())), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("IsEnum", TypeOf(EnumType())), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("IsSealed", TypeOf(StringType())), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("IsClass", TypeOf(StringType())), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("IsAbstract", TypeOf(ObjectType(NeoObjectTypeKind.ValueType))), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("IsInterface", TypeOf(InterfaceType(Thing))), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("IsAbstract", TypeOf(ClassType(Shape))), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("IsSealed", TypeOf(ClassType(Circle))), BoolType()), Is.EqualTo(true));
        }

        [Test]
        public void BaseTypesFollowDotNet()
        {
            Assert.That(Run(TypeMember("Name", TypeMember("BaseType", TypeOf(IntType()))), StringType()), Is.EqualTo("ValueType"));
            Assert.That(Run(TypeMember("Name", TypeMember("BaseType", TypeOf(EnumType()))), StringType()), Is.EqualTo("Enum"));
            Assert.That(Run(TypeMember("Name", TypeMember("BaseType", TypeOf(StringType()))), StringType()), Is.EqualTo("object"));
            Assert.That(Run(TypeMember("BaseType", TypeOf(ObjectType())), ObjectType(NeoObjectTypeKind.Type, nullable: true)), Is.Null);
            Assert.That(Run(TypeMember("BaseType", TypeOf(InterfaceType(Thing))), ObjectType(NeoObjectTypeKind.Type, nullable: true)), Is.Null);
            Assert.That(Run(TypeMember("Name", TypeMember("BaseType", TypeOf(ClassType(Circle)))), StringType()), Is.EqualTo("Shape"));
        }

        [Test]
        public void ClassRelations()
        {
            Assert.That(Run(TypeMember("IsSubclassOf", TypeOf(ClassType(Circle)), TypeOf(ClassType(Shape))), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("IsSubclassOf", TypeOf(ClassType(Circle)), TypeOf(ClassType(Circle))), BoolType()), Is.EqualTo(false));
            Assert.That(Run(TypeMember("IsAssignableFrom", TypeOf(InterfaceType(Thing)), TypeOf(ClassType(Circle))), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("IsAssignableFrom", TypeOf(ClassType(Circle)), TypeOf(ClassType(Shape))), BoolType()), Is.EqualTo(false));
            Assert.That(Run(TypeMember("IsAssignableFrom", TypeOf(Nullable(IntType())), TypeOf(IntType())), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("IsAssignableFrom", TypeOf(ObjectType()), TypeOf(IntType())), BoolType()), Is.EqualTo(true));
            object? interfaces = Run(TypeMember("GetInterfaces", TypeOf(ClassType(Circle))), ListOf(ObjectType(NeoObjectTypeKind.Type)));
            Assert.That(((object?[])interfaces!).Length, Is.EqualTo(2));
        }

        [Test]
        public void GenericTypeMembers()
        {
            JObject listOfInt = new JObject { ["type"] = (int)MemberKind.List, ["required"] = true, ["entryTypeInfo"] = IntType() };
            Assert.That(Run(TypeMember("IsGenericType", TypeOf(listOfInt)), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("IsGenericType", TypeOf(Nullable(IntType()))), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("IsGenericTypeDefinition", TypeOfDefinition(MemberKind.List)), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("ContainsGenericParameters", TypeOfDefinition(MemberKind.List)), BoolType()), Is.EqualTo(true));
            Assert.That(Run(TypeMember("ContainsGenericParameters", TypeOf(listOfInt)), BoolType()), Is.EqualTo(false));
            Assert.That(
                Run(ObjectStatic("ReferenceEquals", TypeMember("GetGenericTypeDefinition", TypeOf(listOfInt)), TypeOfDefinition(MemberKind.List)), BoolType()),
                Is.EqualTo(true));
            Assert.That(Run(TypeMember("Name", TypeMember("GetGenericTypeDefinition", TypeOf(Nullable(IntType())))), StringType()), Is.EqualTo("Nullable<T>"));
            object? arguments = Run(TypeMember("GenericTypeArguments", TypeOf(Nullable(IntType()))), ListOf(ObjectType(NeoObjectTypeKind.Type)));
            Assert.That(((object?[])arguments!)[0], Is.SameAs(Run(TypeOf(IntType()), ObjectType(NeoObjectTypeKind.Type))));
            NSGetterRuntimeError error = Fails(() => Run(TypeMember("GetGenericTypeDefinition", TypeOf(IntType())), ObjectType(NeoObjectTypeKind.Type)));
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoInvalidOperationError));
            Assert.That(error.Message, Is.EqualTo("This operation is only valid on generic types."));
        }

        [Test]
        public void GenericParameterPositionNeedsAParameter()
        {
            NSGetterRuntimeError error = Fails(() => Run(TypeMember("GenericParameterPosition", TypeOf(IntType())), IntType()));
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoInvalidOperationError));
            Assert.That(error.Message, Is.EqualTo("Method may only be called on a Type for which Type.IsGenericParameter is true."));
            Assert.That(Run(TypeMember("IsGenericParameter", TypeOf(IntType())), BoolType()), Is.EqualTo(false));
        }

        [Test]
        public void NullTypeReceiverFails()
        {
            NSGetterRuntimeError error = Fails(() => Run(TypeMember("Name", Null()), StringType()));
            Assert.That(error.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoNullReferenceError));
            Assert.That(error.Message, Is.EqualTo("Object reference not set to an instance of an object."));
        }

        /// <summary>The runtime error a failing evaluation reports, under its NeoScript frames.</summary>
        private static NSGetterRuntimeError Fails(TestDelegate code)
        {
            var error = Assert.Throws<NeoScriptException>(code)!;
            Assert.That(error.InnerException, Is.InstanceOf<NSGetterRuntimeError>());
            return (NSGetterRuntimeError)error.InnerException!;
        }

        private static object? Run(JObject pointer, JObject returnType, params JObject[] locals)
        {
            var instructions = new JArray();
            foreach (JObject local in locals)
                instructions.Add(local);
            instructions.Add(new JObject { ["type"] = "return", ["pointer"] = pointer });
            var function = JsonConvert.DeserializeObject<FunctionWithReturnType>(new JObject
            {
                ["parameters"] = new JArray(),
                ["typeInfo"] = returnType,
                ["instructions"] = instructions,
            }.ToString())!;
            function.compilerRevision = FunctionWithReturnType.CurrentCompilerRevision;
            using NeoClient client = BuildClient();
            return NSGetterEvaluator.Evaluate(function, new NSGetterEvaluator.Context(client, null, null));
        }

        private static JObject Declare(string id, JObject type, JObject pointer) => new JObject
        {
            ["type"] = "variable",
            ["variable"] = new JObject { ["id"] = id, ["typeInfo"] = type, ["pointer"] = pointer },
        };

        private static JObject New(string type, JObject fields)
        {
            fields["type"] = type;
            return fields;
        }

        private static JObject Local(string id) => New("variable", new JObject { ["variableId"] = id });

        private static JObject Value(JObject type, JToken value) =>
            New("value", new JObject { ["value"] = new JObject { ["typeInfo"] = type, ["value"] = value } });

        private static JObject Null() => Value(new JObject { ["type"] = (int)MemberKind.Null, ["required"] = false }, JValue.CreateNull());

        private static JObject Int(double value) => Value(IntType(), value);

        private static JObject Float(double value) => Value(FloatType(), double.IsNaN(value) ? "NaN" : value);

        private static JObject Decimal(string value) => Value(DecimalType(), value);

        private static JObject Text(string value) => Value(StringType(), value);

        private static JObject Option(string optionId) => Value(EnumType(), new JArray(optionId));

        private static JObject Vector2(double x, double y) => Value(Primitive(MemberKind.Vector2), new JObject { ["x"] = x, ["y"] = y });

        private static JObject Tuple(JObject type, params JObject[] elements) => New("function", new JObject
        {
            ["function"] = new JObject
            {
                ["type"] = "tupleCreate",
                ["info"] = new JObject { ["elementPointers"] = new JArray(elements), ["typeInfo"] = type },
            },
        });

        private static JObject Box(JObject pointer, JObject sourceType) =>
            New("box", new JObject { ["pointer"] = pointer, ["sourceType"] = sourceType });

        private static JObject Cast(JObject pointer, JObject targetType, string mode = "cast") => New("cast", new JObject
        {
            ["pointer"] = pointer,
            ["sourceType"] = ObjectType(nullable: true),
            ["targetType"] = targetType,
            ["mode"] = mode,
        });

        private static JObject Convert(JObject pointer, JObject sourceType, JObject targetType, bool isChecked = false)
        {
            JObject convert = New("convert", new JObject { ["pointer"] = pointer, ["sourceType"] = sourceType, ["targetType"] = targetType });
            if (isChecked)
                convert["checked"] = true;
            return convert;
        }

        private static JObject Default(JObject type) => New("default", new JObject { ["typeInfo"] = type });

        private static JObject TypeOf(JObject type) => New("typeOf", new JObject { ["typeInfo"] = type });

        private static JObject TypeOfDefinition(MemberKind kind) =>
            New("typeOf", new JObject { ["definition"] = new JObject { ["type"] = (int)kind } });

        private static JObject IsCheck(JObject pointer, JObject checkType) =>
            New("isCheck", new JObject { ["pointer"] = pointer, ["checkType"] = checkType });

        private static JObject Stringify(JObject pointer, JObject sourceType) =>
            New("stringify", new JObject { ["pointer"] = pointer, ["sourceType"] = sourceType });

        private static JObject ObjectStatic(string member, JObject left, JObject right) =>
            New("objectMember", new JObject { ["member"] = member, ["args"] = new JArray(left, right) });

        private static JObject ObjectMember(string member, JObject receiver, JObject receiverType, params JObject[] args) => New("objectMember", new JObject
        {
            ["member"] = member,
            ["receiver"] = receiver,
            ["receiverType"] = receiverType,
            ["args"] = new JArray(args),
        });

        private static JObject TypeMember(string member, JObject receiver, params JObject[] args) => New("typeMember", new JObject
        {
            ["member"] = member,
            ["receiver"] = receiver,
            ["args"] = new JArray(args),
        });

        private static JObject Primitive(MemberKind kind, bool nullable = false) =>
            new JObject { ["type"] = (int)kind, ["required"] = !nullable };

        private static JObject IntType() => Primitive(MemberKind.Int);

        private static JObject FloatType() => Primitive(MemberKind.Float);

        private static JObject BoolType() => Primitive(MemberKind.Bool);

        private static JObject DecimalType() => Primitive(MemberKind.Decimal);

        private static JObject StringType(bool nullable = false) => Primitive(MemberKind.String, nullable);

        private static JObject Nullable(JObject type)
        {
            var copy = (JObject)type.DeepClone();
            copy["required"] = false;
            return copy;
        }

        private static JObject EnumType() => new JObject { ["type"] = (int)MemberKind.Enum, ["required"] = true, ["enumId"] = Planet };

        private static JObject ClassType(string classId) => new JObject { ["type"] = (int)MemberKind.Class, ["required"] = true, ["classId"] = classId };

        private static JObject InterfaceType(string interfaceId) =>
            new JObject { ["type"] = (int)MemberKind.Interface, ["required"] = true, ["interfaceId"] = interfaceId };

        private static JObject ObjectType(NeoObjectTypeKind? objectType = null, bool nullable = false)
        {
            JObject type = Primitive(MemberKind.Object, nullable);
            if (objectType is { } kind)
                type["objectType"] = (int)kind;
            return type;
        }

        private static JObject ListOf(JObject entry) =>
            new JObject { ["type"] = (int)MemberKind.List, ["required"] = true, ["entryTypeInfo"] = entry };

        private static JObject TupleType(params JObject[] elements)
        {
            var entries = new JArray();
            foreach (JObject element in elements)
                entries.Add(new JObject { ["typeInfo"] = element });
            return new JObject { ["type"] = (int)MemberKind.Tuple, ["required"] = true, ["elements"] = entries };
        }

        private static NeoClient BuildClient()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var shape = new NeoSchemaClass
            {
                id = Shape,
                projectId = "project-a",
                name = "Shape",
                schema = new Dictionary<string, string>(),
                implementsInterfaces = new List<InterfaceConstruction> { new InterfaceConstruction { interfaceId = Round } },
                Modifier = NeoClassModifierKind.Abstract,
            };
            var circle = new NeoSchemaClass
            {
                id = Circle,
                projectId = "project-a",
                name = "Circle",
                schema = new Dictionary<string, string>(),
                extendsClassId = Shape,
                Modifier = NeoClassModifierKind.Sealed,
            };
            var members = new Dictionary<string, NeoCompose.Runtime.Json.Member>();
            var values = new Dictionary<string, MemberValue>();
            foreach (string root in new[] { "root-assets", "root-save", "root-session" })
            {
                members[root] = new ClassMember
                {
                    id = root,
                    projectId = "project-a",
                    name = root,
                    kind = MemberKind.Class,
                    Requirement = NeoMemberRequirementKind.Required,
                    valueId = root + "-value",
                    DeclaredClassId = rootClass.id,
                };
                values[root + "-value"] = new ObjectMemberValue
                {
                    id = root + "-value",
                    classId = rootClass.id,
                    value = new Dictionary<string, string>(),
                };
            }
            var planet = new NeoCompose.Runtime.Json.Enum
            {
                id = Planet,
                projectId = "project-a",
                name = "Planet",
                options = new Dictionary<string, EnumOption>
                {
                    ["earth"] = new EnumOption { text = "Earth" },
                    ["mars"] = new EnumOption { text = "Mars" },
                    ["venus"] = new EnumOption { text = "Venus" },
                },
                optionKeyOrder = new List<string> { "earth", "mars", "venus" },
            };
            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "P127 objects",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = members,
                values = values,
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                    [shape.id] = shape,
                    [circle.id] = circle,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum> { [Planet] = planet },
                files = new Dictionary<string, ProjectFile>
                {
                    ["tree-file"] = new ProjectFile { id = "tree-file", projectId = "project-a", name = "tree.png" },
                    ["song-file"] = new ProjectFile { id = "song-file", projectId = "project-a", name = "song.ogg" },
                },
                interfaces = new Dictionary<string, Interface>
                {
                    [Thing] = new Interface { id = Thing, projectId = "project-a", name = "IThing", members = new Dictionary<string, InterfaceMember>() },
                    [Round] = new Interface
                    {
                        id = Round,
                        projectId = "project-a",
                        name = "IRound",
                        members = new Dictionary<string, InterfaceMember>(),
                        extendsInterfaces = new List<InterfaceConstruction> { new InterfaceConstruction { interfaceId = Thing } },
                    },
                },
            }.WithUserRoot());
        }
    }
}
