// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("a8305a31-7f6c-4ff5-8a9f-5871ef451093")]
public sealed class HullPlatingTile : ConsoleTile
{
    [NeoLookup("e8568eb4-ae16-4888-8b8e-0f52268c4b11", ExtendsId = "68221f3d-e17e-40c0-a5a1-34ca571b5cd7", CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", CollectionValueId = "0893eb05-41c5-40cb-a9d6-8397982519d4", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588122""]}", ExtraJson = @"{""system"":null}")]
    public IReadOnlyList<NeoLookupRef>? CompatibleLayers { get; init; }

    [NeoLookup("5572bd04-b0be-434b-bde9-6ddf77fed61e", ExtendsId = "376b91a0-62b1-4642-a0f0-d0df5322838c", CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", CollectionValueId = "0893eb05-41c5-40cb-a9d6-8397982519d4", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588122""]}", ExtraJson = @"{""system"":null}")]
    public NeoLookupRef DefaultLayer { get; init; }

    [NeoString("08a564d0-4924-457f-8901-46f636e789b5", ExtendsId = "3b02422f-1ef2-4a50-8386-155d5001082b", Localizable = false, DefaultJson = @"{""value"":""Hull Plating""}")]
    public string Name { get; init; }

    [NeoSprite("14750f2f-0ecf-424f-9386-8fd34eca9310", ExtendsId = "cbd6db9a-f473-44b5-b913-7cdc06452f35", DefaultJson = @"{""value"":{""fileId"":""ad8b2628-2e12-4c8a-90e5-b4334b430b6e"",""sliceIndex"":0}}")]
    public NeoSpriteValue Sprite { get; init; }
}
