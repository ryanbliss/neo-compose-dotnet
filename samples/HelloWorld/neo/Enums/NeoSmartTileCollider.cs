// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoEnum("6ff1b376-1b30-449d-8439-d822be95cec1", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
public enum NeoSmartTileCollider
{
    [NeoEnumEntry(Text = "0465f25d-046c-404a-b642-0bc9a52ca208")]
    Grid,
    [NeoEnumEntry(Text = "63c4fc38-4be9-4fdb-925c-5b7d3942fa21")]
    None,
    [NeoEnumEntry(Text = "9c77cfbc-049e-44c9-8dae-144ad8758c1b")]
    Sprite,
}
