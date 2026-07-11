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
    /// One property or Function declaration in a custom-type interface.
    /// The <see cref="kind"/> discriminator determines which signature
    /// fields are present.
    /// </summary>
    [JsonConverter(typeof(InterfaceMemberConverter))]
    public class InterfaceMember
    {
        public string kind = null!;

        // Property signature.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public TypeInfo? typeInfo;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? settable;

        // Function signature.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        [JsonConverter(typeof(FunctionReturnTypeInfoConverter))]
        public TypeInfo? returnTypeInfo;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<FunctionArgumentTypeInfo>? argumentTypes;
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? deferred;
    }

    /// <summary>
    /// Custom-type interface declaration. Mirrors the TS-side
    /// <c>INeoInterface</c> export record.
    /// </summary>
    public class Interface
    {
        public string id = null!;
        public string projectId = null!;
        public string name = null!;
        public Dictionary<string, InterfaceMember> members = null!;
        public List<string>? memberKeyOrder;
        public List<string>? extendsInterfaceIds;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
    }

    public class InterfaceMemberConverter : JsonConverter
    {
        public override bool CanWrite => false;

        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(InterfaceMember);
        }

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var json = JObject.Load(reader);
            var kind = json.Value<string>("kind") ?? throw new JsonSerializationException(
                "Interface member is missing 'kind'.");
            switch (kind)
            {
                case "property":
                    ValidateProperty(json);
                    break;
                case "function":
                    ValidateFunction(json);
                    break;
                default:
                    throw new JsonSerializationException(
                        $"Unknown interface member kind '{kind}'.");
            }

            var member = new InterfaceMember();
            using (var subReader = json.CreateReader())
            {
                serializer.Populate(subReader, member);
            }
            ValidateSupportedTypeInfos(member);
            return member;
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }

        private static void ValidateProperty(JObject json)
        {
            RequireNonNull(json, "typeInfo", "Property interface member");
            RequireBoolean(json, "settable", "Property interface member");
            RejectPresent(json, "returnTypeInfo", "Property interface member");
            RejectPresent(json, "argumentTypes", "Property interface member");
            RejectPresent(json, "deferred", "Property interface member");
        }

        private static void ValidateFunction(JObject json)
        {
            RequireNonNull(json, "returnTypeInfo", "Function interface member");
            if (json["argumentTypes"]?.Type != JTokenType.Array)
            {
                throw new JsonSerializationException(
                    "Function interface member requires an 'argumentTypes' array.");
            }
            RequireBoolean(json, "deferred", "Function interface member");
            RejectPresent(json, "typeInfo", "Function interface member");
            RejectPresent(json, "settable", "Function interface member");
        }

        private static void RequireNonNull(
            JObject json,
            string propertyName,
            string subject)
        {
            if (json[propertyName] is null || json[propertyName]!.Type == JTokenType.Null)
            {
                throw new JsonSerializationException(
                    $"{subject} requires '{propertyName}'.");
            }
        }

        private static void RequireBoolean(
            JObject json,
            string propertyName,
            string subject)
        {
            if (json[propertyName]?.Type != JTokenType.Boolean)
            {
                throw new JsonSerializationException(
                    $"{subject} requires boolean '{propertyName}'.");
            }
        }

        private static void RejectPresent(
            JObject json,
            string propertyName,
            string subject)
        {
            if (json[propertyName] is not null)
            {
                throw new JsonSerializationException(
                    $"{subject} cannot contain '{propertyName}'.");
            }
        }

        private static void ValidateSupportedTypeInfos(InterfaceMember member)
        {
            if (member.kind == "property")
            {
                RejectUnsupportedTypeInfo(member.typeInfo!, "Property interface member");
                return;
            }

            RejectUnsupportedTypeInfo(member.returnTypeInfo!, "Function interface member return");
            foreach (var argument in member.argumentTypes!)
            {
                RejectUnsupportedTypeInfo(argument, "Function interface member argument");
            }
        }

        private static void RejectUnsupportedTypeInfo(TypeInfo typeInfo, string subject)
        {
            if (typeInfo.type == AttributeType.Generic
                || typeInfo.type == AttributeType.Unknown)
            {
                throw new JsonSerializationException(
                    $"{subject} cannot use type '{typeInfo.type}'.");
            }
            if (typeInfo.type == AttributeType.Interface)
            {
                string? interfaceId = typeInfo switch
                {
                    InterfaceTypeInfo interfaceType => interfaceType.interfaceId,
                    FunctionArgumentTypeInfo argumentType => argumentType.interfaceId,
                    _ => null,
                };
                if (string.IsNullOrEmpty(interfaceId))
                {
                    throw new JsonSerializationException(
                        $"{subject} interface type is missing 'interfaceId'.");
                }
            }
            if (typeInfo is CollectionTypeInfo collection)
            {
                if (collection.entryTypeInfo is null)
                {
                    throw new JsonSerializationException(
                        $"{subject} collection type is missing 'entryTypeInfo'.");
                }
                RejectUnsupportedTypeInfo(collection.entryTypeInfo, subject);
            }
            if (typeInfo is FunctionArgumentTypeInfo argument
                && argument.entryTypeInfo is not null)
            {
                RejectUnsupportedTypeInfo(argument.entryTypeInfo, subject);
            }
            if (typeInfo is CustomTypeInfo custom && custom.typeArguments is not null)
            {
                foreach (TypeInfo typeArgument in custom.typeArguments.Values)
                {
                    RejectUnsupportedTypeInfo(typeArgument, subject);
                }
            }
        }
    }
}
