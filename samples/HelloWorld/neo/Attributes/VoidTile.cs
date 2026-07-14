// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("48bcbba5-57c0-40fd-8df8-35f4e7ce73f2")]
public partial class VoidTile : ConsoleTile
{
    [NeoMember("bb63d9bd-1571-4ef4-9453-e453db64ff41")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.TileLayers), CollectionValueMember = "0893eb05-41c5-40cb-a9d6-8397982519d4")]
    public override IReadOnlyList<NeoLookup<NeoTileLayer>>? CompatibleLayers { get; init; } = new[] { Neo.Lookup<NeoTileLayer>("8f96912d-5bbb-428c-84eb-8932ef588121"), Neo.Lookup<NeoTileLayer>("8f96912d-5bbb-428c-84eb-8932ef588122") };

    [NeoMember("bdd71ed6-7f39-4089-8375-fa5909835f00")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.TileLayers), CollectionValueMember = "0893eb05-41c5-40cb-a9d6-8397982519d4")]
    public override NeoLookup<NeoTileLayer> DefaultLayer { get; init; } = Neo.Lookup<NeoTileLayer>("8f96912d-5bbb-428c-84eb-8932ef588121");

    [NeoMember("106c7fee-6a3a-472b-baed-5b5aeb99c280")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoText(Localizable = false)]
    public override string Name { get; init; } = "Void";

    [NeoMember("c95da9bc-2193-4bd0-998f-a2f9f98862ee")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoFile(NeoFileKind.Sprite)]
    public override NeoSprite Sprite { get; init; } = Neo.Sprite("355390cf-4ce9-410d-af90-25273ae4bd3b", 0);
}
