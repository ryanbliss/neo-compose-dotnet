// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("c1dc3dd7-397a-4f8f-acbf-b928cc66076d", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder."",""worldKind"":""objectLayer""}}")]
public abstract class NeoObjectLayer
{
    [NeoString("788a3dbc-6167-4320-83aa-1e884924f776", Locked = true, Localizable = false, DefaultJson = @"{""value"":""""}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public string Name { get; init; }

    [NeoLookup("89dbb12e-1d7c-42ad-b872-63fc7fe8bd5b", Locked = true, CollectionId = "7ec79ef8-a216-407f-8886-9f770bc9895b", CollectionValueId = "e82662a0-10be-45cb-8c4a-a5d8b6b5bb0c", DefaultJson = @"{""value"":[""88c0d53b-94ee-4f48-839b-9148d07828fb""]}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public NeoLookupRef SortingLayer { get; init; }

    [NeoInt("472dfd84-59cb-4516-8763-90d0af6d039f", Locked = true, Min = -32768, Max = 32767, DefaultJson = @"{""value"":0}", ExtraJson = @"{""system"":{""disallow"":[""editRecord"",""deleteRecord"",""selectRecord""],""reason"":""Locked world authoring system type required by the Neo Compose Tile Grid Builder.""}}")]
    public int SortingOrder { get; init; }
}
