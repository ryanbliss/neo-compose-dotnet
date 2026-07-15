// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("4c2aa621-e662-419f-a5b5-d8dcfab9b29b")]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", WorldKind = NeoWorldKind.Object)]
public abstract partial class NeoObject : NeoObjectBase
{
    [NeoMember("bb5d2cf1-a0dd-4eba-a62e-0e1bf0177071", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(NeoObject.ChildrenEntries))]
    public virtual IReadOnlyList<NeoObjectBase> Children { get; init; } = new List<NeoObjectBase> {  };

    private static IReadOnlyList<NeoEntrySettings> ChildrenEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "b3280478-f039-47a2-aa18-918175818bcb",
            Path = "$",
            Kind = NeoEntryKind.Class,
            Required = true,
            Locked = true,
            Virtual = true,
            Default = new NeoValueSettings { Object = new Dictionary<string, NeoValueSettings> {  } },
            Class = new()
            {
                Type = typeof(NeoObjectBase),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };

    [NeoMember("a0083c92-72f7-405f-8863-ff86f995d36d", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public virtual NeoCollider? Collider { get; init; } = null;

    [NeoMember("5915099d-fc2e-4f4a-875c-dad704472d05", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.ObjectLayers))]
    public virtual IReadOnlyList<NeoLookup<NeoObjectLayer>>? CompatibleLayers { get; init; } = null;

    [NeoMember("cf6e9aa2-dd4b-4673-a83b-5a15e617eb9a", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.ObjectLayers))]
    public virtual NeoLookup<NeoObjectLayer> DefaultLayer { get; init; } = default!;

    [NeoMember("3b02422f-1ef2-4a50-8386-155d5001082b", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoText(Localizable = false)]
    public new virtual string Name { get; init; } = "";

    [NeoMember("5a9ca1f5-a21b-4d4e-8c55-00341af594b4", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(NeoObject.PlacementTilesEntries))]
    public virtual IReadOnlyList<NeoObjectPlacementTile> PlacementTiles { get; init; } = new List<NeoObjectPlacementTile> {  };

    private static IReadOnlyList<NeoEntrySettings> PlacementTilesEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "c2bf0c92-1d24-4950-bea3-37d5f195728d",
            Path = "$",
            Kind = NeoEntryKind.Class,
            Required = true,
            Locked = true,
            Virtual = true,
            Default = new NeoValueSettings { Object = new Dictionary<string, NeoValueSettings> {  } },
            Class = new()
            {
                Type = typeof(NeoObjectPlacementTile),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };
}
