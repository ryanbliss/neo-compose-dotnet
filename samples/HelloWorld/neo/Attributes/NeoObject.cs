// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("4c2aa621-e662-419f-a5b5-d8dcfab9b29b", Hidden = true, ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""object""}}")]
public abstract class NeoObject : NeoObjectBase
{
    [NeoLookup("cf6e9aa2-dd4b-4673-a83b-5a15e617eb9a", Locked = true, CollectionId = "7fb51db7-60c7-4064-bcde-6938acea4fe8", CollectionValueId = "aa467eba-bc17-4cc6-933d-4c539caba2ad", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588123""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoLookupRef DefaultLayer { get; init; }

    [NeoLookup("5915099d-fc2e-4f4a-875c-dad704472d05", Locked = true, CollectionId = "7fb51db7-60c7-4064-bcde-6938acea4fe8", CollectionValueId = "aa467eba-bc17-4cc6-933d-4c539caba2ad", DefaultJson = @"{""value"":null}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoLookupRef>? CompatibleLayers { get; init; }

    [NeoString("3b02422f-1ef2-4a50-8386-155d5001082b", Locked = true, Localizable = false, DefaultJson = @"{""value"":""""}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public string Name { get; init; }

    [NeoList("5a9ca1f5-a21b-4d4e-8c55-00341af594b4", Locked = true, EntryChainJson = @"[{""customTypeId"":""38c4b25a-e2a5-4c33-87ca-84bd4cb7cae6"",""defaultValue"":{""value"":{}},""id"":""c2bf0c92-1d24-4950-bea3-37d5f195728d"",""locked"":true,""name"":""PlacementTile"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoObjectPlacementTile> PlacementTiles { get; init; }

    [NeoList("bb5d2cf1-a0dd-4eba-a62e-0e1bf0177071", Locked = true, EntryChainJson = @"[{""customTypeId"":""ec21a2ec-cb95-4e10-9c7d-5ba7e4cdea88"",""defaultValue"":{""value"":{}},""id"":""b3280478-f039-47a2-aa18-918175818bcb"",""locked"":true,""name"":""Child"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoObjectBase> Children { get; init; }

    [NeoObject("a0083c92-72f7-405f-8863-ff86f995d36d", Locked = true, DefaultJson = @"{""value"":null}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoCollider? Collider { get; init; }
}
