// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using Newtonsoft.Json;

namespace NeoCompose.Runtime.Json
{
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

            var save = JsonConvert.DeserializeObject<RemoteGameSave>(json);
            if (save == null)
            {
                throw new InvalidOperationException("Remote save JSON could not be deserialized.");
            }

            return save;
        }

        public static bool TryLoad(string? json, out RemoteGameSave save)
        {
            save = null!;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var parsed = JsonConvert.DeserializeObject<RemoteGameSave>(json);
                if (parsed == null) return false;
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

            var save = JsonConvert.DeserializeObject<LocalGameSave>(json);
            if (save == null)
            {
                throw new InvalidOperationException("Local save JSON could not be deserialized.");
            }

            return save;
        }

        public static bool TryLoad(string? json, out LocalGameSave save)
        {
            save = null!;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var parsed = JsonConvert.DeserializeObject<LocalGameSave>(json);
                if (parsed == null) return false;
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
            if (save == null) throw new ArgumentNullException(nameof(save));
            return JsonConvert.SerializeObject(save);
        }
    }
}
