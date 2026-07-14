// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoLocalizationStatus("localization-status-blocked-by-predecessor")]
public sealed partial class BlockedByPredecessor
{
    public static NeoLocalizationStatusSettings Settings { get; } = new()
    {
        Slug = "blocked-by-predecessor",
        Name = "Blocked by predecessor",
        Description = "A source locale earlier in the fallback chain needs attention first.",
        Color = null,
        Emoji = "🛑",
        ArchivedAt = null,
        TransitionRules = null,
        TransitionOnTextEdit = "localization-status-needs-review",
        TransitionWhenSourceBecomes = "localization-status-blocked-by-predecessor",
        System = null,
    };
}
