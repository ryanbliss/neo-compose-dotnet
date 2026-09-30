// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Reads JSON with every short string and property name interned. The
    /// same id parsed from the project, a partition or a save — and the
    /// generated code's literals — is then one instance, so the runtime's
    /// id comparisons and dictionary probes succeed on reference equality
    /// instead of comparing characters.
    /// </summary>
    internal sealed class NeoInterningJsonReader : JsonTextReader
    {
        // Ids, names and enum options. Longer strings are prose and code.
        private const int MaxInternedLength = 64;

        // A save's own strings are shared within the read but not added to
        // the process-wide intern pool, which would keep them for the life
        // of the process.
        private readonly Dictionary<string, string>? savePool;

        private NeoInterningJsonReader(string json, Dictionary<string, string>? savePool)
            : base(new StringReader(json))
        {
            this.savePool = savePool;
        }

        internal static T? Deserialize<T>(string json, JsonSerializerSettings? settings = null) =>
            Deserialize<T>(new NeoInterningJsonReader(json, null), settings);

        /// <summary>
        /// <see cref="Deserialize{T}"/> for save JSON: strings the process
        /// already interned are reused, and the save's others are shared
        /// only within this read.
        /// </summary>
        internal static T? DeserializeSave<T>(string json, JsonSerializerSettings? settings = null) =>
            Deserialize<T>(new NeoInterningJsonReader(json, new Dictionary<string, string>(StringComparer.Ordinal)), settings);

        private static T? Deserialize<T>(NeoInterningJsonReader reader, JsonSerializerSettings? settings)
        {
            using (reader)
            {
                JsonSerializer serializer = JsonSerializer.CreateDefault(settings);
                serializer.CheckAdditionalContent = true;
                return serializer.Deserialize<T>(reader);
            }
        }

        private string Intern(string value)
        {
            if (savePool is null)
                return string.Intern(value);
            if (string.IsInterned(value) is { } interned)
                return interned;
            if (savePool.TryGetValue(value, out string? pooled))
                return pooled;
            savePool.Add(value, value);
            return value;
        }

        public override bool Read()
        {
            if (!base.Read())
                return false;
            if (TokenType is JsonToken.String or JsonToken.PropertyName
                && Value is string { Length: <= MaxInternedLength } value)
                SetToken(TokenType, Intern(value), false);
            return true;
        }

        public override string? ReadAsString()
        {
            string? value = base.ReadAsString();
            if (TokenType != JsonToken.String || value is not { Length: <= MaxInternedLength })
                return value;
            value = Intern(value);
            SetToken(JsonToken.String, value, false);
            return value;
        }
    }
}
