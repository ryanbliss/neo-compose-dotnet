// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Mirrors the TS-side numeric <c>MemberKind</c> enum from
    /// <c>src/models/members/member-kinds.ts</c>. Order must match
    /// the TS declaration so the on-the-wire integer values deserialize
    /// to the right member.
    /// </summary>
    public enum MemberKind
    {
        Null = 0,
        Bool = 1,
        Int = 2,
        String = 3,
        Float = 4,
        Dictionary = 5,
        List = 6,
        Class = 7,
        Enum = 8,
        Lookup = 9,
        NSProperty = 10,
        Sprite = 11,
        Audio = 12,
        Function = 13,
        Vector2 = 14,
        Vector2Int = 15,
        Vector3 = 16,
        Vector3Int = 17,
        DialogueLookup = 18,
        Color = 19,
        Decimal = 20,
        /// <summary>
        /// Generic parameter placeholder (specs/class-generics.md
        /// Decision 5). Value-bearing only after substitution — resolution
        /// replaces it with the terminal binding member before any node
        /// is constructed (see <c>NeoGenericResolution.SubstituteMember</c>).
        /// </summary>
        Generic = 21,
        /// <summary>
        /// Class interface reference. This is a class-info-only discriminator;
        /// it is never the kind of a persisted member record.
        /// </summary>
        Interface = 22,
        /// <summary>
        /// Mutation-capable NeoScript function with an authored signature and
        /// server-compiled action body.
        /// </summary>
        NSFunction = 23,
        /// <summary>
        /// Reference to a declared native or NeoScript function. The stored
        /// payload is an object containing <c>functionMemberId</c>.
        /// </summary>
        FunctionRef = 24,
        Unknown = -1,
        Void = -2,
    }
}
