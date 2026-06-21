// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("fb219343-34db-4e82-baf0-09df9a2b5210", Hidden = true, ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""sortingLayer""}}")]
public sealed class NeoSortingLayer
{
    [NeoString("e963862e-6228-4594-8220-8ef06098c253", Locked = true, Localizable = false, DefaultJson = @"{""value"":""""}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public string Name { get; init; }
}
