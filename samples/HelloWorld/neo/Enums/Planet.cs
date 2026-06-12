// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoEnum("576de38f-6c77-440d-91b3-95a19aad5e23")]
public enum Planet
{
    [NeoEnumEntry(Text = "165b49a1-0b48-4e99-beec-079dc0531484")]
    earth,
    [NeoEnumEntry(Text = "dae6ab0c-4b82-4a11-8057-097a193fecea")]
    jupiter,
    [NeoEnumEntry(Text = "92fe9254-9e61-40c6-aced-6f55692b0120")]
    mars,
    [NeoEnumEntry(Text = "4e5a1262-ba35-4bf6-a68a-45b27479143d")]
    mercury,
    [NeoEnumEntry(Text = "1ad90a45-a7f6-4e11-9524-5f6d05fe5ab6")]
    neptune,
    [NeoEnumEntry(Text = "ee2f7b0b-786c-41ca-8578-6f348194d52c")]
    pluto,
    [NeoEnumEntry(Text = "1f81373c-bdb0-47ab-87a8-283c45f1a686")]
    saturn,
    [NeoEnumEntry(Text = "fafce114-ab70-4813-a4e2-cb436b66aba6")]
    uranus,
    [NeoEnumEntry(Text = "fe5ab262-4a0f-4551-ab27-5fd1c97da128")]
    venus,
}
