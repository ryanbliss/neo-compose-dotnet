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
    }
}
