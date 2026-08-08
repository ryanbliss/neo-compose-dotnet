// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Thrown by a generated action property setter when the assigned value
    /// is not the member's own live <c>NeoAction</c> instance (P62 §5.2). The
    /// setter exists only so <c>+=</c> / <c>-=</c> compile — C# rewrites them
    /// to get → <c>operator+</c> → set, and the operator returns the same
    /// instance, which makes the setter an identity no-op. Anything else is a
    /// reassignment, and an action's listener set is subscribed to, never
    /// replaced.
    /// </summary>
    public sealed class NeoActionReassignmentException : InvalidOperationException
    {
        public NeoActionReassignmentException(string message)
            : base(message)
        {
        }
    }
}
