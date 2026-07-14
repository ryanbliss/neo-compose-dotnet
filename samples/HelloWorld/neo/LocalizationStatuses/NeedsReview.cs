// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoLocalizationStatus("localization-status-needs-review")]
public sealed partial class NeedsReview
{
    public static NeoLocalizationStatusSettings Settings { get; } = new()
    {
        Slug = "needs-review",
        Name = "Needs review",
        Description = "The text has been edited and is waiting for review.",
        Color = null,
        Emoji = "📚",
        ArchivedAt = null,
        TransitionRules = null,
        TransitionOnTextEdit = null,
        TransitionWhenSourceBecomes = "localization-status-update-needed",
        System = null,
    };
}
