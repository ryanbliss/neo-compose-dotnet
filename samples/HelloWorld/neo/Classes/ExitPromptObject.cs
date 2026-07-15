// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("5c65b156-e83a-41c5-bef0-ee375798bdc2")]
public partial class ExitPromptObject : ConsoleObject
{
    [NeoMember("f05dfa96-f35a-440c-832d-b4462cb2f30a")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.ObjectLayers), CollectionValueMember = "aa467eba-bc17-4cc6-933d-4c539caba2ad")]
    public override IReadOnlyList<NeoLookup<NeoObjectLayer>>? CompatibleLayers { get; init; } = new[] { Neo.Lookup<NeoObjectLayer>("8f96912d-5bbb-428c-84eb-8932ef588123") };

    [NeoMember("ff033c90-8bd5-4c57-929c-0af97005b9d3")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.ObjectLayers), CollectionValueMember = "aa467eba-bc17-4cc6-933d-4c539caba2ad")]
    public override NeoLookup<NeoObjectLayer> DefaultLayer { get; init; } = Neo.Lookup<NeoObjectLayer>("8f96912d-5bbb-428c-84eb-8932ef588123");

    [NeoMember("5676cb8a-be0f-4f1b-adc6-7241c09f8cf5")]
    [NeoDialogue]
    public virtual NeoDialogue ExitPromptRelay { get; init; } = Neo.Dialogue("d5a8097d-f02b-41c7-8356-9442a4a29412");

    [NeoMember("663d7511-cfc3-427c-8431-90cfc87a9813")]
    [NeoDialogue]
    public virtual NeoDialogue ExitPromptQuiet { get; init; } = Neo.Dialogue("7a6bcb67-d42a-4eb8-9934-0263d506e85c");

    [NeoMember("14ec578e-0aa7-4d12-8d02-47463e03a1f3")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoText(Localizable = false)]
    public override string Name { get; init; } = "Exit Prompt";

    [NeoMember("de93c887-ea29-49bd-bfea-a6255b8b9a54", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(ExitPromptObject.ChildrenEntries))]
    public override IReadOnlyList<NeoObjectBase> Children { get; init; } = new List<NeoObjectBase> { Neo.Ref<NeoObjectBase>("6dbdfc93-1071-4e62-85f4-eb6d0cc33f73"), Neo.Ref<NeoObjectBase>("4ca6b71b-2a7e-4f99-977d-1bcdb2556d9e") };

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

    [NeoMember("8e8c5ddf-6273-4440-869e-f1f9ca5dc51b", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public override NeoVector3 Size { get; init; } = new(2, 1, 0);

    [NeoMember("571a0e0b-b36c-45f3-ae9a-5fde39045c11", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(ExitPromptObject.PlacementTilesEntries))]
    public override IReadOnlyList<NeoObjectPlacementTile> PlacementTiles { get; init; } = new List<NeoObjectPlacementTile> { Neo.Ref<NeoObjectPlacementTile>("96cad433-4aed-4a9c-be34-fee3ebca8402"), Neo.Ref<NeoObjectPlacementTile>("8ba3f179-5171-4b77-99de-a8beec7bab25") };

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
