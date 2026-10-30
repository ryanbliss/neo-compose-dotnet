// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>
    /// P110 §5. Binds a generic function's own type parameters. A call frame
    /// holds one concrete type per parameter, by position; signatures and
    /// checks inside the body read it by <c>index</c>.
    /// </summary>
    internal static class NeoFunctionTypeArguments
    {
        /// <summary>Whether <paramref name="typeInfo"/> is a function's own type parameter.</summary>
        internal static bool IsFunctionGeneric(TypeInfo typeInfo) =>
            typeInfo is FunctionGenericTypeInfo
                or FunctionArgumentTypeInfo { type: MemberKind.Generic, ownerMemberId: not null };

        /// <summary>The parameter position of a function's own type parameter.</summary>
        internal static int Index(TypeInfo typeInfo) => typeInfo switch
        {
            FunctionGenericTypeInfo generic => generic.index,
            FunctionArgumentTypeInfo { index: { } index } => index,
            _ => throw new NSGetterRuntimeError(
                $"Type info of kind '{typeInfo.type}' is not a function type parameter."),
        };

        /// <summary>
        /// The executing frame's type for a function type parameter. A frame
        /// whose call supplied none, such as a closure made outside the
        /// function, reads the owner's default.
        /// </summary>
        internal static TypeInfo FrameArgument(TypeInfo typeParameter, TypeInfo[]? table, NeoClient client)
        {
            int index = Index(typeParameter);
            if (table is not null && (uint)index < (uint)table.Length)
                return table[index];
            string ownerMemberId = typeParameter switch
            {
                FunctionGenericTypeInfo generic => generic.ownerMemberId,
                FunctionArgumentTypeInfo argument => argument.ownerMemberId!,
                _ => throw new NSGetterRuntimeError(
                    $"Type info of kind '{typeParameter.type}' is not a function type parameter."),
            };
            client.TryGetMember(ownerMemberId, out Member? owner);
            FunctionGenericParam[]? parameters = owner switch
            {
                NSFunctionMember script => script.genericParams,
                NSPropertyMember property => property.genericParams,
                FunctionMember native => native.genericParams,
                _ => null,
            };
            if (parameters is not null && index < parameters.Length && parameters[index].defaultType is { } defaultType)
                return defaultType;
            throw new NSGetterRuntimeError(
                $"Type parameter {index} of function '{owner?.name ?? ownerMemberId}' has no type argument in this frame and no default.");
        }

        /// <summary>Whether <paramref name="typeInfo"/> names a function's own type parameter anywhere.</summary>
        internal static bool Contains(TypeInfo typeInfo)
        {
            if (IsFunctionGeneric(typeInfo))
                return true;
            foreach (TypeInfo nested in Nested(typeInfo))
            {
                if (Contains(nested))
                    return true;
            }
            return false;
        }

        private static IEnumerable<TypeInfo> Nested(TypeInfo typeInfo)
        {
            switch (typeInfo)
            {
                case TupleTypeInfo tuple:
                    foreach (TupleElementTypeInfo element in tuple.elements)
                        yield return element.typeInfo;
                    break;
                case CollectionTypeInfo collection:
                    yield return collection.entryTypeInfo;
                    break;
                case LookupTypeInfo lookup:
                    yield return lookup.entryTypeInfo;
                    break;
                case ReferenceTypeInfo reference:
                    yield return reference.targetTypeInfo;
                    break;
                case DelegateTypeInfo delegateType:
                    yield return delegateType.returnTypeInfo;
                    foreach (TypeInfo argument in delegateType.argumentTypes)
                        yield return argument;
                    break;
                case ActionTypeInfo actionType:
                    foreach (TypeInfo argument in actionType.argumentTypes)
                        yield return argument;
                    break;
                case ClassTypeInfo { typeArguments: { } typeArguments }:
                    foreach (TypeInfo argument in typeArguments.Values)
                        yield return argument;
                    break;
                case InterfaceTypeInfo { typeArguments: { } interfaceArguments }:
                    foreach (TypeInfo argument in interfaceArguments.Values)
                        yield return argument;
                    break;
                case FunctionArgumentTypeInfo argument:
                    if (argument.elements is not null)
                        foreach (TupleElementTypeInfo element in argument.elements)
                            yield return element.typeInfo;
                    if (argument.entryTypeInfo is not null)
                        yield return argument.entryTypeInfo;
                    if (argument.returnTypeInfo is not null)
                        yield return argument.returnTypeInfo;
                    if (argument.argumentTypes is not null)
                    {
                        foreach (TypeInfo nested in argument.argumentTypes)
                            yield return nested;
                    }
                    if (argument.typeArguments is not null)
                    {
                        foreach (TypeInfo nested in argument.typeArguments.Values)
                            yield return nested;
                    }
                    if (argument.targetTypeInfo is not null)
                        yield return argument.targetTypeInfo;
                    break;
            }
        }

        /// <summary>
        /// Replaces function type parameters with <paramref name="table"/>
        /// entries by position. Returns <paramref name="typeInfo"/> itself when
        /// nothing changed.
        /// </summary>
        internal static TypeInfo Bind(TypeInfo typeInfo, TypeInfo[] table)
        {
            if (typeInfo is FunctionArgumentTypeInfo argument)
                return BindArgument(argument, table);
            if (typeInfo is FunctionGenericTypeInfo generic)
                return Required(Lookup(table, generic.index), typeInfo.required);
            switch (typeInfo)
            {
                case TupleTypeInfo tuple:
                    {
                        TupleElementTypeInfo[] elements = TupleTypeInfo.MapElements(tuple.elements, type => Bind(type, table));
                        if (ReferenceEquals(elements, tuple.elements))
                            return typeInfo;
                        var bound = (TupleTypeInfo)tuple.ShallowClone();
                        bound.elements = elements;
                        return bound;
                    }
                case CollectionTypeInfo collection:
                    {
                        TypeInfo entry = Bind(collection.entryTypeInfo, table);
                        if (ReferenceEquals(entry, collection.entryTypeInfo))
                            return typeInfo;
                        var bound = (CollectionTypeInfo)collection.ShallowClone();
                        bound.entryTypeInfo = entry;
                        return bound;
                    }
                case LookupTypeInfo lookup:
                    {
                        TypeInfo entry = Bind(lookup.entryTypeInfo, table);
                        if (ReferenceEquals(entry, lookup.entryTypeInfo))
                            return typeInfo;
                        var bound = (LookupTypeInfo)lookup.ShallowClone();
                        bound.entryTypeInfo = entry;
                        return bound;
                    }
                case ReferenceTypeInfo reference:
                    {
                        TypeInfo target = Bind(reference.targetTypeInfo, table);
                        if (ReferenceEquals(target, reference.targetTypeInfo))
                            return typeInfo;
                        var bound = (ReferenceTypeInfo)reference.ShallowClone();
                        bound.targetTypeInfo = target;
                        return bound;
                    }
                case DelegateTypeInfo delegateType:
                    {
                        TypeInfo returnTypeInfo = Bind(delegateType.returnTypeInfo, table);
                        TypeInfo[] argumentTypes = BindList(delegateType.argumentTypes, table);
                        if (ReferenceEquals(returnTypeInfo, delegateType.returnTypeInfo)
                            && ReferenceEquals(argumentTypes, delegateType.argumentTypes))
                        {
                            return typeInfo;
                        }
                        var bound = (DelegateTypeInfo)delegateType.ShallowClone();
                        bound.returnTypeInfo = returnTypeInfo;
                        bound.argumentTypes = argumentTypes;
                        return bound;
                    }
                case ActionTypeInfo actionType:
                    {
                        TypeInfo[] argumentTypes = BindList(actionType.argumentTypes, table);
                        if (ReferenceEquals(argumentTypes, actionType.argumentTypes))
                            return typeInfo;
                        var bound = (ActionTypeInfo)actionType.ShallowClone();
                        bound.argumentTypes = argumentTypes;
                        return bound;
                    }
                case ClassTypeInfo { typeArguments: { } typeArguments } classType:
                    {
                        Dictionary<string, TypeInfo>? boundArguments = BindMap(typeArguments, table);
                        if (boundArguments is null)
                            return typeInfo;
                        var bound = (ClassTypeInfo)classType.ShallowClone();
                        bound.typeArguments = boundArguments;
                        return bound;
                    }
                case InterfaceTypeInfo { typeArguments: { } interfaceArguments } interfaceType:
                    {
                        Dictionary<string, TypeInfo>? boundArguments = BindMap(interfaceArguments, table);
                        if (boundArguments is null)
                            return typeInfo;
                        var bound = (InterfaceTypeInfo)interfaceType.ShallowClone();
                        bound.typeArguments = boundArguments;
                        return bound;
                    }
                default:
                    return typeInfo;
            }
        }

        /// <summary>
        /// <see cref="Bind"/> for a named, possibly defaulted parameter. A
        /// parameter typed by a type parameter takes the bound type's fields.
        /// </summary>
        internal static FunctionArgumentTypeInfo BindArgument(FunctionArgumentTypeInfo argument, TypeInfo[] table)
        {
            if (argument.type == MemberKind.Generic && argument.ownerMemberId is not null)
            {
                TypeInfo resolved = Lookup(table, Index(argument));
                FunctionArgumentTypeInfo bound = AsArgument(resolved, argument.name);
                bound.required = NeoGeneratedTypesSupport.ClosedSlotRequired(resolved, declaredNullable: !argument.required);
                // Keep a type-default marker distinct from an explicit null:
                // default(T) must use the closed type, including value types.
                bound.defaultValue = argument.defaultValue;
                bound.mode = argument.mode;
                bound.flow = argument.flow;
                return bound;
            }
            TupleElementTypeInfo[]? elements = argument.elements is null ? null : TupleTypeInfo.MapElements(argument.elements, type => Bind(type, table));
            TypeInfo? entry = argument.entryTypeInfo is null ? null : Bind(argument.entryTypeInfo, table);
            TypeInfo? returnTypeInfo = argument.returnTypeInfo is null ? null : Bind(argument.returnTypeInfo, table);
            TypeInfo[]? argumentTypes = argument.argumentTypes is null ? null : BindList(argument.argumentTypes, table);
            Dictionary<string, TypeInfo>? typeArguments = argument.typeArguments is null
                ? null
                : BindMap(argument.typeArguments, table);
            TypeInfo? target = argument.targetTypeInfo is null ? null : Bind(argument.targetTypeInfo, table);
            if (ReferenceEquals(elements, argument.elements)
                && ReferenceEquals(entry, argument.entryTypeInfo)
                && ReferenceEquals(returnTypeInfo, argument.returnTypeInfo)
                && ReferenceEquals(argumentTypes, argument.argumentTypes)
                && ReferenceEquals(target, argument.targetTypeInfo)
                && typeArguments is null)
            {
                return argument;
            }
            var clone = (FunctionArgumentTypeInfo)argument.ShallowClone();
            clone.elements = elements;
            clone.entryTypeInfo = entry;
            clone.returnTypeInfo = returnTypeInfo;
            clone.argumentTypes = argumentTypes;
            clone.targetTypeInfo = target;
            if (typeArguments is not null)
                clone.typeArguments = typeArguments;
            return clone;
        }

        /// <summary>A bound type in a parameter's flattened shape.</summary>
        internal static FunctionArgumentTypeInfo AsArgument(TypeInfo typeInfo, string name)
        {
            if (typeInfo is FunctionArgumentTypeInfo flattened)
            {
                var renamed = (FunctionArgumentTypeInfo)flattened.ShallowClone();
                renamed.name = name;
                return renamed;
            }
            var argument = new FunctionArgumentTypeInfo
            {
                name = name,
                type = typeInfo.type,
                required = typeInfo.required,
            };
            switch (typeInfo)
            {
                case TupleTypeInfo tuple:
                    argument.elements = tuple.elements;
                    break;
                case ClassTypeInfo classType:
                    argument.classId = classType.classId;
                    argument.typeArguments = classType.typeArguments;
                    break;
                case InterfaceTypeInfo interfaceType:
                    argument.interfaceId = interfaceType.interfaceId;
                    argument.typeArguments = interfaceType.typeArguments;
                    break;
                case EnumTypeInfo enumType:
                    argument.enumId = enumType.enumId;
                    break;
                case CollectionTypeInfo collection:
                    argument.entryTypeInfo = collection.entryTypeInfo;
                    argument.keyEnumId = collection.keyEnumId;
                    argument.listMemberId = collection.listMemberId;
                    break;
                case LookupTypeInfo lookup:
                    argument.entryTypeInfo = lookup.entryTypeInfo;
                    argument.collectionMemberId = lookup.collectionMemberId;
                    argument.collectionValueId = lookup.collectionValueId;
                    break;
                case ReferenceTypeInfo reference:
                    argument.targetTypeInfo = reference.targetTypeInfo;
                    break;
                case DelegateTypeInfo delegateType:
                    argument.returnTypeInfo = delegateType.returnTypeInfo;
                    argument.argumentTypes = delegateType.argumentTypes;
                    break;
                case ActionTypeInfo actionType:
                    argument.argumentTypes = actionType.argumentTypes;
                    break;
                case GenericTypeInfo classGeneric:
                    argument.ownerClassId = classGeneric.ownerClassId;
                    argument.genericParamId = classGeneric.genericParamId;
                    break;
            }
            return argument;
        }

        private static TypeInfo[] BindList(TypeInfo[] list, TypeInfo[] table)
        {
            TypeInfo[]? bound = null;
            for (int i = 0; i < list.Length; i++)
            {
                TypeInfo entry = Bind(list[i], table);
                if (ReferenceEquals(entry, list[i]))
                    continue;
                bound ??= (TypeInfo[])list.Clone();
                bound[i] = entry;
            }
            return bound ?? list;
        }

        /// <summary>The bound map, or null when nothing changed.</summary>
        private static Dictionary<string, TypeInfo>? BindMap(Dictionary<string, TypeInfo> map, TypeInfo[] table)
        {
            Dictionary<string, TypeInfo>? bound = null;
            foreach (KeyValuePair<string, TypeInfo> pair in map)
            {
                TypeInfo entry = Bind(pair.Value, table);
                if (ReferenceEquals(entry, pair.Value))
                    continue;
                bound ??= new Dictionary<string, TypeInfo>(map);
                bound[pair.Key] = entry;
            }
            return bound;
        }

        private static TypeInfo Lookup(TypeInfo[] table, int index)
        {
            if ((uint)index >= (uint)table.Length)
            {
                throw new NSGetterRuntimeError(
                    $"Type parameter {index} has no type argument; the call supplied {table.Length}.");
            }
            return table[index];
        }

        /// <summary>A nullable use of a type parameter is nullable whatever it binds to.</summary>
        private static TypeInfo Required(TypeInfo bound, bool required)
        {
            if (NeoGeneratedTypesSupport.ClosedSlotRequired(bound, declaredNullable: !required) == bound.required)
                return bound;
            TypeInfo nullable = bound.ShallowClone();
            nullable.required = false;
            return nullable;
        }

        /// <summary>
        /// The table a callee's frame reads: <paramref name="written"/>
        /// resolved in the caller's frame, then the callee's defaults for any
        /// position it omits. Null for a non-generic callee.
        /// </summary>
        /// <param name="writtenForwards">Whether <paramref name="written"/> names the caller's type parameters.</param>
        internal static TypeInfo[]? ForCall(
            TypeInfo[]? written,
            bool writtenForwards,
            FunctionGenericParam[]? parameters,
            string calleeName,
            TypeInfo[]? callerTable)
        {
            if ((written?.Length ?? 0) > (parameters?.Length ?? 0))
                throw new NSGetterRuntimeError($"Call to '{calleeName}' supplies too many type arguments.");
            if (parameters is null)
                return null;
            if (written is not null
                && written.Length == parameters.Length
                && !writtenForwards)
            {
                return written;
            }
            var table = new TypeInfo[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (written is not null && i < written.Length)
                {
                    table[i] = callerTable is null ? written[i] : Bind(written[i], callerTable);
                    continue;
                }
                table[i] = parameters[i].defaultType
                    ?? throw new NSGetterRuntimeError(
                        $"Call to '{calleeName}' supplies no type argument for '{parameters[i].name}', and it has no default.");
            }
            return table;
        }

        /// <summary>Whether a call's type arguments name its caller's own type parameters.</summary>
        internal static bool Forwards(TypeInfo[] written)
        {
            foreach (TypeInfo argument in written)
            {
                if (Contains(argument))
                    return true;
            }
            return false;
        }

        /// <summary>The defaults of every parameter, or null when one has none.</summary>
        internal static TypeInfo[]? Defaults(FunctionGenericParam[]? parameters)
        {
            if (parameters is null)
                return null;
            var table = new TypeInfo[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].defaultType is not { } defaultType)
                    return null;
                table[i] = defaultType;
            }
            return table;
        }
    }
}
