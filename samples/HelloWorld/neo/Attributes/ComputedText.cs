// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("2ab1bc07-da0b-47fc-b77b-54cc511575bb")]
public partial class ComputedText
{
    [NeoMember("29659610-fc70-4b9c-833e-a6185f745c04")]
    public virtual string baseText { get; init; } = "5ed5d8c1-a01d-47ff-b2ca-d663be283d79";

    #pragma warning disable CS8618
    [NeoMember("acf7a92c-9ede-4a0d-a00c-c8c64e7a9b80")]
    [NeoComputed]
    public virtual string fullText { get; }
    #pragma warning restore CS8618

    [NeoMember("d56d641e-4f15-4da6-8c1f-114016c9166d")]
    public virtual string? optionalSuffix { get; init; }
}
