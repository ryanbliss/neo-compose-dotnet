// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("22a62498-61f8-4b6f-8d4c-bc05743a5c2e")]
public partial class Worlds
{
    [NeoMember("f5252031-8220-49be-bfc3-b717d5679ca8")]
    public virtual OldConsoleLandingGrid OldConsoleLanding { get; init; } = default!;
}
