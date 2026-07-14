// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("7755a905-f2a1-4e5d-8b60-78cbdd2b2042")]
public partial class PlanetVisit
{
    [NeoMember("04d5e145-412b-4499-a253-b03496a065f0")]
    public virtual Planet World { get; init; }

    [NeoMember("14d38a73-5732-4348-8fde-e81554a1a497")]
    public virtual int DateUnix { get; init; }
}
