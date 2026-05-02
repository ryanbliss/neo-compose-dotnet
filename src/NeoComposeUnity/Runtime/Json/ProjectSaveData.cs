// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Save file shape.
    /// </summary>
    public class ProjectSaveData
    {
        public string projectId = null!;
        public string version = null!;
        public Dictionary<string, AttributeValue> values = null!;
        public Dictionary<string, string> attributeValueOverrides = null!;
    }
}
