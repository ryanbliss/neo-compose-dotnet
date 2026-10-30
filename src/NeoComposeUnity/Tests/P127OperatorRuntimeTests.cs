// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using NeoCompose.Runtime.NeoScript.Globalization;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using NeoCompose.Runtime.NeoScript.Hashing;

namespace NeoCompose.Tests
{
    public sealed class P127OperatorRuntimeTests
    {
        private NeoClient client = null!;
        private NSGetterEvaluator.Context context = null!;
        private NeoScriptScope scope = null!;

        [SetUp]
        public void SetUp()
        {
            client = NeoTestSaveStack.ClientFromSchema(NeoTestExport.Read(File.ReadAllText(
                "Packages/com.ryanbliss.neocompose/Tests/p127-overloads-parity-export.json")), assumeCurrentSchema: false);
            context = new NSGetterEvaluator.Context(client, null, null);
            scope = new NeoScriptScope();
        }

        [TearDown]
        public void TearDown() => client.Dispose();

        [Test]
        public void LetBindingsAreOrderedAndDoNotEscapeTheirChildScope()
        {
            scope.SetLocal("x", 7);
            var let = new LetPointer
            {
                type = PointerKind.Let,
                bindings = new[]
                {
                    new Variable { id = "x", typeInfo = Type(MemberKind.Int), pointer = Value(3) },
                    new Variable { id = "y", typeInfo = Type(MemberKind.Int), pointer = Local("x") },
                },
                pointer = Local("y"),
            };
            Assert.That(Eval(let), Is.EqualTo(3));
            Assert.That(Eval(Local("x")), Is.EqualTo(7));
            Assert.Throws<NSGetterRuntimeError>(() => Eval(Local("y")));
        }

        [TestCase("op_Addition", 2147483647, 1, -2147483648)]
        [TestCase("op_Subtraction", -2147483648, 1, 2147483647)]
        [TestCase("op_Multiply", 1073741824, 4, 0)]
        [TestCase("op_UnsignedRightShift", -1, 1, 2147483647)]
        [TestCase("op_LeftShift", 3, 33, 6)]
        public void IntegerOperatorsUseInt32Semantics(string member, int left, int right, int expected) =>
            Assert.That(Call(MemberKind.Int, member, left, right), Is.EqualTo(expected));

        [TestCase("op_CheckedAddition", int.MaxValue, 1)]
        [TestCase("op_CheckedMultiply", 1073741824, 4)]
        [TestCase("op_Division", int.MinValue, -1)]
        public void IntegerOverflowThrowsTheRuntimeError(string member, int left, int right)
        {
            Assert.That(Assert.Throws<NeoRuntimeError>(() => Call(MemberKind.Int, member, left, right))!.ErrorClass,
                Is.EqualTo(NeoErrorClass.NeoOverflowError));
        }

        [Test]
        public void SingleArithmeticAndBitwiseMembersCanonicalizeNaNs()
        {
            Assert.That(Call(MemberKind.Float, "MultiplyAddEstimate", 0.1, 10, -1), Is.EqualTo(1.4901161E-08f));
            object nan = Call(MemberKind.Float, "op_UnaryNegation", float.NaN)!;
            Assert.That(BitConverter.SingleToInt32Bits((float)nan), Is.EqualTo(unchecked((int)0xFFC00000)));
            Assert.That(Call(MemberKind.Float, "IsNegative", nan), Is.True);
            object or = Call(MemberKind.Float, "op_BitwiseOr", float.NaN, float.Epsilon)!;
            Assert.That(Call(MemberKind.Float, "op_ExclusiveOr", or, float.NaN), Is.EqualTo(0f));
            Assert.That(Call(MemberKind.Float, "CopySign", 1, float.NaN), Is.EqualTo(-1f));
        }

        [Test]
        public void DecimalOperatorsKeepSignedZeroAndScale()
        {
            Assert.That(Call(MemberKind.Decimal, "op_UnaryNegation", "0.00"), Is.EqualTo("-0.00"));
            Assert.That(Call(MemberKind.Decimal, "op_Addition", "1.10", "2.20"), Is.EqualTo("3.30"));
            Assert.That(Call(MemberKind.Decimal, "StaticEquals", "1.10", "1.1"), Is.True);
        }

        [Test]
        public void TryParseWritesDefaultOnFailureAndPreservesDecimalZeroSign()
        {
            object? output = 12;
            var reference = new NeoOutReference(() => output, value => output = value);
            Assert.That(Call(MemberKind.Int, "TryParse", "bad", 7, null, reference), Is.False);
            Assert.That(output, Is.EqualTo(0));
            Assert.That(Call(MemberKind.Decimal, "TryParse", "-0.00", 111, null, reference), Is.True);
            Assert.That(output, Is.EqualTo("-0.00"));
        }

        [Test]
        public void ParsingChecksNullAndStylesInTheSpecifiedOrder()
        {
            var parse = Assert.Throws<NeoRuntimeError>(() => Call(MemberKind.Int, "Parse", null, 65536, null))!;
            Assert.That(parse.ErrorClass, Is.EqualTo(NeoErrorClass.NeoArgumentNullError));
            var tryParse = Assert.Throws<NeoRuntimeError>(() => Call(MemberKind.Int, "TryParse", null, 65536, null, NeoOutReference.Discard()))!;
            Assert.That(tryParse.ErrorClass, Is.EqualTo(NeoErrorClass.NeoArgumentError));
        }

        [Test]
        public void TypedAndObjectEqualityKeepDistinctContracts()
        {
            Assert.That(Instance(MemberKind.Float, "Equals", float.NaN, float.NaN), Is.True);
            Assert.That(Instance(MemberKind.Int, "EqualsObject", 1, NeoBox.Of(Binding(MemberKind.Float), 1f)), Is.False);
            Assert.That(Instance(MemberKind.Int, "EqualsObject", 1, NeoBox.Of(Binding(MemberKind.Int), 1)), Is.True);
        }

        [Test]
        public void ParseResolvesAProviderOnceAfterValidation()
        {
            var provider = new CountingProvider();
            Assert.That(Call(MemberKind.Int, "Parse", "12", 7, provider), Is.EqualTo(12));
            Assert.That(provider.Calls, Is.EqualTo(1));
            Assert.Throws<NeoRuntimeError>(() => Call(MemberKind.Int, "Parse", null, 7, provider));
            Assert.Throws<NeoRuntimeError>(() => Call(MemberKind.Int, "TryParse", "12", 65536, provider, NeoOutReference.Discard()));
            Assert.That(provider.Calls, Is.EqualTo(1));
        }

        [Test]
        public void PrimitiveDelegateRequiresItsCanonicalInterfaceSignature()
        {
            var provider = TypeBinding.Closed(new TypeHead(MemberKind.Interface, true, interfaceId: NeoPrimitiveConformance.InterfaceId("IFormatProvider")));
            var target = new NeoDelegateValue
            {
                builtIn = new NeoBuiltInTarget(Binding(MemberKind.Int), null, "Parse", 0, new[] { Binding(MemberKind.String), provider }),
            };
            Assert.That(NSGetterEvaluator.InvokeDelegate(target, new object?[] { "12", null }, context), Is.EqualTo(12));
            target.builtIn = new NeoBuiltInTarget(Binding(MemberKind.Int), null, "Parse", 0, new[] { Binding(MemberKind.String) });
            Assert.That(Assert.Throws<NeoScriptPreExecutionValidationError>(() => NSGetterEvaluator.InvokeDelegate(target, new object?[] { "12" }, context))!.Message,
                Is.EqualTo("Built-in target 'int.Parse' names no built-in method with its signature."));
        }

        [Test]
        public void TypeInitializationErrorRetainsItsCausePayload()
        {
            var cause = new NeoRuntimeError(NeoErrorClass.NeoOverflowError, "inner");
            var failure = new NeoRuntimeError(NeoErrorClass.NeoTypeInitializationError, "outer", cause);
            Assert.That(failure.Payload.InnerError, Is.SameAs(cause.Payload));
            Assert.That(failure.InnerException, Is.SameAs(cause));
        }

        [TestCase(MemberKind.Float)]
        [TestCase(MemberKind.Vector2)]
        public void TupleOperatorEqualityDoesNotEqualNaN(MemberKind kind)
        {
            object Left() => kind == MemberKind.Float ? (object)double.NaN : new NeoVector2Value { x = double.NaN, y = 1 };
            var type = TupleType(Type(kind), Type(MemberKind.Int));
            var left = new RuntimeTuple(new object?[] { Left(), 1 }, type);
            var right = new RuntimeTuple(new object?[] { Left(), 1 }, type);
            Assert.That(TupleOperatorEquals(left, right, type), Is.False);
            Assert.That(Eval(new PrimitiveMemberPointer
            {
                type = PointerKind.PrimitiveMember,
                primitive = type,
                member = "Equals",
                receiver = Value(left),
                args = new[] { Value(right) }
            }), Is.EqualTo(kind == MemberKind.Float), "Equals keeps its scalar or Unity value-equality contract.");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void TupleOperatorEqualityUsesClassIdentity(bool equatable)
        {
            const string classId = "tuple-operator-class";
            const string equalsId = "tuple-operator-equals";
            var self = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = classId };
            var owner = new NeoSchemaClass
            {
                id = classId,
                name = "TupleElement",
                projectId = "project-id",
                schema = new(),
                implementsInterfaces = new()
            };
            int calls = 0;
            if (equatable)
            {
                string id = NeoPrimitiveConformance.InterfaceId("IEquatable`1");
                string parameter = client.ProjectDataForRuntime.interfaces[id].genericParams![0].id;
                owner.implementsInterfaces.Add(new InterfaceConstruction
                {
                    interfaceId = id,
                    arguments = new() { [parameter] = NeoTypeBindings.FromTypeInfo(self) }
                });
            }
            else
            {
                owner.schema["Equals#" + NeoObjectContract.EqualsMember] = equalsId;
                client.ProjectDataForRuntime.members[equalsId] = new FunctionMember
                {
                    id = equalsId,
                    name = "Equals",
                    kind = MemberKind.Function,
                    extendsMemberId = NeoObjectContract.EqualsMember,
                    returnTypeInfo = Type(MemberKind.Bool),
                    argumentTypes = new[] { new FunctionArgumentTypeInfo { name = "obj", type = MemberKind.Object, required = false } }
                };
                client.RegisterNativeFunctionInvokers(new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>
                {
                    [equalsId] = (_, _, _, _) =>
                    {
                        calls++;
                        return true;
                    }
                });
            }
            client.ProjectDataForRuntime.classes[classId] = owner;
            client.InvalidateSchemaResolutionCaches();
            using var first = NeoGeneratedTypesSupport.CreateWritableClassValue(client, classId, new Dictionary<string, string>(), Array.Empty<MemberValue>());
            using var second = NeoGeneratedTypesSupport.CreateWritableClassValue(client, classId, new Dictionary<string, string>(), Array.Empty<MemberValue>());
            object? a = NSGetterEvaluator.UnwrapRow(first.value!, context, first.ownership, first.member);
            object? b = NSGetterEvaluator.UnwrapRow(second.value!, context, second.ownership, second.member);
            var type = TupleType(self, Type(MemberKind.Int));
            var left = new RuntimeTuple(new object?[] { a, 1 }, type);
            var right = new RuntimeTuple(new object?[] { b, 1 }, type);
            Assert.That(TupleOperatorEquals(left, right, type), Is.False);
            Assert.That(TupleOperatorEquals(left, left, type), Is.True);
            Assert.That(calls, Is.Zero, "The == operator must not call Equals.");
            var equals = new PrimitiveMemberPointer { type = PointerKind.PrimitiveMember, primitive = type, member = "Equals", receiver = Value(left), args = new[] { Value(right) } };
            if (equatable)
                Assert.That(Assert.Throws<NeoRuntimeError>(() => Eval(equals))!.ErrorClass, Is.EqualTo(NeoErrorClass.NeoNotSupportedError));
            else
            {
                Assert.That(Eval(equals), Is.True);
                Assert.That(calls, Is.EqualTo(1));
            }
        }

        [TestCase(NeoErrorContract.NeoTypeInitializationError)]
        [TestCase(NeoErrorContract.NeoError)]
        public void CachedStaticInitializationFailureCanBeCaughtAfterAnUnmatchedCatch(string catchId)
        {
            Pointer Integer(int number) => new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value { typeInfo = Type(MemberKind.Int), value = new JValue(number) }
            };
            const string ownerId = "failing-static-owner";
            const string memberId = "failing-static-field";
            const string valueId = "failing-static-template";
            var construction = new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = ownerId,
                typeArguments = new() { ["T"] = Type(MemberKind.Int) }
            };
            client.ProjectDataForRuntime.classes[ownerId] = new NeoSchemaClass
            {
                id = ownerId,
                projectId = "project-id",
                name = "Failing",
                schema = new() { ["Value"] = memberId },
                genericParams = new() { new() { id = "T", name = "T" } }
            };
            client.ProjectDataForRuntime.members[memberId] = new IntMember
            {
                id = memberId,
                name = "Value",
                kind = MemberKind.Int,
                Modifier = NeoMemberModifierKind.Static,
                Storage = NeoMemberStorage.Session,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = valueId
            };
            client.ProjectDataForRuntime.values[valueId] = new NumberMemberValue
            {
                id = valueId,
                createdAt = "x",
                updatedAt = "x",
                init = new InitializerBody
                {
                    code = "1 / 0",
                    compiled = new FunctionWithReturnType
                    {
                        typeInfo = Type(MemberKind.Int),
                        parameters = Array.Empty<Variable>(),
                        compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                        instructions = new Instruction[] { new ReturnInstruction { pointer = new PrimitiveMemberPointer
                        { type = PointerKind.PrimitiveMember, primitive = Type(MemberKind.Int), member = "op_Division", args = new[] { Integer(1), Integer(0) } } } }
                    }
                }
            };
            client.InvalidateSchemaResolutionCaches();
            var guarded = new TryInstruction
            {
                instructions = new Instruction[] { new ReturnInstruction { pointer = new StaticMemberPointer { type = PointerKind.StaticMember, memberId = memberId, staticConstruction = construction } } },
                catches = new[] { new CatchClause
                {
                    binding = new CatchBinding { id = "error", isReadonly = true, typeInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = NeoErrorContract.NeoArgumentError } },
                    instructions = new Instruction[] { new ReturnInstruction { pointer = Integer(42) } }
                } }
            };
            var body = new FunctionWithReturnType { typeInfo = Type(MemberKind.Int), parameters = Array.Empty<Variable>(), compilerRevision = FunctionWithReturnType.CurrentCompilerRevision, instructions = new Instruction[] { guarded } };
            var first = Assert.Catch<NSGetterRuntimeError>(() => NSGetterEvaluator.Evaluate(body, context))!;
            Assert.That(first.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoTypeInitializationError), first.ToString());
            Assert.That(first.Payload.Searched, Is.True);
            guarded.catches[0].binding.typeInfo = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = catchId };
            Assert.That(NSGetterEvaluator.Evaluate(body, context), Is.EqualTo(42));
            first.Payload.Searched = true;
            first.Payload.Selection = new NeoScriptExecutor.ErrorSelection(
                new NeoScriptExecutor.ErrorHandlerFrame(client, guarded, Type(MemberKind.Int), scope, context, null),
                guarded.catches[0], scope);
            var repeated = Assert.Catch<NSGetterRuntimeError>(() => client.EnsureStaticConstruction(NeoTypeBindings.FromTypeInfo(construction)))!;
            Assert.That(repeated.Payload, Is.SameAs(first.Payload));
            Assert.That(repeated.Payload.Searched, Is.False);
            Assert.That(repeated.Payload.Selection, Is.Null);
        }

        [TestCase("CompareToObject", "Object must be the same type as the enum. The type passed in was 'System.Int32'; the enum type was 'System.Globalization.NumberStyles'.")]
        [TestCase("HasFlag", "The argument type, 'System.Int32', is not the same as the enum type 'System.Globalization.NumberStyles'.")]
        public void EnumMismatchMessagesUseClrNames(string member, string message)
        {
            var type = new EnumTypeInfo { type = MemberKind.Enum, required = true, enumId = NeoPrimitiveConformance.NumberStylesEnumId };
            var call = new PrimitiveMemberPointer
            {
                type = PointerKind.PrimitiveMember,
                primitive = type,
                member = member,
                receiver = Value(7),
                args = new[] { Value(NeoBox.Of(Binding(MemberKind.Int), 1)) }
            };
            Assert.That(Assert.Throws<NeoRuntimeError>(() => Eval(call))!.Message, Is.EqualTo(message));
        }

        [Test]
        public void DigitShapesMismatchMessagesUseClrNames()
        {
            var type = new EnumTypeInfo { type = MemberKind.Enum, required = true, enumId = NeoPrimitiveConformance.DigitShapesEnumId };
            var call = new PrimitiveMemberPointer
            {
                type = PointerKind.PrimitiveMember,
                primitive = type,
                member = "CompareToObject",
                receiver = Value(new object?[] { "system_36613ff4-3187-4fd7-9776-d8139d33eac1" }),
                args = new[] { Value(NeoBox.Of(Binding(MemberKind.Int), 1)) }
            };
            Assert.That(Assert.Throws<NeoRuntimeError>(() => Eval(call))!.Message,
                Is.EqualTo("Object must be the same type as the enum. The type passed in was 'System.Int32'; the enum type was 'System.Globalization.DigitShapes'."));
            call.member = "HasFlag";
            Assert.That(Assert.Throws<NeoRuntimeError>(() => Eval(call))!.Message,
                Is.EqualTo("The argument type, 'System.Int32', is not the same as the enum type 'System.Globalization.DigitShapes'."));
        }

        [TestCase(8, false, "System.ValueTuple`8[System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.ValueTuple`1[System.Int32]]")]
        [TestCase(9, false, "System.ValueTuple`8[System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.Int32,System.ValueTuple`2[System.Int32,System.Int32]]")]
        [TestCase(2, true, "System.ValueTuple`2[System.Nullable`1[System.Int32],System.Int32]")]
        public void TupleMismatchMessagesUseNestedClrNames(int size, bool nullable, string name)
        {
            var elements = Enumerable.Range(0, size).Select(_ => Type(MemberKind.Int)).ToArray();
            elements[0].required = !nullable;
            var type = TupleType(elements);
            var tuple = new RuntimeTuple(Enumerable.Repeat<object?>(1, size).ToArray(), type);
            var call = new PrimitiveMemberPointer
            {
                type = PointerKind.PrimitiveMember,
                primitive = type,
                member = "CompareToObject",
                receiver = Value(tuple),
                args = new[] { Value(NeoBox.Of(Binding(MemberKind.Int), 1)) }
            };
            Assert.That(Assert.Throws<NeoRuntimeError>(() => Eval(call))!.Message, Is.EqualTo($"Argument must be of type {name}. (Parameter 'other')"));
        }

        private static TupleTypeInfo TupleType(params TypeInfo[] elements) => new()
        {
            type = MemberKind.Tuple,
            required = true,
            elements = elements.Select(type => new TupleElementTypeInfo { typeInfo = type }).ToArray()
        };

        private object? TupleOperatorEquals(RuntimeTuple left, RuntimeTuple right, TupleTypeInfo type) => Eval(new FunctionPointer
        {
            type = PointerKind.Function,
            function = new TupleEqualFunction
            {
                type = FunctionKind.TupleEqual,
                info = new FunctionTupleEqualInfo { typeInfo = type, left = Value(left), right = Value(right) }
            }
        });

        [Test]
        public void NumberStylesOptionsAndFlagsUseTheirNumericValues()
        {
            var type = new EnumTypeInfo { type = MemberKind.Enum, required = true, enumId = NeoPrimitiveConformance.NumberStylesEnumId };
            var literal = new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value { typeInfo = type, value = new JValue("system_a82aef72-4cef-4058-8f4d-d3e9c2757ff9") },
            };
            Assert.That(Eval(literal), Is.EqualTo(7));
            Assert.That(Eval(new PrimitiveMemberPointer
            {
                type = PointerKind.PrimitiveMember,
                primitive = type,
                member = "HasFlag",
                receiver = Value(7),
                args = new[] { Value(3) },
            }), Is.True);
            Assert.That(Eval(new PrimitiveMemberPointer
            {
                type = PointerKind.PrimitiveMember,
                primitive = type,
                member = "ToString",
                receiver = Value(3),
                args = new[] { Value(null), Value(null) },
            }), Is.EqualTo("AllowLeadingWhite, AllowTrailingWhite"));
        }

        [Test]
        public void PrimitiveHashCodesRespectPayloadEquality()
        {
            Assert.That(Instance(MemberKind.Float, "GetHashCode", -0f), Is.EqualTo(Instance(MemberKind.Float, "GetHashCode", 0f)));
            Assert.That(Instance(MemberKind.Float, "GetHashCode", float.NaN), Is.EqualTo(Instance(MemberKind.Float, "GetHashCode", BitConverter.Int32BitsToSingle(0x7FC00123))));
            Assert.That(Instance(MemberKind.Decimal, "GetHashCode", "1.10"), Is.EqualTo(Instance(MemberKind.Decimal, "GetHashCode", "1.1")));
        }

        [Test]
        public void NullablePrimitivesHaveNoInterfaces()
        {
            var dispatch = NeoInterfaceDispatch.Of(client);
            Assert.That(dispatch.PrimitiveClosure(TypeBinding.Closed(new TypeHead(MemberKind.Int, false))), Is.Empty);
        }

        [Test]
        public void BoxedPrimitiveCastsRequireTheExactCanonicalConstruction()
        {
            SeedConformanceInterfaces();
            TypeBinding self = Binding(MemberKind.Int);
            string id = NeoPrimitiveConformance.InterfaceId("IComparable`1");
            TypeInfo Target(MemberKind argument) => new InterfaceTypeInfo
            {
                type = MemberKind.Interface,
                required = false,
                interfaceId = id,
                typeArguments = new() { ["p0"] = Type(argument) },
            };
            NeoBox box = NeoBox.Of(self, 12);
            var cast = new CastPointer
            {
                type = PointerKind.Cast,
                mode = CastModeKind.As,
                pointer = Value(box),
                sourceType = new ObjectTypeInfo { type = MemberKind.Object, required = true },
                targetType = Target(MemberKind.Int)
            };
            Assert.That(Eval(cast), Is.SameAs(box));
            cast.targetType = Target(MemberKind.Float);
            Assert.That(Eval(cast), Is.Null);
            cast.mode = CastModeKind.Cast;
            Assert.That(Assert.Throws<NeoRuntimeError>(() => Eval(cast))!.ErrorClass, Is.EqualTo(NeoErrorClass.NeoInvalidCastError));
        }

        [Test]
        public void PrimitiveReflectionPreservesConformanceOrderAndCachesItsClosure()
        {
            SeedConformanceInterfaces();
            TypeBinding type = Binding(MemberKind.Int);
            var dispatch = NeoInterfaceDispatch.Of(client);
            var closure = dispatch.PrimitiveClosure(type);
            Assert.That(dispatch.PrimitiveClosure(type), Is.SameAs(closure));
            object? result = Eval(new TypeMemberPointer
            {
                type = PointerKind.TypeMember,
                member = TypeMemberKind.GetInterfaces,
                receiver = Value(NeoTypeValue.Of(type)),
                args = Array.Empty<Pointer>(),
            });
            var reflected = ((object?[])result!).Cast<NeoTypeValue>().Select(value => value.Binding).ToArray();
            Assert.That(reflected, Is.EqualTo(closure));
            Assert.That(reflected.Select(value => value.Head!.InterfaceId), Is.EqualTo(NeoPrimitiveConformance.Table[NeoConformingTypeKind.Int].Select(row => row.InterfaceId)));
            var comparable = reflected.Single(value => value.Head!.InterfaceId == NeoPrimitiveConformance.InterfaceId("IComparable`1"));
            Assert.That(comparable.Bindings!["p0"], Is.EqualTo(type));
            client.InvalidateSchemaResolutionCaches();
            Assert.That(NeoInterfaceDispatch.Of(client).PrimitiveClosure(type), Is.Not.SameAs(closure));
        }

        [TestCase("Int", "IComparable IFormattable IComparable`1 IEquatable`1 IBitwiseOperators`3 INumber`1 IComparisonOperators`3 IEqualityOperators`3 IModulusOperators`3 INumberBase`1 IAdditionOperators`3 IAdditiveIdentity`2 IDecrementOperators`1 IDivisionOperators`3 IIncrementOperators`1 IMultiplicativeIdentity`2 IMultiplyOperators`3 IParsable`1 ISubtractionOperators`3 IUnaryPlusOperators`2 IUnaryNegationOperators`2 IShiftOperators`3 IMinMaxValue`1 ISignedNumber`1")]
        [TestCase("Float", "IComparable IFormattable IComparable`1 IEquatable`1 IBitwiseOperators`3 INumber`1 IComparisonOperators`3 IEqualityOperators`3 IModulusOperators`3 INumberBase`1 IAdditionOperators`3 IAdditiveIdentity`2 IDecrementOperators`1 IDivisionOperators`3 IIncrementOperators`1 IMultiplicativeIdentity`2 IMultiplyOperators`3 IParsable`1 ISubtractionOperators`3 IUnaryPlusOperators`2 IUnaryNegationOperators`2 ISignedNumber`1 IMinMaxValue`1")]
        [TestCase("Decimal", "IFormattable IComparable IComparable`1 IEquatable`1 INumberBase`1 IAdditionOperators`3 IAdditiveIdentity`2 IDecrementOperators`1 IDivisionOperators`3 IEqualityOperators`3 IIncrementOperators`1 IMultiplicativeIdentity`2 IMultiplyOperators`3 IParsable`1 ISubtractionOperators`3 IUnaryPlusOperators`2 IUnaryNegationOperators`2 INumber`1 IComparisonOperators`3 IModulusOperators`3 ISignedNumber`1 IMinMaxValue`1")]
        [TestCase("Bool", "IComparable IComparable`1 IEquatable`1 IParsable`1")]
        [TestCase("String", "IComparable IComparable`1 IEquatable`1 IParsable`1")]
        [TestCase("Enum", "IComparable IFormattable")]
        [TestCase("Tuple", "IEquatable`1 IStructuralEquatable IStructuralComparable IComparable IComparable`1")]
        public void PrimitiveReflectionMatchesDotNet10Order(string kindName, string expected)
        {
            var kind = (NeoConformingTypeKind)System.Enum.Parse(typeof(NeoConformingTypeKind), kindName);
            SeedConformanceInterfaces();
            TypeInfo type = kind == NeoConformingTypeKind.Tuple ? TupleType(Type(MemberKind.Int), Type(MemberKind.String))
                : kind == NeoConformingTypeKind.Enum ? new EnumTypeInfo { type = MemberKind.Enum, required = true, enumId = NeoPrimitiveConformance.NumberStylesEnumId }
                : Type((MemberKind)System.Enum.Parse(typeof(MemberKind), kind.ToString()));
            var result = (object?[])Eval(new TypeMemberPointer
            {
                type = PointerKind.TypeMember,
                member = TypeMemberKind.GetInterfaces,
                receiver = Value(NeoTypeValue.Of(NeoTypeBindings.FromTypeInfo(type))),
                args = Array.Empty<Pointer>()
            })!;
            Assert.That(result.Cast<NeoTypeValue>().Select(value => value.Binding.Head!.InterfaceId),
                Is.EqualTo(expected.Split(' ').Select(NeoPrimitiveConformance.InterfaceId)));
        }

        [TestCase(MemberKind.Int, "16777216")]
        [TestCase(MemberKind.Float, "16777220")]
        public void DecimalConstructionUsesItsSourceType(MemberKind source, string expected)
        {
            Assert.That(Eval(new ConvertPointer { type = PointerKind.Convert, pointer = Value(16777216d), sourceType = Type(source), targetType = Type(MemberKind.Decimal) }), Is.EqualTo(expected));
        }

        [Test]
        public void FormatProviderIntrinsicReturnsOnlyNumberFormatInfo()
        {
            var primitive = new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = NeoObjectTypeKind.CultureInfo };
            var numberFormat = NeoTypeValue.Of(TypeBinding.Closed(new TypeHead(MemberKind.Object, true, objectType: NeoObjectTypeKind.NumberFormatInfo)));
            var pointer = new PrimitiveMemberPointer { type = PointerKind.PrimitiveMember, primitive = primitive, member = "GetFormat", receiver = Value(CultureInfo.InvariantCulture), args = new[] { Value(numberFormat) } };
            Assert.That(Eval(pointer), Is.SameAs(CultureInfo.InvariantCulture.NumberFormat));
            pointer.args = new[] { Value(null) };
            Assert.That(Eval(pointer), Is.Null);
            pointer.args = new[] { Value(NeoTypeValue.Of(Binding(MemberKind.Int))) };
            Assert.That(Eval(pointer), Is.Null);
            pointer.primitive = new ObjectTypeInfo { type = MemberKind.Object, required = true, objectType = NeoObjectTypeKind.NumberFormatInfo };
            pointer.receiver = Value(CultureInfo.InvariantCulture.NumberFormat);
            pointer.args = new[] { Value(numberFormat) };
            Assert.That(Eval(pointer), Is.SameAs(CultureInfo.InvariantCulture.NumberFormat));
        }

        [Test]
        public void TupleIntrinsicsUseElementSemanticsAndValidateStructuralComparerFirst()
        {
            var info = new TupleTypeInfo
            {
                type = MemberKind.Tuple,
                required = true,
                elements = new[]
            { new TupleElementTypeInfo { typeInfo = Type(MemberKind.Int) }, new TupleElementTypeInfo { typeInfo = Type(MemberKind.String) } }
            };
            var tuple = new RuntimeTuple(new object?[] { 1d, "a" }, info);
            var other = new RuntimeTuple(new object?[] { 1d, "b" }, info);
            var pointer = new PrimitiveMemberPointer { type = PointerKind.PrimitiveMember, primitive = info, member = "CompareTo", receiver = Value(tuple), args = new[] { Value(other) } };
            Assert.That(Eval(pointer), Is.EqualTo(-1));
            pointer.member = "Equals";
            pointer.args = new[] { Value(tuple) };
            Assert.That(Eval(pointer), Is.True);
            pointer.member = "CompareToObject";
            pointer.args = new[] { Value(NeoBox.Of(Binding(MemberKind.Int), 1)) };
            Assert.That(Assert.Throws<NeoRuntimeError>(() => Eval(pointer))!.Message, Is.EqualTo("Argument must be of type System.ValueTuple`2[System.Int32,System.String]. (Parameter 'other')"));
            pointer.member = "StructuralEquals";
            pointer.args = new[] { Value(null), Value(null) };
            Assert.That(Assert.Throws<NSGetterRuntimeError>(() => Eval(pointer))!.Payload.ClassId, Is.EqualTo(NeoErrorContract.NeoNullReferenceError));
        }

        [TestCase(2)]
        [TestCase(8)]
        [TestCase(9)]
        public void StructuralTupleMembersInvokeTheAuthoredComparer(int size)
        {
            const string comparerId = "b5-structural-comparer";
            var owner = new NeoSchemaClass { id = comparerId, name = "AuthoredComparer", projectId = "project-id", schema = new(), implementsInterfaces = new() };
            var invokers = new Dictionary<string, NeoClient.NeoNativeFunctionInvoker>();
            var calls = new List<object?>();
            foreach (string contract in new[] { "IEqualityComparer", "IComparer" })
            {
                string id = NeoPrimitiveConformance.InterfaceId(contract);
                owner.implementsInterfaces.Add(new InterfaceConstruction { interfaceId = id });
                foreach (var entry in client.ProjectDataForRuntime.interfaces[id].members)
                {
                    string memberId = "b5-structural-" + entry.Key;
                    owner.schema[entry.Key] = memberId;
                    client.ProjectDataForRuntime.members[memberId] = new FunctionMember
                    {
                        id = memberId,
                        name = entry.Key,
                        kind = MemberKind.Function,
                        returnTypeInfo = entry.Value.returnTypeInfo!,
                        argumentTypes = entry.Value.argumentTypes!.ToArray(),
                    };
                    string name = entry.Key;
                    invokers[memberId] = (_, _, args, _) =>
                    {
                        Assert.That(args[0], Is.InstanceOf<NeoBox>());
                        var box = (NeoBox)args[0]!;
                        calls.Add(box.ValueType.Head!.Type == MemberKind.Tuple ? "rest:" + ((RuntimeTuple)box.Payload!).Count : box.Payload);
                        return name == "Equals" ? (object)true : name == "Compare" ? 0 : 7;
                    };
                }
            }
            client.ProjectDataForRuntime.classes[comparerId] = owner;
            client.InvalidateSchemaResolutionCaches();
            client.RegisterNativeFunctionInvokers(invokers);
            using var comparer = NeoGeneratedTypesSupport.CreateWritableClassValue(client, comparerId, new Dictionary<string, string>(), Array.Empty<MemberValue>());
            object? comparerValue = NSGetterEvaluator.UnwrapRow(comparer.value!, context, comparer.ownership, comparer.member);
            var info = new TupleTypeInfo
            {
                type = MemberKind.Tuple,
                required = true,
                elements = Enumerable.Range(0, size).Select(_ => new TupleElementTypeInfo { typeInfo = Type(MemberKind.Int) }).ToArray()
            };
            var tuple = new RuntimeTuple(Enumerable.Range(1, size).Select(i => (object?)i).ToArray(), info);
            var boxTuple = NeoBox.Of(NeoTypeBindings.FromTypeInfo(info), tuple);
            var pointer = new PrimitiveMemberPointer
            {
                type = PointerKind.PrimitiveMember,
                primitive = info,
                receiver = Value(tuple),
                member = "StructuralEquals",
                args = new[] { Value(boxTuple), Value(comparerValue) }
            };
            if (size == 8)
            {
                Assert.That(Assert.Throws<NeoRuntimeError>(() => Eval(pointer))!.Message,
                    Is.EqualTo("A structural comparison of '(int, int, int, int, int, int, int, int)' passes its one-element rest as ValueTuple<T8>, which NeoScript has no value for."));
                Assert.That(calls.Count, Is.EqualTo(7));
            }
            else
            {
                Assert.That(Eval(pointer), Is.True);
                Assert.That(calls.Count, Is.EqualTo(Math.Min(size, 8)));
                if (size == 9)
                    Assert.That(calls[7], Is.EqualTo("rest:2"));
                calls.Clear();
                pointer.member = "StructuralCompareTo";
                Assert.That(Eval(pointer), Is.EqualTo(0));
                Assert.That(calls.Count, Is.EqualTo(Math.Min(size, 8)));
            }
            calls.Clear();
            pointer.member = "StructuralGetHashCode";
            pointer.args = new[] { Value(comparerValue) };
            Assert.That(Eval(pointer), Is.TypeOf<int>());
            Assert.That(calls, Is.EqualTo(size == 9 ? new object?[] { 8, 9, 2, 3, 4, 5, 6, 7 }
                : size == 8 ? new object?[] { 8, 1, 2, 3, 4, 5, 6, 7 } : new object?[] { 1, 2 }));
        }

        private void SeedConformanceInterfaces()
        {
            // Isolate closure behavior from fixture generation. Canonical interface IDs
            // are fixed; these test declarations use their own valid parameter IDs.
            foreach (var row in NeoPrimitiveConformance.Table.Values.SelectMany(rows => rows).GroupBy(row => row.InterfaceId).Select(group => group.First()))
                client.ProjectDataForRuntime.interfaces[row.InterfaceId] = new Interface
                {
                    id = row.InterfaceId,
                    name = "Conformance",
                    members = new(),
                    genericParams = Enumerable.Range(0, row.Arguments.Length).Select(index =>
                        new GenericParamDeclaration { id = "p" + index, name = "T" + index }).ToList(),
                };
            client.InvalidateSchemaResolutionCaches();
        }

        private sealed class CountingProvider : INeoAuthoredFormatProvider
        {
            internal int Calls;
            public object GetNumberFormat()
            {
                Calls++;
                return CultureInfo.InvariantCulture.NumberFormat;
            }
        }

        private static TypeBinding Binding(MemberKind kind) => TypeBinding.Closed(new TypeHead(kind, true));
        private static TypeInfo Type(MemberKind kind) => new PrimitiveTypeInfo { type = kind, required = true };
        private static Pointer Value(object? value) => new ValuePointer { type = PointerKind.Value, primitiveResolved = true, primitive = value };
        private static Pointer Local(string id) => new VariablePointer { type = PointerKind.Variable, variableId = id };
        private object? Eval(Pointer pointer) => NSGetterEvaluator.EvaluatePointer(pointer, scope, context);
        private object? Call(MemberKind kind, string member, params object?[] args) => Instance(kind, member, null, args);
        private object? Instance(MemberKind kind, string member, object? receiver, params object?[] args)
        {
            var pointers = new Pointer[args.Length];
            for (int i = 0; i < args.Length; i++)
                pointers[i] = Value(args[i]);
            return Eval(new PrimitiveMemberPointer
            {
                type = PointerKind.PrimitiveMember,
                primitive = Type(kind),
                member = member,
                receiver = receiver is null ? null : Value(receiver),
                args = pointers,
            });
        }
    }
}
