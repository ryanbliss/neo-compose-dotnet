// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("8ccfe860-309f-428b-b74c-76a873bdea8a")]
public partial class OutpostSaveData
{
    [NeoMember("5a4d8d10-9fef-4197-a7a1-1dc1b112677b")]
    public virtual bool Unlocked { get; init; } = false;

    // NeoScript: Scripts/OutpostSaveData/Visited.neo
    [NeoMember("0c02e45d-bb5b-44c3-8b48-91fa75171ff2")]
    [NeoComputed]
    public virtual bool Visited { get; }

    [NeoMember("68cd6fe1-7683-4c67-8030-acd6334f77a2")]
    [NeoNumber(Min = 0)]
    public virtual int VisitCount { get; init; } = 0;

    [NeoMember("1d1d14bc-987e-4079-a8f8-09998d5954fc")]
    public virtual int Reputation { get; init; } = 0;
}
