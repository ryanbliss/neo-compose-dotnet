// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using NeoCompose.Runtime.Json;

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
                ListAttribute or EnumAttribute or LookupAttribute => new ArrayAttributeValue
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
                NSGetterAttribute => new NullAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                },
                _ => throw new System.ArgumentException(
                    $"Unknown attribute type {attribute.GetType().Name}",
                    nameof(attribute)),
            };
            created.typeId = typeId;
            return created;
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
    }
}
