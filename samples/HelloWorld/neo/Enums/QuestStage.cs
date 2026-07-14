// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoEnum("a5c2a3f4-f91a-4878-bec0-a38b432ad762")]
public enum QuestStage
{
    [NeoEnumOption("arrival")]
    arrival,
    [NeoEnumOption("ended")]
    ended,
    [NeoEnumOption("endgame")]
    endgame,
    [NeoEnumOption("followTheWakes")]
    followTheWakes,
    [NeoEnumOption("threePaths")]
    threePaths,
    [NeoEnumOption("vaultOpen")]
    vaultOpen,
}
