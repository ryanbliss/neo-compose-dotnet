// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("8ccfe860-309f-428b-b74c-76a873bdea8a", ExtraJson = @"{""extendsTypeId"":null,""system"":null}")]
public sealed class OutpostSaveData
{
    [NeoBool("5a4d8d10-9fef-4197-a7a1-1dc1b112677b", DefaultJson = @"{""value"":false}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public bool Unlocked { get; init; }

    [NeoProperty("0c02e45d-bb5b-44c3-8b48-91fa75171ff2", Code = @"	return this.VisitCount > 0;", RetJson = @"{""required"":true,""type"":1}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public object? Visited { get; init; }

    [NeoInt("68cd6fe1-7683-4c67-8030-acd6334f77a2", Min = 0, DefaultJson = @"{""value"":0}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public int VisitCount { get; init; }

    [NeoInt("1d1d14bc-987e-4079-a8f8-09998d5954fc", DefaultJson = @"{""value"":0}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public int Reputation { get; init; }
}
