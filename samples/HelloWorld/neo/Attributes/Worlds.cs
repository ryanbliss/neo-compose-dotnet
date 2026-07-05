// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("22a62498-61f8-4b6f-8d4c-bc05743a5c2e")]
public sealed class Worlds
{
    [NeoObject("f5252031-8220-49be-bfc3-b717d5679ca8")]
    public OldConsoleLandingGrid OldConsoleLanding { get; init; }
}
