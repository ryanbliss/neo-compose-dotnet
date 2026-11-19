// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime.NeoScript.Ordering
{
    /// <summary>One <c>OrderBy</c> or <c>ThenBy</c> level.</summary>
    internal sealed class NeoOrderLevel<K>
    {
        /// <param name="keys">The level's keys, one per source element, in source order.</param>
        /// <param name="compare">The level's comparer, or <c>Comparer&lt;K&gt;.Default</c>.</param>
        /// <param name="descending"><c>OrderByDescending</c> or <c>ThenByDescending</c>.</param>
        /// <param name="defaultValueType">
        /// <c>K</c> is a value type compared by <c>Comparer&lt;K&gt;.Default</c>.
        /// With one level, .NET then compares descending keys as
        /// <c>Compare(right, left)</c>, which an authored <c>CompareTo</c> can
        /// observe.
        /// </param>
        internal NeoOrderLevel(IReadOnlyList<K> keys, Comparison<K> compare, bool descending, bool defaultValueType = false)
        {
            Keys = keys;
            Compare = compare;
            Descending = descending;
            DefaultValueType = defaultValueType;
        }

        internal IReadOnlyList<K> Keys
        {
            get;
        }

        internal Comparison<K> Compare
        {
            get;
        }

        internal bool Descending
        {
            get;
        }

        internal bool DefaultValueType
        {
            get;
        }
    }

    /// <summary><c>Min</c> returns the least element, <c>Max</c> the greatest.</summary>
    internal enum NeoExtreme
    {
        Min,
        Max,
    }

    /// <summary>
    /// P127 §10.2 ordering query operators, transcribed from .NET 10's
    /// System.Linq (OrderedEnumerable.cs, Min.cs, Max.cs), dotnet/runtime
    /// release/10.0 at 8f4c012be1d11eef2a7c1991cdb6302479f23b74. The compose
    /// twin is <c>ordering/enumerable.ts</c>.
    /// </summary>
    internal static class NeoEnumerableOrdering
    {
        /// <summary>
        /// <c>OrderBy(…).ThenBy(…)…ToList()</c>: <c>EnumerableSorter</c>'s
        /// stable sort over an index map, by every level in turn, then source
        /// position. An eager <c>ThenBy</c> passes every level so far, so it
        /// sorts again from source order and lands where .NET's single sort
        /// does.
        /// </summary>
        /// <param name="comparerText">
        /// The inconsistent-comparer message's <c>{comparer}</c>; .NET reports
        /// its internal <c>Comparison&lt;int&gt;</c>.
        /// </param>
        internal static T[] OrderBy<T, K>(
            IReadOnlyList<T> source,
            IReadOnlyList<NeoOrderLevel<K>> levels,
            Func<string> comparerText,
            Func<Exception, bool> isCatchable)
        {
            int n = source.Count;
            var map = new int[n];
            for (int i = 0; i < n; i++)
                map[i] = i;
            int last = levels.Count - 1;
            NeoOrderLevel<K> only = levels[0];
            Comparison<int> compareIndexes;
            if (last == 0 && only.DefaultValueType)
            {
                IReadOnlyList<K> keys = only.Keys;
                Comparison<K> compare = only.Compare;
                if (only.Descending)
                {
                    compareIndexes = (a, b) =>
                    {
                        int c = compare(keys[b], keys[a]);
                        return c != 0 ? c : a - b;
                    };
                }
                else
                {
                    compareIndexes = (a, b) =>
                    {
                        int c = compare(keys[a], keys[b]);
                        return c != 0 ? c : a - b;
                    };
                }
            }
            else
            {
                compareIndexes = (a, b) =>
                {
                    for (int level = 0; ; level++)
                    {
                        NeoOrderLevel<K> current = levels[level];
                        int c = current.Compare(current.Keys[a], current.Keys[b]);
                        if (c != 0)
                            return current.Descending != c > 0 ? 1 : -1;
                        if (level == last)
                            return a - b;
                    }
                };
            }
            NeoIntroSort.SortComparer(map, compareIndexes, comparerText, isCatchable);
            var result = new T[n];
            for (int i = 0; i < n; i++)
                result[i] = source[map[i]];
            return result;
        }

        private static bool Better(NeoExtreme which, int c) => which == NeoExtreme.Min ? c < 0 : c > 0;

        /// <summary>
        /// <c>Min()</c>, <c>Max()</c>, <c>Min(selector)</c> and
        /// <c>Max(selector)</c> over any type (pass <c>x =&gt; x</c> for
        /// none). An empty or all-null nullable sequence gives null; an empty
        /// non-nullable one throws.
        /// </summary>
        /// <param name="nullable">
        /// The element (or selector result) type is a reference or nullable
        /// type: nulls are skipped.
        /// </param>
        internal static R? MinMax<S, R>(IEnumerable<S> source, Func<S, R> select, NeoExtreme which, Comparison<R> compare, bool nullable)
        {
            using var iterator = source.GetEnumerator();
            R value;
            do
            {
                if (!iterator.MoveNext())
                {
                    if (nullable)
                        return default;
                    throw NeoOrderingErrors.NoElements();
                }
                value = select(iterator.Current);
            } while (nullable && value is null);
            while (iterator.MoveNext())
            {
                R next = select(iterator.Current);
                if ((!nullable || next is not null) && Better(which, compare(next, value)))
                    value = next;
            }
            return value;
        }

        /// <summary>
        /// <c>MinBy</c> and <c>MaxBy</c>: the first element with the extreme
        /// key, or the first element when every key is null.
        /// </summary>
        /// <param name="sourceNullable">
        /// The source element type is a reference or nullable type: an empty
        /// source gives null.
        /// </param>
        /// <param name="keyNullable">
        /// The key type is a reference or nullable type: null keys are skipped.
        /// </param>
        internal static S? MinMaxBy<S, K>(
            IEnumerable<S> source,
            Func<S, K> keySelector,
            NeoExtreme which,
            Comparison<K> compare,
            bool sourceNullable,
            bool keyNullable)
        {
            using var iterator = source.GetEnumerator();
            if (!iterator.MoveNext())
            {
                if (sourceNullable)
                    return default;
                throw NeoOrderingErrors.NoElements();
            }
            S value = iterator.Current;
            K key = keySelector(value);
            if (keyNullable && key is null)
            {
                S first = value;
                do
                {
                    if (!iterator.MoveNext())
                        return first;
                    value = iterator.Current;
                    key = keySelector(value);
                } while (key is null);
            }
            while (iterator.MoveNext())
            {
                S next = iterator.Current;
                K nextKey = keySelector(next);
                if ((!keyNullable || nextKey is not null) && Better(which, compare(nextKey, key)))
                {
                    key = nextKey;
                    value = next;
                }
            }
            return value;
        }

        /// <summary>
        /// <c>Min</c> and <c>Max</c> over <c>float</c> and <c>float?</c>, with
        /// or without a selector. NaN is less than every number: <c>Min</c>
        /// returns it at the first one, without selecting further, and
        /// <c>Max</c> skips them.
        /// </summary>
        /// <param name="nullable">
        /// <c>float?</c> rather than <c>float</c>: nulls are skipped and an
        /// empty sequence gives null.
        /// </param>
        internal static float? MinMaxFloat32<S>(IEnumerable<S> source, Func<S, float?> select, NeoExtreme which, bool nullable)
        {
            using var iterator = source.GetEnumerator();
            float? first;
            do
            {
                if (!iterator.MoveNext())
                {
                    if (nullable)
                        return null;
                    throw NeoOrderingErrors.NoElements();
                }
                first = select(iterator.Current);
            } while (nullable && first is null);
            float value = first!.Value;
            if (which == NeoExtreme.Min)
            {
                if (float.IsNaN(value))
                    return value;
                while (iterator.MoveNext())
                {
                    float? next = select(iterator.Current);
                    if (next is null)
                        continue;
                    if (next.Value < value)
                        value = next.Value;
                    else if (float.IsNaN(next.Value))
                        return next.Value;
                }
            }
            else
            {
                while (float.IsNaN(value))
                {
                    if (!iterator.MoveNext())
                        return value;
                    float? next = select(iterator.Current);
                    if (next is not null)
                        value = next.Value;
                }
                while (iterator.MoveNext())
                {
                    float? next = select(iterator.Current);
                    if (next is not null && next.Value > value)
                        value = next.Value;
                }
            }
            return value;
        }
    }
}
