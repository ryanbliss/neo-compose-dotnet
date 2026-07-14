// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoEnum("d97f73bc-656f-489f-86de-6cb874ca8181")]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
public enum NeoSmartTileTransform
{
    [NeoEnumOption("Fixed", Text = "8e21d44a-8cc4-4bca-aea4-419c3ec1b1da")]
    Fixed,
    [NeoEnumOption("Rotated", Text = "678e286f-3f0a-4fe4-8ae2-0fcebccec0c3")]
    Rotated,
    [NeoEnumOption("MirrorX", Text = "b586d36f-35f7-4482-9cf7-8740d247f93f")]
    MirrorX,
    [NeoEnumOption("MirrorY", Text = "7a8a6e8e-2775-4ba9-995a-4edf8b988bf6")]
    MirrorY,
    [NeoEnumOption("MirrorXY", Text = "f6057c5a-d772-4043-a327-b785a8776379")]
    MirrorXY,
    [NeoEnumOption("RotatedMirror", Text = "a9d35370-fe60-40b4-afae-f8269c8aa55b")]
    RotatedMirror,
}
