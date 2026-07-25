// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Builds the right concrete <see cref="MemberValue"/> subclass
    /// for a given member and a (typeless) payload. Used by Saved
    /// variants when materializing a fresh value row — it converts the
    /// untyped <c>TPayload</c> into a strongly-typed
    /// <c>*MemberValue</c> matching the parent member's kind, or
    /// throws a focused error when the payload doesn't match the
    /// expected shape.
    ///
    /// <para>Centralising this dispatch means new member kinds need
    /// to be added in just one switch (mirroring
    /// <see cref="NeoMember.Create"/>) rather than every Saved
    /// variant's <c>Set</c> method having to know how to class-check
    /// every other kind.</para>
    /// </summary>
    internal static class MemberValueFactory
    {
        public static MemberValue Create<TPayload>(
            Member member,
            TPayload? payload,
            string id,
            string createdAt,
            string updatedAt)
        {
            object? rawPayload = payload;
            string? classId = null;
            if (rawPayload is NeoValuePayload wrapped)
            {
                rawPayload = wrapped.value;
                classId = wrapped.classId;
            }

            MemberValue created = member switch
            {
                NullMember => new NullMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                },
                BoolMember => new BoolMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<bool?>(rawPayload, member),
                },
                IntMember or FloatMember => new NumberMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<double?>(rawPayload, member),
                },
                StringMember => new StringMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<string?>(rawPayload, member),
                    neoLocalizationMode = NeoStringLocalizationMode.Literal,
                },
                DictionaryMember or ClassMember or FunctionRefMember => new ObjectMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<Dictionary<string, string>?>(rawPayload, member),
                },
                ListMember or EnumMember or LookupMember or DialogueLookupMember => new ArrayMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<string[]?>(rawPayload, member),
                },
                SpriteMember => new SpriteMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<SpriteValue?>(rawPayload, member),
                },
                AudioMember => new FileMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<FileValue?>(rawPayload, member),
                },
                Vector2Member => new Vector2MemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Vector2Payload(rawPayload, member),
                },
                Vector2IntMember => new Vector2MemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Vector2IntPayload(rawPayload, member),
                },
                Vector3Member => new Vector3MemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Vector3Payload(rawPayload, member),
                },
                Vector3IntMember => new Vector3MemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Vector3IntPayload(rawPayload, member),
                },
                ColorMember => new ColorMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = ColorPayload(rawPayload, member),
                },
                DecimalMember => new StringMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = DecimalPayload(rawPayload, member),
                },
                NSPropertyMember => new NullMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                },
                GenericMember generic => throw new System.InvalidOperationException(
                    $"Cannot create a value row for Generic member '{generic.id}' ({generic.name}, param '{generic.genericParamId}') — Generic slots must be substituted to their binding member before value creation (is the enclosing collection row missing its genericBindings stamp?)."),
                _ => throw new System.ArgumentException(
                    $"Unknown member type {member.GetType().Name}",
                    nameof(member)),
            };
            created.classId = classId;
            return created;
        }

        public static MemberValue? CreateFromDefault(
            Member schemaMember,
            string id,
            NeoTimestamp createdAt,
            NeoTimestamp updatedAt)
        {
            return schemaMember switch
            {
                NullMember member => member.defaultValue is null ? null : new NullMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = member.defaultValue.value,
                    classId = member.defaultValue.classId,
                },
                BoolMember member => member.defaultValue is null ? null : new BoolMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = member.defaultValue.value,
                    classId = member.defaultValue.classId,
                },
                IntMember member => member.defaultValue is null ? null : new NumberMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = member.defaultValue.value,
                    classId = member.defaultValue.classId,
                },
                FloatMember member => member.defaultValue is null ? null : new NumberMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = member.defaultValue.value,
                    classId = member.defaultValue.classId,
                },
                StringMember member => member.defaultValue is null ? null : new StringMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = member.defaultValue.value,
                    neoLocalizationMode = (member.defaultValue as StringMemberValueBase)?.neoLocalizationMode,
                    classId = member.defaultValue.classId,
                },
                DictionaryMember member => member.defaultValue is null ? null : new ObjectMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneDictionary(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                ClassMember member => member.defaultValue is null ? null : new ObjectMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneDictionary(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                ListMember member => member.defaultValue is null ? null : new ArrayMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneArray(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                EnumMember member => member.defaultValue is null ? null : new ArrayMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneArray(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                LookupMember member => member.defaultValue is null ? null : new ArrayMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneArray(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                DialogueLookupMember member => member.defaultValue is null ? null : new ArrayMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneArray(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                NSPropertyMember member => member.defaultValue is null ? null : new NullMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = member.defaultValue.value,
                    classId = member.defaultValue.classId,
                },
                FunctionMember member => member.defaultValue is null ? null : new NullMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = member.defaultValue.value,
                    classId = member.defaultValue.classId,
                },
                NSFunctionMember member => member.defaultValue is null ? null : new NullMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = member.defaultValue.value,
                    classId = member.defaultValue.classId,
                },
                FunctionRefMember member => member.defaultValue is null ? null : new ObjectMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneDictionary(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                SpriteMember member => member.defaultValue is null ? null : new SpriteMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneSpriteValue(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                AudioMember member => member.defaultValue is null ? null : new FileMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneFileValue(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                Vector2Member member => member.defaultValue is null ? null : new Vector2MemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneVector2Value(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                Vector2IntMember member => member.defaultValue is null ? null : new Vector2MemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneVector2Value(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                Vector3Member member => member.defaultValue is null ? null : new Vector3MemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneVector3Value(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                Vector3IntMember member => member.defaultValue is null ? null : new Vector3MemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneVector3Value(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                ColorMember member => member.defaultValue is null ? null : new ColorMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneColorValue(member.defaultValue.value),
                    classId = member.defaultValue.classId,
                },
                DecimalMember member => member.defaultValue is null ? null : new StringMemberValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = member.defaultValue.value,
                    classId = member.defaultValue.classId,
                },
                _ => null,
            };
        }

        private static TExpected Cast<TExpected>(object? payload, Member member)
        {
            // Allow null when TExpected admits it (Nullable<T> for value
            // classes, or any reference type).
            if (payload is null) return default!;
            if (payload is TExpected match) return match;
            if (typeof(TExpected) == typeof(double?))
            {
                if (payload is int i) return (TExpected)(object)(double?)i;
                if (payload is float f) return (TExpected)(object)(double?)f;
                if (payload is double d) return (TExpected)(object)(double?)d;
            }
            // Evaluated NeoScript values box string arrays (enum selections,
            // lookup ref lists) as object[]; unbox when every element fits.
            if (typeof(TExpected) == typeof(string[])
                && payload is object?[] boxed
                && System.Array.TrueForAll(boxed, element => element is string))
            {
                var strings = new string[boxed.Length];
                for (var index = 0; index < boxed.Length; index++)
                {
                    strings[index] = (string)boxed[index]!;
                }
                return (TExpected)(object)strings;
            }
            throw new System.ArgumentException(
                $"Cannot set {member.GetType().Name} {member.id} from " +
                $"{payload.GetType().Name}; expected {typeof(TExpected).Name}",
                nameof(payload));
        }

        private static string[]? CloneArray(string[]? source)
        {
            if (source is null) return null;
            var clone = new string[source.Length];
            System.Array.Copy(source, clone, source.Length);
            return clone;
        }

        private static Dictionary<string, string>? CloneDictionary(
            Dictionary<string, string>? source)
        {
            return source is null
                ? null
                : new Dictionary<string, string>(source);
        }

        private static FileValue? CloneFileValue(FileValue? source)
        {
            return source is null
                ? null
                : new FileValue { fileId = source.fileId };
        }

        private static SpriteValue? CloneSpriteValue(SpriteValue? source)
        {
            return source is null
                ? null
                : new SpriteValue
                {
                    fileId = source.fileId,
                    sliceIndex = source.sliceIndex,
                };
        }

        private static NeoVector2Value? CloneVector2Value(NeoVector2Value? source)
        {
            return source is null
                ? null
                : new NeoVector2Value { x = source.x, y = source.y };
        }

        private static NeoVector3Value? CloneVector3Value(NeoVector3Value? source)
        {
            return source is null
                ? null
                : new NeoVector3Value { x = source.x, y = source.y, z = source.z };
        }

        private static NeoColorValue? CloneColorValue(NeoColorValue? source)
        {
            return source is null
                ? null
                : new NeoColorValue
                {
                    r = source.r,
                    g = source.g,
                    b = source.b,
                    a = source.a,
                };
        }

        private static string? DecimalPayload(object? payload, Member member)
        {
            if (payload is null) return null;
            if (payload is string canonical) return canonical;
            if (payload is decimal value) return NeoDecimalValues.Format(value);
            throw PayloadError(payload, member, "Decimal");
        }

        private static NeoColorValue? ColorPayload(object? payload, Member member)
        {
            if (payload is null) return null;
            if (payload is NeoColorValue raw) return raw;
            if (payload is Color color) return NeoColorValues.FromColor(color);
            if (payload is NeoReadOnlyColor wrapper) return NeoColorValues.FromColor(wrapper.Value);
            throw PayloadError(payload, member, "Color");
        }

        private static NeoVector2Value? Vector2Payload(object? payload, Member member)
        {
            if (payload is null) return null;
            if (payload is NeoVector2Value raw) return raw;
            if (payload is Vector2 vector) return NeoVectorValues.FromVector2(vector);
            if (payload is NeoReadOnlyVector2 wrapper) return NeoVectorValues.FromVector2(wrapper.Value);
            throw PayloadError(payload, member, "Vector2");
        }

        private static NeoVector2Value? Vector2IntPayload(object? payload, Member member)
        {
            if (payload is null) return null;
            if (payload is NeoVector2Value raw)
            {
                _ = NeoVectorValues.ToVector2Int(raw);
                return raw;
            }
            if (payload is Vector2Int vector) return NeoVectorValues.FromVector2Int(vector);
            if (payload is NeoReadOnlyVector2Int wrapper) return NeoVectorValues.FromVector2Int(wrapper.Value);
            throw PayloadError(payload, member, "Vector2Int");
        }

        private static NeoVector3Value? Vector3Payload(object? payload, Member member)
        {
            if (payload is null) return null;
            if (payload is NeoVector3Value raw) return raw;
            if (payload is Vector3 vector) return NeoVectorValues.FromVector3(vector);
            if (payload is NeoReadOnlyVector3 wrapper) return NeoVectorValues.FromVector3(wrapper.Value);
            throw PayloadError(payload, member, "Vector3");
        }

        private static NeoVector3Value? Vector3IntPayload(object? payload, Member member)
        {
            if (payload is null) return null;
            if (payload is NeoVector3Value raw)
            {
                _ = NeoVectorValues.ToVector3Int(raw);
                return raw;
            }
            if (payload is Vector3Int vector) return NeoVectorValues.FromVector3Int(vector);
            if (payload is NeoReadOnlyVector3Int wrapper) return NeoVectorValues.FromVector3Int(wrapper.Value);
            throw PayloadError(payload, member, "Vector3Int");
        }

        private static System.ArgumentException PayloadError(
            object payload,
            Member member,
            string expected)
        {
            return new System.ArgumentException(
                $"Cannot set {member.GetType().Name} {member.id} from " +
                $"{payload.GetType().Name}; expected {expected}",
                nameof(payload));
        }
    }
}
