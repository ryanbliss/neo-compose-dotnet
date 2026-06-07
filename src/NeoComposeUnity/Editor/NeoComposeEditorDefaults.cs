// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

namespace NeoCompose.Unity.Editor
{
    public static class NeoComposeEditorDefaults
    {
        public const string ApiBaseUrl = global::NeoCompose.Runtime.NeoComposeDefaults.ApiBaseUrl;
        public const string ConfigPath = "Assets/Resources/Neo/NeoComposeConfig.asset";
        public const string GeneratedTypesDirectory = global::NeoCompose.Runtime.NeoComposeDefaults.GeneratedTypesDirectory;
        public const string ProjectJsonDirectory = global::NeoCompose.Runtime.NeoComposeDefaults.ProjectJsonDirectory;
        public const string LocalizationResourcesDirectory = global::NeoCompose.Runtime.NeoComposeDefaults.LocalizationResourcesDirectory;
        public const string LocalizationStreamingAssetsDirectory = global::NeoCompose.Runtime.NeoComposeDefaults.LocalizationStreamingAssetsDirectory;
        public const string SpriteDirectory = global::NeoCompose.Runtime.NeoComposeDefaults.SpriteDirectory;
        public const string AudioClipDirectory = global::NeoCompose.Runtime.NeoComposeDefaults.AudioClipDirectory;
        public const string GeneratedTypesFileName = "NeoGeneratedTypes.cs";
        public const string ProjectJsonFileName = "project.json";
        public const string AssetDatabaseFileName = "NeoAssetDatabase.asset";

        /// <summary>
        /// The registered first-party OAuth client id for the Unity Editor
        /// device-authorization flow.
        /// </summary>
        public const string OAuthClientId = "neo-compose-unity";

        /// <summary>
        /// Space-delimited OAuth scopes requested by the Unity Editor device
        /// flow. Mirrors the web registration for <see cref="OAuthClientId"/>.
        /// </summary>
        public const string OAuthScopes =
            "openid profile:read project:list project:read project:version:read project:version:status:read project:release-channel:read unity:export unity:settings:write";

        /// <summary>
        /// Better Auth handler base path, relative to the configured origin.
        /// Everything resolves through one origin (the config API base URL).
        /// </summary>
        public const string AuthBasePath = global::NeoCompose.Runtime.NeoComposeDefaults.AuthBasePath;

        /// <summary>
        /// Web verification page path, relative to the configured origin.
        /// </summary>
        public const string DeviceVerificationPath = global::NeoCompose.Runtime.NeoComposeDefaults.DeviceVerificationPath;
    }
}
