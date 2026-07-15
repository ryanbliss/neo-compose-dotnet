// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("b737f725-5a4a-4d33-8bc5-c6953dbeff77")]
public partial class ConsoleObjectLayer : NeoObjectLayer
{
    [NeoMember("248753df-f9b6-445a-b3c6-b12957f99ee2")]
    [NeoText(Localizable = false)]
    public virtual string DisplayName { get; init; } = default!;
}
