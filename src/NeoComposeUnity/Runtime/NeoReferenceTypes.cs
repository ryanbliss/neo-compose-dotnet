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
            if (env is not null)
                member = CloseThrough(client, member, env);
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
                case ObjectMember objectMember:
                    return new ObjectTypeInfo { type = MemberKind.Object, required = required, objectType = objectMember.objectType };
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
                                if (pair.Value.IsOpen)
                                    continue;
                                TypeInfo? argument = NestedSlotTypeInfo(client, NeoTypeBindingViews.Id(pair.Value), env, seen);
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

        /// <summary>
        /// <paramref name="member"/> with the params <paramref name="env"/>
        /// binds substituted. A param it leaves open, or doesn't hold, stays
        /// open, as in a class default's template row.
        /// </summary>
        private static Member CloseThrough(
            NeoClient client,
            Member member,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            switch (member)
            {
                case GenericMember generic:
                    return env.TryGetValue(generic.genericParamId, out NeoGenericEnvEntry? entry) && entry.IsBound
                        ? NeoGenericResolution.SubstituteMember(client, member, env)
                        : member;
                case ClassMember { TypeArguments: { } arguments }:
                    foreach (TypeBinding argument in arguments.Values)
                    {
                        if (argument.IsOpen && !env.ContainsKey(argument.Param!))
                            return member;
                    }
                    return NeoGenericResolution.SubstituteMember(client, member, env);
                default:
                    return member;
            }
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

        /// <summary>
        /// P113 §3.6. The type a class row has as a reference target: its
        /// class, closed by <paramref name="slot"/> when the class is open,
        /// the slot's type read in <paramref name="env"/>, its
        /// <see cref="RowSlotEnvironment"/>. Null when it stays open. The
        /// server's <c>classRowTypeInfo</c>.
        /// </summary>
        internal static ClassTypeInfo? ClassRowTypeInfo(
            NeoClient client,
            string rowClassId,
            Member? slot,
            IReadOnlyDictionary<string, NeoGenericEnvEntry>? env = null)
        {
            if (ClosedClassRowType(client, rowClassId) is { } closed)
                return closed;
            if (slot is null)
                return null;
            return OpenClassRowType(client, rowClassId, SlotTypeInfo(client, slot, env is { Count: > 0 } ? env : null));
        }

        /// <summary>
        /// P127 §1.2. The generic environment row <paramref name="valueId"/>'s
        /// slot reads its type in: its owner's own, read from the owner's
        /// record one hop away (§12.1). So in <c>Hold&lt;T&gt; { Box&lt;T&gt;
        /// Inner; }</c>, a <c>Hold&lt;Seed&gt;</c>'s <c>Inner</c> row reads
        /// <c>Box&lt;Seed&gt;</c>. Empty for a root. The server's <c>RowSlots</c>.
        /// </summary>
        internal static IReadOnlyDictionary<string, NeoGenericEnvEntry> RowSlotEnvironment(
            NeoClient client,
            string valueId,
            NeoValueOwnership ownership)
        {
            if (!client.TryFindOwnedParent(ownership, valueId, out string? parentId, out NeoValueOwnership parentOwnership)
                || parentId.StartsWith("member:", StringComparison.Ordinal)
                || parentId.StartsWith("static:", StringComparison.Ordinal))
            {
                return NeoGenericResolution.EmptyEnv;
            }
            return OwnEnvironment(client, parentId, parentOwnership);
        }

        /// <summary>
        /// The member row <paramref name="valueId"/> is placed in, as declared
        /// rather than closed through its owners, for
        /// <see cref="RowSlotEnvironment"/> to close, so an owner chain that
        /// cycles closes nothing. The server's <c>RowSlots.slot</c>.
        /// </summary>
        internal static Member? DeclaredRowSlot(NeoClient client, string valueId)
        {
            if (!client.TryInferMemberForValueId(valueId, out Member? inferred))
                return null;
            return client.TryGetMember(inferred.id, out Member? declared) && NeoClient.NeedsOwnedMemberContext(declared)
                ? declared
                : inferred;
        }

        /// <summary>The environment a placed row's own members read in, from its record alone (§12.1).</summary>
        private static IReadOnlyDictionary<string, NeoGenericEnvEntry> OwnEnvironment(
            NeoClient client,
            string valueId,
            NeoValueOwnership ownership)
        {
            NeoValueNode? node = null;
            if (client.ReadValue(ownership, valueId, ref node) is not MemberValue row)
                throw new InvalidOperationException($"Owner \"{valueId}\" has no {ownership} row.");
            if (row.typeSource is not null && row.classId is string classId)
                return NeoTypeSources.TypeSourcedClassRowEnv(client, row, classId);
            if (!client.TryInferMemberForValueId(valueId, out Member? placing))
                throw new InvalidOperationException($"Owner \"{valueId}\" is placed under no member.");
            return NeoGenericResolution.EnvFromStamp(client, NeoTypeSources.OwnStamp(
                client,
                valueId,
                row,
                placing,
                id => client.TryGetValue(id, out MemberValue? target) ? target : null));
        }

        /// <summary>
        /// Whether <paramref name="typeInfo"/> names a generic class without
        /// all its arguments anywhere, as an unbound param substituted away
        /// leaves it. The server's <c>typeInfoLacksClassArguments</c>.
        /// </summary>
        private static bool LacksClassArguments(NeoClient client, TypeInfo typeInfo)
        {
            if (ClassIdOf(typeInfo) is { } classId
                && typeInfo.type == MemberKind.Class
                && client.TryGetClass(classId, out NeoSchemaClass? schemaClass)
                && schemaClass.genericParams is { } parameters)
            {
                Dictionary<string, TypeInfo>? arguments = TypeArgumentsOf(typeInfo);
                foreach (GenericParamDeclaration parameter in parameters)
                {
                    if (arguments is null || !arguments.ContainsKey(parameter.id))
                        return true;
                }
            }
            if (TypeArgumentsOf(typeInfo) is { } typeArguments)
            {
                foreach (TypeInfo argument in typeArguments.Values)
                {
                    if (LacksClassArguments(client, argument))
                        return true;
                }
            }
            if (TupleTypeInfo.From(typeInfo) is { } tuple)
            {
                foreach (TupleElementTypeInfo element in tuple.elements)
                {
                    if (LacksClassArguments(client, element.typeInfo))
                        return true;
                }
            }
            if (TargetOf(typeInfo) is { } target && LacksClassArguments(client, target))
                return true;
            if (EntryOf(typeInfo) is { } entry && LacksClassArguments(client, entry))
                return true;
            if (ReturnOf(typeInfo) is { type: not MemberKind.Void } returned && LacksClassArguments(client, returned))
                return true;
            if (ArgumentTypesOf(typeInfo) is { } argumentTypes)
            {
                foreach (TypeInfo argument in argumentTypes)
                {
                    if (LacksClassArguments(client, argument))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A row of a class without type parameters, or of an unknown class:
        /// the class itself. Null for a generic class, which its slot closes.
        /// </summary>
        internal static ClassTypeInfo? ClosedClassRowType(NeoClient client, string rowClassId)
        {
            if (client.TryGetClass(rowClassId, out NeoSchemaClass? rowClass) && rowClass.genericParams is { Count: > 0 })
                return null;
            return new ClassTypeInfo { type = MemberKind.Class, required = true, classId = rowClassId };
        }

        /// <summary>
        /// A generic class's row closed by <paramref name="stored"/>, its
        /// slot's type: the slot's arguments when it names the row's class,
        /// the arguments worked back from a base it names (P127 §1.2), or
        /// those an interface it names determines (P113 §3.6). Null when the
        /// slot leaves the row open, names its class bare, or forwards an
        /// owner's open parameters.
        /// </summary>
        internal static ClassTypeInfo? OpenClassRowType(NeoClient client, string rowClassId, TypeInfo? stored)
        {
            if (stored is null || NeoNSFunctionRuntime.ContainsGeneric(stored))
                return null;
            if (LacksClassArguments(client, stored))
                return null;
            if (stored is ClassTypeInfo sameClass && sameClass.classId == rowClassId)
            {
                return sameClass.typeArguments is { Count: > 0 } sameArguments
                    ? new ClassTypeInfo { type = MemberKind.Class, required = true, classId = rowClassId, typeArguments = sameArguments }
                    : null;
            }
            Dictionary<string, TypeInfo>? typeArguments = stored switch
            {
                ClassTypeInfo classType => BaseSlotRowTypeArguments(client, rowClassId, classType),
                InterfaceTypeInfo interfaceType => InterfaceSlotRowTypeArguments(client, rowClassId, interfaceType),
                _ => null,
            };
            if (typeArguments is null)
                return null;
            return new ClassTypeInfo { type = MemberKind.Class, required = true, classId = rowClassId, typeArguments = typeArguments };
        }

        /// <summary>
        /// The arguments an open row takes from a slot typed by one of its
        /// bases, as C# infers them: <c>DerivedBox&lt;T&gt;</c> from
        /// <c>Box&lt;Seed&gt;</c>, and <c>ListBox&lt;T&gt; : Box&lt;List&lt;T&gt;&gt;</c>
        /// from <c>Box&lt;List&lt;Seed&gt;&gt;</c>. Null when what the row passes
        /// doesn't unify with the slot's arguments, or leaves a row param
        /// unbound, since the row then stays open. The server's
        /// <c>baseSlotRowTypeArguments</c>.
        /// </summary>
        private static Dictionary<string, TypeInfo>? BaseSlotRowTypeArguments(NeoClient client, string rowClassId, ClassTypeInfo slot)
        {
            if (client.TypeRules.UnifyRowParams(client, rowClassId, slot.classId, slot.typeArguments) is not { } bound)
                return null;
            if (!client.TryGetClass(rowClassId, out NeoSchemaClass? rowClass))
                return null;
            foreach (GenericParamDeclaration parameter in rowClass.genericParams!)
            {
                if (!bound.ContainsKey(parameter.id))
                    return null;
            }
            return bound;
        }

        /// <summary>The arguments an interface slot determines for a row (P113 §3.6).</summary>
        private static Dictionary<string, TypeInfo>? InterfaceSlotRowTypeArguments(NeoClient client, string rowClassId, InterfaceTypeInfo slot)
        {
            if (slot.typeArguments is not { } interfaceArguments
                || client.InterfaceImplementers.Determination(rowClassId, slot.interfaceId) is not
                { kind: NeoInterfaceDeterminationKind.Determined, paramByPosition: { } paramByPosition })
            {
                return null;
            }
            var typeArguments = new Dictionary<string, TypeInfo>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> position in paramByPosition)
            {
                if (interfaceArguments.TryGetValue(position.Key, out TypeInfo? argument))
                    typeArguments[position.Value] = argument;
            }
            return typeArguments;
        }

        /// <summary>
        /// P127 §1.2. Whether a row of <paramref name="rowClassId"/> converts
        /// to class <paramref name="target"/>. <paramref name="rowType"/> is
        /// its <see cref="ClassRowTypeInfo"/>, null when its slot leaves an
        /// open row unclosed: the row's own params are then inferred from the
        /// target, as C# infers them. A type that stays open fits. The class
        /// arm of the server's <c>classRowFits</c>.
        /// </summary>
        internal static bool ClassRowFits(NeoClient client, string rowClassId, ClassTypeInfo? rowType, ClassTypeInfo target) =>
            rowType is null
                ? client.TypeRules.OpenRowConvertsToClass(client, rowClassId, target)
                : client.TypeRules.Assignable(client, rowType, target) != false;

        /// <summary>
        /// P113 §3.6. Why unbound interface <paramref name="lookup"/> can't
        /// select <paramref name="targetId"/>, a row of
        /// <paramref name="rowClassId"/> held in <paramref name="slot"/>, read
        /// in <paramref name="env"/>: the interface must close or determine
        /// the row's class, which must implement it with the reference's
        /// arguments. An open row is judged
        /// unclosed first, then with the arguments its slot gives it. Null when
        /// it fits. The interface arm of the server's
        /// <c>objectTargetViolation</c>, word for word.
        /// </summary>
        internal static string? InterfaceTargetRefusal(
            NeoClient client,
            LookupMember lookup,
            string targetId,
            string rowClassId,
            Member? slot,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (lookup.declaredTypeInfo is not InterfaceTypeInfo declared)
                return null;
            if (!client.TryGetClass(rowClassId, out NeoSchemaClass? rowClass))
                return null;
            string label = client.FindSchemaPlacement(lookup.id) is { } placement
                ? $"{placement.ownerClass.name}.{lookup.name}"
                : lookup.name;
            NeoInterfaceImplementers implementers = client.InterfaceImplementers;
            if (implementers.TargetRefusal(label, targetId, rowClassId, declared, rowArguments: null) is { } refusal)
                return refusal;
            if (declared.typeArguments is null || rowClass.genericParams is not { Count: > 0 })
                return null;
            if (ClassRowTypeInfo(client, rowClassId, slot, env)?.typeArguments is not { } rowArguments)
                return null;
            return implementers.TargetRefusal(label, targetId, rowClassId, declared, rowArguments);
        }

        /// <summary>
        /// P127 §1.2. Why unbound <paramref name="lookup"/> can't select
        /// <paramref name="targetId"/>, a row of its declared class or a
        /// subclass, <paramref name="rowClassId"/>, held in <paramref name="slot"/>
        /// read in <paramref name="env"/>.
        /// A closed generic class target needs the row's closed type to
        /// convert to it, so a <c>Derived&lt;T&gt; : Base&lt;T&gt;</c> row
        /// passes its arguments up to <c>Base</c>; an open row is first judged
        /// with its own params inferred from the target. Null when it converts. The
        /// server's <c>closedClassTargetViolation</c>, word for word.
        /// </summary>
        internal static string? ClosedClassTargetRefusal(
            NeoClient client,
            LookupMember lookup,
            string targetId,
            string rowClassId,
            Member? slot,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (lookup.declaredTypeInfo is not ClassTypeInfo { typeArguments: { Count: > 0 } } declared)
                return null;
            if (!client.ClassChainContains(rowClassId, declared.classId))
                return null;
            ClassTypeInfo? refused = null;
            // The server judges an open row unclosed first, then closed by its slot.
            if (ClosedClassRowType(client, rowClassId) is null && !ClassRowFits(client, rowClassId, null, declared))
                refused = new ClassTypeInfo { type = MemberKind.Class, required = true, classId = rowClassId };
            else if (ClassRowTypeInfo(client, rowClassId, slot, env) is { } rowType && !ClassRowFits(client, rowClassId, rowType, declared))
                refused = rowType;
            if (refused is null)
                return null;
            string expected = Format(client, new ClassTypeInfo
            {
                type = MemberKind.Class,
                required = true,
                classId = declared.classId,
                typeArguments = declared.typeArguments,
            });
            string actual = Format(client, refused);
            return $"lookup member {lookup.name} target {targetId} is {actual}, but the member declares {expected}. Point it at a {expected} row.";
        }

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

        /// <summary>
        /// The server's <c>typeInfosInvariantlyEqual</c> with
        /// <c>NullabilityComparison.Annotated</c>: every <c>?</c> counts, at
        /// every depth. Runtime type keys are the erased comparison.
        /// </summary>
        internal static bool InvariantlyEqual(TypeInfo left, TypeInfo right) =>
            left.required == right.required && ShapesEqual(left, right);

        /// <summary><see cref="InvariantlyEqual"/> with the outer <c>?</c> ignored.</summary>
        internal static bool ShapesEqual(TypeInfo left, TypeInfo right)
        {
            if (left.type != right.type)
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
                    if (ClassIdOf(left) != ClassIdOf(right))
                        return false;
                    return TypeArgumentsEqual(TypeArgumentsOf(left), TypeArgumentsOf(right));
                case MemberKind.Interface:
                    if (InterfaceIdOf(left) != InterfaceIdOf(right))
                        return false;
                    return TypeArgumentsEqual(TypeArgumentsOf(left), TypeArgumentsOf(right));
                case MemberKind.Enum:
                    return EnumIdOf(left) == EnumIdOf(right);
                case MemberKind.Object:
                    return ObjectTypeOf(left) == ObjectTypeOf(right);
                case MemberKind.Generic:
                    {
                        // P110 §3: a function's own params match by position.
                        int? leftIndex = FunctionGenericIndexOf(left);
                        int? rightIndex = FunctionGenericIndexOf(right);
                        if (leftIndex is not null || rightIndex is not null)
                            return leftIndex == rightIndex;
                        return GenericParamIdOf(left) == GenericParamIdOf(right)
                            && OwnerInterfaceIdOf(left) == OwnerInterfaceIdOf(right)
                            && OwnerClassIdOf(left) == OwnerClassIdOf(right);
                    }
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
                    return BothNullOrEqual(TargetOf(left), TargetOf(right));
                case MemberKind.NSDelegate:
                    if (!BothNullOrEqual(ReturnOf(left), ReturnOf(right)))
                        return false;
                    return ArgumentsEqual(ArgumentTypesOf(left), ArgumentTypesOf(right));
                case MemberKind.NSAction:
                    return ArgumentsEqual(ArgumentTypesOf(left), ArgumentTypesOf(right));
                default:
                    return true;
            }
        }

        private static bool TypeArgumentsEqual(Dictionary<string, TypeInfo>? left, Dictionary<string, TypeInfo>? right)
        {
            int count = left?.Count ?? 0;
            if (count != (right?.Count ?? 0))
                return false;
            if (count == 0)
                return true;
            foreach (var pair in left!)
            {
                if (!right!.TryGetValue(pair.Key, out TypeInfo? rightArgument))
                    return false;
                if (!InvariantlyEqual(pair.Value, rightArgument))
                    return false;
            }
            return true;
        }

        private static bool ArgumentsEqual(IReadOnlyList<TypeInfo>? left, IReadOnlyList<TypeInfo>? right)
        {
            int count = left?.Count ?? 0;
            if (count != (right?.Count ?? 0))
                return false;
            for (int i = 0; i < count; i++)
            {
                if (!InvariantlyEqual(left![i], right![i]))
                    return false;
            }
            return true;
        }

        private static bool BothNullOrEqual(TypeInfo? left, TypeInfo? right)
        {
            if (left is null || right is null)
                return left is null && right is null;
            return InvariantlyEqual(left, right);
        }

        private static bool EntriesEqual(TypeInfo left, TypeInfo right) => BothNullOrEqual(EntryOf(left), EntryOf(right));

        /// <summary>A type-info spelled the way NeoScript source writes it.</summary>
        internal static string Format(NeoClient client, TypeInfo typeInfo)
        {
            var text = new StringBuilder();
            AppendFormat(client, typeInfo, text);
            return text.ToString();
        }

        /// <summary>The server's <c>formatTypeInfo</c>, byte for byte.</summary>
        private static void AppendFormat(NeoClient client, TypeInfo typeInfo, StringBuilder text)
        {
            switch (typeInfo.type)
            {
                case MemberKind.Null:
                    text.Append("null");
                    return;
                case MemberKind.Void:
                    text.Append("void");
                    return;
                case MemberKind.Unknown:
                    text.Append("unknown");
                    break;
                case MemberKind.Int:
                case MemberKind.Float:
                case MemberKind.Bool:
                case MemberKind.String:
                case MemberKind.Decimal:
                case MemberKind.Sprite:
                case MemberKind.Audio:
                case MemberKind.Vector2:
                case MemberKind.Vector2Int:
                case MemberKind.Vector3:
                case MemberKind.Vector3Int:
                case MemberKind.Color:
                case MemberKind.DialogueLookup:
                    text.Append(NeoTypeNames.LeafName(typeInfo.type));
                    break;
                case MemberKind.Object:
                    text.Append(NeoTypeNames.ObjectName(ObjectTypeOf(typeInfo)));
                    break;
                case MemberKind.Tuple:
                    {
                        TupleElementTypeInfo[] elements = TupleTypeInfo.From(typeInfo)?.elements ?? Array.Empty<TupleElementTypeInfo>();
                        text.Append('(');
                        for (int i = 0; i < elements.Length; i++)
                        {
                            if (i > 0)
                                text.Append(", ");
                            AppendFormat(client, elements[i].typeInfo, text);
                            if (elements[i].name is { Length: > 0 } name)
                                text.Append(' ').Append(name);
                        }
                        text.Append(')');
                        break;
                    }
                case MemberKind.Class:
                    {
                        string classId = ClassIdOf(typeInfo) ?? "";
                        text.Append(NeoTypeNames.ClassName(classId, client.ProjectDataForRuntime));
                        AppendTypeArguments(client, TypeArgumentsOf(typeInfo), text);
                        break;
                    }
                case MemberKind.Interface:
                    {
                        string interfaceId = InterfaceIdOf(typeInfo) ?? "";
                        text.Append(NeoTypeNames.InterfaceName(interfaceId, client.ProjectDataForRuntime));
                        AppendTypeArguments(client, TypeArgumentsOf(typeInfo), text);
                        break;
                    }
                case MemberKind.Enum:
                    text.Append(EnumName(client, EnumIdOf(typeInfo) ?? ""));
                    break;
                case MemberKind.List:
                    AppendWrapped(client, "List<", EntryOf(typeInfo), text);
                    break;
                case MemberKind.Set:
                case MemberKind.Lookup:
                    AppendWrapped(client, "Set<", EntryOf(typeInfo), text);
                    break;
                case MemberKind.Dictionary:
                    {
                        string? keyEnumId = KeyEnumIdOf(typeInfo);
                        string key = keyEnumId is null ? "string" : EnumName(client, keyEnumId);
                        AppendWrapped(client, $"Dictionary<{key}, ", EntryOf(typeInfo), text);
                        break;
                    }
                case MemberKind.Reference:
                    AppendWrapped(client, "Reference<", TargetOf(typeInfo), text);
                    break;
                case MemberKind.Generic:
                    if (FunctionGenericIndexOf(typeInfo) is int index)
                        text.Append('T').Append(index);
                    else
                        text.Append("Generic<").Append(GenericParamIdOf(typeInfo)).Append('>');
                    break;
                case MemberKind.NSDelegate:
                    text.Append("NeoDelegate<");
                    AppendFormat(client, ReturnOf(typeInfo) ?? new UnknownTypeInfo { type = MemberKind.Unknown, required = true }, text);
                    foreach (TypeInfo argument in ArgumentTypesOf(typeInfo) ?? Array.Empty<TypeInfo>())
                    {
                        text.Append(", ");
                        AppendFormat(client, argument, text);
                    }
                    text.Append('>');
                    break;
                case MemberKind.NSAction:
                    {
                        // Always void, never nullable (P62 §2.1): no suffix.
                        text.Append("NeoAction<");
                        TypeInfo[] arguments = ArgumentTypesOf(typeInfo) ?? Array.Empty<TypeInfo>();
                        for (int i = 0; i < arguments.Length; i++)
                        {
                            if (i > 0)
                                text.Append(", ");
                            AppendFormat(client, arguments[i], text);
                        }
                        text.Append('>');
                        return;
                    }
                default:
                    text.Append(typeInfo.type.ToString());
                    break;
            }
            if (!typeInfo.required)
                text.Append('?');
        }

        /// <summary><c>&lt;Seed, int&gt;</c> in declaration order, or nothing when there are none.</summary>
        private static void AppendTypeArguments(NeoClient client, Dictionary<string, TypeInfo>? typeArguments, StringBuilder text)
        {
            if (typeArguments is not { Count: > 0 })
                return;
            text.Append('<');
            bool first = true;
            foreach (TypeInfo argument in typeArguments.Values)
            {
                if (!first)
                    text.Append(", ");
                first = false;
                AppendFormat(client, argument, text);
            }
            text.Append('>');
        }

        private static string EnumName(NeoClient client, string enumId) => NeoTypeNames.EnumName(enumId, client.ProjectDataForRuntime);

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
        internal static string? ClassIdOf(TypeInfo typeInfo) => typeInfo switch
        {
            ClassTypeInfo classType => classType.classId,
            FunctionArgumentTypeInfo argument => argument.classId,
            _ => null,
        };

        internal static Dictionary<string, TypeInfo>? TypeArgumentsOf(TypeInfo typeInfo) => typeInfo switch
        {
            ClassTypeInfo classType => classType.typeArguments,
            InterfaceTypeInfo interfaceType => interfaceType.typeArguments,
            FunctionArgumentTypeInfo argument => argument.typeArguments,
            _ => null,
        };

        internal static string? InterfaceIdOf(TypeInfo typeInfo) => typeInfo switch
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

        private static NeoObjectTypeKind? ObjectTypeOf(TypeInfo typeInfo) => typeInfo switch
        {
            ObjectTypeInfo objectType => objectType.objectType,
            FunctionArgumentTypeInfo argument => argument.objectType,
            _ => null,
        };

        /// <summary>A function's own type parameter's position (P110 §4), or null for a class or interface param.</summary>
        private static int? FunctionGenericIndexOf(TypeInfo typeInfo) => typeInfo switch
        {
            FunctionGenericTypeInfo generic => generic.index,
            FunctionArgumentTypeInfo { ownerMemberId: not null } argument => argument.index,
            _ => null,
        };

        private static string? OwnerClassIdOf(TypeInfo typeInfo) => typeInfo switch
        {
            GenericTypeInfo generic => generic.ownerClassId,
            FunctionArgumentTypeInfo argument => argument.ownerClassId,
            _ => null,
        };

        private static string? OwnerInterfaceIdOf(TypeInfo typeInfo) => typeInfo switch
        {
            GenericTypeInfo generic => generic.ownerInterfaceId,
            FunctionArgumentTypeInfo argument => argument.ownerInterfaceId,
            _ => null,
        };

        private static TypeInfo? ReturnOf(TypeInfo typeInfo) => typeInfo switch
        {
            DelegateTypeInfo callable => callable.returnTypeInfo,
            FunctionArgumentTypeInfo argument => argument.returnTypeInfo,
            _ => null,
        };

        private static TypeInfo[]? ArgumentTypesOf(TypeInfo typeInfo) => typeInfo switch
        {
            DelegateTypeInfo callable => callable.argumentTypes,
            ActionTypeInfo action => action.argumentTypes,
            FunctionArgumentTypeInfo argument => argument.argumentTypes,
            _ => null,
        };
    }
}
