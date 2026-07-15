// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("f1b08825-2ad0-4666-acf1-3df7ffbda64e")]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", WorldKind = NeoWorldKind.ObjectLayerLink)]
public partial class NeoObjectLayerLink : NeoLayerGroupBase
{
    [NeoMember("9cc0ab67-e138-4d11-8011-fab7d7a75b13", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoLookup(nameof(NeoWorlds.ObjectLayers), CollectionValueMember = "aa467eba-bc17-4cc6-933d-4c539caba2ad")]
    public virtual NeoLookup<NeoObjectLayer> ObjectLayer { get; init; } = Neo.Lookup<NeoObjectLayer>("8f96912d-5bbb-428c-84eb-8932ef588123");

    [NeoMember("f8e217b1-da89-4819-9c8d-e9c9da2bdfb2", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList(Kind = NeoListKind.Unordered)]
    [NeoEntries(nameof(NeoObjectLayerLink.ObjectsEntries))]
    public virtual IReadOnlyList<NeoObjectBase> Objects { get; init; } = new List<NeoObjectBase> {  };

    private static IReadOnlyList<NeoEntrySettings> ObjectsEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "934a2525-f95d-4c09-9504-b71da30b9186",
            Path = "$",
            Kind = NeoEntryKind.Class,
            Required = true,
            Locked = true,
            Virtual = true,
            Default = new NeoValueSettings { Object = new Dictionary<string, NeoValueSettings> {  } },
            Class = new()
            {
                Type = typeof(NeoObjectBase),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };
}
