// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    internal static class NeoJsonObjectReader
    {
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
