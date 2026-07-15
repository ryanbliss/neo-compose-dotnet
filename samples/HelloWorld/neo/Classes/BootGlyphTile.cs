// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("07db44f3-8cc5-4164-aace-098ca68460f4")]
public partial class BootGlyphTile : ConsoleTile
{
    [NeoMember("6f734d02-4752-4697-9995-1bfa748e0938")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.TileLayers), CollectionValueMember = "0893eb05-41c5-40cb-a9d6-8397982519d4")]
    public override IReadOnlyList<NeoLookup<NeoTileLayer>>? CompatibleLayers { get; init; } = new[] { Neo.Lookup<NeoTileLayer>("8f96912d-5bbb-428c-84eb-8932ef588121") };

    [NeoMember("0cea4b79-8614-4753-a319-858a749fd2b3")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.TileLayers), CollectionValueMember = "0893eb05-41c5-40cb-a9d6-8397982519d4")]
    public override NeoLookup<NeoTileLayer> DefaultLayer { get; init; } = Neo.Lookup<NeoTileLayer>("8f96912d-5bbb-428c-84eb-8932ef588121");

    [NeoMember("e2a98ca7-1317-4743-9be1-19cd5ec47bf1")]
    [NeoDialogue]
    public virtual NeoDialogue BootGlyphAttuned { get; init; } = Neo.Dialogue("12729fbc-56a7-4d8f-b04a-ac039604dfe9");

    [NeoMember("f703feff-bc27-46d5-a9b9-d29df959342a")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoText(Localizable = false)]
    public override string Name { get; init; } = "Boot Glyph";

    [NeoMember("948a6ed7-d7bf-4ffb-b123-06955293681c")]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoFile(NeoFileKind.Sprite)]
    public override NeoSprite Sprite { get; init; } = Neo.Sprite("2c68221a-2a3c-45d4-8565-c5c23c0654d3", 0);
}
