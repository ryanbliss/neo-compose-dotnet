// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("60c25a92-cb01-46f7-b5cf-c9d950586116")]
public partial class Item
{
    [NeoMember("66e49f29-58cb-4ac7-b128-93febd0f0fb1")]
    public virtual string Name { get; init; } = "b3867593-d854-402b-a1a7-5517bec1b9eb";

    [NeoMember("0c160a97-ff40-4433-ad66-6e649866bffd")]
    [NeoNumber(Min = 2, Max = 91)]
    public virtual int Value { get; init; } = 1;

    [NeoMember("d12785e9-fd6f-4591-81a0-4dabd2b95526")]
    [NeoNumber(Min = 0, Max = 1000)]
    public virtual float? Weight { get; init; }
}
