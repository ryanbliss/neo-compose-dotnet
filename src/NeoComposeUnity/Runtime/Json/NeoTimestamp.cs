// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Globalization;
using Newtonsoft.Json;

namespace NeoCompose.Runtime.Json
{
    [JsonConverter(typeof(NeoTimestampConverter))]
    public readonly struct NeoTimestamp : IEquatable<NeoTimestamp>, IComparable<NeoTimestamp>
    {
        public NeoTimestamp(double epochMilliseconds)
        {
            EpochMilliseconds = epochMilliseconds;
        }

        public double EpochMilliseconds { get; }

        public static NeoTimestamp Now() =>
            new NeoTimestamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        public static bool TryParse(string? value, out NeoTimestamp timestamp)
        {
            timestamp = default;
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (double.TryParse(
                    value,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var epochMilliseconds))
            {
                timestamp = new NeoTimestamp(epochMilliseconds);
                return true;
            }

            if (DateTimeOffset.TryParse(
                    value,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out var dateTimeOffset))
            {
                timestamp = new NeoTimestamp(dateTimeOffset.ToUnixTimeMilliseconds());
                return true;
            }

            return false;
        }

        public int CompareTo(NeoTimestamp other) =>
            EpochMilliseconds.CompareTo(other.EpochMilliseconds);

        public bool Equals(NeoTimestamp other) =>
            EpochMilliseconds.Equals(other.EpochMilliseconds);

        public override bool Equals(object? obj) =>
            obj is NeoTimestamp other && Equals(other);

        public override int GetHashCode() =>
            EpochMilliseconds.GetHashCode();

        public override string ToString() =>
            EpochMilliseconds.ToString(CultureInfo.InvariantCulture);

        public static implicit operator NeoTimestamp(double value) => new NeoTimestamp(value);
        public static implicit operator NeoTimestamp(long value) => new NeoTimestamp(value);
        public static implicit operator NeoTimestamp(int value) => new NeoTimestamp(value);
        public static implicit operator NeoTimestamp(string value) =>
            TryParse(value, out var timestamp) ? timestamp : default;
        public static implicit operator double(NeoTimestamp value) => value.EpochMilliseconds;
        public static implicit operator string(NeoTimestamp value) => value.ToString();
    }

    public sealed class NeoTimestampConverter : JsonConverter<NeoTimestamp>
    {
        public override NeoTimestamp ReadJson(
            JsonReader reader,
            Type objectType,
            NeoTimestamp existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.Integer:
                case JsonToken.Float:
                    return new NeoTimestamp(Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture));
                case JsonToken.String:
                    return NeoTimestamp.TryParse((string?)reader.Value, out var timestamp)
                        ? timestamp
                        : default;
                case JsonToken.Date:
                    if (reader.Value is DateTime dateTime)
                    {
                        return new NeoTimestamp(new DateTimeOffset(dateTime.ToUniversalTime()).ToUnixTimeMilliseconds());
                    }
                    if (reader.Value is DateTimeOffset dateTimeOffset)
                    {
                        return new NeoTimestamp(dateTimeOffset.ToUniversalTime().ToUnixTimeMilliseconds());
                    }
                    return default;
                case JsonToken.Null:
                    return default;
                default:
                    serializer.Deserialize<object>(reader);
                    return default;
            }
        }

        public override void WriteJson(
            JsonWriter writer,
            NeoTimestamp value,
            JsonSerializer serializer)
        {
            writer.WriteValue(value.EpochMilliseconds);
        }
    }
}
