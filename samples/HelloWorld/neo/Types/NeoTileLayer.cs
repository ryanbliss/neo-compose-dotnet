// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("a3e4a1df-f487-44bc-bf90-2edc4a88f1ad")]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", WorldKind = NeoWorldKind.TileLayer)]
public abstract partial class NeoTileLayer
{
    [NeoMember("788a3dbc-6167-4320-83aa-1e884924f776", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoText(Localizable = false)]
    public virtual string Name { get; init; } = "";

    [NeoMember("89dbb12e-1d7c-42ad-b872-63fc7fe8bd5b", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(Assets.SortingLayers), CollectionValueMember = "e82662a0-10be-45cb-8c4a-a5d8b6b5bb0c")]
    public virtual NeoLookup<NeoSortingLayer> SortingLayer { get; init; } = Neo.Lookup<NeoSortingLayer>("88c0d53b-94ee-4f48-839b-9148d07828fb");

    [NeoMember("472dfd84-59cb-4516-8763-90d0af6d039f", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoNumber(Min = -32768, Max = 32767)]
    public virtual int SortingOrder { get; init; } = 0;
}
