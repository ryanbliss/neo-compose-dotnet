// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("ec21a2ec-cb95-4e10-9c7d-5ba7e4cdea88", Hidden = true, ExtraJson = @"{""extendsTypeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""objectBase""}}")]
public abstract class NeoObjectBase
{
    [NeoString("b21bfd01-1234-4f49-ab6b-889f829cb148", Locked = true, Localizable = false, DefaultJson = @"null", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public string Name { get; init; }

    [NeoVector3("7fc41bde-418a-4507-8c4b-9b75d7012125", Locked = true, DefaultJson = @"{""value"":{""x"":0,""y"":0,""z"":0}}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoVector3 Position { get; init; }

    [NeoVector3("e1d820d8-56b1-43ac-aa10-0a019f0dc38f", Locked = true, DefaultJson = @"{""value"":{""x"":1,""y"":1,""z"":0}}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoVector3 Size { get; init; }
}
