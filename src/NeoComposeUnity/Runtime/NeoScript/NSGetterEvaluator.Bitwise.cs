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
            int left = BitwiseOperand(info.op, 0, EvalPointer(info.argPointers[0], scope, ctx), ctx);
            if (info.op == "not")
                return ~left;
            int right = BitwiseOperand(info.op, 1, EvalPointer(info.argPointers[1], scope, ctx), ctx);
            return ApplyBitwiseOp(info.op, left, right);
        }

        /// <summary>A bitwise operand as the 32-bit integer the operation reads.</summary>
        private static int BitwiseOperand(string op, int index, object? raw, Context ctx)
        {
            object? value = UnwrapGeneratedValue(ResolveValueIfId(raw, ctx), ctx);
            if (!TryAsDouble(value, out double number))
                throw new NSGetterRuntimeError($"Bitwise {op} operand {index + 1} must be a number.", NeoErrorContract.NeoArgumentError);
            if (double.IsNaN(number) || double.IsInfinity(number))
                throw new NSGetterRuntimeError($"Bitwise {op} operand {index + 1} must be finite.", NeoErrorContract.NeoArgumentError);
            if (!NeoNumbers.IsWhole(number))
                throw new NSGetterRuntimeError($"Bitwise {op} operand {index + 1} must be an integer.", NeoErrorContract.NeoArgumentError);
            double remainder = number % 4294967296d;
            if (remainder < 0)
                remainder += 4294967296d;
            if (remainder >= 2147483648d)
                remainder -= 4294967296d;
            return (int)remainder;
        }

        private static int ApplyBitwiseOp(string op, int left, int right) => op switch
        {
            "and" => left & right,
            "or" => left | right,
            "xor" => left ^ right,
            "shiftLeft" => left << (right & 31),
            "shiftRight" => left >> (right & 31),
            "shiftRightUnsigned" => unchecked((int)((uint)left >> (right & 31))),
            _ => throw new NSGetterRuntimeError($"Unknown bitwise operation '{op}'.", NeoErrorContract.NeoArgumentError),
        };
    }
}
