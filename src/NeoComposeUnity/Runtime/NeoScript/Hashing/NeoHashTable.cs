// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime.NeoScript.Hashing
{
    /// <summary><c>HashSet&lt;T&gt;</c> allows a null element; <c>Dictionary&lt;TKey, TValue&gt;</c> rejects a null key.</summary>
    internal enum NeoHashTableKind
    {
        Set,
        Dictionary,
    }

    /// <summary>What <c>Add</c> does with a key it already holds: <c>Add</c>/<c>TryAdd</c> keep the entry, the indexer overwrites.</summary>
    internal enum NeoInsertBehavior
    {
        None,
        Overwrite,
        Throw,
    }

    /// <summary>
    /// P127 §9.4's in-memory hash table: .NET 10's <c>HashSet&lt;T&gt;</c> and
    /// <c>Dictionary&lt;TKey, TValue&gt;</c> (HashSet.cs, Dictionary.cs,
    /// HashHelpers.cs; dotnet/runtime release/10.0 at
    /// 8f4c012be1d11eef2a7c1991cdb6302479f23b74), transcribed so an authored
    /// comparer sees the same calls in the same order and enumeration follows
    /// the same slots, including free-list reuse after <c>Remove</c>. The
    /// comparer's <c>Equals</c> receives the stored entry first (§9.2), and a
    /// set never hashes null, which hashes to 0. Each entry keeps the hash
    /// computed when it was inserted; lookups store nothing. .NET's switch
    /// from its non-randomized string comparer after 100 collisions isn't
    /// transcribed: it runs no authored code. The compose twin is
    /// <c>hashing/hash-table.ts</c>.
    /// </summary>
    internal sealed class NeoHashTable<TKey, TValue>
    {
        private const int StartOfFreeList = -3;
        private const int ShrinkThreshold = 3;

        private readonly Func<TKey, string> keyText;
        private int[]? buckets;
        private int[] hashCodes = Array.Empty<int>();
        private int[] nexts = Array.Empty<int>();
        private TKey[] keyAt = Array.Empty<TKey>();
        private TValue[] valueAt = Array.Empty<TValue>();
        private int count;
        private int freeList = -1;
        private int freeCount;

        /// <summary>
        /// <c>new HashSet&lt;T&gt;(capacity, comparer)</c> or
        /// <c>new Dictionary&lt;K, V&gt;(capacity, comparer)</c>.
        /// <paramref name="keyText"/> is a key's <c>ToString()</c>, for the
        /// duplicate-key message.
        /// </summary>
        internal NeoHashTable(
            IEqualityComparer<TKey> comparer,
            NeoHashTableKind kind,
            int capacity = 0,
            Func<TKey, string>? keyText = null)
        {
            Comparer = comparer;
            Kind = kind;
            this.keyText = keyText ?? (key => key?.ToString() ?? "");
            if (capacity < 0)
                throw NeoHashingErrors.CapacityRange();
            if (capacity > 0)
                Initialize(capacity);
        }

        internal IEqualityComparer<TKey> Comparer
        {
            get;
        }

        internal NeoHashTableKind Kind
        {
            get;
        }

        internal int Count => count - freeCount;

        /// <summary>
        /// <c>new HashSet&lt;T&gt;(collection, comparer)</c> for a collection
        /// of known count that isn't a <c>HashSet</c> with an equal comparer
        /// (<c>ToSet</c>, and <c>Distinct</c> materialized by a consumer).
        /// </summary>
        internal static NeoHashTable<TKey, TValue> FromValues(
            IEqualityComparer<TKey> comparer,
            IEnumerable<TKey> values,
            int valueCount)
        {
            var table = new NeoHashTable<TKey, TValue>(comparer, NeoHashTableKind.Set);
            if (valueCount > 0)
                table.Initialize(valueCount);
            foreach (TKey value in values)
                table.Add(value, default!, NeoInsertBehavior.None);
            if (table.count > 0 && table.nexts.Length / table.count > ShrinkThreshold)
                table.TrimExcess();
            return table;
        }

        /// <summary>
        /// <c>new HashSet&lt;T&gt;(source, comparer)</c> when the source is a
        /// <c>HashSet</c> whose comparer equals this one (<c>ConstructFrom</c>):
        /// its slots are copied, or re-added when they would fit a smaller table.
        /// </summary>
        internal static NeoHashTable<TKey, TValue> CopySet(NeoHashTable<TKey, TValue> source)
        {
            var table = new NeoHashTable<TKey, TValue>(source.Comparer, NeoHashTableKind.Set);
            if (source.Count == 0 || source.buckets is null)
                return table;
            if (NeoHashPrimes.ExpandPrime(source.Count + 1) >= source.buckets.Length)
            {
                table.buckets = (int[])source.buckets.Clone();
                table.hashCodes = (int[])source.hashCodes.Clone();
                table.nexts = (int[])source.nexts.Clone();
                table.keyAt = (TKey[])source.keyAt.Clone();
                table.valueAt = (TValue[])source.valueAt.Clone();
                table.freeList = source.freeList;
                table.freeCount = source.freeCount;
                table.count = source.count;
                return table;
            }
            table.Initialize(source.Count);
            for (int i = 0; i < source.count; i++)
            {
                if (source.nexts[i] >= -1)
                    table.Add(source.keyAt[i], default!, NeoInsertBehavior.None);
            }
            return table;
        }

        /// <summary>
        /// <c>new Dictionary&lt;K, V&gt;(source, comparer)</c>. With the
        /// source's own comparer object the live entries are copied compacted,
        /// keeping their hashes; with another they are added in slot order.
        /// </summary>
        internal static NeoHashTable<TKey, TValue> CopyDictionary(
            NeoHashTable<TKey, TValue> source,
            IEqualityComparer<TKey> comparer,
            Func<TKey, string>? keyText = null)
        {
            var table = new NeoHashTable<TKey, TValue>(comparer, NeoHashTableKind.Dictionary, source.Count, keyText);
            if (source.Count == 0)
                return table;
            if (ReferenceEquals(comparer, source.Comparer))
            {
                table.CompactFrom(source.count, source.hashCodes, source.nexts, source.keyAt, source.valueAt);
                return table;
            }
            for (int i = 0; i < source.count; i++)
            {
                if (source.nexts[i] >= -1)
                    table.Add(source.keyAt[i], source.valueAt[i], NeoInsertBehavior.Throw);
            }
            return table;
        }

        /// <summary>The slot holding <paramref name="key"/> (<c>FindItemIndex</c>/<c>FindValue</c>), or -1.</summary>
        internal int IndexOf(TKey key)
        {
            CheckKey(key);
            if (buckets is null)
                return -1;
            int hashCode = HashOf(key);
            int i = buckets[(uint)hashCode % (uint)buckets.Length] - 1;
            while (i >= 0)
            {
                if (hashCodes[i] == hashCode && Comparer.Equals(keyAt[i], key))
                    return i;
                i = nexts[i];
            }
            return -1;
        }

        internal bool Contains(TKey key) => IndexOf(key) >= 0;

        /// <summary>The key stored in a slot <see cref="IndexOf"/> returned.</summary>
        internal TKey KeyAt(int index) => keyAt[index];

        /// <summary>The value stored in a slot <see cref="IndexOf"/> returned.</summary>
        internal TValue ValueAt(int index) => valueAt[index];

        /// <summary><c>AddIfNotPresent</c>/<c>TryInsert</c>: true when the key was added (or, overwriting, its value set).</summary>
        internal bool Add(TKey key, TValue value, NeoInsertBehavior behavior)
        {
            CheckKey(key);
            if (buckets is null)
                Initialize(0);
            int[] table = buckets!;
            int hashCode = HashOf(key);
            uint bucket = (uint)hashCode % (uint)table.Length;
            int i = table[bucket] - 1;
            while (i >= 0)
            {
                if (hashCodes[i] == hashCode && Comparer.Equals(keyAt[i], key))
                {
                    if (behavior == NeoInsertBehavior.Overwrite)
                    {
                        valueAt[i] = value;
                        return true;
                    }
                    if (behavior == NeoInsertBehavior.Throw)
                        throw NeoHashingErrors.DuplicateKey(keyText(key));
                    return false;
                }
                i = nexts[i];
            }
            int index;
            if (freeCount > 0)
            {
                index = freeList;
                freeCount--;
                freeList = StartOfFreeList - nexts[index];
            }
            else
            {
                if (count == nexts.Length)
                {
                    Resize(NeoHashPrimes.ExpandPrime(count));
                    table = buckets!;
                    bucket = (uint)hashCode % (uint)table.Length;
                }
                index = count++;
            }
            hashCodes[index] = hashCode;
            nexts[index] = table[bucket] - 1;
            keyAt[index] = key;
            valueAt[index] = value;
            table[bucket] = index + 1;
            return true;
        }

        /// <summary><c>Remove</c>: frees the key's slot, which the next insert reuses first.</summary>
        internal bool Remove(TKey key)
        {
            CheckKey(key);
            if (buckets is null)
                return false;
            int hashCode = HashOf(key);
            uint bucket = (uint)hashCode % (uint)buckets.Length;
            int last = -1;
            int i = buckets[bucket] - 1;
            while (i >= 0)
            {
                if (hashCodes[i] == hashCode && Comparer.Equals(keyAt[i], key))
                {
                    if (last < 0)
                        buckets[bucket] = nexts[i] + 1;
                    else
                        nexts[last] = nexts[i];
                    nexts[i] = StartOfFreeList - freeList;
                    keyAt[i] = default!;
                    valueAt[i] = default!;
                    freeList = i;
                    freeCount++;
                    return true;
                }
                last = i;
                i = nexts[i];
            }
            return false;
        }

        /// <summary><c>Clear</c>: empties the table and keeps its capacity.</summary>
        internal void Clear()
        {
            int cleared = count;
            if (cleared == 0)
                return;
            Array.Clear(buckets!, 0, buckets!.Length);
            count = 0;
            freeList = -1;
            freeCount = 0;
            Array.Clear(hashCodes, 0, cleared);
            Array.Clear(nexts, 0, cleared);
            Array.Clear(keyAt, 0, cleared);
            Array.Clear(valueAt, 0, cleared);
        }

        /// <summary>The keys in enumeration order.</summary>
        internal List<TKey> Keys()
        {
            var keys = new List<TKey>(Count);
            for (int i = 0; i < count; i++)
            {
                if (nexts[i] >= -1)
                    keys.Add(keyAt[i]);
            }
            return keys;
        }

        /// <summary>The values in enumeration order.</summary>
        internal List<TValue> Values()
        {
            var values = new List<TValue>(Count);
            for (int i = 0; i < count; i++)
            {
                if (nexts[i] >= -1)
                    values.Add(valueAt[i]);
            }
            return values;
        }

        private void CheckKey(TKey key)
        {
            if (Kind == NeoHashTableKind.Dictionary && key is null)
                throw NeoRuntimeError.ArgumentNull("key");
        }

        private int HashOf(TKey key) => key is null ? 0 : Comparer.GetHashCode(key);

        private void Initialize(int capacity)
        {
            int size = NeoHashPrimes.GetPrime(capacity);
            buckets = new int[size];
            hashCodes = new int[size];
            nexts = new int[size];
            keyAt = new TKey[size];
            valueAt = new TValue[size];
            freeList = -1;
        }

        private void Resize(int newSize)
        {
            Array.Resize(ref hashCodes, newSize);
            Array.Resize(ref nexts, newSize);
            Array.Resize(ref keyAt, newSize);
            Array.Resize(ref valueAt, newSize);
            var table = new int[newSize];
            for (int i = 0; i < count; i++)
            {
                if (nexts[i] >= -1)
                {
                    uint bucket = (uint)hashCodes[i] % (uint)newSize;
                    nexts[i] = table[bucket] - 1;
                    table[bucket] = i + 1;
                }
            }
            buckets = table;
        }

        /// <summary><c>TrimExcess()</c>: the live entries, compacted into the smallest prime table that holds them.</summary>
        private void TrimExcess()
        {
            int newSize = NeoHashPrimes.GetPrime(Count);
            if (newSize >= nexts.Length)
                return;
            int oldCount = count;
            int[] oldHashCodes = hashCodes;
            int[] oldNexts = nexts;
            TKey[] oldKeys = keyAt;
            TValue[] oldValues = valueAt;
            Initialize(newSize);
            CompactFrom(oldCount, oldHashCodes, oldNexts, oldKeys, oldValues);
        }

        /// <summary><c>CopyEntries</c>: appends the source's live entries in slot order into this freshly initialized table.</summary>
        private void CompactFrom(int sourceCount, int[] sourceHashCodes, int[] sourceNexts, TKey[] sourceKeys, TValue[] sourceValues)
        {
            int[] table = buckets!;
            int copied = 0;
            for (int i = 0; i < sourceCount; i++)
            {
                if (sourceNexts[i] < -1)
                    continue;
                int hashCode = sourceHashCodes[i];
                uint bucket = (uint)hashCode % (uint)table.Length;
                hashCodes[copied] = hashCode;
                keyAt[copied] = sourceKeys[i];
                valueAt[copied] = sourceValues[i];
                nexts[copied] = table[bucket] - 1;
                table[bucket] = copied + 1;
                copied++;
            }
            count = copied;
            freeCount = 0;
        }
    }

    /// <summary>.NET 10's <c>HashHelpers</c> prime sizes.</summary>
    internal static class NeoHashPrimes
    {
        private const int MaxPrimeArrayLength = 0x7FFFFFC3;
        private const int HashPrime = 101;

        private static readonly int[] Primes =
        {
            3, 7, 11, 17, 23, 29, 37, 47, 59, 71, 89, 107, 131, 163, 197, 239, 293, 353, 431, 521, 631, 761, 919,
            1103, 1327, 1597, 1931, 2333, 2801, 3371, 4049, 4861, 5839, 7013, 8419, 10103, 12143, 14591,
            17519, 21023, 25229, 30293, 36353, 43627, 52361, 62851, 75431, 90523, 108631, 130363, 156437,
            187751, 225307, 270371, 324449, 389357, 467237, 560689, 672827, 807403, 968897, 1162687, 1395263,
            1674319, 2009191, 2411033, 2893249, 3471899, 4166287, 4999559, 5999471, 7199369,
        };

        internal static int GetPrime(int min)
        {
            foreach (int prime in Primes)
            {
                if (prime >= min)
                    return prime;
            }
            for (int i = min | 1; i < int.MaxValue; i += 2)
            {
                if (IsPrime(i) && (i - 1) % HashPrime != 0)
                    return i;
            }
            return min;
        }

        internal static int ExpandPrime(int oldSize)
        {
            int newSize = 2 * oldSize;
            if ((uint)newSize > MaxPrimeArrayLength && MaxPrimeArrayLength > oldSize)
                return MaxPrimeArrayLength;
            return GetPrime(newSize);
        }

        private static bool IsPrime(int candidate)
        {
            if ((candidate & 1) == 0)
                return candidate == 2;
            int limit = (int)Math.Sqrt(candidate);
            for (int divisor = 3; divisor <= limit; divisor += 2)
            {
                if (candidate % divisor == 0)
                    return false;
            }
            return true;
        }
    }
}
