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
    /// union. Newtonsoft dispatches primitive, class, interface, generic, enum,
    /// collection, lookup, and unknown variants on the numeric
    /// <see cref="type"/> via {@link TypeInfoConverter}.
    /// </summary>
    [JsonConverter(typeof(TypeInfoConverter))]
    public abstract class TypeInfo
    {
        public MemberKind type;
        public bool required;
    }

    /// <summary>
    /// Primitive type info — Null / Bool / Int / Float / String and
    /// file-backed asset classes. No
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
    /// Function-return-only sentinel for native Function members that
    /// return no value.
    /// </summary>
    public class VoidTypeInfo : TypeInfo { }

    /// <summary>
    /// Class info. Carries the referenced class id.
    /// Mirrors the TS-side <c>INSTypeInfoClass</c>.
    /// </summary>
    public class ClassTypeInfo : TypeInfo
    {
        public string classId = null!;
        public Dictionary<string, TypeInfo>? typeArguments;
    }

    /// <summary>
    /// Class interface type info. Carries the referenced interface id.
    /// Mirrors the TS-side <c>INSTypeInfoInterface</c>.
    /// </summary>
    public class InterfaceTypeInfo : TypeInfo
    {
        public string interfaceId = null!;
    }

    /// <summary>
    /// Open generic parameter class-info. The declaring class and stable
    /// parameter id mirror the TS-side <c>INSTypeInfoGenericParam</c>.
    /// Generated closed surfaces normally substitute this before runtime;
    /// retaining the wire shape keeps property/function IR round-trippable.
    /// </summary>
    public class GenericTypeInfo : TypeInfo
    {
        public string ownerClassId = null!;
        public string genericParamId = null!;
    }

    /// <summary>
    /// First-class callable type info. The return type is declared first and
    /// the positional argument types follow, matching NeoDelegate's generic
    /// parameter order.
    /// </summary>
    public class DelegateTypeInfo : TypeInfo
    {
        [JsonConverter(typeof(FunctionReturnTypeInfoConverter))]
        public TypeInfo returnTypeInfo = null!;
        public TypeInfo[] argumentTypes = null!;
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
        public string? listMemberId;
    }

    /// <summary>
    /// Multiselect Lookup type info. Carries the recursive entry type
    /// and the collection member the lookup selects from.
    /// </summary>
    public class LookupTypeInfo : TypeInfo
    {
        public TypeInfo entryTypeInfo = null!;
        public string collectionMemberId = null!;
        public string? collectionValueId;
    }

    public class TypeInfoConverter : DiscriminatedConverter<TypeInfo>
    {
        protected override void ValidateObject(JObject obj, Type concrete)
        {
            Schema8LegacyFieldGuard.RejectRemovedTypeInfoTypeId(obj);
        }

        protected override Type? ResolveSubclass(JToken discriminator)
        {
            if (discriminator.Type == JTokenType.String)
            {
                return discriminator.Value<string>() == "Unknown"
                    ? typeof(UnknownTypeInfo)
                    : null;
            }
            // TS-side `MemberKind` is a numeric enum on the wire.
            // Newtonsoft surfaces the JSON number as a long; cast through
            // int to land on the enum value.
            var value = (MemberKind)discriminator.Value<int>();
            switch (value)
            {
                case MemberKind.Null:
                case MemberKind.Bool:
                case MemberKind.Int:
                case MemberKind.Float:
                case MemberKind.String:
                case MemberKind.Sprite:
                case MemberKind.Audio:
                case MemberKind.Vector2:
                case MemberKind.Vector2Int:
                case MemberKind.Vector3:
                case MemberKind.Vector3Int:
                case MemberKind.Color:
                case MemberKind.Decimal:
                    return typeof(PrimitiveTypeInfo);
                case MemberKind.Class:
                    return typeof(ClassTypeInfo);
                case MemberKind.Interface:
                    return typeof(InterfaceTypeInfo);
                case MemberKind.Enum:
                    return typeof(EnumTypeInfo);
                case MemberKind.List:
                case MemberKind.Dictionary:
                    return typeof(CollectionTypeInfo);
                case MemberKind.Lookup:
                    return typeof(LookupTypeInfo);
                case MemberKind.DialogueLookup:
                    return typeof(PrimitiveTypeInfo);
                case MemberKind.Generic:
                    return typeof(GenericTypeInfo);
                case MemberKind.NSDelegate:
                    return typeof(DelegateTypeInfo);
                default:
                    return null;
            }
        }
    }

    public class FunctionReturnTypeInfoConverter : DiscriminatedConverter<TypeInfo>
    {
        protected override void ValidateObject(JObject obj, Type concrete)
        {
            Schema8LegacyFieldGuard.RejectRemovedTypeInfoTypeId(obj);
        }

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

            var value = (MemberKind)discriminator.Value<int>();
            switch (value)
            {
                case MemberKind.Null:
                case MemberKind.Bool:
                case MemberKind.Int:
                case MemberKind.Float:
                case MemberKind.String:
                case MemberKind.Sprite:
                case MemberKind.Audio:
                case MemberKind.Vector2:
                case MemberKind.Vector2Int:
                case MemberKind.Vector3:
                case MemberKind.Vector3Int:
                case MemberKind.Color:
                case MemberKind.Decimal:
                    return typeof(PrimitiveTypeInfo);
                case MemberKind.Class:
                    return typeof(ClassTypeInfo);
                case MemberKind.Interface:
                    return typeof(InterfaceTypeInfo);
                case MemberKind.Enum:
                    return typeof(EnumTypeInfo);
                case MemberKind.List:
                case MemberKind.Dictionary:
                    return typeof(CollectionTypeInfo);
                case MemberKind.Lookup:
                    return typeof(LookupTypeInfo);
                case MemberKind.DialogueLookup:
                    return typeof(PrimitiveTypeInfo);
                case MemberKind.Generic:
                    return typeof(GenericTypeInfo);
                case MemberKind.NSDelegate:
                    return typeof(DelegateTypeInfo);
                default:
                    return null;
            }
        }
    }
}
