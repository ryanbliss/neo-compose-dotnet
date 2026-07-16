// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json.Linq;

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

        /// <summary>"branch" | "release"; legacy rows omit it (treated as releases).</summary>
        public string? kind;

        /// <summary>Branch name; branches carry a placeholder semver, so display this instead.</summary>
        public string? name;
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
        public string mode = "full";
        public string projectId = "";
        public string projectName = "";
        public string projectJson = "";
        public string generatedTypes = "";
        public List<NeoComposeUnityLocalizationFile> localizationFiles = new();
        public List<NeoComposeCodegenDiagnostic> diagnostics = new();
        public NeoComposeProjectVersion? version;
        public NeoComposeProjectVersionStatus? versionStatus;
        public List<NeoComposeProjectReleaseChannel> releaseChannels = new();
        public string? projectDocumentContentHash;
        public string? codegenContractHash;
        public string? runtimeDataContractHash;
        public NeoComposeUnityRuntimeOAuthConfig? runtimeOAuth;
        public NeoComposeUnityExportSyncState? syncState;

        /// <summary>
        /// Convex deployment URL for realtime sync; null when the server has
        /// none configured (the editor then leaves the config field alone).
        /// </summary>
        public string? convexUrl;
    }

    public sealed class NeoComposeUnityExportCursor
    {
        public double createdAt;
        public List<string> transactionIds = new();
        public string versionsStamp = "";
    }

    public sealed class NeoComposeUnityExportHeadDescriptor
    {
        public string recordKind = "";
        public string recordId = "";
        public string? snapshotId;
        public string? contentHash;
        public bool deleted;
    }

    public sealed class NeoComposeUnityExportCachedSnapshot
    {
        public string id = "";
        public string recordKind = "";
        public string recordId = "";
        public string contentHash = "";
        public JToken data = JValue.CreateNull();
    }

    public sealed class NeoComposeUnityExportSyncState
    {
        public int schemaVersion = 1;
        public NeoComposeUnityExportCursor cursor = new();
        public List<NeoComposeUnityExportHeadDescriptor> heads = new();
        public List<NeoComposeUnityExportCachedSnapshot> snapshots = new();
    }

    public sealed class NeoComposeUnityExportDeltaManifestResponse
    {
        public string mode = "incremental";
        public bool fullResync;
        public bool codegenAffected;
        public bool runtimeContractAffected;
        public NeoComposeUnityExportCursor? cursor;
        public List<NeoComposeUnityExportHeadDescriptor> records = new();
    }

    public sealed class NeoComposeUnityExportSnapshotResponse
    {
        public List<NeoComposeUnityExportCachedSnapshot> snapshots = new();
    }

    /// <summary>
    /// Per-project runtime OAuth config carried in the export bundle so the editor
    /// can pre-fill <see cref="NeoCompose.Runtime.NeoComposeConfig"/>.
    /// Introduction-gated server-side: <see cref="configuredForVersion"/> is false
    /// (and <see cref="runtimeOAuthClientId"/> null) for versions predating the
    /// client's introduction, disabled clients, and projects with no runtime client.
    /// </summary>
    public sealed class NeoComposeUnityRuntimeOAuthConfig
    {
        public bool configuredForVersion;
        public string? runtimeOAuthClientId;
        public string[] scopes = System.Array.Empty<string>();
    }

    public sealed class NeoComposeUnityLocalizationFile
    {
        public string locale = "";
        public string fileName = "";
        public string content = "";
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
