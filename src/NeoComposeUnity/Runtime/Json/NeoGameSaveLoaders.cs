// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Shared settings for parsing save content envelopes. Date parsing is
    /// disabled because save values are an opaque JSON overlay: Newtonsoft's
    /// default turns any date-looking string inside <c>values</c> into a
    /// <c>JTokenType.Date</c>, silently reformatting the player's data on the
    /// next round-trip and producing tokens the realtime wire converter cannot
    /// transmit. Strings stay strings, byte for byte.
    /// </summary>
    public static class NeoSaveJson
    {
        public static readonly JsonSerializerSettings ContentSettings =
            CreateContentSettings();

        private static JsonSerializerSettings CreateContentSettings()
        {
            var settings = new JsonSerializerSettings
            {
                DateParseHandling = DateParseHandling.None,
            };
            settings.Converters.Add(new TolerantIntegerConverter());
            return settings;
        }
    }

    /// <summary>
    /// Deserializes a single cloud save JSON envelope into a
    /// <see cref="RemoteGameSave"/>, keeping its value rows opaque (see
    /// <see cref="NeoSaveValues"/>). <see cref="Load"/> throws on a malformed or
    /// empty envelope; <see cref="TryLoad"/> reports failure without throwing so a
    /// caller can fall back (e.g. surface a "save could not be read" state).
    /// </summary>
    public static class RemoteGameSaveLoader
    {
        public static RemoteGameSave Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Remote save JSON was empty.");
            }

            var save = JsonConvert.DeserializeObject<RemoteGameSave>(
                json, NeoSaveJson.ContentSettings);
            if (save == null)
            {
                throw new InvalidOperationException("Remote save JSON could not be deserialized.");
            }

            save.staticBindings ??= new();
            return save;
        }

        public static bool TryLoad(string? json, out RemoteGameSave save)
        {
            save = null!;
            if (string.IsNullOrWhiteSpace(json))
                return false;
            try
            {
                var parsed = JsonConvert.DeserializeObject<RemoteGameSave>(
                    json, NeoSaveJson.ContentSettings);
                if (parsed == null)
                    return false;
                parsed.staticBindings ??= new();
                save = parsed;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Deserializes a locally-persisted save JSON envelope into a
    /// <see cref="LocalGameSave"/>, keeping its value rows opaque. Mirrors
    /// <see cref="RemoteGameSaveLoader"/> for the on-device store.
    /// </summary>
    public static class LocalGameSaveLoader
    {
        public static LocalGameSave Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException("Local save JSON was empty.");
            }

            var save = JsonConvert.DeserializeObject<LocalGameSave>(
                json, NeoSaveJson.ContentSettings);
            if (save == null)
            {
                throw new InvalidOperationException("Local save JSON could not be deserialized.");
            }

            save.staticBindings ??= new();
            return save;
        }

        /// <summary>
        /// <see cref="Load"/> for a save already in memory: the header reads
        /// from its tokens and the value rows stay the snapshot's own token,
        /// so nothing round-trips through a string.
        /// </summary>
        internal static LocalGameSave FromSnapshot(JObject snapshot)
        {
            var header = new JObject();
            foreach (var property in snapshot.Properties())
                if (property.Name != "values")
                    header[property.Name] = property.Value;
            var save = header.ToObject<LocalGameSave>(
                JsonSerializer.Create(NeoSaveJson.ContentSettings))
                ?? throw new InvalidOperationException("Local save snapshot could not be read.");
            save.values = new NeoSaveValues(snapshot["values"]);
            save.staticBindings ??= new();
            return save;
        }

        public static bool TryLoad(string? json, out LocalGameSave save)
        {
            save = null!;
            if (string.IsNullOrWhiteSpace(json))
                return false;
            try
            {
                var parsed = JsonConvert.DeserializeObject<LocalGameSave>(
                    json, NeoSaveJson.ContentSettings);
                if (parsed == null)
                    return false;
                parsed.staticBindings ??= new();
                save = parsed;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        public static string Serialize(LocalGameSave save)
        {
            if (save == null)
                throw new ArgumentNullException(nameof(save));
            return JsonConvert.SerializeObject(save);
        }
    }
}
