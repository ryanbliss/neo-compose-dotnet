// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoEnum("ba63d042-51c1-4bce-be70-d9848e6bb240")]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
public enum NeoSmartTileCondition
{
    [NeoEnumOption("This", Text = "7a6d716c-1c9f-45c5-8f15-29d87beb162b")]
    This,
    [NeoEnumOption("NotThis", Text = "7dcfeadc-d448-478d-82ae-33ebbdd62c7a")]
    NotThis,
    [NeoEnumOption("InheritsFromType", Text = "ce735446-bbb5-4f10-ab06-5f43e7be9ff0")]
    InheritsFromType,
    [NeoEnumOption("NotInheritsFromType", Text = "d305c446-c6f3-4f07-83d4-8388d6695a4c")]
    NotInheritsFromType,
}
