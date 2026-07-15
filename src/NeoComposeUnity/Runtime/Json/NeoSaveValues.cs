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
    /// Opaque carrier for a save file's <c>values</c> map.
    ///
    /// <para>A save's value rows are kept as a raw JSON token until something
    /// actually needs to read them as typed <see cref="MemberValue"/> rows.
    /// This lets the SDK list, clone, archive, and migrate saves it cannot (yet)
    /// interpret — for example a save authored against a newer schema version, or
    /// one bound to a different release channel that must be cloned before it can
    /// load. Deserializing eagerly would throw on those; keeping the values opaque
    /// preserves them byte-for-byte for a later clone/migration.</para>
    ///
    /// <para>The values map is a sparse overlay keyed by stable value id
    /// (<c>save.values[id] ?? authored</c>) — there is no separate
    /// authored-id → value-id bridge map.</para>
    /// </summary>
    [JsonConverter(typeof(NeoSaveValuesConverter))]
    public sealed class NeoSaveValues
    {
        private readonly JToken raw;

        public NeoSaveValues(JToken? raw)
        {
            this.raw = raw ?? JValue.CreateNull();
        }

        /// <summary>An empty (no value rows) values map.</summary>
        public static NeoSaveValues Empty => new NeoSaveValues(new JObject());

        /// <summary>
        /// The untouched JSON token as received. Never deserialized into typed
        /// rows; callers that need typed access use <see cref="TryDeserialize"/>.
        /// </summary>
        public JToken Raw => raw;

        /// <summary>True when the token is JSON null (no values present).</summary>
        public bool IsNull => raw.Type == JTokenType.Null;

        /// <summary>
        /// Attempts to materialize the opaque token into typed
        /// <see cref="MemberValue"/> rows. On any incompatibility (non-object
        /// token, an unrecognized value shape, a deserialization error) this
        /// returns <c>false</c> and the raw token is left untouched and still
        /// readable through <see cref="Raw"/>. The out parameter is an empty map
        /// on failure, never null.
        /// </summary>
        public bool TryDeserialize(out Dictionary<string, MemberValue> values)
        {
            values = new Dictionary<string, MemberValue>();
            if (raw.Type != JTokenType.Object) return false;
            try
            {
                var deserialized = raw.ToObject<Dictionary<string, MemberValue>>();
                if (deserialized == null) return false;
                values = deserialized;
                return true;
            }
            catch (JsonException)
            {
                values = new Dictionary<string, MemberValue>();
                return false;
            }
        }

        /// <summary>
        /// Builds an opaque values map from typed rows (used when serializing a
        /// commit payload). Writes through default serialization so the
        /// per-shape <see cref="MemberValue"/> converters apply.
        /// </summary>
        public static NeoSaveValues FromTypedValues(
            IReadOnlyDictionary<string, MemberValue> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            return new NeoSaveValues(JToken.FromObject(values));
        }
    }

    /// <summary>
    /// Splits a merged save overlay into its storage partitions
    /// (specs/list-member-and-tilegrid-scaling.md §6) for the commit wire:
    /// rows stamped with a non-empty <c>mapKey</c> ride in their partition's
    /// overlay; unstamped rows are the main partition.
    /// </summary>
    public static class NeoSaveValuePartitions
    {
        /// <summary>
        /// Splits <paramref name="merged"/> by each row's <c>mapKey</c> stamp.
        /// Returns the main-partition overlay plus a per-partition map, or
        /// <c>partitions = null</c> when no row carries a stamp — in which
        /// case <c>mainValues</c> is the untouched input token (zero-copy;
        /// the common no-world-edits commit re-serializes nothing extra).
        /// Works at the token level so opaque (schema-incompatible) rows
        /// split without typed deserialization.
        /// </summary>
        public static (NeoSaveValues mainValues, Dictionary<string, NeoSaveValues>? partitions) Split(
            NeoSaveValues merged)
        {
            if (merged == null) throw new ArgumentNullException(nameof(merged));
            if (merged.Raw is not JObject overlay) return (merged, null);

            bool anyStamped = false;
            foreach (var property in overlay.Properties())
            {
                if (RowMapKey(property.Value) is null) continue;
                anyStamped = true;
                break;
            }
            if (!anyStamped) return (merged, null);

            var main = new JObject();
            var partitionTokens = new Dictionary<string, JObject>();
            foreach (var property in overlay.Properties())
            {
                string? mapKey = RowMapKey(property.Value);
                if (mapKey is null)
                {
                    // DeepClone: a JToken has one parent; adding the live token
                    // would detach it from the caller's overlay.
                    main[property.Name] = property.Value.DeepClone();
                    continue;
                }
                if (!partitionTokens.TryGetValue(mapKey, out var partition))
                {
                    partition = new JObject();
                    partitionTokens[mapKey] = partition;
                }
                partition[property.Name] = property.Value.DeepClone();
            }

            var partitions = new Dictionary<string, NeoSaveValues>(partitionTokens.Count);
            foreach (var pair in partitionTokens)
            {
                partitions[pair.Key] = new NeoSaveValues(pair.Value);
            }
            return (new NeoSaveValues(main), partitions);
        }

        private static string? RowMapKey(JToken row)
        {
            if (row.Type != JTokenType.Object) return null;
            var mapKey = ((JObject)row)["mapKey"];
            if (mapKey is null || mapKey.Type != JTokenType.String) return null;
            var key = mapKey.Value<string>();
            return string.IsNullOrEmpty(key) ? null : key;
        }
    }

    /// <summary>
    /// Round-trips <see cref="NeoSaveValues"/> as the raw JSON token: reads keep
    /// the token verbatim (opaque), writes emit it unchanged.
    /// </summary>
    public sealed class NeoSaveValuesConverter : JsonConverter<NeoSaveValues>
    {
        public override NeoSaveValues ReadJson(
            JsonReader reader,
            Type objectType,
            NeoSaveValues? existingValue,
            bool hasExistingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return new NeoSaveValues(JValue.CreateNull());
            }

            return new NeoSaveValues(JToken.Load(reader));
        }

        public override void WriteJson(
            JsonWriter writer,
            NeoSaveValues? value,
            JsonSerializer serializer)
        {
            var token = value?.Raw ?? JValue.CreateNull();
            token.WriteTo(writer);
        }
    }
}
