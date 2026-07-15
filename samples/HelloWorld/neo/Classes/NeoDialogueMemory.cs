// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("48f37cd8-69d2-4cd3-ae44-7cfed7912415", Hidden = true)]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
public partial class NeoDialogueMemory
{
    // NeoScript: Scripts/NeoDialogueMemory/HasVisited.neo
    [NeoMember("cd79c978-da95-4da3-8aa5-eea57f9e4f2c", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
    [NeoComputed]
    public virtual bool HasVisited { get; }

    [NeoMember("defd7f67-7f35-4907-a75d-8da3b24b96f4", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
    public virtual string? LastVisitedAt { get; init; } = null;

    [NeoMember("84960eeb-60ea-4241-a074-99a47a0d8dc1", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.ReplaceValue, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
    [NeoDictionary()]
    [NeoEntries(nameof(NeoDialogueMemory.TextNodeMemoriesEntries))]
    public virtual IReadOnlyDictionary<string, NeoTextNodeMemory> TextNodeMemories { get; init; } = default!;

    private static IReadOnlyList<NeoEntrySettings> TextNodeMemoriesEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "0d4e7b75-97f6-44f2-b42b-925dc3983341",
            Path = "$",
            Kind = NeoEntryKind.Class,
            Required = true,
            Locked = true,
            Virtual = true,
            Class = new()
            {
                Type = typeof(NeoTextNodeMemory),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord }, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.", },
        },
    };

    [NeoMember("504778e6-972f-4b04-8d64-ec038ff2414f", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
    public virtual int VisitCount { get; init; } = 0;
}
