// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("96818dab-90e5-4ab9-8f69-cce66e39e370", ExtraJson = @"{""system"":null}")]
public sealed class SaturnOutpost : Outpost
{
    [NeoProperty("33e86f10-7989-4d82-9156-631a19e3bf06", ExtendsId = "514a79c5-dd23-4ede-9fc8-b07b3c866fe5", Code = @"	return $""{this.Name}, {this.Moon}, {this.Planet}"";", RetJson = @"{""required"":true,""type"":3}", ExtraJson = @"{""system"":null}")]
    public object? FullDisplayText { get; init; }

    [NeoEnum("a804bead-3d5b-4c68-a733-94e85e1e79b6", DefaultJson = @"null", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public SaturnMoon Moon { get; init; }

    [NeoEnum("08cc78f5-3f90-43cc-89d7-9ec8d58e8dd0", ExtendsId = "3166fd08-4bdb-4df9-b100-ecccec859443", DefaultJson = @"{""value"":[""saturn""]}", ExtraJson = @"{""system"":null}")]
    public Planet Planet { get; init; }
}
