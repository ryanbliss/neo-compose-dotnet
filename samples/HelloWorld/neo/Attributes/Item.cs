// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("60c25a92-cb01-46f7-b5cf-c9d950586116", ExtraJson = @"{""extendsTypeId"":null,""system"":null}")]
public sealed class Item
{
    [NeoString("66e49f29-58cb-4ac7-b128-93febd0f0fb1", DefaultJson = @"{""value"":""b3867593-d854-402b-a1a7-5517bec1b9eb""}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public string Name { get; init; }

    [NeoInt("0c160a97-ff40-4433-ad66-6e649866bffd", Min = 2, Max = 91, DefaultJson = @"{""value"":1}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public int Value { get; init; }

    [NeoFloat("d12785e9-fd6f-4591-81a0-4dabd2b95526", Min = 0, Max = 1000)]
    public float? Weight { get; init; }
}
