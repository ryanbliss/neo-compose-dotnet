// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Persisted NeoDelegate value. Exactly one wire variant is valid:
    /// <list type="bullet">
    /// <item><description><c>{ memberId, valueId }</c> binds a callable
    /// member to an instance (or its declaration default when valueId is
    /// null).</description></item>
    /// <item><description><c>{ code?, action }</c> is an authored closure
    /// and its server-derived compiled body.</description></item>
    /// </list>
    /// </summary>
    [JsonConverter(typeof(NeoDelegateValueConverter))]
    public sealed class NeoDelegateValue
    {
        public string? memberId;
        public string? valueId;
        public string? code;
        public FunctionWithReturnType? action;
        public object?[]? captures;

        [JsonIgnore]
        internal object? lexicalThis;

        [JsonIgnore]
        internal object? lexicalRoot;

        [JsonIgnore]
        internal bool hasLexicalEnvironment;

        [JsonIgnore]
        public bool IsMemberTarget => !string.IsNullOrEmpty(memberId);

        [JsonIgnore]
        public bool IsClosure => action is not null;

        internal NeoDelegateValue Capture(object? thisValue, object? rootValue)
        {
            lexicalThis = thisValue;
            lexicalRoot = rootValue;
            hasLexicalEnvironment = true;
            return this;
        }

        internal NeoDelegateValue PersistedCopy() => new()
        {
            memberId = memberId,
            valueId = valueId,
            code = code,
            action = action,
            captures = captures is null ? null : (object?[])captures.Clone(),
        };
    }

    /// <summary>Strict reader for the NeoDelegate persisted-value union.</summary>
    public sealed class NeoDelegateValueConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            objectType == typeof(NeoDelegateValue);

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            JObject obj = JObject.Load(reader);
            bool target = IsNonEmptyString(obj["memberId"]);
            bool closure = obj["action"]?.Type == JTokenType.Object;
            if (target == closure)
            {
                throw new JsonSerializationException(
                    "NeoDelegate value must be exactly one of a bound member target or a compiled closure.");
            }

            if (target)
            {
                if (obj.Count != 2 || obj.Property("valueId") is null)
                {
                    throw new JsonSerializationException(
                        "NeoDelegate member target must contain exactly 'memberId' and 'valueId'.");
                }
                JToken valueId = obj["valueId"]!;
                if (valueId.Type != JTokenType.Null && !IsNonEmptyString(valueId))
                {
                    throw new JsonSerializationException(
                        "NeoDelegate member target 'valueId' must be null or a non-empty string.");
                }
            }
            else
            {
                bool hasCode = obj.Property("code") is not null;
                bool hasCaptures = obj.Property("captures") is not null;
                if (obj.Count != 1 + (hasCode ? 1 : 0) + (hasCaptures ? 1 : 0))
                {
                    throw new JsonSerializationException(
                        "NeoDelegate closure must contain exactly 'action' and optional 'code'/'captures'.");
                }
                if (hasCode && !IsNonEmptyString(obj["code"]))
                {
                    throw new JsonSerializationException(
                        "NeoDelegate closure 'code' must be a non-empty string when present.");
                }
                if (hasCaptures && obj["captures"]?.Type != JTokenType.Array)
                {
                    throw new JsonSerializationException(
                        "NeoDelegate closure 'captures' must be an array when present.");
                }
            }

            var value = new NeoDelegateValue();
            using (JsonReader subReader = obj.CreateReader())
            {
                serializer.Populate(subReader, value);
            }
            return value;
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            if (value is not NeoDelegateValue delegateValue)
            {
                writer.WriteNull();
                return;
            }
            if (delegateValue.IsMemberTarget == delegateValue.IsClosure)
            {
                throw new JsonSerializationException(
                    "NeoDelegate value must be exactly one of a bound member target or a compiled closure.");
            }
            writer.WriteStartObject();
            if (delegateValue.IsMemberTarget)
            {
                writer.WritePropertyName("memberId");
                writer.WriteValue(delegateValue.memberId);
                writer.WritePropertyName("valueId");
                writer.WriteValue(delegateValue.valueId);
            }
            else
            {
                if (!string.IsNullOrEmpty(delegateValue.code))
                {
                    writer.WritePropertyName("code");
                    writer.WriteValue(delegateValue.code);
                }
                writer.WritePropertyName("action");
                serializer.Serialize(writer, delegateValue.action);
                if (delegateValue.captures is not null)
                {
                    writer.WritePropertyName("captures");
                    serializer.Serialize(writer, delegateValue.captures);
                }
            }
            writer.WriteEndObject();
        }

        internal static bool LooksLikeValue(JToken? token)
        {
            if (token is not JObject obj) return false;
            return IsNonEmptyString(obj["memberId"])
                || obj["action"]?.Type == JTokenType.Object;
        }

        private static bool IsNonEmptyString(JToken? token) =>
            token?.Type == JTokenType.String
            && !string.IsNullOrEmpty(token.Value<string>());
    }
}
