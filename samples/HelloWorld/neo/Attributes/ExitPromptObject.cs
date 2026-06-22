// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("5c65b156-e83a-41c5-bef0-ee375798bdc2")]
public sealed class ExitPromptObject : ConsoleObject
{
    [NeoLookup("f05dfa96-f35a-440c-832d-b4462cb2f30a", ExtendsId = "5915099d-fc2e-4f4a-875c-dad704472d05", CollectionId = "7fb51db7-60c7-4064-bcde-6938acea4fe8", CollectionValueId = "aa467eba-bc17-4cc6-933d-4c539caba2ad", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588123""]}")]
    public IReadOnlyList<NeoLookupRef>? CompatibleLayers { get; init; }

    [NeoLookup("ff033c90-8bd5-4c57-929c-0af97005b9d3", ExtendsId = "cf6e9aa2-dd4b-4673-a83b-5a15e617eb9a", CollectionId = "7fb51db7-60c7-4064-bcde-6938acea4fe8", CollectionValueId = "aa467eba-bc17-4cc6-933d-4c539caba2ad", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588123""]}")]
    public NeoLookupRef DefaultLayer { get; init; }

    [NeoString("14ec578e-0aa7-4d12-8d02-47463e03a1f3", ExtendsId = "3b02422f-1ef2-4a50-8386-155d5001082b", Localizable = false, DefaultJson = @"{""value"":""Exit Prompt""}")]
    public string Name { get; init; }

    [NeoList("de93c887-ea29-49bd-bfea-a6255b8b9a54", ExtendsId = "bb5d2cf1-a0dd-4eba-a62e-0e1bf0177071", Locked = true, DefaultJson = @"{""value"":[""6dbdfc93-1071-4e62-85f4-eb6d0cc33f73"",""4ca6b71b-2a7e-4f99-977d-1bcdb2556d9e""]}")]
    public IReadOnlyList<NeoObjectBase> Children { get; init; }

    [NeoVector3("8e8c5ddf-6273-4440-869e-f1f9ca5dc51b", ExtendsId = "e1d820d8-56b1-43ac-aa10-0a019f0dc38f", Locked = true, DefaultJson = @"{""value"":{""x"":2,""y"":1,""z"":0}}", ExtraJson = @"{""storage"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoVector3 Size { get; init; }

    [NeoList("571a0e0b-b36c-45f3-ae9a-5fde39045c11", ExtendsId = "5a9ca1f5-a21b-4d4e-8c55-00341af594b4", Locked = true, EntryChainJson = @"[{""customTypeId"":""38c4b25a-e2a5-4c33-87ca-84bd4cb7cae6"",""defaultValue"":{""value"":{}},""id"":""c2bf0c92-1d24-4950-bea3-37d5f195728d"",""locked"":true,""name"":""PlacementTile"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[""96cad433-4aed-4a9c-be34-fee3ebca8402"",""8ba3f179-5171-4b77-99de-a8beec7bab25""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoObjectPlacementTile> PlacementTiles { get; init; }
}
