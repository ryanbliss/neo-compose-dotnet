// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    public enum NeoValueOwnership
    {
        Asset,
        Save,
        Session,
        /// <summary>The player's user file, shared across saves (P104).</summary>
        User,
    }
}
