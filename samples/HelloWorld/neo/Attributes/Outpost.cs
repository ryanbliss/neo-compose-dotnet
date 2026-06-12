// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("4c196697-4e08-4aeb-823f-322b353071ac", ExtraJson = @"{""extendsTypeId"":null,""system"":null}")]
public sealed class Outpost
{
    [NeoObject("d7607422-7e33-402f-9fe6-8e9ac85a7019", DefaultJson = @"null", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public AnimationInfo? AnimatedImage { get; init; }

    [NeoFunction("e549555b-9276-48d8-be33-156972520d31", RetJson = @"{""required"":true,""type"":3}", ArgsJson = @"[{""name"":""text"",""required"":true,""type"":3}]", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public NeoFunctionValue? DebugLog { get; init; }

    [NeoGetter("514a79c5-dd23-4ede-9fc8-b07b3c866fe5", Code = @"	return $""{this.Name}, {this.Planet}"";", RetJson = @"{""required"":true,""type"":3}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public object? FullDisplayText { get; init; }

    [NeoSprite("7ce8a389-265c-4ad4-90f4-42c3e91e7648", ExtraJson = @"{""extendsAttributeId"":null,""system"":null,""templateId"":null}")]
    public NeoSpriteValue Image { get; init; }

    [NeoString("b56410b3-b2da-4681-897f-a25ce0a0ceb1", DefaultJson = @"null", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public string Name { get; init; }

    [NeoEnum("3166fd08-4bdb-4df9-b100-ecccec859443", DefaultJson = @"null", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public Planet Planet { get; init; }

    [NeoFunction("cab850e3-cf8c-42b3-a70b-f0066089e6fb", RetJson = @"{""required"":true,""type"":1}", ArgsJson = @"[]", Deferred = true, ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public NeoFunctionValue? PlayAnimation { get; init; }

    [NeoGetter("cccadaa5-0623-4a0f-9197-7175726c0e8b", Code = @"	return this.SaveUnsafe!;", RetJson = @"{""required"":true,""type"":7,""typeId"":""8ccfe860-309f-428b-b74c-76a873bdea8a""}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public object? Save { get; init; }

    [NeoGetter("f66fba24-44d4-467c-98ac-4db1539910df", Code = @"	return root.Save.OutpostSaveMap.FirstOrDefault((key, value) => { return key == this.Id; });", RetJson = @"{""required"":false,""type"":7,""typeId"":""8ccfe860-309f-428b-b74c-76a873bdea8a""}", ExtraJson = @"{""extendsAttributeId"":null,""system"":null}")]
    public object? SaveUnsafe { get; init; }

    [NeoFunction("736ca2ec-5f56-4f93-8cc5-c8b2ae8f76a1", RetJson = @"{""required"":true,""type"":1}", ArgsJson = @"[]")]
    public NeoFunctionValue? ShowRelic { get; init; }
}
