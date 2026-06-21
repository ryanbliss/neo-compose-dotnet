// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("63b261c9-46f2-4d37-84e9-06f16af04e58", Hidden = true, ExtraJson = @"{""extendsTypeId"":null,""system"":null}")]
public sealed class Session
{
    [NeoVector3("1c7de3b5-6474-483c-aef5-fc48265199b0", DefaultJson = @"{""value"":{""x"":0,""y"":0,""z"":0}}", ExtraJson = @"{""extendsAttributeId"":null,""storage"":null,""system"":null}")]
    public NeoVector3 Position { get; init; }
}
