// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using UnityEngine;

namespace NeoCompose.Runtime
{
    public sealed class NeoComposeConfig : ScriptableObject
    {
        public string apiBaseUrl = NeoComposeDefaults.ApiBaseUrl;
        public string projectId = "";
        public string projectName = "";
        public string targetReleaseChannelId = "";
        public string versionId = "";
        public string generatedTypesDirectory = NeoComposeDefaults.GeneratedTypesDirectory;
        public string projectJsonDirectory = NeoComposeDefaults.ProjectJsonDirectory;
        public string localizationResourcesDirectory = NeoComposeDefaults.LocalizationResourcesDirectory;
        public string localizationStreamingAssetsDirectory = NeoComposeDefaults.LocalizationStreamingAssetsDirectory;
        public bool useStreamingAssetsForNonRootLocales;
        public bool preloadSystemLocale = true;
        public string localeOverride = "";
        public string spriteDirectory = NeoComposeDefaults.SpriteDirectory;
        public string audioClipDirectory = NeoComposeDefaults.AudioClipDirectory;
        public string namespaceForGeneratedTypes = NeoComposeDefaults.NamespaceForGeneratedTypes;
        public bool singleton = NeoComposeDefaults.Singleton;

        public bool HasProject => !string.IsNullOrWhiteSpace(projectId);

        public void SelectProject(
            string id,
            string name,
            string? namespaceForGeneratedTypes = null,
            bool singleton = NeoComposeDefaults.Singleton)
        {
            if (projectId != id)
            {
                targetReleaseChannelId = "";
                versionId = "";
            }

            projectId = id;
            projectName = name;
            this.namespaceForGeneratedTypes = string.IsNullOrWhiteSpace(namespaceForGeneratedTypes)
                ? NeoComposeDefaults.NamespaceForGeneratedTypes
                : namespaceForGeneratedTypes;
            this.singleton = singleton;
        }

        public void ClearProject()
        {
            projectId = "";
            projectName = "";
            targetReleaseChannelId = "";
            versionId = "";
        }
    }
}
