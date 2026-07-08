// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Builds the right concrete <see cref="AttributeValue"/> subclass
    /// for a given attribute and a (typeless) payload. Used by Saved
    /// variants when materializing a fresh value row — it converts the
    /// untyped <c>TPayload</c> into a strongly-typed
    /// <c>*AttributeValue</c> matching the parent attribute's kind, or
    /// throws a focused error when the payload doesn't match the
    /// expected shape.
    ///
    /// <para>Centralising this dispatch means new attribute kinds need
    /// to be added in just one switch (mirroring
    /// <see cref="NeoAttribute.Create"/>) rather than every Saved
    /// variant's <c>Set</c> method having to know how to type-check
    /// every other kind.</para>
    /// </summary>
    internal static class AttributeValueFactory
    {
        public static AttributeValue Create<TPayload>(
            Attribute attribute,
            TPayload? payload,
            string id,
            string createdAt,
            string updatedAt)
        {
            object? rawPayload = payload;
            string? typeId = null;
            if (rawPayload is NeoValuePayload wrapped)
            {
                rawPayload = wrapped.value;
                typeId = wrapped.typeId;
            }

            AttributeValue created = attribute switch
            {
                NullAttribute => new NullAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                },
                BoolAttribute => new BoolAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<bool?>(rawPayload, attribute),
                },
                IntAttribute or FloatAttribute => new NumberAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<double?>(rawPayload, attribute),
                },
                StringAttribute => new StringAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<string?>(rawPayload, attribute),
                    neoLocalizationMode = NeoStringLocalizationMode.Literal,
                },
                DictionaryAttribute or CustomAttribute => new ObjectAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<Dictionary<string, string>?>(rawPayload, attribute),
                },
                ListAttribute or EnumAttribute or LookupAttribute or DialogueLookupAttribute => new ArrayAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<string[]?>(rawPayload, attribute),
                },
                SpriteAttribute => new SpriteAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<SpriteValue?>(rawPayload, attribute),
                },
                AudioAttribute => new FileAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<FileValue?>(rawPayload, attribute),
                },
                Vector2Attribute => new Vector2AttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Vector2Payload(rawPayload, attribute),
                },
                Vector2IntAttribute => new Vector2AttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Vector2IntPayload(rawPayload, attribute),
                },
                Vector3Attribute => new Vector3AttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Vector3Payload(rawPayload, attribute),
                },
                Vector3IntAttribute => new Vector3AttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Vector3IntPayload(rawPayload, attribute),
                },
                ColorAttribute => new ColorAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = ColorPayload(rawPayload, attribute),
                },
                DecimalAttribute => new StringAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = DecimalPayload(rawPayload, attribute),
                },
                NSGetterAttribute => new NullAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                },
                GenericAttribute generic => throw new System.InvalidOperationException(
                    $"Cannot create a value row for Generic attribute '{generic.id}' ({generic.name}, param '{generic.genericParamId}') — Generic slots must be substituted to their binding attribute before value creation (is the enclosing collection row missing its genericBindings stamp?)."),
                _ => throw new System.ArgumentException(
                    $"Unknown attribute type {attribute.GetType().Name}",
                    nameof(attribute)),
            };
            created.typeId = typeId;
            return created;
        }

        public static AttributeValue? CreateFromDefault(
            Attribute attribute,
            string id,
            NeoTimestamp createdAt,
            NeoTimestamp updatedAt)
        {
            return attribute switch
            {
                NullAttribute attr => attr.defaultValue is null ? null : new NullAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = attr.defaultValue.value,
                    typeId = attr.defaultValue.typeId,
                },
                BoolAttribute attr => attr.defaultValue is null ? null : new BoolAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = attr.defaultValue.value,
                    typeId = attr.defaultValue.typeId,
                },
                IntAttribute attr => attr.defaultValue is null ? null : new NumberAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = attr.defaultValue.value,
                    typeId = attr.defaultValue.typeId,
                },
                FloatAttribute attr => attr.defaultValue is null ? null : new NumberAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = attr.defaultValue.value,
                    typeId = attr.defaultValue.typeId,
                },
                StringAttribute attr => attr.defaultValue is null ? null : new StringAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = attr.defaultValue.value,
                    neoLocalizationMode = (attr.defaultValue as StringAttributeValueBase)?.neoLocalizationMode,
                    typeId = attr.defaultValue.typeId,
                },
                DictionaryAttribute attr => attr.defaultValue is null ? null : new ObjectAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneDictionary(attr.defaultValue.value),
                    typeId = attr.defaultValue.typeId,
                },
                CustomAttribute attr => attr.defaultValue is null ? null : new ObjectAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneDictionary(attr.defaultValue.value),
                    typeId = attr.defaultValue.typeId,
                },
                ListAttribute attr => attr.defaultValue is null ? null : new ArrayAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneArray(attr.defaultValue.value),
                    typeId = attr.defaultValue.typeId,
                },
                EnumAttribute attr => attr.defaultValue is null ? null : new ArrayAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneArray(attr.defaultValue.value),
                    typeId = attr.defaultValue.typeId,
                },
                LookupAttribute attr => attr.defaultValue is null ? null : new ArrayAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneArray(attr.defaultValue.value),
                    typeId = attr.defaultValue.typeId,
                },
                DialogueLookupAttribute attr => attr.defaultValue is null ? null : new ArrayAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneArray(attr.defaultValue.value),
                    typeId = attr.defaultValue.typeId,
                },
                NSGetterAttribute attr => attr.defaultValue is null ? null : new NullAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = attr.defaultValue.value,
                    typeId = attr.defaultValue.typeId,
                },
                FunctionAttribute attr => attr.defaultValue is null ? null : new NullAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = attr.defaultValue.value,
                    typeId = attr.defaultValue.typeId,
                },
                SpriteAttribute attr => attr.defaultValue is null ? null : new SpriteAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneSpriteValue(attr.defaultValue.value),
                    typeId = attr.defaultValue.typeId,
                },
                AudioAttribute attr => attr.defaultValue is null ? null : new FileAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneFileValue(attr.defaultValue.value),
                    typeId = attr.defaultValue.typeId,
                },
                Vector2Attribute attr => attr.defaultValue is null ? null : new Vector2AttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneVector2Value(attr.defaultValue.value),
                    typeId = attr.defaultValue.typeId,
                },
                Vector2IntAttribute attr => attr.defaultValue is null ? null : new Vector2AttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneVector2Value(attr.defaultValue.value),
                    typeId = attr.defaultValue.typeId,
                },
                Vector3Attribute attr => attr.defaultValue is null ? null : new Vector3AttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneVector3Value(attr.defaultValue.value),
                    typeId = attr.defaultValue.typeId,
                },
                Vector3IntAttribute attr => attr.defaultValue is null ? null : new Vector3AttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneVector3Value(attr.defaultValue.value),
                    typeId = attr.defaultValue.typeId,
                },
                ColorAttribute attr => attr.defaultValue is null ? null : new ColorAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = CloneColorValue(attr.defaultValue.value),
                    typeId = attr.defaultValue.typeId,
                },
                DecimalAttribute attr => attr.defaultValue is null ? null : new StringAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = attr.defaultValue.value,
                    typeId = attr.defaultValue.typeId,
                },
                _ => null,
            };
        }

        private static TExpected Cast<TExpected>(object? payload, Attribute attribute)
        {
            // Allow null when TExpected admits it (Nullable<T> for value
            // types, or any reference type).
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
                $"Cannot set {attribute.GetType().Name} {attribute.id} from " +
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

        private static string? DecimalPayload(object? payload, Attribute attribute)
        {
            if (payload is null) return null;
            if (payload is string canonical) return canonical;
            if (payload is decimal value) return NeoDecimalValues.Format(value);
            throw PayloadError(payload, attribute, "Decimal");
        }

        private static NeoColorValue? ColorPayload(object? payload, Attribute attribute)
        {
            if (payload is null) return null;
            if (payload is NeoColorValue raw) return raw;
            if (payload is Color color) return NeoColorValues.FromColor(color);
            if (payload is NeoReadOnlyColor wrapper) return NeoColorValues.FromColor(wrapper.Value);
            throw PayloadError(payload, attribute, "Color");
        }

        private static NeoVector2Value? Vector2Payload(object? payload, Attribute attribute)
        {
            if (payload is null) return null;
            if (payload is NeoVector2Value raw) return raw;
            if (payload is Vector2 vector) return NeoVectorValues.FromVector2(vector);
            if (payload is NeoReadOnlyVector2 wrapper) return NeoVectorValues.FromVector2(wrapper.Value);
            throw PayloadError(payload, attribute, "Vector2");
        }

        private static NeoVector2Value? Vector2IntPayload(object? payload, Attribute attribute)
        {
            if (payload is null) return null;
            if (payload is NeoVector2Value raw)
            {
                _ = NeoVectorValues.ToVector2Int(raw);
                return raw;
            }
            if (payload is Vector2Int vector) return NeoVectorValues.FromVector2Int(vector);
            if (payload is NeoReadOnlyVector2Int wrapper) return NeoVectorValues.FromVector2Int(wrapper.Value);
            throw PayloadError(payload, attribute, "Vector2Int");
        }

        private static NeoVector3Value? Vector3Payload(object? payload, Attribute attribute)
        {
            if (payload is null) return null;
            if (payload is NeoVector3Value raw) return raw;
            if (payload is Vector3 vector) return NeoVectorValues.FromVector3(vector);
            if (payload is NeoReadOnlyVector3 wrapper) return NeoVectorValues.FromVector3(wrapper.Value);
            throw PayloadError(payload, attribute, "Vector3");
        }

        private static NeoVector3Value? Vector3IntPayload(object? payload, Attribute attribute)
        {
            if (payload is null) return null;
            if (payload is NeoVector3Value raw)
            {
                _ = NeoVectorValues.ToVector3Int(raw);
                return raw;
            }
            if (payload is Vector3Int vector) return NeoVectorValues.FromVector3Int(vector);
            if (payload is NeoReadOnlyVector3Int wrapper) return NeoVectorValues.FromVector3Int(wrapper.Value);
            throw PayloadError(payload, attribute, "Vector3Int");
        }

        private static System.ArgumentException PayloadError(
            object payload,
            Attribute attribute,
            string expected)
        {
            return new System.ArgumentException(
                $"Cannot set {attribute.GetType().Name} {attribute.id} from " +
                $"{payload.GetType().Name}; expected {expected}",
                nameof(payload));
        }
    }
}
