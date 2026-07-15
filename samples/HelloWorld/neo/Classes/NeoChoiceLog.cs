// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoSchemaClass("af5795d0-e019-4776-8b7c-d0206f90d59f", Hidden = true)]
[NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
public partial class NeoChoiceLog
{
    [NeoMember("dd65f671-aedb-4c17-8849-1a3290b5c4d0", Locked = true)]
    [NeoSystem(NeoSystemDisallowedOperation.EditRecord, NeoSystemDisallowedOperation.DeleteRecord, Reason = "Generated dialogue memory schema required by the Neo Compose dialogue runtime.")]
    public virtual string ChoiceId { get; init; } = "48020dd6-3fb9-4b73-98be-fba38e00285b";
}
