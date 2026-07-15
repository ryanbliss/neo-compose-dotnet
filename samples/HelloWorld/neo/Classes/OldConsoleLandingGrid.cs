// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("b44d80a9-7760-4919-8844-0cb71d08b788")]
public partial class OldConsoleLandingGrid : NeoTileGrid
{
    [NeoMember("f7493cd5-3da1-44ef-a275-5997d765b640")]
    [NeoText(Localizable = false)]
    public virtual string DisplayName { get; init; } = default!;

    [NeoMember("2193c5a4-cca1-4cd1-b079-62b83c1664e8", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.ObjectLayers))]
    public new virtual IReadOnlyList<NeoLookup<NeoObjectLayer>> ObjectLayers { get; init; } = Array.Empty<NeoLookup<NeoObjectLayer>>();

    [NeoMember("2faf47b8-cf59-4b51-91bc-ae4babe5d4b2", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.TileLayers))]
    public new virtual IReadOnlyList<NeoLookup<NeoTileLayer>> TileLayers { get; init; } = Array.Empty<NeoLookup<NeoTileLayer>>();
}
