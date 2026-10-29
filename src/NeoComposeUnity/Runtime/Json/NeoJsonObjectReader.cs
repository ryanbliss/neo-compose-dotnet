// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    internal static class NeoJsonObjectReader
    {
        // Population can consume an existing token reader directly. Peeking
        // avoids Skip walking the subtree before Populate walks it again.
        internal static JObject ReadForPopulate(JsonReader reader) =>
            reader is JTokenReader { TokenType: JsonToken.StartObject, CurrentToken: JObject obj }
                ? obj : JObject.Load(reader);

        internal static void Populate(JsonReader reader, JObject obj, JsonSerializer serializer, object instance)
        {
            if (reader is JTokenReader { TokenType: JsonToken.StartObject } tokens
                && ReferenceEquals(tokens.CurrentToken, obj))
                serializer.Populate(reader, instance);
            else
                using (var subReader = obj.CreateReader())
                    serializer.Populate(subReader, instance);
        }

        // Nested converters already read from the project's token tree. Loading
        // another JObject would copy the entire subtree at every nesting level.
        // Borrow it for validation/population, leaving the input unmodified.
        internal static JObject Read(JsonReader reader)
        {
            if (reader is JTokenReader tokenReader
                && reader.TokenType == JsonToken.StartObject
                && tokenReader.CurrentToken is JObject obj)
            {
                reader.Skip();
                return obj;
            }
            return JObject.Load(reader);
        }
    }
}
