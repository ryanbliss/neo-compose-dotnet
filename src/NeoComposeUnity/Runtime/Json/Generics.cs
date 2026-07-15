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
    /// (specs/class-generics.md §1.1).
    /// </summary>
    public static class NeoGenericConstraintKinds
    {
        public const string Class = "class";
        public const string Enum = "enum";
    }

    /// <summary>
    /// Constraint on a generic parameter. Absent (a null field on the
    /// declaration) means any eligible member type. Exactly one of
    /// <see cref="classId"/> / <see cref="enumId"/> is set, matching
    /// <see cref="kind"/> — enforced by
    /// <see cref="GenericParamConstraintConverter"/> on read.
    /// </summary>
    [JsonConverter(typeof(GenericParamConstraintConverter))]
    public class GenericParamConstraint
    {
        public string kind = null!;

        /// <summary>Set iff <see cref="kind"/> is "class".</summary>
        public string? classId;

        /// <summary>Set iff <see cref="kind"/> is "enum".</summary>
        public string? enumId;
    }

    /// <summary>
    /// One generic parameter declared by a class. <see cref="id"/> is
    /// stable (never regenerated); <see cref="name"/> is the C#-identifier
    /// display name and a cross-repo codegen contract — renames are safe,
    /// ids are load-bearing (specs/class-generics.md Decision 2/3).
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

        /// <summary>Supplies a concrete member record as the type argument.</summary>
        public const string Member = "member";
    }

    /// <summary>
    /// How a generic param is implemented — at the extends boundary
    /// (<see cref="NeoSchemaClass.extendsGenericBindings"/>) or at a usage site
    /// (<see cref="ClassMember.classArguments"/>). Exactly one of
    /// <see cref="genericParamId"/> / <see cref="memberId"/> is set,
    /// matching <see cref="kind"/> — enforced by
    /// <see cref="GenericBindingConverter"/> on read.
    /// </summary>
    [JsonConverter(typeof(GenericBindingConverter))]
    public class GenericBinding
    {
        public string kind = null!;

        /// <summary>Set iff <see cref="kind"/> is "generic".</summary>
        public string? genericParamId;

        /// <summary>Set iff <see cref="kind"/> is "member".</summary>
        public string? memberId;

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
            Schema8LegacyFieldGuard.RejectRemovedReferenceFieldsShallow(
                obj,
                "Generic param constraint");
            Schema8LegacyFieldGuard.RejectRemovedTypeInfoTypeId(obj);
            var kind = obj.Value<string>("kind");
            if (kind is null)
            {
                throw new JsonSerializationException(
                    "Generic param constraint is missing 'kind'.");
            }
            if (kind == NeoGenericConstraintKinds.Class)
            {
                var classId = obj.Value<string>("classId");
                if (string.IsNullOrEmpty(classId))
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'class' is missing 'classId'.");
                }
                return new GenericParamConstraint { kind = kind, classId = classId };
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
            Schema8LegacyFieldGuard.RejectRemovedReferenceFieldsShallow(
                obj,
                "Generic binding");
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
            if (kind == NeoGenericBindingKinds.Member)
            {
                var memberId = obj.Value<string>("memberId");
                if (string.IsNullOrEmpty(memberId))
                {
                    throw new JsonSerializationException(
                        "Generic binding of kind 'member' is missing 'memberId'.");
                }
                return new GenericBinding { kind = kind, memberId = memberId };
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
