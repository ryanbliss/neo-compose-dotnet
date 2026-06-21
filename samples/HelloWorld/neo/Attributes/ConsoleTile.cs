// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("1c859b34-ff59-40f8-a763-cd0f04dc96c0")]
public abstract class ConsoleTile : NeoTile
{
    [NeoString("d611cb73-fc9b-44e8-a3d9-5784253cae6f", ExtendsId = "3b02422f-1ef2-4a50-8386-155d5001082b", Localizable = false, DefaultJson = @"{""value"":""ConsoleTile""}")]
    public string Name { get; init; }
}
