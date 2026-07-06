// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("720c1361-de9e-4c12-b90e-bb6ac9e1ce8b")]
public sealed class SealBarrierTile : ConsoleTile
{
    [NeoLookup("daaf5b5f-4191-4e22-95e5-d10c3bc264cf", ExtendsId = "68221f3d-e17e-40c0-a5a1-34ca571b5cd7", CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", CollectionValueId = "0893eb05-41c5-40cb-a9d6-8397982519d4", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588122""]}", ExtraJson = @"{""system"":null}")]
    public IReadOnlyList<NeoLookupRef>? CompatibleLayers { get; init; }

    [NeoLookup("fb8dc50f-20de-461a-9d27-97fe9f8eb5f2", ExtendsId = "376b91a0-62b1-4642-a0f0-d0df5322838c", CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", CollectionValueId = "0893eb05-41c5-40cb-a9d6-8397982519d4", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588122""]}", ExtraJson = @"{""system"":null}")]
    public NeoLookupRef DefaultLayer { get; init; }

    [NeoString("95ec9093-fb16-487e-b897-89ac9f60c426", ExtendsId = "3b02422f-1ef2-4a50-8386-155d5001082b", Localizable = false, DefaultJson = @"{""value"":""Seal Barrier""}")]
    public string Name { get; init; }

    [NeoSprite("9de921e3-007d-4ca7-aa65-699c5b92a8f1", ExtendsId = "cbd6db9a-f473-44b5-b913-7cdc06452f35", DefaultJson = @"{""value"":{""fileId"":""18da2470-d75e-4634-97ce-7ea8bd26b743"",""sliceIndex"":0}}")]
    public NeoSpriteValue Sprite { get; init; }
}
