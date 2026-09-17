// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;

namespace NeoCompose.Runtime
{
    public sealed class NeoPlacementValidationException : InvalidOperationException
    {
        public string ErrorCode { get; }
        public NeoPlacementValidationException(string errorCode, string message) : base(message) =>
            ErrorCode = errorCode;
    }
}
