// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Thrown when generated C# code invokes a Neo Compose Function
    /// member before a developer-provided FunctionHandler has been
    /// attached to the generated value wrapper.
    /// </summary>
    public sealed class NeoFunctionHandlerMissingException : InvalidOperationException
    {
        public NeoFunctionHandlerMissingException(string message)
            : base(message)
        {
        }
    }
}
