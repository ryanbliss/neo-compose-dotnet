// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// One constraint on a generic parameter. The fields set match
    /// <see cref="kind"/>: <see cref="classId"/> for Class,
    /// <see cref="enumId"/> for Enum, <see cref="interfaceId"/> for
    /// Interface, and none for AnyEnum (P127 §4.7) — enforced by
    /// <see cref="GenericParamConstraintConverter"/> on read.
    /// </summary>
    [JsonConverter(typeof(GenericParamConstraintConverter))]
    public class GenericParamConstraint
    {
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public NeoGenericParamConstraintKind kind;

        /// <summary>Set iff <see cref="kind"/> is Class.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? classId;

        /// <summary>Optional constructed arguments for a Class constraint (P127 §12.1 type arguments).</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(TypeArgumentsConverter))]
        public Dictionary<string, TypeBinding>? classArguments;

        /// <summary>Set iff <see cref="kind"/> is Enum.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? enumId;

        /// <summary>Interface constraints only (P113 §2.5).</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? interfaceId;

        /// <summary>Present iff <see cref="interfaceId"/> names a generic interface.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(TypeArgumentsConverter))]
        public Dictionary<string, TypeBinding>? interfaceArguments;

        /// <summary>
        /// P127 §4.2. A nullable constraint annotation
        /// (<c>INumber&lt;TSelf&gt;?</c>) on a Class or Interface arm. Only
        /// <c>true</c> is stored.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? nullable;
    }

    /// <summary>
    /// One generic parameter declared by a class or interface.
    /// <see cref="id"/> is stable (never regenerated); <see cref="name"/> is
    /// the C#-identifier display name and a cross-repo codegen contract —
    /// renames are safe, ids are load-bearing (specs/class-generics.md
    /// Decision 2/3).
    /// </summary>
    [JsonConverter(typeof(GenericParamDeclarationConverter))]
    public class GenericParamDeclaration
    {
        public string id = null!;
        public string name = null!;

        /// <summary>P127 §4.7. Non-empty when present, in source order.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<GenericParamConstraint>? constraints;

        /// <summary>P127 §4.8. Interface params only; absent means invariant.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public NeoGenericParamVarianceKind? variance;

        /// <summary>The class constraint, which C# puts first (CS0406).</summary>
        [JsonIgnore]
        public GenericParamConstraint? ClassConstraint =>
            constraints is { Count: > 0 } && constraints[0].kind == NeoGenericParamConstraintKind.Class
                ? constraints[0]
                : null;
    }

    /// <summary>
    /// One implemented or extended interface construction (P127 §4.7). A
    /// list may hold several constructions of one generic interface.
    /// </summary>
    public class InterfaceConstruction
    {
        [JsonProperty(Required = Required.Always)]
        public string interfaceId = null!;

        /// <summary>Interface param id → type argument. Present iff the interface is generic.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(TypeArgumentsConverter))]
        public Dictionary<string, TypeBinding>? arguments;
    }

    /// <summary>
    /// P127 §5. The interface member an interface override, a reabstraction
    /// or an explicit implementation names, through one construction.
    /// </summary>
    public class ExplicitInterfaceImplementation : InterfaceConstruction
    {
        [JsonProperty(Required = Required.Always)]
        public string interfaceMemberId = null!;
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
            if (reader.TokenType == JsonToken.Null)
                return null;
            var obj = NeoJsonObjectReader.Read(reader);
            RecordShapeContractGuard.ValidateGenericParamConstraint(obj);
            var kind = StrictRecordShapeEnums.ReadDefaulted(
                obj,
                "kind",
                "Generic param constraint",
                NeoGenericParamConstraintKind.Class);
            if (kind == NeoGenericParamConstraintKind.Class)
            {
                if (obj.Property("enumId") is not null)
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'Class' cannot declare 'enumId'.");
                }
                if (obj.Property("interfaceId") is not null)
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'Class' cannot declare 'interfaceId'.");
                }
                var classId = obj.Value<string>("classId");
                if (string.IsNullOrEmpty(classId))
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'Class' is missing 'classId'.");
                }
                return new GenericParamConstraint
                {
                    kind = kind,
                    classId = classId,
                    classArguments = ReadArguments(obj, "classArguments", serializer),
                    nullable = ReadNullable(obj, "Class"),
                };
            }
            if (kind == NeoGenericParamConstraintKind.Interface)
            {
                if (obj.Property("classId") is not null)
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'Interface' cannot declare 'classId'.");
                }
                if (obj.Property("enumId") is not null)
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'Interface' cannot declare 'enumId'.");
                }
                var interfaceId = obj.Value<string>("interfaceId");
                if (string.IsNullOrEmpty(interfaceId))
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'Interface' is missing 'interfaceId'.");
                }
                return new GenericParamConstraint
                {
                    kind = kind,
                    interfaceId = interfaceId,
                    interfaceArguments = ReadArguments(obj, "interfaceArguments", serializer),
                    nullable = ReadNullable(obj, "Interface"),
                };
            }
            if (kind == NeoGenericParamConstraintKind.Enum)
            {
                if (obj.Property("classId") is not null
                    || obj.Property("classArguments") is not null)
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'Enum' cannot declare 'classId' or 'classArguments'.");
                }
                if (obj.Property("interfaceId") is not null)
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'Enum' cannot declare 'interfaceId'.");
                }
                if (obj.Property("nullable") is not null)
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'Enum' cannot declare 'nullable'.");
                }
                var enumId = obj.Value<string>("enumId");
                if (string.IsNullOrEmpty(enumId))
                {
                    throw new JsonSerializationException(
                        "Generic param constraint of kind 'Enum' is missing 'enumId'.");
                }
                return new GenericParamConstraint { kind = kind, enumId = enumId };
            }
            if (kind == NeoGenericParamConstraintKind.AnyEnum)
            {
                foreach (JProperty property in obj.Properties())
                {
                    if (property.Name != "kind")
                    {
                        throw new JsonSerializationException(
                            $"Generic param constraint of kind 'AnyEnum' cannot declare '{property.Name}'.");
                    }
                }
                return new GenericParamConstraint { kind = kind };
            }
            throw new JsonSerializationException(
                $"Unknown generic param constraint kind '{(int)kind}'.");
        }

        private static bool? ReadNullable(JObject obj, string kind)
        {
            JToken? token = obj["nullable"];
            if (token is null)
                return null;
            if (token.Type != JTokenType.Boolean || !token.Value<bool>())
            {
                throw new JsonSerializationException(
                    $"Generic param constraint of kind '{kind}' field 'nullable' must be true when present.");
            }
            return true;
        }

        private static Dictionary<string, TypeBinding>? ReadArguments(
            JObject obj,
            string field,
            JsonSerializer serializer)
        {
            if (obj.Property(field) is not JProperty argumentsProperty)
                return null;
            if (argumentsProperty.Value is not JObject arguments)
            {
                throw new JsonSerializationException(
                    $"Generic param constraint field '{field}' must be an object when present.");
            }
            return TypeBindings.ReadArguments(arguments, $"Generic param constraint field '{field}'");
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
    /// Strict reader for <see cref="GenericParamDeclaration"/>: the P127 §4.7
    /// <c>constraints</c> list replaces the single <c>constraint</c>, and the
    /// list is non-empty when present. Read-only; default serialization
    /// handles writes.
    /// </summary>
    public class GenericParamDeclarationConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(GenericParamDeclaration);
        }

        public override bool CanWrite => false;

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            var obj = NeoJsonObjectReader.Read(reader);
            RecordShapeContractGuard.ValidateGenericParamDeclaration(obj);
            string? id = obj.Value<string>("id");
            string? name = obj.Value<string>("name");
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
            {
                throw new JsonSerializationException(
                    "Generic param declaration requires a non-empty 'id' and 'name'.");
            }
            var declaration = new GenericParamDeclaration { id = id!, name = name! };
            if (obj.Property("constraints") is JProperty constraints)
            {
                if (constraints.Value is not JArray { Count: > 0 } list)
                {
                    throw new JsonSerializationException(
                        $"Generic param '{name}' field 'constraints' must be a non-empty array when present.");
                }
                declaration.constraints = new List<GenericParamConstraint>(list.Count);
                foreach (JToken entry in list)
                {
                    declaration.constraints.Add(entry.ToObject<GenericParamConstraint>(serializer)
                        ?? throw new JsonSerializationException(
                            $"Generic param '{name}' has a null constraint."));
                }
            }
            if (obj["variance"] is not null)
            {
                declaration.variance = StrictRecordShapeEnums.ReadRequired<NeoGenericParamVarianceKind>(
                    obj,
                    "variance",
                    $"Generic param '{name}'");
            }
            return declaration;
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotImplementedException(
                "GenericParamDeclarationConverter is read-only; default serialization handles writes.");
        }
    }
}
