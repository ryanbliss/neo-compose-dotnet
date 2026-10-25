// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using NeoCompose.Runtime.Json;
using JsonEnum = NeoCompose.Runtime.Json.Enum;
using Member = NeoCompose.Runtime.Json.Member;

namespace NeoCompose.Runtime
{
    /// <summary>What a <c>Reference&lt;T&gt;</c>'s target row is (P112 §1.1).</summary>
    internal enum NeoReferenceCategory
    {
        /// <summary>The row is the object or collection; a reference to it is the handle.</summary>
        Handle,
        /// <summary>The row is a member slot; reads read it and writes write it.</summary>
        Slot,
        /// <summary>A generic parameter; its binding decides (P112 §3.5).</summary>
        Deferred,
        /// <summary>Not a value type: code, <c>null</c>, or unknown (P112 §1.2).</summary>
        Ineligible,
    }

    /// <summary>
    /// The P112 type rules the runtime applies to references. Mirrors
    /// <c>reference-category.ts</c>, <c>slot-type-info.ts</c>,
    /// <c>type-info-equality.ts</c> and <c>type-info-format.ts</c>.
    /// </summary>
    internal static class NeoReferenceTypes
    {
        private const string VariantWorldKind = "variant";
        private const string LookupVariantWorldKind = "lookupVariant";

        /// <summary>The category of <c>Reference&lt;target&gt;</c>.</summary>
        internal static NeoReferenceCategory Category(TypeInfo target, NeoClient client)
        {
            switch (target.type)
            {
                case MemberKind.Class:
                    return ClassIdOf(target) is string classId && IsVariantClass(client, classId)
                        ? NeoReferenceCategory.Slot
                        : NeoReferenceCategory.Handle;
                case MemberKind.Interface:
                case MemberKind.List:
                case MemberKind.Set:
                case MemberKind.Dictionary:
                case MemberKind.Lookup:
                    return NeoReferenceCategory.Handle;
                case MemberKind.Tuple:
                case MemberKind.Bool:
                case MemberKind.Int:
                case MemberKind.String:
                case MemberKind.Float:
                case MemberKind.Decimal:
                case MemberKind.Enum:
                case MemberKind.Sprite:
                case MemberKind.Audio:
                case MemberKind.Vector2:
                case MemberKind.Vector2Int:
                case MemberKind.Vector3:
                case MemberKind.Vector3Int:
                case MemberKind.Color:
                case MemberKind.DialogueLookup:
                case MemberKind.Reference:
                    return NeoReferenceCategory.Slot;
                case MemberKind.Generic:
                    return NeoReferenceCategory.Deferred;
                default:
                    return NeoReferenceCategory.Ineligible;
            }
        }

        /// <summary>
        /// The seeded <c>NeoVariant</c> and <c>NeoLookupVariant</c> classes.
        /// The language types a variant as a class, but its row stores a
        /// <c>{classId, variantId}</c> value.
        /// </summary>
        internal static bool IsVariantClass(NeoClient client, string classId)
        {
            if (!client.TryGetClass(classId, out NeoSchemaClass? schemaClass))
                return false;
            string? worldKind = schemaClass.system?["worldKind"]?.ToString();
            return worldKind == VariantWorldKind || worldKind == LookupVariantWorldKind;
        }

        internal static bool VariantSelectionMatches(NeoClient client, object? value, TypeInfo typeInfo)
        {
            if (value is not NeoVariantReference variant)
                return false;
            string? classId = ClassIdOf(typeInfo);
            if (classId is null || !IsVariantClass(client, classId))
                return false;
            if (!client.TryGetClass(classId, out NeoSchemaClass? schema))
                return false;
            if (schema.genericParams is not { Count: > 0 })
                return false;
            Dictionary<string, TypeInfo>? arguments = TypeArgumentsOf(typeInfo);
            if (arguments is null || !arguments.TryGetValue(schema.genericParams[0].id, out TypeInfo target))
                return true;
            string? targetClassId = ClassIdOf(target);
            return targetClassId is null || client.ClassChainContains(variant.classId, targetClassId);
        }

        /// <summary>
        /// The type a member slot stores, as its declaration spells it
        /// (P112 §4.3): a Lookup is <c>Reference&lt;…&gt;</c>, a multi-select
        /// is a set of them, and every <c>Set&lt;…&gt;</c> is the Lookup
        /// type-info a <c>Set&lt;T&gt;</c> type position spells. Null for code
        /// members, which no reference can name. <paramref name="env"/>
        /// closes the generic slots of the row's owner (§3.5).
        /// </summary>
        internal static TypeInfo? SlotTypeInfo(
            NeoClient client,
            Member member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? env = null)
        {
            return SlotTypeInfo(client, member, env, new HashSet<string>(StringComparer.Ordinal));
        }

        private static TypeInfo? SlotTypeInfo(
            NeoClient client,
            Member member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? env,
            HashSet<string> seen)
        {
            if (env is not null && member is GenericMember)
                member = NeoGenericResolution.SubstituteMember(client, member, env);
            if (!seen.Add(member.id))
                return null;
            try
            {
                return SlotTypeInfoOf(client, member, env, seen);
            }
            finally
            {
                seen.Remove(member.id);
            }
        }

        private static TypeInfo? SlotTypeInfoOf(
            NeoClient client,
            Member member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? env,
            HashSet<string> seen)
        {
            bool required = member.Requirement == NeoMemberRequirementKind.Required;
            switch (member)
            {
                case LookupMember lookup:
                    {
                        TypeInfo? target = lookup.declaredTypeInfo is { } declared
                            ? env is null ? declared : NeoGenericResolution.SubstituteTypeInfo(client, declared, env)
                            : CollectionEntryType(client, lookup, env, seen);
                        if (target is null)
                            return null;
                        if (lookup.Selection != NeoMemberSelectionKind.Multi)
                            return Reference(target, required);
                        return new LookupTypeInfo
                        {
                            type = MemberKind.Lookup,
                            required = required,
                            entryTypeInfo = Reference(target, true),
                        };
                    }
                case EnumMember enumMember:
                    {
                        var enumType = new EnumTypeInfo
                        {
                            type = MemberKind.Enum,
                            required = enumMember.Selection == NeoMemberSelectionKind.Multi || required,
                            enumId = enumMember.enumId,
                        };
                        if (enumMember.Selection != NeoMemberSelectionKind.Multi)
                            return enumType;
                        return new LookupTypeInfo
                        {
                            type = MemberKind.Lookup,
                            required = required,
                            entryTypeInfo = enumType,
                        };
                    }
                case DialogueLookupMember dialogue:
                    {
                        if (dialogue.Selection != NeoMemberSelectionKind.Multi)
                            return Primitive(MemberKind.DialogueLookup, required);
                        return new LookupTypeInfo
                        {
                            type = MemberKind.Lookup,
                            required = required,
                            entryTypeInfo = Primitive(MemberKind.DialogueLookup, true),
                        };
                    }
                case TupleMember tuple:
                    {
                        var elements = new TupleElementTypeInfo[tuple.elementMemberIds.Length];
                        for (int i = 0; i < elements.Length; i++)
                        {
                            string id = tuple.elementMemberIds[i];
                            TypeInfo? element = NestedSlotTypeInfo(client, id, env, seen);
                            if (element is null || !client.TryGetMember(id, out Member? descriptor))
                                return null;
                            elements[i] = new TupleElementTypeInfo
                            {
                                typeInfo = element,
                                name = descriptor.name == $"Item{i + 1}" ? null : descriptor.name,
                            };
                        }
                        return new TupleTypeInfo { type = MemberKind.Tuple, required = required, elements = elements };
                    }
                case ListMember list:
                    {
                        TypeInfo? entry = NestedSlotTypeInfo(client, list.entryMemberId, env, seen);
                        if (entry is null)
                            return null;
                        if (list is SetMember)
                        {
                            return new LookupTypeInfo
                            {
                                type = MemberKind.Lookup,
                                required = required,
                                entryTypeInfo = entry,
                            };
                        }
                        return new CollectionTypeInfo
                        {
                            type = MemberKind.List,
                            required = required,
                            entryTypeInfo = entry,
                        };
                    }
                case DictionaryMember dictionary:
                    {
                        TypeInfo? entry = NestedSlotTypeInfo(client, dictionary.entryMemberId, env, seen);
                        if (entry is null)
                            return null;
                        return new CollectionTypeInfo
                        {
                            type = MemberKind.Dictionary,
                            required = required,
                            entryTypeInfo = entry,
                            keyEnumId = string.IsNullOrEmpty(dictionary.keyEnumId) ? null : dictionary.keyEnumId,
                        };
                    }
                case ClassMember classMember:
                    {
                        Dictionary<string, TypeInfo>? typeArguments = null;
                        if (classMember.TypeArguments is not null)
                        {
                            foreach (var pair in classMember.TypeArguments)
                            {
                                if (pair.Value.kind != NeoGenericBindingKind.Member)
                                    continue;
                                TypeInfo? argument = NestedSlotTypeInfo(client, pair.Value.memberId, env, seen);
                                if (argument is not null)
                                    (typeArguments ??= new Dictionary<string, TypeInfo>(StringComparer.Ordinal))[pair.Key] = argument;
                            }
                        }
                        return classMember.DeclaredTypeInfo(required, typeArguments);
                    }
                case VariantMember variant:
                    return VariantTypeInfo(client, variant, required);
                case GenericMember generic:
                    // An open slot: the binding decides the stored type (§3.5).
                    return new GenericTypeInfo
                    {
                        type = MemberKind.Generic,
                        required = true,
                        ownerClassId = GenericOwnerClassId(client, generic.genericParamId),
                        genericParamId = generic.genericParamId,
                    };
            }
            switch (member.kind)
            {
                case MemberKind.Bool:
                case MemberKind.Int:
                case MemberKind.String:
                case MemberKind.Float:
                case MemberKind.Decimal:
                case MemberKind.Sprite:
                case MemberKind.Audio:
                case MemberKind.Vector2:
                case MemberKind.Vector2Int:
                case MemberKind.Vector3:
                case MemberKind.Vector3Int:
                case MemberKind.Color:
                    return Primitive(member.kind, required);
                default:
                    return null;
            }
        }

        private static TypeInfo? NestedSlotTypeInfo(
            NeoClient client,
            string? memberId,
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? env,
            HashSet<string> seen)
        {
            if (string.IsNullOrEmpty(memberId) || !client.TryGetMember(memberId!, out Member? nested))
                return null;
            return SlotTypeInfo(client, nested, env, seen);
        }

        /// <summary>A bound Lookup's target: its collection's entry type.</summary>
        private static TypeInfo? CollectionEntryType(
            NeoClient client,
            LookupMember lookup,
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? env,
            HashSet<string> seen)
        {
            TypeInfo? collection = NestedSlotTypeInfo(client, lookup.collectionMemberId, env, seen);
            return collection switch
            {
                CollectionTypeInfo { type: MemberKind.List or MemberKind.Dictionary } entries => entries.entryTypeInfo,
                LookupTypeInfo set => set.entryTypeInfo,
                _ => null,
            };
        }

        private static ClassTypeInfo? VariantTypeInfo(NeoClient client, VariantMember variant, bool required)
        {
            if (variant.targetTypeInfo is null)
                return null;
            string worldKind = variant.valueTypeInfo is null ? VariantWorldKind : LookupVariantWorldKind;
            foreach (NeoSchemaClass schemaClass in client.classes.Values)
            {
                if (schemaClass.system?["worldKind"]?.ToString() != worldKind)
                    continue;
                List<GenericParamDeclaration>? parameters = schemaClass.genericParams;
                if (parameters is null || parameters.Count == 0)
                    return null;
                var typeArguments = new Dictionary<string, TypeInfo>(StringComparer.Ordinal)
                {
                    [parameters[0].id] = variant.targetTypeInfo,
                };
                if (variant.valueTypeInfo is not null && parameters.Count > 1)
                    typeArguments[parameters[1].id] = variant.valueTypeInfo;
                return new ClassTypeInfo
                {
                    type = MemberKind.Class,
                    required = required,
                    classId = schemaClass.id,
                    typeArguments = typeArguments,
                };
            }
            return null;
        }

        private static string GenericOwnerClassId(NeoClient client, string genericParamId)
        {
            foreach (NeoSchemaClass schemaClass in client.classes.Values)
            {
                if (schemaClass.genericParams is null)
                    continue;
                foreach (GenericParamDeclaration parameter in schemaClass.genericParams)
                {
                    if (parameter.id == genericParamId)
                        return schemaClass.id;
                }
            }
            return "";
        }

        /// <summary>
        /// Whether a row whose stored type is <paramref name="stored"/> fits a
        /// slot reference to <paramref name="target"/> (P112 §4.3). A slot
        /// reference also writes the target, so the fit is exact, nullability
        /// included.
        /// </summary>
        internal static bool SlotFits(TypeInfo stored, TypeInfo target) => InvariantlyEqual(stored, target);

        /// <summary>§4.3's fit error. <paramref name="subject"/> is "Reference `Owner.Member`".</summary>
        internal static string FitError(
            NeoClient client,
            string subject,
            string targetId,
            TypeInfo stored,
            TypeInfo target)
        {
            string storedName = Format(client, stored);
            string article = storedName.Length > 0 && "aeiouAEIOU".IndexOf(storedName[0]) >= 0 ? "an" : "a";
            return $"{subject} target '{targetId}' is {article} {storedName}, but the reference declares {Format(client, target)}.";
        }

        internal static bool InvariantlyEqual(TypeInfo left, TypeInfo right)
        {
            if (left.type != right.type)
                return false;
            if (left.required != right.required)
                return false;
            switch (left.type)
            {
                case MemberKind.Tuple:
                    {
                        TupleTypeInfo? a = TupleTypeInfo.From(left);
                        TupleTypeInfo? b = TupleTypeInfo.From(right);
                        if (a is null || b is null || a.elements.Length != b.elements.Length)
                            return false;
                        for (int i = 0; i < a.elements.Length; i++)
                            if (!InvariantlyEqual(a.elements[i].typeInfo, b.elements[i].typeInfo))
                                return false;
                        return true;
                    }
                case MemberKind.Class:
                    {
                        if (ClassIdOf(left) != ClassIdOf(right))
                            return false;
                        Dictionary<string, TypeInfo>? leftArguments = TypeArgumentsOf(left);
                        Dictionary<string, TypeInfo>? rightArguments = TypeArgumentsOf(right);
                        int leftCount = leftArguments?.Count ?? 0;
                        if (leftCount != (rightArguments?.Count ?? 0))
                            return false;
                        if (leftCount == 0)
                            return true;
                        foreach (var pair in leftArguments!)
                        {
                            if (!rightArguments!.TryGetValue(pair.Key, out TypeInfo? rightArgument))
                                return false;
                            if (!InvariantlyEqual(pair.Value, rightArgument))
                                return false;
                        }
                        return true;
                    }
                case MemberKind.Interface:
                    return InterfaceIdOf(left) == InterfaceIdOf(right);
                case MemberKind.Enum:
                    return EnumIdOf(left) == EnumIdOf(right);
                case MemberKind.Generic:
                    return GenericParamIdOf(left) == GenericParamIdOf(right);
                case MemberKind.List:
                case MemberKind.Set:
                case MemberKind.Dictionary:
                    if (KeyEnumIdOf(left) != KeyEnumIdOf(right))
                        return false;
                    return EntriesEqual(left, right);
                case MemberKind.Lookup:
                    if (CollectionMemberIdOf(left) != CollectionMemberIdOf(right))
                        return false;
                    if (CollectionValueIdOf(left) != CollectionValueIdOf(right))
                        return false;
                    return EntriesEqual(left, right);
                case MemberKind.Reference:
                    {
                        TypeInfo? leftTarget = TargetOf(left);
                        TypeInfo? rightTarget = TargetOf(right);
                        if (leftTarget is null || rightTarget is null)
                            return leftTarget is null && rightTarget is null;
                        return InvariantlyEqual(leftTarget, rightTarget);
                    }
                default:
                    return true;
            }
        }

        private static bool EntriesEqual(TypeInfo left, TypeInfo right)
        {
            TypeInfo? leftEntry = EntryOf(left);
            TypeInfo? rightEntry = EntryOf(right);
            if (leftEntry is null || rightEntry is null)
                return leftEntry is null && rightEntry is null;
            return InvariantlyEqual(leftEntry, rightEntry);
        }

        /// <summary>A type-info spelled the way NeoScript source writes it.</summary>
        internal static string Format(NeoClient client, TypeInfo typeInfo)
        {
            var text = new StringBuilder();
            AppendFormat(client, typeInfo, text);
            return text.ToString();
        }

        private static void AppendFormat(NeoClient client, TypeInfo typeInfo, StringBuilder text)
        {
            switch (typeInfo.type)
            {
                case MemberKind.Null:
                    text.Append("null");
                    return;
                case MemberKind.Int:
                    text.Append("int");
                    break;
                case MemberKind.Float:
                    text.Append("float");
                    break;
                case MemberKind.Bool:
                    text.Append("bool");
                    break;
                case MemberKind.String:
                    text.Append("string");
                    break;
                case MemberKind.Decimal:
                    text.Append("decimal");
                    break;
                case MemberKind.Sprite:
                    text.Append("SpriteInfo");
                    break;
                case MemberKind.Audio:
                    text.Append("AudioClipInfo");
                    break;
                case MemberKind.Vector2:
                case MemberKind.Vector2Int:
                case MemberKind.Vector3:
                case MemberKind.Vector3Int:
                case MemberKind.Color:
                    text.Append(typeInfo.type.ToString());
                    break;
                case MemberKind.DialogueLookup:
                    text.Append("NeoDialogueReference");
                    break;
                case MemberKind.Class:
                    {
                        string classId = ClassIdOf(typeInfo) ?? "";
                        text.Append(client.TryGetClass(classId, out NeoSchemaClass? schemaClass) ? schemaClass.name : classId);
                        break;
                    }
                case MemberKind.Interface:
                    {
                        string interfaceId = InterfaceIdOf(typeInfo) ?? "";
                        text.Append(client.TryGetInterface(interfaceId, out Interface? declaration) ? declaration.name : interfaceId);
                        break;
                    }
                case MemberKind.Enum:
                    {
                        string enumId = EnumIdOf(typeInfo) ?? "";
                        text.Append(client.TryGetEnum(enumId, out JsonEnum? enumInfo) ? enumInfo.name : enumId);
                        break;
                    }
                case MemberKind.List:
                    AppendWrapped(client, "List<", EntryOf(typeInfo), text);
                    break;
                case MemberKind.Set:
                case MemberKind.Lookup:
                    AppendWrapped(client, "Set<", EntryOf(typeInfo), text);
                    break;
                case MemberKind.Dictionary:
                    AppendWrapped(client, "Dictionary<string, ", EntryOf(typeInfo), text);
                    break;
                case MemberKind.Reference:
                    AppendWrapped(client, "Reference<", TargetOf(typeInfo), text);
                    break;
                case MemberKind.Generic:
                    text.Append("Generic<").Append(GenericParamIdOf(typeInfo)).Append('>');
                    break;
                default:
                    text.Append(typeInfo.type.ToString());
                    break;
            }
            if (!typeInfo.required)
                text.Append('?');
        }

        private static void AppendWrapped(NeoClient client, string open, TypeInfo? inner, StringBuilder text)
        {
            text.Append(open);
            if (inner is null)
                text.Append("unknown");
            else
                AppendFormat(client, inner, text);
            text.Append('>');
        }

        internal static ReferenceTypeInfo Reference(TypeInfo target, bool required) => new()
        {
            type = MemberKind.Reference,
            required = required,
            targetTypeInfo = target,
        };

        private static PrimitiveTypeInfo Primitive(MemberKind kind, bool required) => new()
        {
            type = kind,
            required = required,
        };

        // A function argument carries every arm's fields on one shape.
        private static string? ClassIdOf(TypeInfo typeInfo) => typeInfo switch
        {
            ClassTypeInfo classType => classType.classId,
            FunctionArgumentTypeInfo argument => argument.classId,
            _ => null,
        };

        private static Dictionary<string, TypeInfo>? TypeArgumentsOf(TypeInfo typeInfo) => typeInfo switch
        {
            ClassTypeInfo classType => classType.typeArguments,
            FunctionArgumentTypeInfo argument => argument.typeArguments,
            _ => null,
        };

        private static string? InterfaceIdOf(TypeInfo typeInfo) => typeInfo switch
        {
            InterfaceTypeInfo interfaceType => interfaceType.interfaceId,
            FunctionArgumentTypeInfo argument => argument.interfaceId,
            _ => null,
        };

        private static string? EnumIdOf(TypeInfo typeInfo) => typeInfo switch
        {
            EnumTypeInfo enumType => enumType.enumId,
            FunctionArgumentTypeInfo argument => argument.enumId,
            _ => null,
        };

        private static string? GenericParamIdOf(TypeInfo typeInfo) => typeInfo switch
        {
            GenericTypeInfo generic => generic.genericParamId,
            FunctionArgumentTypeInfo argument => argument.genericParamId,
            _ => null,
        };

        private static TypeInfo? EntryOf(TypeInfo typeInfo) => typeInfo switch
        {
            CollectionTypeInfo collection => collection.entryTypeInfo,
            LookupTypeInfo lookup => lookup.entryTypeInfo,
            FunctionArgumentTypeInfo argument => argument.entryTypeInfo,
            _ => null,
        };

        private static string? KeyEnumIdOf(TypeInfo typeInfo)
        {
            string? keyEnumId = typeInfo switch
            {
                CollectionTypeInfo collection => collection.keyEnumId,
                FunctionArgumentTypeInfo argument => argument.keyEnumId,
                _ => null,
            };
            return string.IsNullOrEmpty(keyEnumId) ? null : keyEnumId;
        }

        private static string? CollectionMemberIdOf(TypeInfo typeInfo) => typeInfo switch
        {
            LookupTypeInfo lookup => lookup.collectionMemberId,
            FunctionArgumentTypeInfo argument => argument.collectionMemberId,
            _ => null,
        };

        private static string? CollectionValueIdOf(TypeInfo typeInfo) => typeInfo switch
        {
            LookupTypeInfo lookup => lookup.collectionValueId,
            FunctionArgumentTypeInfo argument => argument.collectionValueId,
            _ => null,
        };

        internal static TypeInfo? TargetOf(TypeInfo typeInfo) => typeInfo switch
        {
            ReferenceTypeInfo reference => reference.targetTypeInfo,
            FunctionArgumentTypeInfo argument => argument.targetTypeInfo,
            _ => null,
        };
    }
}
