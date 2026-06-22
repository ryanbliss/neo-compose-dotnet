// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("7ed188f9-484b-4692-840d-436acf1635aa", Hidden = true, ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""objectCollider""}}")]
public abstract class NeoCollider
{
    [NeoVector2("c66c17e3-ecfc-4f41-96de-6a53bb2acd4f", Name = "Size", Locked = true, DefaultJson = @"{""value"":{""x"":1,""y"":1}}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoVector2 size { get; init; }

    [NeoVector2("ec5b5d5e-7053-4378-87d1-9cceec30ff1a", Name = "Offset", Locked = true, DefaultJson = @"{""value"":{""x"":0,""y"":0}}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoVector2? offset { get; init; }

    [NeoBool("6ed1890b-0ae6-4979-bf5b-6f216d69f432", Name = "Is Trigger", Locked = true, DefaultJson = @"{""value"":false}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public bool? isTrigger { get; init; }
}
