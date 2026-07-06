// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoEnum("d97f73bc-656f-489f-86de-6cb874ca8181", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
public enum NeoSmartTileTransform
{
    [NeoEnumEntry(Text = "8e21d44a-8cc4-4bca-aea4-419c3ec1b1da")]
    Fixed,
    [NeoEnumEntry(Text = "b586d36f-35f7-4482-9cf7-8740d247f93f")]
    MirrorX,
    [NeoEnumEntry(Text = "f6057c5a-d772-4043-a327-b785a8776379")]
    MirrorXY,
    [NeoEnumEntry(Text = "7a8a6e8e-2775-4ba9-995a-4edf8b988bf6")]
    MirrorY,
    [NeoEnumEntry(Text = "678e286f-3f0a-4fe4-8ae2-0fcebccec0c3")]
    Rotated,
    [NeoEnumEntry(Text = "a9d35370-fe60-40b4-afae-f8269c8aa55b")]
    RotatedMirror,
}
