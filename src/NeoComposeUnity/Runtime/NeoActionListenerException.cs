// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Thrown at the subscribing line when a value handed to
    /// <c>NeoAction.AddListener</c> / <c>RemoveListener</c> (or to the
    /// <c>+=</c> / <c>-=</c> operators) cannot be resolved to a Neo member
    /// target (P62 §5.2). A listener is always data — a generated
    /// <c>Function</c> / <c>NSFunction</c> method group or a Neo-obtained
    /// delegate — because every subscription is a durable member-value write
    /// and a native lambda has no persisted representation.
    /// </summary>
    public sealed class NeoActionListenerException : InvalidOperationException
    {
        public NeoActionListenerException(string message)
            : base(message)
        {
        }
    }
}
