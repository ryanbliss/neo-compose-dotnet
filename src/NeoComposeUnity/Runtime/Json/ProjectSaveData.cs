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
    /// Save metadata about a game runtime platform. Used for diagnostics.
    /// </summary>
    public class GameRuntimePlatform
    {
        /// <summary>
        /// Platform kind, captured from Unity's <c>RuntimePlatform</c>.
        /// </summary>
        public string kind = null!;

        /// <summary>
        /// Last time this platform wrote to the save file.
        /// </summary>
        public NeoTimestamp lastSavedAt;
    }

    /// <summary>
    /// Save metadata about system info. Used for diagnostics.
    /// </summary>
    public class GameSystemInfo
    {
        public string deviceType = null!;
        public string deviceModel = null!;
        public string deviceName = null!;
        public string operatingSystem = null!;
        public NeoTimestamp lastSavedAt;
    }

    /// <summary>
    /// Save metadata about an input device snapshot. Used for diagnostics.
    /// </summary>
    public class GameInputDeviceInfo
    {
        public string source = null!;
        public string kind = null!;
        public string name = null!;
        public string displayName = null!;
        public string layout = null!;
        public string manufacturer = null!;
        public string product = null!;
        public int? slot;
        public NeoTimestamp lastSavedAt;
    }

    /// <summary>
    /// Project version captured by a save file.
    /// </summary>
    public class VersionData
    {
        /// <summary>
        /// Stable project-version id. This is the value code should use to
        /// identify which exported project version the save was created
        /// against.
        /// </summary>
        public string id = null!;

        /// <summary>
        /// Human-readable version label copied into the save for inspection
        /// and debugging. It is intentionally secondary to <see cref="id"/>.
        /// </summary>
        public string label = null!;
    }

    /// <summary>
    /// Save file envelope for runtime-owned values.
    ///
    /// <para>The authored export in <see cref="ProjectData"/> remains the
    /// immutable asset/default graph. A save file is a <b>sparse overlay</b>:
    /// it stores only the value rows runtime code has created or changed, each
    /// keyed by its <b>stable value id</b>. Resolution is
    /// <c>save.values[id] ?? authored.values[id]</c> — a save row shadows the
    /// authored default at the same id, so there is no override-map
    /// indirection.</para>
    /// </summary>
    [JsonConverter(typeof(Schema8SaveEnvelopeConverter<ProjectSaveData>))]
    public class ProjectSaveData
    {
        /// <summary>
        /// Human-readable save name. New save files get a generated name by
        /// default, and hosts can override generation through
        /// <c>NeoClient.BuildSaveName</c>.
        /// </summary>
        public string name = null!;

        /// <summary>
        /// Stable client-owned save id for the active save graph, or null for the
        /// session graph / a not-yet-identified save. Bridges this in-memory graph
        /// to its persisted <see cref="LocalGameSave"/> and cloud
        /// <see cref="RemoteGameSave"/> counterparts.
        /// </summary>
        public string? customId;

        /// <summary>Release channel the active save targets, or null for the session graph.</summary>
        public string? releaseChannelId;

        /// <summary>Server document id once the save has synchronized; null otherwise.</summary>
        public string? serverId;

        /// <summary>
        /// The head snapshot id the in-memory graph was loaded from. Sent as the
        /// commit's conflict base so a moved head produces a typed conflict rather
        /// than a silent overwrite. Null for a local-only or session graph.
        /// </summary>
        public string? snapshotId;

        /// <summary>
        /// Legacy opaque local-artifact field retained only so old developer
        /// save files round-trip. Cloud synchronization ignores it.
        /// </summary>
        public string? snapshotHash;

        /// <summary>
        /// Last successful cloud sync time as epoch milliseconds; null when never
        /// synced. Plain nullable so a JSON <c>null</c> round-trips to null.
        /// </summary>
        public double? synchronizedAt;

        /// <summary>
        /// Stable project id for the authored project this save belongs to.
        /// </summary>
        public string projectId = null!;

        /// <summary>
        /// Stable version id plus a readable label for the project export
        /// this save was created against.
        /// </summary>
        public VersionData version = null!;

        /// <summary>
        /// Time the save file was first created, serialized as Convex-style
        /// epoch milliseconds through <see cref="NeoTimestamp"/>.
        /// </summary>
        public NeoTimestamp createdAt;

        /// <summary>
        /// Time the save file was last emitted through the save callback.
        /// Loading or serializing the save does not update this value.
        /// </summary>
        public NeoTimestamp updatedAt;

        /// <summary>
        /// Platforms that have written to this save file, or null when
        /// diagnostic logging is disabled.
        /// </summary>
        public List<GameRuntimePlatform>? platforms;

        /// <summary>
        /// Systems that have written to this save file, or null when
        /// diagnostic logging is disabled.
        /// </summary>
        public List<GameSystemInfo>? systems;

        /// <summary>
        /// Input devices detected while writing this save file, or null
        /// when diagnostic logging is disabled.
        /// </summary>
        public List<GameInputDeviceInfo>? inputDevices;

        /// <summary>
        /// Runtime-owned value rows keyed by their stable value id. Each row
        /// shadows the authored default at the same id (sparse overlay:
        /// <c>save.values[id] ?? authored.values[id]</c>); a row may carry a
        /// <c>mark: "removed"</c> tombstone to express an explicit unset.
        /// </summary>
        public Dictionary<string, MemberValue> values = null!;

        /// <summary>
        /// Sparse runtime bindings for static Class members, keyed by
        /// stable member id. A missing key inherits the authored
        /// <c>member.valueId</c>; a present null value is an explicit unset.
        /// The referenced value graph remains in <see cref="values"/>.
        /// </summary>
        public Dictionary<string, string?> staticBindings = new();
    }

    /// <summary>
    /// Strict reader for schema-8 save envelopes. Value-row metadata is
    /// checked for removed class/member fields while the authored payload
    /// inside each row's <c>value</c> property remains opaque.
    /// </summary>
    public sealed class Schema8SaveEnvelopeConverter<T> : JsonConverter
        where T : class, new()
    {
        public override bool CanConvert(Type objectType) => objectType == typeof(T);

        public override bool CanWrite => false;

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;

            var obj = JObject.Load(reader);
            Schema8LegacyFieldGuard.ValidateSaveEnvelope(obj);

            var envelope = new T();
            using (var subReader = obj.CreateReader())
            {
                serializer.Populate(subReader, envelope);
            }
            return envelope;
        }

        public override void WriteJson(
            JsonWriter writer,
            object? value,
            JsonSerializer serializer)
        {
            throw new NotImplementedException(
                "Schema8SaveEnvelopeConverter is read-only; default serialization handles writes.");
        }
    }
}
