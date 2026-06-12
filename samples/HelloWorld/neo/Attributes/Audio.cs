// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("44d6324f-6507-4420-a919-3496681c3b21", Hidden = true)]
public sealed class Audio
{
    [NeoAudio("421de440-ad72-4f9f-aef0-e3def8cd0582")]
    public NeoAudioValue BitsGainSfx { get; init; }

    [NeoAudio("d30300c6-4fca-4fa4-be07-65ccc5ebc427")]
    public NeoAudioValue BitsSpendSfx { get; init; }

    [NeoAudio("e6bdf8bd-bb3c-4827-b14c-24173d6b75d1")]
    public NeoAudioValue DialogCloseSfx { get; init; }

    [NeoAudio("9862c2d2-92ad-48d6-8ed3-a1e4e3e8c35e")]
    public NeoAudioValue DialogNextSfx { get; init; }

    [NeoAudio("e1f13e5c-3d16-476e-9fa4-c120959a4d32")]
    public NeoAudioValue DialogOpenSfx { get; init; }

    [NeoAudio("a44ed1c2-3296-40e1-b9ef-7924ec97cb21")]
    public NeoAudioValue ItemGetSfx { get; init; }

    [NeoAudio("46284af1-9326-40f3-94c5-491484099f70")]
    public NeoAudioValue RocketThrustSfx { get; init; }
}
