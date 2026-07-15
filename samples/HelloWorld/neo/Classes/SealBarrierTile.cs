// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("720c1361-de9e-4c12-b90e-bb6ac9e1ce8b")]
public partial class SealBarrierTile : ConsoleTile
{
    [NeoMember("daaf5b5f-4191-4e22-95e5-d10c3bc264cf")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.TileLayers), CollectionValueMember = "0893eb05-41c5-40cb-a9d6-8397982519d4")]
    public override IReadOnlyList<NeoLookup<NeoTileLayer>>? CompatibleLayers { get; init; } = new[] { Neo.Lookup<NeoTileLayer>("8f96912d-5bbb-428c-84eb-8932ef588122") };

    [NeoMember("fb8dc50f-20de-461a-9d27-97fe9f8eb5f2")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.TileLayers), CollectionValueMember = "0893eb05-41c5-40cb-a9d6-8397982519d4")]
    public override NeoLookup<NeoTileLayer> DefaultLayer { get; init; } = Neo.Lookup<NeoTileLayer>("8f96912d-5bbb-428c-84eb-8932ef588122");

    [NeoMember("95ec9093-fb16-487e-b897-89ac9f60c426")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoText(Localizable = false)]
    public override string Name { get; init; } = "Seal Barrier";

    [NeoMember("9de921e3-007d-4ca7-aa65-699c5b92a8f1")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoFile(NeoFileKind.Sprite)]
    public override NeoSprite Sprite { get; init; } = Neo.Sprite("18da2470-d75e-4634-97ce-7ea8bd26b743", 0);
}
