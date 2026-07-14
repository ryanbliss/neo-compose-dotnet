// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("7d9647b1-df4d-4cb6-9f4d-7d80fe381f2f")]
public partial class PlayerSpawnObject : ConsoleObject
{
    [NeoMember("6ae49766-2186-48b8-b63e-62768cb3e88b")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.ObjectLayers), CollectionValueMember = "aa467eba-bc17-4cc6-933d-4c539caba2ad")]
    public override IReadOnlyList<NeoLookup<NeoObjectLayer>>? CompatibleLayers { get; init; } = new[] { Neo.Lookup<NeoObjectLayer>("8f96912d-5bbb-428c-84eb-8932ef588123") };

    [NeoMember("ee30bd7d-5ba2-4a83-9fbe-38ee6b53d7ca")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.ObjectLayers), CollectionValueMember = "aa467eba-bc17-4cc6-933d-4c539caba2ad")]
    public override NeoLookup<NeoObjectLayer> DefaultLayer { get; init; } = Neo.Lookup<NeoObjectLayer>("8f96912d-5bbb-428c-84eb-8932ef588123");

    [NeoMember("1994e574-7fcd-4c5f-8abe-f3e807bd334d")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoText(Localizable = false)]
    public override string Name { get; init; } = "Player Spawn";

    [NeoMember("35275ba0-4a3f-4b83-8b09-fccb7bd7a515", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(PlayerSpawnObject.ChildrenEntries))]
    public override IReadOnlyList<NeoObjectBase> Children { get; init; } = new List<NeoObjectBase> { Neo.Ref<NeoObjectBase>("d0d48343-8748-40bf-b35a-3e88cdd7e3a5") };

    private static IReadOnlyList<NeoEntrySettings> ChildrenEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "b3280478-f039-47a2-aa18-918175818bcb",
            Path = "$",
            Kind = NeoEntryKind.Custom,
            Required = true,
            Locked = true,
            Virtual = true,
            Default = new NeoValueSettings { Object = new Dictionary<string, NeoValueSettings> {  } },
            Custom = new()
            {
                Type = typeof(NeoObjectBase),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };

    [NeoMember("47c21aa5-e852-41d2-882c-b4f555aee9dd", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(PlayerSpawnObject.PlacementTilesEntries))]
    public override IReadOnlyList<NeoObjectPlacementTile> PlacementTiles { get; init; } = new List<NeoObjectPlacementTile> { Neo.Ref<NeoObjectPlacementTile>("6de63821-b102-4b62-aac6-c99c8aabecc9") };

    private static IReadOnlyList<NeoEntrySettings> PlacementTilesEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "c2bf0c92-1d24-4950-bea3-37d5f195728d",
            Path = "$",
            Kind = NeoEntryKind.Custom,
            Required = true,
            Locked = true,
            Virtual = true,
            Default = new NeoValueSettings { Object = new Dictionary<string, NeoValueSettings> {  } },
            Custom = new()
            {
                Type = typeof(NeoObjectPlacementTile),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };

    [NeoMember("e5f5125c-fe5c-46b4-9589-9c6ae6fcba19", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public override NeoCollider? Collider { get; init; } = new NeoCollider { isTrigger = Neo.Ref<bool>("38170df8-0175-40a0-87f4-10145984914d"), offset = Neo.Ref<NeoVector2>("4cc9752c-508e-43f8-9589-18d25679e8c0"), size = Neo.Ref<NeoVector2>("ee73946a-62a2-4a20-8403-22a50101a38c") };

    [NeoMember("2b10e854-d60a-40a7-bc4c-8aede3e5049e", Locked = true, Storage = NeoStorage.Session)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public override NeoVector3 Position { get; init; } = new(0, 0, 0);
}
