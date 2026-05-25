// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using System.Threading.Tasks;

namespace NeoCompose.Runtime
{
    public interface INeoLocalizationLocaleFileSource
    {
        bool TryLoadResourcesLocale(
            ProjectLocalizationExport localization,
            string locale,
            out ProjectLocalizationLocaleFile? file);

        Task<ProjectLocalizationLocaleFile?> LoadStreamingAssetsLocaleAsync(
            ProjectLocalizationExport localization,
            string locale,
            string streamingAssetsRelativePath);
    }
}
