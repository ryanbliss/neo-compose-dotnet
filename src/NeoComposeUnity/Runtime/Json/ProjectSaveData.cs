// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Save file shape.
    /// </summary>
    public class ProjectSaveData
    {
        public string projectId;
        public string version;
        public Dictionary<string, AttributeValue> values;
        public Dictionary<string, string> attributeValueOverrides;
    }
}
