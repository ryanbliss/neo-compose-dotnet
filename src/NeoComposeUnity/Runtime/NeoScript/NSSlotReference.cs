// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;

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

        private NSSlotPlace(object? owner, string? key, string memberId)
        {
            Owner = owner;
            Key = key;
            MemberId = memberId;
        }

        internal bool IsStatic => Owner is null;

        internal static NSSlotPlace Member(object owner, string key, string memberId) => new(owner, key, memberId);

        internal static NSSlotPlace Static(string memberId) => new(null, null, memberId);
    }

    /// <summary>
    /// P112 §1.1. A slot-kind reference: the id of the row it names. A handle
    /// reference is the handle itself, so it never takes this shape. Hosts
    /// see only the id.
    /// </summary>
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
}
