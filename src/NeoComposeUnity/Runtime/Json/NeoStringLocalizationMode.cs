// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace NeoCompose.Runtime.Json
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum NeoStringLocalizationMode
    {
        TextId,
        Literal,
    }
}
