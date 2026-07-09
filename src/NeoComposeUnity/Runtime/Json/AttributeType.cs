// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

namespace NeoCompose.Runtime.Json
{
    /// <summary>
    /// Mirrors the TS-side numeric <c>AttributeType</c> enum from
    /// <c>src/models/attributes/attribute-types.ts</c>. Order must match
    /// the TS declaration so the on-the-wire integer values deserialize
    /// to the right member.
    /// </summary>
    public enum AttributeType
    {
        Null = 0,
        Bool = 1,
        Int = 2,
        String = 3,
        Float = 4,
        Dictionary = 5,
        List = 6,
        Custom = 7,
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
        /// Generic parameter placeholder (specs/custom-type-generics.md
        /// Decision 5). Value-bearing only after substitution — resolution
        /// replaces it with the terminal binding attribute before any node
        /// is constructed (see <c>NeoGenericResolution.SubstituteAttribute</c>).
        /// </summary>
        Generic = 21,
        Unknown = -1,
        Void = -2,
    }
}
