// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// One argument array per small arity and thread. A caller rents one,
    /// hands it to a call that only reads it, and returns it; a nested call
    /// of the same arity finds the slot empty and allocates.
    /// </summary>
    internal static class NeoArgumentArrays
    {
        private const int MaxPooledArity = 4;

        [ThreadStatic]
        private static object?[]?[]? free;

        internal static object?[] Rent(int arity)
        {
            if (arity > MaxPooledArity)
                return new object?[arity];
            free ??= new object?[MaxPooledArity + 1][];
            object?[]? args = free[arity];
            if (args is null)
                return new object?[arity];
            free[arity] = null;
            return args;
        }

        internal static void Return(object?[] args)
        {
            if (args.Length > MaxPooledArity)
                return;
            Array.Clear(args, 0, args.Length);
            free![args.Length] = args;
        }
    }
}
