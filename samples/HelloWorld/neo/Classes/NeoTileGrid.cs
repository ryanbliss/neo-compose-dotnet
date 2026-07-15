// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("e93a83cb-9bc2-46fe-9c85-d70465a89da8")]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", WorldKind = NeoWorldKind.TileGrid)]
public abstract partial class NeoTileGrid
{
    [NeoMember("3b523230-a851-4ab5-a6f2-c2d0745c116f", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public virtual NeoVector3 CellSize { get; init; } = new(1, 1, 0);

    [NeoMember("98578ba3-a70e-4397-9283-996a898d44c8", Locked = true, StorageKey = "world:$parentClass")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(NeoTileGrid.ChildrenEntries))]
    public virtual IReadOnlyList<NeoLayerGroupBase> Children { get; init; } = new List<NeoLayerGroupBase> {  };

    private static IReadOnlyList<NeoEntrySettings> ChildrenEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "f1d89b43-7de6-4d50-9614-342bcdf85531",
            Path = "$",
            Kind = NeoEntryKind.Class,
            Required = true,
            Locked = true,
            Virtual = true,
            Default = new NeoValueSettings { Object = new Dictionary<string, NeoValueSettings> {  } },
            Class = new()
            {
                Type = typeof(NeoLayerGroupBase),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };

    [NeoMember("2193c5a4-cca1-4cd1-b079-62b83c1664e8", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.ObjectLayers))]
    public virtual IReadOnlyList<NeoLookup<NeoObjectLayer>> ObjectLayers { get; init; } = Array.Empty<NeoLookup<NeoObjectLayer>>();

    [NeoMember("cddb5d5d-04cf-4c61-b9df-e46bfdabe3a5", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.Objects))]
    public virtual IReadOnlyList<NeoLookup<NeoObject>> Objects { get; init; } = Array.Empty<NeoLookup<NeoObject>>();

    [NeoMember("8ece74e4-e17e-4e56-9ef6-8dc2bc9f59f0", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public virtual int PixelsPerUnit { get; init; } = 100;

    [NeoMember("2faf47b8-cf59-4b51-91bc-ae4babe5d4b2", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.TileLayers))]
    public virtual IReadOnlyList<NeoLookup<NeoTileLayer>> TileLayers { get; init; } = Array.Empty<NeoLookup<NeoTileLayer>>();

    [NeoMember("711149d9-1c0d-4e36-af29-245a6ff2bc67", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.Tiles))]
    public virtual IReadOnlyList<NeoLookup<NeoTile>> Tiles { get; init; } = Array.Empty<NeoLookup<NeoTile>>();
}
