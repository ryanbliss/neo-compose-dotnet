// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("a50efb7e-58f6-4342-906e-0b01f98b15af", ExtraJson = @"{""system"":null}")]
public sealed class JupiterOutpost : Outpost
{
    [NeoGetter("48c7bc64-fc1a-41bc-becb-4d24da7df9aa", ExtendsId = "514a79c5-dd23-4ede-9fc8-b07b3c866fe5", Code = @"	return $""{this.Name}, {this.Moon}, {this.Planet}"";", RetJson = @"{""required"":true,""type"":3}", ExtraJson = @"{""system"":null}")]
    public object? FullDisplayText { get; init; }

    [NeoSprite("6133e24a-bf87-4d3a-a71e-3933eebcab25", ExtendsId = "7ce8a389-265c-4ad4-90f4-42c3e91e7648", TemplateId = "66504747-cbd5-4026-9d4c-89a0644f8192", DefaultJson = @"{""value"":{""fileId"":""6e1643e2-5b91-45a6-a4b1-435f36c1a0f9"",""sliceIndex"":0}}", ExtraJson = @"{""system"":null}")]
    public NeoSpriteValue? Image { get; init; }

    [NeoEnum("c91791ff-8b41-47b2-ba12-81a84e595f42", DefaultJson = @"null", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public JupiterMoon Moon { get; init; }

    [NeoEnum("54c74f99-f949-44f6-bc1c-37a80888e32f", ExtendsId = "3166fd08-4bdb-4df9-b100-ecccec859443", DefaultJson = @"{""value"":[""jupiter""]}", ExtraJson = @"{""system"":null}")]
    public Planet Planet { get; init; }
}
