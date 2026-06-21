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
}
