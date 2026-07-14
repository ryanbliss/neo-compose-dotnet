// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoLocalizationStatus("localization-status-update-needed")]
public sealed partial class UpdateNeeded
{
    public static NeoLocalizationStatusSettings Settings { get; } = new()
    {
        Slug = "update-needed",
        Name = "Update needed",
        Description = "The source text changed after this locale's text was translated.",
        Color = null,
        Emoji = "🚧",
        ArchivedAt = null,
        TransitionRules = null,
        TransitionOnTextEdit = "localization-status-needs-review",
        TransitionWhenSourceBecomes = "localization-status-blocked-by-predecessor",
        System = null,
    };
}
