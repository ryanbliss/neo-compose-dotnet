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
