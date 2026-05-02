// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Abstract base for the TS-side <c>IAttribute&lt;TType, TValue&gt;</c>
    /// discriminated union. Eleven concrete variants — one per
    /// <see cref="AttributeType"/> — collapse every per-type field into
    /// its own subclass instead of a bag-of-fields class. Newtonsoft
    /// dispatches on the numeric <see cref="type"/> via
    /// {@link AttributeConverter}.
    ///
    /// Common fields (id, name, timestamps, etc.) live here; per-type
    /// extras (`customTypeId`, `enumId`, `entryAttributeId`, etc.) live
    /// on the concrete subclass that needs them. <c>defaultValue</c>
    /// lives on the typed <see cref="Attribute{TValue}"/> intermediate
    /// — typed access is per concrete subclass via <c>TValue</c>.
    /// </summary>
    [JsonConverter(typeof(AttributeConverter))]
    public abstract class Attribute
    {
        public string id = null!;
        public string _id = null!;
        public string projectId = null!;
        public string name = null!;
        public AttributeType type;
        public bool locked;
        public bool required;
        /// <summary>
        /// When set, this attribute is an *override* of the referenced
        /// attribute. Most other fields may be absent on overrides;
        /// missing fields resolve from the inherited attribute via the
        /// chain. Optional on the TS side.
        /// </summary>
        public string? extendsAttributeId;
        /// <summary>
        /// Optional value id pointing to this attribute's stored value.
        /// Mirrors TS-side <c>valueId?</c>. Unset for template-only
        /// attributes (e.g., a List's entryAttribute is a template, not
        /// itself a stored value).
        /// </summary>
        public string? valueId;
        [JsonConverter(typeof(TolerantStringConverter))]
        public string createdAt = null!;
        [JsonConverter(typeof(TolerantStringConverter))]
        public string updatedAt = null!;
    }

    /// <summary>
    /// Typed attribute intermediate — mirrors TS-side
    /// <c>IAttribute&lt;TType, TValue&gt;</c>. Concrete subclasses
    /// extend this with the already-nullable <typeparamref name="TValue"/>
    /// matching the attribute's stored payload type — e.g.
    /// <c>BoolAttribute : Attribute&lt;bool?&gt;</c>. Hosts the typed
    /// <see cref="defaultValue"/> field, which the
    /// {@link AttributeValueBaseConverter} resolves to the matching
    /// <see cref="AttributeValueBase{TValue}"/> concrete via context
    /// dispatch (so a wire <c>{value: null}</c> on a typed attribute
    /// produces the typed concrete with <c>value = null</c> rather than
    /// the shape-dispatched <see cref="NullAttributeValueBase"/>).
    /// </summary>
    public abstract class Attribute<TValue> : Attribute
    {
        /// <summary>
        /// Default value for the attribute. Optional on the TS side
        /// (<c>defaultValue?: IAttributeValueBase&lt;TValue&gt;</c>).
        /// Strongly typed — accessing <c>.value</c> returns
        /// <typeparamref name="TValue"/>.
        /// </summary>
        public AttributeValueBase<TValue>? defaultValue;
    }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeNull</c>. <c>TValue</c> is
    /// <c>object?</c> — TS uses the literal <c>null</c> type which has
    /// no direct C# analog; <c>object?</c> with the implicit invariant
    /// "always null" is the practical equivalent.
    /// </summary>
    public class NullAttribute : Attribute<object?> { }

    /// <summary>Mirror of TS-side <c>TAttributeBool</c>.</summary>
    public class BoolAttribute : Attribute<bool?> { }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeInt</c>. Stored as <c>double?</c>
    /// (parallel to <see cref="NumberAttributeValueBase"/>) so Int and
    /// Float share the wire numeric shape. <see cref="minValue"/> and
    /// <see cref="maxValue"/> are <c>number?</c> on the TS side —
    /// nullable here so absence is distinguishable from "explicitly 0".
    /// </summary>
    public class IntAttribute : Attribute<double?>
    {
        public float? minValue;
        public float? maxValue;
    }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeFloat</c>. All three constraint
    /// fields are <c>number?</c> on the wire — nullable here.
    /// <see cref="decimalPoints"/> as <c>null</c> means "no rounding";
    /// <c>0</c> would be "round to integer".
    /// </summary>
    public class FloatAttribute : Attribute<double?>
    {
        public float? minValue;
        public float? maxValue;
        public int? decimalPoints;
    }

    /// <summary>Mirror of TS-side <c>TAttributeString</c>.</summary>
    public class StringAttribute : Attribute<string?> { }

    /// <summary>Mirror of TS-side <c>TAttributeDictionary</c>.</summary>
    public class DictionaryAttribute : Attribute<Dictionary<string, string>?>
    {
        public string entryAttributeId = null!;
    }

    /// <summary>Mirror of TS-side <c>TAttributeList</c>.</summary>
    public class ListAttribute : Attribute<string[]?>
    {
        public string entryAttributeId = null!;
    }

    /// <summary>Mirror of TS-side <c>TAttributeCustom</c>.</summary>
    public class CustomAttribute : Attribute<Dictionary<string, string>?>
    {
        public string customTypeId = null!;
    }

    /// <summary>Mirror of TS-side <c>TAttributeEnum</c>.</summary>
    public class EnumAttribute : Attribute<string[]?>
    {
        public string enumId = null!;
        public bool multiselect;
    }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeLookup</c>.
    /// <see cref="collectionValueId"/> is <c>string | null | undefined</c>
    /// on the wire (distinct from absent — see the TS-side docs):
    /// <c>null</c> means "use the parent collection's valueId";
    /// a present id means "drill into that specific entry". Nullable
    /// here preserves both cases.
    /// </summary>
    public class LookupAttribute : Attribute<string[]?>
    {
        public string collectionAttributeId = null!;
        public string? collectionValueId;
        public bool multiselect;
    }

    /// <summary>
    /// Mirror of TS-side <c>TAttributeNSGetter</c>. The stored value is
    /// always null (the runtime computes it via <c>getter</c>), so
    /// <c>TValue</c> is <c>object?</c>. <see cref="code"/> is
    /// client-authored NeoScript; <see cref="returnTypeInfo"/> is the
    /// declared return type; <see cref="getter"/> is the server-compiled
    /// IR.
    /// </summary>
    public class NSGetterAttribute : Attribute<object?>
    {
        public string code = null!;
        public TypeInfo returnTypeInfo = null!;
        public FunctionWithReturnType getter = null!;
    }

    public class AttributeConverter : DiscriminatedConverter<Attribute>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
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
