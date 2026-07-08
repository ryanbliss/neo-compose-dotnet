// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Well-known values for <see cref="GenericParamConstraint.kind"/>.
    /// Mirrors the TS-side <c>TGenericParamConstraint</c> tagged union
    /// (specs/custom-type-generics.md §1.1).
    /// </summary>
    public static class NeoGenericConstraintKinds
    {
        public const string CustomType = "customType";
        public const string Enum = "enum";
    }

    /// <summary>
    /// Constraint on a generic parameter. Absent (a null field on the
    /// declaration) means any eligible attribute type. Exactly one of
    /// <see cref="typeId"/> / <see cref="enumId"/> is set, matching
    /// <see cref="kind"/> — enforced by
    /// <see cref="GenericParamConstraintConverter"/> on read.
    /// </summary>
    [JsonConverter(typeof(GenericParamConstraintConverter))]
    public class GenericParamConstraint
    {
        public string kind = null!;

        /// <summary>Set iff <see cref="kind"/> is "customType".</summary>
        public string? typeId;

        /// <summary>Set iff <see cref="kind"/> is "enum".</summary>
        public string? enumId;
    }

    /// <summary>
    /// One generic parameter declared by a custom type. <see cref="id"/> is
    /// stable (never regenerated); <see cref="name"/> is the C#-identifier
    /// display name and a cross-repo codegen contract — renames are safe,
    /// ids are load-bearing (specs/custom-type-generics.md Decision 2/3).
    /// </summary>
    public class GenericParamDeclaration
    {
        public string id = null!;
        public string name = null!;
        public GenericParamConstraint? constraint;
    }

    /// <summary>
    /// Well-known values for <see cref="GenericBinding.kind"/>. Mirrors the
    /// TS-side <c>TGenericBinding</c> tagged union.
    /// </summary>
    public static class NeoGenericBindingKinds
    {
        /// <summary>Forwards one of the declaring context's own params.</summary>
        public const string Generic = "generic";

        /// <summary>Supplies a concrete attribute record as the type argument.</summary>
        public const string Attribute = "attribute";
    }

    /// <summary>
    /// How a generic param is implemented — at the extends boundary
    /// (<see cref="CustomType.extendsGenericBindings"/>) or at a usage site
    /// (<see cref="CustomAttribute.customTypeArguments"/>). Exactly one of
    /// <see cref="genericParamId"/> / <see cref="attributeId"/> is set,
    /// matching <see cref="kind"/> — enforced by
    /// <see cref="GenericBindingConverter"/> on read.
    /// </summary>
    [JsonConverter(typeof(GenericBindingConverter))]
    public class GenericBinding
    {
        public string kind = null!;

        /// <summary>Set iff <see cref="kind"/> is "generic".</summary>
        public string? genericParamId;

        /// <summary>Set iff <see cref="kind"/> is "attribute".</summary>
        public string? attributeId;

        /// <summary>True when this binding forwards an in-scope param.</summary>
        public bool IsForward => kind == NeoGenericBindingKinds.Generic;
    }

    /// <summary>
    /// Strict read converter for <see cref="GenericParamConstraint"/> —
    /// rejects unknown kinds and a missing discriminated field, each with a
    /// distinct message. Read-only; default serialization handles writes.
    /// </summary>
    public class GenericParamConstraintConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(GenericParamConstraint);
        }

        public override bool CanWrite => false;

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var obj = JObject.Load(reader);
            var kind = obj.Value<string>("kind");
            if (kind is null)
            {
                throw new JsonSerializationException(
                    "Generic param constraint is missing 'kind'.");
            }
            if (kind == NeoGenericConstraintKinds.CustomType)
            {
                var typeId = obj.Value<string>("typeId");
                if (string.IsNullOrEmpty(typeId))
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'customType' is missing 'typeId'.");
                }
                return new GenericParamConstraint { kind = kind, typeId = typeId };
            }
            if (kind == NeoGenericConstraintKinds.Enum)
            {
                var enumId = obj.Value<string>("enumId");
                if (string.IsNullOrEmpty(enumId))
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'enum' is missing 'enumId'.");
                }
                return new GenericParamConstraint { kind = kind, enumId = enumId };
            }
            throw new JsonSerializationException(
                $"Unknown generic param constraint kind '{kind}'.");
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotImplementedException(
                "GenericParamConstraintConverter is read-only; default serialization handles writes.");
        }
    }

    /// <summary>
    /// Strict read converter for <see cref="GenericBinding"/> — rejects
    /// unknown kinds and a missing discriminated field, each with a distinct
    /// message. Read-only; default serialization handles writes.
    /// </summary>
    public class GenericBindingConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(GenericBinding);
        }

        public override bool CanWrite => false;

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var obj = JObject.Load(reader);
            var kind = obj.Value<string>("kind");
            if (kind is null)
            {
                throw new JsonSerializationException(
                    "Generic binding is missing 'kind'.");
            }
            if (kind == NeoGenericBindingKinds.Generic)
            {
                var genericParamId = obj.Value<string>("genericParamId");
                if (string.IsNullOrEmpty(genericParamId))
                {
                    throw new JsonSerializationException(
                        "Generic binding of kind 'generic' is missing 'genericParamId'.");
                }
                return new GenericBinding { kind = kind, genericParamId = genericParamId };
            }
            if (kind == NeoGenericBindingKinds.Attribute)
            {
                var attributeId = obj.Value<string>("attributeId");
                if (string.IsNullOrEmpty(attributeId))
                {
                    throw new JsonSerializationException(
                        "Generic binding of kind 'attribute' is missing 'attributeId'.");
                }
                return new GenericBinding { kind = kind, attributeId = attributeId };
            }
            throw new JsonSerializationException(
                $"Unknown generic binding kind '{kind}'.");
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotImplementedException(
                "GenericBindingConverter is read-only; default serialization handles writes.");
        }
    }
}
