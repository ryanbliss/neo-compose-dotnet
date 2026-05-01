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
        NSGetter = 10,
    }
}
