// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoEnum("9d3ef3f4-0823-412b-a2b6-97874739072d")]
public enum SaturnMoon
{
    [NeoEnumOption("titan", Text = "c59bdeaf-9f71-4970-a95f-058c7f54c18b")]
    titan,
    [NeoEnumOption("enceladus", Text = "543a1e52-c9ca-470a-ad07-884ee0c55cc2")]
    enceladus,
}
