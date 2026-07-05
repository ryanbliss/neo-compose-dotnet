// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("38c4b25a-e2a5-4c33-87ca-84bd4cb7cae6", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""objectPlacementTile""}}")]
public sealed class NeoObjectPlacementTile
{
    [NeoVector2Int("b0b3c45c-a87d-4218-b056-7418ef46aac5", Locked = true, DefaultJson = @"{""value"":{""x"":0,""y"":0}}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoVector2Int Cell { get; init; }
}
