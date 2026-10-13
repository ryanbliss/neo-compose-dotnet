// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Thrown when a C# type argument has no NeoScript form, or when a value
    /// a generic function returns isn't its type argument (P110 §7).
    /// </summary>
    public sealed class NeoTypeArgumentException : InvalidOperationException
    {
        public NeoTypeArgumentException(string message)
            : base(message)
        {
        }
    }
}
