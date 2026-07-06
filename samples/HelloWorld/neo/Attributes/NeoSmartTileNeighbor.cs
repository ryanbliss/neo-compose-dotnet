// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("628e0cce-5472-4bec-addd-71230b8e64a6", ExtraJson = @"{""extendsTypeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""smartTileNeighbor""}}")]
public sealed class NeoSmartTileNeighbor
{
    [NeoVector2Int("7202cfb6-9cc4-49d3-b3fb-e44a23915b40", Locked = true, DefaultJson = @"{""value"":{""x"":0,""y"":0}}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoVector2Int Cell { get; init; }

    [NeoEnum("496b0bb9-0375-4ca8-b768-0a1bdf88a158", Locked = true, DefaultJson = @"{""value"":[""This""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoSmartTileCondition Condition { get; init; }

    [NeoLookup("2b2bb88e-6817-463b-a5d5-73145531016e", Locked = true, CollectionId = "56831afd-18d8-418d-9bcf-c76c770592c4", DefaultJson = @"{""value"":null}", ExtraJson = @"{""collectionValueId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoLookupRef? Tile { get; init; }
}
