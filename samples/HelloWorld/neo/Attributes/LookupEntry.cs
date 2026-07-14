// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("9296e4be-bd27-44e3-9823-77fbeaa60665")]
public partial class LookupEntry
{
    [NeoMember("29563228-5f16-44e1-bb3e-89f2097fd3cb")]
    public virtual string Name { get; init; } = "ede61345-580d-4c04-a061-3c789175566d";
}
