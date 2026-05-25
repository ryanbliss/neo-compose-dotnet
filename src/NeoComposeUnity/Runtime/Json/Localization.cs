// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Runtime.Json
{
    public class ProjectLocalizationExport
    {
        public int schemaVersion;
        public string mainLocale = null!;
        public ProjectLocalizationLocale[] supportedLocales = System.Array.Empty<ProjectLocalizationLocale>();
        public string[] textIds = System.Array.Empty<string>();
        public string mainLocaleFileName = null!;
        public Dictionary<string, string> localeFileNames = new();
        public ProjectLocalizationFormatting formatting = new();
    }

    public class ProjectLocalizationLocale
    {
        public string locale = null!;
        public string? sourceLocale;
        public string? archivedAt;
    }

    public class ProjectLocalizationFormatting
    {
        public string syntax = "";
        public string sourceSyntax = "";
    }

    public class ProjectLocalizationLocaleFile
    {
        public int schemaVersion;
        public string projectId = null!;
        public string versionId = null!;
        public string locale = null!;
        public string? sourceLocale;
        public string formattingSyntax = "";
        public Dictionary<string, string?> values = new();
    }
}
