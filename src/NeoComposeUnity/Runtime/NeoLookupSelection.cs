// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    public readonly struct NeoLookupSelection
    {
        public string valueId { get; }

        public NeoLookupSelection(string valueId)
        {
            this.valueId = valueId;
        }

        public static implicit operator string(NeoLookupSelection selection) =>
            selection.valueId;
    }
}
