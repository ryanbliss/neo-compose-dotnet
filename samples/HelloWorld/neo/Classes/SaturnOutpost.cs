// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("96818dab-90e5-4ab9-8f69-cce66e39e370")]
public partial class SaturnOutpost : Outpost
{
    // NeoScript: Scripts/SaturnOutpost/FullDisplayText.neo
    [NeoMember("33e86f10-7989-4d82-9156-631a19e3bf06")]
    [NeoComputed]
    public override string FullDisplayText { get; }

    [NeoMember("a804bead-3d5b-4c68-a733-94e85e1e79b6")]
    public virtual SaturnMoon Moon { get; init; }

    [NeoMember("08cc78f5-3f90-43cc-89d7-9ec8d58e8dd0")]
    public override Planet Planet { get; init; } = Planet.saturn;
}
