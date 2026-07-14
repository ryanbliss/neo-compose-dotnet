// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoLocalizationStatus("localization-status-in-progress")]
public sealed partial class InProgress
{
    public static NeoLocalizationStatusSettings Settings { get; } = new()
    {
        Slug = "in-progress",
        Name = "In progress",
        Description = "A translator is actively working on the text.",
        Color = null,
        Emoji = "✍️",
        ArchivedAt = null,
        TransitionRules = null,
        TransitionOnTextEdit = "localization-status-needs-review",
        TransitionWhenSourceBecomes = "localization-status-blocked-by-predecessor",
        System = null,
    };
}
