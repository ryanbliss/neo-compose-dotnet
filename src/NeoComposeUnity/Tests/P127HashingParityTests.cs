// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NeoCompose.Runtime.NeoScript;
using NeoCompose.Runtime.NeoScript.Hashing;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace NeoCompose.Tests
{
    /// <summary>
    /// P127 §7.4, §8.1, §8.4 to §8.6 and §9.4: replays the .NET 10 hashing
    /// fixture the compose runtime shares. Each case must match the result,
    /// the error and every callback call. See the fixture's README beside the
    /// compose copy for the format.
    /// </summary>
    public class P127HashingParityTests
    {
        private const string FixturePath = "Packages/com.ryanbliss.neocompose/Tests/hashing-parity.json";

        private static JObject? fixture;

        private static JObject Fixture => fixture ??= JObject.Parse(File.ReadAllText(FixturePath));

        [Test]
        public void HashesIntsBoolsFloatsDecimalsAndStrings()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("hashes"))
            {
                string kind = (string)c["kind"]!;
                JToken v = c["v"]!;
                int hash = kind switch
                {
                    "int" => (int)v,
                    "bool" => NeoHashes.Bool((bool)v),
                    "float" => NeoHashes.Float(BitConverter.Int32BitsToSingle((int)v)),
                    "decimal" => NeoHashes.Decimal((string)v!),
                    "string" => NeoHashes.String(new string(Ints(v).Select(unit => (char)unit).ToArray())),
                    _ => throw new InvalidOperationException(kind),
                };
                Check(failures, c, Text(hash), (string)c["hash"]!);
            }
            Report(failures);
        }

        [Test]
        public void HashesVectorsAndColorsAsUnity()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("unity"))
            {
                int[] v = Ints(c["v"]!);
                float[] f = v.Select(BitConverter.Int32BitsToSingle).ToArray();
                string type = (string)c["type"]!;
                int hash = type switch
                {
                    "Vector2" => NeoHashes.Vector2(f[0], f[1]),
                    "Vector3" => NeoHashes.Vector3(f[0], f[1], f[2]),
                    "Color" => NeoHashes.Color(f[0], f[1], f[2], f[3]),
                    "Vector2Int" => NeoHashes.Vector2Int(v[0], v[1]),
                    "Vector3Int" => NeoHashes.Vector3Int(v[0], v[1], v[2]),
                    _ => throw new InvalidOperationException(type),
                };
                Check(failures, c, Text(hash), (string)c["hash"]!);
            }
            Report(failures);
        }

        [Test]
        public void RunsTupleMembersWithValueTuplesCalls()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("tuple"))
            {
                var log = new Log();
                string op = (string)c["op"]!;
                string? other = (string?)c["other"];
                List<object?> x = TupleItems(c["x"]!, 0);
                List<object?>? y = c["y"] is JArray ys ? TupleItems(ys, 100) : null;
                Func<string> typeName = () => (string?)c["type"] ?? "";
                var comparer = new StructuralComparer(log, typeName);
                string result = "-";
                string error = Run(() => result = op switch
                {
                    // Equals(object?) of null or another type is false before any element runs.
                    "eq" => Bool(y is not null && NeoTuples.TupleEquals(x, y, (i, a, b) => DefaultEquals(log, a, b))),
                    "cmp" => Text(NeoTuples.TupleCompareToObject(x, y, other == "null", (i, a, b) => DefaultCompare(log, a, b), typeName)),
                    "hash" => Text(NeoTuples.TupleHash(x, (i, item) => DefaultHash(log, item))),
                    "seq" => Bool(NeoTuples.TupleStructuralEquals(x, y, comparer)),
                    "scmp" => Text(NeoTuples.TupleStructuralCompareTo(x, y, other == "null", comparer, typeName)),
                    "shash" => Text(NeoTuples.TupleStructuralHash(x, comparer)),
                    _ => throw new InvalidOperationException(op),
                });
                string wanted = c["r"] is null ? "-" : Encode(c["r"]!);
                Check(failures, c, $"{error} r={result} calls={Join(log.Calls)}", $"{ExpectedError(c)} r={wanted} calls={Join(Ints(c["calls"]!))}");
            }
            Report(failures);
        }

        [Test]
        public void RunsHashSetScriptsWithDotnetSlots()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("hashSet"))
            {
                var log = new Log();
                int mod = (int)c["mod"]!;
                var comparer = new TableComparer(log, mod);
                var results = new List<int>();
                NeoHashTable<Item?, bool>? set = null;
                string error = Run(() =>
                {
                    NeoHashTable<Item?, bool> table;
                    if (c["from"] is JArray from)
                    {
                        Item?[] items = Keys(from).Select((key, i) => ItemOf(key, 1000 + i)).ToArray();
                        table = NeoHashTable<Item?, bool>.FromValues(comparer, items, items.Length);
                    }
                    else
                        table = new NeoHashTable<Item?, bool>(comparer, NeoHashTableKind.Set, (int?)c["capacity"] ?? 0);
                    JArray ops = (JArray)c["ops"]!;
                    for (int i = 0; i < ops.Count; i++)
                    {
                        Item? item = ItemOf((int?)ops[i][1], i);
                        switch ((int)ops[i][0]!)
                        {
                            case 0:
                                results.Add(table.Add(item, false, NeoInsertBehavior.None) ? 1 : 0);
                                break;
                            case 1:
                                results.Add(table.Remove(item) ? 1 : 0);
                                break;
                            case 2:
                                results.Add(table.Contains(item) ? 1 : 0);
                                break;
                            case 3:
                                table.Clear();
                                results.Add(-1);
                                break;
                            case 4:
                                table = NeoHashTable<Item?, bool>.CopySet(table);
                                results.Add(-1);
                                break;
                            case 5:
                                comparer = new TableComparer(log, mod);
                                table = NeoHashTable<Item?, bool>.FromValues(comparer, table.Keys(), table.Count);
                                results.Add(-1);
                                break;
                        }
                    }
                    set = table;
                });
                string actual = set is null
                    ? $"{error} calls={Join(log.Calls)}"
                    : $"{error} r={Join(results)} out={Join(set.Keys().Select(IdOf))} calls={Join(log.Calls)}";
                string wanted = c["r"] is null
                    ? $"{ExpectedError(c)} calls={Join(Ints(c["calls"]!))}"
                    : $"{ExpectedError(c)} r={Join(Ints(c["r"]!))} out={Join(Ints(c["out"]!))} calls={Join(Ints(c["calls"]!))}";
                Check(failures, c, actual, wanted);
            }
            Report(failures);
        }

        [Test]
        public void RunsDictionaryScriptsWithDotnetSlotsAndErrors()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("dictionary"))
            {
                var log = new Log();
                int mod = (int)c["mod"]!;
                var comparer = new TableComparer(log, mod);
                var results = new List<int>();
                NeoHashTable<Item?, int>? table = null;
                string error = Run(() =>
                {
                    table = new NeoHashTable<Item?, int>(comparer, NeoHashTableKind.Dictionary, (int?)c["capacity"] ?? 0, KeyText);
                    JArray ops = (JArray)c["ops"]!;
                    for (int i = 0; i < ops.Count; i++)
                    {
                        Item? item = ItemOf((int?)ops[i][1], i);
                        switch ((int)ops[i][0]!)
                        {
                            case 0:
                                table.Add(item, i, NeoInsertBehavior.Throw);
                                results.Add(-1);
                                break;
                            case 1:
                                table.Add(item, i, NeoInsertBehavior.Overwrite);
                                results.Add(-1);
                                break;
                            case 2:
                                results.Add(table.Add(item, i, NeoInsertBehavior.None) ? 1 : 0);
                                break;
                            case 3:
                                results.Add(table.Remove(item) ? 1 : 0);
                                break;
                            case 4:
                                results.Add(table.Contains(item) ? 1 : 0);
                                break;
                            case 5:
                                int index = table.IndexOf(item);
                                results.Add(index >= 0 ? table.ValueAt(index) : -2);
                                break;
                            case 6:
                                table.Clear();
                                results.Add(-1);
                                break;
                            case 7:
                                table = NeoHashTable<Item?, int>.CopyDictionary(table, comparer, KeyText);
                                results.Add(-1);
                                break;
                            case 8:
                                comparer = new TableComparer(log, mod);
                                table = NeoHashTable<Item?, int>.CopyDictionary(table, comparer, KeyText);
                                results.Add(-1);
                                break;
                        }
                    }
                });
                string actual = $"{error} r={Join(results)} out={(table is null ? "-" : Pairs(table))} calls={Join(log.Calls)}";
                string wantedOut = c["out"] is JArray o ? string.Join(";", o.Select(pair => Join(Ints(pair)))) : "-";
                Check(failures, c, actual, $"{ExpectedError(c)} r={Join(Ints(c["r"]!))} out={wantedOut} calls={Join(Ints(c["calls"]!))}");
                if (c["at"] is JToken at && results.Count != (int)at)
                    failures.Add($"{c.ToString(Newtonsoft.Json.Formatting.None)}\n  stopped at {results.Count}");
            }
            Report(failures);
        }

        [Test]
        public void RunsDistinctWithDotnetCalls()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("distinct"))
            {
                var log = new Log();
                List<Item?> items = Keys(c["keys"]!).Select((key, i) => ItemOf(key, i)).ToList();
                List<Item?> result = NeoHashingQueries.Distinct<Item?>(items, new TableComparer(log, (int)c["mod"]!));
                Check(failures, c, $"out={Join(result.Select(IdOf))} calls={Join(log.Calls)}", $"out={Join(Ints(c["out"]!))} calls={Join(Ints(c["calls"]!))}");
            }
            Report(failures);
        }

        [Test]
        public void RunsGroupByWithDotnetCalls()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("groupBy"))
            {
                var log = new Log();
                List<Source> source = Sources(c["keys"]!);
                var groups = NeoHashingQueries.GroupBy<Source, Item?, int>(
                    source,
                    s => s.SelectKey(log),
                    s => s.SelectElement(log),
                    new TableComparer(log, (int)c["mod"]!));
                string actual = string.Join(";", groups.Select(g => $"{IdOf(g.Key)}:{Join(g.Elements)}"));
                string wanted = string.Join(";", ((JArray)c["out"]!).Select(g => $"{(int)g[0]!}:{Join(Ints(g[1]!))}"));
                Check(failures, c, $"out={actual} calls={Join(log.Calls)}", $"out={wanted} calls={Join(Ints(c["calls"]!))}");
            }
            Report(failures);
        }

        [Test]
        public void RunsToDictionaryWithDotnetCallsAndErrors()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("toDictionary"))
            {
                var log = new Log();
                List<Source> source = Sources(c["keys"]!);
                string result = "-";
                string error = Run(() => result = Pairs(NeoHashingQueries.ToDictionary<Source, Item?, int>(
                    source,
                    s => s.SelectKey(log),
                    s => s.SelectElement(log),
                    new TableComparer(log, (int)c["mod"]!),
                    KeyText)));
                string wantedOut = c["out"] is JArray o ? string.Join(";", o.Select(pair => Join(Ints(pair)))) : "-";
                Check(failures, c, $"{error} out={result} calls={Join(log.Calls)}", $"{ExpectedError(c)} out={wantedOut} calls={Join(Ints(c["calls"]!))}");
            }
            Report(failures);
        }

        [Test]
        public void NumbersOrphansByFnv1a()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("orphan"))
                Check(failures, c, Text(NeoEnums.OrphanValue((string)c["id"]!)), (string)c["value"]!);
            Report(failures);
        }

        [Test]
        public void FormatsEnumValues()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("enumFormat"))
            {
                string result = "-";
                string error = Run(() => result = NeoEnums.Format((int)c["v"]!, (string)c["name"]!, (string?)c["f"]));
                Check(failures, c, $"{error} out={result}", $"{ExpectedError(c)} out={(string?)c["out"] ?? "-"}");
            }
            Report(failures);
        }

        [Test]
        public void ComparesEnumValues()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("enumCompare"))
            {
                string result = "-";
                string error = Run(() => result = Text(NeoEnums.CompareToObject((int)c["v"]!, (string?)c["enum"] ?? "Planet", ArgumentOf(c["t"], c["type"]))));
                Check(failures, c, $"{error} r={result}", $"{ExpectedError(c)} r={(string?)c["r"] ?? "-"}");
            }
            Report(failures);
        }

        [Test]
        public void TestsFlags()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("hasFlag"))
            {
                string result = "-";
                string enumType = (string?)c["enumType"] ?? (string)c["enum"]!;
                string error = Run(() => result = Bool(NeoEnums.HasFlag((int)c["v"]!, enumType, ArgumentOf(c["flag"], c["type"]))));
                Check(failures, c, $"{error} r={result}", $"{ExpectedError(c)} r={(c["r"] is null ? "-" : Encode(c["r"]!))}");
            }
            Report(failures);
        }

        [Test]
        public void FormatsNumberStylesAsDotnetsFlagsEnum()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("numberStyles"))
            {
                string result = "-";
                string error = Run(() => result = NeoNumberStyles.Format((int)c["v"]!, (string?)c["f"]));
                Check(failures, c, $"{error} out={result}", $"{ExpectedError(c)} out={(string?)c["out"] ?? "-"}");
            }
            Report(failures);
        }

        [Test]
        public void ComparesBools()
        {
            var failures = new List<string>();
            foreach (JToken c in Cases("boolCompare"))
            {
                JToken t = c["t"]!;
                bool? target = t.Type == JTokenType.Boolean ? (bool)t : null;
                string result = "-";
                string error = Run(() => result = Text(NeoEnums.BoolCompareToObject((bool)c["v"]!, target, t.Type == JTokenType.Null)));
                Check(failures, c, $"{error} r={result}", $"{ExpectedError(c)} r={(string?)c["r"] ?? "-"}");
            }
            Report(failures);
        }

        private static NeoEnumArgument? ArgumentOf(JToken? t, JToken? typeName)
        {
            if (t is null || t.Type == JTokenType.Null)
                return null;
            if (t.Type == JTokenType.String)
                return new NeoEnumArgument(false, (string)typeName!, 0);
            return new NeoEnumArgument(true, "", (int)t);
        }

        private static List<object?> TupleItems(JToken keys, int idBase) =>
            Keys(keys).Select((key, i) => key is int k ? new Item(idBase + i, k) : (object?)null).ToList();

        // EqualityComparer<TupleItem>.Default and Comparer<TupleItem>.Default over logging elements.
        private static bool DefaultEquals(Log log, object? a, object? b)
        {
            if (a is null)
                return b is null;
            if (b is null)
                return false;
            var x = (Item)a;
            var y = (Item)b;
            log.Call(1, x.Id, y.Id);
            return x.Key == y.Key;
        }

        private static int DefaultCompare(Log log, object? a, object? b)
        {
            if (a is null)
                return b is null ? 0 : -1;
            if (b is null)
                return 1;
            var x = (Item)a;
            var y = (Item)b;
            log.Call(3, x.Id, y.Id);
            return Math.Sign(x.Key - y.Key);
        }

        private static int DefaultHash(Log log, object? value)
        {
            var item = (Item)value!;
            log.Call(2, item.Id);
            return TupleHashOf(item);
        }

        private static int TupleHashOf(Item item) => unchecked(item.Key * 1000003);

        private static Item? ItemOf(int? key, int id) => key is int k ? new Item(id, k) : null;

        private static int IdOf(Item? item) => item?.Id ?? -1;

        private static string KeyText(Item? item) => item is null ? "" : Text(item.Key);

        private static string Pairs(NeoHashTable<Item?, int> table)
        {
            List<int> values = table.Values();
            return string.Join(";", table.Keys().Select((key, i) => $"{IdOf(key)},{values[i]}"));
        }

        private static List<Source> Sources(JToken keys) => Keys(keys).Select((key, id) => new Source(id, key)).ToList();

        private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Encode(JToken value) => value.Type == JTokenType.Boolean ? Bool((bool)value) : (string)value!;

        private static IEnumerable<JToken> Cases(string section) => (JArray)Fixture[section]!;

        private static int[] Ints(JToken values) => values.Select(value => (int)value).ToArray();

        private static int?[] Keys(JToken values) => values.Select(value => (int?)value).ToArray();

        private static string Join<T>(IEnumerable<T> values) => string.Join(",", values);

        private static string Run(Action action)
        {
            try
            {
                action();
                return "ok";
            }
            catch (NeoRuntimeError error)
            {
                return $"{error.ErrorClass}: {error.Message}";
            }
        }

        private static string ExpectedError(JToken c)
        {
            string? e = (string?)c["e"];
            return e is null ? "ok" : $"{e}: {(string)c["m"]!}";
        }

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
            internal Item(int id, int key)
            {
                Id = id;
                Key = key;
            }

            internal int Id
            {
                get;
            }

            internal int Key
            {
                get;
            }
        }

        private sealed class Rest
        {
            internal Rest(List<object?> elements)
            {
                Elements = elements;
            }

            internal List<object?> Elements
            {
                get;
            }
        }

        /// <summary>A query source element; selectors log <c>2, id</c> and <c>3, id</c>.</summary>
        private sealed class Source
        {
            private readonly int id;
            private readonly int? key;

            internal Source(int id, int? key)
            {
                this.id = id;
                this.key = key;
            }

            internal Item? SelectKey(Log log)
            {
                log.Call(2, id);
                return ItemOf(key, id);
            }

            internal int SelectElement(Log log)
            {
                log.Call(3, id);
                return id;
            }
        }

        private sealed class Log
        {
            internal List<int> Calls { get; } = new List<int>();

            internal void Call(params int?[] entry)
            {
                foreach (int? part in entry)
                    Calls.Add(part ?? -1);
            }
        }

        /// <summary>The fixture's <c>TableComparer</c>: logs <c>0, id</c> per hash and <c>1, entry, candidate</c> per equals.</summary>
        private sealed class TableComparer : IEqualityComparer<Item?>
        {
            private readonly Log log;
            private readonly int mod;

            internal TableComparer(Log log, int mod)
            {
                this.log = log;
                this.mod = mod;
            }

            public bool Equals(Item? entry, Item? candidate)
            {
                log.Call(1, entry?.Id, candidate?.Id);
                if (entry is null || candidate is null)
                    return entry is null && candidate is null;
                return entry.Key == candidate.Key;
            }

            public int GetHashCode(Item? item)
            {
                log.Call(0, item?.Id);
                if (item is null)
                    return 0;
                return mod == 0 ? item.Key : item.Key % mod;
            }
        }

        /// <summary>The fixture's <c>StructuralComparer</c>: logs, and recurses into a rest through its structural members.</summary>
        private sealed class StructuralComparer : INeoStructuralEqualityComparer<object?>, INeoStructuralOrderComparer<object?>
        {
            private readonly Log log;
            private readonly Func<string> typeName;

            internal StructuralComparer(Log log, Func<string> typeName)
            {
                this.log = log;
                this.typeName = typeName;
            }

            public object? Rest(IReadOnlyList<object?> elements, int start) => new Rest(elements.Skip(start).ToList());

            public new bool Equals(object? a, object? b)
            {
                log.Call(4, RefOf(a), RefOf(b));
                if (a is Rest rest)
                    return NeoTuples.TupleStructuralEquals(rest.Elements, (b as Rest)?.Elements, this);
                if (a is null || b is null)
                    return a is null && b is null;
                return ((Item)a).Key == ((Item)b).Key;
            }

            public int Compare(object? a, object? b)
            {
                log.Call(5, RefOf(a), RefOf(b));
                if (a is Rest rest)
                    return NeoTuples.TupleStructuralCompareTo(rest.Elements, (b as Rest)?.Elements, b is null, this, typeName);
                if (a is null)
                    return b is null ? 0 : -1;
                if (b is null)
                    return 1;
                return Math.Sign(((Item)a).Key - ((Item)b).Key);
            }

            public int Hash(object? value)
            {
                log.Call(6, RefOf(value));
                if (value is Rest rest)
                    return NeoTuples.TupleStructuralHash(rest.Elements, this);
                return value is null ? 0 : TupleHashOf((Item)value);
            }

            /// <summary>An element's id, -1 for null, 1000 + first id for a rest.</summary>
            private static int RefOf(object? value) => value switch
            {
                null => -1,
                Rest rest => 1000 + ((Item)rest.Elements[0]!).Id,
                Item item => item.Id,
                _ => throw new InvalidOperationException("unexpected " + value),
            };
        }
    }
}
