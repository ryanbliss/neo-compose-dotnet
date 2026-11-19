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

        // Cache each short string once per read instead of repeatedly probing
        // the global intern pool. Authored strings remain globally interned;
        // save-only strings stay local so player data is not retained forever.
        private readonly Dictionary<string, string> pool;
        private readonly bool saving;

        private NeoInterningJsonReader(string json, Dictionary<string, string>? savePool)
            : base(new StringReader(json))
        {
            pool = savePool ?? new Dictionary<string, string>(StringComparer.Ordinal);
            saving = savePool is not null;
        }

        // Compiled expression trees can exceed Json.NET's default depth of 64.
        internal static readonly JsonSerializerSettings ProjectSettings = new() { MaxDepth = 256 };

        internal static T? Deserialize<T>(string json, JsonSerializerSettings? settings = null) =>
            Deserialize<T>(new NeoInterningJsonReader(json, null), settings ?? ProjectSettings);

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
            if (pool.TryGetValue(value, out string? pooled))
                return pooled;
            string shared = saving ? string.IsInterned(value) ?? value : string.Intern(value);
            pool.Add(value, shared);
            return shared;
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
