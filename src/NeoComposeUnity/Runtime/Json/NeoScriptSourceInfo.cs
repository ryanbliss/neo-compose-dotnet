// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#nullable enable
namespace NeoCompose.Runtime.Json
{
    public sealed class NeoScriptSourceInfo
    {
        public string name = "<body>";
        public string? uri;
        public string coordinateSpace = "body";
    }
    public sealed class NeoScriptSourcePosition
    {
        public int line;
        public int column;
    }
}
