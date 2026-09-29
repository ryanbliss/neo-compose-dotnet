// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    /// <summary>
    /// A NeoScript temporary handed to C# before it has become rows. Generated
    /// classes wrap one in a view that reads its members directly and turns it
    /// into rows only when something needs a row: its id, a write, a
    /// subscription.
    /// </summary>
    public abstract class NeoDetachedValue
    {
        private protected NeoDetachedValue()
        {
        }
    }
}
