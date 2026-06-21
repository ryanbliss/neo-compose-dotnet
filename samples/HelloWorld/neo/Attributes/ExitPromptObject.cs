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
}
