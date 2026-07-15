// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoRegistry]
public abstract partial class Root
{
    [NeoMember("8f1dbf20-8d7c-49e0-9e0f-5e172da6f684", Locked = true, Storage = NeoStorage.Immutable)]
    [NeoSchemaOrder("Outposts", "Computed", "Items", "LookupContainer")]
    public virtual Assets Assets { get; init; } = default!;

    [NeoMember("01225bc0-9381-4c01-946c-488dd3c89bce", Key = "Children Entry")]
    public virtual NeoObjectBase ChildrenEntry { get; init; } = default!;

    [NeoMember("3898975b-c6cc-4f35-a82e-89ba3ff240c6", Key = "Children Entry")]
    public virtual NeoObjectBase ChildrenEntry_2 { get; init; } = default!;

    [NeoMember("99a98018-cf3f-40b0-8405-7b0e1e0020e0", Key = "Children Entry")]
    public virtual NeoObjectBase ChildrenEntry_3 { get; init; } = default!;

    [NeoMember("52e6a08d-5b41-4d77-8890-e23bac51eda8")]
    [NeoList]
    [NeoEntries(nameof(Root.ObjectLayersEntries))]
    public virtual IReadOnlyList<ConsoleObjectLayer> ObjectLayers { get; init; } = default!;

    private static IReadOnlyList<NeoEntrySettings> ObjectLayersEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "444f7fa9-8160-4514-a3a7-eb63b327fa76",
            Path = "$",
            Kind = NeoEntryKind.Custom,
            Required = true,
            Virtual = true,
            Custom = new()
            {
                Type = typeof(ConsoleObjectLayer),
            },
        },
    };

    [NeoMember("4a1a7058-e5ba-4479-9f2f-7686a649a6b5", Locked = true, Storage = NeoStorage.Save)]
    [NeoSchemaOrder("World", "Location", "Visited", "Dead", "OutpostSaveMap", "Inventory", "Bits", "NeoMemory")]
    public virtual Save Save { get; init; } = default!;

    [NeoMember("f0ce0706-def6-41ff-b8e9-baa2a7a84de6", Locked = true, Storage = NeoStorage.Session)]
    public virtual Session Session { get; init; } = default!;

    [NeoMember("477a7320-35e3-42f6-b03e-ff4a23b4f18a")]
    [NeoList]
    [NeoEntries(nameof(Root.TileLayersEntries))]
    public virtual IReadOnlyList<ConsoleTileLayer> TileLayers { get; init; } = default!;

    private static IReadOnlyList<NeoEntrySettings> TileLayersEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "4bc2ef98-930e-483f-90fa-c3716346b4d7",
            Path = "$",
            Kind = NeoEntryKind.Custom,
            Required = true,
            Virtual = true,
            Custom = new()
            {
                Type = typeof(ConsoleTileLayer),
            },
        },
    };
}
