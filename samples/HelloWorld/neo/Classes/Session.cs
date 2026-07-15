// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("63b261c9-46f2-4d37-84e9-06f16af04e58", Hidden = true)]
public partial class Session
{
    [NeoMember("1c7de3b5-6474-483c-aef5-fc48265199b0")]
    public virtual NeoVector3 Position { get; init; } = new(0, 0, 0);
}
