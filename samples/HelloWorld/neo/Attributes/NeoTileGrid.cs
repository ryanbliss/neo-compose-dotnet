// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("e93a83cb-9bc2-46fe-9c85-d70465a89da8", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""tileGrid""}}")]
public abstract class NeoTileGrid
{
    [NeoVector3("3b523230-a851-4ab5-a6f2-c2d0745c116f", Name = "Cell size", Locked = true, DefaultJson = @"{""value"":{""x"":1,""y"":1,""z"":0}}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoVector3 CellSize { get; init; }

    [NeoInt("8ece74e4-e17e-4e56-9ef6-8dc2bc9f59f0", Locked = true, DefaultJson = @"{""value"":100}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public int PixelsPerUnit { get; init; }

    [NeoLookup("711149d9-1c0d-4e36-af29-245a6ff2bc67", Locked = true, CollectionId = "56831afd-18d8-418d-9bcf-c76c770592c4", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""collectionValueId"":null,""listKind"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoLookupRef> Tiles { get; init; }

    [NeoLookup("2faf47b8-cf59-4b51-91bc-ae4babe5d4b2", Locked = true, CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""collectionValueId"":null,""listKind"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoLookupRef> TileLayers { get; init; }

    [NeoLookup("cddb5d5d-04cf-4c61-b9df-e46bfdabe3a5", Locked = true, CollectionId = "61d22e83-1799-485b-a1a7-51b6b85e7ba8", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""collectionValueId"":null,""listKind"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoLookupRef> Objects { get; init; }

    [NeoLookup("2193c5a4-cca1-4cd1-b079-62b83c1664e8", Locked = true, CollectionId = "7fb51db7-60c7-4064-bcde-6938acea4fe8", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""collectionValueId"":null,""listKind"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoLookupRef> ObjectLayers { get; init; }

    [NeoList("98578ba3-a70e-4397-9283-996a898d44c8", Locked = true, EntryChainJson = @"[{""customTypeId"":""6d069010-c47c-4744-89af-243f4448f537"",""defaultValue"":{""value"":{}},""id"":""f1d89b43-7de6-4d50-9614-342bcdf85531"",""locked"":true,""name"":""LayerGroup"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""listKind"":null,""storageKey"":""world:$parentType"",""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoLayerGroupBase> Children { get; init; }
}
