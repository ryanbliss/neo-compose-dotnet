// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("3415d063-b5b9-4cfb-a77d-ba6ebb5890e2", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""worldAssets""}}")]
public abstract class NeoWorlds
{
    [NeoList("56831afd-18d8-418d-9bcf-c76c770592c4", Locked = true, EntryChainJson = @"[{""customTypeId"":""1968a188-8220-4d1a-99b2-f1d0cea0c802"",""defaultValue"":{""value"":{}},""id"":""2835bbe7-ba93-4bf4-bf63-90561770b5e0"",""locked"":true,""name"":""Tile"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoTile> Tiles { get; init; }

    [NeoList("5161fb81-7254-4e41-b153-25138b8e9e74", Locked = true, EntryChainJson = @"[{""customTypeId"":""a3e4a1df-f487-44bc-bf90-2edc4a88f1ad"",""defaultValue"":{""value"":{}},""id"":""6760f916-5df1-41af-8300-e466adaa397b"",""locked"":true,""name"":""TileLayer"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588121""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoTileLayer> TileLayers { get; init; }

    [NeoList("61d22e83-1799-485b-a1a7-51b6b85e7ba8", Locked = true, EntryChainJson = @"[{""customTypeId"":""4c2aa621-e662-419f-a5b5-d8dcfab9b29b"",""defaultValue"":{""value"":{}},""id"":""7593513d-7aa5-427a-a51b-a7d44f04a5d7"",""locked"":true,""name"":""Object"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoObject> Objects { get; init; }

    [NeoList("7fb51db7-60c7-4064-bcde-6938acea4fe8", Locked = true, EntryChainJson = @"[{""customTypeId"":""c1dc3dd7-397a-4f8f-acbf-b928cc66076d"",""defaultValue"":{""value"":{}},""id"":""d28efda6-7366-4ac1-a4ef-27c443f70586"",""locked"":true,""name"":""ObjectLayer"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588123""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoObjectLayer> ObjectLayers { get; init; }
}
