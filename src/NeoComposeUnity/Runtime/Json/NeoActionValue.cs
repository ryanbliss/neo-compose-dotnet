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
    /// Persisted NSAction value (P62 §2.1) — an insertion-ordered listener
    /// set, wrapped in an object so shape sniffing stays unambiguous:
    /// <c>{ listeners: [{ memberId, valueId }, …] }</c>. Every entry is the
    /// member-target arm of <see cref="NeoDelegateValue"/>; a closure is not
    /// a listener (§2), and no two entries share a
    /// <c>(memberId, valueId)</c> identity (§3.3). The empty set — not
    /// <c>null</c> — is an action's rest state, so invoking one with no
    /// listeners is a successful no-op.
    /// </summary>
    [JsonConverter(typeof(NeoActionValueConverter))]
    public sealed class NeoActionValue
    {
        public List<NeoDelegateValue> listeners = new List<NeoDelegateValue>();

        /// <summary>
        /// Listener identity per §3.3 — the member target's
        /// <c>(memberId, valueId)</c> pair, with a null valueId (the
        /// declaration default) distinct from any instance.
        /// </summary>
        public static string ListenerIdentity(NeoDelegateValue listener)
        {
            if (listener is null)
            {
                throw new ArgumentNullException(nameof(listener));
            }
            if (string.IsNullOrEmpty(listener.memberId))
            {
                throw new ArgumentException(
                    "An NSAction listener must be a member target with a non-empty 'memberId'.",
                    nameof(listener));
            }
            return $"{listener.memberId}\u0000{listener.valueId ?? string.Empty}";
        }

        /// <summary>
        /// Deep copy for value positions that must not alias — notably a
        /// declaration default handed to a stored row, where a runtime
        /// <c>+=</c> would otherwise mutate the shared schema object.
        /// </summary>
        internal NeoActionValue PersistedCopy()
        {
            var copy = new NeoActionValue();
            foreach (NeoDelegateValue listener in listeners)
            {
                copy.listeners.Add(listener.PersistedCopy());
            }
            return copy;
        }
    }

    /// <summary>
    /// Strict reader/writer for the NSAction persisted value. Mirrors
    /// <see cref="NeoDelegateValueConverter"/>, restricted to the
    /// member-target arm: a closure entry is invalid data, not a variant.
    /// </summary>
    public sealed class NeoActionValueConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) =>
            objectType == typeof(NeoActionValue);

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            JObject obj = JObject.Load(reader);
            if (obj.Count != 1 || obj.Property("listeners") is null)
            {
                throw new JsonSerializationException(
                    "NSAction value must contain exactly 'listeners'.");
            }
            if (obj["listeners"] is not JArray entries)
            {
                throw new JsonSerializationException(
                    "NSAction 'listeners' must be an array.");
            }

            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < entries.Count; index++)
            {
                if (!identities.Add(ValidateListener(entries[index], index)))
                {
                    throw new JsonSerializationException(
                        $"NSAction listener {index} duplicates an earlier listener identity; a listener set holds each (memberId, valueId) once.");
                }
            }

            var value = new NeoActionValue();
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
            if (value is not NeoActionValue actionValue)
            {
                writer.WriteNull();
                return;
            }
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < actionValue.listeners.Count; index++)
            {
                NeoDelegateValue listener = actionValue.listeners[index];
                if (listener is null)
                {
                    throw new JsonSerializationException(
                        $"NSAction listener {index} is null; every listener must be a member target.");
                }
                if (listener.IsClosure)
                {
                    throw new JsonSerializationException(
                        $"NSAction listener {index} is a closure; only member targets are valid listeners.");
                }
                if (!listener.IsMemberTarget)
                {
                    throw new JsonSerializationException(
                        $"NSAction listener {index} is missing 'memberId'; every listener must be a member target.");
                }
                if (!identities.Add(NeoActionValue.ListenerIdentity(listener)))
                {
                    throw new JsonSerializationException(
                        $"NSAction listener {index} duplicates an earlier listener identity; a listener set holds each (memberId, valueId) once.");
                }
            }

            writer.WriteStartObject();
            writer.WritePropertyName("listeners");
            writer.WriteStartArray();
            foreach (NeoDelegateValue listener in actionValue.listeners)
            {
                serializer.Serialize(writer, listener);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        internal static bool LooksLikeValue(JToken? token)
        {
            if (token is not JObject obj) return false;
            return obj["listeners"]?.Type == JTokenType.Array;
        }

        /// <summary>
        /// Validates one wire entry as the member-target arm and returns its
        /// listener identity, so the caller can enforce the set invariant.
        /// </summary>
        private static string ValidateListener(JToken entry, int index)
        {
            if (entry is not JObject obj)
            {
                throw new JsonSerializationException(
                    $"NSAction listener {index} must be an object.");
            }
            if (obj.Property("action") is not null)
            {
                throw new JsonSerializationException(
                    $"NSAction listener {index} carries 'action'; a closure is not a valid listener, only a member target is.");
            }
            if (obj.Property("code") is not null)
            {
                throw new JsonSerializationException(
                    $"NSAction listener {index} carries 'code'; a closure is not a valid listener, only a member target is.");
            }
            if (obj.Count != 2 || obj.Property("valueId") is null)
            {
                throw new JsonSerializationException(
                    $"NSAction listener {index} must contain exactly 'memberId' and 'valueId'.");
            }
            JToken? memberId = obj["memberId"];
            if (!IsNonEmptyString(memberId))
            {
                throw new JsonSerializationException(
                    $"NSAction listener {index} 'memberId' must be a non-empty string.");
            }
            JToken valueId = obj["valueId"]!;
            if (valueId.Type != JTokenType.Null && !IsNonEmptyString(valueId))
            {
                throw new JsonSerializationException(
                    $"NSAction listener {index} 'valueId' must be null or a non-empty string.");
            }
            string valueIdentity = valueId.Type == JTokenType.Null
                ? string.Empty
                : valueId.Value<string>() ?? string.Empty;
            return $"{memberId!.Value<string>()}\u0000{valueIdentity}";
        }

        private static bool IsNonEmptyString(JToken? token) =>
            token?.Type == JTokenType.String
            && !string.IsNullOrEmpty(token.Value<string>());
    }
}
