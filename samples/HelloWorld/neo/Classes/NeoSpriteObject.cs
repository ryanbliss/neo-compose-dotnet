// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("7cb1b706-95d2-4465-8e75-c82a6b7d8830")]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", WorldKind = NeoWorldKind.SpriteObject)]
public partial class NeoSpriteObject : NeoObjectBase
{
    [NeoMember("441cb790-a45f-4488-a5a9-6f375af6c369", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoText(Localizable = false)]
    public new virtual string Name { get; init; } = "";

    [NeoMember("e9288ba9-f5a2-4485-8443-6afb155b31e0", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoFile(NeoFileKind.Sprite)]
    public virtual NeoSprite Sprite { get; init; } = default!;
}
