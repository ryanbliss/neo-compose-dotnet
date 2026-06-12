// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoEnum("9d3ef3f4-0823-412b-a2b6-97874739072d")]
public enum SaturnMoon
{
    [NeoEnumEntry(Text = "543a1e52-c9ca-470a-ad07-884ee0c55cc2")]
    enceladus,
    [NeoEnumEntry(Text = "c59bdeaf-9f71-4970-a95f-058c7f54c18b")]
    titan,
}
