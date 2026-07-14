// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("77558d64-4fcc-46ac-8351-893093ee0002")]
public partial class LookupContainer
{
    [NeoMember("b8f18264-dfdc-4ae7-ada7-77e1db50f70a")]
    [NeoLookup(nameof(LookupContainer.LookupList))]
    public virtual NeoLookup<LookupEntry> Lookup { get; init; } = default(NeoLookup<LookupEntry>)!;

    [NeoMember("a65d4782-28cb-401f-8577-128dccca3d46")]
    [NeoDictionary()]
    [NeoEntries(nameof(LookupContainer.LookupListEntries))]
    public virtual IReadOnlyDictionary<string, LookupEntry> LookupList { get; init; } = default!;

    private static IReadOnlyList<NeoEntrySettings> LookupListEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "4cf934a0-531f-4513-8c22-27a688669eec",
            Path = "$",
            Kind = NeoEntryKind.Custom,
            Required = true,
            Virtual = true,
            Custom = new()
            {
                Type = typeof(LookupEntry),
            },
        },
    };
}
