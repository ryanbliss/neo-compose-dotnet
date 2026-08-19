// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime;
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
                    Assert.IsInstanceOf(
                        MapExpectedError(expectedError),
                        caught,
                        $"Wrong error type for {description}");
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

        private static Type MapExpectedError(string kind)
        {
            switch (kind)
            {
                case "overflow":
                    return typeof(DecimalOverflowException);
                case "divideByZero":
                    return typeof(DecimalDivisionByZeroException);
                case "digitsRange":
                    return typeof(DecimalDigitsRangeException);
                case "nonFinite":
                    return typeof(DecimalNonFiniteException);
                default:
                    throw new ArgumentException($"Unknown expectedError kind '{kind}'.");
            }
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
        public void MinMax_TiesReturnTheFirstArgument()
        {
            // "1.10" and "1.1" compare equal, so which argument a tie returns
            // is observable only through the scale that survives — and
            // System.Math.Min(decimal, decimal) returns the first.
            Assert.AreEqual("1.10", NeoDecimalMath.Min("1.10", "1.1"));
            Assert.AreEqual("1.1", NeoDecimalMath.Min("1.1", "1.10"));
            Assert.AreEqual("1.10", NeoDecimalMath.Max("1.10", "1.1"));
            Assert.AreEqual("1.1", NeoDecimalMath.Max("1.1", "1.10"));
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
