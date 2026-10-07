// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
using NeoCompose.Runtime.NeoScript;
namespace NeoCompose.Runtime.Json
{
    public sealed class NeoScriptSourceInfo
    {
        public string name = "<body>";
        public string? uri;
        public NeoScriptCoordinateSpace coordinateSpace;
    }
    public sealed class NeoScriptSourcePosition
    {
        public int line;
        public int column;
    }
}
