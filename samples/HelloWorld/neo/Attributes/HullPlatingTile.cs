// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("a8305a31-7f6c-4ff5-8a9f-5871ef451093")]
public partial class HullPlatingTile : ConsoleTile
{
    [NeoMember("e8568eb4-ae16-4888-8b8e-0f52268c4b11")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.TileLayers), CollectionValueMember = "0893eb05-41c5-40cb-a9d6-8397982519d4")]
    public override IReadOnlyList<NeoLookup<NeoTileLayer>>? CompatibleLayers { get; init; } = new[] { Neo.Lookup<NeoTileLayer>("8f96912d-5bbb-428c-84eb-8932ef588122") };

    [NeoMember("5572bd04-b0be-434b-bde9-6ddf77fed61e")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.TileLayers), CollectionValueMember = "0893eb05-41c5-40cb-a9d6-8397982519d4")]
    public override NeoLookup<NeoTileLayer> DefaultLayer { get; init; } = Neo.Lookup<NeoTileLayer>("8f96912d-5bbb-428c-84eb-8932ef588122");

    [NeoMember("08a564d0-4924-457f-8901-46f636e789b5")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoText(Localizable = false)]
    public override string Name { get; init; } = "Hull Plating";

    [NeoMember("14750f2f-0ecf-424f-9386-8fd34eca9310")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoFile(NeoFileKind.Sprite)]
    public override NeoSprite Sprite { get; init; } = Neo.Sprite("ad8b2628-2e12-4c8a-90e5-b4334b430b6e", 0);
}
