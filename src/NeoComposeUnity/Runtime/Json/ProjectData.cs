// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Top-level deserialization target — pass to
    /// <c>JsonConvert.DeserializeObject&lt;ProjectExport&gt;(json)</c>.
    /// Mirrors the TS-side <c>IProjectUnityExport</c> wrapper:
    /// the project record nested under <see cref="project"/>, plus four
    /// keyed-by-id payloads as <see cref="Dictionary{TKey, TValue}"/>.
    ///
    /// JSON shape:
    ///
    /// <code>
    /// {
    ///   "project":    { ... },
    ///   "attributes": { "&lt;id&gt;": { ... } },
    ///   "enums":      { "&lt;id&gt;": { ... } },
    ///   "types":      { "&lt;id&gt;": { ... } },
    ///   "values":     { "&lt;id&gt;": { ... } }
    /// }
    /// </code>
    /// </summary>
    public class ProjectData
    {
        public Project project;
        public Dictionary<string, Attribute> attributes;
        public Dictionary<string, AttributeValue> values;
        public Dictionary<string, CustomType> types;
        public Dictionary<string, Enum> enums;
    }
}
