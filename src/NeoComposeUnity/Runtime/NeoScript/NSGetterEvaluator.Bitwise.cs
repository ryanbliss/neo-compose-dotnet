// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        private static double EvalBitwiseOp(FunctionBitwiseOpInfo info, NeoScriptScope scope, Context ctx)
        {
            string? error = info.ValidationError();
            if (error is not null)
                throw new NSGetterRuntimeError(error);
            int Operand(int index)
            {
                object? value = UnwrapGeneratedValue(ResolveValueIfId(EvalPointer(info.argPointers[index], scope, ctx), ctx), ctx);
                if (!TryAsDouble(value, out double number))
                    throw new NSGetterRuntimeError($"Bitwise {info.op} operand {index + 1} must be a number.");
                if (double.IsNaN(number) || double.IsInfinity(number))
                    throw new NSGetterRuntimeError($"Bitwise {info.op} operand {index + 1} must be finite.");
                if (!NeoNumbers.IsWhole(number))
                    throw new NSGetterRuntimeError($"Bitwise {info.op} operand {index + 1} must be an integer.");
                double remainder = number % 4294967296d;
                if (remainder < 0)
                    remainder += 4294967296d;
                if (remainder >= 2147483648d)
                    remainder -= 4294967296d;
                return (int)remainder;
            }
            int left = Operand(0);
            if (info.op == "not")
                return ~left;
            int right = Operand(1);
            return info.op switch
            {
                "and" => left & right,
                "or" => left | right,
                "xor" => left ^ right,
                "shiftLeft" => left << (right & 31),
                "shiftRight" => left >> (right & 31),
                "shiftRightUnsigned" => unchecked((int)((uint)left >> (right & 31))),
                _ => throw new NSGetterRuntimeError($"Unknown bitwise operation '{info.op}'."),
            };
        }
    }
}
