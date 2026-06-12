// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("77558d64-4fcc-46ac-8351-893093ee0002", ExtraJson = @"{""extendsTypeId"":null,""system"":null}")]
public sealed class LookupContainer
{
    [NeoLookup("b8f18264-dfdc-4ae7-ada7-77e1db50f70a", CollectionId = "a65d4782-28cb-401f-8577-128dccca3d46", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public NeoLookupRef Lookup { get; init; }

    [NeoDictionary("a65d4782-28cb-401f-8577-128dccca3d46", EntryChainJson = @"[{""customTypeId"":""9296e4be-bd27-44e3-9823-77fbeaa60665"",""defaultValue"":{""value"":{}},""extendsAttributeId"":null,""id"":""4cf934a0-531f-4513-8c22-27a688669eec"",""locked"":false,""name"":""LookupEntry"",""required"":true,""type"":7}]", DefaultJson = @"{""value"":{}}", ExtraJson = @"{""extendsAttributeId"":null}")]
    public IReadOnlyDictionary<string, LookupEntry> LookupList { get; init; }
}
