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

        private const int SmallIntMin = -128;
        // A box is immutable, so the small ints native calls pass most share one.
        private static readonly object[] SmallInts = CreateSmallInts(1152);

        internal static object Box(int value)
        {
            uint slot = (uint)(value - SmallIntMin);
            return slot < (uint)SmallInts.Length ? SmallInts[slot] : value;
        }

        private static object[] CreateSmallInts(int count)
        {
            var boxes = new object[count];
            for (int i = 0; i < count; i++)
                boxes[i] = SmallIntMin + i;
            return boxes;
        }
    }
}
