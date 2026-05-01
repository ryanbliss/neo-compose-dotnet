// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NeoCompose.Export
{
    /// <summary>
    /// Generic Newtonsoft converter helper for deserializing TS-style
    /// discriminated unions. Each derived converter declares the JSON
    /// field that carries the discriminator (e.g. <c>"type"</c>) and a
    /// dispatch function from a discriminator <see cref="JToken"/> to a
    /// concrete subclass type.
    ///
    /// Apply at the abstract base of the union via
    /// <c>[JsonConverter(typeof(MyConverter))]</c>; the converter reads
    /// the JSON object, peeks at the discriminator field, and rehydrates
    /// the right subclass via <see cref="JObject.ToObject(Type, JsonSerializer)"/>.
    ///
    /// Read-only: <see cref="CanWrite"/> is <c>false</c> so default
    /// serialization is used when going C# → JSON. The day-1 SDK only
    /// reads from the export, so this is sufficient; if write-back is
    /// ever needed each subclass can declare <see cref="JsonConverter"/>
    /// at the class level to opt out, or this base can grow a
    /// <see cref="WriteJson"/> implementation.
    /// </summary>
    public abstract class DiscriminatedConverter<TBase> : JsonConverter
        where TBase : class
    {
        /// <summary>
        /// Name of the JSON field carrying the discriminator value.
        /// Defaults to <c>"type"</c> (matches every TS-side discriminated
        /// union in this SDK); override if a particular union uses a
        /// different field.
        /// </summary>
        protected virtual string DiscriminatorField => "type";

        /// <summary>
        /// Map a discriminator value to the concrete subclass type, or
        /// return <c>null</c> if the value is unknown.
        /// </summary>
        protected abstract Type ResolveSubclass(JToken discriminator);

        public override bool CanConvert(Type objectType)
        {
            return typeof(TBase).IsAssignableFrom(objectType);
        }

        public override bool CanWrite => false;

        public override object ReadJson(
            JsonReader reader,
            Type objectType,
            object existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null) return null;
            var obj = JObject.Load(reader);
            var disc = obj[DiscriminatorField];
            if (disc == null)
            {
                throw new JsonSerializationException(
                    $"Missing discriminator field '{DiscriminatorField}' on {typeof(TBase).Name}");
            }
            var concrete = ResolveSubclass(disc);
            if (concrete == null)
            {
                throw new JsonSerializationException(
                    $"Unknown discriminator value '{disc}' for {typeof(TBase).Name}");
            }
            // CRITICAL: do NOT call `obj.ToObject(concrete, serializer)`.
            // Newtonsoft would re-check `concrete`'s base attribute,
            // find this same converter via `CanConvert`, and re-enter
            // — recursing until the runtime stack overflows. (Unity
            // crashes when this loops at PlayMode-test scale.)
            //
            // Instead: instantiate the concrete subclass and use
            // `JsonSerializer.Populate` to fill its fields. Populate
            // does NOT re-invoke the converter for the target object,
            // but DOES dispatch normally for nested fields — which is
            // exactly what we want, so an inner discriminated-union
            // field still flows through its own converter.
            var instance = Activator.CreateInstance(concrete);
            using (var subReader = obj.CreateReader())
            {
                serializer.Populate(subReader, instance);
            }
            return instance;
        }

        public override void WriteJson(
            JsonWriter writer,
            object value,
            JsonSerializer serializer)
        {
            // CanWrite=false routes serialization through Newtonsoft's
            // default path; this method is unreachable but the abstract
            // signature requires an implementation.
            throw new NotImplementedException(
                "DiscriminatedConverter is read-only; default serialization handles writes.");
        }
    }
}
