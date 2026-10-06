// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Headless ingest of a <c>neo export</c>, for agents and CI.
    /// </summary>
    /// <remarks>
    /// <para>Run <c>neo export</c> first, then invoke:</para>
    /// <code>
    /// Unity -batchmode -nographics -projectPath &lt;project&gt; \
    ///   -executeMethod NeoCompose.Unity.Editor.NeoComposeBatchSync.Run
    /// </code>
    /// <para>
    /// Do <b>not</b> pass <c>-quit</c>: <see cref="Run"/> exits the editor
    /// itself — <c>0</c> on success, <c>1</c> on failure, including when there
    /// is no export to ingest.
    /// </para>
    /// <para>
    /// The configuration comes from the committed asset with the active rig
    /// manifest overlaid (see <see cref="NeoComposeResolvedConfig"/>), and every
    /// prompt is answered by
    /// <see cref="NeoComposeNonInteractiveConfirmationService"/>, so no dialog can
    /// open. Deferred post-synchronize work (tile/rule-tile generation, which
    /// needs a domain reload) is not awaited here.
    /// </para>
    /// </remarks>
    public static class NeoComposeBatchSync
    {
        /// <summary>Console marker external tooling greps for.</summary>
        public const string LogPrefix = "[NeoComposeBatchSync]";

        /// <summary>
        /// Batchmode entry point. Ingests the sidecar <c>neo export</c> wrote,
        /// then exits the editor with <c>0</c> or <c>1</c>.
        /// </summary>
        public static void Run()
        {
            Debug.Log($"{LogPrefix} start");
            try
            {
                var rigStatus = NeoComposeResolvedConfig.DescribeActiveRig();
                Debug.Log($"{LogPrefix} {rigStatus ?? "No rig manifest is bound; using the committed configuration."}");
                string projectRoot = Directory.GetCurrentDirectory();
                NeoComposeExportSidecar sidecar = NeoComposeExportIngest.ReadSidecar(projectRoot)
                    ?? throw new InvalidOperationException(
                        $"No `neo export` to ingest: {NeoComposeExportIngest.SidecarPath} does not exist. Run `neo pull`, then `neo export --unity-project {projectRoot}`.");
                string[] fileErrors = NeoComposeExportIngest.Ingest(
                    projectRoot,
                    sidecar,
                    NeoComposeResolvedConfig.Resolve(NeoComposeConfigProvider.LoadOrCreate()),
                    new NeoComposeNonInteractiveConfirmationService(),
                    new NeoComposeEditorAssetService());
                if (fileErrors.Length > 0)
                {
                    Debug.LogError(
                        $"{LogPrefix} end: failed — export {sidecar.exportId} ingested, but some project files failed:\n" +
                        string.Join("\n", fileErrors));
                    EditorApplication.Exit(1);
                    return;
                }

                Debug.Log($"{LogPrefix} end: success — ingested export {sidecar.exportId}.");
                EditorApplication.Exit(0);
            }
            catch (Exception exception)
            {
                Debug.LogError($"{LogPrefix} end: failed — {exception}");
                EditorApplication.Exit(1);
            }
        }
    }
}
