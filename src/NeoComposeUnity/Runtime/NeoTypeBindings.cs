// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using NeoCompose.Runtime.Json;
using NeoCompose.Runtime.NeoScript;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Converts between <see cref="TypeBinding"/>, the stored form of a
    /// runtime type, and the SDK's in-memory forms: a closed
    /// <see cref="TypeInfo"/>, and the generic environment entry a stamp's
    /// binding reads as (<see cref="Entry"/>).
    /// </summary>
    internal static class NeoTypeBindings
    {
        /// <summary>A closed type as a binding. Throws for a type that names a parameter or no runtime type.</summary>
        internal static TypeBinding FromTypeInfo(TypeInfo typeInfo) => BindingOf(typeInfo, open: false);

        /// <summary>
        /// A type info as a type argument: a closed type whose arms may forward
        /// a class's or interface's parameter, which reads as an
        /// <see cref="TypeBindingKind.Open"/> binding. Throws for a function's
        /// own type parameter or a type with no runtime type.
        /// </summary>
        internal static TypeBinding ArgumentOf(TypeInfo typeInfo) => BindingOf(typeInfo, open: true);

        private static TypeBinding BindingOf(TypeInfo typeInfo, bool open)
        {
            typeInfo = NeoTypeRules.Plain(typeInfo);
            bool required = typeInfo.required;
            switch (typeInfo)
            {
                case ClassTypeInfo classType:
                    return TypeBinding.Closed(
                        new TypeHead(MemberKind.Class, required, classId: classType.classId),
                        Arguments(classType.typeArguments, open));
                case InterfaceTypeInfo interfaceType:
                    return TypeBinding.Closed(
                        new TypeHead(MemberKind.Interface, required, interfaceId: interfaceType.interfaceId),
                        Arguments(interfaceType.typeArguments, open));
                case EnumTypeInfo enumType:
                    return TypeBinding.Closed(new TypeHead(MemberKind.Enum, required, enumId: enumType.enumId));
                case ObjectTypeInfo objectType:
                    return TypeBinding.Closed(new TypeHead(MemberKind.Object, required, objectType: objectType.objectType));
                case CollectionTypeInfo { type: MemberKind.Dictionary } dictionary:
                    {
                        TypeHead key = dictionary.keyEnumId is string keyEnumId
                            ? new TypeHead(MemberKind.Enum, true, enumId: keyEnumId)
                            : new TypeHead(MemberKind.String, true);
                        return Positional(
                            new TypeHead(MemberKind.Dictionary, required),
                            TypeBinding.Closed(key),
                            BindingOf(dictionary.entryTypeInfo, open));
                    }
                case CollectionTypeInfo collection:
                    return Positional(new TypeHead(collection.type, required), BindingOf(collection.entryTypeInfo, open));
                case LookupTypeInfo:
                    throw new InvalidOperationException("A Lookup type has no stored type binding.");
                case ReferenceTypeInfo reference:
                    return Positional(new TypeHead(MemberKind.Reference, required), BindingOf(reference.targetTypeInfo, open));
                case TupleTypeInfo tuple:
                    {
                        var elements = new TypeBinding[tuple.elements.Length];
                        for (int index = 0; index < elements.Length; index++)
                            elements[index] = BindingOf(tuple.elements[index].typeInfo, open);
                        return Positional(new TypeHead(MemberKind.Tuple, required), elements);
                    }
                case DelegateTypeInfo delegateType:
                    {
                        var positions = new TypeBinding[delegateType.argumentTypes.Length + 1];
                        positions[0] = delegateType.returnTypeInfo is VoidTypeInfo
                            ? TypeBinding.Closed(new TypeHead(MemberKind.Void, true))
                            : BindingOf(delegateType.returnTypeInfo, open);
                        for (int index = 0; index < delegateType.argumentTypes.Length; index++)
                            positions[index + 1] = BindingOf(delegateType.argumentTypes[index], open);
                        return Positional(new TypeHead(MemberKind.NSDelegate, required), positions);
                    }
                case ActionTypeInfo actionType:
                    {
                        var positions = new TypeBinding[actionType.argumentTypes.Length];
                        for (int index = 0; index < positions.Length; index++)
                            positions[index] = BindingOf(actionType.argumentTypes[index], open);
                        return Positional(new TypeHead(MemberKind.NSAction, required), positions);
                    }
                case PrimitiveTypeInfo primitive when primitive.type != MemberKind.Null:
                    return TypeBinding.Closed(new TypeHead(primitive.type, required));
                case GenericTypeInfo { ownerClassId: string ownerClassId } generic when open:
                    return TypeBinding.Open(new TypeDefinition(MemberKind.Class, classId: ownerClassId), generic.genericParamId);
                case GenericTypeInfo { ownerInterfaceId: string ownerInterfaceId } generic when open:
                    return TypeBinding.Open(new TypeDefinition(MemberKind.Interface, interfaceId: ownerInterfaceId), generic.genericParamId);
                case GenericTypeInfo generic:
                    throw new InvalidOperationException(
                        $"Type parameter '{generic.genericParamId}' is open, so it has no closed type binding.");
                case FunctionGenericTypeInfo functionGeneric:
                    throw new InvalidOperationException(
                        $"Type parameter {functionGeneric.index} of function '{functionGeneric.ownerMemberId}' is open, so it has no closed type binding.");
                default:
                    throw new InvalidOperationException($"Type {typeInfo.type} names no runtime type, so it has no type binding.");
            }
        }

        private static IReadOnlyDictionary<string, TypeBinding>? Arguments(Dictionary<string, TypeInfo>? typeArguments, bool open)
        {
            if (typeArguments is null || typeArguments.Count == 0)
                return null;
            var bindings = new Dictionary<string, TypeBinding>(typeArguments.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, TypeInfo> pair in typeArguments)
                bindings[pair.Key] = BindingOf(pair.Value, open);
            return bindings;
        }

        private static TypeBinding Positional(TypeHead head, params TypeBinding[] positions)
        {
            if (positions.Length == 0)
                return TypeBinding.Closed(head);
            var bindings = new Dictionary<string, TypeBinding>(positions.Length, StringComparer.Ordinal);
            for (int index = 0; index < positions.Length; index++)
                bindings[index.ToString(CultureInfo.InvariantCulture)] = positions[index];
            return TypeBinding.Closed(head, bindings);
        }

        /// <summary>
        /// A closed binding as a type info, the spelling a type position
        /// reads (<see cref="NeoTypeRules.Declared"/>'s inverse). Throws for
        /// an open, definition or opaque binding.
        /// </summary>
        internal static TypeInfo ToTypeInfo(TypeBinding binding) => TypeInfoOf(binding, open: false);

        /// <summary>
        /// A type argument as a type info: a closed binding whose arms may
        /// forward a class's or interface's parameter, which reads as a
        /// <see cref="GenericTypeInfo"/>. Throws for a definition or opaque binding.
        /// </summary>
        internal static TypeInfo ArgumentTypeInfo(TypeBinding binding) => TypeInfoOf(binding, open: true);

        private static TypeInfo TypeInfoOf(TypeBinding binding, bool open)
        {
            if (open && binding.Kind == TypeBindingKind.Open)
            {
                TypeDefinition owner = binding.Definition!;
                return new GenericTypeInfo
                {
                    type = MemberKind.Generic,
                    required = true,
                    ownerClassId = owner.ClassId,
                    ownerInterfaceId = owner.InterfaceId,
                    genericParamId = binding.Param!,
                };
            }
            if (binding.Kind != TypeBindingKind.Closed)
                throw new InvalidOperationException($"A {binding.Kind} type binding has no closed type info.");
            TypeHead head = binding.Head!;
            if (head.IsOpaque)
                throw new InvalidOperationException($"Opaque type '{head.OpaqueName}' has no type info.");
            bool required = head.Required;
            switch (head.Type)
            {
                case MemberKind.Class:
                    return new ClassTypeInfo
                    {
                        type = MemberKind.Class,
                        required = required,
                        classId = head.ClassId!,
                        typeArguments = TypeArguments(binding.Bindings, open),
                    };
                case MemberKind.Interface:
                    return new InterfaceTypeInfo
                    {
                        type = MemberKind.Interface,
                        required = required,
                        interfaceId = head.InterfaceId!,
                        typeArguments = TypeArguments(binding.Bindings, open),
                    };
                case MemberKind.Enum:
                    return new EnumTypeInfo { type = MemberKind.Enum, required = required, enumId = head.EnumId! };
                case MemberKind.Object:
                    return new ObjectTypeInfo { type = MemberKind.Object, required = required, objectType = head.ObjectType };
                case MemberKind.List:
                    return new CollectionTypeInfo { type = MemberKind.List, required = required, entryTypeInfo = TypeInfoOf(binding.At(0), open) };
                case MemberKind.Set:
                    // A type position spells Set<T> as an unbound Lookup.
                    return new LookupTypeInfo { type = MemberKind.Lookup, required = required, entryTypeInfo = TypeInfoOf(binding.At(0), open) };
                case MemberKind.Dictionary:
                    return new CollectionTypeInfo
                    {
                        type = MemberKind.Dictionary,
                        required = required,
                        keyEnumId = binding.At(0).Head!.EnumId,
                        entryTypeInfo = TypeInfoOf(binding.At(1), open),
                    };
                case MemberKind.Reference:
                    return new ReferenceTypeInfo { type = MemberKind.Reference, required = required, targetTypeInfo = TypeInfoOf(binding.At(0), open) };
                case MemberKind.Tuple:
                    {
                        var elements = new TupleElementTypeInfo[binding.Bindings!.Count];
                        for (int index = 0; index < elements.Length; index++)
                        {
                            elements[index] = new TupleElementTypeInfo
                            {
                                typeInfo = TypeInfoOf(binding.At(index), open),
                                name = head.Names?[index] is { Length: > 0 } name ? name : null,
                            };
                        }
                        return new TupleTypeInfo { type = MemberKind.Tuple, required = required, elements = elements };
                    }
                case MemberKind.NSDelegate:
                    {
                        int count = binding.Bindings!.Count;
                        var arguments = new TypeInfo[count - 1];
                        for (int index = 1; index < count; index++)
                            arguments[index - 1] = TypeInfoOf(binding.At(index), open);
                        TypeBinding result = binding.At(0);
                        return new DelegateTypeInfo
                        {
                            type = MemberKind.NSDelegate,
                            required = required,
                            returnTypeInfo = result.Kind == TypeBindingKind.Closed && result.Head!.Type == MemberKind.Void
                                ? new VoidTypeInfo { type = MemberKind.Void, required = true }
                                : TypeInfoOf(result, open),
                            argumentTypes = arguments,
                        };
                    }
                case MemberKind.NSAction:
                    {
                        int count = binding.Bindings?.Count ?? 0;
                        var arguments = new TypeInfo[count];
                        for (int index = 0; index < count; index++)
                            arguments[index] = TypeInfoOf(binding.At(index), open);
                        return new ActionTypeInfo { type = MemberKind.NSAction, required = required, argumentTypes = arguments };
                    }
                case MemberKind.Void:
                    return new VoidTypeInfo { type = MemberKind.Void, required = true };
                default:
                    return new PrimitiveTypeInfo { type = head.Type, required = required };
            }
        }

        private static Dictionary<string, TypeInfo>? TypeArguments(IReadOnlyDictionary<string, TypeBinding>? bindings, bool open)
        {
            if (bindings is null)
                return null;
            var arguments = new Dictionary<string, TypeInfo>(bindings.Count, StringComparer.Ordinal);
            foreach (KeyValuePair<string, TypeBinding> pair in bindings)
                arguments[pair.Key] = TypeInfoOf(pair.Value, open);
            return arguments;
        }

        /// <summary>
        /// The closed type <paramref name="entry"/> binds, read in
        /// <paramref name="env"/>: what a stamp stores for it. Throws for an
        /// unbound entry.
        /// </summary>
        internal static TypeBinding OfEntry(
            NeoClient client,
            NeoGenericEnvEntry entry,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (!entry.IsBound)
                throw new InvalidOperationException($"Generic param '{entry.unboundParamId}' is unbound, so it has no closed type binding.");
            if (!client.TryGetMember(entry.memberId!, out Member? member))
                throw new InvalidOperationException($"Generic binding member '{entry.memberId}' is missing.");
            TypeInfo typeInfo = NeoNSFunctionRuntime.TypeInfoFromBindingMember(
                client,
                member,
                NeoGenericResolution.BindingEnv(env, entry),
                new HashSet<string>(StringComparer.Ordinal),
                NeoNSFunctionRuntime.BindingTypeProjection.Stored);
            return FromTypeInfo(NeoTypeRules.Declared(typeInfo));
        }

        /// <summary>
        /// The type argument a bound <paramref name="entry"/> stands for: a
        /// binding view's own binding, else the closed type its member reads
        /// as in <paramref name="env"/> (<see cref="OfEntry"/>).
        /// </summary>
        internal static TypeBinding ArgumentOfEntry(
            NeoClient client,
            NeoGenericEnvEntry entry,
            IReadOnlyDictionary<string, NeoGenericEnvEntry> env)
        {
            if (entry.bindings is null
                && entry.memberId is string memberId
                && NeoTypeBindingViews.BindingOf(memberId) is TypeBinding viewed)
            {
                return viewed;
            }
            return OfEntry(client, entry, env);
        }

        /// <summary>The closed type argument the member <paramref name="memberId"/> declares.</summary>
        internal static TypeBinding OfMemberId(NeoClient client, string memberId) =>
            ArgumentOfEntry(client, NeoGenericEnvEntry.Bound(memberId), NeoGenericResolution.EmptyEnv);

        /// <summary>
        /// The environment entry a stored closed binding reads as: its binding
        /// view (<see cref="NeoTypeBindingViews"/>), which names no parameter.
        /// </summary>
        internal static NeoGenericEnvEntry Entry(TypeBinding binding)
        {
            if (binding.Kind != TypeBindingKind.Closed)
                throw new InvalidOperationException($"A {binding.Kind} type binding binds no generic param.");
            return NeoGenericEnvEntry.Bound(NeoTypeBindingViews.Id(binding));
        }
    }
}
