// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// One option inside an <see cref="Enum"/>. Mirrors the TS-side
    /// <c>IEnumOption</c>.
    /// </summary>
    public class EnumOption
    {
        public string text;
    }

    /// <summary>
    /// Enum declaration. Mirrors the TS-side <c>IEnum</c>. The
    /// <see cref="options"/> field is the wire-form
    /// <c>Record&lt;string, IEnumOption&gt;</c> — a plain JSON object
    /// keyed by option id, which Newtonsoft maps directly onto a
    /// <see cref="Dictionary{TKey, TValue}"/>.
    /// </summary>
    public class Enum
    {
        public string id;
        public string _id;
        public string projectId;
        public string name;
        public Dictionary<string, EnumOption> options;
        [Newtonsoft.Json.JsonConverter(typeof(TolerantStringConverter))]
        public string createdAt;
        [Newtonsoft.Json.JsonConverter(typeof(TolerantStringConverter))]
        public string updatedAt;
    }
}
