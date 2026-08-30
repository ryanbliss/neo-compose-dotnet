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
    /// The <c>mathOp</c> intrinsic (P69 §5.2) at the evaluator seam, where the
    /// shared <see cref="NeoScriptMathParityFixture"/> cannot reach: values a
    /// compiled body can never carry (a null or non-numeric argument, an
    /// unknown op) and the argument-domain failures spelled out by hand rather
    /// than reconstructed from IEEE overflow the way the fixture must.
    ///
    /// <para>Every message here is quoted in full, never matched by substring:
    /// P69 §5.3 makes these strings a cross-runtime contract, and a partial
    /// match would let one host's wording drift past the gate.</para>
    /// </summary>
    public class P69MathBuiltinTests
    {
        // ------------------------------------------------------------------
        // IR deserialization — the wire op union and the `decimal` flag.
        // ------------------------------------------------------------------

        [Test]
        public void Json_MathOpFunction_Deserializes()
        {
            var mathOp = JsonConvert.DeserializeObject<Function>(
                @"{
                    ""type"": ""mathOp"",
                    ""info"": {
                        ""op"": ""clamp"",
                        ""argPointers"": [
                            { ""type"": ""variable"", ""variableId"": ""v"" },
                            { ""type"": ""variable"", ""variableId"": ""lo"" },
                            { ""type"": ""variable"", ""variableId"": ""hi"" }
                        ],
                        ""decimal"": true
                    }
                }");
            Assert.IsInstanceOf<MathOpFunction>(mathOp);
            var info = ((MathOpFunction)mathOp!).info;
            Assert.AreEqual(MathOpKind.Clamp, info.op);
            Assert.AreEqual(3, info.argPointers.Length);
            Assert.AreEqual(true, info.isDecimal);
        }

        // ------------------------------------------------------------------
        // The float arm.
        // ------------------------------------------------------------------

        [Test]
        public void Round_IsHalfEvenNotHalfUp()
        {
            // The whole reason the TS evaluator hand-rolls this one: JS
            // Math.round(2.5) is 3, and half-even is the pinned contract.
            Assert.AreEqual(2d, EvaluateInt(MathOp(MathOpKind.Round, FloatLiteral(2.5))));
            Assert.AreEqual(4d, EvaluateInt(MathOp(MathOpKind.Round, FloatLiteral(3.5))));
            Assert.AreEqual(-2d, EvaluateInt(MathOp(MathOpKind.Round, FloatLiteral(-2.5))));
        }

        [Test]
        public void FloorCeilingTruncate_AreDirectionalOnNegatives()
        {
            Assert.AreEqual(-5d, EvaluateInt(MathOp(MathOpKind.Floor, FloatLiteral(-4.25))));
            Assert.AreEqual(-4d, EvaluateInt(MathOp(MathOpKind.Ceiling, FloatLiteral(-4.25))));
            Assert.AreEqual(-4d, EvaluateInt(MathOp(MathOpKind.Truncate, FloatLiteral(-4.25))));
        }

        [Test]
        public void Clamp_RejectsReversedBounds()
        {
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                EvaluateFloat(MathOp(
                    MathOpKind.Clamp,
                    FloatLiteral(5.0),
                    FloatLiteral(10.0),
                    FloatLiteral(2.0))));
            Assert.AreEqual("Math.Clamp requires min <= max.", error!.Message);
        }

        [Test]
        public void Clamp_NaNBoundsDoNotTripTheOrderGuard()
        {
            // The guard is a real comparison: NaN > x is false, so a NaN bound
            // leaves the two pins to decide, and the value passes through.
            Assert.AreEqual(
                1d,
                EvaluateFloat(MathOp(
                    MathOpKind.Clamp,
                    FloatLiteral(1.0),
                    FloatLiteral(double.NaN),
                    FloatLiteral(10.0))));
        }

        [Test]
        public void Sign_IsThreeWayAndRejectsNaN()
        {
            Assert.AreEqual(-1d, EvaluateInt(MathOp(MathOpKind.Sign, FloatLiteral(-3.2))));
            Assert.AreEqual(0d, EvaluateInt(MathOp(MathOpKind.Sign, FloatLiteral(-0.0))));
            Assert.AreEqual(1d, EvaluateInt(MathOp(MathOpKind.Sign, FloatLiteral(3.2))));

            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                EvaluateInt(MathOp(MathOpKind.Sign, FloatLiteral(double.NaN))));
            Assert.AreEqual("Math.Sign is undefined for NaN.", error!.Message);
        }

        [TestCase(MathOpKind.Round, "Round")]
        [TestCase(MathOpKind.Floor, "Floor")]
        [TestCase(MathOpKind.Ceiling, "Ceiling")]
        [TestCase(MathOpKind.Truncate, "Truncate")]
        public void IntReturningOps_RejectNonFiniteArguments(string op, string name)
        {
            // These four are NeoScript's only float → int conversions, so a
            // non-finite argument would mint an Int-typed value the runtime's
            // own integral validation refuses downstream (P69 §2.5).
            foreach (double nonFinite in new[]
            {
                double.PositiveInfinity,
                double.NegativeInfinity,
                double.NaN,
            })
            {
                var error = Assert.Throws<NSGetterRuntimeError>(() =>
                    EvaluateInt(MathOp(op, FloatLiteral(nonFinite))));
                Assert.AreEqual($"Math.{name} requires a finite argument.", error!.Message);
            }
        }

        [Test]
        public void FloatArm_KeepsHostNonFiniteSemantics()
        {
            // Min/Max/Clamp/Abs/Sqrt stay Float-typed, so they carry the
            // host's answer through rather than guarding it.
            Assert.AreEqual(
                double.PositiveInfinity,
                EvaluateFloat(MathOp(
                    MathOpKind.Max,
                    FloatLiteral(1.5),
                    FloatLiteral(double.PositiveInfinity))));
            Assert.AreEqual(
                double.PositiveInfinity,
                EvaluateFloat(MathOp(
                    MathOpKind.Abs,
                    FloatLiteral(double.NegativeInfinity))));
        }

        [Test]
        public void NullArgument_ThrowsNamingTheFunction()
        {
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                EvaluateFloat(MathOp(MathOpKind.Sqrt, NullLiteral())));
            Assert.AreEqual("Math.Sqrt argument is null.", error!.Message);
        }

        [Test]
        public void NonNumericArgument_ThrowsNamingTheFunctionAndTheType()
        {
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                EvaluateFloat(MathOp(
                    MathOpKind.Min,
                    FloatLiteral(1.0),
                    StringLiteral("two"))));
            Assert.AreEqual("Math.Min argument is not numeric: string.", error!.Message);
        }

        // ------------------------------------------------------------------
        // The exact-decimal arm.
        // ------------------------------------------------------------------

        [Test]
        public void DecimalMinMax_TiesReturnTheFirstArgument()
        {
            // "1.10" and "1.1" are the same decimal spelled two ways, so which
            // argument a tie returns is observable through the surviving scale.
            Assert.AreEqual(
                "1.10",
                EvaluateDecimal(DecimalMathOp(
                    MathOpKind.Min, DecimalLiteral("1.10"), DecimalLiteral("1.1"))));
            Assert.AreEqual(
                "1.1",
                EvaluateDecimal(DecimalMathOp(
                    MathOpKind.Max, DecimalLiteral("1.1"), DecimalLiteral("1.10"))));
        }

        [Test]
        public void DecimalRound_IsExactlyTheScaleZeroDecimalRound()
        {
            Assert.AreEqual(
                NeoDecimalMath.Round("2.5", 0),
                EvaluateDecimal(DecimalMathOp(MathOpKind.Round, DecimalLiteral("2.5"))));
            Assert.AreEqual(
                NeoDecimalMath.Round("-2.5", 0),
                EvaluateDecimal(DecimalMathOp(MathOpKind.Round, DecimalLiteral("-2.5"))));
        }

        [Test]
        public void DecimalAbsAndSign_RunOnTheExactCore()
        {
            // The shared fixture's decimal cases stop at clamp/min/max and the
            // integer-valued ops, so these two arms are pinned here: Abs keeps
            // the argument's scale, and Sign is Int-typed on every arm.
            Assert.AreEqual(
                "1.50",
                EvaluateDecimal(DecimalMathOp(MathOpKind.Abs, DecimalLiteral("-1.50"))));
            Assert.AreEqual(
                -1d,
                EvaluateInt(DecimalMathOp(MathOpKind.Sign, DecimalLiteral("-0.0001"))));
            Assert.AreEqual(
                0d,
                EvaluateInt(DecimalMathOp(MathOpKind.Sign, DecimalLiteral("0.00"))));
            Assert.AreEqual(
                1d,
                EvaluateInt(DecimalMathOp(MathOpKind.Sign, DecimalLiteral("0.0001"))));
        }

        [Test]
        public void DecimalClamp_RejectsReversedBounds()
        {
            var error = Assert.Throws<NSGetterRuntimeError>(() =>
                EvaluateDecimal(DecimalMathOp(
                    MathOpKind.Clamp,
                    DecimalLiteral("5"),
                    DecimalLiteral("10"),
                    DecimalLiteral("2"))));
            Assert.AreEqual("Math.Clamp requires min <= max.", error!.Message);
        }

        [Test]
        public void DecimalArm_WidensAnIntArgumentExactly()
        {
            Assert.AreEqual(
                "2.5",
                EvaluateDecimal(DecimalMathOp(
                    MathOpKind.Min, IntLiteral(3.0), DecimalLiteral("2.5"))));
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
                    name = "Math builtins",
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
                Requirement = NeoMemberRequirementKind.Required,
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

        /// <summary>Evaluates `return &lt;pointer&gt;;` as a getter of the given type.</summary>
        private static object? EvaluateReturn(Pointer pointer, MemberKind returnType)
        {
            return NSGetterEvaluator.Evaluate(
                new FunctionWithReturnType
                {
                    parameters = new Variable[0],
                    typeInfo = new PrimitiveTypeInfo { type = returnType, required = true },
                    instructions = new Instruction[]
                    {
                        new ReturnInstruction
                        {
                            type = InstructionKind.Return,
                            pointer = pointer,
                        },
                    },
                },
                new NSGetterEvaluator.Context(BuildClient(), null, null));
        }

        private static object? EvaluateInt(Pointer pointer) =>
            EvaluateReturn(pointer, MemberKind.Int);

        private static object? EvaluateFloat(Pointer pointer) =>
            EvaluateReturn(pointer, MemberKind.Float);

        private static object? EvaluateDecimal(Pointer pointer) =>
            EvaluateReturn(pointer, MemberKind.Decimal);

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

        private static Pointer FloatLiteral(double value) =>
            Literal(MemberKind.Float, JToken.FromObject(value));

        private static Pointer IntLiteral(double value) =>
            Literal(MemberKind.Int, JToken.FromObject(value));

        private static Pointer DecimalLiteral(string value) =>
            Literal(MemberKind.Decimal, JToken.FromObject(value));

        private static Pointer StringLiteral(string value) =>
            Literal(MemberKind.String, JToken.FromObject(value));

        private static Pointer NullLiteral() =>
            Literal(MemberKind.Null, JValue.CreateNull());

        private static Pointer MathOp(string op, params Pointer[] args) =>
            MathOp(op, isDecimal: null, args);

        private static Pointer DecimalMathOp(string op, params Pointer[] args) =>
            MathOp(op, isDecimal: true, args);

        private static Pointer MathOp(string op, bool? isDecimal, params Pointer[] args)
        {
            return new FunctionPointer
            {
                type = PointerKind.Function,
                function = new MathOpFunction
                {
                    type = FunctionKind.MathOp,
                    info = new FunctionMathOpInfo
                    {
                        op = op,
                        argPointers = args,
                        isDecimal = isDecimal,
                    },
                },
            };
        }
    }
}
