// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Stored value for an attribute. Mirrors the TS-side
    /// <c>IAttributeValue&lt;TValue&gt;</c>. The <see cref="value"/>
    /// field is the polymorphic payload — the runtime shape varies by
    /// the owning attribute's type:
    ///
    ///  - <c>Null</c> → JSON <c>null</c>
    ///  - <c>Bool</c> → JSON boolean
    ///  - <c>Int</c> / <c>Float</c> → JSON number
    ///  - <c>String</c> → JSON string
    ///  - <c>List</c> / <c>Enum</c> / <c>Lookup</c> → JSON array of
    ///    strings (valueIds / option-ids)
    ///  - <c>Custom</c> / <c>Dictionary</c> → JSON object mapping
    ///    schema keys to value ids
    ///
    /// We type the field as <see cref="JToken"/> so callers can dispatch
    /// at runtime via the attribute's <c>type</c> discriminator and
    /// extract the right shape:
    /// <c>value.Value&lt;string&gt;()</c>,
    /// <c>value.ToObject&lt;Dictionary&lt;string, string&gt;&gt;()</c>,
    /// etc.
    /// </summary>
    public class AttributeValue
    {
        public string id;
        public string _id;
        public string projectId;
        public JToken value;
        /// <summary>
        /// Optional Custom-record subtype override. Wire emits the field
        /// only when the row carries an explicit override; absent /
        /// <c>null</c> means "use the attribute's declared
        /// <c>customTypeId</c>".
        /// </summary>
        public string typeId;
        [Newtonsoft.Json.JsonConverter(typeof(TolerantStringConverter))]
        public string createdAt;
        [Newtonsoft.Json.JsonConverter(typeof(TolerantStringConverter))]
        public string updatedAt;
    }
}
