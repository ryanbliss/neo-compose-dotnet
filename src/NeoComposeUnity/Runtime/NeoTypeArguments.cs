// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// P110 §7.2. Maps a C# type argument to the NeoScript type a generic
    /// function binds. It only inspects <see cref="Type"/>, so it is AOT-safe.
    /// </summary>
    public sealed class NeoTypeArguments
    {
        private static readonly Dictionary<Type, MemberKind> PrimitiveKinds = new()
        {
            [typeof(bool)] = MemberKind.Bool,
            [typeof(int)] = MemberKind.Int,
            [typeof(float)] = MemberKind.Float,
            [typeof(decimal)] = MemberKind.Decimal,
            [typeof(string)] = MemberKind.String,
            [typeof(Sprite)] = MemberKind.Sprite,
            [typeof(AudioClip)] = MemberKind.Audio,
            [typeof(Vector2)] = MemberKind.Vector2,
            [typeof(Vector2Int)] = MemberKind.Vector2Int,
            [typeof(Vector3)] = MemberKind.Vector3,
            [typeof(Vector3Int)] = MemberKind.Vector3Int,
            [typeof(Color)] = MemberKind.Color,
        };

        private readonly Dictionary<Type, TypeInfo> cache = new();
        private readonly Dictionary<Type, TypeInfo[]> tables = new();
        private IReadOnlyDictionary<Type, string> classIds = new Dictionary<Type, string>();
        private IReadOnlyDictionary<Type, string> enumIds = new Dictionary<Type, string>();
        private IReadOnlyDictionary<Type, string> interfaceIds = new Dictionary<Type, string>();
        private readonly NeoClient client;

        internal NeoTypeArguments(NeoClient client)
        {
            this.client = client;
        }

        /// <summary>
        /// Registers the generated project's class, enum and interface tables.
        /// A generic interface is keyed by its generic definition.
        /// </summary>
        public void Register(
            IReadOnlyDictionary<Type, string> classIds,
            IReadOnlyDictionary<Type, string> enumIds,
            IReadOnlyDictionary<Type, string>? interfaceIds = null)
        {
            this.classIds = classIds;
            this.enumIds = enumIds;
            this.interfaceIds = interfaceIds ?? new Dictionary<Type, string>();
            cache.Clear();
            tables.Clear();
        }

        /// <summary><typeparamref name="T"/>'s NeoScript type, cached per type.</summary>
        public TypeInfo Of<T>() => Of(typeof(T));

        /// <summary>
        /// A generic call's type-argument table, one entry per element of
        /// <typeparamref name="TTuple"/>, as in <c>Table&lt;(T, U)&gt;()</c>.
        /// Cached per tuple type, so a call allocates nothing and the
        /// runtime binds each table once.
        /// </summary>
        public TypeInfo[] Table<TTuple>()
            where TTuple : struct
        {
            Type tuple = typeof(TTuple);
            if (tables.TryGetValue(tuple, out TypeInfo[]? table))
                return table;
            var arguments = new List<TypeInfo>();
            AddTupleElements(tuple, arguments);
            table = arguments.ToArray();
            tables[tuple] = table;
            return table;
        }

        private void AddTupleElements(Type tuple, List<TypeInfo> arguments)
        {
            Type[] elements = tuple.GetGenericArguments();
            for (int i = 0; i < elements.Length; i++)
            {
                // A tuple of eight or more nests the rest in its eighth element.
                if (i == 7)
                    AddTupleElements(elements[i], arguments);
                else
                    arguments.Add(Of(elements[i]));
            }
        }

        private TypeInfo Of(Type type)
        {
            if (cache.TryGetValue(type, out TypeInfo? cached))
                return cached;
            TypeInfo typeInfo = Map(type);
            cache[type] = typeInfo;
            return typeInfo;
        }

        private TypeInfo Map(Type type)
        {
            if (classIds.TryGetValue(type, out string? classId))
                return new ClassTypeInfo { type = MemberKind.Class, classId = classId, required = true };
            if (enumIds.TryGetValue(type, out string? enumId))
                return new EnumTypeInfo { type = MemberKind.Enum, enumId = enumId, required = true };
            if (interfaceIds.TryGetValue(type, out string? interfaceId))
                return new InterfaceTypeInfo { type = MemberKind.Interface, interfaceId = interfaceId, required = true };
            if (PrimitiveKinds.TryGetValue(type, out MemberKind kind))
                return new PrimitiveTypeInfo { type = kind, required = true };
            if (Nullable.GetUnderlyingType(type) is { } underlying)
            {
                TypeInfo nullable = Of(underlying).ShallowClone();
                nullable.required = false;
                return nullable;
            }
            if (type.IsGenericType)
            {
                Type definition = type.GetGenericTypeDefinition();
                Type[] arguments = type.GetGenericArguments();
                if (typeof(System.Runtime.CompilerServices.ITuple).IsAssignableFrom(type) && type.IsValueType)
                {
                    var types = new List<TypeInfo>();
                    AddTupleElements(type, types);
                    if (types.Count < 2)
                        throw new NeoTypeArgumentException("A NeoScript tuple requires at least two elements.");
                    var elements = new TupleElementTypeInfo[types.Count];
                    for (int i = 0; i < elements.Length; i++)
                        elements[i] = new TupleElementTypeInfo { typeInfo = types[i] };
                    return new TupleTypeInfo { type = MemberKind.Tuple, required = true, elements = elements };
                }
                if (interfaceIds.TryGetValue(definition, out string? genericInterfaceId))
                    return ConstructedInterface(type, genericInterfaceId, arguments);
                if (definition == typeof(IReadOnlyList<>))
                {
                    return new CollectionTypeInfo
                    {
                        type = MemberKind.List,
                        entryTypeInfo = Of(arguments[0]),
                        required = true,
                    };
                }
                if (definition == typeof(IReadOnlyDictionary<,>) && arguments[0] == typeof(string))
                {
                    return new CollectionTypeInfo
                    {
                        type = MemberKind.Dictionary,
                        entryTypeInfo = Of(arguments[1]),
                        required = true,
                    };
                }
            }
            throw new NeoTypeArgumentException(
                $"Type argument '{type.FullName}' has no NeoScript type.");
        }

        /// <summary>P113 §6. A constructed interface maps its C# arguments in its params' order.</summary>
        private TypeInfo ConstructedInterface(Type type, string interfaceId, Type[] arguments)
        {
            if (!client.TryGetInterface(interfaceId, out Interface? declaration))
            {
                throw new NeoTypeArgumentException(
                    $"Type argument '{type.FullName}' names interface '{interfaceId}', which the project doesn't declare.");
            }
            List<GenericParamDeclaration>? genericParams = declaration.genericParams;
            if (genericParams is null || genericParams.Count != arguments.Length)
            {
                throw new NeoTypeArgumentException(
                    $"Type argument '{type.FullName}' has {arguments.Length} arguments, and {declaration.name} takes {genericParams?.Count ?? 0}.");
            }
            var typeArguments = new Dictionary<string, TypeInfo>(arguments.Length);
            for (int i = 0; i < arguments.Length; i++)
                typeArguments[genericParams[i].id] = Of(arguments[i]);
            return new InterfaceTypeInfo
            {
                type = MemberKind.Interface,
                interfaceId = interfaceId,
                typeArguments = typeArguments,
                required = true,
            };
        }
    }
}
