// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime.NeoScript.Ordering
{
    /// <summary>
    /// Which .NET sort helper <c>List&lt;T&gt;.Sort</c> uses. The evaluator
    /// decides, because it knows <c>T</c>'s conversions.
    /// </summary>
    internal enum NeoSortHelper
    {
        /// <summary>
        /// <c>GenericArraySortHelper&lt;T&gt;</c>: no comparer or
        /// <c>Comparer&lt;T&gt;.Default</c>, and <c>T</c> converts to
        /// <c>IComparable&lt;T&gt;</c>.
        /// </summary>
        Comparable,

        /// <summary><c>ArraySortHelper&lt;T&gt;</c>: everything else.</summary>
        Comparer,
    }

    /// <summary>
    /// P127 §10.2 <c>List&lt;T&gt;.Sort</c>: a line-by-line transcription of
    /// .NET 10's introsort, <c>ArraySortHelper&lt;T&gt;</c> and
    /// <c>GenericArraySortHelper&lt;T&gt;</c> in System.Private.CoreLib's
    /// ArraySortHelper.cs, dotnet/runtime release/10.0 at
    /// 8f4c012be1d11eef2a7c1991cdb6302479f23b74. .NET sorts a span; here a span
    /// is <c>keys[lo..hi]</c>, inclusive, and every index is absolute. The
    /// compose twin is <c>ordering/introsort.ts</c>.
    /// </summary>
    internal static class NeoIntroSort
    {
        private const int IntrosortSizeThreshold = 16;

        /// <summary>
        /// <c>List&lt;T&gt;.Sort(index, count, comparer)</c>, with its argument
        /// checks.
        /// </summary>
        /// <param name="compare">
        /// <see cref="NeoSortHelper.Comparable"/>: <c>x.CompareTo(y)</c>. The
        /// sort handles a null <c>x</c> itself except where .NET dereferences
        /// it (a heapsort over nulls), so the callback throws its
        /// null-reference error there. <see cref="NeoSortHelper.Comparer"/>:
        /// <c>Compare(x, y)</c> of the comparer, the <c>Comparison&lt;T&gt;</c>
        /// or <c>Comparer&lt;T&gt;.Default</c>.
        /// </param>
        /// <param name="isNaN">
        /// <see cref="NeoSortHelper.Comparable"/> over <c>float</c>: NaNs move
        /// to the front first (<c>SortUtils.MoveNansToFront</c>).
        /// </param>
        /// <param name="comparerText">
        /// The inconsistent-comparer message's <c>{comparer}</c>: its
        /// <c>ToString()</c> or the delegate's type name.
        /// </param>
        /// <param name="isCatchable">
        /// True for errors .NET's <c>catch (Exception)</c> sees; the rest
        /// (deadlines, host faults) pass through.
        /// </param>
        internal static void SortList<T>(
            IList<T> items,
            int index,
            int count,
            NeoSortHelper helper,
            Comparison<T> compare,
            Func<T, bool>? isNaN,
            Func<string> comparerText,
            Func<Exception, bool> isCatchable,
            Func<T, bool>? isNull = null)
        {
            isNull ??= value => value is null;
            if (index < 0)
                throw NeoOrderingErrors.NeedNonNegative("index");
            if (count < 0)
                throw NeoOrderingErrors.NeedNonNegative("count");
            if (items.Count - index < count)
                throw NeoOrderingErrors.InvalidOffsetLength();
            if (count <= 1)
                return;
            int hi = index + count - 1;
            try
            {
                if (helper == NeoSortHelper.Comparer)
                {
                    IntroSort(items, index, hi, DepthLimit(count), compare);
                    return;
                }
                int lo = index;
                if (isNaN is not null)
                {
                    lo += MoveNansToFront(items, lo, hi, isNaN);
                    if (lo > hi)
                        return;
                }
                GenericIntroSort(items, lo, hi, DepthLimit(hi - lo + 1), compare, isNull);
            }
            catch (Exception error)
            {
                throw Rethrown(error, comparerText, isCatchable);
            }
        }

        /// <summary>
        /// <c>ArraySortHelper&lt;T&gt;.Sort(span, comparison)</c>, as
        /// <c>EnumerableSorter</c> uses it over its index map.
        /// </summary>
        internal static void SortComparer<T>(
            IList<T> keys,
            Comparison<T> compare,
            Func<string> comparerText,
            Func<Exception, bool> isCatchable)
        {
            if (keys.Count <= 1)
                return;
            try
            {
                IntroSort(keys, 0, keys.Count - 1, DepthLimit(keys.Count), compare);
            }
            catch (Exception error)
            {
                throw Rethrown(error, comparerText, isCatchable);
            }
        }

        private static Exception Rethrown(Exception error, Func<string> comparerText, Func<Exception, bool> isCatchable)
        {
            if (error is InconsistentComparerSignal)
                return NeoOrderingErrors.InconsistentComparer(comparerText());
            if (!isCatchable(error))
                return error;
            return NeoOrderingErrors.CompareFailed(error);
        }

        private static int DepthLimit(int length)
        {
            int log2 = 0;
            while ((length >>= 1) != 0)
                log2++;
            return 2 * (log2 + 1);
        }

        private static void Swap<T>(IList<T> keys, int i, int j)
        {
            T t = keys[i];
            keys[i] = keys[j];
            keys[j] = t;
        }

        // ---------- ArraySortHelper<T> ----------

        private static void SwapIfGreater<T>(IList<T> keys, Comparison<T> compare, int i, int j)
        {
            if (compare(keys[i], keys[j]) > 0)
                Swap(keys, i, j);
        }

        private static void IntroSort<T>(IList<T> keys, int lo, int hi, int depthLimit, Comparison<T> compare)
        {
            int partitionSize = hi - lo + 1;
            while (partitionSize > 1)
            {
                if (partitionSize <= IntrosortSizeThreshold)
                {
                    if (partitionSize == 2)
                    {
                        SwapIfGreater(keys, compare, lo, lo + 1);
                        return;
                    }
                    if (partitionSize == 3)
                    {
                        SwapIfGreater(keys, compare, lo, lo + 1);
                        SwapIfGreater(keys, compare, lo, lo + 2);
                        SwapIfGreater(keys, compare, lo + 1, lo + 2);
                        return;
                    }
                    InsertionSort(keys, lo, lo + partitionSize - 1, compare);
                    return;
                }
                if (depthLimit == 0)
                {
                    HeapSort(keys, lo, lo + partitionSize - 1, compare);
                    return;
                }
                depthLimit--;
                int p = PickPivotAndPartition(keys, lo, lo + partitionSize - 1, compare);
                // The pivot is already in place.
                IntroSort(keys, p + 1, lo + partitionSize - 1, depthLimit, compare);
                partitionSize = p - lo;
            }
        }

        private static int PickPivotAndPartition<T>(IList<T> keys, int lo, int hi, Comparison<T> compare)
        {
            // Median-of-three, which also partitions lo, middle and hi.
            int middle = lo + ((hi - lo) >> 1);
            SwapIfGreater(keys, compare, lo, middle);
            SwapIfGreater(keys, compare, lo, hi);
            SwapIfGreater(keys, compare, middle, hi);
            T pivot = keys[middle];
            Swap(keys, middle, hi - 1);
            int left = lo;
            int right = hi - 1;
            while (left < right)
            {
                // .NET's span indexer throws IndexOutOfRangeException where a scan runs off the span.
                do
                {
                    if (++left > hi)
                        throw new InconsistentComparerSignal();
                }
                while (compare(keys[left], pivot) < 0);
                do
                {
                    if (--right < lo)
                        throw new InconsistentComparerSignal();
                }
                while (compare(pivot, keys[right]) < 0);
                if (left >= right)
                    break;
                Swap(keys, left, right);
            }
            if (left != hi - 1)
                Swap(keys, left, hi - 1);
            return left;
        }

        private static void HeapSort<T>(IList<T> keys, int lo, int hi, Comparison<T> compare)
        {
            int n = hi - lo + 1;
            for (int i = n >> 1; i >= 1; i--)
                DownHeap(keys, lo, i, n, compare);
            for (int i = n; i > 1; i--)
            {
                Swap(keys, lo, lo + i - 1);
                DownHeap(keys, lo, 1, i - 1, compare);
            }
        }

        private static void DownHeap<T>(IList<T> keys, int lo, int i, int n, Comparison<T> compare)
        {
            T d = keys[lo + i - 1];
            while (i <= n >> 1)
            {
                int child = 2 * i;
                if (child < n && compare(keys[lo + child - 1], keys[lo + child]) < 0)
                    child++;
                if (!(compare(d, keys[lo + child - 1]) < 0))
                    break;
                keys[lo + i - 1] = keys[lo + child - 1];
                i = child;
            }
            keys[lo + i - 1] = d;
        }

        private static void InsertionSort<T>(IList<T> keys, int lo, int hi, Comparison<T> compare)
        {
            for (int i = lo; i < hi; i++)
            {
                T t = keys[i + 1];
                int j = i;
                while (j >= lo && compare(t, keys[j]) < 0)
                {
                    keys[j + 1] = keys[j];
                    j--;
                }
                keys[j + 1] = t;
            }
        }

        // ---------- GenericArraySortHelper<T> ----------
        // Null sorts first without a call. LessThan and GreaterThan are
        // CompareTo's sign; for float, NaNs are gone by then, so they agree
        // with .NET's primitive < and >.

        private static int MoveNansToFront<T>(IList<T> keys, int lo, int hi, Func<T, bool> isNaN)
        {
            int left = lo;
            for (int i = lo; i <= hi; i++)
            {
                if (isNaN(keys[i]))
                {
                    Swap(keys, left, i);
                    left++;
                }
            }
            return left - lo;
        }

        private static void GenericSwapIfGreater<T>(IList<T> keys, Comparison<T> compare, int i, int j, Func<T, bool> isNull)
        {
            if (!isNull(keys[i]) && compare(keys[i], keys[j]) > 0)
                Swap(keys, i, j);
        }

        private static void GenericIntroSort<T>(IList<T> keys, int lo, int hi, int depthLimit, Comparison<T> compare, Func<T, bool> isNull)
        {
            int partitionSize = hi - lo + 1;
            while (partitionSize > 1)
            {
                if (partitionSize <= IntrosortSizeThreshold)
                {
                    if (partitionSize == 2)
                    {
                        GenericSwapIfGreater(keys, compare, lo, lo + 1, isNull);
                        return;
                    }
                    if (partitionSize == 3)
                    {
                        GenericSwapIfGreater(keys, compare, lo, lo + 1, isNull);
                        GenericSwapIfGreater(keys, compare, lo, lo + 2, isNull);
                        GenericSwapIfGreater(keys, compare, lo + 1, lo + 2, isNull);
                        return;
                    }
                    GenericInsertionSort(keys, lo, lo + partitionSize - 1, compare, isNull);
                    return;
                }
                if (depthLimit == 0)
                {
                    GenericHeapSort(keys, lo, lo + partitionSize - 1, compare, isNull);
                    return;
                }
                depthLimit--;
                int p = GenericPickPivotAndPartition(keys, lo, lo + partitionSize - 1, compare, isNull);
                GenericIntroSort(keys, p + 1, lo + partitionSize - 1, depthLimit, compare, isNull);
                partitionSize = p - lo;
            }
        }

        private static int GenericPickPivotAndPartition<T>(IList<T> keys, int lo, int hi, Comparison<T> compare, Func<T, bool> isNull)
        {
            int middle = lo + ((hi - lo) >> 1);
            GenericSwapIfGreater(keys, compare, lo, middle, isNull);
            GenericSwapIfGreater(keys, compare, lo, hi, isNull);
            GenericSwapIfGreater(keys, compare, middle, hi, isNull);
            int nextToLast = hi - 1;
            T pivot = keys[middle];
            Swap(keys, middle, nextToLast);
            int left = lo;
            int right = nextToLast;
            while (left < right)
            {
                if (isNull(pivot))
                {
                    while (left < nextToLast && isNull(keys[++left]))
                    {
                    }
                    while (right > lo && !isNull(keys[--right]))
                    {
                    }
                }
                else
                {
                    while (left < nextToLast && compare(pivot, keys[++left]) > 0)
                    {
                    }
                    while (right > lo && compare(pivot, keys[--right]) < 0)
                    {
                    }
                }
                if (left >= right)
                    break;
                Swap(keys, left, right);
            }
            if (left != nextToLast)
                Swap(keys, left, nextToLast);
            return left;
        }

        private static void GenericHeapSort<T>(IList<T> keys, int lo, int hi, Comparison<T> compare, Func<T, bool> isNull)
        {
            int n = hi - lo + 1;
            for (int i = n >> 1; i >= 1; i--)
                GenericDownHeap(keys, lo, i, n, compare, isNull);
            for (int i = n; i > 1; i--)
            {
                Swap(keys, lo, lo + i - 1);
                GenericDownHeap(keys, lo, 1, i - 1, compare, isNull);
            }
        }

        private static void GenericDownHeap<T>(IList<T> keys, int lo, int i, int n, Comparison<T> compare, Func<T, bool> isNull)
        {
            T d = keys[lo + i - 1];
            while (i <= n >> 1)
            {
                int child = 2 * i;
                if (child < n && (isNull(keys[lo + child - 1]) || compare(keys[lo + child - 1], keys[lo + child]) < 0))
                    child++;
                if (isNull(keys[lo + child - 1]) || !(compare(d, keys[lo + child - 1]) < 0))
                    break;
                keys[lo + i - 1] = keys[lo + child - 1];
                i = child;
            }
            keys[lo + i - 1] = d;
        }

        private static void GenericInsertionSort<T>(IList<T> keys, int lo, int hi, Comparison<T> compare, Func<T, bool> isNull)
        {
            for (int i = lo; i < hi; i++)
            {
                T t = keys[i + 1];
                int j = i;
                while (j >= lo && (isNull(t) || compare(t, keys[j]) < 0))
                {
                    keys[j + 1] = keys[j];
                    j--;
                }
                keys[j + 1] = t;
            }
        }

        /// <summary>Where .NET's span indexer throws: the partition scan ran off its span.</summary>
        private sealed class InconsistentComparerSignal : Exception
        {
        }
    }
}
