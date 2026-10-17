// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    public static partial class NSGetterEvaluator
    {
        private static object? EvalVectorOp(FunctionVectorOpInfo info, NeoScriptScope scope, Context ctx)
        {
            string? error = info.ValidationError();
            if (error is not null)
                throw new NSGetterRuntimeError(error);
            bool integer = info.kind is "Vector2Int" or "Vector3Int";
            int count = info.kind == "Color" ? 4 : info.kind is "Vector3" or "Vector3Int" ? 3 : 2;
            string op = info.op;
            VectorComponents left = default;
            VectorComponents right = default;
            int vectorIndex = 0;
            double scalar = 0;
            for (int index = 0; index < info.argPointers.Length; index++)
            {
                object? value = UnwrapGeneratedValue(ResolveValueIfId(EvalPointer(info.argPointers[index], scope, ctx), ctx), ctx);
                if (value is null)
                {
                    if (info.optional == true)
                        return null;
                    throw new NSGetterRuntimeError($"{info.kind}.{op} argument {index + 1} is null.");
                }
                bool scalarArgument = op == "scaleLeft" && index == 0
                    || (op is "multiply" or "divide") && index == 1
                    || (op is "lerp" or "moveTowards") && index == 2;
                if (scalarArgument)
                {
                    if (!TryAsDouble(value, out double number))
                        throw new NSGetterRuntimeError($"{info.kind}.{op} argument {index + 1} must be a number.");
                    if (integer && (!NeoNumbers.IsWhole(number) || double.IsInfinity(number)))
                        throw new NSGetterRuntimeError($"{info.kind}.{op} scalar must be an integer.");
                    scalar = VectorRound(number, integer);
                }
                else
                {
                    VectorComponents captured = ReadVectorOperand(value, info.kind, op, index, integer, count);
                    if (vectorIndex++ == 0)
                        left = captured;
                    else
                        right = captured;
                }
            }
            if (op == "divide" && scalar == 0)
                throw new NSGetterRuntimeError("Division by zero");
            if (op == "lerp")
                scalar = scalar < 0 ? 0 : scalar > 1 ? 1 : scalar;
            VectorComponents difference = default;
            double sum = 0;
            if (op is "distance" or "moveTowards")
            {
                for (int i = 0; i < count; i++)
                    difference.Set(i, VectorRound(right.Get(i) - left.Get(i), integer));
            }
            if (op is "magnitude" or "sqrMagnitude" or "normalized" or "distance" or "moveTowards" or "dot")
            {
                for (int i = 0; i < count; i++)
                {
                    double x = op is "distance" or "moveTowards" ? difference.Get(i) : left.Get(i);
                    double y = op == "dot" ? right.Get(i) : x;
                    double product = VectorRound(x * y, integer);
                    sum = i == 0 ? product : VectorRound(sum + product, integer);
                }
                if (op is "sqrMagnitude" or "dot")
                    return Box(sum);
                if (op is "magnitude" or "distance")
                    return Box((double)(float)Math.Sqrt(sum));
            }
            double magnitude = (float)Math.Sqrt(sum);
            bool reached = op == "moveTowards" && (sum == 0 || (scalar >= 0 && VectorRound(scalar * scalar, false) >= sum));
            VectorComponents result = default;
            for (int i = 0; i < count; i++)
            {
                double x = left.Get(i);
                double y = right.Get(i);
                double component = op switch
                {
                    "add" => VectorRound(x + y, integer),
                    "subtract" => VectorRound(x - y, integer),
                    "negate" => VectorRound(-x, integer),
                    "multiply" or "scaleLeft" => VectorRound(x * scalar, integer),
                    "divide" => integer ? Math.Truncate(x / scalar) : VectorRound(x / scalar, false),
                    "normalized" => magnitude > (float)0.00001 ? VectorRound(x / magnitude, false) : 0,
                    "lerp" => VectorRound(x + VectorRound(VectorRound(y - x, false) * scalar, false), false),
                    "moveTowards" => reached ? y : VectorRound(x + VectorRound(VectorRound(difference.Get(i) / magnitude, false) * scalar, false), false),
                    "min" => EvalFloatMathOp(MathOp.Min, "min", x, y, 0),
                    "max" => EvalFloatMathOp(MathOp.Max, "max", x, y, 0),
                    "cross" => CrossComponent(left, right, i),
                    _ => throw new NSGetterRuntimeError($"Unknown vector operation '{op}'."),
                };
                result.Set(i, component);
            }
            if (info.kind == "Color")
                return new NeoColorValue { r = (float)result.x, g = (float)result.y, b = (float)result.z, a = (float)result.w };
            if (count == 3)
                return new NeoVector3Value { x = result.x, y = result.y, z = result.z };
            return new NeoVector2Value { x = result.x, y = result.y };
        }

        private static double CrossComponent(VectorComponents a, VectorComponents b, int index)
        {
            int j = (index + 1) % 3;
            int k = (index + 2) % 3;
            return VectorRound(VectorRound(a.Get(j) * b.Get(k), false) - VectorRound(a.Get(k) * b.Get(j), false), false);
        }

        private static double VectorRound(double value, bool integer) => integer ? value : (float)value;

        private static VectorComponents ReadVectorOperand(object? value, string kind, string op, int argumentIndex, bool integer, int count)
        {
            if (value is null)
                throw new NSGetterRuntimeError($"vectorOp {kind} argument is null.");
            VectorComponents components = default;
            for (int i = 0; i < count; i++)
            {
                double component;
                if (kind == "Color")
                {
                    if (!TryReadColorComponent(value, i == 0 ? "r" : i == 1 ? "g" : i == 2 ? "b" : "a", out float channel))
                        throw new NSGetterRuntimeError("vectorOp requires a Color argument.");
                    component = channel;
                }
                else if (!TryReadVectorComponent(value, i == 0 ? "x" : i == 1 ? "y" : "z", out component))
                {
                    throw new NSGetterRuntimeError($"vectorOp requires a {kind} argument.");
                }
                if (integer && (!NeoNumbers.IsWhole(component) || double.IsInfinity(component)))
                    throw new NSGetterRuntimeError($"{kind}.{op} argument {argumentIndex + 1} component '{(i == 0 ? "x" : i == 1 ? "y" : "z")}' must be an integer.");
                components.Set(i, VectorRound(component, integer));
            }
            return components;
        }

        private static NeoVector2Value? RoundVectorIngress(NeoVector2Value? value, MemberKind? kind)
        {
            if (value is null || kind is not (MemberKind.Vector2 or MemberKind.Vector3))
                return value;
            double x = (float)value.x;
            double y = (float)value.y;
            if (value is NeoVector3Value v3)
            {
                double z = (float)v3.z;
                return x == value.x && y == value.y && z == v3.z ? value : new NeoVector3Value { x = x, y = y, z = z };
            }
            return x == value.x && y == value.y ? value : new NeoVector2Value { x = x, y = y };
        }

        private struct VectorComponents
        {
            internal double x, y, z, w;
            internal double Get(int index) => index == 0 ? x : index == 1 ? y : index == 2 ? z : w;
            internal void Set(int index, double value)
            {
                if (index == 0)
                    x = value;
                else if (index == 1)
                    y = value;
                else if (index == 2)
                    z = value;
                else
                    w = value;
            }
        }
    }
}
