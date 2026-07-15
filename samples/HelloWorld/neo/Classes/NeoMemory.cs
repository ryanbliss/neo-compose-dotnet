// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("6c6f3bb8-30a0-4132-b0d4-cce75943aedd", Hidden = true)]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
public partial class NeoMemory
{
    [NeoMember("6223e7c9-a37a-480d-820b-c70f53c6eb0d", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.ReplaceValue, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
    [NeoDictionary()]
    [NeoEntries(nameof(NeoMemory.DialogueMemoriesEntries))]
    public virtual IReadOnlyDictionary<string, NeoDialogueMemory> DialogueMemories { get; init; } = default!;

    private static IReadOnlyList<NeoEntrySettings> DialogueMemoriesEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "3611d8af-6bfe-4015-93c4-2b611b33f2b6",
            Path = "$",
            Kind = NeoEntryKind.Class,
            Required = true,
            Locked = true,
            Virtual = true,
            Class = new()
            {
                Type = typeof(NeoDialogueMemory),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord }, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.", },
        },
    };
}
