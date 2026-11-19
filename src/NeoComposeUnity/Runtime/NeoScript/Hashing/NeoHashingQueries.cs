// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime.NeoScript.Hashing
{
    /// <summary>A <c>GroupBy</c> group: its key and its elements in source order.</summary>
    internal sealed class NeoGrouping<TKey, TElement>
    {
        internal NeoGrouping(TKey key)
        {
            Key = key;
        }

        internal TKey Key
        {
            get;
        }

        internal List<TElement> Elements
        {
            get;
        } = new List<TElement>();
    }

    /// <summary>
    /// The hashing query operators, eager as NeoScript's are, with the calls
    /// .NET 10's <c>ToList()</c> makes (System.Linq Distinct.SpeedOpt.cs,
    /// Grouping.SpeedOpt.cs, Lookup.cs and ToCollection.cs; dotnet/runtime
    /// release/10.0 at 8f4c012be1d11eef2a7c1991cdb6302479f23b74). The compose
    /// twin is <c>hashing/queries.ts</c>.
    /// </summary>
    internal static class NeoHashingQueries
    {
        /// <summary>
        /// <c>Distinct(comparer)</c>: <c>new HashSet&lt;T&gt;(source, comparer)</c>'s
        /// elements. A source that is a set with an equal comparer is already
        /// distinct and runs no comparer (<see cref="NeoHashTable{TKey, TValue}.CopySet"/>).
        /// </summary>
        internal static List<T> Distinct<T>(IReadOnlyList<T> source, IEqualityComparer<T> comparer) =>
            NeoHashTable<T, bool>.FromValues(comparer, source, source.Count).Keys();

        /// <summary>
        /// <c>GroupBy(keySelector, elementSelector, comparer)</c> (pass
        /// <c>x =&gt; x</c> for no element selector), as <c>Lookup.Create</c>
        /// builds it. Per element: the key selector, the key's hash masked to
        /// 31 bits (none for a null key), <c>Equals</c> on each earlier group
        /// with that masked hash, newest first, then the element selector.
        /// Groups keep first-seen order.
        /// </summary>
        internal static List<NeoGrouping<TKey, TElement>> GroupBy<TSource, TKey, TElement>(
            IReadOnlyList<TSource> source,
            Func<TSource, TKey> keySelector,
            Func<TSource, TElement> elementSelector,
            IEqualityComparer<TKey> comparer)
        {
            var groups = new List<NeoGrouping<TKey, TElement>>();
            // Lookup chains hold groups newest first and never remove one, so each masked hash's
            // groups in creation order, scanned from the end, are the chain's equal-hash entries.
            var byHash = new Dictionary<int, List<NeoGrouping<TKey, TElement>>>();
            foreach (TSource item in source)
            {
                TKey key = keySelector(item);
                int hashCode = key is null ? 0 : comparer.GetHashCode(key) & 0x7FFFFFFF;
                NeoGrouping<TKey, TElement>? group = null;
                if (byHash.TryGetValue(hashCode, out List<NeoGrouping<TKey, TElement>>? chain))
                {
                    for (int i = chain.Count - 1; i >= 0; i--)
                    {
                        if (comparer.Equals(chain[i].Key, key))
                        {
                            group = chain[i];
                            break;
                        }
                    }
                }
                else
                {
                    chain = new List<NeoGrouping<TKey, TElement>>();
                    byHash.Add(hashCode, chain);
                }
                if (group is null)
                {
                    group = new NeoGrouping<TKey, TElement>(key);
                    chain.Add(group);
                    groups.Add(group);
                }
                group.Elements.Add(elementSelector(item));
            }
            return groups;
        }

        /// <summary>
        /// <c>ToDictionary(keySelector, elementSelector, comparer)</c> (pass
        /// <c>x =&gt; x</c> for no element selector). Per element: the key
        /// selector, the element selector, then <c>Add</c>, which throws for a
        /// null or duplicate key.
        /// </summary>
        internal static NeoHashTable<TKey, TValue> ToDictionary<TSource, TKey, TValue>(
            IReadOnlyList<TSource> source,
            Func<TSource, TKey> keySelector,
            Func<TSource, TValue> elementSelector,
            IEqualityComparer<TKey> comparer,
            Func<TKey, string> keyText)
        {
            var table = new NeoHashTable<TKey, TValue>(comparer, NeoHashTableKind.Dictionary, source.Count, keyText);
            foreach (TSource element in source)
            {
                TKey key = keySelector(element);
                TValue value = elementSelector(element);
                table.Add(key, value, NeoInsertBehavior.Throw);
            }
            return table;
        }
    }
}
