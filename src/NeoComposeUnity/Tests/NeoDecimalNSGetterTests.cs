// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Linq;
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
        public void Arithmetic_StringAdditionFormatsNumbersAsTheirInvariantText()
        {
            // A whole number formats through an integer fast path; its text
            // must stay the double's own, sign of zero and E notation included.
            foreach (double number in new[] { 7d, -0d, 2.5, 1e15, 999999999999999d })
            {
                string text = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Assert.AreEqual(
                    "n=" + text,
                    EvaluateReturn(
                        Arithmetic(
                            ArithmeticOpKind.Addition,
                            isDecimal: null,
                            StringLiteral("n="),
                            NumberLiteral(number)),
                        MemberKind.String));
                Assert.AreEqual(
                    text + "|" + text,
                    EvaluateReturn(
                        Arithmetic(
                            ArithmeticOpKind.Addition,
                            isDecimal: null,
                            NumberLiteral(number),
                            StringLiteral("|"),
                            NumberLiteral(number)),
                        MemberKind.String));
            }
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
            // decimal.MaxValue + 1 overflows System.Decimal's 96-bit coefficient (P127 §3.4).
            var error = Assert.Catch<NSGetterRuntimeError>(() =>
                EvaluateReturn(
                    DecimalArithmetic(
                        ArithmeticOpKind.Addition,
                        DecimalLiteral("79228162514264337593543950335"),
                        DecimalLiteral("1"))));
            Assert.AreEqual("Value was either too large or too small for a Decimal.", error!.Message);
        }

        [Test]
        public void Arithmetic_RoundsLikeSystemDecimal()
        {
            // The exact sum needs 30 digits, so the scale drops to 27 (P127 §3.4).
            Assert.AreEqual(
                "10.000000000000000000000000000",
                EvaluateReturn(
                    DecimalArithmetic(
                        ArithmeticOpKind.Addition,
                        DecimalLiteral("10"),
                        DecimalLiteral("0.0000000000000000000000000001"))));
        }

        [TestCase("/", "1", "3", "0.3333333333333333333333333333")]
        [TestCase("%", "7.5", "2", "1.5")]
        [TestCase("%", "-7.5", "2", "-1.5")]
        [TestCase("%", "7.5", "-2", "1.5")]
        public void Arithmetic_DecimalDivisionAndRemainderReadLocals(string op, string left, string right, string expected)
        {
            Assert.That(EvaluateDecimalLocals(op, left, right), Is.EqualTo(expected));
        }

        [TestCase("/", "8", "2", "2", "2")]
        [TestCase("%", "7.5", "2", "1", "0.5")]
        public void Arithmetic_DecimalDivisionAndRemainderFoldLeft(string op, string first, string second, string third, string expected)
        {
            Assert.That(EvaluateDecimalLocals(op, first, second, third), Is.EqualTo(expected));
        }

        [Test]
        public void Arithmetic_DecimalQuotientOverflowKeepsTypedErrorAndMessage()
        {
            var failure = Assert.Throws<NeoScriptException>(() =>
                EvaluateDecimalLocals("/", "79228162514264337593543950335", "0.1"))!;
            Assert.That(failure.InnerException, Is.InstanceOf<NeoRuntimeError>());
            var error = (NeoRuntimeError)failure.InnerException!;
            Assert.That(error.ErrorClass, Is.EqualTo(NeoErrorClass.NeoOverflowError));
            Assert.That(error.Message, Is.EqualTo("Value was either too large or too small for a Decimal."));
        }

        private static object? EvaluateDecimalLocals(string op, params string[] values)
        {
            var instructions = new List<Instruction>();
            var operands = new Pointer[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                string id = "operand" + i;
                instructions.Add(new VariableInstruction
                {
                    type = InstructionKind.Variable,
                    variable = new Variable { id = id, typeInfo = DecimalTypeInfo(), pointer = DecimalLiteral(values[i]) },
                });
                operands[i] = VariableRef(id);
            }
            instructions.Add(Return(DecimalArithmetic(op, operands)));
            return Evaluate(new FunctionWithReturnType
            {
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
                parameters = new Variable[0],
                typeInfo = DecimalTypeInfo(),
                instructions = instructions.ToArray(),
            });
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
            var error = Assert.Catch<NSGetterRuntimeError>(() =>
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
            var error = Assert.Catch<NSGetterRuntimeError>(() =>
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
        public void DecimalOp_ToDecimalOverflow_ThrowsNeoOverflowError()
        {
            // 1e30 has 31 integer digits, past System.Decimal's 2^96 envelope;
            // the TS runtime raises NeoOverflowError here too (P127 §3.4).
            // With error classes loaded the evaluator rewraps it as a
            // catchable error with the minted overflow class id.
            var error = Assert.Catch<NSGetterRuntimeError>(() =>
                EvaluateReturn(DecimalOp(
                    DecimalOpKind.ToDecimal, NumberLiteral(1e30), digits: 0)));
            var raised = error as NeoRuntimeError ?? error!.InnerException as NeoRuntimeError;
            Assert.IsNotNull(raised, error!.ToString());
            Assert.AreEqual(NeoErrorClass.NeoOverflowError, raised!.ErrorClass);
            Assert.AreEqual("Value was either too large or too small for a Decimal.", raised.Message);
            Assert.AreEqual(NeoErrorContract.NeoOverflowError, error.Payload.ClassId);
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
            var error = Assert.Catch<NSGetterRuntimeError>(() =>
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
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
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
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
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
            var error = Assert.Catch<NSGetterRuntimeError>(() =>
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
            var error = Assert.Catch<NSGetterRuntimeError>(() =>
                EvaluateReturn(
                    StringOp(StringOpKind.Trim, NullLiteral()),
                    MemberKind.String));
            StringAssert.Contains("receiver must be a string", error!.Message);
        }

        // ------------------------------------------------------------------
        // Stored decimal collections (P127 §3.4 Average).
        // ------------------------------------------------------------------

        // .NET seeds a List's Average with 0m but a HashSet's with its first entry.
        [Test]
        public void Average_StoredRows_SeedLikeDotNet()
        {
            using NeoClient client = BuildStoredCollectionsClient();
            var ctx = new NSGetterEvaluator.Context(client, null, null);

            Assert.AreEqual("0", StoredAverage("amounts", ctx));
            Assert.AreEqual("-0", StoredAverage("bag", ctx));
        }

        private static object? StoredAverage(string rowId, NSGetterEvaluator.Context ctx) => NSGetterEvaluator.EvaluatePointer(
            new FunctionPointer
            {
                type = PointerKind.Function,
                function = new CollectionQueryFunction
                {
                    type = FunctionKind.CollectionQuery,
                    info = new FunctionCollectionQueryInfo
                    {
                        op = "Average",
                        collectionPointer = new ReferencePointer { type = PointerKind.Reference, valueId = rowId },
                        keyType = "decimal",
                    },
                },
            },
            new Dictionary<string, object?>(),
            ctx);

        /// <summary>
        /// Save root <c>{ List&lt;decimal&gt; Amounts; Set&lt;decimal&gt; Bag; }</c>,
        /// each field storing one <c>-0</c> entry.
        /// </summary>
        private static NeoClient BuildStoredCollectionsClient()
        {
            var required = NeoMemberRequirementKind.Required;
            var rootClass = new NeoSchemaClass
            {
                id = "root-class",
                projectId = "project-a",
                name = "Root",
                schema = new Dictionary<string, string>(),
            };
            var saveClass = new NeoSchemaClass
            {
                id = "save-class",
                projectId = "project-a",
                name = "Save",
                schema = new Dictionary<string, string> { ["Amounts"] = "amounts-member", ["Bag"] = "bag-member" },
            };
            var saveRoot = ObjectValue("root-save-value", saveClass.id);
            saveRoot.value!["Amounts"] = "amounts";
            saveRoot.value["Bag"] = "bag";
            var members = new NeoCompose.Runtime.Json.Member[]
            {
                RootMember("root-assets", "root-assets-value", rootClass.id),
                RootMember("root-save", "root-save-value", saveClass.id),
                RootMember("root-session", "root-session-value", rootClass.id),
                new DecimalMember { id = "amount-entry", projectId = "project-a", name = "Amount", kind = MemberKind.Decimal, Requirement = required },
                new ListMember { id = "amounts-member", projectId = "project-a", name = "Amounts", kind = MemberKind.List, entryMemberId = "amount-entry", Requirement = required },
                new SetMember { id = "bag-member", projectId = "project-a", name = "Bag", kind = MemberKind.Set, entryMemberId = "amount-entry", Requirement = required },
            };
            var values = new MemberValue[]
            {
                ObjectValue("root-assets-value", rootClass.id),
                ObjectValue("root-session-value", rootClass.id),
                saveRoot,
                new ArrayMemberValue { id = "amounts", value = new[] { "amount-0" } },
                new StringMemberValue { id = "amount-0", value = "-0" },
                new ArrayMemberValue { id = "bag", value = new[] { "bag-0" } },
                new StringMemberValue { id = "bag-0", value = "-0" },
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
                members = members.ToDictionary(member => member.id),
                values = values.ToDictionary(value => value.id),
                classes = new Dictionary<string, NeoSchemaClass> { [rootClass.id] = rootClass, [saveClass.id] = saveClass },
                enums = new Dictionary<string, NeoCompose.Runtime.Json.Enum>(),
            }.WithUserRoot());
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
            }.WithUserRoot());
        }

        private static ClassMember RootMember(string id, string valueId, string classId)
        {
            return new ClassMember
            {
                id = id,
                projectId = "project-a",
                name = id,
                kind = MemberKind.Class,
                Requirement = NeoMemberRequirementKind.Required,
                valueId = valueId,
                DeclaredClassId = classId,
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
                compilerRevision = FunctionWithReturnType.CurrentCompilerRevision,
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
