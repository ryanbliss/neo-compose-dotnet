// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoLocalizationStatus("localization-status-needs-translation")]
public sealed partial class NeedsTranslation
{
    public static NeoLocalizationStatusSettings Settings { get; } = new()
    {
        Slug = "needs-translation",
        Name = "Needs translation",
        Description = "The text has not been translated for this locale yet.",
        Color = null,
        Emoji = "🆕",
        ArchivedAt = null,
        TransitionRules = null,
        TransitionOnTextEdit = "localization-status-needs-review",
        TransitionWhenSourceBecomes = "localization-status-blocked-by-predecessor",
        System = null,
    };
}
