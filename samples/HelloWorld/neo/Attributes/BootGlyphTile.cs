// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("07db44f3-8cc5-4164-aace-098ca68460f4")]
public sealed class BootGlyphTile : ConsoleTile
{
    [NeoLookup("6f734d02-4752-4697-9995-1bfa748e0938", ExtendsId = "68221f3d-e17e-40c0-a5a1-34ca571b5cd7", CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", CollectionValueId = "0893eb05-41c5-40cb-a9d6-8397982519d4", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588121""]}")]
    public IReadOnlyList<NeoLookupRef>? CompatibleLayers { get; init; }

    [NeoLookup("0cea4b79-8614-4753-a319-858a749fd2b3", ExtendsId = "376b91a0-62b1-4642-a0f0-d0df5322838c", CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", CollectionValueId = "0893eb05-41c5-40cb-a9d6-8397982519d4", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588121""]}")]
    public NeoLookupRef DefaultLayer { get; init; }

    [NeoString("f703feff-bc27-46d5-a9b9-d29df959342a", ExtendsId = "3b02422f-1ef2-4a50-8386-155d5001082b", Localizable = false, DefaultJson = @"{""value"":""Boot Glyph""}")]
    public string Name { get; init; }

    [NeoSprite("948a6ed7-d7bf-4ffb-b123-06955293681c", ExtendsId = "cbd6db9a-f473-44b5-b913-7cdc06452f35", DefaultJson = @"{""value"":{""fileId"":""2c68221a-2a3c-45d4-8565-c5c23c0654d3"",""sliceIndex"":0}}")]
    public NeoSpriteValue Sprite { get; init; }
}
