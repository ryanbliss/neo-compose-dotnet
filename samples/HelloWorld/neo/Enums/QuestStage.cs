// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoEnum("a5c2a3f4-f91a-4878-bec0-a38b432ad762")]
public enum QuestStage
{
    arrival,
    ended,
    endgame,
    followTheWakes,
    threePaths,
    vaultOpen,
}
