// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoEnum("576de38f-6c77-440d-91b3-95a19aad5e23")]
public enum Planet
{
    [NeoEnumOption("mercury", Text = "4e5a1262-ba35-4bf6-a68a-45b27479143d")]
    mercury,
    [NeoEnumOption("venus", Text = "fe5ab262-4a0f-4551-ab27-5fd1c97da128")]
    venus,
    [NeoEnumOption("earth", Text = "165b49a1-0b48-4e99-beec-079dc0531484")]
    earth,
    [NeoEnumOption("mars", Text = "92fe9254-9e61-40c6-aced-6f55692b0120")]
    mars,
    [NeoEnumOption("jupiter", Text = "dae6ab0c-4b82-4a11-8057-097a193fecea")]
    jupiter,
    [NeoEnumOption("saturn", Text = "1f81373c-bdb0-47ab-87a8-283c45f1a686")]
    saturn,
    [NeoEnumOption("uranus", Text = "fafce114-ab70-4813-a4e2-cb436b66aba6")]
    uranus,
    [NeoEnumOption("neptune", Text = "1ad90a45-a7f6-4e11-9524-5f6d05fe5ab6")]
    neptune,
    [NeoEnumOption("pluto", Text = "ee2f7b0b-786c-41ca-8578-6f348194d52c")]
    pluto,
}
