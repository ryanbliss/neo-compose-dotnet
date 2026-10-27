// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// P127 §12.1. Where a row that isn't a binding node reads its type, in
    /// one of two exclusive forms: <c>{ valueId, path }</c>, the binding-key
    /// path into the closed binding of its nearest owning binding node or box
    /// row, or <c>{ valueId?, type }</c>, its own type, whose <c>Open</c> arms
    /// name params of the binding node <see cref="ValueId"/> names. A member
    /// default's template rows store an open <see cref="Type"/> with no
    /// <see cref="ValueId"/>; the placement walk judges where that's allowed.
    /// Immutable.
    /// </summary>
    [JsonConverter(typeof(NeoTypeSourceConverter))]
    public sealed class NeoTypeSource : IEquatable<NeoTypeSource>
    {
        private NeoTypeSource(string? valueId, IReadOnlyList<string>? path, TypeBinding? type)
        {
            ValueId = valueId;
            Path = path;
            Type = type;
        }

        /// <summary>The <c>{ valueId, path }</c> form.</summary>
        public static NeoTypeSource AtPath(string valueId, IReadOnlyList<string> path) => new(valueId, path, null);

        /// <summary>The <c>{ valueId?, type }</c> form.</summary>
        public static NeoTypeSource OfType(TypeBinding type, string? valueId = null) => new(valueId, null, TypeBindings.WithoutNames(type));

        /// <summary>The row the type reads from; absent on a closed <see cref="Type"/> and a template row.</summary>
        public string? ValueId
        {
            get;
        }

        /// <summary>The binding-key path into <see cref="ValueId"/>'s binding; null in the type form.</summary>
        public IReadOnlyList<string>? Path
        {
            get;
        }

        /// <summary>The row's own type; null in the path form.</summary>
        public TypeBinding? Type
        {
            get;
        }

        public bool Equals(NeoTypeSource? other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other is null || ValueId != other.ValueId || !Equals(Type, other.Type))
                return false;
            if (Path is null || other.Path is null)
                return Path is null && other.Path is null;
            return Path.SequenceEqual(other.Path, StringComparer.Ordinal);
        }

        public override bool Equals(object? obj) => Equals(obj as NeoTypeSource);

        public override int GetHashCode()
        {
            int hash = HashCode.Combine(ValueId, Type);
            if (Path is not null)
                foreach (string segment in Path)
                    hash = HashCode.Combine(hash, segment);
            return hash;
        }

        /// <summary>
        /// The first shape rule <paramref name="stored"/> breaks, or null:
        /// only <c>valueId</c>, <c>path</c> and <c>type</c>; <c>path</c> and
        /// <c>type</c> exclusive; a closed <c>type</c> names no
        /// <c>valueId</c>; a <c>path</c> names one and is a non-empty list of
        /// binding keys, never an in-memory binding view. The server's
        /// <c>typeSourceViolation</c>, except that an open <c>type</c> without
        /// a <c>valueId</c> is a member default's template row (§12.1), which
        /// only its placement can judge.
        /// </summary>
        internal static string? Violation(JObject stored)
        {
            foreach (JProperty property in stored.Properties())
            {
                if (property.Name is not ("valueId" or "path" or "type"))
                    return $"typeSource has unknown field \"{property.Name}\".";
            }
            JToken? valueId = stored["valueId"];
            if (valueId is not null)
            {
                if (valueId.Type != JTokenType.String)
                    return "typeSource.valueId is not a string.";
                if (((string?)valueId)!.Length == 0)
                    return "typeSource.valueId is empty.";
            }
            JToken? type = stored["type"];
            JToken? path = stored["path"];
            if (type is not null)
            {
                if (path is not null)
                    return "typeSource stores both path and type; they are exclusive.";
                if (type is not JObject binding)
                    return "typeSource.type must be a type binding object.";
                TypeBinding read;
                try
                {
                    read = TypeBindings.Read(binding, "typeSource.type");
                }
                catch (JsonSerializationException error)
                {
                    return error.Message;
                }
                if (TypeBindings.PositionViolation(read, TypeBindingPosition.Argument) is string violation)
                    return $"typeSource.type {violation}.";
                // A row's runtime type, so its tuples carry no labels (§12.1).
                if (TypeBindings.NamesIn(read))
                    return $"typeSource.type {TypeBindings.NamesViolation}.";
                if (!NeoTypeSources.HasOpenArm(read) && valueId is not null)
                    return "typeSource.type is closed but names a valueId.";
                return null;
            }
            if (path is null)
                return "typeSource stores neither path nor type.";
            if (valueId is null)
                return "typeSource.path names no valueId.";
            if (path is not JArray segments)
                return "typeSource.path is not an array.";
            if (segments.Count == 0)
                return "typeSource.path is empty.";
            for (int index = 0; index < segments.Count; index++)
            {
                if (segments[index].Type != JTokenType.String)
                    return $"typeSource.path[{index}] is not a string.";
                string segment = (string)segments[index]!;
                if (segment.Length == 0)
                    return $"typeSource.path[{index}] is empty.";
                if (NeoTypeBindingViews.IsViewId(segment))
                    return $"typeSource.path[{index}] names in-memory binding view \"{segment}\".";
            }
            return null;
        }
    }

    /// <summary>Reads and writes <see cref="NeoTypeSource"/>, refusing any shape <see cref="NeoTypeSource.Violation"/> refuses.</summary>
    public sealed class NeoTypeSourceConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(NeoTypeSource);

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            string path = reader.Path;
            if (reader.TokenType != JsonToken.StartObject)
                throw new JsonSerializationException($"{path} typeSource is not an object.");
            JObject stored = NeoJsonObjectReader.Read(reader);
            if (NeoTypeSource.Violation(stored) is string violation)
                throw new JsonSerializationException($"{path} {violation}");
            string? valueId = (string?)stored["valueId"];
            if (stored["type"] is JObject type)
                return NeoTypeSource.OfType(TypeBindings.Read(type, $"{path}.type"), valueId);
            return NeoTypeSource.AtPath(valueId!, ((JArray)stored["path"]!).Select(segment => (string)segment!).ToArray());
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            if (value is not NeoTypeSource source)
            {
                writer.WriteNull();
                return;
            }
            writer.WriteStartObject();
            if (source.ValueId is not null)
            {
                writer.WritePropertyName("valueId");
                writer.WriteValue(source.ValueId);
            }
            if (source.Path is not null)
            {
                writer.WritePropertyName("path");
                writer.WriteStartArray();
                foreach (string segment in source.Path)
                    writer.WriteValue(segment);
                writer.WriteEndArray();
            }
            if (source.Type is not null)
            {
                writer.WritePropertyName("type");
                TypeBindings.Write(writer, source.Type);
            }
            writer.WriteEndObject();
        }
    }
}
