// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("89b38b34-c040-4e69-8707-487f1484a056", ExtraJson = @"{""extendsTypeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""smartTile""}}")]
public sealed class NeoSmartTile
{
    [NeoEnum("0a51bdef-4b3e-49d6-913b-11cbea98bced", Locked = true, DefaultJson = @"{""value"":[""Sprite""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoSmartTileCollider DefaultCollider { get; init; }

    [NeoList("97cb9d95-2d54-4809-ae88-0b0ba7859248", Locked = true, EntryChainJson = @"[{""customTypeId"":""d500e920-87d9-4804-affa-1bd8fc5e91ae"",""defaultValue"":{""value"":{}},""id"":""fa146cb3-b0d5-4a87-a781-918b78307b20"",""locked"":true,""name"":""Rule"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoSmartTileRule> Rules { get; init; }
}
