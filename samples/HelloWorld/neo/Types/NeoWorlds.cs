// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("3415d063-b5b9-4cfb-a77d-ba6ebb5890e2")]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", WorldKind = NeoWorldKind.WorldAssets)]
public abstract partial class NeoWorlds
{
    [NeoMember("7fb51db7-60c7-4064-bcde-6938acea4fe8", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(NeoWorlds.ObjectLayersEntries))]
    public virtual IReadOnlyList<NeoObjectLayer> ObjectLayers { get; init; } = new List<NeoObjectLayer> { Neo.Ref<NeoObjectLayer>("8f96912d-5bbb-428c-84eb-8932ef588123") };

    private static IReadOnlyList<NeoEntrySettings> ObjectLayersEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "d28efda6-7366-4ac1-a4ef-27c443f70586",
            Path = "$",
            Kind = NeoEntryKind.Custom,
            Required = true,
            Locked = true,
            Virtual = true,
            Default = new NeoValueSettings { Object = new Dictionary<string, NeoValueSettings> {  } },
            Custom = new()
            {
                Type = typeof(NeoObjectLayer),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };

    [NeoMember("61d22e83-1799-485b-a1a7-51b6b85e7ba8", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(NeoWorlds.ObjectsEntries))]
    public virtual IReadOnlyList<NeoObject> Objects { get; init; } = new List<NeoObject> {  };

    private static IReadOnlyList<NeoEntrySettings> ObjectsEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "7593513d-7aa5-427a-a51b-a7d44f04a5d7",
            Path = "$",
            Kind = NeoEntryKind.Custom,
            Required = true,
            Locked = true,
            Virtual = true,
            Default = new NeoValueSettings { Object = new Dictionary<string, NeoValueSettings> {  } },
            Custom = new()
            {
                Type = typeof(NeoObject),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };

    [NeoMember("5161fb81-7254-4e41-b153-25138b8e9e74", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(NeoWorlds.TileLayersEntries))]
    public virtual IReadOnlyList<NeoTileLayer> TileLayers { get; init; } = new List<NeoTileLayer> { Neo.Ref<NeoTileLayer>("8f96912d-5bbb-428c-84eb-8932ef588121") };

    private static IReadOnlyList<NeoEntrySettings> TileLayersEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "6760f916-5df1-41af-8300-e466adaa397b",
            Path = "$",
            Kind = NeoEntryKind.Custom,
            Required = true,
            Locked = true,
            Virtual = true,
            Default = new NeoValueSettings { Object = new Dictionary<string, NeoValueSettings> {  } },
            Custom = new()
            {
                Type = typeof(NeoTileLayer),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };

    [NeoMember("56831afd-18d8-418d-9bcf-c76c770592c4", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(NeoWorlds.TilesEntries))]
    public virtual IReadOnlyList<NeoTile> Tiles { get; init; } = new List<NeoTile> {  };

    private static IReadOnlyList<NeoEntrySettings> TilesEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "2835bbe7-ba93-4bf4-bf63-90561770b5e0",
            Path = "$",
            Kind = NeoEntryKind.Custom,
            Required = true,
            Locked = true,
            Virtual = true,
            Default = new NeoValueSettings { Object = new Dictionary<string, NeoValueSettings> {  } },
            Custom = new()
            {
                Type = typeof(NeoTile),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };
}
