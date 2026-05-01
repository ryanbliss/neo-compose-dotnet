// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Export
{
    /// <summary>
    /// Default-value carrier on an <see cref="Attribute"/>. Mirrors the
    /// TS-side <c>IAttributeValueBase</c>. Same payload shape as
    /// <see cref="AttributeValue"/> minus id / timestamp metadata. The
    /// polymorphic <see cref="value"/> field rides as <see cref="JToken"/>
    /// so callers dispatch on the parent attribute's <c>type</c>.
    /// </summary>
    public class AttributeValueBase
    {
        public JToken value;
        public string typeId;
    }

    /// <summary>
    /// Attribute record. Mirrors the TS-side
    /// <c>IAttribute&lt;AttributeType, unknown&gt;</c> — the union over
    /// all per-type attribute variants. Per-type fields
    /// (<see cref="customTypeId"/>, <see cref="entryAttributeId"/>,
    /// <see cref="enumId"/>, etc.) are populated only on the variants
    /// that need them; readers branch on <see cref="type"/> to know
    /// which fields are valid.
    ///
    /// NSGetter-specific:
    ///
    ///  - <see cref="code"/> — NeoScript source authored by the user.
    ///  - <see cref="returnTypeInfo"/> — declared return type.
    ///  - <see cref="getter"/> — server-compiled IR. Set after compile;
    ///    <c>null</c> until then.
    ///
    /// Override-specific:
    ///
    ///  - <see cref="extendsAttributeId"/> — when set, this attribute
    ///    is an override; missing fields resolve from the inherited
    ///    attribute via the chain.
    /// </summary>
    public class Attribute
    {
        // Common (every variant)
        public string id;
        public string _id;
        public string projectId;
        public string name;
        public AttributeType type;
        public bool locked;
        public bool required;
        public AttributeValueBase defaultValue;
        public string extendsAttributeId;
        public string valueId;
        [JsonConverter(typeof(TolerantStringConverter))]
        public string createdAt;
        [JsonConverter(typeof(TolerantStringConverter))]
        public string updatedAt;

        // Int / Float — both attribute types share these slots. Declared
        // as `float` because the Float variant carries fractional
        // values; Int values fit exactly in a float up to 2^24, well
        // past any realistic range. Newtonsoft can't express "absent"
        // for primitive numerics — the type-default `0f` is
        // indistinguishable from an explicit zero, so callers that
        // need true presence checking must validate against the
        // attribute's `type` discriminator first.
        public float minValue;
        public float maxValue;
        public int decimalPoints;

        // Custom
        public string customTypeId;

        // List / Dictionary
        public string entryAttributeId;

        // Enum
        public string enumId;

        // Enum / Lookup
        public bool multiselect;

        // Lookup
        public string collectionAttributeId;
        public string collectionValueId;

        // NSGetter
        public string code;
        public TypeInfo returnTypeInfo;
        public FunctionWithReturnType getter;
    }
}
