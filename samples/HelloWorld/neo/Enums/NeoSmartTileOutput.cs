// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoEnum("551025ad-e441-4d43-90b0-2821c6235786")]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
public enum NeoSmartTileOutput
{
    [NeoEnumOption("Single", Text = "35370b82-4269-4fdf-b768-c268edbab60e")]
    Single,
    [NeoEnumOption("Random", Text = "37e58009-597f-4075-8f84-793a5e63cb44")]
    Random,
    [NeoEnumOption("Animation", Text = "d6191a4c-6b7b-4e86-a1ed-adbb43f2752c")]
    Animation,
}
