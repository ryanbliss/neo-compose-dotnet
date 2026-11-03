// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NeoCompose.Runtime.NeoScript;
using NeoCompose.Runtime.NeoScript.Ordering;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §10.2: replays the .NET 10 ordering fixture the compose runtime
    /// shares. Each case must match the result, the error and every callback
    /// call. See the fixture's README beside the compose copy for the format.
    /// </summary>
    public class P127OrderingParityTests
    {
        private const string FixturePath = "Packages/com.ryanbliss.neocompose/Tests/ordering-parity.json";
        private const string OrderComparerText = "System.Comparison`1[System.Int32]";

        private static JObject? fixture;

        private static JObject Fixture => fixture ??= JObject.Parse(File.ReadAllText(FixturePath));

        [Test]
        public void SortsListsAsArraySortHelperAndGenericArraySortHelperDo()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("sort"))
            {
                string mode = (string)c["mode"]!;
                string cmp = (string)c["cmp"]!;
                int[] input = Ints(c["input"]!);
                var items = input.Select((k, id) => k == -1 ? null : new Item(k, id)).ToList();
                var log = new Log((int?)c["throwAt"] ?? 0);
                Comparison<Item?> compare = mode == "comparable"
                    ? (x, y) =>
                    {
                        if (x is null)
                            throw new InvalidOperationException("null receiver");
                        log.Call(x.Id, y?.Id);
                        if (cmp == "alwaysLess")
                            return -1;
                        return y is null ? 1 : Math.Sign(x.K - y.K);
                    }
                : (x, y) =>
                    {
                        log.Call(x?.Id, y?.Id);
                        if (cmp == "alwaysLess")
                            return -1;
                        return cmp == "desc" ? Ascending(y, x) : Ascending(x, y);
                    };
                string error = Run(() => NeoIntroSort.SortList(
                    items,
                    (int?)c["index"] ?? 0,
                    (int?)c["count"] ?? items.Count,
                    mode == "comparable" ? NeoSortHelper.Comparable : NeoSortHelper.Comparer,
                    compare,
                    null,
                    () => cmp,
                    NeoScriptErrorClassification.IsAuthoredCatchable));
                string actual = $"{error} output={Join(items.Select(item => item?.Id ?? -1))} calls={Join(log.Calls)}";
                Check(failures, c, actual, $"{ExpectedError(c)} output={Join(Ints(c["output"]!))} calls={Join(Ints(c["calls"]!))}");
            }
            Report(failures);
        }

        [Test]
        public void SortsFloatsWithNaNsMovedToTheFront()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("floatSort"))
            {
                var items = c["input"]!.Select(DecodeFloat).ToList();
                NeoIntroSort.SortList(
                    items,
                    (int?)c["index"] ?? 0,
                    (int?)c["count"] ?? items.Count,
                    NeoSortHelper.Comparable,
                    CompareFloat,
                    float.IsNaN,
                    () => "float",
                    NeoScriptErrorClassification.IsAuthoredCatchable);
                Check(failures, c, Join(items.Select(EncodeFloat)), Join(c["output"]!.Select(value => EncodeFloat(DecodeFloat(value)))));
            }
            Report(failures);
        }

        [Test]
        public void OrdersStablyByEveryLevelWithKeysComputedOncePerElement()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("order"))
            {
                var levelCases = c["levels"]!.ToArray();
                int stride = levelCases.Length;
                int[] input = Ints(c["input"]!);
                int count = input.Length / stride;
                int[] source = Enumerable.Range(0, count).ToArray();
                var log = new Log((int?)c["throwAt"] ?? 0);
                int[]? output = null;
                string error = Run(() =>
                {
                    // .NET's ToList computes no keys for fewer than two elements.
                    var keys = levelCases.Select(_ => new List<Item?>()).ToArray();
                    if (count >= 2)
                    {
                        for (int level = 0; level < stride; level++)
                        {
                            foreach (int id in source)
                            {
                                log.Call(0, level, id);
                                int k = input[id * stride + level];
                                keys[level].Add(k == -1 ? null : new Item(k, id));
                            }
                        }
                    }
                    var levels = levelCases.Select((level, index) => new NeoOrderLevel<Item?>(
                        keys[index],
                        LevelCompare((string)level["kind"]!, index, log),
                        (bool)level["desc"]!,
                        (string)level["kind"]! == "struct")).ToArray();
                    output = NeoEnumerableOrdering.OrderBy(source, levels, () => OrderComparerText, NeoScriptErrorClassification.IsAuthoredCatchable);
                });
                string actual = $"{error} output={(output is null ? "-" : Join(output))} calls={Join(log.Calls)}";
                string wanted = $"{ExpectedError(c)} output={(c["output"] is JArray o ? Join(Ints(o)) : "-")} calls={Join(Ints(c["calls"]!))}";
                Check(failures, c, actual, wanted);
            }
            Report(failures);
        }

        [Test]
        public void FindsMinMaxMinByAndMaxByWithDotNetEmptyNullAndNaNRules()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("minMax"))
            {
                var log = new Log((int?)c["throwAt"] ?? 0);
                string result = "-";
                string error = Run(() => result = MinMaxCase(c, log));
                string actual = $"{error} result={result}" + (c["calls"] is null ? "" : $" calls={Join(log.Calls)}");
                string wanted = $"{ExpectedError(c)} result={(c["result"] is null ? "-" : Encode(c["result"]!))}"
                    + (c["calls"] is JArray calls ? $" calls={Join(Ints(calls))}" : "");
                Check(failures, c, actual, wanted);
            }
            Report(failures);
        }

        [Test]
        public void ComparesObjectsAsTheNonGenericComparerDefault()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("comparerDefault"))
            {
                var objects = new Dictionary<int, JObject>();
                JObject? Make(JToken? value)
                {
                    if (value is not JObject o)
                        return null;
                    int id = (int)o["id"]!;
                    if (!objects.TryGetValue(id, out JObject? existing))
                        objects[id] = existing = o;
                    return existing;
                }
                var log = new Log();
                JObject? x = Make(c["x"]);
                JObject? y = Make(c["y"]);
                string result = "-";
                string error = Run(() => result = NeoComparers.CompareObjects(
                    x,
                    y,
                    (a, b) => ReferenceEquals(a, b),
                    o => (bool)o["c"]!,
                    (o, other) =>
                    {
                        log.Call(1, 0, (int)o["id"]!, (int?)other?["id"]);
                        return other is not null && (bool)other["c"]! ? Math.Sign((int)o["k"]! - (int)other["k"]!) : 1;
                    }).ToString());
                string actual = $"{error} result={result} calls={Join(log.Calls)}";
                string wanted = $"{ExpectedError(c)} result={(c["result"] is null ? "-" : (string)c["result"]!)} calls={Join(Ints(c["calls"]!))}";
                Check(failures, c, actual, wanted);
            }
            Report(failures);
        }

        private static string MinMaxCase(JToken c, Log log)
        {
            string op = (string)c["op"]!;
            string elem = (string)c["elem"]!;
            string? key = (string?)c["key"];
            NeoExtreme which = op.StartsWith("Min", StringComparison.Ordinal) ? NeoExtreme.Min : NeoExtreme.Max;
            if (elem == "string")
            {
                var strings = c["input"]!.Select(s => (string?)s).ToArray();
                string? r = NeoEnumerableOrdering.MinMax<string?, string?>(
                    strings,
                    s => s,
                    which,
                    (x, y) => NeoComparers.CompareComparable(x, y, (a, b) => string.CompareOrdinal(a, b)),
                    true);
                return r ?? "null";
            }
            if (elem == "float" || elem == "float?")
            {
                var values = c["input"]!.Select(f => f.Type == JTokenType.Null ? (float?)null : DecodeFloat(f)).ToArray();
                bool nullable = elem == "float?";
                float? f = c["selector"] is null
                    ? NeoEnumerableOrdering.MinMaxFloat32(values, v => v, which, nullable)
                    : NeoEnumerableOrdering.MinMaxFloat32(Enumerable.Range(0, values.Length).ToArray(), id =>
                    {
                        log.Call(0, 0, id);
                        return values[id];
                    }, which, nullable);
                return f is null ? "null" : EncodeFloat(f.Value);
            }
            int[] keys = Ints(c["input"]!);
            int[] ids = Enumerable.Range(0, keys.Length).ToArray();
            Item? KeyOf(int id) => keys[id] == -1 ? null : new Item(keys[id], id);
            Item? Select(int id)
            {
                log.Call(0, 0, id);
                return KeyOf(id);
            }
            string keyKind = key ?? elem;
            Comparison<Item?> compare = LevelCompare(c["comparer"] is null ? keyKind : "comparer", 0, log);
            if (op is "MinBy" or "MaxBy")
            {
                // Boxed ids, so an empty nullable source can return null.
                object? r = NeoEnumerableOrdering.MinMaxBy<object?, Item?>(
                    ids.Cast<object?>().ToArray(),
                    id => Select((int)id!),
                    which,
                    compare,
                    elem == "class",
                    keyKind == "class");
                return r is null ? "null" : r.ToString()!;
            }
            Item? result = key is null
                ? NeoEnumerableOrdering.MinMax(ids.Select(KeyOf).ToArray(), item => item, which, compare, elem == "class")
                : NeoEnumerableOrdering.MinMax(ids, Select, which, compare, key == "class");
            return result is null ? "null" : result.Id.ToString();
        }

        /// <summary>A level's comparer: the key's <c>CompareTo</c> through <c>Comparer&lt;K&gt;.Default</c>, or an explicit comparer.</summary>
        private static Comparison<Item?> LevelCompare(string kind, int level, Log log)
        {
            int CompareTo(Item x, Item y)
            {
                log.Call(1, level, x.Id, y.Id);
                return Math.Sign(x.K - y.K);
            }
            if (kind == "class")
                return (x, y) => NeoComparers.CompareComparable(x, y, (a, b) => CompareTo(a!, b!));
            if (kind == "struct")
                return (x, y) => CompareTo(x!, y!);
            return (x, y) =>
            {
                log.Call(1, level, x?.Id, y?.Id);
                return kind == "alwaysLess" ? -1 : Ascending(x, y);
            };
        }

        private static int Ascending(Item? x, Item? y) => x is null ? (y is null ? 0 : -1) : y is null ? 1 : Math.Sign(x.K - y.K);

        /// <summary><c>float.CompareTo</c>: NaN first and equal to itself.</summary>
        private static int CompareFloat(float x, float y)
        {
            if (x < y)
                return -1;
            if (x > y)
                return 1;
            if (x == y)
                return 0;
            if (float.IsNaN(x))
                return float.IsNaN(y) ? 0 : -1;
            return 1;
        }

        private static float DecodeFloat(JToken value) => value.Type == JTokenType.String
            ? (string)value! switch
            {
                "NaN" => float.NaN,
                "Infinity" => float.PositiveInfinity,
                "-Infinity" => float.NegativeInfinity,
                "-0" => -0f,
                _ => throw new InvalidOperationException((string)value!),
            }
        : (float)(double)value;

        // Shortest round-trip text in both directions, so encoded floats compare as the fixture's JSON text.
        private static string EncodeFloat(float value)
        {
            if (float.IsNaN(value))
                return "NaN";
            if (float.IsPositiveInfinity(value))
                return "Infinity";
            if (float.IsNegativeInfinity(value))
                return "-Infinity";
            if (value == 0 && float.IsNegative(value))
                return "-0";
            return ((double)value).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string Encode(JToken value) => value.Type switch
        {
            JTokenType.Null => "null",
            JTokenType.String => (string)value!,
            JTokenType.Float => EncodeFloat((float)(double)value),
            _ => value.ToString(),
        };

        private static IEnumerable<JToken> Cases(string section) => (JArray)Fixture[section]!;

        private static int[] Ints(JToken values) => values.Select(value => (int)value).ToArray();

        private static string Join<T>(IEnumerable<T> values) => string.Join(",", values);

        private static string Run(Action action)
        {
            try
            {
                action();
                return "ok";
            }
            catch (NSGetterRuntimeError error)
            {
                if (error.Message == "boom")
                    return "raw: boom";
                string inner = error.InnerException is null ? "" : $" inner={error.InnerException.Message}";
                return $"{error.Payload.ClassId}: {error.Message}{inner}";
            }
        }

        private static string ExpectedError(JToken c)
        {
            string? e = (string?)c["e"];
            if (e is null)
                return "ok";
            if (e == "raw")
                return "raw: boom";
            string inner = c["inner"] is null ? "" : $" inner={(string)c["inner"]!}";
            return $"{ClassId(e)}: {(string)c["m"]!}{inner}";
        }

        private static string ClassId(string errorClass) => errorClass switch
        {
            "NeoArgumentError" => new NeoRuntimeError(NeoErrorClass.NeoArgumentError, "").Payload.ClassId,
            "NeoArgumentOutOfRangeError" => new NeoRuntimeError(NeoErrorClass.NeoArgumentOutOfRangeError, "").Payload.ClassId,
            "NeoInvalidOperationError" => new NeoRuntimeError(NeoErrorClass.NeoInvalidOperationError, "").Payload.ClassId,
            _ => throw new InvalidOperationException(errorClass),
        };

        private static void Check(List<string> failures, JToken c, string actual, string wanted)
        {
            if (actual != wanted)
                failures.Add($"{c.ToString(Newtonsoft.Json.Formatting.None)}\n  got  {actual}\n  want {wanted}");
        }

        private static void Report(List<string> failures)
        {
            Assert.That(failures, Is.Empty, $"{failures.Count} mismatches:\n" + string.Join("\n", failures.Take(10)));
        }

        private sealed class Item
        {
            internal Item(int k, int id)
            {
                K = k;
                Id = id;
            }

            internal int K
            {
                get;
            }

            internal int Id
            {
                get;
            }
        }

        /// <summary>The fixture's logging callbacks: each logs, then the <c>throwAt</c>-th throws a catchable "boom".</summary>
        private sealed class Log
        {
            private readonly int throwAt;
            private int count;

            internal Log(int throwAt = 0) => this.throwAt = throwAt;

            internal List<int> Calls { get; } = new List<int>();

            internal void Call(params int?[] entry)
            {
                foreach (int? part in entry)
                    Calls.Add(part ?? -1);
                if (++count == throwAt)
                    throw new NSGetterRuntimeError("boom");
            }
        }
    }
}
