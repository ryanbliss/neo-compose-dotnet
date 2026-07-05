// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("bda4cf72-c8da-4be0-8148-024d0fc2d826")]
public sealed class GlassFloorTile : ConsoleTile
{
    [NeoLookup("e0e00d1b-0ee3-4102-9d83-34b3038c573f", ExtendsId = "68221f3d-e17e-40c0-a5a1-34ca571b5cd7", CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", CollectionValueId = "0893eb05-41c5-40cb-a9d6-8397982519d4", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588121""]}")]
    public IReadOnlyList<NeoLookupRef>? CompatibleLayers { get; init; }

    [NeoLookup("b562d0ae-f799-4364-b90c-b4f026c7d870", ExtendsId = "376b91a0-62b1-4642-a0f0-d0df5322838c", CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", CollectionValueId = "0893eb05-41c5-40cb-a9d6-8397982519d4", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588121""]}")]
    public NeoLookupRef DefaultLayer { get; init; }

    [NeoString("04100351-2919-4795-8aa6-fadd571a6036", ExtendsId = "3b02422f-1ef2-4a50-8386-155d5001082b", Localizable = false, DefaultJson = @"{""value"":""Glass Floor""}")]
    public string Name { get; init; }

    [NeoSprite("deabb583-d797-4c3e-bb07-3bafbb1e84b3", ExtendsId = "cbd6db9a-f473-44b5-b913-7cdc06452f35", DefaultJson = @"{""value"":{""fileId"":""acf20f9d-cd05-4205-a449-a0c21dcd4e12"",""sliceIndex"":0}}")]
    public NeoSpriteValue Sprite { get; init; }
}
