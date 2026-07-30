// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using UnityEngine;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Runtime contract implemented by generated world object values (world
    /// kind <c>objectBase</c>, e.g. a generated <c>NeoObjectBase</c> family).
    /// Every placed object, composition child, and layer group link inherits
    /// it, so the renderer reads placement without reflecting on property
    /// names.
    /// </summary>
    public interface INeoWorldObjectValue : INeoValueReference
    {
        string Name { get; }

        /// <summary>Cells from the enclosing object's origin.</summary>
        NeoReadOnlyVector3 Position { get; }

        /// <summary>Footprint in cells.</summary>
        NeoReadOnlyVector3 Size { get; }

        /// <summary>
        /// When false, this object and its subtree render nowhere and
        /// contribute no collider. The value stays live: member writes still
        /// apply, and a clip playing on or through the object keeps running and
        /// keeps writing. Disabling an object hides its whole subtree
        /// regardless of each child's own value, so re-enabling it restores
        /// exactly what was there.
        /// </summary>
        bool Enabled { get; }
    }

    /// <summary>
    /// Runtime contract implemented by generated composed object values (world
    /// kind <c>object</c>, e.g. a generated <c>NeoObject</c> family). Presence
    /// of the interface is the compile-time answer to "can this object have
    /// children?".
    /// </summary>
    public interface INeoObjectCompositionSource
    {
        IReadOnlyList<INeoWorldObjectValue> Children { get; }
    }

    /// <summary>
    /// Runtime contract implemented by generated composed object values (world
    /// kind <c>object</c>) that can carry an authored collider.
    /// </summary>
    public interface INeoColliderSource
    {
        INeoCollider? Collider { get; }
    }

    /// <summary>
    /// Runtime contract implemented by any generated class that declares a
    /// member of a <c>sortingGroup</c> world-kind class — the author attaches
    /// one to their own <c>NeoObject</c> subclass, the way a component is added
    /// in Unity, so it is not a member of <c>NeoObject</c> itself. The property
    /// maps to whatever the author named that member. A non-null
    /// <see cref="SortingGroup"/> makes the object and its children sort as one
    /// unit.
    /// </summary>
    public interface INeoSortingGroupSource
    {
        INeoSortingGroup? SortingGroup { get; }
    }

    /// <summary>
    /// Runtime contract implemented by generated collider values (world kind
    /// <c>objectCollider</c>, e.g. a generated <c>NeoCollider</c> family).
    /// <see cref="Size"/> and <see cref="Offset"/> are in cells and share the
    /// object root's origin, the same contract the web editor renders.
    /// </summary>
    public interface INeoCollider : INeoValueReference
    {
        NeoReadOnlyVector2 Size { get; }

        NeoReadOnlyVector2? Offset { get; }

        bool? IsTrigger { get; }
    }

    /// <summary>
    /// Runtime contract implemented by generated sorting group values (world
    /// kind <c>sortingGroup</c>, e.g. a generated <c>NeoSortingGroup</c>
    /// family). Sorting layer and order still come from the object's layer
    /// group; only <see cref="SortAtRoot"/> is authored here.
    /// </summary>
    public interface INeoSortingGroup : INeoValueReference
    {
        /// <summary>
        /// Sort this group against the scene root, ignoring any enclosing
        /// sorting group. Maps to <c>SortingGroup.sortAtRoot</c>, and is read
        /// once at spawn.
        /// </summary>
        bool SortAtRoot { get; }
    }

    /// <summary>
    /// Runtime contract implemented by generated sprite object values (world
    /// kind <c>spriteObject</c>, e.g. a generated <c>NeoSpriteObject</c>
    /// family). Carries everything the renderer writes onto a
    /// <see cref="SpriteRenderer"/>.
    /// </summary>
    public interface INeoSpriteObjectValue : INeoWorldObjectValue
    {
        Sprite Sprite { get; }

        bool FlipX { get; }

        bool FlipY { get; }

        /// <summary>
        /// Mask interaction enum option id. Deliberately the raw id rather
        /// than <see cref="NeoSpriteMaskInteraction"/>: this contract is the
        /// renderer's data view of a value, and generated code satisfies it
        /// with an explicit bridge off its own typed member. Convert with
        /// <see cref="NeoSpriteMaskInteractions.ToUnity"/>.
        /// </summary>
        string MaskInteraction { get; }

        /// <summary>
        /// Offset added to the draw order derived from the object's layer
        /// group — it does not replace it. Null means no offset.
        /// </summary>
        int? SortingOrder { get; }
    }

    /// <summary>
    /// How a sprite reads against a mask — the one mask-interaction type game
    /// code sees, shared with the renderer's own contract. Its option ids are
    /// contract ids, so its generated wrapper would be byte-identical in every
    /// project; the SDK ships that exact shape once and codegen skips emitting
    /// it, the same arrangement <see cref="NeoPlayDirection"/> uses.
    /// The body below must stay identical to what the generator would emit —
    /// the web repo's sdk-runtime-enums binding pins the ids and member names.
    /// </summary>
    public sealed class NeoSpriteMaskInteraction : IEquatable<NeoSpriteMaskInteraction>, INeoEnumOption
    {
        private static readonly Dictionary<string, NeoSpriteMaskInteraction> values = new Dictionary<string, NeoSpriteMaskInteraction>();
        public string optionId { get; }
        public string Text => TextForOptionId(optionId);
        public string TextId => TextIdForOptionId(optionId);

        private NeoSpriteMaskInteraction(string optionId)
        {
            this.optionId = optionId;
        }

        public static readonly NeoSpriteMaskInteraction None = FromOptionId("system_9d607a4f-60c3-4347-94fc-f24b538bf468");
        public static readonly NeoSpriteMaskInteraction VisibleInsideMask = FromOptionId("system_4c670ac9-78a4-44e9-9833-94e1c69dca97");
        public static readonly NeoSpriteMaskInteraction VisibleOutsideMask = FromOptionId("system_a0aeb200-7216-49e2-aad2-e151ff35c336");

        public static NeoSpriteMaskInteraction FromOptionId(string optionId)
        {
            if (values.TryGetValue(optionId, out var known)) return known;
            var created = new NeoSpriteMaskInteraction(optionId);
            values[optionId] = created;
            return created;
        }

        public static string[] ToOptionIds(IEnumerable<NeoSpriteMaskInteraction>? options)
        {
            if (options is null) return Array.Empty<string>();
            var ids = new List<string>();
            foreach (var option in options) ids.Add(option.optionId);
            return ids.ToArray();
        }

        public static bool IsKnown(string id)
        {
            return id switch
            {
                "system_9d607a4f-60c3-4347-94fc-f24b538bf468" => true,
                "system_4c670ac9-78a4-44e9-9833-94e1c69dca97" => true,
                "system_a0aeb200-7216-49e2-aad2-e151ff35c336" => true,
                _ => false,
            };
        }

        public static string TextIdForOptionId(string optionId)
        {
            return optionId switch
            {
                "system_9d607a4f-60c3-4347-94fc-f24b538bf468" => "None",
                "system_4c670ac9-78a4-44e9-9833-94e1c69dca97" => "Visible inside mask",
                "system_a0aeb200-7216-49e2-aad2-e151ff35c336" => "Visible outside mask",
                _ => optionId,
            };
        }

        public static string TextForOptionId(string optionId, NeoClient? client = null)
        {
            return client is null ? TextIdForOptionId(optionId) : client.Localization.ResolveText(TextIdForOptionId(optionId));
        }

        public static implicit operator string(NeoSpriteMaskInteraction value) => value.optionId;
        public static implicit operator NeoSpriteMaskInteraction(string optionId) => FromOptionId(optionId);
        public override string ToString() => optionId;
        public bool Equals(NeoSpriteMaskInteraction? other) => other is not null && optionId == other.optionId;
        public override bool Equals(object? obj) => Equals(obj as NeoSpriteMaskInteraction);
        public override int GetHashCode() => optionId.GetHashCode();
        public static bool operator ==(NeoSpriteMaskInteraction? left, NeoSpriteMaskInteraction? right) => ReferenceEquals(left, right) || (left is not null && left.Equals(right));
        public static bool operator !=(NeoSpriteMaskInteraction? left, NeoSpriteMaskInteraction? right) => !(left == right);
    }

    /// <summary>
    /// Unity interop for <see cref="NeoSpriteMaskInteraction"/>.
    ///
    /// It lives beside the type rather than on it because the wrapper's body
    /// has to stay byte-identical to what codegen would emit — an SDK-only
    /// member there would be a body the generator never writes, and the next
    /// person to compare the two would have no way to tell which differences
    /// were deliberate.
    /// </summary>
    public static class NeoSpriteMaskInteractions
    {
        /// <summary>
        /// The Unity enum an authored option maps onto. Accepts an option id
        /// directly — the implicit conversion interns it — so a renderer can
        /// pass <see cref="INeoSpriteObjectValue.MaskInteraction"/> straight
        /// through.
        /// </summary>
        public static SpriteMaskInteraction ToUnity(NeoSpriteMaskInteraction value)
        {
            if (value == NeoSpriteMaskInteraction.None)
            {
                return SpriteMaskInteraction.None;
            }
            if (value == NeoSpriteMaskInteraction.VisibleInsideMask)
            {
                return SpriteMaskInteraction.VisibleInsideMask;
            }
            if (value == NeoSpriteMaskInteraction.VisibleOutsideMask)
            {
                return SpriteMaskInteraction.VisibleOutsideMask;
            }
            throw new ArgumentException(
                "Unrecognized sprite MaskInteraction option id "
                    + $"'{value.optionId}'.",
                nameof(value));
        }
    }

}
