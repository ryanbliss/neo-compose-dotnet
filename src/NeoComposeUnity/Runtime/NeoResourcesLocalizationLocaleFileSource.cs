// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime.Json;
using Newtonsoft.Json;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace NeoCompose.Runtime
{
    public sealed class NeoResourcesLocalizationLocaleFileSource : INeoLocalizationLocaleFileSource
    {
        public bool TryLoadResourcesLocale(
            ProjectLocalizationExport localization,
            string locale,
            out ProjectLocalizationLocaleFile? file)
        {
            file = null;
            if (!localization.localeFileNames.TryGetValue(locale, out var fileName))
            {
                return false;
            }

            var resourcePath = NeoLocalizationResourcePath(fileName);
            var asset = Resources.Load<TextAsset>(resourcePath);
            if (asset == null)
            {
                Debug.LogWarning(
                    $"Neo Compose localization file for locale '{locale}' was not found at Resources/{resourcePath}.");
                return false;
            }

            try
            {
                file = JsonConvert.DeserializeObject<ProjectLocalizationLocaleFile>(asset.text);
                return file != null;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"Neo Compose localization file for locale '{locale}' could not be deserialized: {ex.Message}");
                return false;
            }
        }

        public async Task<ProjectLocalizationLocaleFile?> LoadStreamingAssetsLocaleAsync(
            ProjectLocalizationExport localization,
            string locale,
            string streamingAssetsRelativePath)
        {
            if (!localization.localeFileNames.TryGetValue(locale, out var fileName))
            {
                return null;
            }

            var path = Path.Combine(Application.streamingAssetsPath, streamingAssetsRelativePath, fileName);
            using var request = UnityWebRequest.Get(path);
            var operation = request.SendWebRequest();
            while (!operation.isDone)
            {
                await Task.Yield();
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning(
                    $"Neo Compose streaming localization file for locale '{locale}' could not be loaded from {path}: {request.error}");
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<ProjectLocalizationLocaleFile>(request.downloadHandler.text);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"Neo Compose streaming localization file for locale '{locale}' could not be deserialized: {ex.Message}");
                return null;
            }
        }

        private static string NeoLocalizationResourcePath(string fileName)
        {
            var withoutExtension = fileName.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - ".json".Length)
                : fileName;
            return $"{NeoComposeDefaults.LocalizationResourcesPath}/{withoutExtension}";
        }
    }
}
