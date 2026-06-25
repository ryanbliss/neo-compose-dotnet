// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("1968a188-8220-4d1a-99b2-f1d0cea0c802", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""tile""}}")]
public abstract class NeoTile
{
    [NeoString("3b02422f-1ef2-4a50-8386-155d5001082b", Locked = true, Localizable = false, DefaultJson = @"{""value"":""""}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public string Name { get; init; }

    [NeoSprite("cbd6db9a-f473-44b5-b913-7cdc06452f35", Locked = true, ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoSpriteValue Sprite { get; init; }

    [NeoLookup("376b91a0-62b1-4642-a0f0-d0df5322838c", Locked = true, CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", DefaultJson = @"null", ExtraJson = @"{""collectionValueId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoLookupRef DefaultLayer { get; init; }

    [NeoLookup("68221f3d-e17e-40c0-a5a1-34ca571b5cd7", Locked = true, CollectionId = "5161fb81-7254-4e41-b153-25138b8e9e74", DefaultJson = @"{""value"":null}", ExtraJson = @"{""collectionValueId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoLookupRef>? CompatibleLayers { get; init; }
}
