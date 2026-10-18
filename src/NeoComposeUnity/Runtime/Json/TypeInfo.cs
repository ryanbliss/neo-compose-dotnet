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
        [JsonConverter(typeof(TypeInfoKindConverter))]
        public MemberKind type;
        public bool required;

        internal TypeInfo ShallowClone() => (TypeInfo)MemberwiseClone();
    }

    /// <summary>
    /// Primitive type info — Null / Bool / Int / Float / String and
    /// file-backed asset classes. No
    /// extra fields; <see cref="TypeInfo.type"/> alone identifies the
    /// primitive variant.
    /// </summary>
    public sealed class PrimitiveTypeInfo : TypeInfo
    {
    }

    /// <summary>
    /// Compile-time dynamic value. Used by generated bridge signatures when
    /// the concrete type is intentionally not known ahead of time.
    /// </summary>
    public sealed class UnknownTypeInfo : TypeInfo
    {
    }

    /// <summary>
    /// Function-return-only sentinel for native Function members that
    /// return no value.
    /// </summary>
    public sealed class VoidTypeInfo : TypeInfo
    {
    }

    /// <summary>
    /// Class info. Carries the referenced class id.
    /// Mirrors the TS-side <c>INSTypeInfoClass</c>.
    /// </summary>
    public sealed class ClassTypeInfo : TypeInfo
    {
        public string classId = null!;
        public Dictionary<string, TypeInfo>? typeArguments;
    }

    /// <summary>
    /// Class interface type info. Carries the referenced interface id.
    /// Mirrors the TS-side <c>INSTypeInfoInterface</c>.
    /// </summary>
    public sealed class InterfaceTypeInfo : TypeInfo
    {
        public string interfaceId = null!;
        /// <summary>
        /// P113 §4.4. One argument per param of a generic interface, keyed by
        /// the interface's param id. Compared invariantly.
        /// </summary>
        public Dictionary<string, TypeInfo>? typeArguments;
    }

    /// <summary>
    /// Open generic parameter class-info. The declaring class and stable
    /// parameter id mirror the TS-side <c>INSTypeInfoGenericParam</c>.
    /// Generated closed surfaces normally substitute this before runtime;
    /// retaining the wire shape keeps property/function IR round-trippable.
    /// </summary>
    public sealed class GenericTypeInfo : TypeInfo
    {
        /// <summary>The declaring class; null for an interface's param.</summary>
        public string? ownerClassId;
        /// <summary>P113 §2.4. The declaring interface; null for a class's param.</summary>
        public string? ownerInterfaceId;
        public string genericParamId = null!;
    }

    /// <summary>
    /// P110 §4. A function's own type parameter, by its position in the
    /// declaring function's <c>genericParams</c>. A call frame binds it to
    /// a concrete type.
    /// </summary>
    public sealed class FunctionGenericTypeInfo : TypeInfo
    {
        public string ownerMemberId = null!;
        public int index;
    }

    /// <summary>
    /// First-class callable type info. The return type is declared first and
    /// the positional argument types follow, matching NeoDelegate's generic
    /// parameter order.
    /// </summary>
    public sealed class DelegateTypeInfo : TypeInfo
    {
        [JsonConverter(typeof(FunctionReturnTypeInfoConverter))]
        public TypeInfo returnTypeInfo = null!;
        public TypeInfo[] argumentTypes = null!;
    }

    /// <summary>
    /// Multicast void callable type info (P62 §2.1, TS-side
    /// <c>INSTypeInfoAction</c>). Mirrors <see cref="DelegateTypeInfo"/>
    /// minus the return slot: an action is always void, so void-ness is
    /// structural rather than declared.
    /// </summary>
    public sealed class ActionTypeInfo : TypeInfo
    {
        public TypeInfo[] argumentTypes = null!;
    }

    /// <summary>
    /// Enum type info. Carries the referenced enum id. Mirrors the
    /// TS-side <c>ITypeInfoEnum</c>.
    /// </summary>
    public sealed class EnumTypeInfo : TypeInfo
    {
        public string enumId = null!;
    }

    /// <summary>
    /// List / Dictionary collection type info. Carries the recursive
    /// entry type. Mirrors the TS-side <c>ITypeInfoCollection</c>.
    /// </summary>
    public sealed class CollectionTypeInfo : TypeInfo
    {
        public TypeInfo entryTypeInfo = null!;
        public string? keyEnumId;
        public string? listMemberId;
    }

    /// <summary>
    /// Multiselect Lookup type info. Carries the recursive entry type
    /// and an optional collection binding. Plain Set annotations are unbound.
    /// </summary>
    public sealed class LookupTypeInfo : TypeInfo
    {
        public TypeInfo entryTypeInfo = null!;
        public string? collectionMemberId;
        public string? collectionValueId;
    }

    public sealed class TupleElementTypeInfo
    {
        public TypeInfo typeInfo = null!;
        public string? name;
    }

    public sealed class TupleTypeInfo : TypeInfo
    {
        public TupleElementTypeInfo[] elements = null!;

        internal static void ValidateElements(TupleElementTypeInfo[]? elements)
        {
            if (elements is null)
                throw new JsonSerializationException("Tuple type requires elements.");
            if (elements.Length < 2)
                throw new JsonSerializationException("Tuple type requires at least two elements.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (TupleElementTypeInfo? element in elements)
            {
                if (element?.typeInfo is null)
                    throw new JsonSerializationException("Tuple element requires typeInfo.");
                if (element.name is not { } name)
                    continue;
                if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$"))
                    throw new JsonSerializationException($"Tuple label '{name}' is not an identifier.");
                if (name is "Rest" or "ToString" or "Equals" or "GetHashCode"
                    || System.Text.RegularExpressions.Regex.IsMatch(name, @"^Item[1-9]\d*$"))
                    throw new JsonSerializationException($"Tuple label '{name}' is reserved.");
                if (!names.Add(name))
                    throw new JsonSerializationException($"Tuple label '{name}' is repeated.");
            }
        }

        internal static TupleTypeInfo? From(TypeInfo? typeInfo) => typeInfo switch
        {
            TupleTypeInfo tuple => tuple,
            FunctionArgumentTypeInfo { type: MemberKind.Tuple, elements: { } elements } argument =>
                new TupleTypeInfo { type = MemberKind.Tuple, required = argument.required, elements = elements },
            _ => null,
        };

        internal static TupleElementTypeInfo[] MapElements(
            TupleElementTypeInfo[] elements, Func<TypeInfo, TypeInfo> map)
        {
            TupleElementTypeInfo[]? mapped = null;
            for (int i = 0; i < elements.Length; i++)
            {
                TypeInfo typeInfo = map(elements[i].typeInfo);
                if (ReferenceEquals(typeInfo, elements[i].typeInfo))
                    continue;
                mapped ??= (TupleElementTypeInfo[])elements.Clone();
                mapped[i] = new TupleElementTypeInfo { name = elements[i].name, typeInfo = typeInfo };
            }
            return mapped ?? elements;
        }
    }

    /// <summary>
    /// <c>Reference&lt;T&gt;</c> type info (P112 §5.2). Mirrors the TS-side
    /// <c>INSTypeInfoReference</c>: a slot-kind or open generic target, or a
    /// reference's own target, where normalization stops.
    /// </summary>
    public sealed class ReferenceTypeInfo : TypeInfo
    {
        public TypeInfo targetTypeInfo = null!;
    }

    // Unlike member records, TypeInfo uses string sentinels for Unknown and Void.
    // Preserve that wire shape when closures are written into save constructor arguments.
    public sealed class TypeInfoKindConverter : JsonConverter
    {
        public override bool CanRead => false;
        public override bool CanConvert(Type objectType) => objectType == typeof(MemberKind);

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            var kind = (MemberKind)value!;
            if (kind is MemberKind.Unknown or MemberKind.Void)
                writer.WriteValue(kind.ToString());
            else
                writer.WriteValue((int)kind);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
            => throw new NotSupportedException();
    }

    public class TypeInfoConverter : DiscriminatedConverter<TypeInfo>
    {
        protected override void OnPopulated(JObject obj, TypeInfo instance)
        {
            if (instance is TupleTypeInfo tuple)
                TupleTypeInfo.ValidateElements(tuple.elements);
        }

        protected override Type? ResolveSubclass(JObject obj, JToken discriminator) =>
            FunctionGenericTypeInfoArm(obj, ResolveSubclass(discriminator));

        /// <summary>A Generic type info naming its owner function is the function arm.</summary>
        internal static Type? FunctionGenericTypeInfoArm(JObject obj, Type? resolved) =>
            resolved == typeof(GenericTypeInfo) && obj.Property("ownerMemberId") is not null
                ? typeof(FunctionGenericTypeInfo)
                : resolved;

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
                case MemberKind.Set:
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
                case MemberKind.NSAction:
                    return typeof(ActionTypeInfo);
                case MemberKind.Reference:
                    return typeof(ReferenceTypeInfo);
                case MemberKind.Tuple:
                    return typeof(TupleTypeInfo);
                default:
                    return null;
            }
        }
    }

    public class FunctionReturnTypeInfoConverter : DiscriminatedConverter<TypeInfo>
    {
        protected override void OnPopulated(JObject obj, TypeInfo instance)
        {
            if (instance is TupleTypeInfo tuple)
                TupleTypeInfo.ValidateElements(tuple.elements);
        }

        protected override Type? ResolveSubclass(JObject obj, JToken discriminator) =>
            TypeInfoConverter.FunctionGenericTypeInfoArm(obj, ResolveSubclass(discriminator));

        protected override Type? ResolveSubclass(JToken discriminator)
        {
            if (discriminator.Type == JTokenType.String)
            {
                switch (discriminator.Value<string>())
                {
                    case "Unknown":
                        return typeof(UnknownTypeInfo);
                    case "Void":
                        return typeof(VoidTypeInfo);
                    default:
                        return null;
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
                case MemberKind.Set:
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
                case MemberKind.Reference:
                    return typeof(ReferenceTypeInfo);
                case MemberKind.Tuple:
                    return typeof(TupleTypeInfo);
                // MemberKind.NSAction is deliberately absent: an action is
                // statement-only and can never be a return type (P62 §3.1),
                // so an NSAction discriminator in return position is invalid
                // data and hard-fails here rather than resolving.
                default:
                    return null;
            }
        }
    }
}
