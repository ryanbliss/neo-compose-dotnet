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
    /// P127 §3.1: int is Int32 in the evaluator. Int arithmetic is exact and
    /// wraps unchecked, <c>/</c> truncates, division by zero and
    /// <c>int.MinValue / -1</c> throw, an int has no <c>-0</c>, and
    /// <c>Sum</c> over ints is checked while <c>Average</c> totals in 64 bits.
    /// </summary>
    public class P127TypesIntegerTests
    {
        private const double MaxValue = int.MaxValue;
        private const double MinValue = int.MinValue;

        [TestCase("+", MaxValue, 1d, MinValue)]
        [TestCase("-", MinValue, 1d, MaxValue)]
        [TestCase("*", 46341d, 46341d, -2147479015d)]
        // 2147483647² is past 2^53, so a double product loses the low bits that decide the wrap.
        [TestCase("*", MaxValue, MaxValue, 1d)]
        [TestCase("*", MinValue, -1d, MinValue)]
        [TestCase("/", -7d, 2d, -3d)]
        [TestCase("%", -7d, 2d, -1d)]
        [TestCase("%", 7d, -2d, 1d)]
        public void IntArithmeticWrapsExactly(string op, double left, double right, double expected)
        {
            Assert.That(EvaluateInt(op, left, right), Is.EqualTo(expected));
        }

        [TestCase("+", MaxValue, 1d)]
        [TestCase("-", MinValue, 1d)]
        [TestCase("*", 65536d, 32768d)]
        // Unary minus lowers to x * -1.
        [TestCase("*", MinValue, -1d)]
        public void CheckedIntArithmeticOverflows(string op, double left, double right)
        {
            var error = Fails(() => EvaluateReturn(
                Arithmetic(op, ArithmeticNumeric.Int, Number(left), Number(right), isChecked: true),
                MemberKind.Int));
            Assert.That(error.ErrorClass, Is.EqualTo(NeoErrorClass.NeoOverflowError));
            Assert.That(error.Message, Is.EqualTo("Arithmetic operation resulted in an overflow."));
        }

        [Test]
        public void CheckedIntArithmeticInRangeIsExact()
        {
            object? result = EvaluateReturn(
                Arithmetic("*", ArithmeticNumeric.Int, Number(46340d), Number(46340d), isChecked: true),
                MemberKind.Int);
            Assert.That(result, Is.EqualTo(2147395600d));
        }

        [Test]
        public void IntMathAbsOfMinValueOverflows()
        {
            var error = Fails(() => EvaluateReturn(MathAbs(MinValue, ArithmeticNumeric.Int), MemberKind.Int));
            Assert.That(error.ErrorClass, Is.EqualTo(NeoErrorClass.NeoOverflowError));
            Assert.That(error.Message, Is.EqualTo("Negating the minimum value of a twos complement number is invalid."));
            Assert.That(EvaluateReturn(MathAbs(-5d, ArithmeticNumeric.Int), MemberKind.Int), Is.EqualTo(5d));
            // The float overload has no int range.
            Assert.That(EvaluateReturn(MathAbs(MinValue, null), MemberKind.Float), Is.EqualTo(-MinValue));
        }

        [TestCase("*", 0d, -1d)]
        [TestCase("/", 0d, -5d)]
        [TestCase("%", -4d, 2d)]
        [TestCase("-", 0d, 0d)]
        public void IntResultHasNoNegativeZero(string op, double left, double right)
        {
            object? result = EvaluateInt(op, left, right);
            Assert.That(result, Is.EqualTo(0d));
            Assert.That(double.IsNegative((double)result!), Is.False);
        }

        [TestCase("/", 7d, 0d, "Division by zero")]
        [TestCase("%", 7d, 0d, "Modulo by zero")]
        public void IntDivisionByZeroThrows(string op, double left, double right, string message)
        {
            var error = Fails(() => EvaluateInt(op, left, right));
            Assert.That(error.ErrorClass, Is.EqualTo(NeoErrorClass.NeoDivideByZeroError));
            Assert.That(error.Message, Is.EqualTo(message));
        }

        [TestCase("/")]
        [TestCase("%")]
        public void MinValueOverMinusOneOverflowsUnchecked(string op)
        {
            var error = Fails(() => EvaluateInt(op, MinValue, -1d));
            Assert.That(error.ErrorClass, Is.EqualTo(NeoErrorClass.NeoOverflowError));
            Assert.That(error.Message, Is.EqualTo("Arithmetic operation resulted in an overflow."));
        }

        [Test]
        public void FloatArithmeticKeepsFloatRounding()
        {
            object? result = EvaluateReturn(
                Arithmetic("/", ArithmeticNumeric.Float, Number(1d), Number(3d)),
                MemberKind.Float);
            Assert.That(result, Is.EqualTo((double)(1f / 3f)));
        }

        [Test]
        public void IntSumIsChecked()
        {
            var error = Fails(() => EvaluateQuery("Sum", MaxValue, 1d));
            Assert.That(error.ErrorClass, Is.EqualTo(NeoErrorClass.NeoOverflowError));
            Assert.That(error.Message, Is.EqualTo("Arithmetic operation resulted in an overflow."));
            Assert.That(EvaluateQuery("Sum", MaxValue, MinValue), Is.EqualTo(-1d));
        }

        [Test]
        public void IntAverageTotalsInSixtyFourBits()
        {
            Assert.That(EvaluateQuery("Average", MaxValue, MaxValue), Is.EqualTo((double)(float)MaxValue));
            Assert.That(EvaluateQuery("Average", MinValue, MinValue, 1d), Is.EqualTo((double)(float)((2d * MinValue + 1d) / 3d)));
        }

        [Test]
        public void IntVectorComponentsWrap()
        {
            object? sum = EvaluateReturn(
                VectorOp("add", Vector2Int(MaxValue, 0d), Vector2Int(1d, MinValue)),
                MemberKind.Vector2Int);
            Assert.That(sum, Is.InstanceOf<NeoVector2Value>());
            Assert.That(((NeoVector2Value)sum!).x, Is.EqualTo(MinValue));
            // 2147483647² wraps to 1, plus 1².
            object? square = EvaluateReturn(
                VectorOp("sqrMagnitude", Vector2Int(MaxValue, 1d)),
                MemberKind.Int);
            Assert.That(square, Is.EqualTo(2d));
        }

        [Test]
        public void IntVectorDivisionByZeroThrowsDivideByZero()
        {
            var error = Fails(() => EvaluateReturn(
                VectorOp("divide", Vector2Int(4d, 2d), Number(0d)),
                MemberKind.Vector2Int));
            Assert.That(error.ErrorClass, Is.EqualTo(NeoErrorClass.NeoDivideByZeroError));
            Assert.That(error.Message, Is.EqualTo("Division by zero"));
        }

        [Test]
        public void BitwiseOperandsWrapToInt32()
        {
            object? result = EvaluateReturn(
                new FunctionPointer
                {
                    type = PointerKind.Function,
                    function = new BitwiseOpFunction
                    {
                        type = FunctionKind.BitwiseOp,
                        info = new FunctionBitwiseOpInfo
                        {
                            op = "or",
                            argPointers = new[] { Number(4294967297d), Number(0d) },
                        },
                    },
                },
                MemberKind.Int);
            Assert.That(result, Is.EqualTo(1d));
        }

        /// <summary>The runtime error a failing evaluation reports, under its NeoScript frames.</summary>
        private static NeoRuntimeError Fails(TestDelegate code)
        {
            var error = Assert.Throws<NeoScriptException>(code)!;
            Assert.That(error.InnerException, Is.InstanceOf<NeoRuntimeError>());
            return (NeoRuntimeError)error.InnerException!;
        }

        private static object? EvaluateInt(string op, double left, double right) =>
            EvaluateReturn(Arithmetic(op, ArithmeticNumeric.Int, Number(left), Number(right)), MemberKind.Int);

        private static object? EvaluateQuery(string op, params double[] entries)
        {
            var literals = new JArray();
            foreach (double entry in entries)
                literals.Add(JObject.FromObject(new
                {
                    type = "value",
                    value = new
                    {
                        typeInfo = IntType(),
                        value = entry
                    }
                }));
            string json = new JObject
            {
                ["type"] = "collectionQuery",
                ["info"] = new JObject
                {
                    ["collectionPointer"] = new JObject
                    {
                        ["type"] = "sequenceConversion",
                        ["operation"] = "queryEnumerable",
                        ["pointer"] = new JObject
                        {
                            ["type"] = "listLiteral",
                            ["typeInfo"] = new JObject { ["type"] = (int)MemberKind.List, ["required"] = true, ["entryTypeInfo"] = IntType() },
                            ["entries"] = literals,
                        },
                        ["elementType"] = IntType(),
                    },
                    ["op"] = op,
                    ["keyType"] = "int",
                    ["nullable"] = false,
                },
            }.ToString();
            var function = JsonConvert.DeserializeObject<Function>(json)!;
            return EvaluateReturn(
                new FunctionPointer { type = PointerKind.Function, function = function },
                op == "Average" ? MemberKind.Float : MemberKind.Int);
        }

        private static JObject IntType() => new JObject { ["type"] = (int)MemberKind.Int, ["required"] = true };

        private static object? EvaluateReturn(Pointer pointer, MemberKind returnType)
        {
            using NeoClient client = BuildClient();
            return NSGetterEvaluator.Evaluate(
                new FunctionWithReturnType
                {
                    compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                    parameters = new Variable[0],
                    typeInfo = new PrimitiveTypeInfo { type = returnType, required = true },
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction { type = InstructionKind.Return, pointer = pointer },
                    },
                },
                new NSGetterEvaluator.Context(client, null, null));
        }

        private static Pointer Number(double value) => new ValuePointer
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Int, required = true },
                value = JToken.FromObject(value),
            },
        };

        private static Pointer Vector2Int(double x, double y) => new ValuePointer
        {
            type = PointerKind.Value,
            value = new Value
            {
                typeInfo = new PrimitiveTypeInfo { type = MemberKind.Vector2Int, required = true },
                value = new JObject { ["x"] = x, ["y"] = y },
            },
        };

        private static Pointer Arithmetic(string op, ArithmeticNumeric numeric, Pointer left, Pointer right, bool isChecked = false) => new OperationPointer
        {
            type = PointerKind.Operation,
            operation = new ArithmeticOperation
            {
                type = OperationKind.Arithmetic,
                arithmetic = new ArithmeticOpInfo
                {
                    type = op,
                    pointers = new[] { left, right },
                    numeric = numeric,
                    isChecked = isChecked ? true : null,
                },
            },
        };

        private static Pointer MathAbs(double value, ArithmeticNumeric? numeric) => new FunctionPointer
        {
            type = PointerKind.Function,
            function = new MathOpFunction
            {
                type = FunctionKind.MathOp,
                info = new FunctionMathOpInfo
                {
                    op = MathOpKind.Abs,
                    argPointers = new[] { Number(value) },
                    numeric = numeric,
                },
            },
        };

        private static Pointer VectorOp(string op, params Pointer[] arguments) => new FunctionPointer
        {
            type = PointerKind.Function,
            function = new VectorOpFunction
            {
                type = FunctionKind.VectorOp,
                info = new FunctionVectorOpInfo
                {
                    kind = "Vector2Int",
                    op = op,
                    argPointers = arguments,
                },
            },
        };

        private static NeoClient BuildClient()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
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
            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "P127 ints",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = members,
                values = values,
                classes = new Dictionary<string, NeoSchemaClass> { [rootClass.id] = rootClass },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            }.WithUserRoot());
        }
    }
}
