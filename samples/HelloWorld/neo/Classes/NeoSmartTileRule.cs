// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("d500e920-87d9-4804-affa-1bd8fc5e91ae")]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", WorldKind = NeoWorldKind.SmartTileRule)]
public partial class NeoSmartTileRule
{
    [NeoMember("f8106137-0cde-4be0-bce7-3db5cf40257a", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public virtual NeoSmartTileCollider Collider { get; init; } = NeoSmartTileCollider.Sprite;

    [NeoMember("9ae9cf8b-4ea1-413e-8c7a-dcabe1a5cc98", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoNumber(Min = 0)]
    public virtual float MaxAnimationSpeed { get; init; } = 1;

    [NeoMember("3111ed6f-d441-40b9-97f0-25fff2fb9838", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoNumber(Min = 0)]
    public virtual float MinAnimationSpeed { get; init; } = 1;

    [NeoMember("8bfadaa8-14e9-4488-a103-ee688c1cc9c4", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(NeoSmartTileRule.NeighborsEntries))]
    public virtual IReadOnlyList<NeoSmartTileNeighbor> Neighbors { get; init; } = new List<NeoSmartTileNeighbor> {  };

    private static IReadOnlyList<NeoEntrySettings> NeighborsEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "11873fad-7426-46cf-97d6-47b45bd1c091",
            Path = "$",
            Kind = NeoEntryKind.Class,
            Required = true,
            Locked = true,
            Virtual = true,
            Default = new NeoValueSettings { Object = new Dictionary<string, NeoValueSettings> {  } },
            Class = new()
            {
                Type = typeof(NeoSmartTileNeighbor),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };

    [NeoMember("9b22d8df-ea41-4c76-9d2e-a6c64c95a64a", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public virtual NeoSmartTileOutput Output { get; init; } = NeoSmartTileOutput.Single;

    [NeoMember("29a3d2a7-ce2e-4d40-bcdf-ee6c314023fe", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public virtual NeoSmartTileTransform RuleTransform { get; init; } = NeoSmartTileTransform.Fixed;

    [NeoMember("fd3a7f0f-cff8-4069-9b19-004015a6aca1", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(NeoSmartTileRule.SpritesEntries))]
    public virtual IReadOnlyList<NeoSprite> Sprites { get; init; } = new List<NeoSprite> {  };

    private static IReadOnlyList<NeoEntrySettings> SpritesEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "2cd93779-d755-4368-b064-2361463526ea",
            Path = "$",
            Kind = NeoEntryKind.Sprite,
            Required = true,
            Locked = true,
            Virtual = true,
            File = new()
            {
                Kind = NeoFileKind.Sprite,
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };
}
