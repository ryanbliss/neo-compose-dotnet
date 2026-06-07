// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Runtime
{
    public static class NeoComposeDefaults
    {
        public const string ApiBaseUrl = "http://localhost:3000";
        public const string GeneratedTypesDirectory = "Assets/Scripts/Neo";
        public const string ProjectJsonDirectory = "Assets/Resources/Neo";

        /// <summary>
        /// Resources-relative file name (no extension) of the bundled project
        /// JSON <c>TextAsset</c> (the editor writes <c>project.json</c>, which
        /// Unity imports as a <c>TextAsset</c> named <c>project</c>).
        /// </summary>
        public const string ProjectJsonResourceName = "project";
        public const string LocalizationResourcesDirectory = "Assets/Resources/Neo/Localization";
        public const string LocalizationStreamingAssetsDirectory = "Assets/StreamingAssets/Neo/Localization";
        public const string LocalizationResourcesPath = "Neo/Localization";
        public const string LocalizationStreamingAssetsRelativePath = "Neo/Localization";
        public const string ConfigResourcePath = "Neo/NeoComposeConfig";

        /// <summary>
        /// Resources-relative path (no extension) of the gitignored runtime secret
        /// asset (<see cref="NeoComposeRuntimeSecret"/>) holding the runtime API key.
        /// </summary>
        public const string RuntimeSecretResourcePath = "Neo/NeoComposeRuntimeSecret";
        public const string SpriteDirectory = "Assets/Resources/Neo/Files/Sprites";
        public const string AudioClipDirectory = "Assets/Resources/Neo/Files/Audio";
        public const string AssetDatabaseResourcePath = "Neo/NeoAssetDatabase";
        public const string NamespaceForGeneratedTypes = "Assets.Scripts.Neo";
        public const bool Singleton = true;

        /// <summary>
        /// Better Auth handler base path, relative to the configured origin.
        /// Shared by the editor and runtime device-authorization flows.
        /// </summary>
        public const string AuthBasePath = "/api/auth";

        /// <summary>
        /// Web verification page path, relative to the configured origin.
        /// </summary>
        public const string DeviceVerificationPath = "/auth/device";
    }
}
