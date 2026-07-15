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
    /// NSGetterEvaluator decimal wiring (specs/decimal-member.md
    /// decision 7 / §6.4), mirroring the web evaluator's semantics:
    /// decimal-stamped arithmetic/comparisons route through
    /// <see cref="NeoDecimalMath"/> over canonical strings, the
    /// <c>decimalOp</c> builtins evaluate here, Int operands widen exactly
    /// at the three seams (variable init / assignment / top-level return),
    /// and unstamped string arithmetic still concatenates. Also covers the
    /// <c>stringOp</c> function kind (a pre-existing parity gap — the web
    /// evaluator supported it but the dotnet converter had no variant).
    /// </summary>
    public class NeoDecimalNSGetterTests
    {
        // ------------------------------------------------------------------
        // IR deserialization — the wire `decimal` flag and function kinds.
        // ------------------------------------------------------------------

        [Test]
        public void Json_ArithmeticDecimalFlag_RoundTrips()
        {
            var operation = JsonConvert.DeserializeObject<Operation>(
                @"{
                    ""type"": ""arithmetic"",
                    ""arithmetic"": {
                        ""type"": ""+"",
                        ""decimal"": true,
                        ""pointers"": []
                    }
                }");
            Assert.IsInstanceOf<ArithmeticOperation>(operation);
            Assert.AreEqual(true, ((ArithmeticOperation)operation!).arithmetic.isDecimal);

            var condition = JsonConvert.DeserializeObject<Condition>(
                @"{
                    ""type"": ""equalTo"",
                    ""decimal"": true,
                    ""operand1"": { ""type"": ""variable"", ""variableId"": ""a"" },
                    ""operand2"": { ""type"": ""variable"", ""variableId"": ""b"" }
                }");
            Assert.AreEqual(true, condition!.isDecimal);
        }

        [Test]
        public void Json_DecimalOpAndStringOpFunctions_Deserialize()
        {
            var decimalOp = JsonConvert.DeserializeObject<Function>(
                @"{
                    ""type"": ""decimalOp"",
                    ""info"": {
                        ""op"": ""divide"",
                        ""receiverPointer"": { ""type"": ""variable"", ""variableId"": ""a"" },
                        ""argPointer"": { ""type"": ""variable"", ""variableId"": ""b"" },
                        ""digitsPointer"": { ""type"": ""variable"", ""variableId"": ""d"" }
                    }
                }");
            Assert.IsInstanceOf<DecimalOpFunction>(decimalOp);
            Assert.AreEqual(DecimalOpKind.Divide, ((DecimalOpFunction)decimalOp!).info.op);

            var stringOp = JsonConvert.DeserializeObject<Function>(
                @"{
                    ""type"": ""stringOp"",
                    ""info"": {
                        ""op"": ""startsWith"",
                        ""receiverPointer"": { ""type"": ""variable"", ""variableId"": ""s"" },
                        ""argPointer"": { ""type"": ""variable"", ""variableId"": ""p"" }
                    }
                }");
            Assert.IsInstanceOf<StringOpFunction>(stringOp);
            Assert.AreEqual(StringOpKind.StartsWith, ((StringOpFunction)stringOp!).info.op);
        }

        // ------------------------------------------------------------------
        // Decimal-stamped arithmetic.
        // ------------------------------------------------------------------

        [Test]
        public void Arithmetic_StampedAddition_IsExactNotConcat()
        {
            Assert.AreEqual(
                "0.3",
                EvaluateReturn(
                    DecimalArithmetic(
                        ArithmeticOpKind.Addition,
                        DecimalLiteral("0.1"),
                        DecimalLiteral("0.2"))));
        }

        [Test]
        public void Arithmetic_UnstampedStringAddition_StillConcatenates()
        {
            // Regression guard: without the decimal stamp, `+` over two
            // strings is concatenation — "0.1" + "0.2" is "0.10.2".
            Assert.AreEqual(
                "0.10.2",
                EvaluateReturn(
                    Arithmetic(
                        ArithmeticOpKind.Addition,
                        isDecimal: null,
                        StringLiteral("0.1"),
                        StringLiteral("0.2")),
                    MemberKind.String));
        }

        [Test]
        public void Arithmetic_IntOperandWidensExactly()
        {
            Assert.AreEqual(
                "3.0",
                EvaluateReturn(
                    DecimalArithmetic(
                        ArithmeticOpKind.Multiplication,
                        NumberLiteral(2.0),
                        DecimalLiteral("1.5"))));
        }

        [Test]
        public void Arithmetic_OverflowSurfacesAsRuntimeError()
        {
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                EvaluateReturn(
                    DecimalArithmetic(
                        ArithmeticOpKind.Addition,
                        DecimalLiteral(new string('9', 28)),
                        DecimalLiteral("1"))));
            StringAssert.Contains("Decimal overflow", error!.Message);
        }

        // ------------------------------------------------------------------
        // Decimal-stamped comparisons.
        // ------------------------------------------------------------------

        [Test]
        public void Condition_StampedEqualityIsScaleBlind()
        {
            Assert.AreEqual(
                true,
                EvaluateCondition(OperatorKind.EqualTo, DecimalLiteral("1.10"), DecimalLiteral("1.1")));
            Assert.AreEqual(
                false,
                EvaluateCondition(OperatorKind.DoesNotEqual, DecimalLiteral("1.10"), DecimalLiteral("1.1")));
        }

        [Test]
        public void Condition_StampedOrderingIsExact()
        {
            // float64 would round both sides to the same double; exact
            // decimal comparison sees the difference.
            Assert.AreEqual(
                true,
                EvaluateCondition(
                    OperatorKind.GreaterThan,
                    DecimalLiteral("0.3"),
                    DecimalLiteral("0.29999999999999999")));
        }

        [Test]
        public void Condition_OrderingAgainstNullThrows()
        {
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                EvaluateCondition(
                    OperatorKind.GreaterThan,
                    DecimalLiteral("1"),
                    NullLiteral()));
            StringAssert.Contains("null operand", error!.Message);
        }

        [Test]
        public void Condition_NullEqualitySemantics()
        {
            Assert.AreEqual(
                true,
                EvaluateCondition(OperatorKind.EqualTo, NullLiteral(), NullLiteral()));
            Assert.AreEqual(
                true,
                EvaluateCondition(OperatorKind.DoesNotEqual, DecimalLiteral("1"), NullLiteral()));
        }

        // ------------------------------------------------------------------
        // decimalOp builtins.
        // ------------------------------------------------------------------

        [Test]
        public void DecimalOp_Round_IsHalfEven()
        {
            Assert.AreEqual("2", EvaluateReturn(DecimalOp(
                DecimalOpKind.Round, DecimalLiteral("2.5"), digits: 0)));
            Assert.AreEqual("4", EvaluateReturn(DecimalOp(
                DecimalOpKind.Round, DecimalLiteral("3.5"), digits: 0)));
        }

        [Test]
        public void DecimalOp_Divide_RoundsHalfEvenToDigits()
        {
            Assert.AreEqual("0.33333", EvaluateReturn(DecimalOp(
                DecimalOpKind.Divide,
                DecimalLiteral("1"),
                digits: 5,
                arg: DecimalLiteral("3"))));
        }

        [Test]
        public void DecimalOp_DivideByZero_SurfacesAsRuntimeError()
        {
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                EvaluateReturn(DecimalOp(
                    DecimalOpKind.Divide,
                    DecimalLiteral("1"),
                    digits: 5,
                    arg: DecimalLiteral("0"))));
            StringAssert.Contains("division by zero", error!.Message);
        }

        [Test]
        public void DecimalOp_ToDecimal_ExpandsTheExactDouble()
        {
            Assert.AreEqual(
                "0.10000000000000000555",
                EvaluateReturn(DecimalOp(
                    DecimalOpKind.ToDecimal, NumberLiteral(0.1), digits: 20)));
        }

        [Test]
        public void DecimalOp_ToFloat_ReturnsNearestDouble()
        {
            Assert.AreEqual(
                0.5d,
                EvaluateReturn(
                    DecimalOp(DecimalOpKind.ToFloat, DecimalLiteral("0.5")),
                    MemberKind.Float));
        }

        [Test]
        public void DecimalOp_NullReceiverThrowsNamingTheOp()
        {
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                EvaluateReturn(DecimalOp(
                    DecimalOpKind.Round, NullLiteral(), digits: 0)));
            StringAssert.Contains("round receiver is null", error!.Message);
        }

        // ------------------------------------------------------------------
        // The three Int→Decimal widening seams.
        // ------------------------------------------------------------------

        [Test]
        public void Seam_VariableInitialization_CoercesIntToCanonicalString()
        {
            var getter = new FunctionWithReturnType
            {
                parameters = new Variable[0],
                typeInfo = DecimalTypeInfo(),
                instructions = new Instruction[]
                {
                    new VariableInstruction
                    {
                        type = InstructionKind.Variable,
                        variable = new Variable
                        {
                            id = "x",
                            typeInfo = DecimalTypeInfo(),
                            pointer = NumberLiteral(3.0),
                        },
                    },
                    Return(VariableRef("x")),
                },
            };

            Assert.AreEqual("3", Evaluate(getter));
        }

        [Test]
        public void Seam_Assignment_CoercesIntToCanonicalString()
        {
            var getter = new FunctionWithReturnType
            {
                parameters = new Variable[0],
                typeInfo = DecimalTypeInfo(),
                instructions = new Instruction[]
                {
                    new VariableInstruction
                    {
                        type = InstructionKind.Variable,
                        variable = new Variable
                        {
                            id = "x",
                            typeInfo = DecimalTypeInfo(),
                            pointer = DecimalLiteral("1"),
                        },
                    },
                    new AssignInstruction
                    {
                        type = InstructionKind.Assign,
                        operatorValue = "=",
                        target = new WriteTarget
                        {
                            pointer = VariableRef("x"),
                            typeInfo = DecimalTypeInfo(),
                            writability = WritabilityKind.Local,
                        },
                        pointer = NumberLiteral(5.0),
                    },
                    Return(VariableRef("x")),
                },
            };

            Assert.AreEqual("5", Evaluate(getter));
        }

        [Test]
        public void Seam_TopLevelReturn_CoercesIntToCanonicalString()
        {
            Assert.AreEqual(
                "7",
                EvaluateReturn(NumberLiteral(7.0)));
        }

        [Test]
        public void Seam_NonIntegerNumberThrows()
        {
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                EvaluateReturn(NumberLiteral(7.5)));
            StringAssert.Contains("ToDecimal(digits)", error!.Message);
        }

        // ------------------------------------------------------------------
        // stringOp (the pre-existing parity-gap fix).
        // ------------------------------------------------------------------

        [Test]
        public void StringOp_ToLowerAndStartsWith_Evaluate()
        {
            Assert.AreEqual(
                "hello",
                EvaluateReturn(
                    StringOp(StringOpKind.ToLower, StringLiteral("HeLLo")),
                    MemberKind.String));
            Assert.AreEqual(
                true,
                EvaluateReturn(
                    StringOp(StringOpKind.StartsWith, StringLiteral("hello"), StringLiteral("he")),
                    MemberKind.Bool));
        }

        [Test]
        public void StringOp_NullReceiverThrows()
        {
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                EvaluateReturn(
                    StringOp(StringOpKind.Trim, NullLiteral()),
                    MemberKind.String));
            StringAssert.Contains("receiver must be a string", error!.Message);
        }

        // ------------------------------------------------------------------
        // Harness — minimal client + POCO IR builders.
        // ------------------------------------------------------------------

        private static NeoClient BuildClient()
        {
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            return NeoTestSaveStack.ClientFromSchema(new ProjectData
            {
                project = new Project
                {
                    id = "project-a",
                    _id = "project-a",
                    name = "Decimal NSGetters",
                    rootAssetsMemberId = "root-assets",
                    rootSaveFileMemberId = "root-save",
                    rootSessionMemberId = "root-session",
                },
                members = new Dictionary<string, NeoCompose.Runtime.Json.Member>
                {
                    ["root-assets"] = RootMember("root-assets", "root-assets-value", rootClass.id),
                    ["root-save"] = RootMember("root-save", "root-save-value", rootClass.id),
                    ["root-session"] = RootMember("root-session", "root-session-value", rootClass.id),
                },
                values = new Dictionary<string, MemberValue>
                {
                    ["root-assets-value"] = ObjectValue("root-assets-value", rootClass.id),
                    ["root-save-value"] = ObjectValue("root-save-value", rootClass.id),
                    ["root-session-value"] = ObjectValue("root-session-value", rootClass.id),
                },
                classes = new Dictionary<string, NeoSchemaClass>
                {
                    [rootClass.id] = rootClass,
                },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            });
        }

        private static ClassMember RootMember(string id, string valueId, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-a",
                name = id,
                kind = MemberKind.Class,
                required = true,
                valueId = valueId,
                classId = classId,
            };
        }

        private static ObjectMemberValue ObjectValue(string id, string classId)
        {
            return new ObjectMemberValue
            {
                id = id,
                classId = classId,
                value = new Dictionary<string, string>(),
            };
        }

        private static object? Evaluate(FunctionWithReturnType getter)
        {
            return NSGetterEvaluator.Evaluate(
                getter,
                new NSGetterEvaluator.Context(BuildClient(), null, null));
        }

        /// <summary>Evaluates `return &lt;pointer&gt;;` as a getter of the given type.</summary>
        private static object? EvaluateReturn(
            Pointer pointer,
            MemberKind returnType = MemberKind.Decimal)
        {
            return Evaluate(new FunctionWithReturnType
            {
                parameters = new Variable[0],
                typeInfo = new PrimitiveTypeInfo { type = returnType, required = true },
                instructions = new Instruction[] { Return(pointer) },
            });
        }

        /// <summary>Evaluates a decimal-stamped condition to a bool.</summary>
        private static object? EvaluateCondition(string op, Pointer a, Pointer b)
        {
            return EvaluateReturn(
                new OperationPointer
                {
                    type = PointerKind.Operation,
                    operation = new BooleanOperation
                    {
                        type = OperationKind.Boolean,
                        expression = new BooleanExpression
                        {
                            condition = new Condition
                            {
                                type = op,
                                operand1 = a,
                                operand2 = b,
                                isDecimal = true,
                            },
                        },
                    },
                },
                MemberKind.Bool);
        }

        private static ReturnInstruction Return(Pointer pointer)
        {
            return new ReturnInstruction
            {
                type = InstructionKind.Return,
                pointer = pointer,
            };
        }

        private static VariablePointer VariableRef(string id)
        {
            return new VariablePointer
            {
                type = PointerKind.Variable,
                variableId = id,
            };
        }

        private static PrimitiveTypeInfo DecimalTypeInfo()
        {
            return new PrimitiveTypeInfo { type = MemberKind.Decimal, required = true };
        }

        private static ValuePointer Literal(MemberKind type, JToken? value)
        {
            return new ValuePointer
            {
                type = PointerKind.Value,
                value = new Value
                {
                    typeInfo = new PrimitiveTypeInfo { type = type, required = true },
                    value = value,
                },
            };
        }

        private static Pointer DecimalLiteral(string value) =>
            Literal(MemberKind.Decimal, JToken.FromObject(value));

        private static Pointer StringLiteral(string value) =>
            Literal(MemberKind.String, JToken.FromObject(value));

        private static Pointer NumberLiteral(double value) =>
            Literal(MemberKind.Int, JToken.FromObject(value));

        private static Pointer NullLiteral() =>
            Literal(MemberKind.Null, JValue.CreateNull());

        private static Pointer Arithmetic(string op, bool? isDecimal, params Pointer[] operands)
        {
            return new OperationPointer
            {
                type = PointerKind.Operation,
                operation = new ArithmeticOperation
                {
                    type = OperationKind.Arithmetic,
                    arithmetic = new ArithmeticOpInfo
                    {
                        type = op,
                        pointers = operands,
                        isDecimal = isDecimal,
                    },
                },
            };
        }

        private static Pointer DecimalArithmetic(string op, params Pointer[] operands) =>
            Arithmetic(op, isDecimal: true, operands);

        private static Pointer DecimalOp(
            string op,
            Pointer receiver,
            int? digits = null,
            Pointer? arg = null)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new DecimalOpFunction
                {
                    type = FunctionKind.DecimalOp,
                    info = new FunctionDecimalOpInfo
                    {
                        op = op,
                        receiverPointer = receiver,
                        argPointer = arg,
                        digitsPointer = digits is null
                            ? null
                            : Literal(MemberKind.Int, JToken.FromObject((double)digits.Value)),
                    },
                },
            };
        }

        private static Pointer StringOp(string op, Pointer receiver, Pointer? arg = null)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new StringOpFunction
                {
                    type = FunctionKind.StringOp,
                    info = new FunctionStringOpInfo
                    {
                        op = op,
                        receiverPointer = receiver,
                        argPointer = arg,
                    },
                },
            };
        }
    }
}
