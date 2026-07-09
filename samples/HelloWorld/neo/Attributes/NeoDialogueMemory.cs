// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("48f37cd8-69d2-4cd3-ae44-7cfed7912415", Hidden = true, ExtraJson = @"{""extendsTypeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
public sealed class NeoDialogueMemory
{
    [NeoProperty("cd79c978-da95-4da3-8aa5-eea57f9e4f2c", Locked = true, Code = @"return this.VisitCount > 0;", RetJson = @"{""required"":true,""type"":1}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
    public object HasVisited { get; init; }

    [NeoString("defd7f67-7f35-4907-a75d-8da3b24b96f4", Locked = true, DefaultJson = @"{""value"":null}", ExtraJson = @"{""extendsAttributeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
    public string? LastVisitedAt { get; init; }

    [NeoDictionary("84960eeb-60ea-4241-a074-99a47a0d8dc1", Locked = true, EntryChainJson = @"[{""customTypeId"":""4cdf4a5b-b299-4253-854b-d25c0a4c7c20"",""defaultValue"":{""value"":{}},""extendsAttributeId"":null,""id"":""0d4e7b75-97f6-44f2-b42b-925dc3983341"",""locked"":true,""name"":""NeoTextNodeMemory"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""},""type"":7}]", DefaultJson = @"{""value"":{}}", ExtraJson = @"{""extendsAttributeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""replaceValue""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
    public IReadOnlyDictionary<string, NeoTextNodeMemory> TextNodeMemories { get; init; }

    [NeoInt("504778e6-972f-4b04-8d64-ec038ff2414f", Locked = true, DefaultJson = @"{""value"":0}", ExtraJson = @"{""extendsAttributeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
    public int VisitCount { get; init; }
}
