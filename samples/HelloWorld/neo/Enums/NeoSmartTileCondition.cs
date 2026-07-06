// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoEnum("ba63d042-51c1-4bce-be70-d9848e6bb240", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
public enum NeoSmartTileCondition
{
    [NeoEnumEntry(Text = "ce735446-bbb5-4f10-ab06-5f43e7be9ff0")]
    InheritsFromType,
    [NeoEnumEntry(Text = "d305c446-c6f3-4f07-83d4-8388d6695a4c")]
    NotInheritsFromType,
    [NeoEnumEntry(Text = "7dcfeadc-d448-478d-82ae-33ebbdd62c7a")]
    NotThis,
    [NeoEnumEntry(Text = "7a6d716c-1c9f-45c5-8f15-29d87beb162b")]
    This,
}
