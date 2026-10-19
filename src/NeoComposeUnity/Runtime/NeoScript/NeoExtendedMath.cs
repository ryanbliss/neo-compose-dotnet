using System;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    internal static class NeoExtendedMath
    {
        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
                return min;
            if (value > max)
                return max;
            return value;
        }

        private static double Repeat(double t, double length)
        {
            double quotient = (float)(t / length);
            if (!IsFinite(quotient))
                return double.NaN;
            double multiple = (float)(Math.Floor(quotient) * length);
            if (!IsFinite(multiple))
                return double.NaN;
            return Clamp((float)(t - multiple), 0, length);
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static void RequireRepeatDomain(string name, double t, double length)
        {
            if (!IsFinite(t))
                throw new NSGetterRuntimeError($"Math.{name} requires finite t.", NeoErrorContract.NeoArgumentError);
            if (!IsFinite(length))
                throw new NSGetterRuntimeError($"Math.{name} requires finite length.", NeoErrorContract.NeoArgumentError);
            if (length <= 0)
                throw new NSGetterRuntimeError($"Math.{name} requires length > 0.", NeoErrorContract.NeoArgumentError);
        }

        internal static double Evaluate(MathOp op, double a, double b, double c)
        {
            switch (op)
            {
                case MathOp.Pow:
                    return NeoMathKernel.Pow(a, b);
                case MathOp.Exp:
                    return NeoMathKernel.Exp(a);
                case MathOp.Log:
                    return NeoMathKernel.Log(a);
                case MathOp.Log10:
                    return NeoMathKernel.Log10(a);
                case MathOp.Sin:
                    return NeoMathKernel.Sin(a);
                case MathOp.Cos:
                    return NeoMathKernel.Cos(a);
                case MathOp.Tan:
                    return NeoMathKernel.Tan(a);
                case MathOp.Asin:
                    return NeoMathKernel.Asin(a);
                case MathOp.Acos:
                    return NeoMathKernel.Acos(a);
                case MathOp.Atan:
                    return NeoMathKernel.Atan(a);
                case MathOp.Atan2:
                    return NeoMathKernel.Atan2(a, b);
                case MathOp.Lerp:
                    return (float)(a + (float)((float)(b - a) * Clamp(c, 0, 1)));
                case MathOp.InverseLerp:
                    return a == b ? 0 : Clamp((float)((float)(c - a) / (float)(b - a)), 0, 1);
                case MathOp.MoveTowards:
                    double delta = (float)(b - a);
                    if (Math.Abs(delta) <= c)
                        return b;
                    double sign = delta >= 0 ? 1 : -1;
                    return (float)(a + (float)(sign * c));
                case MathOp.Repeat:
                    RequireRepeatDomain("Repeat", a, b);
                    return Repeat(a, b);
                case MathOp.PingPong:
                    RequireRepeatDomain("PingPong", a, b);
                    double period = (float)(2 * b);
                    if (double.IsInfinity(period))
                        return double.NaN;
                    return (float)(b - Math.Abs((float)(Repeat(a, period) - b)));
                default:
                    throw new NSGetterRuntimeError($"Unknown extended math op '{op}'.");
            }
        }
    }
}
