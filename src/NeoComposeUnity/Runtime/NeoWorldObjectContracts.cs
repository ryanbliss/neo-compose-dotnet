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
        /// Mask interaction enum option id. See
        /// <see cref="NeoSpriteMaskInteractionIds"/>.
        /// </summary>
        string MaskInteraction { get; }

        /// <summary>
        /// Offset added to the draw order derived from the object's layer
        /// group — it does not replace it. Null means no offset.
        /// </summary>
        int? SortingOrder { get; }
    }

    /// <summary>
    /// Sprite mask interaction enum option ids authored on the web side. These
    /// exact ids are pinned in the neo-compose repo at
    /// <c>src/models/classes/world-system-classes.generated.ts</c>, generated
    /// from <c>system-schema/Enums/*.neo</c> — keep both sides in sync.
    ///
    /// Option names mirror Unity's <see cref="SpriteMaskInteraction"/> exactly,
    /// so the generated C# maps by name.
    /// </summary>
    public static class NeoSpriteMaskInteractionIds
    {
        public const string None = "system_9d607a4f-60c3-4347-94fc-f24b538bf468";
        public const string VisibleInsideMask =
            "system_4c670ac9-78a4-44e9-9833-94e1c69dca97";
        public const string VisibleOutsideMask =
            "system_a0aeb200-7216-49e2-aad2-e151ff35c336";

        public static SpriteMaskInteraction Parse(string optionId)
        {
            switch (optionId)
            {
                case None:
                    return SpriteMaskInteraction.None;
                case VisibleInsideMask:
                    return SpriteMaskInteraction.VisibleInsideMask;
                case VisibleOutsideMask:
                    return SpriteMaskInteraction.VisibleOutsideMask;
                default:
                    throw new ArgumentException(
                        "Unrecognized sprite MaskInteraction option id "
                            + $"'{optionId}'.",
                        nameof(optionId));
            }
        }
    }

}
