// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("dd0bbe5a-47ef-4164-9421-caea07f6f56f", Hidden = true, ExtraJson = @"{""extendsTypeId"":null,""system"":null}")]
public sealed class Assets
{
    [NeoObject("92fe2bbb-e542-40a7-9d5e-7a7ad5b9abca", DefaultJson = @"{""value"":{}}", ExtraJson = @"{""extendsAttributeId"":null}")]
    public ComputedText Computed { get; init; }

    [NeoList("214df1a1-abca-4141-987b-380a5417c70a", EntryChainJson = @"[{""customTypeId"":""60c25a92-cb01-46f7-b5cf-c9d950586116"",""defaultValue"":{""value"":{}},""extendsAttributeId"":null,""id"":""dd6686e3-6435-47a3-b455-4cfbadad14ff"",""locked"":false,""name"":""Item"",""required"":true,""system"":null,""type"":7}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public IReadOnlyList<Item> Items { get; init; }

    [NeoObject("2c57948b-7479-47e0-b97c-242f6d543ae0", DefaultJson = @"{""value"":{}}", ExtraJson = @"{""extendsAttributeId"":null}")]
    public LookupContainer LookupContainer { get; init; }

    [NeoList("2827aefd-7f57-48ea-994c-c5c39ec659e3", EntryChainJson = @"[{""customTypeId"":""4c196697-4e08-4aeb-823f-322b353071ac"",""defaultValue"":{""value"":{}},""extendsAttributeId"":null,""id"":""9619809a-f7f0-4605-8f79-d1617b339819"",""locked"":false,""name"":""Outpost"",""required"":true,""system"":null,""type"":7}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""columnSettings"":[{""attributeKey"":""Name"",""frozen"":true,""hidden"":false,""width"":158,""wrapContent"":false},{""attributeKey"":""Image"",""frozen"":true,""hidden"":false,""width"":96,""wrapContent"":false},{""attributeKey"":""Planet"",""frozen"":false,""hidden"":false,""width"":115,""wrapContent"":false},{""attributeKey"":""FullDisplayText"",""frozen"":false,""hidden"":false,""width"":223,""wrapContent"":false},{""attributeKey"":""SaveUnsafe"",""frozen"":false,""hidden"":false,""width"":208,""wrapContent"":false},{""attributeKey"":""Save"",""frozen"":false,""hidden"":true,""width"":264,""wrapContent"":false},{""attributeKey"":""DebugLog"",""frozen"":false,""hidden"":true,""width"":null,""wrapContent"":false},{""attributeKey"":""AnimatedImage"",""frozen"":false,""hidden"":false,""width"":298,""wrapContent"":false},{""attributeKey"":""PlayAnimation"",""frozen"":false,""hidden"":true,""width"":null,""wrapContent"":false}],""extendsAttributeId"":null,""system"":null}")]
    public IReadOnlyList<Outpost> Outposts { get; init; }
}
