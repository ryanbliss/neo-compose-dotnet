// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using NeoCompose.Runtime;

namespace NeoCompose.Unity.Editor
{
    public static class NeoComposeProjectSummaryExtensions
    {
        public static string UnityNamespaceOrDefault(this NeoComposeProjectSummary project)
        {
            return string.IsNullOrWhiteSpace(project.exportSettings?.unity?.namespaceForGeneratedTypes)
                ? NeoComposeDefaults.NamespaceForGeneratedTypes
                : project.exportSettings.unity.namespaceForGeneratedTypes!;
        }
    }
}
