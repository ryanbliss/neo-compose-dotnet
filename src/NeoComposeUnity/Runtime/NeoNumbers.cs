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
    }
}
