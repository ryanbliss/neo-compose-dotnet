// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Abstract base for the TS-side <c>TNSTypeInfo</c> discriminated
    /// union. Four concrete variants:
    /// {@link PrimitiveTypeInfo}, {@link CustomTypeInfo},
    /// {@link EnumTypeInfo}, {@link CollectionTypeInfo}. Newtonsoft
    /// dispatches on the numeric <see cref="type"/> via
    /// {@link TypeInfoConverter}.
    /// </summary>
    [JsonConverter(typeof(TypeInfoConverter))]
    public abstract class TypeInfo
    {
        public AttributeType type;
        public bool required;
    }

    /// <summary>
    /// Primitive type info — Null / Bool / Int / Float / String. No
    /// extra fields; <see cref="TypeInfo.type"/> alone identifies the
    /// primitive variant.
    /// </summary>
    public class PrimitiveTypeInfo : TypeInfo { }

    /// <summary>
    /// Custom type info. Carries the referenced custom-type id.
    /// Mirrors the TS-side <c>INSTypeInfoCustom</c>.
    /// </summary>
    public class CustomTypeInfo : TypeInfo
    {
        public string typeId;
    }

    /// <summary>
    /// Enum type info. Carries the referenced enum id. Mirrors the
    /// TS-side <c>ITypeInfoEnum</c>.
    /// </summary>
    public class EnumTypeInfo : TypeInfo
    {
        public string enumId;
    }

    /// <summary>
    /// List / Dictionary collection type info. Carries the recursive
    /// entry type. Mirrors the TS-side <c>ITypeInfoCollection</c>.
    /// </summary>
    public class CollectionTypeInfo : TypeInfo
    {
        public TypeInfo entryTypeInfo;
    }

    public class TypeInfoConverter : DiscriminatedConverter<TypeInfo>
    {
        protected override Type ResolveSubclass(JToken discriminator)
        {
            // TS-side `AttributeType` is a numeric enum on the wire.
            // Newtonsoft surfaces the JSON number as a long; cast through
            // int to land on the enum value.
            var value = (AttributeType)discriminator.Value<int>();
            switch (value)
            {
                case AttributeType.Null:
                case AttributeType.Bool:
                case AttributeType.Int:
                case AttributeType.Float:
                case AttributeType.String:
                    return typeof(PrimitiveTypeInfo);
                case AttributeType.Custom:
                    return typeof(CustomTypeInfo);
                case AttributeType.Enum:
                    return typeof(EnumTypeInfo);
                case AttributeType.List:
                case AttributeType.Dictionary:
                    return typeof(CollectionTypeInfo);
                default:
                    return null;
            }
        }
    }
}
