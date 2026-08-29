// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
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
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NeoGenericParamConstraintKind kind;

        /// <summary>Set iff <see cref="kind"/> is Class.</summary>
        public string? classId;

        /// <summary>Set iff <see cref="kind"/> is Enum.</summary>
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
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NeoGenericBindingKind kind;

        /// <summary>Set iff <see cref="kind"/> is Generic.</summary>
        public string? genericParamId;

        /// <summary>Set iff <see cref="kind"/> is Member.</summary>
        public string? memberId;

        /// <summary>True when this binding forwards an in-scope param.</summary>
        public bool IsForward => kind == NeoGenericBindingKind.Generic;
    }

    /// <summary>
    /// Strict numeric-discriminator reader for
    /// <see cref="GenericParamConstraint"/>. An absent kind uses the zero
    /// ordinal, Class. Read-only; default serialization handles writes.
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
            P80RecordShapeGuard.ValidateGenericParamConstraint(obj);
            var kind = StrictRecordShapeEnums.ReadDefaulted(
                obj,
                "kind",
                "Generic param constraint",
                NeoGenericParamConstraintKind.Class);
            if (kind == NeoGenericParamConstraintKind.Class)
            {
                var classId = obj.Value<string>("classId");
                if (string.IsNullOrEmpty(classId))
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'Class' is missing 'classId'.");
                }
                return new GenericParamConstraint { kind = kind, classId = classId };
            }
            if (kind == NeoGenericParamConstraintKind.Enum)
            {
                var enumId = obj.Value<string>("enumId");
                if (string.IsNullOrEmpty(enumId))
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'Enum' is missing 'enumId'.");
                }
                return new GenericParamConstraint { kind = kind, enumId = enumId };
            }
            throw new JsonSerializationException(
                $"Unknown generic param constraint kind '{(int)kind}'.");
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
    /// Strict numeric-discriminator reader for <see cref="GenericBinding"/>.
    /// An absent kind uses the zero ordinal, Generic. Read-only; default
    /// serialization handles writes.
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
            P80RecordShapeGuard.ValidateGenericBinding(obj);
            var kind = StrictRecordShapeEnums.ReadDefaulted(
                obj,
                "kind",
                "Generic binding",
                NeoGenericBindingKind.Generic);
            if (kind == NeoGenericBindingKind.Generic)
            {
                var genericParamId = obj.Value<string>("genericParamId");
                if (string.IsNullOrEmpty(genericParamId))
                {
                    throw new JsonSerializationException(
                        "Generic binding of kind 'Generic' is missing 'genericParamId'.");
                }
                return new GenericBinding { kind = kind, genericParamId = genericParamId };
            }
            if (kind == NeoGenericBindingKind.Member)
            {
                var memberId = obj.Value<string>("memberId");
                if (string.IsNullOrEmpty(memberId))
                {
                    throw new JsonSerializationException(
                        "Generic binding of kind 'Member' is missing 'memberId'.");
                }
                return new GenericBinding { kind = kind, memberId = memberId };
            }
            throw new JsonSerializationException(
                $"Unknown generic binding kind '{(int)kind}'.");
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
