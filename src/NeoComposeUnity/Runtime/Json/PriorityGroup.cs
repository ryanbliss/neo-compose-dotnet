// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using Newtonsoft.Json;

namespace NeoCompose.Runtime.Json
{
    public class PriorityType
    {
        public string id = null!;
        public string name = null!;
    }

    public class PriorityGroup
    {
        public string id = null!;
        public string projectId = null!;
        public string name = null!;
        public PriorityType[] options = null!;
        public object? system;
        [JsonConverter(typeof(TolerantStringConverter))]
        public string createdAt = null!;
        [JsonConverter(typeof(TolerantStringConverter))]
        public string updatedAt = null!;
    }
}
