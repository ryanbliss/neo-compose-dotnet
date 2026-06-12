// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("4cdf4a5b-b299-4253-854b-d25c0a4c7c20", Hidden = true, ExtraJson = @"{""extendsTypeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
public sealed class NeoTextNodeMemory
{
    [NeoList("214c9215-b01d-463a-b5fb-cb21e14b1961", Locked = true, EntryChainJson = @"[{""customTypeId"":""af5795d0-e019-4776-8b7c-d0206f90d59f"",""defaultValue"":{""value"":{}},""extendsAttributeId"":null,""id"":""f81b06c0-9dde-4674-b419-918f7cf23a4f"",""locked"":true,""name"":""NeoChoiceLog"",""required"":true,""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""},""type"":7}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""extendsAttributeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""replaceValue""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
    public IReadOnlyList<NeoChoiceLog> ChoiceHistory { get; init; }

    [NeoGetter("4042fd7d-88d4-4acf-81de-13052c70673e", Locked = true, Code = @"return this.VisitCount > 0;", RetJson = @"{""required"":true,""type"":1}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
    public object HasVisited { get; init; }

    [NeoString("8f20c7ca-a552-4418-a355-6e35ee96639e", Locked = true, DefaultJson = @"{""value"":null}", ExtraJson = @"{""extendsAttributeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
    public string? LastVisitedAt { get; init; }

    [NeoString("28beaf7f-a3d3-4e9c-9f31-325d6708bd66", Locked = true, DefaultJson = @"{""value"":null}", ExtraJson = @"{""extendsAttributeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
    public string? MostRecentChoiceId { get; init; }

    [NeoInt("2bcf2b63-87aa-4c4a-99ea-590e2b555fa6", Locked = true, DefaultJson = @"{""value"":0}", ExtraJson = @"{""extendsAttributeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
    public int VisitCount { get; init; }
}
