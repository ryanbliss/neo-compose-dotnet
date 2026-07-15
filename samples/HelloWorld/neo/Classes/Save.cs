// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("96e8284d-ae43-4e91-919d-86c25ce098e0", Hidden = true)]
public partial class Save
{
    [NeoMember("1f76e191-faaf-4840-9bb0-29785a1c8ed6")]
    [NeoNumber(Min = 0)]
    public virtual int Bits { get; init; } = 1000;

    [NeoMember("5155954d-ca1d-441e-b9ee-1837b0a08e05")]
    public virtual bool Dead { get; init; } = false;

    [NeoMember("bf4a75b5-acf9-4ff8-8511-57f871749db3")]
    [NeoLookup(nameof(Assets.Items))]
    public virtual IReadOnlyList<NeoLookup<Item>> Inventory { get; init; } = Array.Empty<NeoLookup<Item>>();

    [NeoMember("82a12a52-95ac-453a-9353-24c11a63c530")]
    [NeoLookup(nameof(Assets.Outposts))]
    public virtual NeoLookup<Outpost> Location { get; init; } = Neo.Lookup<Outpost>("913fd757-d6ee-488e-866f-c7af41aa544b");

    [NeoMember("99b07db1-f732-437e-a903-98183edca96b", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.ReplaceValue, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
    public virtual NeoMemory NeoMemory { get; init; } = default!;

    [NeoMember("f977a94e-aa40-414c-9812-dacdd50110a8")]
    [NeoDictionary()]
    [NeoEntries(nameof(Save.OutpostSaveMapEntries))]
    public virtual IReadOnlyDictionary<string, OutpostSaveData> OutpostSaveMap { get; init; } = default!;

    private static IReadOnlyList<NeoEntrySettings> OutpostSaveMapEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "42d3d49d-3ba8-4672-ad54-53dc109697fc",
            Path = "$",
            Kind = NeoEntryKind.Class,
            Required = false,
            Virtual = true,
            Class = new()
            {
                Type = typeof(OutpostSaveData),
            },
        },
    };

    [NeoMember("4868ab84-027a-405d-bfca-d04d4d4917fa")]
    public virtual QuestState Quest { get; init; } = new();

    [NeoMember("c151eda4-ecce-4edf-988d-25a97c657146")]
    [NeoList]
    [NeoEntries(nameof(Save.VisitedEntries))]
    public virtual IReadOnlyList<PlanetVisit> Visited { get; init; } = new List<PlanetVisit> {  };

    private static IReadOnlyList<NeoEntrySettings> VisitedEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "659589fe-95b2-472f-a53b-e305db97450f",
            Path = "$",
            Kind = NeoEntryKind.Class,
            Required = true,
            Virtual = true,
            Class = new()
            {
                Type = typeof(PlanetVisit),
            },
        },
    };

    [NeoMember("ebab06a2-98e9-4a30-bb13-cfea8f910462")]
    public virtual Planet World { get; init; } = Planet.earth;
}
