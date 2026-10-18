// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime.Json
{
    public sealed class FunctionBitwiseOpInfo
    {
        public string op = null!;
        public Pointer[] argPointers = null!;

        internal string? ValidationError()
        {
            int arity = op switch
            {
                "not" => 1,
                "and" or "or" or "xor" or "shiftLeft" or "shiftRight" or "shiftRightUnsigned" => 2,
                _ => 0,
            };
            if (arity == 0)
                return $"bitwiseOp has unknown operation '{op}'.";
            if (argPointers is null)
                return $"bitwiseOp {op} requires an argument pointer array.";
            if (argPointers.Length != arity)
                return $"bitwiseOp {op} requires {arity} arguments.";
            foreach (Pointer pointer in argPointers)
            {
                if (pointer is null)
                    return "bitwiseOp arguments must be pointers.";
            }
            return null;
        }
    }
}
