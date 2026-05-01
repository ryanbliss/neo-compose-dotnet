// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
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
    /// Abstract base for the TS-side <c>IAttribute&lt;TType, TValue&gt;</c>
    /// discriminated union. Eleven concrete variants — one per
    /// <see cref="AttributeType"/> — collapse every per-type field into
    /// its own subclass instead of a bag-of-fields class. Newtonsoft
    /// dispatches on the numeric <see cref="type"/> via
    /// {@link AttributeConverter}.
    ///
    /// Common fields (id, name, timestamps, defaultValue, etc.) live
    /// here; per-type extras (`customTypeId`, `enumId`,
    /// `entryAttributeId`, etc.) live on the subclass that needs them.
    /// </summary>
    [JsonConverter(typeof(AttributeConverter))]
    public abstract class Attribute
    {
        public string id;
        public string _id;
        public string projectId;
        public string name;
        public AttributeType type;
        public bool locked;
        public bool required;
        public AttributeValueBase defaultValue;
        /// <summary>
        /// When set, this attribute is an *override* of the referenced
        /// attribute. Most other fields may be absent on overrides;
        /// missing fields resolve from the inherited attribute via the
        /// chain.
        /// </summary>
        public string extendsAttributeId;
        public string valueId;
        [JsonConverter(typeof(TolerantStringConverter))]
        public string createdAt;
        [JsonConverter(typeof(TolerantStringConverter))]
        public string updatedAt;
    }

    /// <summary>Mirror of TS-side <c>TAttributeNull</c>. No extra fields.</summary>
    public class NullAttribute : Attribute { }

    /// <summary>Mirror of TS-side <c>TAttributeBool</c>. No extra fields.</summary>
    public class BoolAttribute : Attribute { }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeInt</c>. Optional range
    /// constraints; `0` is the type-default and indistinguishable from
    /// "not set" — callers that need true presence checking should
    /// validate downstream.
    /// </summary>
    public class IntAttribute : Attribute
    {
        public float minValue;
        public float maxValue;
    }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeFloat</c>. Same range-constraint
    /// caveat as <see cref="IntAttribute"/>. <see cref="decimalPoints"/>
    /// is `0` when unset — the validator treats `0` as "no rounding".
    /// </summary>
    public class FloatAttribute : Attribute
    {
        public float minValue;
        public float maxValue;
        public int decimalPoints;
    }

    /// <summary>Mirror of TS-side <c>TAttributeString</c>.</summary>
    public class StringAttribute : Attribute { }

    /// <summary>Mirror of TS-side <c>TAttributeDictionary</c>.</summary>
    public class DictionaryAttribute : Attribute
    {
        public string entryAttributeId;
    }

    /// <summary>Mirror of TS-side <c>TAttributeList</c>.</summary>
    public class ListAttribute : Attribute
    {
        public string entryAttributeId;
    }

    /// <summary>Mirror of TS-side <c>TAttributeCustom</c>.</summary>
    public class CustomAttribute : Attribute
    {
        public string customTypeId;
    }

    /// <summary>Mirror of TS-side <c>TAttributeEnum</c>.</summary>
    public class EnumAttribute : Attribute
    {
        public string enumId;
        public bool multiselect;
    }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeLookup</c>.
    /// <see cref="collectionValueId"/> accepts an explicit <c>null</c>
    /// on the wire (distinct from absent — see the TS-side docs).
    /// Newtonsoft preserves the difference because the C# field is a
    /// reference type.
    /// </summary>
    public class LookupAttribute : Attribute
    {
        public string collectionAttributeId;
        public string collectionValueId;
        public bool multiselect;
    }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeNSGetter</c>. <see cref="code"/>
    /// is client-authored NeoScript; <see cref="returnTypeInfo"/> is
    /// the declared return type; <see cref="getter"/> is the
    /// server-compiled IR (`null` until the first compile lands).
    /// </summary>
    public class NSGetterAttribute : Attribute
    {
        public string code;
        public TypeInfo returnTypeInfo;
        public FunctionWithReturnType getter;
    }

    public class AttributeConverter : DiscriminatedConverter<Attribute>
    {
        protected override Type ResolveSubclass(JToken discriminator)
        {
            // The TS-side `AttributeType` is a numeric enum on the
            // wire. Newtonsoft surfaces the JSON number as a long; cast
            // through int to land on the enum.
            var value = (AttributeType)discriminator.Value<int>();
            switch (value)
            {
                case AttributeType.Null: return typeof(NullAttribute);
                case AttributeType.Bool: return typeof(BoolAttribute);
                case AttributeType.Int: return typeof(IntAttribute);
                case AttributeType.Float: return typeof(FloatAttribute);
                case AttributeType.String: return typeof(StringAttribute);
                case AttributeType.Dictionary: return typeof(DictionaryAttribute);
                case AttributeType.List: return typeof(ListAttribute);
                case AttributeType.Custom: return typeof(CustomAttribute);
                case AttributeType.Enum: return typeof(EnumAttribute);
                case AttributeType.Lookup: return typeof(LookupAttribute);
                case AttributeType.NSGetter: return typeof(NSGetterAttribute);
                default: return null;
            }
        }
    }
}
