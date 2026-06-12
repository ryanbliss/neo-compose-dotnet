// Canonical Neo Compose schema projection — managed by `neo`.
// Edit freely within the constrained subset; pull/push rewrite this file canonically.

using NeoCompose.Schema;

namespace ProjectSchema;

[NeoLocalization("localization-config", ExtraJson = @"{""mainLocale"":""en-US"",""mainLocaleDefaultStatusId"":""localization-status-approved"",""sortedStatusIds"":[""localization-status-needs-translation"",""localization-status-in-progress"",""localization-status-needs-review"",""localization-status-update-needed"",""localization-status-blocked-by-predecessor"",""localization-status-approved""],""supportedLocales"":[{""archivedAt"":null,""locale"":""en-US"",""name"":""English (US)"",""sortOrder"":0,""sourceLocale"":null},{""archivedAt"":null,""locale"":""es-ES"",""name"":""Spanish (Spain)"",""sortOrder"":1,""sourceLocale"":""en-US""}]}")]
public sealed class Localization
{
}
