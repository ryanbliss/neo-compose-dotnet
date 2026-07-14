// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("4c196697-4e08-4aeb-823f-322b353071ac")]
public partial class Outpost : IHasName
{
    [NeoMember("d7607422-7e33-402f-9fe6-8e9ac85a7019")]
    [NeoSchemaOrder("FPS", "Name", "Frames")]
    public virtual AnimationInfo? AnimatedImage { get; init; }

    [NeoMember("e549555b-9276-48d8-be33-156972520d31")]
    [NeoFunction]
    public virtual partial string DebugLog(string text);

    #pragma warning disable CS8618
    [NeoMember("514a79c5-dd23-4ede-9fc8-b07b3c866fe5")]
    [NeoComputed]
    public virtual string FullDisplayText { get; }
    #pragma warning restore CS8618

    [NeoMember("7ce8a389-265c-4ad4-90f4-42c3e91e7648")]
    [NeoFile(NeoFileKind.Sprite)]
    public virtual NeoSprite Image { get; init; } = default!;

    [NeoMember("b56410b3-b2da-4681-897f-a25ce0a0ceb1")]
    [NeoText(SearchKey = true)]
    public virtual string Name { get; init; } = default!;

    [NeoMember("3166fd08-4bdb-4df9-b100-ecccec859443")]
    public virtual Planet Planet { get; init; }

    [NeoMember("cab850e3-cf8c-42b3-a70b-f0066089e6fb")]
    [NeoFunction]
    public virtual partial Task<bool> PlayAnimation();

    #pragma warning disable CS8618
    [NeoMember("cccadaa5-0623-4a0f-9197-7175726c0e8b")]
    [NeoComputed]
    public virtual OutpostSaveData Save { get; }
    #pragma warning restore CS8618

    [NeoMember("f66fba24-44d4-467c-98ac-4db1539910df")]
    [NeoComputed]
    public virtual OutpostSaveData? SaveUnsafe { get; }

    [NeoMember("736ca2ec-5f56-4f93-8cc5-c8b2ae8f76a1")]
    [NeoFunction]
    public virtual partial bool ShowRelic();
}
