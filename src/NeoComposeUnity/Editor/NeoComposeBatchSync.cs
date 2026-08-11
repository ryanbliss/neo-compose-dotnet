// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Headless project synchronization, for agents and CI.
    /// </summary>
    /// <remarks>
    /// <para>Invoke as:</para>
    /// <code>
    /// Unity -batchmode -nographics -projectPath &lt;project&gt; \
    ///   -executeMethod NeoCompose.Unity.Editor.NeoComposeBatchSync.Run
    /// </code>
    /// <para>
    /// Do <b>not</b> pass <c>-quit</c>: synchronization is asynchronous and the
    /// editor's update loop is what drives the in-flight web requests to
    /// completion, so <see cref="Run"/> returns immediately and exits the editor
    /// itself — <c>0</c> on success, <c>1</c> on failure. Blocking the main
    /// thread instead would deadlock, because the request completion callbacks
    /// need the very loop the block is holding.
    /// </para>
    /// <para>
    /// The configuration comes from the committed asset with the active rig
    /// manifest overlaid (see <see cref="NeoComposeEffectiveConfig"/>), and every
    /// prompt is answered by
    /// <see cref="NeoComposeNonInteractiveConfirmationService"/>, so no dialog can
    /// open. Deferred post-synchronize work (tile/rule-tile generation, which
    /// needs a domain reload) is not awaited here; the generated C#,
    /// <c>project.json</c>, localization, and file assets are.
    /// </para>
    /// </remarks>
    public static class NeoComposeBatchSync
    {
        /// <summary>Console marker external tooling greps for.</summary>
        public const string LogPrefix = "[NeoComposeBatchSync]";

        /// <summary>
        /// Batchmode entry point. Starts synchronization, then exits the editor
        /// with <c>0</c> or <c>1</c> when it settles.
        /// </summary>
        public static void Run()
        {
            Start(exitOnCompletion: true);
        }

        /// <summary>
        /// Same pipeline as <see cref="Run"/> but leaves the editor running, for
        /// the in-editor menu affordance.
        /// </summary>
        public static void RunWithoutExit()
        {
            Start(exitOnCompletion: false);
        }

        /// <summary>
        /// Runs one synchronize pass against the effective configuration using the
        /// non-interactive confirmation service. Exposed for callers that want to
        /// await the result rather than have the editor exit.
        /// </summary>
        public static Task<NeoComposeSyncResult> SynchronizeAsync(Action<string>? onProgress = null)
        {
            var config = ResolveEffectiveConfig();
            var synchronizer = new NeoComposeSynchronizer(
                new NeoComposeEditorApiClient(),
                new NeoComposeNonInteractiveConfirmationService(),
                new NeoComposeEditorAssetService());
            return synchronizer.SynchronizeAsync(config, onProgress);
        }

        /// <summary>
        /// The committed config with the active rig manifest overlaid. Throws when
        /// a rig is bound but its wiring or manifest is inconsistent, so a headless
        /// run fails loudly instead of synchronizing against the wrong deployment.
        /// </summary>
        public static NeoComposeConfig ResolveEffectiveConfig()
        {
            return NeoComposeEffectiveConfig.Resolve(NeoComposeConfigProvider.LoadOrCreate());
        }

        private static void Start(bool exitOnCompletion)
        {
            Debug.Log($"{LogPrefix} start");

            Task<NeoComposeSyncResult> synchronize;
            try
            {
                var rigStatus = NeoComposeEffectiveConfig.DescribeActiveRig();
                Debug.Log($"{LogPrefix} {rigStatus ?? "No rig manifest is bound; using the committed configuration."}");
                synchronize = SynchronizeAsync(message => Debug.Log($"{LogPrefix} {message}"));
            }
            catch (Exception exception)
            {
                Finish(exception, exitOnCompletion);
                return;
            }

            PumpUntilComplete(synchronize, exitOnCompletion);
        }

        /// <summary>
        /// Drives the synchronize task from the editor update loop — the loop is
        /// the pump the web requests need — and reports once it settles.
        /// </summary>
        private static void PumpUntilComplete(Task<NeoComposeSyncResult> synchronize, bool exitOnCompletion)
        {
            void Poll()
            {
                if (!synchronize.IsCompleted) return;
                EditorApplication.update -= Poll;
                Finish(synchronize, exitOnCompletion);
            }

            if (synchronize.IsCompleted)
            {
                Finish(synchronize, exitOnCompletion);
                return;
            }

            EditorApplication.update += Poll;
        }

        private static void Finish(Task<NeoComposeSyncResult> synchronize, bool exitOnCompletion)
        {
            if (synchronize.IsFaulted)
            {
                Finish(
                    synchronize.Exception?.GetBaseException()
                        ?? new InvalidOperationException("Synchronization faulted without an exception."),
                    exitOnCompletion);
                return;
            }

            if (synchronize.IsCanceled)
            {
                Finish(new OperationCanceledException("Synchronization was cancelled."), exitOnCompletion);
                return;
            }

            AssetDatabase.Refresh();

            var result = synchronize.Result;
            if (!result.success)
            {
                Debug.LogError($"{LogPrefix} end: failed — {result.message}");
                if (exitOnCompletion) EditorApplication.Exit(1);
                return;
            }

            Debug.Log($"{LogPrefix} end: success — {result.message}");
            if (exitOnCompletion) EditorApplication.Exit(0);
        }

        private static void Finish(Exception exception, bool exitOnCompletion)
        {
            Debug.LogError($"{LogPrefix} end: failed — {exception}");
            if (exitOnCompletion) EditorApplication.Exit(1);
        }
    }
}
