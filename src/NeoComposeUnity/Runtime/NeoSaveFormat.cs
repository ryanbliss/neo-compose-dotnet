// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable

using System;
using System.Globalization;
using Newtonsoft.Json;

namespace NeoCompose.Runtime
{
    /// <summary>Save compatibility is independent of the authored export and compiler revisions.</summary>
    public static class NeoSaveFormat
    {
        public const int LegacyRevision = 1;
        public const int ListenerRevision = 2;

        /// <summary>
        /// P127 §15 "Local saves". Every save this SDK writes is stamped with
        /// it, and it reads no older save: there is no load-time upgrade. A
        /// save at this revision is validated strictly on load.
        /// </summary>
        public const int P127Revision = 3;
        public const int SupportedRevision = P127Revision;

        public static void RequireSupported(int? requiredRevision)
        {
            int required = requiredRevision ?? LegacyRevision;
            if (required < LegacyRevision)
                throw new NeoUnsupportedSaveFormatException(required, "The save format revision is invalid.");
            if (required > SupportedRevision)
                throw new NeoUnsupportedSaveFormatException(required,
                    $"This save requires format revision {required}. Upgrade the Neo Compose SDK before opening it; this SDK supports revision {SupportedRevision}.");
        }

        /// <summary>
        /// Refuses a save written below <see cref="P127Revision"/>, naming
        /// both revisions; <paramref name="outcome"/> says what was not done.
        /// </summary>
        internal static void RequireCurrent(int? requiredRevision, string outcome)
        {
            int required = requiredRevision ?? LegacyRevision;
            if (required < P127Revision)
                throw new NeoUnsupportedSaveFormatException(required, $"{BelowCurrent(requiredRevision)} {outcome}");
        }

        /// <summary>Why a save below <see cref="P127Revision"/> is refused.</summary>
        internal static string BelowCurrent(int? requiredRevision) =>
            $"This save has format revision {requiredRevision ?? LegacyRevision}, and this SDK reads only revision {P127Revision} saves; it doesn't upgrade older ones. Delete the save to start a new one.";

        internal static int? Combine(int? current, int? incoming)
        {
            RequireSupported(current);
            RequireSupported(incoming);
            if (current is null)
                return incoming;
            return incoming is null ? current : Math.Max(current.Value, incoming.Value);
        }
    }

    /// <summary>An incompatible save must not be recovered as an empty draft.</summary>
    public sealed class NeoUnsupportedSaveFormatException : InvalidOperationException
    {
        public int RequiredRevision
        {
            get;
        }

        public NeoUnsupportedSaveFormatException(int requiredRevision, string message) : base(message)
        {
            RequiredRevision = requiredRevision;
        }
    }

    /// <summary>
    /// A revision-3 save value refused after the save loaded, by a partition
    /// load or a live apply. The running save is left as it was.
    /// </summary>
    public sealed class NeoSaveValueRefusedException : InvalidOperationException
    {
        public int RequiredRevision
        {
            get;
        }

        public NeoSaveValueRefusedException(int requiredRevision, string message) : base(message)
        {
            RequiredRevision = requiredRevision;
        }
    }

    /// <summary>Malformed compatibility markers must never enter the corrupt-save fallback.</summary>
    internal sealed class NeoSaveFormatRevisionConverter : JsonConverter<int?>
    {
        public override int? ReadJson(JsonReader reader, Type objectType, int? existingValue,
            bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            if (reader.TokenType == JsonToken.Integer || reader.TokenType == JsonToken.Float)
            {
                double value;
                try
                {
                    value = Convert.ToDouble(reader.Value, CultureInfo.InvariantCulture);
                }
                catch (Exception error) when (error is InvalidCastException || error is OverflowException)
                {
                    throw InvalidMarker(reader.Path);
                }
                if (double.IsFinite(value) && value >= 1 && value <= int.MaxValue && Math.Truncate(value) == value)
                    return (int)value;
            }
            throw InvalidMarker(reader.Path);
        }

        public override void WriteJson(JsonWriter writer, int? value, JsonSerializer serializer)
        {
            writer.WriteValue(value);
        }

        private static NeoUnsupportedSaveFormatException InvalidMarker(string path) => new(0,
            $"Save compatibility marker at '{path}' must be a positive integral revision. The save was not loaded.");
    }
}
