// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using NeoCompose.Runtime;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Headless Neo Compose sign-in for agent rigs and CI.
    /// </summary>
    /// <remarks>
    /// <para>Invoke as:</para>
    /// <code>
    /// Unity -batchmode -nographics -projectPath &lt;project&gt; \
    ///   -executeMethod NeoCompose.Unity.Editor.NeoComposeBatchLogin.Run
    /// </code>
    /// <para>
    /// Do <b>not</b> pass <c>-quit</c>, for the same reason
    /// <see cref="NeoComposeBatchSync"/> does not: the device flow is
    /// asynchronous and the editor's update loop is the pump its web requests
    /// need, so <see cref="Run"/> returns immediately and exits the editor
    /// itself — <c>0</c> once a credential is stored, <c>1</c> otherwise.
    /// </para>
    /// <para>
    /// Sign-in always targets the <em>rig</em> origin: this entry point resolves
    /// the active rig manifest and refuses to run without one, so a headless run
    /// can never authorize against personal dev or production by inheriting a
    /// committed <c>apiBaseUrl</c>. Approval is external — batchmode has no
    /// browser, so the verification URL and user code are logged under
    /// <see cref="LogPrefix"/> for the caller to approve, and the credential
    /// lands in the same per-origin <see cref="NeoComposeTokenStore"/> the editor
    /// window and <see cref="NeoComposeBatchSync"/> read.
    /// </para>
    /// </remarks>
    public static class NeoComposeBatchLogin
    {
        /// <summary>Console marker external tooling greps for.</summary>
        public const string LogPrefix = "[NeoComposeBatchLogin]";

        /// <summary>
        /// Set to <c>1</c> to re-authorize even when a usable credential is
        /// already stored for the rig origin.
        /// </summary>
        public const string ForceEnvironmentVariable = "NEO_COMPOSE_BATCH_LOGIN_FORCE";

        /// <summary>
        /// Ceiling on one headless attempt. The server's own <c>expires_in</c>
        /// governs the flow; this is the outer tripwire that keeps an
        /// unapproved run from occupying a batchmode editor indefinitely.
        /// </summary>
        internal static readonly TimeSpan ApprovalDeadline = TimeSpan.FromMinutes(10);

        /// <summary>
        /// A stored credential is only reused when it still has this much life
        /// left — a token that expires mid-synchronize is no better than none.
        /// </summary>
        internal static readonly TimeSpan ReuseMargin = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Batchmode entry point. Signs in against the rig origin, then exits the
        /// editor with <c>0</c> or <c>1</c> when it settles.
        /// </summary>
        public static void Run()
        {
            Start(exitOnCompletion: true);
        }

        /// <summary>
        /// Same pipeline as <see cref="Run"/> but leaves the editor running, for
        /// an in-editor affordance.
        /// </summary>
        public static void RunWithoutExit()
        {
            Start(exitOnCompletion: false);
        }

        /// <summary>
        /// True when <paramref name="stored"/> can stand in for a fresh
        /// authorization.
        /// </summary>
        internal static bool CanReuseStoredSignIn(
            NeoComposeStoredToken? stored,
            DateTimeOffset now,
            bool force)
        {
            if (force) return false;
            if (stored == null) return false;
            if (!stored.HasAccessToken) return false;
            return !stored.IsExpired(now + ReuseMargin);
        }

        internal static bool ForceRequested()
        {
            var value = Environment.GetEnvironmentVariable(ForceEnvironmentVariable);
            return !string.IsNullOrWhiteSpace(value) &&
                !string.Equals(value, "0", StringComparison.Ordinal);
        }

        /// <summary>
        /// The rig origin this entry point signs in against. Throws when no rig
        /// is bound, rather than falling back to the committed configuration.
        /// </summary>
        internal static string ResolveRigOrigin()
        {
            var rig = NeoComposeEffectiveConfig.ResolveActiveRig();
            if (rig == null)
            {
                throw new InvalidOperationException(
                    "No rig manifest is bound to this Unity project, so there is no rig origin to sign in " +
                    "against. Headless sign-in is a rig affordance: run \"npm run agent:setup -- --source " +
                    "neo-compose-dotnet\" from the Neo workspace, or set " +
                    NeoComposeRigManifestResolver.ManifestEnvironmentVariable + " to the manifest path.");
            }

            Debug.Log($"{LogPrefix} {rig.Describe()}");
            return rig.Manifest.web.origin;
        }

        private static void Start(bool exitOnCompletion)
        {
            Debug.Log($"{LogPrefix} start");

            string origin;
            INeoComposeTokenStore store;
            try
            {
                origin = ResolveRigOrigin();
                store = NeoComposeTokenStore.Create(origin);
                if (CanReuseStoredSignIn(store.Load(), DateTimeOffset.UtcNow, ForceRequested()))
                {
                    Debug.Log(
                        $"{LogPrefix} end: success — already signed in for {origin}; set " +
                        $"{ForceEnvironmentVariable}=1 to re-authorize.");
                    if (exitOnCompletion) EditorApplication.Exit(0);
                    return;
                }
            }
            catch (Exception exception)
            {
                Finish(exception, exitOnCompletion);
                return;
            }

            Task<NeoComposeDeviceAuthResult> authorize;
            var cancellation = new CancellationTokenSource(ApprovalDeadline);
            try
            {
                var flow = NeoComposeEditorAuthController.CreateFlow(
                    origin,
                    store,
                    // Batchmode has no browser: the caller approves this URL.
                    url => Debug.Log($"{LogPrefix} verification url: {url}"));
                Debug.Log(
                    $"{LogPrefix} awaiting approval at {origin} " +
                    $"(deadline {ApprovalDeadline.TotalMinutes:0} minutes)");
                authorize = flow.AuthorizeAsync(
                    origin,
                    code => Debug.Log($"{LogPrefix} user code: {code.userCode}"),
                    cancellation.Token);
            }
            catch (Exception exception)
            {
                cancellation.Dispose();
                Finish(exception, exitOnCompletion);
                return;
            }

            PumpUntilComplete(authorize, cancellation, exitOnCompletion);
        }

        /// <summary>
        /// Drives the authorization task from the editor update loop — the loop
        /// is the pump the web requests need — and reports once it settles.
        /// </summary>
        private static void PumpUntilComplete(
            Task<NeoComposeDeviceAuthResult> authorize,
            CancellationTokenSource cancellation,
            bool exitOnCompletion)
        {
            void Poll()
            {
                if (!authorize.IsCompleted) return;
                EditorApplication.update -= Poll;
                Finish(authorize, cancellation, exitOnCompletion);
            }

            if (authorize.IsCompleted)
            {
                Finish(authorize, cancellation, exitOnCompletion);
                return;
            }

            EditorApplication.update += Poll;
        }

        private static void Finish(
            Task<NeoComposeDeviceAuthResult> authorize,
            CancellationTokenSource cancellation,
            bool exitOnCompletion)
        {
            cancellation.Dispose();

            if (authorize.IsFaulted)
            {
                Finish(
                    authorize.Exception?.GetBaseException()
                        ?? new InvalidOperationException("Sign-in faulted without an exception."),
                    exitOnCompletion);
                return;
            }

            if (authorize.IsCanceled)
            {
                Finish(new OperationCanceledException("Sign-in was cancelled."), exitOnCompletion);
                return;
            }

            var result = authorize.Result;
            if (!result.IsSuccess)
            {
                // Only the outcome and the flow's own message are logged; the
                // device code and token never are.
                Debug.LogError($"{LogPrefix} end: failed — {result.outcome}: {result.message}");
                if (exitOnCompletion) EditorApplication.Exit(1);
                return;
            }

            var identity = result.token?.displayEmail ?? "";
            Debug.Log(
                $"{LogPrefix} end: success — signed in" +
                (identity.Length > 0 ? $" as {identity}" : "") + ".");
            if (exitOnCompletion) EditorApplication.Exit(0);
        }

        private static void Finish(Exception exception, bool exitOnCompletion)
        {
            Debug.LogError($"{LogPrefix} end: failed — {exception}");
            if (exitOnCompletion) EditorApplication.Exit(1);
        }
    }
}
