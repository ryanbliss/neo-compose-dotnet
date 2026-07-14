// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoType("47a1f7dd-b16d-4f04-96f8-6c0199d18c7b", AllowedStorage = NeoAllowedStorage.Save)]
public partial class BlockedPath : NeoTileLayerLink
{
    [NeoMember("e0316e12-b70d-4d4a-9c16-1861ce394849")]
    [NeoDialogue]
    public virtual NeoDialogue BootGlyphSealLocked { get; init; } = Neo.Dialogue("2a49e84a-ab1f-4468-a9a3-f29796cbf086");

    [NeoMember("0659d085-0267-4d6e-8e18-b726f393f740")]
    [NeoDialogue]
    public virtual NeoDialogue BootGlyphSealReady { get; init; } = Neo.Dialogue("d755935f-4c3a-4d43-8c40-4ba3f7d28063");

    [NeoMember("0fa12fa0-9e74-4e85-9ce3-df0efe78d2dd")]
    [NeoFunction]
    public virtual partial bool ClearPath();
}
