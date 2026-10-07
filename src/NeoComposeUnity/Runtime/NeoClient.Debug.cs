// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using System;
namespace NeoCompose.Runtime
{
    public partial class NeoClient
    {
        /// <summary>Default sink for new script executions. Null disables output.</summary>
        public Action<NeoScript.NeoScriptDebugEvent>? ScriptDebugSink { get; set; } = NeoScript.NeoScriptDebug.UnitySink;
    }
}
