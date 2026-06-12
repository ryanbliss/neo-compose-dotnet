// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoEnum("a2d44a04-35a6-4529-8fd2-f54a55cf518b")]
public enum JupiterMoon
{
    [NeoEnumEntry(Text = "c9ec2854-89aa-4a36-bbd8-ece671852b77")]
    callisto,
    [NeoEnumEntry(Text = "134542ba-7414-4353-b0bd-5773e31d63bf")]
    europa,
    [NeoEnumEntry(Text = "15a60d3e-940d-4ac4-b6ba-ac1403dcfea2")]
    ganymede,
    [NeoEnumEntry(Text = "03d962a1-c763-4589-89bd-f0db811c9b95")]
    io,
}
