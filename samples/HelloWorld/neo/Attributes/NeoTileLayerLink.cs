// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("e78cfcd2-78ae-4656-9f04-6429bb0efe20", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""tileLayerLink""}}")]
public sealed class NeoTileLayerLink : NeoObjectBase
{
    [NeoLookup("325dba0e-5967-4e18-937e-5c6800b68abc", Locked = true, CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", DefaultJson = @"null", ExtraJson = @"{""collectionValueId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoLookupRef TileLayer { get; init; }

    [NeoList("98655d2b-ad0b-45e2-a901-62600b4d3a22", Locked = true, EntryChainJson = @"[{""customTypeId"":""38c4b25a-e2a5-4c33-87ca-84bd4cb7cae6"",""defaultValue"":{""value"":{}},""id"":""04383910-20a1-4c7f-ad90-f87e165083ba"",""locked"":true,""name"":""Tile"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoObjectPlacementTile> Tiles { get; init; }
}
