// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using NeoCompose.Unity.Editor;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEditor.Build;

namespace NeoCompose.Tests
{
    /// <summary>P101 §4: the <c>localExport</c> stamp in the editor.</summary>
    public partial class NeoComposeEditorTests
    {
        [Test]
        public void LocalExport_FailsTheBuildAndNamesTheWorkspaceUntilACleanExport()
        {
            using var fixture = new IngestFixture("");
            string projectJsonPath = Path.Combine(
                fixture.projectRoot,
                fixture.config.projectJsonDirectory,
                "project.json");
            string clean = File.ReadAllText(projectJsonPath);
            File.WriteAllText(
                Path.Combine(fixture.projectRoot, NeoComposeExportIngest.SidecarPath),
                JsonConvert.SerializeObject(fixture.sidecar));

            WriteProjectJson(projectJsonPath, clean.Replace(@"""schemaVersion""", @"""localExport"":true,""schemaVersion"""), 1);

            Assert.IsTrue(NeoComposeLocalExport.TryGetLocalWorkspace(
                fixture.projectRoot,
                fixture.config,
                out string workspaceRoot));
            Assert.AreEqual(fixture.workspaceRoot, workspaceRoot);
            var error = Assert.Throws<BuildFailedException>(() =>
                NeoComposeLocalExportBuildProcessor.RefuseLocalExport(fixture.projectRoot, fixture.config));
            StringAssert.Contains("Run `neo push`, or revert the edits and run `neo export`", error!.Message);

            WriteProjectJson(projectJsonPath, clean, 2);

            Assert.IsFalse(NeoComposeLocalExport.IsLocal(fixture.projectRoot, fixture.config));
            Assert.DoesNotThrow(() =>
                NeoComposeLocalExportBuildProcessor.RefuseLocalExport(fixture.projectRoot, fixture.config));
        }

        /// <summary>A distinct write time per export, as two real exports have.</summary>
        private static void WriteProjectJson(string path, string json, int export)
        {
            File.WriteAllText(path, json);
            File.SetLastWriteTimeUtc(path, new DateTime(2026, 10, 6, 0, 0, export, DateTimeKind.Utc));
        }
    }
}
