// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("27874300-3e78-4d1c-802b-caf34d25d1ab")]
public sealed class RecoveryCacheObject : ConsoleObject
{
    [NeoList("32be897f-be96-4ab7-a586-a9a6fdfff8b7", ExtendsId = "bb5d2cf1-a0dd-4eba-a62e-0e1bf0177071", Locked = true, EntryChainJson = @"[{""customTypeId"":""ec21a2ec-cb95-4e10-9c7d-5ba7e4cdea88"",""defaultValue"":{""value"":{}},""id"":""b3280478-f039-47a2-aa18-918175818bcb"",""locked"":true,""name"":""Child"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[""4d4d8a22-92e0-457d-8e86-0a41c6193259""]}")]
    public IReadOnlyList<NeoObjectBase> Children { get; init; }

    [NeoLookup("16778506-5859-42ac-a233-da915bc170d6", ExtendsId = "5915099d-fc2e-4f4a-875c-dad704472d05", CollectionId = "7fb51db7-60c7-4064-bcde-6938acea4fe8", CollectionValueId = "aa467eba-bc17-4cc6-933d-4c539caba2ad", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588123""]}")]
    public IReadOnlyList<NeoLookupRef>? CompatibleLayers { get; init; }

    [NeoLookup("e37c6500-370f-44a5-b78d-b4f68a22ae5e", ExtendsId = "cf6e9aa2-dd4b-4673-a83b-5a15e617eb9a", CollectionId = "7fb51db7-60c7-4064-bcde-6938acea4fe8", CollectionValueId = "aa467eba-bc17-4cc6-933d-4c539caba2ad", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588123""]}")]
    public NeoLookupRef DefaultLayer { get; init; }

    [NeoString("345b54f2-eb9c-4cd9-8c5f-cee868c9602d", ExtendsId = "3b02422f-1ef2-4a50-8386-155d5001082b", Localizable = false, DefaultJson = @"{""value"":""Recovery Cache""}")]
    public string Name { get; init; }

    [NeoList("d8e9ad0e-157f-4709-96a7-8775efa3dd11", ExtendsId = "5a9ca1f5-a21b-4d4e-8c55-00341af594b4", Locked = true, EntryChainJson = @"[{""customTypeId"":""38c4b25a-e2a5-4c33-87ca-84bd4cb7cae6"",""defaultValue"":{""value"":{}},""id"":""c2bf0c92-1d24-4950-bea3-37d5f195728d"",""locked"":true,""name"":""PlacementTile"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[""9404babf-786a-4e74-aec4-9c6667485278""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoObjectPlacementTile> PlacementTiles { get; init; }

    [NeoDialogueLookup("47466be3-368c-4ac1-8c0e-7a825af6b538", DefaultJson = @"{""value"":[""cb0ac79c-f3b4-4c96-b968-8c4173c1f712""]}")]
    public NeoDialogueLookupRef RecoveryCache { get; init; }
}
