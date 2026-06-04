// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    public sealed class NeoSaveOptions
    {
        public bool DiagnosticsEnabled { get; set; } = true;
        public NeoClient.BuildSaveName? BuildSaveName { get; set; }
    }
}
