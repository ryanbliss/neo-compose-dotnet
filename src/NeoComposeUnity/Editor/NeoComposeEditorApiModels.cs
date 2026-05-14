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
    }

    public sealed class NeoComposeUnityExportFileDownloadRequest
    {
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
