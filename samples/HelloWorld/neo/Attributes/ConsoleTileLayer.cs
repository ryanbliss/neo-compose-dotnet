// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("fbbd7a13-2b2a-4d0c-bd8f-78b5474cd4ba")]
public sealed class ConsoleTileLayer : NeoTileLayer
{
    [NeoString("679e3c47-e6eb-4808-bcc5-2f8440612626", Localizable = false)]
    public string DisplayName { get; init; }
}
