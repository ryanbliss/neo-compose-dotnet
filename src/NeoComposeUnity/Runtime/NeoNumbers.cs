// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    internal static class NeoNumbers
    {
        // 2^52: every double at least this large is whole.
        private const double WholeMagnitude = 4503599627370496.0;

        /// <summary>
        /// <c>value == Math.Truncate(value)</c>, without Mono's native
        /// <c>modf</c> call: true for infinities, false for NaN.
        /// </summary>
        internal static bool IsWhole(double value) =>
            value is > -WholeMagnitude and < WholeMagnitude
                ? (long)value == value
                : !double.IsNaN(value);

        // Scripts count, index and compare small integers far more often than
        // they measure, so each integral value in this range shares one box.
        // A box is immutable, so sharing one is safe.
        private const int MinSharedBox = -128;
        private const int MaxSharedBox = 1023;
        private static readonly object[] SharedDoubleBoxes = CreateSharedBoxes(value => (double)value);
        private static readonly object[] SharedIntBoxes = CreateSharedBoxes(value => value);
        private static readonly object[] SharedFloatBoxes = CreateSharedBoxes(value => (float)value);

        private static object[] CreateSharedBoxes(System.Func<int, object> box)
        {
            var boxes = new object[MaxSharedBox - MinSharedBox + 1];
            for (int i = 0; i < boxes.Length; i++)
                boxes[i] = box(i + MinSharedBox);
            return boxes;
        }

        internal static object Box(int value) =>
            value is >= MinSharedBox and <= MaxSharedBox
                ? SharedIntBoxes[value - MinSharedBox]
                : value;

        // Vector components and color channels read as float.
        internal static object Box(float value)
        {
            if (value is >= MinSharedBox and <= MaxSharedBox)
            {
                int integral = (int)value;
                if (integral == value && (integral != 0 || !float.IsNegative(value)))
                    return SharedFloatBoxes[integral - MinSharedBox];
            }
            return value;
        }

        internal static object Box(double value)
        {
            if (value is >= MinSharedBox and <= MaxSharedBox)
            {
                int integral = (int)value;
                // -0.0 keeps its own box: it is integral but not the shared 0.
                if (integral == value && (integral != 0 || !double.IsNegative(value)))
                    return SharedDoubleBoxes[integral - MinSharedBox];
            }
            return value;
        }
    }
}
