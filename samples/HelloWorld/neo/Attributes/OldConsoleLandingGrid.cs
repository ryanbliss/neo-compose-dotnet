// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("b44d80a9-7760-4919-8844-0cb71d08b788")]
public sealed class OldConsoleLandingGrid : NeoTileGrid
{
    [NeoString("f7493cd5-3da1-44ef-a275-5997d765b640", Localizable = false)]
    public string DisplayName { get; init; }

    [NeoLookup("2193c5a4-cca1-4cd1-b079-62b83c1664e8", Locked = true, CollectionId = "7fb51db7-60c7-4064-bcde-6938acea4fe8", CollectionValueId = "aa467eba-bc17-4cc6-933d-4c539caba2ad", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588123""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoLookupRef> ObjectLayers { get; init; }

    [NeoLookup("2faf47b8-cf59-4b51-91bc-ae4babe5d4b2", Locked = true, CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", CollectionValueId = "0893eb05-41c5-40cb-a9d6-8397982519d4", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588121""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoLookupRef> TileLayers { get; init; }
}
