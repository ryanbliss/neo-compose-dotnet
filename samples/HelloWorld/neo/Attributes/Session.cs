// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("63b261c9-46f2-4d37-84e9-06f16af04e58", Hidden = true, ExtraJson = @"{""extendsTypeId"":null,""system"":null}")]
public sealed class Session
{
    [NeoBool("8da60967-116a-4062-9cfb-e9d6a052914d", DefaultJson = @"{""value"":false}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public bool Foo { get; init; }
}
