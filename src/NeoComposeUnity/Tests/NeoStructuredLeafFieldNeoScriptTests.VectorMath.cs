// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    public partial class NeoStructuredLeafFieldNeoScriptTests
    {
        private static FunctionPointer VectorMath(string kind, string op, params Pointer[] args) => new()
        {
            type = PointerKind.Function,
            function = new VectorOpFunction
            {
                type = FunctionKind.VectorOp,
                info = new FunctionVectorOpInfo { kind = kind, op = op, argPointers = args },
            },
        };

        private static FunctionPointer VectorInt(double x, double y) => new()
        {
            type = PointerKind.Function,
            function = new VectorConstructorFunction
            {
                type = FunctionKind.VectorConstructor,
                info = new FunctionVectorConstructorInfo
                {
                    vectorType = MemberKind.Vector2Int,
                    componentPointers = new Pointer[] { NumberLiteral(x), NumberLiteral(y) },
                },
            },
        };

        [Test]
        public void VectorArithmeticRebindLeavesAliasAndInputsUntouched()
        {
            var rebind = new AssignInstruction
            {
                type = InstructionKind.Assign,
                operatorValue = "=",
                target = new WriteTarget { pointer = LocalVariable("a"), typeInfo = new PrimitiveTypeInfo { type = MemberKind.Vector2Int, required = true }, writability = WritabilityKind.Runtime },
                pointer = VectorMath("Vector2Int", "add", LocalVariable("a"), VectorInt(3, 4)),
            };
            var mutateAlias = AssignLocalField("b", "x", NumberLiteral(6));
            NeoVector2Value a = RunLocalVectorProgram("a", rebind, mutateAlias);
            NeoVector2Value b = RunLocalVectorProgram("b", rebind, mutateAlias);
            Assert.AreEqual(4, a.x);
            Assert.AreEqual(6, a.y);
            Assert.AreEqual(6, b.x);
            Assert.AreEqual(2, b.y);
        }

        [Test]
        public void ConstantsAreFreshAndMinMaxPreserveSignedZeroOrdering()
        {
            NeoClient client = BuildClient();
            var ctx = ContextWithRoot(client, out _);
            var vectorType = new PrimitiveTypeInfo { type = MemberKind.Vector2, required = true };
            object? Evaluate(Pointer pointer)
            {
                var getter = Getter(pointer);
                getter.typeInfo = vectorType;
                return NSGetterEvaluator.Evaluate(getter, ctx);
            }
            var integerGetter = Getter(VectorInt(16777217, -16777217));
            integerGetter.typeInfo = new PrimitiveTypeInfo { type = MemberKind.Vector2Int, required = true };
            var integer = (NeoVector2Value)NSGetterEvaluator.Evaluate(integerGetter, ctx)!;
            Assert.AreEqual(16777217d, integer.x);
            Assert.AreEqual(-16777217d, integer.y);
            Pointer zero = VectorInt(0, 0);
            var first = (NeoVector2Value)Evaluate(zero)!;
            first.x = 9;
            Assert.AreEqual(0d, ((NeoVector2Value)Evaluate(zero)!).x);
            Pointer negativeZero = VectorMath("Vector2", "negate", zero);
            var minimum = (NeoVector2Value)Evaluate(VectorMath("Vector2", "min", negativeZero, zero))!;
            var maximum = (NeoVector2Value)Evaluate(VectorMath("Vector2", "max", zero, negativeZero))!;
            Assert.Less(BitConverter.DoubleToInt64Bits(minimum.x), 0);
            Assert.AreEqual(0L, BitConverter.DoubleToInt64Bits(maximum.x));
        }

        [Test]
        public void IntegerPrecisionSurvivesLocalMutationAndArithmetic()
        {
            NeoVector2Value result = RunLocalVectorProgram("a",
                AssignLocalField("a", "x", NumberLiteral(16777217)),
                AssignLocalField("b", "y", KeyOf(VectorMath("Vector2Int", "add", LocalVariable("a"), VectorInt(2, 0)), "x")));
            Assert.AreEqual(16777217d, result.x);
            Assert.AreEqual(16777219d, result.y);
        }

        [Test]
        public void StoredVectorMathTracksAndReadsComponentWrites()
        {
            NeoClient client = BuildClient();
            var ctx = ContextWithRoot(client, out Dictionary<string, object?> scope);
            var getter = Getter(VectorMath("Vector2Int", "sqrMagnitude", LeafPointer("Cell")));
            getter.typeInfo = IntType();
            var enclosing = client.BeginGetterReadCapture();
            Assert.AreEqual(20d, NSGetterEvaluator.Evaluate(getter, ctx));
            var capture = client.EndGetterReadCapture(enclosing);
            Assert.IsTrue(capture.reads!.Contains("value-cell"));
            client.RecycleGetterCapture(capture);
            NeoScriptExecutor.Execute(client,
                Action(AssignInstruction("Cell", "x", NumberLiteral(16777217), IntType())), scope, ctx);
            Assert.AreEqual(16777217d, RequireVector2(client, "value-cell").x);
            Assert.AreEqual(281475010265105d, NSGetterEvaluator.Evaluate(getter, ctx));
        }

        [Test]
        public void OperandComponentsAreCapturedBeforeFollowingSideEffects()
        {
            NeoClient client = BuildClient();
            var ctx = ContextWithRoot(client, out Dictionary<string, object?> scope);
            var right = new ObjectInitializerPointer
            {
                type = PointerKind.ObjectInitializer,
                receiver = new Variable
                {
                    id = "rhs",
                    typeInfo = new PrimitiveTypeInfo { type = MemberKind.Vector2Int, required = true },
                    pointer = VectorInt(0, 0),
                },
                assignments = new[] { AssignInstruction("Cell", "x", Add(LeafFieldPointer("Cell", "x"), NumberLiteral(1)), IntType()) },
            };
            var body = Action(new ReturnInstruction { type = InstructionKind.Return, pointer = VectorMath("Vector2Int", "add", LeafPointer("Cell"), right) });
            body.typeInfo = new PrimitiveTypeInfo { type = MemberKind.Vector2Int, required = true };
            var result = (NeoVector2Value)NeoScriptExecutor.Execute(client, body, scope, ctx).ReturnValue!;
            Assert.AreEqual(2d, result.x);
            Assert.AreEqual(3d, RequireVector2(client, "value-cell").x);
            Assert.IsTrue(NeoScriptIrWalker.MayCall(body.instructions[0]), "The walker must see the nested initializer.");
        }

        [TestCase("lerp")]
        [TestCase("moveTowards")]
        public void EndpointMathReturnsFreshValues(string op)
        {
            NeoClient client = BuildClient();
            var ctx = ContextWithRoot(client, out Dictionary<string, object?> scope);
            var vectorType = new PrimitiveTypeInfo { type = MemberKind.Vector3, required = true };
            var body = Action(
                new VariableInstruction
                {
                    type = InstructionKind.Variable,
                    variable = new Variable
                    {
                        id = "result",
                        typeInfo = vectorType,
                        pointer = VectorMath("Vector3", op, LeafPointer("Position"), LeafPointer("Position"), NumberLiteral(1)),
                    }
                },
                new AssignInstruction
                {
                    type = InstructionKind.Assign,
                    operatorValue = "=",
                    target = new WriteTarget { pointer = KeyOf(LocalVariable("result"), "x"), typeInfo = FloatType(), writability = WritabilityKind.Runtime },
                    pointer = NumberLiteral(99),
                });
            NeoScriptExecutor.Execute(client, body, scope, ctx);
            Assert.AreEqual(1d, RequireVector3(client, "value-position").x);
        }
    }
}
