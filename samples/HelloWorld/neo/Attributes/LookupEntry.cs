// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("9296e4be-bd27-44e3-9823-77fbeaa60665", ExtraJson = @"{""extendsTypeId"":null}")]
public sealed class LookupEntry
{
    [NeoString("29563228-5f16-44e1-bb3e-89f2097fd3cb", DefaultJson = @"{""value"":""ede61345-580d-4c04-a061-3c789175566d""}", ExtraJson = @"{""extendsAttributeId"":null}")]
    public string Name { get; init; }
}
