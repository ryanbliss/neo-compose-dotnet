// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("f1b08825-2ad0-4666-acf1-3df7ffbda64e", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""objectLayerLink""}}")]
public sealed class NeoObjectLayerLink : NeoLayerGroupBase
{
    [NeoLookup("9cc0ab67-e138-4d11-8011-fab7d7a75b13", Locked = true, CollectionId = "7fb51db7-60c7-4064-bcde-6938acea4fe8", CollectionValueId = "aa467eba-bc17-4cc6-933d-4c539caba2ad", DefaultJson = @"{""value"":[""8f96912d-5bbb-428c-84eb-8932ef588123""]}", ExtraJson = @"{""listKind"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoLookupRef ObjectLayer { get; init; }

    [NeoList("f8e217b1-da89-4819-9c8d-e9c9da2bdfb2", Locked = true, EntryChainJson = @"[{""customTypeId"":""ec21a2ec-cb95-4e10-9c7d-5ba7e4cdea88"",""defaultValue"":{""value"":{}},""id"":""934a2525-f95d-4c09-9504-b71da30b9186"",""locked"":true,""name"":""Object"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""listKind"":""unordered"",""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoObjectBase> Objects { get; init; }
}
