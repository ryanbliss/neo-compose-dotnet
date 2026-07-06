// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("47a1f7dd-b16d-4f04-96f8-6c0199d18c7b", AllowedStorage = "save", ExtraJson = @"{""system"":null}")]
public sealed class BlockedPath : NeoTileLayerLink
{
    [NeoDialogueLookup("e0316e12-b70d-4d4a-9c16-1861ce394849", DefaultJson = @"{""value"":[""2a49e84a-ab1f-4468-a9a3-f29796cbf086""]}")]
    public NeoDialogueLookupRef BootGlyphSealLocked { get; init; }

    [NeoDialogueLookup("0659d085-0267-4d6e-8e18-b726f393f740", DefaultJson = @"{""value"":[""d755935f-4c3a-4d43-8c40-4ba3f7d28063""]}")]
    public NeoDialogueLookupRef BootGlyphSealReady { get; init; }

    [NeoFunction("0fa12fa0-9e74-4e85-9ce3-df0efe78d2dd", RetJson = @"{""required"":true,""type"":1}", ArgsJson = @"[]", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public NeoFunctionValue? ClearPath { get; init; }
}
