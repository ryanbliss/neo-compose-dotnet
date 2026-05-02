// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// One option inside an <see cref="Enum"/>. Mirrors the TS-side
    /// <c>IEnumOption</c>.
    /// </summary>
    public class EnumOption
    {
        public string text = null!;
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
        public string id = null!;
        public string _id = null!;
        public string projectId = null!;
        public string name = null!;
        public Dictionary<string, EnumOption> options = null!;
        [Newtonsoft.Json.JsonConverter(typeof(TolerantStringConverter))]
        public string createdAt = null!;
        [Newtonsoft.Json.JsonConverter(typeof(TolerantStringConverter))]
        public string updatedAt = null!;
    }
}
