// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime;
using NeoCompose.Runtime.NeoScript;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// Cross-runtime parity for <see cref="NeoDecimalMath"/>
    /// (specs/decimal-member.md §6.3/§8). Every vector in the vendored
    /// <see cref="NeoDecimalParityFixture"/> — shared verbatim with the web's
    /// <c>decimal-math.test.ts</c> — is executed against the BigInteger core
    /// and asserted against the expected canonical result or the mapped
    /// distinct exception type.
    /// </summary>
    public class NeoDecimalMathTests
    {
        [Test]
        public void ParityFixture_EveryVectorMatches()
        {
            var root = JObject.Parse(NeoDecimalParityFixture.Json);
            var vectors = (JArray)root["vectors"]!;
            Assert.Greater(vectors.Count, 0, "Parity fixture has no vectors.");

            foreach (JObject vector in vectors.Values<JObject>())
            {
                string description = vector.ToString(Formatting.None);
                string op = vector["op"]!.Value<string>()!;
                string? expectedError = vector["expectedError"]?.Value<string>();

                if (expectedError is not null)
                {
                    Exception? caught = Assert.Catch(() => Execute(vector));
                    Assert.IsNotNull(caught, $"Expected an error for {description}");
                    AssertExpectedError(op, expectedError, caught!, description);
                    continue;
                }

                object result = Execute(vector);
                if (op == "compare")
                {
                    Assert.AreEqual(
                        vector["expected"]!.Value<int>(),
                        (int)result,
                        description);
                }
                else
                {
                    Assert.AreEqual(
                        vector["expected"]!.Value<string>(),
                        (string)result,
                        description);
                }
            }
        }

        private static object Execute(JObject vector)
        {
            string op = vector["op"]!.Value<string>()!;
            switch (op)
            {
                case "add":
                    return NeoDecimalMath.Add(Arg(vector, "a"), Arg(vector, "b"));
                case "subtract":
                    return NeoDecimalMath.Subtract(Arg(vector, "a"), Arg(vector, "b"));
                case "multiply":
                    return NeoDecimalMath.Multiply(Arg(vector, "a"), Arg(vector, "b"));
                case "quotient":
                    return NeoDecimalMath.Divide(Arg(vector, "a"), Arg(vector, "b"));
                case "remainder":
                    return NeoDecimalMath.Remainder(Arg(vector, "a"), Arg(vector, "b"));
                case "multiplyAddEstimate":
                    return NeoDecimalMath.MultiplyAddEstimate(Arg(vector, "a"), Arg(vector, "b"), Arg(vector, "c"));
                case "compare":
                    return NeoDecimalMath.Compare(Arg(vector, "a"), Arg(vector, "b"));
                case "round":
                    return NeoDecimalMath.Round(Arg(vector, "a"), vector["digits"]!.Value<int>());
                case "divide":
                    return NeoDecimalMath.Divide(
                        Arg(vector, "a"),
                        Arg(vector, "b"),
                        vector["digits"]!.Value<int>());
                case "fromFloat":
                    return NeoDecimalMath.FromFloat(
                        vector["float"]!.Value<double>(),
                        vector["digits"]!.Value<int>());
                default:
                    throw new ArgumentException($"Unknown fixture op '{op}'.");
            }
        }

        private static string Arg(JObject vector, string key)
        {
            return vector[key]!.Value<string>()!;
        }

        /// <summary>
        /// The operators (and their statics) throw System.Decimal's errors
        /// with .NET's messages (P127 §3.4, §3.5). The NeoScript-only entry
        /// points keep their own: <c>Divide(divisor, digits)</c> by zero is
        /// the existing NeoDivideByZeroError message, and an explicit
        /// rounding entry point that can't fit its result throws
        /// <see cref="DecimalOverflowException"/>.
        /// </summary>
        private static void AssertExpectedError(string op, string kind, Exception caught, string description)
        {
            bool operatorOp = op is "add" or "subtract" or "multiply" or "quotient" or "remainder" or "multiplyAddEstimate";
            switch (kind)
            {
                case "overflow" when operatorOp:
                    AssertNeoError(caught, NeoErrorClass.NeoOverflowError, "Value was either too large or too small for a Decimal.", description);
                    return;
                case "overflow":
                    Assert.IsInstanceOf<DecimalOverflowException>(caught, description);
                    Assert.AreEqual(NeoDecimalMath.OverflowMessage, caught.Message, description);
                    return;
                case "divideByZero" when operatorOp:
                    AssertNeoError(caught, NeoErrorClass.NeoDivideByZeroError, "Attempted to divide by zero.", description);
                    return;
                case "divideByZero":
                    Assert.IsInstanceOf<NeoRuntimeError>(caught, description);
                    Assert.AreEqual(NeoErrorClass.NeoDivideByZeroError, ((NeoRuntimeError)caught).ErrorClass, description);
                    StringAssert.StartsWith("Decimal division by zero: ", caught.Message, description);
                    return;
                case "digitsRange":
                    Assert.IsInstanceOf<DecimalDigitsRangeException>(caught, description);
                    return;
                case "nonFinite":
                    Assert.IsInstanceOf<DecimalNonFiniteException>(caught, description);
                    return;
                default:
                    throw new ArgumentException($"Unknown expectedError kind '{kind}'.");
            }
        }

        private static void AssertNeoError(Exception caught, NeoErrorClass errorClass, string message, string description)
        {
            Assert.IsInstanceOf<NeoRuntimeError>(caught, description);
            Assert.AreEqual(errorClass, ((NeoRuntimeError)caught).ErrorClass, description);
            Assert.AreEqual(message, caught.Message, description);
        }

        // A couple of direct, human-readable spot checks over the fixture-driven
        // sweep — the motivating float64 bug and scale preservation.

        [Test]
        public void Add_ExactBaseTenAvoidsFloatDrift()
        {
            Assert.AreEqual("0.3", NeoDecimalMath.Add("0.1", "0.2"));
            Assert.AreEqual(0, NeoDecimalMath.Compare(NeoDecimalMath.Add("0.1", "0.2"), "0.3"));
        }

        [Test]
        public void Multiply_PreservesScaleSum()
        {
            Assert.AreEqual("2.20", NeoDecimalMath.Multiply("1.10", "2"));
        }

        // P127 §3.4: the operators round like System.Decimal. Each expectation
        // is .NET 10's result for the same operands; the fixture sweeps the
        // rest of DecCalc's branches.

        [TestCase("10", "0.0000000000000000000000000001", "10.000000000000000000000000000")]
        [TestCase("10", "0.0000000000000000000000000015", "10.000000000000000000000000002")]
        [TestCase("79228162514264337593543950334", "0.5", "79228162514264337593543950334")]
        [TestCase("7922816251426433759354395033.5", "0.05", "7922816251426433759354395034")]
        public void Add_RoundsHalfEvenByLoweringTheScale(string a, string b, string expected)
        {
            Assert.AreEqual(expected, NeoDecimalMath.Add(a, b));
        }

        [TestCase("1.000000000000001", "1.000000000000001", "1.0000000000000020000000000000")]
        [TestCase("0.0000000000000000000000000025", "0.1", "0.0000000000000000000000000002")]
        [TestCase("0.00000000000001", "-0.000000000000001", "-0.0000000000000000000000000000")]
        [TestCase("0.000000000000000000000001", "-0.000000000000000000000001", "0")]
        [TestCase("-0.00", "5000000000", "0")]
        [TestCase("-0.00", "5", "-0.00")]
        public void Multiply_RoundsAndSignsZeroLikeSystemDecimal(string a, string b, string expected)
        {
            Assert.AreEqual(expected, NeoDecimalMath.Multiply(a, b));
        }

        [TestCase("79228162514264337593543950335", "0.5")]
        [TestCase("-79228162514264337593543950335", "-1")]
        public void Add_OverflowsOnlyWhenTheIntegerPartDoesNotFit(string a, string b)
        {
            var error = Assert.Throws<NeoRuntimeError>(() => NeoDecimalMath.Add(a, b));
            Assert.AreEqual(NeoErrorClass.NeoOverflowError, error!.ErrorClass);
            Assert.AreEqual("Value was either too large or too small for a Decimal.", error.Message);
        }

        [TestCase("1", "3", "0.3333333333333333333333333333")]
        [TestCase("2", "3", "0.6666666666666666666666666667")]
        [TestCase("1.000", "4", "0.250")]
        [TestCase("1", "4", "0.25")]
        [TestCase("1.0", "1.00", "1")]
        [TestCase("100", "0.01", "10000")]
        [TestCase("1", "81", "0.012345679012345679012345679")]
        [TestCase("-0.0000000000000000000000000001", "3", "-0")]
        [TestCase("-0", "5", "-0")]
        public void Divide_FollowsSystemDecimalResultScale(string a, string b, string expected)
        {
            Assert.AreEqual(expected, NeoDecimalMath.Divide(a, b));
        }

        [TestCase("79228162514264337593543950335", "0.1", "0.0")]
        [TestCase("79228162514264337593543950335", "0.0000000000000000000000000003", "0.0000000000000000000000000000")]
        [TestCase("-5", "2", "-1")]
        [TestCase("5", "-2", "1")]
        [TestCase("-4", "2", "-0")]
        [TestCase("1", "3.00", "1")]
        [TestCase("-1", "1.0", "-0.0")]
        [TestCase("-0.00", "5", "-0.00")]
        public void Remainder_NeverFormsTheQuotient(string a, string b, string expected)
        {
            Assert.AreEqual(expected, NeoDecimalMath.Remainder(a, b));
        }

        [TestCase("1", "0")]
        [TestCase("0", "-0.00")]
        public void DivideAndRemainder_ByZeroThrowNeoDivideByZeroError(string a, string b)
        {
            foreach (TestDelegate call in new TestDelegate[] { () => NeoDecimalMath.Divide(a, b), () => NeoDecimalMath.Remainder(a, b) })
            {
                var error = Assert.Throws<NeoRuntimeError>(call);
                Assert.AreEqual(NeoErrorClass.NeoDivideByZeroError, error!.ErrorClass);
                Assert.AreEqual("Attempted to divide by zero.", error.Message);
            }
        }

        [Test]
        public void DivideWithDigits_ByZeroKeepsItsMessage()
        {
            var error = Assert.Throws<NeoRuntimeError>(() => NeoDecimalMath.Divide("1", "0.00", 5));
            Assert.AreEqual(NeoErrorClass.NeoDivideByZeroError, error!.ErrorClass);
            Assert.AreEqual("Decimal division by zero: \"1\" / \"0.00\".", error.Message);
        }

        [Test]
        public void MultiplyAddEstimate_RoundsTheProductBeforeAdding()
        {
            // Fused, this would be 2.0000000000000000000000000001E-28.
            Assert.AreEqual(
                "0.0000000000000000000000000002",
                NeoDecimalMath.MultiplyAddEstimate("1.0000000000000000000000000001", "1.0000000000000000000000000001", "-1"));
        }

        [Test]
        public void ToFloat_ReturnsNearestDouble()
        {
            Assert.AreEqual(0.5d, NeoDecimalMath.ToFloat("0.5"));
            Assert.AreEqual(-2.25d, NeoDecimalMath.ToFloat("-2.25"));
        }

        // The Math builtin arms on the exact core (P69 §2.2/§2.5/§2.6). The
        // shared math fixture covers them through the evaluator; these pin the
        // core's own contract, which the evaluator only forwards.

        [Test]
        public void MinMax_TiesFollowSystemDecimal()
        {
            // "1.10" and "1.1" compare equal, so which argument a tie returns
            // is observable only through the scale and sign that survive.
            // .NET 10's decimal.Min returns the second, decimal.Max the first.
            Assert.AreEqual("1.1", NeoDecimalMath.Min("1.10", "1.1"));
            Assert.AreEqual("1.10", NeoDecimalMath.Min("1.1", "1.10"));
            Assert.AreEqual("1.00", NeoDecimalMath.Min("1.0", "1.00"));
            Assert.AreEqual("-0", NeoDecimalMath.Min("0", "-0"));
            Assert.AreEqual("0", NeoDecimalMath.Min("-0.00", "0"));
            Assert.AreEqual("1.10", NeoDecimalMath.Max("1.10", "1.1"));
            Assert.AreEqual("1.1", NeoDecimalMath.Max("1.1", "1.10"));
            Assert.AreEqual("-0", NeoDecimalMath.Max("-0", "0"));
            Assert.AreEqual("-0.00", NeoDecimalMath.Max("-0.00", "0"));
        }

        // P127 §3.4: decimal follows System.Decimal, so -0 keeps its sign and
        // scale through parsing, arithmetic and formatting. Each expectation
        // below is .NET 10's decimal result for the same operands.

        [TestCase("-0")]
        [TestCase("-0.00")]
        [TestCase("0.00")]
        public void NegativeZero_RoundTripsWithItsSignAndScale(string text)
        {
            Assert.AreEqual(text, NeoDecimalMath.FormatParts(NeoDecimalMath.ParseArg(text, "value")));
            Assert.AreEqual(text.StartsWith("-"), NeoDecimalMath.ParseArg(text, "value").Negative);
        }

        [TestCase("-0", "0", "-0")]
        [TestCase("0", "-0", "0")]
        [TestCase("-0.00", "0", "-0.00")]
        [TestCase("-1.5", "1.5", "-0.0")]
        [TestCase("1.5", "-1.50", "0.00")]
        public void NegativeZero_AddFollowsSystemDecimal(string a, string b, string expected)
        {
            Assert.AreEqual(expected, NeoDecimalMath.Add(a, b));
        }

        [TestCase("-0", "0", "-0")]
        [TestCase("0", "0", "0")]
        [TestCase("1.0", "1", "-0.0")]
        public void NegativeZero_SubtractFollowsSystemDecimal(string a, string b, string expected)
        {
            Assert.AreEqual(expected, NeoDecimalMath.Subtract(a, b));
        }

        [TestCase("-0", "1", "-0")]
        [TestCase("-1", "0.00", "-0.00")]
        [TestCase("-1", "-0", "0")]
        public void NegativeZero_MultiplyTakesTheSignsXor(string a, string b, string expected)
        {
            Assert.AreEqual(expected, NeoDecimalMath.Multiply(a, b));
        }

        [Test]
        public void NegativeZero_DivideTakesTheSignsXor()
        {
            Assert.AreEqual("-0", NeoDecimalMath.Divide("-0", "3", 0));
            Assert.AreEqual("-0", NeoDecimalMath.Divide("0", "-3", 0));
            Assert.AreEqual("0", NeoDecimalMath.Divide("-0", "-3", 0));
        }

        [Test]
        public void NegativeZero_RoundingKeepsTheSignAndAbsDropsIt()
        {
            Assert.AreEqual("-1", NeoDecimalMath.Floor("-0.5"));
            Assert.AreEqual("-0", NeoDecimalMath.Floor("-0.00"));
            Assert.AreEqual("-0", NeoDecimalMath.Ceiling("-0.5"));
            Assert.AreEqual("-0", NeoDecimalMath.Truncate("-0.5"));
            Assert.AreEqual("-0", NeoDecimalMath.Round("-0.4", 0));
            Assert.AreEqual("-0.00", NeoDecimalMath.Round("-0.004", 2));
            Assert.AreEqual("0.00", NeoDecimalMath.Abs("-0.00"));
        }

        [TestCase("-0")]
        [TestCase("-0.00")]
        public void NegativeZero_ToFloatIsNegativeZero(string text)
        {
            double value = NeoDecimalMath.ToFloat(text);
            Assert.AreEqual(0d, value);
            Assert.IsTrue(double.IsNegative(value));
        }

        [Test]
        public void MinMax_CompareExactlyNotLexically()
        {
            Assert.AreEqual("2.5", NeoDecimalMath.Min("10", "2.5"));
            Assert.AreEqual("10", NeoDecimalMath.Max("10", "2.5"));
        }

        [Test]
        public void Abs_PreservesScale()
        {
            Assert.AreEqual("1.50", NeoDecimalMath.Abs("-1.50"));
            Assert.AreEqual("0", NeoDecimalMath.Abs("0"));
        }

        [Test]
        public void FloorCeilingTruncate_AreDirectional()
        {
            Assert.AreEqual("-5", NeoDecimalMath.Floor("-4.25"));
            Assert.AreEqual("-4", NeoDecimalMath.Ceiling("-4.25"));
            Assert.AreEqual("-4", NeoDecimalMath.Truncate("-4.25"));
            Assert.AreEqual("4", NeoDecimalMath.Floor("4.25"));
            Assert.AreEqual("5", NeoDecimalMath.Ceiling("4.25"));
            Assert.AreEqual("4", NeoDecimalMath.Truncate("4.25"));
        }

        [Test]
        public void FloorCeilingTruncate_CanonicalizeAnAlreadyIntegralValue()
        {
            // Scale 0 is the shape System.Math.Floor(decimal) produces, so
            // "4.00" comes back as "4" rather than carrying its scale through.
            Assert.AreEqual("4", NeoDecimalMath.Floor("4.00"));
            Assert.AreEqual("4", NeoDecimalMath.Ceiling("4.00"));
            Assert.AreEqual("4", NeoDecimalMath.Truncate("4.00"));
        }

        [Test]
        public void Directional_DiffersFromHalfEvenRoundingAtAMidpoint()
        {
            // Math.Round(decimal) is defined as x.Round(0) — half-even — while
            // the three directional ops are not. 2.5 is where that shows.
            Assert.AreEqual("2", NeoDecimalMath.Round("2.5", 0));
            Assert.AreEqual("2", NeoDecimalMath.Floor("2.5"));
            Assert.AreEqual("3", NeoDecimalMath.Ceiling("2.5"));
            Assert.AreEqual("2", NeoDecimalMath.Truncate("2.5"));
        }
    }
}
