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
    /// Abstract base for the TS-side <c>TNSTypeInfo</c> discriminated
    /// union. Newtonsoft dispatches primitive, custom, interface, generic, enum,
    /// collection, lookup, and unknown variants on the numeric
    /// <see cref="type"/> via {@link TypeInfoConverter}.
    /// </summary>
    [JsonConverter(typeof(TypeInfoConverter))]
    public abstract class TypeInfo
    {
        public AttributeType type;
        public bool required;
    }

    /// <summary>
    /// Primitive type info — Null / Bool / Int / Float / String and
    /// file-backed asset types. No
    /// extra fields; <see cref="TypeInfo.type"/> alone identifies the
    /// primitive variant.
    /// </summary>
    public class PrimitiveTypeInfo : TypeInfo { }

    /// <summary>
    /// Compile-time dynamic value. Used by generated bridge signatures when
    /// the concrete type is intentionally not known ahead of time.
    /// </summary>
    public class UnknownTypeInfo : TypeInfo { }

    /// <summary>
    /// Function-return-only sentinel for native Function attributes that
    /// return no value.
    /// </summary>
    public class VoidTypeInfo : TypeInfo { }

    /// <summary>
    /// Custom type info. Carries the referenced custom-type id.
    /// Mirrors the TS-side <c>INSTypeInfoCustom</c>.
    /// </summary>
    public class CustomTypeInfo : TypeInfo
    {
        public string typeId = null!;
        public Dictionary<string, TypeInfo>? typeArguments;
    }

    /// <summary>
    /// Custom-type interface type info. Carries the referenced interface id.
    /// Mirrors the TS-side <c>INSTypeInfoInterface</c>.
    /// </summary>
    public class InterfaceTypeInfo : TypeInfo
    {
        public string interfaceId = null!;
    }

    /// <summary>
    /// Open generic parameter type-info. The declaring type and stable
    /// parameter id mirror the TS-side <c>INSTypeInfoGenericParam</c>.
    /// Generated closed surfaces normally substitute this before runtime;
    /// retaining the wire shape keeps property/function IR round-trippable.
    /// </summary>
    public class GenericTypeInfo : TypeInfo
    {
        public string ownerTypeId = null!;
        public string genericParamId = null!;
    }

    /// <summary>
    /// Enum type info. Carries the referenced enum id. Mirrors the
    /// TS-side <c>ITypeInfoEnum</c>.
    /// </summary>
    public class EnumTypeInfo : TypeInfo
    {
        public string enumId = null!;
    }

    /// <summary>
    /// List / Dictionary collection type info. Carries the recursive
    /// entry type. Mirrors the TS-side <c>ITypeInfoCollection</c>.
    /// </summary>
    public class CollectionTypeInfo : TypeInfo
    {
        public TypeInfo entryTypeInfo = null!;
        public string? keyEnumId;
        public string? listAttributeId;
    }

    /// <summary>
    /// Multiselect Lookup type info. Carries the recursive entry type
    /// and the collection attribute the lookup selects from.
    /// </summary>
    public class LookupTypeInfo : TypeInfo
    {
        public TypeInfo entryTypeInfo = null!;
        public string collectionAttributeId = null!;
        public string? collectionValueId;
    }

    public class TypeInfoConverter : DiscriminatedConverter<TypeInfo>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
        {
            if (discriminator.Type == JTokenType.String)
            {
                return discriminator.Value<string>() == "Unknown"
                    ? typeof(UnknownTypeInfo)
                    : null;
            }
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
                case AttributeType.Sprite:
                case AttributeType.Audio:
                case AttributeType.Vector2:
                case AttributeType.Vector2Int:
                case AttributeType.Vector3:
                case AttributeType.Vector3Int:
                case AttributeType.Color:
                case AttributeType.Decimal:
                    return typeof(PrimitiveTypeInfo);
                case AttributeType.Custom:
                    return typeof(CustomTypeInfo);
                case AttributeType.Interface:
                    return typeof(InterfaceTypeInfo);
                case AttributeType.Enum:
                    return typeof(EnumTypeInfo);
                case AttributeType.List:
                case AttributeType.Dictionary:
                    return typeof(CollectionTypeInfo);
                case AttributeType.Lookup:
                    return typeof(LookupTypeInfo);
                case AttributeType.DialogueLookup:
                    return typeof(PrimitiveTypeInfo);
                case AttributeType.Generic:
                    return typeof(GenericTypeInfo);
                default:
                    return null;
            }
        }
    }

    public class FunctionReturnTypeInfoConverter : DiscriminatedConverter<TypeInfo>
    {
        protected override Type? ResolveSubclass(JToken discriminator)
        {
            if (discriminator.Type == JTokenType.String)
            {
                switch (discriminator.Value<string>())
                {
                    case "Unknown": return typeof(UnknownTypeInfo);
                    case "Void": return typeof(VoidTypeInfo);
                    default: return null;
                }
            }

            var value = (AttributeType)discriminator.Value<int>();
            switch (value)
            {
                case AttributeType.Null:
                case AttributeType.Bool:
                case AttributeType.Int:
                case AttributeType.Float:
                case AttributeType.String:
                case AttributeType.Sprite:
                case AttributeType.Audio:
                case AttributeType.Vector2:
                case AttributeType.Vector2Int:
                case AttributeType.Vector3:
                case AttributeType.Vector3Int:
                case AttributeType.Color:
                case AttributeType.Decimal:
                    return typeof(PrimitiveTypeInfo);
                case AttributeType.Custom:
                    return typeof(CustomTypeInfo);
                case AttributeType.Interface:
                    return typeof(InterfaceTypeInfo);
                case AttributeType.Enum:
                    return typeof(EnumTypeInfo);
                case AttributeType.List:
                case AttributeType.Dictionary:
                    return typeof(CollectionTypeInfo);
                case AttributeType.Lookup:
                    return typeof(LookupTypeInfo);
                case AttributeType.DialogueLookup:
                    return typeof(PrimitiveTypeInfo);
                case AttributeType.Generic:
                    return typeof(GenericTypeInfo);
                default:
                    return null;
            }
        }
    }
}
