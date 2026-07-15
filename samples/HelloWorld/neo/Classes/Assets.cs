// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("dd0bbe5a-47ef-4164-9421-caea07f6f56f", Hidden = true)]
public partial class Assets
{
    [NeoMember("3b73b328-9a9d-4ee3-955c-9cc573170346")]
    public virtual Art Art { get; init; } = new();

    [NeoMember("6cc17e87-3907-4f96-84a2-b0b5a32bac25")]
    public virtual Audio Audio { get; init; } = new();

    [NeoMember("92fe2bbb-e542-40a7-9d5e-7a7ad5b9abca")]
    public virtual ComputedText Computed { get; init; } = default!;

    [NeoMember("214df1a1-abca-4141-987b-380a5417c70a")]
    [NeoList]
    [NeoEntries(nameof(Assets.ItemsEntries))]
    public virtual IReadOnlyList<Item> Items { get; init; } = new List<Item> {  };

    private static IReadOnlyList<NeoEntrySettings> ItemsEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "dd6686e3-6435-47a3-b455-4cfbadad14ff",
            Path = "$",
            Kind = NeoEntryKind.Class,
            Required = true,
            Virtual = true,
            Class = new()
            {
                Type = typeof(Item),
            },
        },
    };

    [NeoMember("2c57948b-7479-47e0-b97c-242f6d543ae0")]
    public virtual LookupContainer LookupContainer { get; init; } = default!;

    [NeoMember("ef275539-d049-42da-bf44-541375ab0bf8", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    public virtual NeoWorlds NeoWorlds { get; init; } = default!;

    [NeoMember("2827aefd-7f57-48ea-994c-c5c39ec659e3")]
    [NeoList]
    [NeoColumn("Name", Width = 158, Frozen = true)]
    [NeoColumn("Image", Width = 96, Frozen = true)]
    [NeoColumn("Planet", Width = 115)]
    [NeoColumn("FullDisplayText", Width = 223)]
    [NeoColumn("SaveUnsafe", Width = 221)]
    [NeoColumn("Save", Width = 264, Hidden = true)]
    [NeoColumn("DebugLog", Hidden = true)]
    [NeoColumn("AnimatedImage", Width = 298)]
    [NeoColumn("PlayAnimation", Hidden = true)]
    [NeoColumn("ShowRelic", Hidden = true)]
    [NeoEntries(nameof(Assets.OutpostsEntries))]
    public virtual IReadOnlyList<Outpost> Outposts { get; init; } = new List<Outpost> {  };

    private static IReadOnlyList<NeoEntrySettings> OutpostsEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "9619809a-f7f0-4605-8f79-d1617b339819",
            Path = "$",
            Kind = NeoEntryKind.Class,
            Required = true,
            Virtual = true,
            Class = new()
            {
                Type = typeof(Outpost),
            },
        },
    };

    [NeoMember("7ec79ef8-a216-407f-8886-9f770bc9895b", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.")]
    [NeoList]
    [NeoEntries(nameof(Assets.SortingLayersEntries))]
    public virtual IReadOnlyList<NeoSortingLayer> SortingLayers { get; init; } = new List<NeoSortingLayer> { Neo.Ref<NeoSortingLayer>("88c0d53b-94ee-4f48-839b-9148d07828fb") };

    private static IReadOnlyList<NeoEntrySettings> SortingLayersEntries { get; } =
    new NeoEntrySettings[]
    {
        new NeoEntrySettings
        {
            Id = "10be115c-1d1f-49f2-ae26-ab4b52657fda",
            Path = "$",
            Kind = NeoEntryKind.Class,
            Required = true,
            Locked = true,
            Virtual = true,
            Default = new NeoValueSettings { Object = new Dictionary<string, NeoValueSettings> {  } },
            Class = new()
            {
                Type = typeof(NeoSortingLayer),
            },
            System = new NeoSystemSettings { Disallow = new[] { NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, NeoSystemDisallowedOperation.SelectRecord }, Reason = "Locked world authoring system type required by the Neo Compose Tile Grid Builder.", },
        },
    };

    [NeoMember("80d05ad2-08ff-4e17-8b29-8b185562b2c6")]
    public virtual Worlds Worlds { get; init; } = default!;
}
