// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NeoCompose.Runtime.NeoScript;
using NeoCompose.Runtime.NeoScript.NumberMembers;
using NeoCompose.Runtime.NeoScript.Numerics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §7.3 and §8.1 to §8.5: runs the .NET 10 number-members fixture the
    /// compose runtime shares, including §8.1's one-NaN cases as NeoScript
    /// answers them.
    /// </summary>
    public class P127NumberMembersParityTests
    {
        private const string FixturePath = "Packages/com.ryanbliss.neocompose/Tests/number-members-parity.json";

        private static readonly string[] IntResults = { "Sign", "CompareTo", "Compare" };

        private static JObject? fixture;

        private static JObject Fixture => fixture ??= JObject.Parse(File.ReadAllText(FixturePath));

        [Test]
        public void MatchesTheIdentitiesAndConstants()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("identities"))
            {
                string kind = (string)c["k"]!;
                foreach (KeyValuePair<string, object> identity in Identities(kind))
                {
                    string resultKind = identity.Key == "Radix" ? "int" : kind;
                    Check(failures, c, Value(Text(resultKind, identity.Value)), Expected(resultKind, c[identity.Key]!), identity.Key);
                }
                var constants = (JObject)c["constants"]!;
                Dictionary<string, object> actual = Constants(kind);
                Check(failures, c, string.Join(",", actual.Keys), string.Join(",", constants.Properties().Select(p => p.Name)), "constant names");
                foreach (KeyValuePair<string, object> constant in actual)
                    Check(failures, c, Value(Text(kind, constant.Value)), Expected(kind, constants[constant.Key]!), constant.Key);
            }
            Report(failures);
        }

        [Test]
        public void ClassifiesEveryEdgeValue()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("classify"))
            {
                string kind = (string)c["k"]!;
                object value = Decode(kind, c["v"]!);
                foreach (NeoNumberPredicate predicate in (NeoNumberPredicate[])Enum.GetValues(typeof(NeoNumberPredicate)))
                {
                    bool actual = kind switch
                    {
                        "int" => NeoInt32Members.Is(predicate, (int)value),
                        "float" => NeoSingleMembers.Is(predicate, (float)value),
                        _ => NeoDecimalMembers.Is(predicate, (string)value),
                    };
                    Check(failures, c, Bool(actual), Bool((bool)c["r"]![predicate.ToString()]!), predicate.ToString());
                }
            }
            Report(failures);
        }

        [Test]
        public void RunsEveryUnaryMember()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("unary"))
            {
                string kind = (string)c["k"]!;
                string op = (string)c["op"]!;
                object value = Decode(kind, c["v"]!);
                string resultKind = ResultKind(kind, op);
                Check(failures, c, Run(resultKind, () => Unary(kind, op, value)), Expected(resultKind, c["r"]!));
            }
            Report(failures);
        }

        [Test]
        public void RunsEveryBinaryMember()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("binary"))
            {
                string kind = (string)c["k"]!;
                string op = (string)c["op"]!;
                object a = Decode(kind, c["a"]!);
                object b = Decode(kind, c["b"]!);
                string resultKind = ResultKind(kind, op);
                Check(failures, c, Run(resultKind, () => Binary(kind, op, a, b)), Expected(resultKind, c["r"]!));
            }
            Report(failures);
        }

        [Test]
        public void RunsEveryTernaryMember()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("ternary"))
            {
                string kind = (string)c["k"]!;
                string op = (string)c["op"]!;
                object a = Decode(kind, c["a"]!);
                object b = Decode(kind, c["b"]!);
                object d = Decode(kind, c["c"]!);
                Check(failures, c, Run(kind, () => Ternary(kind, op, a, b, d)), Expected(kind, c["r"]!));
            }
            Report(failures);
        }

        [Test]
        public void ComparesAndEqualsBoxedValues()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("object"))
            {
                string kind = (string)c["k"]!;
                object value = Decode(kind, c["v"]!);
                JToken other = c["o"]!;
                object? box = other.Type == JTokenType.Null ? null : Decode((string)other["k"]!, other["v"]!);
                bool sameKind = other.Type != JTokenType.Null && (string)other["k"]! == kind;
                bool equals;
                string compareTo;
                switch (kind)
                {
                    case "int":
                        equals = NeoInt32Members.EqualsObject((int)value, box);
                        compareTo = Run("int", () => NeoInt32Members.CompareToObject((int)value, box));
                        break;
                    case "float":
                        equals = NeoSingleMembers.EqualsObject((float)value, box);
                        compareTo = Run("int", () => NeoSingleMembers.CompareToObject((float)value, box));
                        break;
                    default:
                        equals = NeoDecimalMembers.EqualsObject((string)value, box, sameKind);
                        compareTo = Run("int", () => NeoDecimalMembers.CompareToObject((string)value, box, sameKind));
                        break;
                }
                Check(failures, c, Bool(equals), Bool((bool)c["equals"]!), "Equals");
                Check(failures, c, compareTo, Expected("int", c["compareTo"]!), "CompareTo");
            }
            Report(failures);
        }

        [Test]
        public void ConvertsThroughTheHooksAsDotnetSplitsThem()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("convert"))
            {
                bool from = (string)c["hook"]! == "from";
                var mode = (NeoCreateMode)Enum.Parse(typeof(NeoCreateMode), (string)c["mode"]!);
                string self = (string)c["self"]!;
                string other = (string)c["other"]!;
                string source = from ? other : self;
                string target = from ? self : other;
                object value = Decode(source, c["v"]!);
                JToken expected = c["r"]!;
                string actual = Run(target, () =>
                {
                    bool converted = from
                        ? NeoNumberConversionMembers.TryConvertFrom(mode, Kind(self), Kind(other), value, out object? result)
                        : NeoNumberConversionMembers.TryConvertTo(mode, Kind(self), Kind(other), value, out result);
                    return converted ? result! : "refused";
                });
                string wanted = expected.Type == JTokenType.Null ? Value("refused") : Expected(target, expected);
                Check(failures, c, actual, wanted);
            }
            Report(failures);
        }

        [Test]
        public void AnswersSection81sOneNaNCasesAsNeoScript()
        {
            float nan = NeoSingleMembers.NaN;
            float zeroOverZero = Divide(0f, 0f);
            float epsilon = NeoSingleMembers.Epsilon;
            var cases = new Dictionary<string, Func<object>>
            {
                ["IsNegative(0f / 0f)"] = () => NeoSingleMembers.Is(NeoNumberPredicate.IsNegative, zeroOverZero),
                ["IsNegative(-NaN)"] = () => NeoSingleMembers.Is(NeoNumberPredicate.IsNegative, -nan),
                ["IsNegative(Abs(NaN))"] = () => NeoSingleMembers.Is(NeoNumberPredicate.IsNegative, NeoSingleMembers.Abs(nan)),
                ["IsPositive(CopySign(NaN, 1f))"] = () => NeoSingleMembers.Is(NeoNumberPredicate.IsPositive, NeoSingleMembers.CopySign(nan, 1f)),
                ["CopySign(1f, 0f / 0f)"] = () => Bits(NeoSingleMembers.CopySign(1f, zeroOverZero)),
                ["Xor(Or(NaN, Epsilon), NaN)"] = () => Bits(NeoSingleMembers.Xor(NeoSingleMembers.Or(nan, epsilon), nan)),
                ["IsNegative(NaN)"] = () => NeoSingleMembers.Is(NeoNumberPredicate.IsNegative, nan),
                ["IsPositive(NaN)"] = () => NeoSingleMembers.Is(NeoNumberPredicate.IsPositive, nan),
                ["Bits(Abs(NaN))"] = () => Bits(NeoSingleMembers.Abs(nan)),
                ["Bits(CopySign(NaN, 1f))"] = () => Bits(NeoSingleMembers.CopySign(nan, 1f)),
                ["Bits(CopySign(NaN, -1f))"] = () => Bits(NeoSingleMembers.CopySign(nan, -1f)),
                ["Bits(Or(NaN, Epsilon))"] = () => Bits(NeoSingleMembers.Or(nan, epsilon)),
                ["Bits(And(0f / 0f, NaN))"] = () => Bits(NeoSingleMembers.And(zeroOverZero, nan)),
                ["Bits(Not(NaN))"] = () => Bits(NeoSingleMembers.Not(nan)),
                ["Bits(Not(Not(-NaN)))"] = () => Bits(NeoSingleMembers.Not(NeoSingleMembers.Not(-nan))),
                ["Bits(Xor(0f / 0f, -0f))"] = () => Bits(NeoSingleMembers.Xor(zeroOverZero, -0f)),
            };
            JArray expected = Cases("oneNaN");
            Assert.That(cases.Keys, Is.EqualTo(expected.Select(c => (string)c["expr"]!)));
            var failures = new List<string>();
            foreach (JToken c in expected)
            {
                object actual = cases[(string)c["expr"]!]();
                string text = actual is bool b ? Bool(b) : (string)actual;
                JToken r = c["r"]!;
                Check(failures, c, text, r.Type == JTokenType.Boolean ? Bool((bool)r) : (string)r!);
            }
            Report(failures);
        }

        /// <summary>A run-time 0f / 0f, which the compiler can't fold.</summary>
        private static float Divide(float left, float right) => left / right;

        private static string Bits(float value) =>
            "0x" + NeoSingleMembers.ToBits(value).ToString("X8", CultureInfo.InvariantCulture);

        private static NeoNumericKind Kind(string kind) => kind switch
        {
            "int" => NeoNumericKind.Int,
            "float" => NeoNumericKind.Float,
            _ => NeoNumericKind.Decimal,
        };

        private static string ResultKind(string kind, string op) => IntResults.Contains(op) ? "int" : kind;

        private static Dictionary<string, object> Identities(string kind) => kind switch
        {
            "int" => new Dictionary<string, object>
            {
                ["Zero"] = NeoInt32Members.Zero,
                ["One"] = NeoInt32Members.One,
                ["NegativeOne"] = NeoInt32Members.NegativeOne,
                ["AdditiveIdentity"] = NeoInt32Members.AdditiveIdentity,
                ["MultiplicativeIdentity"] = NeoInt32Members.MultiplicativeIdentity,
                ["Radix"] = NeoInt32Members.Radix,
                ["MinValue"] = NeoInt32Members.MinValue,
                ["MaxValue"] = NeoInt32Members.MaxValue,
            },
            "float" => new Dictionary<string, object>
            {
                ["Zero"] = NeoSingleMembers.Zero,
                ["One"] = NeoSingleMembers.One,
                ["NegativeOne"] = NeoSingleMembers.NegativeOne,
                ["AdditiveIdentity"] = NeoSingleMembers.AdditiveIdentity,
                ["MultiplicativeIdentity"] = NeoSingleMembers.MultiplicativeIdentity,
                ["Radix"] = NeoSingleMembers.Radix,
                ["MinValue"] = NeoSingleMembers.MinValue,
                ["MaxValue"] = NeoSingleMembers.MaxValue,
            },
            _ => new Dictionary<string, object>
            {
                ["Zero"] = NeoDecimalMembers.Zero,
                ["One"] = NeoDecimalMembers.One,
                ["NegativeOne"] = NeoDecimalMembers.NegativeOne,
                ["AdditiveIdentity"] = NeoDecimalMembers.AdditiveIdentity,
                ["MultiplicativeIdentity"] = NeoDecimalMembers.MultiplicativeIdentity,
                ["Radix"] = NeoDecimalMembers.Radix,
                ["MinValue"] = NeoDecimalMembers.MinValue,
                ["MaxValue"] = NeoDecimalMembers.MaxValue,
            },
        };

        private static Dictionary<string, object> Constants(string kind) => kind switch
        {
            "int" => new Dictionary<string, object>
            {
                ["MinValue"] = NeoInt32Members.MinValue,
                ["MaxValue"] = NeoInt32Members.MaxValue,
            },
            "float" => new Dictionary<string, object>
            {
                ["MinValue"] = NeoSingleMembers.MinValue,
                ["MaxValue"] = NeoSingleMembers.MaxValue,
                ["Epsilon"] = NeoSingleMembers.Epsilon,
                ["NaN"] = NeoSingleMembers.NaN,
                ["PositiveInfinity"] = NeoSingleMembers.PositiveInfinity,
                ["NegativeInfinity"] = NeoSingleMembers.NegativeInfinity,
            },
            _ => new Dictionary<string, object>
            {
                ["Zero"] = NeoDecimalMembers.Zero,
                ["One"] = NeoDecimalMembers.One,
                ["MinusOne"] = NeoDecimalMembers.MinusOne,
                ["MinValue"] = NeoDecimalMembers.MinValue,
                ["MaxValue"] = NeoDecimalMembers.MaxValue,
            },
        };

        private static object Unary(string kind, string op, object v)
        {
            switch (kind)
            {
                case "int":
                    int i = (int)v;
                    return op switch
                    {
                        "Abs" => NeoInt32.Abs(i),
                        "Sign" => NeoInt32Members.Sign(i),
                        _ => throw new ArgumentException(op),
                    };
                case "float":
                    float f = (float)v;
                    return op switch
                    {
                        "Abs" => NeoSingleMembers.Abs(f),
                        // Boxed, so the int doesn't convert to float.
                        "Sign" => (object)NeoSingleMembers.Sign(f),
                        "~" => NeoSingleMembers.Not(f),
                        _ => throw new ArgumentException(op),
                    };
                default:
                    string m = (string)v;
                    return op switch
                    {
                        "Abs" => NeoDecimalMembers.Abs(m),
                        "Sign" => NeoDecimalMembers.Sign(m),
                        "Negate" => NeoDecimalMembers.Negate(m),
                        _ => throw new ArgumentException(op),
                    };
            }
        }

        private static object Binary(string kind, string op, object a, object b)
        {
            switch (kind)
            {
                case "int":
                    int x = (int)a;
                    int y = (int)b;
                    return op switch
                    {
                        "Max" or "MaxNumber" => NeoInt32Members.Max(x, y),
                        "MaxNative" => NeoInt32Members.MaxNative(x, y),
                        "Min" or "MinNumber" => NeoInt32Members.Min(x, y),
                        "MinNative" => NeoInt32Members.MinNative(x, y),
                        "MaxMagnitude" or "MaxMagnitudeNumber" => NeoInt32Members.MaxMagnitude(x, y),
                        "MinMagnitude" or "MinMagnitudeNumber" => NeoInt32Members.MinMagnitude(x, y),
                        "CopySign" => NeoInt32Members.CopySign(x, y),
                        "Equals" => NeoInt32Members.Equals(x, y),
                        "CompareTo" => NeoInt32Members.CompareTo(x, y),
                        _ => throw new ArgumentException(op),
                    };
                case "float":
                    float p = (float)a;
                    float q = (float)b;
                    return op switch
                    {
                        "Max" => NeoSingleMembers.Max(p, q),
                        "MaxNative" => NeoSingleMembers.MaxNative(p, q),
                        "MaxNumber" => NeoSingleMembers.MaxNumber(p, q),
                        "Min" => NeoSingleMembers.Min(p, q),
                        "MinNative" => NeoSingleMembers.MinNative(p, q),
                        "MinNumber" => NeoSingleMembers.MinNumber(p, q),
                        "MaxMagnitude" => NeoSingleMembers.MaxMagnitude(p, q),
                        "MaxMagnitudeNumber" => NeoSingleMembers.MaxMagnitudeNumber(p, q),
                        "MinMagnitude" => NeoSingleMembers.MinMagnitude(p, q),
                        "MinMagnitudeNumber" => NeoSingleMembers.MinMagnitudeNumber(p, q),
                        "CopySign" => NeoSingleMembers.CopySign(p, q),
                        "Equals" => NeoSingleMembers.Equals(p, q),
                        "CompareTo" => NeoSingleMembers.CompareTo(p, q),
                        "&" => NeoSingleMembers.And(p, q),
                        "|" => NeoSingleMembers.Or(p, q),
                        "^" => NeoSingleMembers.Xor(p, q),
                        _ => throw new ArgumentException(op),
                    };
                default:
                    string m = (string)a;
                    string n = (string)b;
                    return op switch
                    {
                        "Max" or "MaxNumber" => NeoDecimalMembers.Max(m, n),
                        "MaxNative" => NeoDecimalMembers.MaxNative(m, n),
                        "Min" or "MinNumber" => NeoDecimalMembers.Min(m, n),
                        "MinNative" => NeoDecimalMembers.MinNative(m, n),
                        "MaxMagnitude" or "MaxMagnitudeNumber" => NeoDecimalMembers.MaxMagnitude(m, n),
                        "MinMagnitude" or "MinMagnitudeNumber" => NeoDecimalMembers.MinMagnitude(m, n),
                        "CopySign" => NeoDecimalMembers.CopySign(m, n),
                        "Equals" or "StaticEquals" => NeoDecimalMembers.Equals(m, n),
                        "CompareTo" or "Compare" => NeoDecimalMembers.CompareTo(m, n),
                        _ => throw new ArgumentException(op),
                    };
            }
        }

        private static object Ternary(string kind, string op, object a, object b, object c)
        {
            switch (kind)
            {
                case "int":
                    return op switch
                    {
                        "Clamp" => NeoInt32Members.Clamp((int)a, (int)b, (int)c),
                        "ClampNative" => NeoInt32Members.ClampNative((int)a, (int)b, (int)c),
                        "MultiplyAddEstimate" => NeoInt32Members.MultiplyAddEstimate((int)a, (int)b, (int)c),
                        _ => throw new ArgumentException(op),
                    };
                case "float":
                    return op switch
                    {
                        "Clamp" => NeoSingleMembers.Clamp((float)a, (float)b, (float)c),
                        "ClampNative" => NeoSingleMembers.ClampNative((float)a, (float)b, (float)c),
                        "MultiplyAddEstimate" => NeoSingleMembers.MultiplyAddEstimate((float)a, (float)b, (float)c),
                        _ => throw new ArgumentException(op),
                    };
                default:
                    return op switch
                    {
                        "Clamp" => NeoDecimalMembers.Clamp((string)a, (string)b, (string)c),
                        "ClampNative" => NeoDecimalMembers.ClampNative((string)a, (string)b, (string)c),
                        _ => throw new ArgumentException(op),
                    };
            }
        }

        private static JArray Cases(string section) => (JArray)Fixture[section]!;

        private static object Decode(string kind, JToken value) => kind switch
        {
            "int" => (int)value,
            "float" => FloatFromBits((string)value!),
            _ => (string)value!,
        };

        private static float FloatFromBits(string text) =>
            BitConverter.Int32BitsToSingle(unchecked((int)Convert.ToUInt32(text.Substring(2), 16)));

        // NeoScript has one NaN (§8.1), so every NaN compares as NaN.
        private static string FloatText(float value) =>
            float.IsNaN(value) ? "NaN" : "0x" + BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture);

        private static string Bool(bool value) => value ? "true" : "false";

        /// <summary>A runtime result in the fixture's encoding for <paramref name="kind"/>.</summary>
        private static string Text(string kind, object value) => value switch
        {
            bool b => Bool(b),
            float f => FloatText(f),
            int i => i.ToString(CultureInfo.InvariantCulture),
            string s => s,
            _ => throw new ArgumentException($"Unexpected {kind} result {value}."),
        };

        private static string Value(string value) => "value:" + value;

        private static string Run(string kind, Func<object> action)
        {
            try
            {
                return Value(Text(kind, action()));
            }
            catch (NSGetterRuntimeError error)
            {
                return $"error:{error.Payload.ClassId}:{error.Message}";
            }
        }

        /// <summary>A fixture outcome: an error, a bool, an int, or float bits or decimal text.</summary>
        private static string Expected(string kind, JToken expected)
        {
            if (expected is JObject error)
                return $"error:{ClassId((string)error["e"]!)}:{(string)error["m"]!}";
            if (expected.Type == JTokenType.Boolean)
                return Value(Bool((bool)expected));
            if (expected.Type == JTokenType.Integer)
                return Value(((int)expected).ToString(CultureInfo.InvariantCulture));
            string text = (string)expected!;
            return Value(kind == "float" ? FloatText(FloatFromBits(text)) : text);
        }

        private static string ClassId(string errorClass)
        {
            if (errorClass == "NeoArithmeticError")
                return NeoErrorContract.NeoArithmeticError;
            var neoClass = (NeoErrorClass)Enum.Parse(typeof(NeoErrorClass), errorClass);
            return new NeoRuntimeError(neoClass, "").Payload.ClassId;
        }

        /// <summary>Collects mismatches so one failing section reports every broken case.</summary>
        private static void Check(List<string> failures, JToken label, string actual, string wanted, string? member = null)
        {
            if (actual == wanted)
                return;
            failures.Add($"{(member is null ? "" : member + " ")}{label.ToString(Newtonsoft.Json.Formatting.None)}\n  got  {actual}\n  want {wanted}");
        }

        private static void Report(List<string> failures)
        {
            Assert.That(failures, Is.Empty, $"{failures.Count} mismatches:\n" + string.Join("\n", failures.Take(40)));
        }
    }
}
