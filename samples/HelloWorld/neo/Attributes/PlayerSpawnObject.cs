// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("7d9647b1-df4d-4cb6-9f4d-7d80fe381f2f")]
public sealed class PlayerSpawnObject : ConsoleObject
{
    [NeoLookup("6ae49766-2186-48b8-b63e-62768cb3e88b", ExtendsId = "5915099d-fc2e-4f4a-875c-dad704472d05", CollectionId = "7fb51db7-60c7-4064-bcde-6938acea4fe8", CollectionValueId = "aa467eba-bc17-4cc6-933d-4c539caba2ad", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588123""]}")]
    public IReadOnlyList<NeoLookupRef>? CompatibleLayers { get; init; }

    [NeoLookup("ee30bd7d-5ba2-4a83-9fbe-38ee6b53d7ca", ExtendsId = "cf6e9aa2-dd4b-4673-a83b-5a15e617eb9a", CollectionId = "7fb51db7-60c7-4064-bcde-6938acea4fe8", CollectionValueId = "aa467eba-bc17-4cc6-933d-4c539caba2ad", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588123""]}")]
    public NeoLookupRef DefaultLayer { get; init; }

    [NeoString("1994e574-7fcd-4c5f-8abe-f3e807bd334d", ExtendsId = "3b02422f-1ef2-4a50-8386-155d5001082b", Localizable = false, DefaultJson = @"{""value"":""Player Spawn""}")]
    public string Name { get; init; }

    [NeoList("35275ba0-4a3f-4b83-8b09-fccb7bd7a515", ExtendsId = "bb5d2cf1-a0dd-4eba-a62e-0e1bf0177071", Locked = true, EntryChainJson = @"[{""customTypeId"":""ec21a2ec-cb95-4e10-9c7d-5ba7e4cdea88"",""defaultValue"":{""value"":{}},""id"":""b3280478-f039-47a2-aa18-918175818bcb"",""locked"":true,""name"":""Child"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[""d0d48343-8748-40bf-b35a-3e88cdd7e3a5""]}")]
    public IReadOnlyList<NeoObjectBase> Children { get; init; }

    [NeoList("47c21aa5-e852-41d2-882c-b4f555aee9dd", ExtendsId = "5a9ca1f5-a21b-4d4e-8c55-00341af594b4", Locked = true, EntryChainJson = @"[{""customTypeId"":""38c4b25a-e2a5-4c33-87ca-84bd4cb7cae6"",""defaultValue"":{""value"":{}},""id"":""c2bf0c92-1d24-4950-bea3-37d5f195728d"",""locked"":true,""name"":""PlacementTile"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[""6de63821-b102-4b62-aac6-c99c8aabecc9""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoObjectPlacementTile> PlacementTiles { get; init; }

    [NeoObject("e5f5125c-fe5c-46b4-9589-9c6ae6fcba19", ExtendsId = "a0083c92-72f7-405f-8863-ff86f995d36d", Locked = true, DefaultJson = @"{""typeId"":""7ed188f9-484b-4692-840d-436acf1635aa"",""value"":{""isTrigger"":""38170df8-0175-40a0-87f4-10145984914d"",""offset"":""4cc9752c-508e-43f8-9589-18d25679e8c0"",""size"":""ee73946a-62a2-4a20-8403-22a50101a38c""}}", ExtraJson = @"{""storage"":null,""system"":null}")]
    public NeoCollider? Collider { get; init; }

    [NeoVector3("2b10e854-d60a-40a7-bc4c-8aede3e5049e", ExtendsId = "7fc41bde-418a-4507-8c4b-9b75d7012125", Locked = true, Storage = "session")]
    public NeoVector3 Position { get; init; }
}
