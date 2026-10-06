// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System.IO;
using NeoCompose.Runtime;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Fails a player build of a local export (P101 §4): it can hold records
    /// the server doesn't have, so players' saves couldn't sync.
    /// </summary>
    public sealed class NeoComposeLocalExportBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            NeoComposeConfig? config = NeoComposeConfig.LoadDefault();
            if (config == null)
                return;
            RefuseLocalExport(Directory.GetCurrentDirectory(), config);
        }

        internal static void RefuseLocalExport(string projectRoot, NeoComposeConfig config)
        {
            if (!NeoComposeLocalExport.IsLocal(projectRoot, config))
                return;
            throw new BuildFailedException(
                "Neo Compose: project.json is a local export with edits that aren't pushed. " +
                "Run `neo push`, or revert the edits and run `neo export`, then build again.");
        }
    }
}
