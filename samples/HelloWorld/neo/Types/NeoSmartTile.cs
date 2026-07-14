// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("89b38b34-c040-4e69-8707-487f1484a056")]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", WorldKind = NeoWorldKind.SmartTile)]
public partial class NeoSmartTile
{
    [NeoMember("0a51bdef-4b3e-49d6-913b-11cbea98bced", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public virtual NeoSmartTileCollider DefaultCollider { get; init; } = NeoSmartTileCollider.Sprite;

    [NeoMember("97cb9d95-2d54-4809-ae88-0b0ba7859248", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(NeoSmartTile.RulesEntries))]
    public virtual IReadOnlyList<NeoSmartTileRule> Rules { get; init; } = new List<NeoSmartTileRule> {  };

    private static IReadOnlyList<NeoEntrySettings> RulesEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "fa146cb3-b0d5-4a87-a781-918b78307b20",
            Path = "$",
            Kind = NeoEntryKind.Custom,
            Required = true,
            Locked = true,
            Virtual = true,
            Default = new NeoValueSettings { Object = new Dictionary<string, NeoValueSettings> {  } },
            Custom = new()
            {
                Type = typeof(NeoSmartTileRule),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };
}
