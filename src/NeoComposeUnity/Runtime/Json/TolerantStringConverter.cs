// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Newtonsoft converter that tolerates non-string JSON tokens for a
    /// <c>string</c>-typed field — returning <c>null</c> instead of
    /// throwing.
    ///
    /// Motivation: real-world exports occasionally contain dates that
    /// surface as <c>{}</c> (empty objects) on the wire — a corruption
    /// from upstream BSON / Mongo serialization that lost the date
    /// content. The default Newtonsoft pipeline raises
    /// <see cref="JsonReaderException"/> on the first bad shape, which
    /// would block the entire export from deserializing for what's
    /// effectively a missing optional metadata field.
    ///
    /// Apply via <c>[JsonConverter(typeof(TolerantStringConverter))]</c>
    /// on date / metadata-string fields where the trade-off makes
    /// sense (the field stays <c>null</c> and downstream code branches
    /// on null). Don't apply to fields whose contents drive logic —
    /// those should fail loudly on bad data.
    /// </summary>
    public class TolerantStringConverter : JsonConverter<string>
    {
        public override string ReadJson(
            JsonReader reader,
            Type objectType,
            string existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            switch (reader.TokenType)
            {
                case JsonToken.String:
                    return (string)reader.Value;
                case JsonToken.Null:
                    return null;
                case JsonToken.StartObject:
                case JsonToken.StartArray:
                    // Consume the object / array so the reader stays in
                    // sync, then surface as null. Without the consume,
                    // Newtonsoft's reader would dangle inside the
                    // unconsumed tokens and corrupt downstream parsing
                    // of sibling fields.
                    JToken.Load(reader);
                    return null;
                default:
                    // Numbers / booleans / dates that ended up in a
                    // string field — coerce via ToString. Better than
                    // null when the data is *almost* right.
                    var v = reader.Value;
                    return v?.ToString();
            }
        }

        public override void WriteJson(
            JsonWriter writer,
            string value,
            JsonSerializer serializer)
        {
            if (value == null) writer.WriteNull();
            else writer.WriteValue(value);
        }
    }
}
