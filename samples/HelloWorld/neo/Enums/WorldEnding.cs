// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoEnum("d0b44688-28ab-44ba-b2a1-5cdad0f51e50")]
public enum WorldEnding
{
    [NeoEnumOption("commentOut")]
    commentOut,
    [NeoEnumOption("goodbyeWorld")]
    goodbyeWorld,
    [NeoEnumOption("helloWorld")]
    helloWorld,
    [NeoEnumOption("none")]
    none,
    [NeoEnumOption("secondSun")]
    secondSun,
}
