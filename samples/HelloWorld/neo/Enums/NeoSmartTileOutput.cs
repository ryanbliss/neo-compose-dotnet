// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoEnum("551025ad-e441-4d43-90b0-2821c6235786", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
public enum NeoSmartTileOutput
{
    [NeoEnumEntry(Text = "d6191a4c-6b7b-4e86-a1ed-adbb43f2752c")]
    Animation,
    [NeoEnumEntry(Text = "37e58009-597f-4075-8f84-793a5e63cb44")]
    Random,
    [NeoEnumEntry(Text = "35370b82-4269-4fdf-b768-c268edbab60e")]
    Single,
}
