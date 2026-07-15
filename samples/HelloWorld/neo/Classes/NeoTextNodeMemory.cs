// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("4cdf4a5b-b299-4253-854b-d25c0a4c7c20", Hidden = true)]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
public partial class NeoTextNodeMemory
{
    [NeoMember("214c9215-b01d-463a-b5fb-cb21e14b1961", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.ReplaceValue, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
    [NeoList]
    [NeoEntries(nameof(NeoTextNodeMemory.ChoiceHistoryEntries))]
    public virtual IReadOnlyList<NeoChoiceLog> ChoiceHistory { get; init; } = new List<NeoChoiceLog> {  };

    private static IReadOnlyList<NeoEntrySettings> ChoiceHistoryEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "f81b06c0-9dde-4674-b419-918f7cf23a4f",
            Path = "$",
            Kind = NeoEntryKind.Class,
            Required = true,
            Locked = true,
            Virtual = true,
            Class = new()
            {
                Type = typeof(NeoChoiceLog),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord }, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.", },
        },
    };

    // NeoScript: Scripts/NeoTextNodeMemory/HasVisited.neo
    [NeoMember("4042fd7d-88d4-4acf-81de-13052c70673e", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
    [NeoComputed]
    public virtual bool HasVisited { get; }

    [NeoMember("8f20c7ca-a552-4418-a355-6e35ee96639e", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
    public virtual string? LastVisitedAt { get; init; } = null;

    [NeoMember("28beaf7f-a3d3-4e9c-9f31-325d6708bd66", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
    public virtual string? MostRecentChoiceId { get; init; } = null;

    [NeoMember("2bcf2b63-87aa-4c4a-99ea-590e2b555fa6", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
    public virtual int VisitCount { get; init; } = 0;
}
