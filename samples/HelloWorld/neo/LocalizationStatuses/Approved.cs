// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoLocalizationStatus("localization-status-approved")]
public sealed partial class Approved
{
    public static NeoLocalizationStatusSettings Settings { get; } = new()
    {
        Slug = "approved",
        Name = "Approved",
        Description = "The text is ready for release.",
        Color = null,
        Emoji = "✅",
        ArchivedAt = null,
        TransitionRules = null,
        TransitionOnTextEdit = null,
        TransitionWhenSourceBecomes = null,
        System = null,
    };
}
