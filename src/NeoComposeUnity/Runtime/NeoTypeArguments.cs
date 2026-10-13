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
        private IReadOnlyDictionary<Type, string> classIds = new Dictionary<Type, string>();
        private IReadOnlyDictionary<Type, string> enumIds = new Dictionary<Type, string>();

        internal NeoTypeArguments()
        {
        }

        /// <summary>Registers the generated project's class and enum tables.</summary>
        public void Register(
            IReadOnlyDictionary<Type, string> classIds,
            IReadOnlyDictionary<Type, string> enumIds)
        {
            this.classIds = classIds;
            this.enumIds = enumIds;
            cache.Clear();
        }

        /// <summary><typeparamref name="T"/>'s NeoScript type, cached per type.</summary>
        public TypeInfo Of<T>() => Of(typeof(T));

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
            if (PrimitiveKinds.TryGetValue(type, out MemberKind kind))
                return new PrimitiveTypeInfo { type = kind, required = true };
            if (type.IsGenericType)
            {
                Type definition = type.GetGenericTypeDefinition();
                Type[] arguments = type.GetGenericArguments();
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
    }
}
