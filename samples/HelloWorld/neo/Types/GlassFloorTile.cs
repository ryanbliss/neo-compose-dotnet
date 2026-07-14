// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("bda4cf72-c8da-4be0-8148-024d0fc2d826")]
public partial class GlassFloorTile : ConsoleTile
{
    [NeoMember("e0e00d1b-0ee3-4102-9d83-34b3038c573f")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.TileLayers), CollectionValueMember = "0893eb05-41c5-40cb-a9d6-8397982519d4")]
    public override IReadOnlyList<NeoLookup<NeoTileLayer>>? CompatibleLayers { get; init; } = new[] { Neo.Lookup<NeoTileLayer>("8f96912d-5bbb-428c-84eb-8932ef588121") };

    [NeoMember("b562d0ae-f799-4364-b90c-b4f026c7d870")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.TileLayers), CollectionValueMember = "0893eb05-41c5-40cb-a9d6-8397982519d4")]
    public override NeoLookup<NeoTileLayer> DefaultLayer { get; init; } = Neo.Lookup<NeoTileLayer>("8f96912d-5bbb-428c-84eb-8932ef588121");

    [NeoMember("04100351-2919-4795-8aa6-fadd571a6036")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoText(Localizable = false)]
    public override string Name { get; init; } = "Glass Floor";

    [NeoMember("deabb583-d797-4c3e-bb07-3bafbb1e84b3")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoFile(NeoFileKind.Sprite)]
    public override NeoSprite Sprite { get; init; } = Neo.Sprite("acf20f9d-cd05-4205-a449-a0c21dcd4e12", 0);
}
