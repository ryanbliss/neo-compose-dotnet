// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// One argument array per small arity. A caller rents one, hands it to a
    /// call that only reads it, and returns it; a nested call of the same
    /// arity finds it lent and allocates.
    /// </summary>
    internal static class NeoArgumentArrays
    {
        private const int MaxPooledArity = 4;

        // The arrays stay put and a flag tracks each loan, so renting and
        // returning store no reference (Mono write-barriers each one). A
        // plain flag, like the IR's other caches: evaluation is
        // single-threaded.
        private static readonly object?[][] buffers =
        {
            Array.Empty<object?>(),
            new object?[1],
            new object?[2],
            new object?[3],
            new object?[4],
        };
        private static readonly bool[] lent = new bool[MaxPooledArity + 1];

        internal static object?[] Rent(int arity)
        {
            if (arity > MaxPooledArity || lent[arity])
                return new object?[arity];
            lent[arity] = true;
            return buffers[arity];
        }

        internal static void Return(object?[] args)
        {
            int arity = args.Length;
            if (arity > MaxPooledArity || !ReferenceEquals(args, buffers[arity]))
                return;
            Clear(args);
            lent[arity] = false;
        }

        /// <summary>Drops the arguments a pooled array still holds.</summary>
        internal static void Clear(object?[] args)
        {
            // Mono write-barriers even a constant null array element store.
            // Array.Clear zeroes natively without barriers but pays an
            // icall, which measures cheaper only past two elements.
            if (args.Length > 2)
            {
                Array.Clear(args, 0, args.Length);
                return;
            }
            if (args.Length > 0)
                args[0] = null;
            if (args.Length > 1)
                args[1] = null;
        }
    }
}
