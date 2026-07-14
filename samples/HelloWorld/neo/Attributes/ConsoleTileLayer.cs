// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("fbbd7a13-2b2a-4d0c-bd8f-78b5474cd4ba")]
public partial class ConsoleTileLayer : NeoTileLayer
{
    [NeoMember("679e3c47-e6eb-4808-bcc5-2f8440612626")]
    [NeoText(Localizable = false)]
    public virtual string DisplayName { get; init; } = default!;
}
