// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime.NeoScript.Ordering
{
    /// <summary>
    /// P127 §10.2 <c>Comparer&lt;T&gt;.Default</c>, from .NET 10's Comparer.cs
    /// files, dotnet/runtime release/10.0 at
    /// 8f4c012be1d11eef2a7c1991cdb6302479f23b74. The compose twin is
    /// <c>ordering/comparers.ts</c>.
    /// </summary>
    internal static class NeoComparers
    {
        /// <summary>
        /// <c>GenericComparer&lt;T&gt;.Compare</c>, <c>Comparer&lt;T&gt;.Default</c>
        /// when <c>T</c> converts to <c>IComparable&lt;T&gt;</c> (steps 1 and
        /// 3): null first, two nulls equal, otherwise <c>x.CompareTo(y)</c>.
        /// </summary>
        internal static int CompareComparable<T>(T? x, T? y, Comparison<T> compareTo)
        {
            if (x is not null)
                return y is not null ? compareTo(x, y) : 1;
            return y is not null ? -1 : 0;
        }

        /// <summary>The non-generic <c>Comparer.Default.Compare</c> (step 4).</summary>
        /// <param name="identical">One value (§1.2): reference identity, with each box its own object.</param>
        /// <param name="isComparable">The value implements the non-generic <c>IComparable</c>.</param>
        /// <param name="compareTo"><c>value.CompareTo(other)</c> through <c>IComparable</c>.</param>
        internal static int CompareObjects<T>(
            T? x,
            T? y,
            Func<T, T, bool> identical,
            Func<T, bool> isComparable,
            Func<T, T?, int> compareTo)
        {
            if (x is null)
                return y is null ? 0 : -1;
            if (y is null)
                return 1;
            if (identical(x, y))
                return 0;
            if (isComparable(x))
                return compareTo(x, y);
            // Int32 negation: int.MinValue stays itself.
            if (isComparable(y))
                return unchecked(-compareTo(y, x));
            throw NeoOrderingErrors.ImplementIComparable();
        }
    }
}
