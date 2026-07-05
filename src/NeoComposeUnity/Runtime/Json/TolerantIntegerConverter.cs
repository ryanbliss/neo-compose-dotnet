// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Globalization;
using Newtonsoft.Json;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Newtonsoft converter for integer DTO fields that accepts integer-valued
    /// floats and strings. Convex realtime values cross a float64 wire, so a
    /// schema version can arrive as <c>1.0</c> even though the SDK model should
    /// stay an <see cref="int"/>.
    /// </summary>
    public sealed class TolerantIntegerConverter : JsonConverter<int>
    {
        public override int ReadJson(
            JsonReader reader,
            Type objectType,
            int existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Integer:
                case JsonToken.Float:
                    return ReadIntegerValue(reader.Value, reader.Path);
                case JsonToken.String:
                    return ReadIntegerString((string?)reader.Value, reader.Path);
                default:
                    throw new JsonSerializationException(
                        $"Expected an integer at path \"{reader.Path}\" but found {reader.TokenType}.");
            }
        }

        public override void WriteJson(
            JsonWriter writer,
            int value,
            JsonSerializer serializer)
        {
            writer.WriteValue(value);
        }

        private static int ReadIntegerValue(object? raw, string path)
        {
            if (raw == null)
            {
                throw new JsonSerializationException(
                    $"Expected an integer at path \"{path}\" but found null.");
            }

            var value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
            return CoerceInteger(value, path, raw.ToString() ?? "");
        }

        private static int ReadIntegerString(string? raw, string path)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new JsonSerializationException(
                    $"Expected an integer at path \"{path}\" but found an empty string.");
            }

            if (!double.TryParse(
                    raw,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
            {
                throw new JsonSerializationException(
                    $"Expected an integer at path \"{path}\" but found \"{raw}\".");
            }

            return CoerceInteger(value, path, raw);
        }

        private static int CoerceInteger(double value, string path, string raw)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new JsonSerializationException(
                    $"Expected a finite integer at path \"{path}\" but found \"{raw}\".");
            }

            if (Math.Truncate(value) != value)
            {
                throw new JsonSerializationException(
                    $"Expected an integer at path \"{path}\" but found \"{raw}\".");
            }

            if (value < int.MinValue || value > int.MaxValue)
            {
                throw new JsonSerializationException(
                    $"Integer at path \"{path}\" was outside the Int32 range: \"{raw}\".");
            }

            return Convert.ToInt32(value, CultureInfo.InvariantCulture);
        }
    }
}
