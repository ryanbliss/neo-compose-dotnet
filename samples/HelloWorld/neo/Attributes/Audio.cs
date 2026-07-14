// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("44d6324f-6507-4420-a919-3496681c3b21", Hidden = true)]
public partial class Audio
{
    [NeoMember("421de440-ad72-4f9f-aef0-e3def8cd0582")]
    [NeoFile(NeoFileKind.Audio)]
    public virtual NeoAudio BitsGainSfx { get; init; } = default!;

    [NeoMember("d30300c6-4fca-4fa4-be07-65ccc5ebc427")]
    [NeoFile(NeoFileKind.Audio)]
    public virtual NeoAudio BitsSpendSfx { get; init; } = default!;

    [NeoMember("e6bdf8bd-bb3c-4827-b14c-24173d6b75d1")]
    [NeoFile(NeoFileKind.Audio)]
    public virtual NeoAudio DialogCloseSfx { get; init; } = default!;

    [NeoMember("9862c2d2-92ad-48d6-8ed3-a1e4e3e8c35e")]
    [NeoFile(NeoFileKind.Audio)]
    public virtual NeoAudio DialogNextSfx { get; init; } = default!;

    [NeoMember("e1f13e5c-3d16-476e-9fa4-c120959a4d32")]
    [NeoFile(NeoFileKind.Audio)]
    public virtual NeoAudio DialogOpenSfx { get; init; } = default!;

    [NeoMember("a44ed1c2-3296-40e1-b9ef-7924ec97cb21")]
    [NeoFile(NeoFileKind.Audio)]
    public virtual NeoAudio ItemGetSfx { get; init; } = default!;

    [NeoMember("46284af1-9326-40f3-94c5-491484099f70")]
    [NeoFile(NeoFileKind.Audio)]
    public virtual NeoAudio RocketThrustSfx { get; init; } = default!;
}
