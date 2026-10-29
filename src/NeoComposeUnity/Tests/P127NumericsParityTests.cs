// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime.NeoScript;
using NeoCompose.Runtime.NeoScript.Numerics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §3.1 to §3.3: runs the runtime sections of the .NET 10 numerics
    /// fixture the compose runtime shares. The <c>fold</c> sections belong to
    /// the compiler, which only the compose twin has.
    /// </summary>
    public class P127NumericsParityTests
    {
        private const string FixturePath = "Packages/com.ryanbliss.neocompose/Tests/numerics-parity.json";

        private static JObject? fixture;

        private static JObject Fixture => fixture ??= JObject.Parse(File.ReadAllText(FixturePath));

        [Test]
        public void RunsEveryInt32BinaryOperatorOverTheEdgeValues()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("int32"))
            {
                string op = (string)c["op"]!;
                int a = (int)c["a"]!;
                int b = (int)c["b"]!;
                Check(failures, c, Run(() => Binary(op, a, b, false)), Expected(c["u"]!), "unchecked");
                Check(failures, c, Run(() => Binary(op, a, b, true)), Expected(c["c"]!), "checked");
            }
            Report(failures);
        }

        [Test]
        public void RunsEveryInt32UnaryOperatorOverTheEdgeValues()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("int32Unary"))
            {
                string op = (string)c["op"]!;
                int a = (int)c["a"]!;
                Check(failures, c, Run(() => Unary(op, a, false)), Expected(c["u"]!), "unchecked");
                Check(failures, c, Run(() => Unary(op, a, true)), Expected(c["c"]!), "checked");
            }
            Report(failures);
        }

        [Test]
        public void ChecksAbsSumAndAverageAsMathAndEnumerableDo()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("abs"))
            {
                Check(failures, c, Run(() => Int(NeoInt32.Abs((int)c["v"]!))), Expected(c["r"]!));
            }
            foreach (JToken c in Cases("sum"))
            {
                int[] values = Ints(c["values"]!);
                Check(failures, c, Run(() => Int(NeoInt32.Sum(values))), Expected(c["r"]!));
            }
            foreach (JToken c in Cases("average"))
            {
                int[] values = Ints(c["values"]!);
                Check(failures, c, Run(() => FloatText(NeoInt32.Average(values)!.Value)), Expected(c["r"]!));
            }
            Report(failures);
        }

        [Test]
        public void ConvertsIntFloatAndDecimalValuesAsDotNetCastsDo()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("fromInt"))
            {
                int v = (int)c["v"]!;
                Check(failures, c, Run(() => FloatText(NeoNumericConversions.SingleFromInt32(v))), Expected(c["f"]!), "float");
                Check(failures, c, Run(() => NeoNumericConversions.DecimalFromInt32(v)), Expected(c["m"]!), "decimal");
            }
            foreach (JToken c in Cases("fromFloat"))
            {
                float v = FloatFromBits((string)c["v"]!);
                Check(failures, c, Run(() => Int(NeoNumericConversions.SingleToInt32(v, false))), Expected(c["iu"]!), "unchecked int");
                Check(failures, c, Run(() => Int(NeoNumericConversions.SingleToInt32(v, true))), Expected(c["ic"]!), "checked int");
                Check(failures, c, Run(() => NeoNumericConversions.SingleToDecimal(v)), Expected(c["m"]!), "decimal");
            }
            foreach (JToken c in Cases("fromDecimal"))
            {
                string v = (string)c["v"]!;
                Check(failures, c, Run(() => Int(NeoNumericConversions.DecimalToInt32(v))), Expected(c["i"]!), "int");
                Check(failures, c, Run(() => FloatText(NeoNumericConversions.DecimalToSingle(v))), Expected(c["f"]!), "float");
            }
            Report(failures);
        }

        [Test]
        public void RunsCreateCheckedSaturatingAndTruncating()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("create"))
            {
                string to = (string)c["to"]!;
                string from = (string)c["from"]!;
                Check(failures, c, Run(() => Create(NeoCreateMode.Checked, to, from, c["v"]!)), Expected(c["c"]!), "checked");
                Check(failures, c, Run(() => Create(NeoCreateMode.Saturating, to, from, c["v"]!)), Expected(c["s"]!), "saturating");
                Check(failures, c, Run(() => Create(NeoCreateMode.Truncating, to, from, c["v"]!)), Expected(c["t"]!), "truncating");
            }
            Report(failures);
        }

        private static string Binary(string op, int a, int b, bool isChecked) => Int(op switch
        {
            "+" => NeoInt32.Add(a, b, isChecked),
            "-" => NeoInt32.Subtract(a, b, isChecked),
            "*" => NeoInt32.Multiply(a, b, isChecked),
            "/" => NeoInt32.Divide(a, b),
            "%" => NeoInt32.Remainder(a, b),
            "&" => a & b,
            "|" => a | b,
            "^" => a ^ b,
            "<<" => NeoInt32.ShiftLeft(a, b),
            ">>" => NeoInt32.ShiftRight(a, b),
            ">>>" => NeoInt32.UnsignedShiftRight(a, b),
            _ => throw new ArgumentException($"Unexpected operator {op}."),
        });

        private static string Unary(string op, int a, bool isChecked) => Int(op switch
        {
            "+" => a,
            "-" => NeoInt32.Negate(a, isChecked),
            "~" => ~a,
            "++" => NeoInt32.Increment(a, isChecked),
            "--" => NeoInt32.Decrement(a, isChecked),
            _ => throw new ArgumentException($"Unexpected operator {op}."),
        });

        private static string Create(NeoCreateMode mode, string to, string from, JToken value)
        {
            switch (from)
            {
                case "int":
                    int i = (int)value;
                    return to switch
                    {
                        "int" => Int(i),
                        "float" => FloatText(NeoNumericConversions.SingleFromInt32(i)),
                        _ => NeoNumericConversions.DecimalFromInt32(i),
                    };
                case "float":
                    float f = FloatFromBits((string)value!);
                    return to switch
                    {
                        "int" => Int(NeoNumericConversions.Int32FromSingle(f, mode)),
                        "float" => FloatText(f),
                        _ => NeoNumericConversions.DecimalFromSingle(f, mode),
                    };
                default:
                    string m = (string)value!;
                    return to switch
                    {
                        "int" => Int(NeoNumericConversions.Int32FromDecimal(m, mode)),
                        "float" => FloatText(NeoNumericConversions.DecimalToSingle(m)),
                        _ => m,
                    };
            }
        }

        private static JArray Cases(string section) => (JArray)Fixture[section]!;

        private static int[] Ints(JToken values) => values.Select(value => (int)value).ToArray();

        private static string Int(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private static float FloatFromBits(string text) =>
            BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32(text.Substring(2), 16)));

        // NeoScript has one NaN (§8.1), so every NaN compares as NaN.
        private static string FloatText(float value) =>
            float.IsNaN(value) ? "NaN" : "0x" + BitConverter.SingleToInt32Bits(value).ToString("X8", System.Globalization.CultureInfo.InvariantCulture);

        private static string Value(string value) => "value:" + value;

        private static string Run(Func<string> action)
        {
            try
            {
                return Value(action());
            }
            catch (NeoRuntimeError error)
            {
                return $"error:{error.ErrorClass}:{error.Message}";
            }
        }

        /// <summary>A fixture outcome: an error, an int, or float bits or decimal text.</summary>
        private static string Expected(JToken expected)
        {
            if (expected is JObject error)
                return $"error:{(string)error["e"]!}:{(string)error["m"]!}";
            if (expected.Type == JTokenType.Integer)
                return Value(Int((int)expected));
            string text = (string)expected!;
            return Value(text.StartsWith("0x", StringComparison.Ordinal) ? FloatText(FloatFromBits(text)) : text);
        }

        /// <summary>Collects mismatches so one failing section reports every broken case.</summary>
        private static void Check(List<string> failures, JToken label, string actual, string wanted, string? op = null)
        {
            if (actual == wanted)
                return;
            failures.Add($"{(op is null ? "" : op + " ")}{label.ToString(Newtonsoft.Json.Formatting.None)}\n  got  {actual}\n  want {wanted}");
        }

        private static void Report(List<string> failures)
        {
            Assert.That(failures, Is.Empty, $"{failures.Count} mismatches:\n" + string.Join("\n", failures.Take(40)));
        }
    }
}
