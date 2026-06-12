// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("96e8284d-ae43-4e91-919d-86c25ce098e0", Hidden = true, ExtraJson = @"{""extendsTypeId"":null,""system"":null}")]
public sealed class Save
{
    [NeoInt("1f76e191-faaf-4840-9bb0-29785a1c8ed6", Min = 0, DefaultJson = @"{""value"":1000}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public int Bits { get; init; }

    [NeoBool("5155954d-ca1d-441e-b9ee-1837b0a08e05", DefaultJson = @"{""value"":false}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public bool Dead { get; init; }

    [NeoLookup("bf4a75b5-acf9-4ff8-8511-57f871749db3", CollectionId = "214df1a1-abca-4141-987b-380a5417c70a", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public IReadOnlyList<NeoLookupRef> Inventory { get; init; }

    [NeoLookup("82a12a52-95ac-453a-9353-24c11a63c530", CollectionId = "2827aefd-7f57-48ea-994c-c5c39ec659e3", DefaultJson = @"{""value"":[""913fd757-d6ee-488e-866f-c7af41aa544b""]}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public NeoLookupRef Location { get; init; }

    [NeoObject("99b07db1-f732-437e-a903-98183edca96b", Locked = true, DefaultJson = @"{""value"":{}}", ExtraJson = @"{""extendsAttributeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord"",""replaceValue""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
    public NeoMemory NeoMemory { get; init; }

    [NeoDictionary("f977a94e-aa40-414c-9812-dacdd50110a8", EntryChainJson = @"[{""customTypeId"":""8ccfe860-309f-428b-b74c-76a873bdea8a"",""defaultValue"":{""value"":{}},""extendsAttributeId"":null,""id"":""42d3d49d-3ba8-4672-ad54-53dc109697fc"",""locked"":false,""name"":""OutpostSaveData"",""required"":false,""system"":null,""type"":7}]", DefaultJson = @"{""value"":{}}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public IReadOnlyDictionary<string, OutpostSaveData> OutpostSaveMap { get; init; }

    [NeoList("c151eda4-ecce-4edf-988d-25a97c657146", EntryChainJson = @"[{""customTypeId"":""7755a905-f2a1-4e5d-8b60-78cbdd2b2042"",""defaultValue"":{""value"":{}},""extendsAttributeId"":null,""id"":""659589fe-95b2-472f-a53b-e305db97450f"",""locked"":false,""name"":""visits"",""required"":true,""type"":7}]", DefaultJson = @"{""value"":[]}", ExtraJson = @"{""extendsAttributeId"":null}")]
    public IReadOnlyList<PlanetVisit> Visited { get; init; }

    [NeoEnum("ebab06a2-98e9-4a30-bb13-cfea8f910462", DefaultJson = @"{""value"":[""earth""]}", ExtraJson = @"{""extendsAttributeId"":null}")]
    public Planet World { get; init; }
}
