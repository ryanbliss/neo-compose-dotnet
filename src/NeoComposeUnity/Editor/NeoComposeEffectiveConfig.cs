// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using NeoCompose.Runtime;
using UnityEditor;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Editor/development-only effective configuration overlay (P53 §6).
    /// </summary>
    /// <remarks>
    /// The committed <see cref="NeoComposeConfig"/> asset stays the checked-in
    /// sample/release artifact: an agent rig must never patch-and-restore it just
    /// to point Unity at a disposable deployment. Instead the editor loads the
    /// committed asset, clones it with <see cref="HideFlags.DontSave"/>, applies
    /// the active rig manifest to the clone, and hands the clone to editor sync
    /// and play-mode clients. Save callbacks recognize the clone and refuse to
    /// serialize it. Player builds never see any of this — the resolver is only
    /// installed under <c>UNITY_EDITOR</c>.
    /// </remarks>
    public static class NeoComposeEffectiveConfig
    {
        /// <summary>
        /// Appended to the clone's object name. Doubles as the marker that
        /// survives a stale entity-id registry, so a clone is never mistaken
        /// for the committed asset.
        /// </summary>
        internal const string RigOverlayNameSuffix = " (Rig Overlay)";

        private static readonly HashSet<EntityId> RigOverlayEntityIds = new();

        /// <summary>Entity ids already told that saving is refused, so a GUI
        /// edit burst logs the explanation once instead of once per field.</summary>
        private static readonly HashSet<EntityId> ReportedSaveRefusals = new();

        private static NeoComposeRigResolution? cachedRig;
        private static bool rigResolved;
        private static NeoComposeConfig? cachedCommitted;
        private static NeoComposeConfig? cachedOverlay;

        /// <summary>
        /// The rig governing this Unity project, or null when none is bound.
        /// Resolved once per domain reload; throws when a rig is bound but its
        /// pointer, environment override, or manifest is inconsistent.
        /// </summary>
        public static NeoComposeRigResolution? ResolveActiveRig()
        {
            if (rigResolved) return cachedRig;

            cachedRig = NeoComposeRigManifestResolver.Resolve();
            rigResolved = true;
            return cachedRig;
        }

        /// <summary>
        /// Returns the configuration the editor should actually use: the
        /// committed asset when no rig is bound, otherwise a cached
        /// <see cref="HideFlags.DontSave"/> clone with the rig manifest applied.
        /// </summary>
        public static NeoComposeConfig Resolve(NeoComposeConfig committed)
        {
            if (committed == null)
            {
                throw new ArgumentNullException(nameof(committed));
            }

            var rig = ResolveActiveRig();
            if (rig == null) return committed;

            if (cachedOverlay != null && cachedCommitted == committed) return cachedOverlay;

            cachedCommitted = committed;
            cachedOverlay = Apply(committed, rig.Manifest);
            return cachedOverlay;
        }

        /// <summary>
        /// Clones <paramref name="committed"/> and applies <paramref name="manifest"/>
        /// to the clone.
        /// </summary>
        /// <remarks>
        /// Endpoints always come from the rig. Project identity comes from the rig
        /// only once the rig is seeded (<c>sample</c> non-null); an unseeded rig
        /// leaves the committed project/version/channel in force so the sample
        /// still points at whatever the developer selected. The runtime OAuth
        /// fields honour the committed asset's <c>runtimeOAuthOverridden</c> latch
        /// exactly like project synchronization does: a hand-edited client id and
        /// scope set is developer-owned and is never overwritten.
        /// </remarks>
        public static NeoComposeConfig Apply(NeoComposeConfig committed, NeoComposeRigManifest manifest)
        {
            var overlay = UnityEngine.Object.Instantiate(committed);
            overlay.hideFlags = HideFlags.DontSave;
            overlay.name = committed.name + RigOverlayNameSuffix;
            RigOverlayEntityIds.Add(overlay.GetEntityId());

            overlay.apiBaseUrl = manifest.web.origin;
            overlay.convexUrl = manifest.deployment.url;

            var sample = manifest.sample;
            if (sample != null)
            {
                overlay.projectId = sample.projectId;
                overlay.projectName = sample.projectName;
                overlay.versionId = sample.versionId;
                overlay.targetReleaseChannelId = sample.releaseChannelId;
                if (!committed.runtimeOAuthOverridden)
                {
                    overlay.runtimeOAuthClientId = sample.runtimeOAuthClientId;
                    overlay.runtimeOAuthScopes = (string[])sample.runtimeOAuthScopes.Clone();
                }
            }

            return overlay;
        }

        /// <summary>
        /// True when <paramref name="config"/> is an ephemeral rig overlay rather
        /// than the committed asset, so a save callback can refuse it.
        /// </summary>
        public static bool IsRigOverlay(NeoComposeConfig config)
        {
            if (config == null) return false;
            if (RigOverlayEntityIds.Contains(config.GetEntityId())) return true;
            return config.name.EndsWith(RigOverlayNameSuffix, StringComparison.Ordinal);
        }

        /// <summary>
        /// Logs, once per overlay instance, why a save was dropped. Called by
        /// <see cref="NeoComposeConfigProvider.Save"/> so every save path — editor
        /// window fields, synchronization, headless sync — reports the same reason.
        /// </summary>
        internal static void ReportSaveRefused(NeoComposeConfig overlay)
        {
            if (!ReportedSaveRefusals.Add(overlay.GetEntityId())) return;

            var rig = cachedRig;
            var rigDescription = rig == null ? "an active rig" : rig.Describe();
            Debug.Log(
                "Neo Compose is running against " + rigDescription +
                ", so configuration changes are applied to an ephemeral overlay and are not " +
                "written to the committed NeoComposeConfig asset. Edit the committed asset " +
                "outside rig mode if the change is meant to be permanent.");
        }

        /// <summary>Status line describing rig mode, or null when no rig is bound.</summary>
        public static string? DescribeActiveRig()
        {
            var rig = ResolveActiveRig();
            return rig == null
                ? null
                : $"Rig mode: {rig.Describe()}. Configuration edits are not saved to the committed asset.";
        }

        /// <summary>Drops the cached rig and overlay so a test can install its own.</summary>
        internal static void ResetForTests()
        {
            cachedRig = null;
            rigResolved = false;
            cachedCommitted = null;
            cachedOverlay = null;
            RigOverlayEntityIds.Clear();
            ReportedSaveRefusals.Clear();
        }
    }

    /// <summary>
    /// Installs the editor overlay into <see cref="NeoComposeConfig.LoadDefault"/>
    /// so play-mode clients resolving the default config in the editor get the
    /// rig overlay too. The runtime assembly holds only the hook; the policy
    /// lives here, and a player build has neither.
    /// </summary>
    [InitializeOnLoad]
    internal static class NeoComposeEffectiveConfigInstaller
    {
        static NeoComposeEffectiveConfigInstaller()
        {
            NeoComposeConfig.EditorEffectiveConfigResolver = NeoComposeEffectiveConfig.Resolve;
        }
    }
}
