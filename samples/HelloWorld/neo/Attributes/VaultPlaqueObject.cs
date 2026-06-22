// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("cacf06dd-db1d-4f48-99c7-f3cea5a6961f")]
public sealed class VaultPlaqueObject : ConsoleObject
{
    [NeoLookup("b98218ae-934e-4359-a9cd-4ca82433dc51", ExtendsId = "5915099d-fc2e-4f4a-875c-dad704472d05", CollectionId = "7fb51db7-60c7-4064-bcde-6938acea4fe8", CollectionValueId = "aa467eba-bc17-4cc6-933d-4c539caba2ad", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588123""]}")]
    public IReadOnlyList<NeoLookupRef>? CompatibleLayers { get; init; }

    [NeoLookup("71bca16c-04c7-46ad-884b-002a1914bee8", ExtendsId = "cf6e9aa2-dd4b-4673-a83b-5a15e617eb9a", CollectionId = "7fb51db7-60c7-4064-bcde-6938acea4fe8", CollectionValueId = "aa467eba-bc17-4cc6-933d-4c539caba2ad", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588123""]}")]
    public NeoLookupRef DefaultLayer { get; init; }

    [NeoString("9bcfd9a8-ef4a-4a89-b6d4-446c9baf01d7", ExtendsId = "3b02422f-1ef2-4a50-8386-155d5001082b", Localizable = false, DefaultJson = @"{""value"":""Vault Plaque""}")]
    public string Name { get; init; }

    [NeoList("c481dc1e-b5fd-4352-a76b-536cc3e17f71", ExtendsId = "bb5d2cf1-a0dd-4eba-a62e-0e1bf0177071", Locked = true, DefaultJson = @"{""value"":[""522f0109-97d2-4ac1-ad7c-70357d278035""]}")]
    public IReadOnlyList<NeoObjectBase> Children { get; init; }

    [NeoList("430fca56-b45a-4896-9ab2-795a3faf57f6", ExtendsId = "5a9ca1f5-a21b-4d4e-8c55-00341af594b4", Locked = true, EntryChainJson = @"[{""customTypeId"":""38c4b25a-e2a5-4c33-87ca-84bd4cb7cae6"",""defaultValue"":{""value"":{}},""id"":""c2bf0c92-1d24-4950-bea3-37d5f195728d"",""locked"":true,""name"":""PlacementTile"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[""293652f8-54d6-4168-89be-7ac75207dcd0""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoObjectPlacementTile> PlacementTiles { get; init; }
}
