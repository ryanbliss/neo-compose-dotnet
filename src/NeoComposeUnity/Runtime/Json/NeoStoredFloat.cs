// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// P127 §12.1's float codec, twin of <c>float32.ts</c>'s
    /// <c>encodeStoredFloat</c> / <c>decodeStoredFloat</c>. A stored float is
    /// a finite JSON number other than <c>-0</c>, or one of the strings
    /// <c>"NaN"</c>, <c>"Infinity"</c>, <c>"-Infinity"</c> and <c>"-0"</c>.
    /// Readers also accept a numeric <c>-0</c>. Every NaN writes as
    /// <c>"NaN"</c>.
    /// </summary>
    internal static class NeoStoredFloat
    {
        internal const string NaN = "NaN";
        internal const string PositiveInfinity = "Infinity";
        internal const string NegativeInfinity = "-Infinity";
        internal const string NegativeZero = "-0";

        /// <summary>Whether <paramref name="text"/> is one of the four symbol strings.</summary>
        internal static bool IsSymbol(string? text) =>
            text == NaN || text == PositiveInfinity || text == NegativeInfinity || text == NegativeZero;

        /// <summary>The value a symbol string stores.</summary>
        internal static double DecodeSymbol(string text) => text switch
        {
            NaN => double.NaN,
            PositiveInfinity => double.PositiveInfinity,
            NegativeInfinity => double.NegativeInfinity,
            NegativeZero => -0d,
            _ => throw new JsonSerializationException($"'{text}' is not a stored float symbol."),
        };

        /// <summary>
        /// Reads a stored float payload: a finite JSON number, or a symbol
        /// string. False for any other token, including a non-finite number.
        /// </summary>
        internal static bool TryDecode(JToken? token, out double value)
        {
            value = 0d;
            if (token is null)
                return false;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<double>();
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            if (token.Type == JTokenType.String && IsSymbol(token.Value<string>()))
            {
                value = DecodeSymbol(token.Value<string>()!);
                return true;
            }
            return false;
        }

        /// <summary>Whether <paramref name="token"/> is a stored float payload.</summary>
        internal static bool IsStoredFloat(JToken? token) => TryDecode(token, out _);

        /// <summary>The stored payload for a runtime float: a symbol string or a finite number.</summary>
        internal static JValue Encode(double value) =>
            SymbolOf(value) is string symbol ? new JValue(symbol) : new JValue(value);

        /// <summary>Writes a runtime float's stored payload.</summary>
        internal static void Write(JsonWriter writer, double value)
        {
            if (SymbolOf(value) is string symbol)
                writer.WriteValue(symbol);
            else
                writer.WriteValue(value);
        }

        /// <summary>Writes a single's stored payload, in the single's own shortest form.</summary>
        internal static void Write(JsonWriter writer, float value)
        {
            if (SymbolOf(value) is string symbol)
                writer.WriteValue(symbol);
            else
                writer.WriteValue(value);
        }

        /// <summary>The symbol string <paramref name="value"/> stores as, or null for a finite non-<c>-0</c> number.</summary>
        internal static string? SymbolOf(double value)
        {
            if (double.IsNaN(value))
                return NaN;
            if (double.IsPositiveInfinity(value))
                return PositiveInfinity;
            if (double.IsNegativeInfinity(value))
                return NegativeInfinity;
            if (value == 0d && double.IsNegative(value))
                return NegativeZero;
            return null;
        }
    }
}
