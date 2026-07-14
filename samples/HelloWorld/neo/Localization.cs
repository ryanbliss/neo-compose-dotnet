// Canonical Neo Compose schema projection — managed by `neo`.
// Native C# is authoritative. NeoScript bodies live under Scripts/.

using NeoCompose.Schema;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace ProjectSchema;

[NeoLocalization("localization-config")]
public sealed partial class Localization
{
    public static NeoLocalizationSettings Settings { get; } = new()
    {
        MainLocale = "en-US",
        MainLocaleDefaultStatus = "localization-status-approved",
        StatusIds = new[] { "localization-status-needs-translation", "localization-status-in-progress", "localization-status-needs-review", "localization-status-update-needed", "localization-status-blocked-by-predecessor", "localization-status-approved" },
        Locales =
        new NeoLocaleSettings[]
        {
            new NeoLocaleSettings
            {
                Locale = "en-US",
                SourceLocale = null,
                Name = "English (US)",
                Order = 0,
                ArchivedAt = null,
            },
            new NeoLocaleSettings
            {
                Locale = "es-ES",
                SourceLocale = "en-US",
                Name = "Spanish (Spain)",
                Order = 1,
                ArchivedAt = null,
            },
        },
    };
}
