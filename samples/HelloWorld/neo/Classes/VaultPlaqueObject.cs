// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("cacf06dd-db1d-4f48-99c7-f3cea5a6961f")]
public partial class VaultPlaqueObject : ConsoleObject
{
    [NeoMember("b98218ae-934e-4359-a9cd-4ca82433dc51")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.ObjectLayers), CollectionValueMember = "aa467eba-bc17-4cc6-933d-4c539caba2ad")]
    public override IReadOnlyList<NeoLookup<NeoObjectLayer>>? CompatibleLayers { get; init; } = new[] { Neo.Lookup<NeoObjectLayer>("8f96912d-5bbb-428c-84eb-8932ef588123") };

    [NeoMember("71bca16c-04c7-46ad-884b-002a1914bee8")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.ObjectLayers), CollectionValueMember = "aa467eba-bc17-4cc6-933d-4c539caba2ad")]
    public override NeoLookup<NeoObjectLayer> DefaultLayer { get; init; } = Neo.Lookup<NeoObjectLayer>("8f96912d-5bbb-428c-84eb-8932ef588123");

    [NeoMember("9bcfd9a8-ef4a-4a89-b6d4-446c9baf01d7")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoText(Localizable = false)]
    public override string Name { get; init; } = "Vault Plaque";

    [NeoMember("502c9308-974b-446d-935b-c22adde5a9d2")]
    [NeoDialogue]
    public virtual NeoDialogue VaultPlaqueLocked { get; init; } = Neo.Dialogue("da73bce9-0d39-4c27-bb09-32b538f97f61");

    [NeoMember("d4a33b58-95a5-4874-8236-05ec38df8f82")]
    [NeoDialogue]
    public virtual NeoDialogue VaultPlaqueReward { get; init; } = Neo.Dialogue("bbda459e-c77e-4084-9047-22b1dfbb0bff");

    [NeoMember("c481dc1e-b5fd-4352-a76b-536cc3e17f71", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(VaultPlaqueObject.ChildrenEntries))]
    public override IReadOnlyList<NeoObjectBase> Children { get; init; } = new List<NeoObjectBase> { Neo.Ref<NeoObjectBase>("522f0109-97d2-4ac1-ad7c-70357d278035") };

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

    [NeoMember("430fca56-b45a-4896-9ab2-795a3faf57f6", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(VaultPlaqueObject.PlacementTilesEntries))]
    public override IReadOnlyList<NeoObjectPlacementTile> PlacementTiles { get; init; } = new List<NeoObjectPlacementTile> { Neo.Ref<NeoObjectPlacementTile>("293652f8-54d6-4168-89be-7ac75207dcd0") };

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
