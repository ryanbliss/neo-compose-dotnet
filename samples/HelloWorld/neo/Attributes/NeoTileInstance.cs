// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("3ab13327-ccaa-4dbe-8ec7-24b9592ddf15", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""tileInstance""}}")]
public sealed class NeoTileInstance
{
    [NeoVector2Int("f4be2707-74f5-4833-9784-e81bb2474330", Locked = true, DefaultJson = @"{""value"":{""x"":0,""y"":0}}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoVector2Int Cell { get; init; }

    [NeoLookup("cffc3a6e-5fed-4a15-aa89-cb61d512b7a1", Locked = true, CollectionId = "56831afd-18d8-418d-9bcf-c76c770592c4", DefaultJson = @"{""value"":null}", ExtraJson = @"{""collectionValueId"":null,""listKind"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoLookupRef Tile { get; init; }
}
