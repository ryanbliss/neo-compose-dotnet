// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("9a6019b6-680f-4300-8cea-bde6fce47fc1", Hidden = true)]
public sealed class Art
{
    [NeoSprite("8d27045c-c3a0-44ae-9095-499e2f9779a7")]
    public NeoSpriteValue FirstWorldIconSprite { get; init; }

    [NeoObject("f6c2157b-c0f2-4d6d-b16c-412675070862", DefaultJson = @"null")]
    public AnimationInfo? FlareAnimation { get; init; }

    [NeoSprite("13e587b4-f143-419e-b2f1-a5fb2fef92fd")]
    public NeoSpriteValue FlareStaticSprite { get; init; }

    [NeoObject("7a3f6a94-a649-4b75-8776-eb623f55fe1b", DefaultJson = @"null")]
    public AnimationInfo? ShipAnimation { get; init; }

    [NeoSprite("7d53ed57-05e7-47f9-a805-f7917b77dc55")]
    public NeoSpriteValue ShipSprite { get; init; }

    [NeoSprite("20d4dfe4-935c-441d-b2c2-ca8052c5a96e")]
    public NeoSpriteValue VaultPlaqueSprite { get; init; }
}
