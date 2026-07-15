// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("27874300-3e78-4d1c-802b-caf34d25d1ab")]
public partial class RecoveryCacheObject : ConsoleObject
{
    [NeoMember("32be897f-be96-4ab7-a586-a9a6fdfff8b7", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(RecoveryCacheObject.ChildrenEntries))]
    public override IReadOnlyList<NeoObjectBase> Children { get; init; } = new List<NeoObjectBase> { Neo.Ref<NeoObjectBase>("4d4d8a22-92e0-457d-8e86-0a41c6193259") };

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

    [NeoMember("16778506-5859-42ac-a233-da915bc170d6")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.ObjectLayers), CollectionValueMember = "aa467eba-bc17-4cc6-933d-4c539caba2ad")]
    public override IReadOnlyList<NeoLookup<NeoObjectLayer>>? CompatibleLayers { get; init; } = new[] { Neo.Lookup<NeoObjectLayer>("8f96912d-5bbb-428c-84eb-8932ef588123") };

    [NeoMember("e37c6500-370f-44a5-b78d-b4f68a22ae5e")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.ObjectLayers), CollectionValueMember = "aa467eba-bc17-4cc6-933d-4c539caba2ad")]
    public override NeoLookup<NeoObjectLayer> DefaultLayer { get; init; } = Neo.Lookup<NeoObjectLayer>("8f96912d-5bbb-428c-84eb-8932ef588123");

    [NeoMember("345b54f2-eb9c-4cd9-8c5f-cee868c9602d")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoText(Localizable = false)]
    public override string Name { get; init; } = "Recovery Cache";

    [NeoMember("d8e9ad0e-157f-4709-96a7-8775efa3dd11", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(RecoveryCacheObject.PlacementTilesEntries))]
    public override IReadOnlyList<NeoObjectPlacementTile> PlacementTiles { get; init; } = new List<NeoObjectPlacementTile> { Neo.Ref<NeoObjectPlacementTile>("9404babf-786a-4e74-aec4-9c6667485278") };

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

    [NeoMember("47466be3-368c-4ac1-8c0e-7a825af6b538")]
    [NeoDialogue]
    public virtual NeoDialogue RecoveryCache { get; init; } = Neo.Dialogue("cb0ac79c-f3b4-4c96-b968-8c4173c1f712");

    [NeoMember("94472662-a3a9-4c02-8abb-6229442e1e49", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public override NeoCollider? Collider { get; init; } = new NeoCollider { isTrigger = Neo.Ref<bool>("fd96438e-101d-4f6b-9fa3-2a33bd26f494"), offset = Neo.Ref<NeoVector2>("3777dff9-fdf7-4ba4-84f0-c4cea2307151"), size = Neo.Ref<NeoVector2>("e153f62d-e081-461f-9d78-6debfec01104") };
}
