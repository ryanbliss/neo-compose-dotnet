// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("9a6019b6-680f-4300-8cea-bde6fce47fc1", Hidden = true)]
public partial class Art
{
    [NeoMember("8d27045c-c3a0-44ae-9095-499e2f9779a7")]
    [NeoFile(NeoFileKind.Sprite)]
    public virtual NeoSprite FirstWorldIconSprite { get; init; } = default!;

    [NeoMember("f6c2157b-c0f2-4d6d-b16c-412675070862")]
    public virtual AnimationInfo? FlareAnimation { get; init; }

    [NeoMember("13e587b4-f143-419e-b2f1-a5fb2fef92fd")]
    [NeoFile(NeoFileKind.Sprite)]
    public virtual NeoSprite FlareStaticSprite { get; init; } = default!;

    [NeoMember("dca14d32-7e42-4db4-8174-de9f3798a5be")]
    [NeoFile(NeoFileKind.Sprite)]
    public virtual NeoSprite JupiterSprite { get; init; } = default!;

    [NeoMember("8042556b-586e-4f79-b358-80924968a7b8")]
    [NeoFile(NeoFileKind.Sprite)]
    public virtual NeoSprite SaturnSprite { get; init; } = default!;

    [NeoMember("7a3f6a94-a649-4b75-8776-eb623f55fe1b")]
    public virtual AnimationInfo? ShipAnimation { get; init; }

    [NeoMember("7d53ed57-05e7-47f9-a805-f7917b77dc55")]
    [NeoFile(NeoFileKind.Sprite)]
    public virtual NeoSprite ShipSprite { get; init; } = default!;

    [NeoMember("dfa0872f-9f3e-4083-8244-9f6d0fa88f8b")]
    [NeoFile(NeoFileKind.Sprite)]
    public virtual NeoSprite SunSprite { get; init; } = default!;

    [NeoMember("20d4dfe4-935c-441d-b2c2-ca8052c5a96e")]
    [NeoFile(NeoFileKind.Sprite)]
    public virtual NeoSprite VaultPlaqueSprite { get; init; } = default!;
}
