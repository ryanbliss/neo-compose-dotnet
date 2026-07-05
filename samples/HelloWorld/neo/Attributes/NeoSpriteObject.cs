// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("7cb1b706-95d2-4465-8e75-c82a6b7d8830", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""spriteObject""}}")]
public sealed class NeoSpriteObject : NeoObjectBase
{
    [NeoString("441cb790-a45f-4488-a5a9-6f375af6c369", Locked = true, Localizable = false, DefaultJson = @"{""value"":""""}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public string Name { get; init; }

    [NeoSprite("e9288ba9-f5a2-4485-8443-6afb155b31e0", Locked = true, DefaultJson = @"null", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""},""templateId"":null}")]
    public NeoSpriteValue Sprite { get; init; }
}
