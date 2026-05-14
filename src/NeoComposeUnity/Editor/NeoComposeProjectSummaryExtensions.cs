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
            var namespaceForGeneratedTypes = project.exportSettings?.unity?.namespaceForGeneratedTypes;
            if (string.IsNullOrWhiteSpace(namespaceForGeneratedTypes))
            {
                return NeoComposeDefaults.NamespaceForGeneratedTypes;
            }

            return namespaceForGeneratedTypes!;
        }

        public static bool UnitySingletonOrDefault(this NeoComposeProjectSummary project)
        {
            return project.exportSettings?.unity?.singleton ?? NeoComposeDefaults.Singleton;
        }
    }
}
