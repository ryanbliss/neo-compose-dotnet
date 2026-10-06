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
        string Name
        {
            get;
        }

        /// <summary>Cells from the enclosing object's origin.</summary>
        NeoReadOnlyVector3 Position
        {
            get;
        }

        /// <summary>Footprint in cells.</summary>
        NeoReadOnlyVector3 Size
        {
            get;
        }

        /// <summary>
        /// When false, this object and its subtree render nowhere and
        /// contribute no collider. The value stays live: member writes still
        /// apply, and a clip playing on or through the object keeps running and
        /// keeps writing. Disabling an object hides its whole subtree
        /// regardless of each child's own value, so re-enabling it restores
        /// exactly what was there.
        /// </summary>
        bool Enabled
        {
            get;
        }
    }

    /// <summary>
    /// Runtime contract implemented by generated composed object values (world
    /// kind <c>object</c>, e.g. a generated <c>NeoObject</c> family). Presence
    /// of the interface is the compile-time answer to "can this object have
    /// children?".
    /// </summary>
    public interface INeoObjectCompositionSource
    {
        IReadOnlyList<INeoWorldObjectValue> Children
        {
            get;
        }
    }

    /// <summary>
    /// Runtime contract implemented by every generated <c>NeoObjectBase</c>
    /// value — objects, sprite children, and layer links — which can carry a
    /// collider. The tile grid renderer hosts the colliders of placed objects
    /// and their children; a tile layer link's collider has no host.
    /// </summary>
    public interface INeoColliderSource
    {
        INeoCollider? Collider
        {
            get;
        }
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
        INeoSortingGroup? SortingGroup
        {
            get;
        }
    }

    /// <summary>
    /// Runtime contract implemented by generated collider values (world kind
    /// <c>objectCollider</c>, e.g. a generated <c>NeoCollider</c> family).
    /// <see cref="Size"/> and <see cref="Offset"/> are in cells and share the
    /// object root's origin, the same contract the web editor renders.
    /// </summary>
    public interface INeoCollider : INeoValueReference
    {
        NeoReadOnlyVector2 Size
        {
            get;
        }

        NeoReadOnlyVector2? Offset
        {
            get;
        }

        bool? IsTrigger
        {
            get;
        }
    }

    /// <summary>
    /// Runtime contract implemented by generated sorting group values (world
    /// kind <c>sortingGroup</c>, e.g. a generated <c>NeoSortingGroup</c>
    /// family). Sorting layer and order still come from the object's layer
    /// group; <see cref="SortAtRoot"/> and <see cref="SortPoint"/> are
    /// authored here.
    /// </summary>
    public interface INeoSortingGroup : INeoValueReference
    {
        /// <summary>
        /// Sort this group against the scene root, ignoring any enclosing
        /// sorting group. Maps to <c>SortingGroup.sortAtRoot</c>, and is read
        /// once at spawn.
        /// </summary>
        bool SortAtRoot
        {
            get;
        }

        /// <summary>
        /// Where the group sorts along the camera's transparency sort axis, in
        /// cells from the object's origin-cell corner, the same space as
        /// <see cref="INeoCollider.Offset"/>. The renderer moves only the
        /// <c>SortingGroup</c> component's GameObject to this point and cancels
        /// the offset beneath it, so the art never moves. Unlike
        /// <see cref="SortAtRoot"/> it is live: a write applies on the next
        /// coalesced refresh.
        /// </summary>
        NeoReadOnlyVector2 SortPoint
        {
            get;
        }
    }

    /// <summary>
    /// Runtime contract implemented by generated sprite object values (world
    /// kind <c>spriteObject</c>, e.g. a generated <c>NeoSpriteObject</c>
    /// family). Carries everything the renderer writes onto a
    /// <see cref="SpriteRenderer"/>.
    /// </summary>
    public interface INeoSpriteObjectValue : INeoWorldObjectValue
    {
        Sprite Sprite
        {
            get;
        }

        bool FlipX
        {
            get;
        }

        bool FlipY
        {
            get;
        }

        /// <summary>
        /// Mask interaction enum option id. Deliberately the raw id rather
        /// than <see cref="NeoSpriteMaskInteraction"/>: this contract is the
        /// renderer's data view of a value, and generated code satisfies it
        /// with an explicit bridge off its own typed member. Convert with
        /// <see cref="NeoSpriteMaskInteractionOptions.FromOptionId"/> and
        /// <see cref="NeoSpriteMaskInteractions.ToUnity"/>.
        /// </summary>
        string MaskInteraction
        {
            get;
        }

        /// <summary>
        /// Offset added to the draw order derived from the object's layer
        /// group — it does not replace it. Null means no offset.
        /// </summary>
        int? SortingOrder
        {
            get;
        }
    }

    /// <summary>
    /// How a sprite reads against a mask — the one mask-interaction type game
    /// code sees, shared with the renderer's own contract. Its option ids are
    /// contract ids, so its generated enum would be byte-identical in every
    /// project; the SDK ships that exact shape once and codegen skips emitting
    /// it, the same arrangement <see cref="NeoPlayDirection"/> uses.
    /// The body below must stay identical to what the generator would emit —
    /// the web repo's sdk-runtime-enums binding pins the ids and member names.
    /// </summary>
    [NeoEnum(typeof(NeoSpriteMaskInteractionOptions))]
    public enum NeoSpriteMaskInteraction
    {
        None = 218713620,
        VisibleInsideMask = 452037583,
        VisibleOutsideMask = 743191512,
    }

    /// <summary>
    /// Converts <see cref="NeoSpriteMaskInteraction"/> to and from the option
    /// ids Neo stores.
    /// </summary>
    public static class NeoSpriteMaskInteractionOptions
    {
        public static NeoSpriteMaskInteraction FromOptionId(string optionId)
        {
            return optionId switch
            {
                "system_9d607a4f-60c3-4347-94fc-f24b538bf468" => NeoSpriteMaskInteraction.None,
                "system_4c670ac9-78a4-44e9-9833-94e1c69dca97" => NeoSpriteMaskInteraction.VisibleInsideMask,
                "system_a0aeb200-7216-49e2-aad2-e151ff35c336" => NeoSpriteMaskInteraction.VisibleOutsideMask,
                _ => NeoUndeclaredEnumOptions<NeoSpriteMaskInteraction>.FromOptionId(optionId),
            };
        }

        public static string OptionId(this NeoSpriteMaskInteraction value)
        {
            return value switch
            {
                NeoSpriteMaskInteraction.None => "system_9d607a4f-60c3-4347-94fc-f24b538bf468",
                NeoSpriteMaskInteraction.VisibleInsideMask => "system_4c670ac9-78a4-44e9-9833-94e1c69dca97",
                NeoSpriteMaskInteraction.VisibleOutsideMask => "system_a0aeb200-7216-49e2-aad2-e151ff35c336",
                _ => NeoUndeclaredEnumOptions<NeoSpriteMaskInteraction>.OptionId(value),
            };
        }

        public static string[] ToOptionIds(IEnumerable<NeoSpriteMaskInteraction>? options)
        {
            if (options is null)
                return Array.Empty<string>();
            var ids = new List<string>();
            foreach (var option in options)
                ids.Add(option.OptionId());
            return ids.ToArray();
        }

        public static bool IsKnown(string optionId)
        {
            return optionId switch
            {
                "system_9d607a4f-60c3-4347-94fc-f24b538bf468" => true,
                "system_4c670ac9-78a4-44e9-9833-94e1c69dca97" => true,
                "system_a0aeb200-7216-49e2-aad2-e151ff35c336" => true,
                _ => false,
            };
        }

        public static string TextId(this NeoSpriteMaskInteraction value)
        {
            return value switch
            {
                NeoSpriteMaskInteraction.None => "None",
                NeoSpriteMaskInteraction.VisibleInsideMask => "Visible inside mask",
                NeoSpriteMaskInteraction.VisibleOutsideMask => "Visible outside mask",
                _ => value.OptionId(),
            };
        }

        public static string Text(this NeoSpriteMaskInteraction value, NeoClient? client = null)
        {
            return client is null ? value.TextId() : client.Localization.ResolveText(value.TextId());
        }
    }

    /// <summary>
    /// Unity interop for <see cref="NeoSpriteMaskInteraction"/>.
    ///
    /// It lives beside the options class rather than in it because that body
    /// has to stay byte-identical to what codegen would emit — an SDK-only
    /// member there would be a body the generator never writes, and the next
    /// person to compare the two would have no way to tell which differences
    /// were deliberate.
    /// </summary>
    public static class NeoSpriteMaskInteractions
    {
        /// <summary>
        /// The Unity enum an authored option maps onto. A renderer holding
        /// <see cref="INeoSpriteObjectValue.MaskInteraction"/>'s option id
        /// converts it with
        /// <see cref="NeoSpriteMaskInteractionOptions.FromOptionId"/> first.
        /// </summary>
        public static SpriteMaskInteraction ToUnity(NeoSpriteMaskInteraction value)
        {
            return value switch
            {
                NeoSpriteMaskInteraction.None => SpriteMaskInteraction.None,
                NeoSpriteMaskInteraction.VisibleInsideMask => SpriteMaskInteraction.VisibleInsideMask,
                NeoSpriteMaskInteraction.VisibleOutsideMask => SpriteMaskInteraction.VisibleOutsideMask,
                _ => throw new ArgumentException($"Unrecognized sprite MaskInteraction option id '{value.OptionId()}'.", nameof(value)),
            };
        }
    }

}
