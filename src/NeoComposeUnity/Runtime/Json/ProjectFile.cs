// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using Newtonsoft.Json.Linq;

namespace NeoCompose.Runtime.Json
{
    public sealed class ProjectFilePendingReplacement
    {
        public string name = null!;
        public string mimeType = null!;
        public double byteLength;
        public string storageKey = null!;
        public double? audioDurationSeconds;
        public NeoTimestamp createdAt;
    }

    public sealed class ProjectFile
    {
        public string id = null!;
        public string projectId = null!;
        public string status = null!;
        public string name = null!;
        public string fileType = null!;
        public string mimeType = null!;
        public double byteLength;
        public string storageKey = null!;
        public string? storageETag;
        public double? audioDurationSeconds;
        public ProjectFilePendingReplacement? pendingReplacement;
        public FileUnityTextureImportSettings? unityTextureSettings;
        public FileUnityAudioClipImportSettings? unityAudioClipSettings;
        public NeoTimestamp createdAt;
        public NeoTimestamp updatedAt;
    }

    public sealed class FileUnityTextureImportSettings
    {
        public string? templateId;
        public string? type;
        public string[]? overridePaths;
        public JObject? values;
    }

    public sealed class FileUnityAudioClipImportSettings
    {
        public string? templateId;
        public string[]? overridePaths;
        public JObject? values;
    }
}
