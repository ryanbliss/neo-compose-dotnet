// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("b737f725-5a4a-4d33-8bc5-c6953dbeff77")]
public sealed class ConsoleObjectLayer : NeoObjectLayer
{
    [NeoString("248753df-f9b6-445a-b3c6-b12957f99ee2", Localizable = false)]
    public string DisplayName { get; init; }
}
