// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    public sealed class NeoLocalizationOptions
    {
        public string? localeOverride;
        public bool useStreamingAssetsForNonRootLocales;
        public bool preloadSystemLocale = true;
        public string streamingAssetsRelativePath = NeoComposeDefaults.LocalizationStreamingAssetsRelativePath;
    }
}
