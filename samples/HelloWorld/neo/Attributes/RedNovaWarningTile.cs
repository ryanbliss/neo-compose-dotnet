// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("d931c907-19cd-4f3d-b04a-e6f1945fb216")]
public sealed class RedNovaWarningTile : ConsoleTile
{
    [NeoLookup("0e5e27bb-20e3-4081-8e4b-f32b682881e0", ExtendsId = "68221f3d-e17e-40c0-a5a1-34ca571b5cd7", CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", CollectionValueId = "0893eb05-41c5-40cb-a9d6-8397982519d4", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588121""]}")]
    public IReadOnlyList<NeoLookupRef>? CompatibleLayers { get; init; }

    [NeoLookup("4f6da61d-0f3d-4b69-980b-460fc951b32f", ExtendsId = "376b91a0-62b1-4642-a0f0-d0df5322838c", CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", CollectionValueId = "0893eb05-41c5-40cb-a9d6-8397982519d4", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588121""]}")]
    public NeoLookupRef DefaultLayer { get; init; }

    [NeoString("1462765c-6a91-4576-acc7-0fb47c242f1b", ExtendsId = "3b02422f-1ef2-4a50-8386-155d5001082b", Localizable = false, DefaultJson = @"{""value"":""Red Nova Warning""}")]
    public string Name { get; init; }

    [NeoSprite("5c877f60-4f89-41fc-ae54-c724b1733860", ExtendsId = "cbd6db9a-f473-44b5-b913-7cdc06452f35", DefaultJson = @"{""value"":{""fileId"":""666675d5-8a39-41c1-ba2c-18f014bf03f5"",""sliceIndex"":0}}")]
    public NeoSpriteValue Sprite { get; init; }
}
