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
    /// Persisted NeoDelegate value. Exactly one wire variant is valid:
    /// <list type="bullet">
    /// <item><description><c>{ memberId, valueId }</c> binds a callable
    /// member to an instance (or its declaration default when valueId is
    /// null).</description></item>
    /// <item><description><c>{ code?, action, captures? }</c> is an authored
    /// closure, its server-derived compiled body, and explicit by-value
    /// captures.</description></item>
    /// <item><description><c>{ kind: BuiltIn, declaring, name, arity, parameterTypes, ... }</c>
    /// names a built-in static method, which has no member row
    /// (<see cref="NeoBuiltInTarget"/>).</description></item>
    /// </list>
    /// P127 §12.1 adds optional identity fields, each a closed
    /// <see cref="TypeBinding"/> except <c>site</c> and <c>placement</c>: a
    /// member target may carry <c>typeArguments</c>,
    /// <c>staticConstruction</c>, <c>interfaceConstruction</c> and
    /// <c>delegateType</c>; a closure may carry <c>typeArguments</c>,
    /// <c>declaringConstruction</c>, <c>delegateType</c>, <c>site</c> and
    /// <c>placement</c>.
    /// </summary>
    [JsonConverter(typeof(NeoDelegateValueConverter))]
    public sealed class NeoDelegateValue
    {
        public string? memberId;
        public string? valueId;
        public string? code;
        public FunctionWithReturnType? action;
        public object?[]? captures;

        /// <summary>P127 §5.2 step 6 / §12.1: a generic function frame's or target's closed type arguments, by position.</summary>
        [JsonIgnore]
        public TypeBinding[]? typeArguments;

        /// <summary>P127 §12.1: the closed NeoDelegate type, when it differs from its position's declared type.</summary>
        [JsonIgnore]
        public TypeBinding? delegateType;

        /// <summary>P127 §5.2 step 6: the closed class or interface construction declaring a closure's body.</summary>
        [JsonIgnore]
        public TypeBinding? declaringConstruction;

        /// <summary>P127 §9.7: <c>{owner}:{body}:{index}</c>, the lambda's source site.</summary>
        [JsonIgnore]
        public string? site;

        /// <summary>P127 §9.7: <c>"this"</c> or <c>"environment"</c>; absent when the lambda has no target.</summary>
        [JsonIgnore]
        public string? placement;

        /// <summary>P127 §12.1: the closed class (or, for a default body, interface) a static member target is bound through.</summary>
        [JsonIgnore]
        public TypeBinding? staticConstruction;

        /// <summary>P127 §12.1: the closed interface construction an interface member target is bound through.</summary>
        [JsonIgnore]
        public TypeBinding? interfaceConstruction;

        /// <summary>P127 §12.1: the built-in static method this target names in place of a <see cref="memberId"/>.</summary>
        [JsonIgnore]
        public NeoBuiltInTarget? builtIn;

        [JsonIgnore]
        internal object? lexicalThis;

        [JsonIgnore]
        internal object? lexicalRoot;

        [JsonIgnore]
        internal bool hasLexicalEnvironment;

        /// <summary>The type arguments of the generic function frame that made this closure (P110 §5).</summary>
        [JsonIgnore]
        internal TypeInfo[]? lexicalTypeArguments;

        /// <summary>
        /// Each capture's closed static type where it can hold a float or a
        /// decimal, so <see cref="captures"/> write in their stored forms
        /// (P127 §12.1). Null when no capture can.
        /// </summary>
        [JsonIgnore]
        internal TypeInfo?[]? captureTypes;

        /// <summary>
        /// A loaded closure's captures decoded by their closed types, filled
        /// on first call. <see cref="captures"/> keeps the stored forms: a
        /// worker may be serializing this value, and rows are never edited
        /// in place.
        /// </summary>
        [JsonIgnore]
        internal object?[]? decodedCaptures;

        /// <summary>
        /// The NSFunction or native Function <see cref="memberId"/> last
        /// resolved to. Each is stamped with its schema resolution, so a
        /// schema change or another client misses.
        /// </summary>
        [JsonIgnore]
        internal object? resolvedTarget;

        /// <summary>
        /// The evaluator's per-Class answer to whether this listener binds to
        /// the row that owns its action, stamped with its schema resolution.
        /// </summary>
        [JsonIgnore]
        internal object? ownerTargets;

        [JsonIgnore]
        public bool IsMemberTarget => !string.IsNullOrEmpty(memberId);

        [JsonIgnore]
        public bool IsClosure => action is not null;

        [JsonIgnore]
        public bool IsBuiltInTarget => builtIn is not null;

        /// <summary>Whether exactly one of the three arms is set.</summary>
        [JsonIgnore]
        internal bool HasOneArm => (IsMemberTarget ? 1 : 0) + (IsClosure ? 1 : 0) + (IsBuiltInTarget ? 1 : 0) == 1;

        internal NeoDelegateValue Capture(object? thisValue, object? rootValue, TypeInfo[]? typeArguments)
        {
            lexicalThis = thisValue;
            lexicalRoot = rootValue;
            lexicalTypeArguments = typeArguments;
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
            captureTypes = captureTypes,
            typeArguments = typeArguments is null ? null : (TypeBinding[])typeArguments.Clone(),
            delegateType = delegateType,
            declaringConstruction = declaringConstruction,
            site = site,
            placement = placement,
            staticConstruction = staticConstruction,
            interfaceConstruction = interfaceConstruction,
            builtIn = builtIn,
        };
    }

    /// <summary>
    /// P127 §12.1's structural checks for the identity fields of stored
    /// closures and member targets, twin of <c>stored-delegate-fields.ts</c>.
    /// Owner existence and project-aware key checks belong to the server.
    /// </summary>
    internal static class NeoStoredDelegateFields
    {
        internal static readonly string[] ClosureKeys =
        {
            "typeArguments",
            "declaringConstruction",
            "delegateType",
            "site",
            "placement",
        };

        internal static readonly string[] MemberTargetKeys =
        {
            "typeArguments",
            "staticConstruction",
            "interfaceConstruction",
            "delegateType",
        };

        private static readonly HashSet<string> SiteBodies = new HashSet<string>(StringComparer.Ordinal)
        {
            "getter",
            "setter",
            "function",
            "property",
            "migration",
            "initializer",
            "initializerExpression",
            "constructor",
            "dialogue",
        };

        /// <summary>How many of <paramref name="keys"/> <paramref name="record"/> carries.</summary>
        internal static int CountPresent(JObject record, string[] keys)
        {
            int count = 0;
            foreach (string key in keys)
            {
                if (record.Property(key) is not null)
                    count++;
            }
            return count;
        }

        /// <summary>Reads a stored closure's identity fields into <paramref name="value"/>, refusing a malformed one.</summary>
        internal static void ReadClosure(JObject record, NeoDelegateValue value, string subject)
        {
            value.typeArguments = ReadTypeArguments(record, subject);
            value.declaringConstruction = ReadConstruction(record, "declaringConstruction", subject, MemberKind.Class, MemberKind.Interface);
            if (value.declaringConstruction is not null && record.Property("site") is null)
                throw new JsonSerializationException($"{subject} declaringConstruction requires a site.");
            value.delegateType = ReadConstruction(record, "delegateType", subject, MemberKind.NSDelegate, MemberKind.NSDelegate);
            if (record.Property("site") is not null)
            {
                string? violation = SiteViolation(record["site"]);
                if (violation is not null)
                    throw new JsonSerializationException($"{subject} {violation}.");
                value.site = record["site"]!.Value<string>();
            }
            if (record.Property("placement") is not null)
            {
                JToken? placement = record["placement"];
                string? text = placement?.Type == JTokenType.String ? placement.Value<string>() : null;
                if (text != "this" && text != "environment")
                    throw new JsonSerializationException($"{subject} placement must be \"this\" or \"environment\".");
                value.placement = text;
            }
        }

        /// <summary>Reads a stored member target's construction fields into <paramref name="value"/>, refusing a malformed one.</summary>
        internal static void ReadMemberTarget(JObject record, NeoDelegateValue value, string subject)
        {
            value.typeArguments = ReadTypeArguments(record, subject);
            value.staticConstruction = ReadConstruction(record, "staticConstruction", subject, MemberKind.Class, MemberKind.Interface);
            value.interfaceConstruction = ReadConstruction(record, "interfaceConstruction", subject, MemberKind.Interface, MemberKind.Interface);
            value.delegateType = ReadConstruction(record, "delegateType", subject, MemberKind.NSDelegate, MemberKind.NSDelegate);
        }

        /// <summary>Writes a member target's construction fields into the open target object.</summary>
        internal static void WriteMemberTarget(JsonWriter writer, NeoDelegateValue value)
        {
            WriteTypeArguments(writer, value.typeArguments);
            WriteBinding(writer, "staticConstruction", value.staticConstruction);
            WriteBinding(writer, "interfaceConstruction", value.interfaceConstruction);
            WriteBinding(writer, "delegateType", value.delegateType);
        }

        internal static void WriteTypeArguments(JsonWriter writer, TypeBinding[]? typeArguments)
        {
            if (typeArguments is null)
                return;
            writer.WritePropertyName("typeArguments");
            writer.WriteStartArray();
            foreach (TypeBinding argument in typeArguments)
                TypeBindings.Write(writer, argument);
            writer.WriteEndArray();
        }

        internal static void WriteBinding(JsonWriter writer, string name, TypeBinding? binding)
        {
            if (binding is null)
                return;
            writer.WritePropertyName(name);
            TypeBindings.Write(writer, binding);
        }

        /// <summary>Why <paramref name="site"/> isn't a well-formed closure site, or null (P127 §9.7).</summary>
        internal static string? SiteViolation(JToken? site)
        {
            if (site?.Type != JTokenType.String)
                return "site must be a string";
            string text = site.Value<string>()!;
            string[] parts = text.Split(':');
            if (parts.Length != 3)
                return $"site \"{text}\" must have three ':'-separated parts";
            string owner = parts[0];
            string body = parts[1];
            string index = parts[2];
            if (!SiteBodies.Contains(body))
                return $"site \"{text}\" names unknown body \"{body}\"";
            if (!IsCanonicalIndex(index))
                return $"site \"{text}\" index must be a non-negative integer";
            if (body != "dialogue")
            {
                if (owner.Length == 0)
                    return $"site \"{text}\" has an empty owner";
                if (owner.IndexOf('/') >= 0)
                    return $"site \"{text}\" owner of a {body} body must be one id";
                return null;
            }
            string[] ownerParts = owner.Split('/');
            if (ownerParts.Length != 3)
                return $"site \"{text}\" dialogue owner must have three '/'-separated parts";
            foreach (string part in ownerParts)
            {
                if (part.Length == 0)
                    return $"site \"{text}\" dialogue owner has an empty part";
            }
            return null;
        }

        private static bool IsCanonicalIndex(string text)
        {
            if (text.Length == 0 || text.Length > 1 && text[0] == '0')
                return false;
            foreach (char character in text)
            {
                if (character < '0' || character > '9')
                    return false;
            }
            return true;
        }

        private static TypeBinding[]? ReadTypeArguments(JObject record, string subject)
        {
            if (record.Property("typeArguments") is null)
                return null;
            if (record["typeArguments"] is not JArray tokens)
                throw new JsonSerializationException($"{subject} typeArguments must be an array.");
            if (tokens.Count == 0)
                throw new JsonSerializationException($"{subject} typeArguments must not be empty.");
            var arguments = new TypeBinding[tokens.Count];
            for (int index = 0; index < arguments.Length; index++)
                arguments[index] = NeoBuiltInTarget.ReadIdentity(tokens[index], $"{subject} typeArguments[{index}]");
            return arguments;
        }

        /// <summary>Reads an optional closed binding whose head is <paramref name="head"/> or <paramref name="other"/>.</summary>
        private static TypeBinding? ReadConstruction(JObject record, string field, string subject, MemberKind head, MemberKind other)
        {
            if (record.Property(field) is null)
                return null;
            TypeBinding binding = NeoBuiltInTarget.ReadIdentity(record[field], $"{subject} {field}");
            MemberKind kind = binding.Head!.Type;
            if (kind != head && kind != other)
            {
                string expected = head == other ? $"a closed {head}" : $"a closed {head} or {other}";
                throw new JsonSerializationException($"{subject} {field} names a {kind}; it must be {expected}.");
            }
            return binding;
        }
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
            if (reader.TokenType == JsonToken.Null)
                return null;
            JObject obj = NeoJsonObjectReader.Read(reader);
            bool target = IsNonEmptyString(obj["memberId"]);
            bool closure = obj["action"]?.Type == JTokenType.Object;
            bool builtIn = NeoBuiltInTarget.IsBuiltIn(obj);
            if (obj["action"]?["generator"] is { Type: not JTokenType.Null })
                throw new JsonSerializationException("A delegate closure cannot be a generator.");
            if ((target ? 1 : 0) + (closure ? 1 : 0) + (builtIn ? 1 : 0) != 1)
            {
                throw new JsonSerializationException(
                    "NeoDelegate value must be exactly one of a bound member target, a compiled closure or a built-in target.");
            }
            if (builtIn)
                return new NeoDelegateValue { builtIn = NeoBuiltInTarget.Read(obj, "NeoDelegate", false) };

            if (target)
            {
                int extra = NeoStoredDelegateFields.CountPresent(obj, NeoStoredDelegateFields.MemberTargetKeys);
                if (obj.Count != 2 + extra || obj.Property("valueId") is null)
                {
                    throw new JsonSerializationException(
                        "NeoDelegate member target must contain exactly 'memberId' and 'valueId', and optional 'typeArguments'/'staticConstruction'/'interfaceConstruction'/'delegateType'.");
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
                int extra = NeoStoredDelegateFields.CountPresent(obj, NeoStoredDelegateFields.ClosureKeys);
                if (obj.Count != 1 + (hasCode ? 1 : 0) + (hasCaptures ? 1 : 0) + extra)
                {
                    throw new JsonSerializationException(
                        "NeoDelegate closure must contain exactly 'action' and optional 'code'/'captures'/'typeArguments'/'declaringConstruction'/'delegateType'/'site'/'placement'.");
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
            if (target)
                NeoStoredDelegateFields.ReadMemberTarget(obj, value, "NeoDelegate member target");
            else
                NeoStoredDelegateFields.ReadClosure(obj, value, "NeoDelegate closure");
            using (JsonReader subReader = obj.CreateReader())
            {
                serializer.Populate(subReader, value);
            }
            // Captures read as their runtime values, so a captured list is the
            // array its closure made rather than a JArray.
            if (obj["captures"] is JArray captures)
                value.captures = (object?[])NeoJsonRuntimeValue.From(captures)!;
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
            if (!delegateValue.HasOneArm)
            {
                throw new JsonSerializationException(
                    "NeoDelegate value must be exactly one of a bound member target, a compiled closure or a built-in target.");
            }
            writer.WriteStartObject();
            if (delegateValue.builtIn is not null)
            {
                delegateValue.builtIn.WriteFields(writer);
            }
            else if (delegateValue.IsMemberTarget)
            {
                writer.WritePropertyName("memberId");
                writer.WriteValue(delegateValue.memberId);
                writer.WritePropertyName("valueId");
                // A declaration-default target is JSON null. Writing the null
                // string instead records a String token holding null when the
                // destination is a JToken, and the reader this pairs with
                // accepts only a real null — so the value would no longer read
                // back as the one it was written from.
                if (delegateValue.valueId is null)
                    writer.WriteNull();
                else
                    writer.WriteValue(delegateValue.valueId);
                NeoStoredDelegateFields.WriteMemberTarget(writer, delegateValue);
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
                    WriteCaptures(writer, delegateValue, serializer);
                }
                NeoStoredDelegateFields.WriteTypeArguments(writer, delegateValue.typeArguments);
                NeoStoredDelegateFields.WriteBinding(writer, "declaringConstruction", delegateValue.declaringConstruction);
                NeoStoredDelegateFields.WriteBinding(writer, "delegateType", delegateValue.delegateType);
                WriteOptional(writer, "site", delegateValue.site);
                WriteOptional(writer, "placement", delegateValue.placement);
            }
            writer.WriteEndObject();
        }

        private static void WriteCaptures(JsonWriter writer, NeoDelegateValue delegateValue, JsonSerializer serializer)
        {
            if (delegateValue.captureTypes is not { } captureTypes)
            {
                serializer.Serialize(writer, delegateValue.captures);
                return;
            }
            writer.WriteStartArray();
            for (int index = 0; index < delegateValue.captures!.Length; index++)
                NeoStoredNumberJsonConverter.WriteTyped(writer, delegateValue.captures[index], captureTypes[index], serializer);
            writer.WriteEndArray();
        }

        private static void WriteOptional(JsonWriter writer, string name, string? value)
        {
            if (value is null)
                return;
            writer.WritePropertyName(name);
            writer.WriteValue(value);
        }

        internal static bool LooksLikeValue(JToken? token)
        {
            if (token is not JObject obj)
                return false;
            return IsNonEmptyString(obj["memberId"])
                || obj["action"]?.Type == JTokenType.Object
                || (NeoBuiltInTarget.IsBuiltIn(obj) && obj.Property("declaring") is not null);
        }

        private static bool IsNonEmptyString(JToken? token) =>
            token?.Type == JTokenType.String
            && !string.IsNullOrEmpty(token.Value<string>());
    }
}
