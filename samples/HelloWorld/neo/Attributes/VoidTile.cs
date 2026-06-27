// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("48bcbba5-57c0-40fd-8df8-35f4e7ce73f2")]
public sealed class VoidTile : ConsoleTile
{
    [NeoLookup("bb63d9bd-1571-4ef4-9453-e453db64ff41", ExtendsId = "68221f3d-e17e-40c0-a5a1-34ca571b5cd7", CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", CollectionValueId = "0893eb05-41c5-40cb-a9d6-8397982519d4", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588121""]}", ExtraJson = @"{""system"":null}")]
    public IReadOnlyList<NeoLookupRef>? CompatibleLayers { get; init; }

    [NeoLookup("bdd71ed6-7f39-4089-8375-fa5909835f00", ExtendsId = "376b91a0-62b1-4642-a0f0-d0df5322838c", CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", CollectionValueId = "0893eb05-41c5-40cb-a9d6-8397982519d4", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588121""]}", ExtraJson = @"{""system"":null}")]
    public NeoLookupRef DefaultLayer { get; init; }

    [NeoString("106c7fee-6a3a-472b-baed-5b5aeb99c280", ExtendsId = "3b02422f-1ef2-4a50-8386-155d5001082b", Localizable = false, DefaultJson = @"{""value"":""Void""}")]
    public string Name { get; init; }

    [NeoSprite("c95da9bc-2193-4bd0-998f-a2f9f98862ee", ExtendsId = "cbd6db9a-f473-44b5-b913-7cdc06452f35", DefaultJson = @"{""value"":{""fileId"":""355390cf-4ce9-410d-af90-25273ae4bd3b"",""sliceIndex"":0}}")]
    public NeoSpriteValue Sprite { get; init; }
}
