// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("7755a905-f2a1-4e5d-8b60-78cbdd2b2042", ExtraJson = @"{""extendsTypeId"":null,""system"":null}")]
public sealed class PlanetVisit
{
    [NeoEnum("04d5e145-412b-4499-a253-b03496a065f0", ExtraJson = @"{""extendsAttributeId"":null}")]
    public Planet World { get; init; }

    [NeoInt("14d38a73-5732-4348-8fde-e81554a1a497", ExtraJson = @"{""extendsAttributeId"":null}")]
    public int DateUnix { get; init; }
}
