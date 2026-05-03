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
        public string generatedTypesDirectory = NeoComposeDefaults.GeneratedTypesDirectory;
        public string projectJsonDirectory = NeoComposeDefaults.ProjectJsonDirectory;
        public string namespaceForGeneratedTypes = NeoComposeDefaults.NamespaceForGeneratedTypes;

        public bool HasProject => !string.IsNullOrWhiteSpace(projectId);

        public void SelectProject(string id, string name, string? namespaceForGeneratedTypes = null)
        {
            projectId = id;
            projectName = name;
            this.namespaceForGeneratedTypes = string.IsNullOrWhiteSpace(namespaceForGeneratedTypes)
                ? NeoComposeDefaults.NamespaceForGeneratedTypes
                : namespaceForGeneratedTypes;
        }

        public void ClearProject()
        {
            projectId = "";
            projectName = "";
        }
    }
}
