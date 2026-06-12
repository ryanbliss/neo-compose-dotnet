// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoCustomType("af5795d0-e019-4776-8b7c-d0206f90d59f", Hidden = true, ExtraJson = @"{""extendsTypeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
public sealed class NeoChoiceLog
{
    [NeoString("dd65f671-aedb-4c17-8849-1a3290b5c4d0", Locked = true, DefaultJson = @"{""value"":""48020dd6-3fb9-4b73-98be-fba38e00285b""}", ExtraJson = @"{""extendsAttributeId"":null,""system"":{""disallow"":[""editRecord"",""deleteRecord""],""reason"":""Generated dialogue memory schema required by the Neo Compose dialogue runtime.""}}")]
    public string ChoiceId { get; init; }
}
