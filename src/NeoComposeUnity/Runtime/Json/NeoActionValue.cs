// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Persisted NSAction value (P62 §2.1) — an insertion-ordered listener
    /// set, wrapped in an object so shape sniffing stays unambiguous:
    /// <c>{ listeners: [{ memberId, valueId }, …] }</c>. Every entry is the
    /// member-target or built-in target arm of <see cref="NeoDelegateValue"/>;
    /// a closure is not a listener (§2), and no two entries share a
    /// <c>(memberId, valueId)</c> identity (§3.3). The empty set — not
    /// <c>null</c> — is an action's rest state, so invoking one with no
    /// listeners is a successful no-op.
    /// </summary>
    [JsonConverter(typeof(NeoActionValueConverter))]
    public sealed class NeoActionValue
    {
        public List<NeoDelegateValue> listeners = new List<NeoDelegateValue>();

        /// <summary>
        /// Listener identity per §3.3, built from every stored field: a plain
        /// member target's <c>(memberId, valueId)</c> pair, with a null
        /// valueId (the declaration default) distinct from any instance, and
        /// otherwise the canonical JSON of its stored form, so a target
        /// differing only in a P127 §12.1 construction or type argument, or
        /// a built-in target, is its own listener. In memory only: a key for
        /// comparing listeners, never written to a save or to JSON.
        /// </summary>
        public static string ListenerIdentity(NeoDelegateValue listener)
        {
            if (listener is null)
            {
                throw new ArgumentNullException(nameof(listener));
            }
            if (listener.builtIn is null && string.IsNullOrEmpty(listener.memberId))
            {
                throw new ArgumentException(
                    "An NSAction listener must be a member target with a non-empty 'memberId'.",
                    nameof(listener));
            }
            if (listener.builtIn is null
                && listener.typeArguments is null
                && listener.staticConstruction is null
                && listener.interfaceConstruction is null
                && listener.delegateType is null)
            {
                return $"{listener.memberId}\u0000{listener.valueId ?? string.Empty}";
            }
            return StoredIdentity(listener);
        }

        /// <summary>
        /// The canonical JSON of a structured target's stored fields, object
        /// keys in ordinal order so binding map order doesn't matter. The
        /// leading NUL keeps it apart from every plain member target's.
        /// </summary>
        private static string StoredIdentity(NeoDelegateValue listener)
        {
            var writer = new JTokenWriter();
            writer.WriteStartObject();
            if (listener.builtIn is not null)
            {
                listener.builtIn.WriteFields(writer);
            }
            else
            {
                writer.WritePropertyName("memberId");
                writer.WriteValue(listener.memberId);
                writer.WritePropertyName("valueId");
                writer.WriteValue(listener.valueId);
                NeoStoredDelegateFields.WriteMemberTarget(writer, listener);
            }
            writer.WriteEndObject();
            var builder = new StringBuilder("\u0000");
            AppendCanonical(builder, writer.Token!);
            return builder.ToString();
        }

        private static void AppendCanonical(StringBuilder builder, JToken token)
        {
            switch (token)
            {
                case JObject obj:
                    var names = new List<string>(obj.Count);
                    foreach (JProperty property in obj.Properties())
                        names.Add(property.Name);
                    names.Sort(StringComparer.Ordinal);
                    builder.Append('{');
                    foreach (string name in names)
                    {
                        builder.Append(JsonConvert.ToString(name)).Append(':');
                        AppendCanonical(builder, obj[name]!);
                        builder.Append(',');
                    }
                    builder.Append('}');
                    return;
                case JArray array:
                    builder.Append('[');
                    foreach (JToken item in array)
                    {
                        AppendCanonical(builder, item);
                        builder.Append(',');
                    }
                    builder.Append(']');
                    return;
                default:
                    builder.Append(token.ToString(Formatting.None));
                    return;
            }
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
            if (reader.TokenType == JsonToken.Null)
                return null;
            JObject obj = NeoJsonObjectReader.Read(reader);
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

            var value = new NeoActionValue();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < entries.Count; index++)
            {
                NeoDelegateValue listener = ReadListener(entries[index], index);
                if (!identities.Add(NeoActionValue.ListenerIdentity(listener)))
                {
                    throw new JsonSerializationException(
                        $"NSAction listener {index} duplicates an earlier listener identity; a listener set holds each (memberId, valueId) once.");
                }
                value.listeners.Add(listener);
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
                if (!listener.IsMemberTarget && !listener.IsBuiltInTarget)
                {
                    throw new JsonSerializationException(
                        $"NSAction listener {index} is missing 'memberId'; every listener must be a member target.");
                }
                if (listener.builtIn?.DelegateType is not null)
                {
                    throw new JsonSerializationException(
                        $"NSAction listener {index} carries 'delegateType'; a listener always has its action's delegate type.");
                }
                if (listener.delegateType is not null)
                {
                    throw new JsonSerializationException(
                        $"NSAction listener {index} carries 'delegateType'; a listener always has its action's delegate type.");
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
            if (token is not JObject obj)
                return false;
            return obj["listeners"]?.Type == JTokenType.Array;
        }

        /// <summary>Reads and validates one wire entry as the member-target or built-in target arm.</summary>
        private static NeoDelegateValue ReadListener(JToken entry, int index)
        {
            string subject = $"NSAction listener {index}";
            if (entry is not JObject obj)
            {
                throw new JsonSerializationException(
                    $"{subject} must be an object.");
            }
            if (obj.Property("action") is not null)
            {
                throw new JsonSerializationException(
                    $"{subject} carries 'action'; a closure is not a valid listener, only a member target is.");
            }
            if (obj.Property("code") is not null)
            {
                throw new JsonSerializationException(
                    $"{subject} carries 'code'; a closure is not a valid listener, only a member target is.");
            }
            if (obj.Property("delegateType") is not null)
            {
                throw new JsonSerializationException(
                    $"{subject} carries 'delegateType'; a listener always has its action's delegate type.");
            }
            if (NeoBuiltInTarget.IsBuiltIn(obj))
                return new NeoDelegateValue { builtIn = NeoBuiltInTarget.Read(obj, subject, true) };
            int extra = NeoStoredDelegateFields.CountPresent(obj, NeoStoredDelegateFields.MemberTargetKeys);
            if (obj.Count != 2 + extra || obj.Property("valueId") is null)
            {
                throw new JsonSerializationException(
                    $"{subject} must contain exactly 'memberId' and 'valueId', and optional 'typeArguments'/'staticConstruction'/'interfaceConstruction'.");
            }
            JToken? memberId = obj["memberId"];
            if (!IsNonEmptyString(memberId))
            {
                throw new JsonSerializationException(
                    $"{subject} 'memberId' must be a non-empty string.");
            }
            JToken valueId = obj["valueId"]!;
            if (valueId.Type != JTokenType.Null && !IsNonEmptyString(valueId))
            {
                throw new JsonSerializationException(
                    $"{subject} 'valueId' must be null or a non-empty string.");
            }
            var listener = new NeoDelegateValue
            {
                memberId = memberId!.Value<string>(),
                valueId = valueId.Type == JTokenType.Null ? null : valueId.Value<string>(),
            };
            NeoStoredDelegateFields.ReadMemberTarget(obj, listener, subject);
            return listener;
        }

        private static bool IsNonEmptyString(JToken? token) =>
            token?.Type == JTokenType.String
            && !string.IsNullOrEmpty(token.Value<string>());
    }
}
