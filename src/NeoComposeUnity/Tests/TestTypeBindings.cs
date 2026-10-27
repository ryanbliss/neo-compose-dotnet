// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Tests
{
    /// <summary>Closed <see cref="TypeBinding"/>s and stamps for hand-built fixtures.</summary>
    internal static class TestTypeBindings
    {
        internal static TypeBinding Of(MemberKind kind, bool required = true) =>
            TypeBinding.Closed(new TypeHead(kind, required));

        /// <summary>The closed type a primitive or enum member declares, its requirement as the head's nullability.</summary>
        internal static TypeBinding Of(Member member) =>
            TypeBinding.Closed(new TypeHead(
                member.kind,
                member.Requirement == NeoMemberRequirementKind.Required,
                enumId: (member as EnumMember)?.enumId));

        internal static TypeBinding Class(string classId, bool required = true, params (string param, TypeBinding binding)[] arguments) =>
            TypeBinding.Closed(new TypeHead(MemberKind.Class, required, classId: classId), Map(arguments));

        internal static TypeBinding Interface(string interfaceId, bool required = true, params (string param, TypeBinding binding)[] arguments) =>
            TypeBinding.Closed(new TypeHead(MemberKind.Interface, required, interfaceId: interfaceId), Map(arguments));

        internal static TypeBinding Enum(string enumId, bool required = true) =>
            TypeBinding.Closed(new TypeHead(MemberKind.Enum, required, enumId: enumId));

        /// <summary>Class <paramref name="classId"/>'s own parameter <paramref name="param"/>, forwarded.</summary>
        internal static TypeBinding Open(string classId, string param) =>
            TypeBinding.Open(new TypeDefinition(MemberKind.Class, classId: classId), param);

        /// <summary>Interface <paramref name="interfaceId"/>'s own parameter <paramref name="param"/>, forwarded.</summary>
        internal static TypeBinding OpenInterface(string interfaceId, string param) =>
            TypeBinding.Open(new TypeDefinition(MemberKind.Interface, interfaceId: interfaceId), param);

        internal static Dictionary<string, TypeBinding> Arguments(params (string param, TypeBinding binding)[] arguments) => Stamp(arguments);

        internal static TypeBinding List(TypeBinding entry, bool required = true) =>
            TypeBinding.Closed(new TypeHead(MemberKind.List, required), Map(("0", entry)));

        internal static TypeBinding Set(TypeBinding entry, bool required = true) =>
            TypeBinding.Closed(new TypeHead(MemberKind.Set, required), Map(("0", entry)));

        internal static TypeBinding Dictionary(TypeBinding key, TypeBinding entry, bool required = true) =>
            TypeBinding.Closed(new TypeHead(MemberKind.Dictionary, required), Map(("0", key), ("1", entry)));

        internal static TypeBinding Reference(TypeBinding target, bool required = true) =>
            TypeBinding.Closed(new TypeHead(MemberKind.Reference, required), Map(("0", target)));

        internal static TypeBinding Tuple(params TypeBinding[] elements)
        {
            var positions = new (string, TypeBinding)[elements.Length];
            for (int index = 0; index < elements.Length; index++)
                positions[index] = (index.ToString(System.Globalization.CultureInfo.InvariantCulture), elements[index]);
            return TypeBinding.Closed(new TypeHead(MemberKind.Tuple, true), Map(positions));
        }

        /// <summary>
        /// The closed binding a runtime type key spells, so fixtures can name
        /// stored bindings readably. Class and interface arguments bind
        /// <paramref name="data"/>'s parameter ids in declaration order.
        /// </summary>
        internal static TypeBinding FromKey(string key, ProjectData? data = null)
        {
            if (!NeoRuntimeTypeKeys.TryParse(key, out NeoRuntimeTypeKey parsed, out string error))
                throw new ArgumentException($"'{key}': {error}", nameof(key));
            return FromParsed(parsed, data);
        }

        /// <summary><see cref="FromKey"/> as the stored JSON object.</summary>
        internal static JObject JsonFromKey(string key, ProjectData? data = null) =>
            JObject.FromObject(FromKey(key, data));

        private static TypeBinding FromParsed(NeoRuntimeTypeKey key, ProjectData? data)
        {
            switch (key.Kind)
            {
                case NeoRuntimeTypeKeyKind.Nullable:
                    return FromParsed(key.Arguments![0], data).WithRequired(false);
                case NeoRuntimeTypeKeyKind.Void:
                    return Of(MemberKind.Void);
                case NeoRuntimeTypeKeyKind.Enum:
                    return TypeBinding.Closed(new TypeHead(MemberKind.Enum, true, enumId: key.Id));
                case NeoRuntimeTypeKeyKind.Tuple:
                    return Positional(new TypeHead(MemberKind.Tuple, true), key, data);
                case NeoRuntimeTypeKeyKind.Class:
                    return Declared(new TypeHead(MemberKind.Class, true, classId: key.Id), data?.classes is { } classes && classes.TryGetValue(key.Id!, out NeoSchemaClass? schemaClass) ? schemaClass.genericParams : null, key, data);
                case NeoRuntimeTypeKeyKind.Interface:
                    return Declared(new TypeHead(MemberKind.Interface, true, interfaceId: key.Id), data?.interfaces is { } interfaces && interfaces.TryGetValue(key.Id!, out Interface? record) ? record.genericParams : null, key, data);
                case NeoRuntimeTypeKeyKind.BuiltinConstruction:
                    {
                        MemberKind kind = key.Name switch
                        {
                            "NeoDelegate" => MemberKind.NSDelegate,
                            "NeoAction" => MemberKind.NSAction,
                            _ => (MemberKind)System.Enum.Parse(typeof(MemberKind), key.Name!),
                        };
                        return Positional(new TypeHead(kind, true), key, data);
                    }
                case NeoRuntimeTypeKeyKind.Builtin:
                    return key.Name switch
                    {
                        "object" => TypeBinding.Closed(new TypeHead(MemberKind.Object, true)),
                        "ValueType" or "Enum" or "Type" or "CultureInfo" or "NumberFormatInfo" => TypeBinding.Closed(
                            new TypeHead(MemberKind.Object, true, objectType: (NeoObjectTypeKind)System.Enum.Parse(typeof(NeoObjectTypeKind), key.Name!))),
                        "NeoAction" => Of(MemberKind.NSAction),
                        "string" => Of(MemberKind.String),
                        "SpriteInfo" => Of(MemberKind.Sprite),
                        "AudioClipInfo" => Of(MemberKind.Audio),
                        "NeoDialogueReference" => Of(MemberKind.DialogueLookup),
                        "int" => Of(MemberKind.Int),
                        "float" => Of(MemberKind.Float),
                        "decimal" => Of(MemberKind.Decimal),
                        "bool" => Of(MemberKind.Bool),
                        _ => Of((MemberKind)System.Enum.Parse(typeof(MemberKind), key.Name!)),
                    };
                default:
                    throw new ArgumentException($"'{key}' is no closed stored type.", nameof(key));
            }
        }

        private static TypeBinding Positional(TypeHead head, NeoRuntimeTypeKey key, ProjectData? data)
        {
            NeoRuntimeTypeKey[] arguments = key.Arguments ?? Array.Empty<NeoRuntimeTypeKey>();
            var positions = new (string, TypeBinding)[arguments.Length];
            for (int index = 0; index < arguments.Length; index++)
                positions[index] = (index.ToString(System.Globalization.CultureInfo.InvariantCulture), FromParsed(arguments[index], data));
            return TypeBinding.Closed(head, Map(positions));
        }

        private static TypeBinding Declared(TypeHead head, List<GenericParamDeclaration>? parameters, NeoRuntimeTypeKey key, ProjectData? data)
        {
            NeoRuntimeTypeKey[] arguments = key.Arguments ?? Array.Empty<NeoRuntimeTypeKey>();
            if ((parameters?.Count ?? 0) != arguments.Length)
                throw new ArgumentException($"'{key}' binds {arguments.Length} arguments, but the fixture declares {parameters?.Count ?? 0}.", nameof(key));
            var pairs = new (string, TypeBinding)[arguments.Length];
            for (int index = 0; index < arguments.Length; index++)
                pairs[index] = (parameters![index].id, FromParsed(arguments[index], data));
            return TypeBinding.Closed(head, Map(pairs));
        }

        internal static Dictionary<string, TypeBinding> Stamp(params (string param, TypeBinding binding)[] bindings) =>
            new Dictionary<string, TypeBinding>(Map(bindings) ?? new Dictionary<string, TypeBinding>(), StringComparer.Ordinal);

        private static Dictionary<string, TypeBinding>? Map(params (string key, TypeBinding binding)[] pairs)
        {
            if (pairs.Length == 0)
                return null;
            var map = new Dictionary<string, TypeBinding>(pairs.Length, StringComparer.Ordinal);
            foreach ((string key, TypeBinding binding) in pairs)
                map[key] = binding;
            return map;
        }
    }
}
