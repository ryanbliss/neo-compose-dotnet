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
        public const string SpriteDirectory = "Assets/Resources/Neo/Files/Sprites";
        public const string AudioClipDirectory = "Assets/Resources/Neo/Files/Audio";
        public const string AssetDatabaseResourcePath = "Neo/NeoAssetDatabase";
        public const string NamespaceForGeneratedTypes = "Assets.Scripts.Neo";
        public const bool Singleton = true;
    }
}
