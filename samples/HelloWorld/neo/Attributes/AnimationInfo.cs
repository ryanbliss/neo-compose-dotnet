// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("11177bd5-0678-4bff-86b8-46718ff1827b")]
public partial class AnimationInfo
{
    [NeoMember("87ec59c5-a157-4670-9cb9-794487cc79d3")]
    [NeoNumber(Min = 1)]
    public virtual int FPS { get; init; } = 30;

    [NeoMember("cc7fec3f-593b-4888-bf93-b9bb6bcb5e44")]
    [NeoList]
    [NeoEntries(nameof(AnimationInfo.FramesEntries))]
    public virtual IReadOnlyList<NeoSprite> Frames { get; init; } = default!;

    private static IReadOnlyList<NeoEntrySettings> FramesEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "d17d421c-b2e8-4956-baf5-ab174d1f4cb0",
            Path = "$",
            Kind = NeoEntryKind.Sprite,
            Required = true,
            Virtual = true,
            File = new()
            {
                Kind = NeoFileKind.Sprite,
            },
        },
    };

    [NeoMember("8b8ac389-c9a7-4a0c-8335-2352865ee1b4")]
    public virtual string Name { get; init; } = "5c90af78-a25e-462a-b5c3-8082e5080037";
}
