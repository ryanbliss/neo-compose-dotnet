// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("2ab1bc07-da0b-47fc-b77b-54cc511575bb", ExtraJson = @"{""extendsTypeId"":null}")]
public sealed class ComputedText
{
    [NeoString("29659610-fc70-4b9c-833e-a6185f745c04", DefaultJson = @"{""value"":""5ed5d8c1-a01d-47ff-b2ca-d663be283d79""}", ExtraJson = @"{""extendsAttributeId"":null}")]
    public string baseText { get; init; }

    [NeoGetter("acf7a92c-9ede-4a0d-a00c-c8c64e7a9b80", Code = @"	Planet planet = root.Save.World;
	string suffix = this.optionalSuffix ?? """";
	return $""{this.baseText} {planet}{suffix}"";", RetJson = @"{""required"":true,""type"":3}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public object? fullText { get; init; }

    [NeoString("d56d641e-4f15-4da6-8c1f-114016c9166d", ExtraJson = @"{""extendsAttributeId"":null}")]
    public string? optionalSuffix { get; init; }
}
