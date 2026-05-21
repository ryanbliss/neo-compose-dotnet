// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;

namespace NeoCompose.Unity.Editor
{
    public sealed class NeoComposeProjectSummary
    {
        public string id = "";
        public string name = "";
        public NeoComposeProjectExportSettings? exportSettings;
    }

    public sealed class NeoComposeProjectListResponse
    {
        public List<NeoComposeProjectSummary> projects = new();
    }

    public sealed class NeoComposeProjectEditResponse
    {
        public NeoComposeProjectSummary project = new();
    }

    public sealed class NeoComposeProjectEditRequest
    {
        public NeoComposeProjectExportSettings? exportSettings;
    }

    public sealed class NeoComposeProjectVersionSemver
    {
        public int major;
        public int minor;
        public int patch;
        public string label = "";
    }

    public sealed class NeoComposeProjectVersion
    {
        public string id = "";
        public string projectId = "";
        public NeoComposeProjectVersionSemver semver = new();
        public string statusId = "";
        public string? archivedAt;
    }

    public sealed class NeoComposeProjectVersionStatus
    {
        public string id = "";
        public string projectId = "";
        public string name = "";
        public int sortOrder;
        public string? archivedAt;
        public bool isWritable;
        public string[] releaseChannelIds = System.Array.Empty<string>();
    }

    public sealed class NeoComposeProjectReleaseChannel
    {
        public string id = "";
        public string projectId = "";
        public string name = "";
        public string slug = "";
        public int sortOrder;
    }

    public sealed class NeoComposeProjectReleaseChannelListResponse
    {
        public List<NeoComposeProjectReleaseChannel> channels = new();
    }

    public sealed class NeoComposeProjectVersionListResponse
    {
        public List<NeoComposeProjectVersion> versions = new();
    }

    public sealed class NeoComposeProjectVersionStatusListResponse
    {
        public List<NeoComposeProjectVersionStatus> statuses = new();
    }

    public sealed class NeoComposeProjectVersionMetadataResponse
    {
        public NeoComposeProjectVersion version = new();
        public NeoComposeProjectVersionStatus versionStatus = new();
        public List<NeoComposeProjectVersionStatus> versionStatuses = new();
        public List<NeoComposeProjectReleaseChannel> releaseChannels = new();
    }

    public sealed class NeoComposeProjectExportSettings
    {
        public NeoComposeUnityExportSettings? unity;
    }

    public sealed class NeoComposeUnityExportSettings
    {
        public string? namespaceForGeneratedTypes;
        public bool? singleton;
    }

    public sealed class NeoComposeCodegenDiagnostic
    {
        public string severity = "";
        public string message = "";
        public string? path;
    }

    public sealed class NeoComposeUnityExportResponse
    {
        public string projectId = "";
        public string projectName = "";
        public string projectJson = "";
        public string generatedTypes = "";
        public List<NeoComposeCodegenDiagnostic> diagnostics = new();
        public NeoComposeProjectVersion? version;
        public NeoComposeProjectVersionStatus? versionStatus;
        public List<NeoComposeProjectReleaseChannel> releaseChannels = new();
        public string? projectDocumentContentHash;
        public string? codegenContractHash;
        public string? runtimeDataContractHash;
    }

    public sealed class NeoComposeUnityExportFileDownloadRequest
    {
        public string versionId = "";
        public string[] fileIds = System.Array.Empty<string>();
    }

    public sealed class NeoComposeUnityExportFileDownload
    {
        public string fileId = "";
        public string downloadUrl = "";
        public string expiresAt = "";
    }

    public sealed class NeoComposeUnityExportFileDownloadResponse
    {
        public Dictionary<string, NeoComposeUnityExportFileDownload> files = new();
    }
}
