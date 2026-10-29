// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;

namespace NeoCompose.Runtime.NeoScript.Hashing
{
    /// <summary>
    /// The comparer an <c>IStructuralEquatable</c> or <c>IStructuralComparable</c>
    /// member receives. Elements 8 and on reach it as one boxed rest tuple,
    /// which <see cref="Rest"/> builds from <c>elements[start..]</c>.
    /// </summary>
    internal interface INeoStructuralComparer<T>
    {
        T Rest(IReadOnlyList<T> elements, int start);
    }

    internal interface INeoStructuralEqualityComparer<T> : INeoStructuralComparer<T>
    {
        bool Equals(T a, T b);

        /// <summary>The comparer's <c>GetHashCode</c>, which receives null elements too.</summary>
        int Hash(T value);
    }

    internal interface INeoStructuralOrderComparer<T> : INeoStructuralComparer<T>
    {
        int Compare(T a, T b);
    }

    /// <summary>
    /// P127 §8.1 and §8.4: tuples as <c>ValueTuple</c>, from .NET 10's
    /// ValueTuple.cs (dotnet/runtime release/10.0 at
    /// 8f4c012be1d11eef2a7c1991cdb6302479f23b74). A tuple is its flat element
    /// list; a tuple of eight or more elements is <c>(e1, …, e7, rest)</c>
    /// wherever the nesting is observable. Element callbacks receive the
    /// element's index. The compose twin is <c>hashing/tuples.ts</c>.
    /// </summary>
    internal static class NeoTuples
    {
        /// <summary>Elements 1 to 7, then the rest.</summary>
        private const int RestIndex = 7;

        /// <summary><c>Equals(T)</c>, and <c>Equals(object?)</c> for a tuple of the same type: every element in order.</summary>
        internal static bool TupleEquals<T>(IReadOnlyList<T> x, IReadOnlyList<T> y, Func<int, T, T, bool> equals)
        {
            for (int i = 0; i < x.Count; i++)
            {
                if (!equals(i, x[i], y[i]))
                    return false;
            }
            return true;
        }

        /// <summary><c>CompareTo(T)</c>: every element in order, stopping at the first nonzero result.</summary>
        internal static int TupleCompareTo<T>(IReadOnlyList<T> x, IReadOnlyList<T> y, Func<int, T, T, int> compare)
        {
            for (int i = 0; i < x.Count; i++)
            {
                int c = compare(i, x[i], y[i]);
                if (c != 0)
                    return c;
            }
            return 0;
        }

        /// <summary>
        /// <c>IComparable.CompareTo(object?)</c>. <paramref name="other"/> is
        /// the argument's elements when it is a tuple of the receiver's type
        /// and null otherwise; a null argument gives 1, and another type
        /// throws, naming the receiver's type.
        /// </summary>
        internal static int TupleCompareToObject<T>(
            IReadOnlyList<T> x,
            IReadOnlyList<T>? other,
            bool argumentIsNull,
            Func<int, T, T, int> compare,
            Func<string> typeName)
        {
            if (argumentIsNull)
                return 1;
            if (other is null)
                throw NeoHashingErrors.TupleIncorrectType(typeName());
            return TupleCompareTo(x, other, compare);
        }

        /// <summary><c>GetHashCode()</c>: each element's hash, null as 0, combined in §8.4's selection and order.</summary>
        internal static int TupleHash<T>(IReadOnlyList<T> x, Func<int, T, int> hash) =>
            HashFrom(x, 0, i => x[i] is null ? 0 : hash(i, x[i]));

        /// <summary>
        /// <c>IStructuralEquatable.Equals(other, comparer)</c>.
        /// <paramref name="other"/> is the argument's elements when it is a
        /// tuple of the receiver's type, and null otherwise (false).
        /// </summary>
        internal static bool TupleStructuralEquals<T>(
            IReadOnlyList<T> x,
            IReadOnlyList<T>? other,
            INeoStructuralEqualityComparer<T> comparer)
        {
            if (other is null)
                return false;
            for (int i = 0; i < x.Count; i++)
            {
                if (i == RestIndex)
                    return comparer.Equals(comparer.Rest(x, RestIndex), comparer.Rest(other, RestIndex));
                if (!comparer.Equals(x[i], other[i]))
                    return false;
            }
            return true;
        }

        /// <summary><c>IStructuralComparable.CompareTo(other, comparer)</c>, with <see cref="TupleCompareToObject"/>'s arguments.</summary>
        internal static int TupleStructuralCompareTo<T>(
            IReadOnlyList<T> x,
            IReadOnlyList<T>? other,
            bool argumentIsNull,
            INeoStructuralOrderComparer<T> comparer,
            Func<string> typeName)
        {
            if (argumentIsNull)
                return 1;
            if (other is null)
                throw NeoHashingErrors.TupleIncorrectType(typeName());
            for (int i = 0; i < x.Count; i++)
            {
                if (i == RestIndex)
                    return comparer.Compare(comparer.Rest(x, RestIndex), comparer.Rest(other, RestIndex));
                int c = comparer.Compare(x[i], other[i]);
                if (c != 0)
                    return c;
            }
            return 0;
        }

        /// <summary><c>IStructuralEquatable.GetHashCode(comparer)</c>: the comparer's hashes in <see cref="TupleHash"/>'s selection.</summary>
        internal static int TupleStructuralHash<T>(IReadOnlyList<T> x, INeoStructuralEqualityComparer<T> comparer) =>
            HashFrom(x, 0, i => comparer.Hash(x[i]));

        /// <summary>
        /// The hash of the tuple <c>elements[start..]</c>. One element (an
        /// eight-element tuple's rest) is its own hash, as
        /// <c>ValueTuple&lt;T1&gt;</c>'s is; two to seven combine in order. A
        /// longer one hashes its rest first: a rest of eight or more is the
        /// hash, and a rest of <c>n</c> combines elements <c>n</c> through 7,
        /// then the rest's hash.
        /// </summary>
        private static int HashFrom<T>(IReadOnlyList<T> elements, int start, Func<int, int> hash)
        {
            int size = elements.Count - start;
            if (size == 1)
                return hash(start);
            int h = NeoHashes.Seed;
            if (size <= RestIndex)
            {
                for (int i = start; i < elements.Count; i++)
                    h = NeoHashes.Combine(h, hash(i));
                return h;
            }
            int restHash = HashFrom(elements, start + RestIndex, hash);
            int restSize = size - RestIndex;
            if (restSize > RestIndex)
                return restHash;
            for (int i = start + restSize - 1; i < start + RestIndex; i++)
                h = NeoHashes.Combine(h, hash(i));
            return NeoHashes.Combine(h, restHash);
        }
    }
}
