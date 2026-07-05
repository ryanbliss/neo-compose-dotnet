// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("47a1f7dd-b16d-4f04-96f8-6c0199d18c7b", AllowedStorage = "save", ExtraJson = @"{""system"":null}")]
public sealed class BlockedPath : NeoTileLayerLink
{
    [NeoFunction("0fa12fa0-9e74-4e85-9ce3-df0efe78d2dd", RetJson = @"{""required"":true,""type"":1}", ArgsJson = @"[]", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public NeoFunctionValue? ClearPath { get; init; }
}
