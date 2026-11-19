// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;

namespace NeoCompose.Runtime.NeoScript
{
    /// <summary>The member slot a reference's id stands for.</summary>
    internal sealed class NSSlotPlace
    {
        /// <summary>The owner's class value; null for a static slot.</summary>
        internal readonly object? Owner;

        /// <summary>The owner's schema key; null for a static slot.</summary>
        internal readonly string? Key;

        internal readonly string MemberId;
        internal readonly TypeBinding? StaticConstruction;

        private NSSlotPlace(object? owner, string? key, string memberId, TypeBinding? staticConstruction = null)
        {
            Owner = owner;
            Key = key;
            MemberId = memberId;
            StaticConstruction = staticConstruction;
        }

        internal bool IsStatic => Owner is null;

        internal static NSSlotPlace Member(object owner, string key, string memberId) => new(owner, key, memberId);

        internal static NSSlotPlace Static(string memberId, TypeBinding? construction = null) => new(null, null, memberId, construction);
    }

    /// <summary>
    /// P112 §1.1. A slot-kind reference: the id of the row it names. A handle
    /// reference is the handle itself, so it never takes this shape. Hosts
    /// see only the id.
    /// </summary>
    [JsonConverter(typeof(NSSlotReferenceConverter))]
    internal sealed class NSSlotReference
    {
        internal readonly string TargetId;

        /// <summary>
        /// The slot, when this evaluation formed the reference. A derived id
        /// has no row until its first write (§2.2), so only the place can read it.
        /// </summary>
        internal readonly NSSlotPlace? Place;

        /// <summary>The declared target, fit-checked when the reference is used (§4.3).</summary>
        internal readonly TypeInfo? Target;

        /// <summary>The stored reference member this was read from, for errors.</summary>
        internal readonly string? MemberId;

        internal NSSlotReference(string targetId, NSSlotPlace? place, TypeInfo? target, string? memberId)
        {
            TargetId = targetId;
            Place = place;
            Target = target;
            MemberId = memberId;
        }
    }

    /// <summary>
    /// Writes a reference as its target's id, the shape a saved closure's
    /// capture keeps. The capture's declared type restores it on invocation.
    /// </summary>
    internal sealed class NSSlotReferenceConverter : JsonConverter
    {
        public override bool CanRead => false;

        public override bool CanConvert(Type objectType) => objectType == typeof(NSSlotReference);

        public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer) =>
            throw new NotSupportedException("A reference reads back through its declared type, not as a reference.");

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) =>
            writer.WriteValue(((NSSlotReference)value!).TargetId);
    }
}
