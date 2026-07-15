// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("ec21a2ec-cb95-4e10-9c7d-5ba7e4cdea88", Hidden = true)]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", WorldKind = NeoWorldKind.ObjectBase)]
public abstract partial class NeoObjectBase
{
    [NeoMember("b21bfd01-1234-4f49-ab6b-889f829cb148", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoText(Localizable = false)]
    public virtual string Name { get; init; } = default!;

    [NeoMember("7fc41bde-418a-4507-8c4b-9b75d7012125", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public virtual NeoVector3 Position { get; init; } = new(0, 0, 0);

    [NeoMember("e1d820d8-56b1-43ac-aa10-0a019f0dc38f", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public virtual NeoVector3 Size { get; init; } = new(1, 1, 0);
}
