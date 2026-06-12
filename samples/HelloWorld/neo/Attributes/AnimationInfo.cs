// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("11177bd5-0678-4bff-86b8-46718ff1827b", ExtraJson = @"{""extendsTypeId"":null,""system"":null}")]
public sealed class AnimationInfo
{
    [NeoInt("87ec59c5-a157-4670-9cb9-794487cc79d3", Min = 1, DefaultJson = @"{""value"":30}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public int FPS { get; init; }

    [NeoList("cc7fec3f-593b-4888-bf93-b9bb6bcb5e44", EntryChainJson = @"[{""defaultValue"":null,""extendsAttributeId"":null,""id"":""d17d421c-b2e8-4956-baf5-ab174d1f4cb0"",""locked"":false,""name"":""List"",""required"":true,""system"":null,""templateId"":null,""type"":11}]", DefaultJson = @"null", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public IReadOnlyList<object> Frames { get; init; }

    [NeoString("8b8ac389-c9a7-4a0c-8335-2352865ee1b4", DefaultJson = @"{""value"":""5c90af78-a25e-462a-b5c3-8082e5080037""}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public string Name { get; init; }
}
