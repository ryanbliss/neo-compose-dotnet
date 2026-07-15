// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("a50efb7e-58f6-4342-906e-0b01f98b15af")]
public partial class JupiterOutpost : Outpost
{
    // NeoScript: Scripts/JupiterOutpost/FullDisplayText.neo
    [NeoMember("48c7bc64-fc1a-41bc-becb-4d24da7df9aa")]
    [NeoComputed]
    public override string FullDisplayText { get; }

    #pragma warning disable CS8764, CS8765
    [NeoMember("6133e24a-bf87-4d3a-a71e-3933eebcab25")]
    [NeoFile(NeoFileKind.Sprite, Template = typeof(_16x16PivotCenter))]
    public override NeoSprite? Image { get; init; } = Neo.Sprite("6e1643e2-5b91-45a6-a4b1-435f36c1a0f9", 0);
    #pragma warning restore CS8764, CS8765

    [NeoMember("c91791ff-8b41-47b2-ba12-81a84e595f42")]
    public virtual JupiterMoon Moon { get; init; }

    [NeoMember("54c74f99-f949-44f6-bc1c-37a80888e32f")]
    public override Planet Planet { get; init; } = Planet.jupiter;
}
