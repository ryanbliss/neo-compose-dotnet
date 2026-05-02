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
            return attribute switch
            {
                NullAttribute => new NullAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                },
                BoolAttribute => new BoolAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<TPayload, bool?>(payload, attribute),
                },
                IntAttribute or FloatAttribute => new NumberAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<TPayload, double?>(payload, attribute),
                },
                StringAttribute => new StringAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<TPayload, string?>(payload, attribute),
                },
                DictionaryAttribute or CustomAttribute => new ObjectAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<TPayload, Dictionary<string, string>?>(payload, attribute),
                },
                ListAttribute or EnumAttribute or LookupAttribute => new ArrayAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                    value = Cast<TPayload, string[]?>(payload, attribute),
                },
                NSGetterAttribute => new NullAttributeValue
                {
                    id = id, createdAt = createdAt, updatedAt = updatedAt,
                },
                _ => throw new System.ArgumentException(
                    $"Unknown attribute type {attribute.GetType().Name}",
                    nameof(attribute)),
            };
        }

        private static TExpected Cast<TPayload, TExpected>(TPayload? payload, Attribute attribute)
        {
            // Allow null when TExpected admits it (Nullable<T> for value
            // types, or any reference type).
            if (payload is null) return default!;
            if (payload is TExpected match) return match;
            throw new System.ArgumentException(
                $"Cannot set {attribute.GetType().Name} {attribute.id} from " +
                $"{typeof(TPayload).Name}; expected {typeof(TExpected).Name}",
                nameof(payload));
        }
    }
}
