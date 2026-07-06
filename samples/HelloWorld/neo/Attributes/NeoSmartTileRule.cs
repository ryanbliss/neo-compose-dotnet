// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("d500e920-87d9-4804-affa-1bd8fc5e91ae", ExtraJson = @"{""extendsTypeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""smartTileRule""}}")]
public sealed class NeoSmartTileRule
{
    [NeoList("8bfadaa8-14e9-4488-a103-ee688c1cc9c4", Locked = true, EntryChainJson = @"[{""customTypeId"":""628e0cce-5472-4bec-addd-71230b8e64a6"",""defaultValue"":{""value"":{}},""id"":""11873fad-7426-46cf-97d6-47b45bd1c091"",""locked"":true,""name"":""Neighbor"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""type"":7}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<NeoSmartTileNeighbor> Neighbors { get; init; }

    [NeoList("fd3a7f0f-cff8-4069-9b19-004015a6aca1", Locked = true, EntryChainJson = @"[{""defaultValue"":null,""id"":""2cd93779-d755-4368-b064-2361463526ea"",""locked"":true,""name"":""Sprite"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""templateId"":null,""type"":11}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public IReadOnlyList<object> Sprites { get; init; }

    [NeoEnum("9b22d8df-ea41-4c76-9d2e-a6c64c95a64a", Locked = true, DefaultJson = @"{""value"":[""Single""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoSmartTileOutput Output { get; init; }

    [NeoEnum("f8106137-0cde-4be0-bce7-3db5cf40257a", Locked = true, DefaultJson = @"{""value"":[""Sprite""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoSmartTileCollider Collider { get; init; }

    [NeoFloat("3111ed6f-d441-40b9-97f0-25fff2fb9838", Locked = true, Min = 0, DefaultJson = @"{""value"":1}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public float MinAnimationSpeed { get; init; }

    [NeoFloat("9ae9cf8b-4ea1-413e-8c7a-dcabe1a5cc98", Locked = true, Min = 0, DefaultJson = @"{""value"":1}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public float MaxAnimationSpeed { get; init; }

    [NeoEnum("29a3d2a7-ce2e-4d40-bcdf-ee6c314023fe", Locked = true, DefaultJson = @"{""value"":[""Fixed""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoSmartTileTransform RuleTransform { get; init; }
}
