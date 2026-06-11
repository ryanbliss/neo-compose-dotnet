// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Linq;
using UnityEngine;

namespace NeoCompose.Runtime
{
    public sealed class NeoComposeConfig : ScriptableObject
    {
        public string apiBaseUrl = NeoComposeDefaults.ApiBaseUrl;
        public string projectId = "";
        public string projectName = "";
        public string targetReleaseChannelId = "";
        public string versionId = "";
        public string generatedTypesDirectory = NeoComposeDefaults.GeneratedTypesDirectory;
        public string projectJsonDirectory = NeoComposeDefaults.ProjectJsonDirectory;
        public string localizationResourcesDirectory = NeoComposeDefaults.LocalizationResourcesDirectory;
        public string localizationStreamingAssetsDirectory = NeoComposeDefaults.LocalizationStreamingAssetsDirectory;
        public bool useStreamingAssetsForNonMainLocales;
        public bool preloadSystemLocale = true;
        public string localeOverride = "";
        public string spriteDirectory = NeoComposeDefaults.SpriteDirectory;
        public string audioClipDirectory = NeoComposeDefaults.AudioClipDirectory;
        public string namespaceForGeneratedTypes = NeoComposeDefaults.NamespaceForGeneratedTypes;
        public bool singleton = NeoComposeDefaults.Singleton;

        /// <summary>
        /// Per-project runtime OAuth client id used by the runtime device flow to
        /// authenticate a player for cloud save sync.
        /// </summary>
        /// <remarks>
        /// <b>Synced.</b> Overwritten on each project synchronization from the web
        /// portal's introduction-gated export bundle, so a developer rarely edits it
        /// by hand. Feeds <see cref="NeoAuthenticationOptions.clientId"/>. Empty when
        /// the selected version predates the project's runtime-OAuth introduction or
        /// no runtime OAuth client is enabled.
        /// </remarks>
        public string runtimeOAuthClientId = "";

        /// <summary>
        /// Per-project runtime OAuth scopes the device flow requests (e.g.
        /// <c>project:&lt;id&gt;:save:read</c>).
        /// </summary>
        /// <remarks>
        /// <b>Synced</b> alongside <see cref="runtimeOAuthClientId"/>. Feeds
        /// <see cref="NeoAuthenticationOptions.scopes"/> (space-delimited).
        /// </remarks>
        public string[] runtimeOAuthScopes = Array.Empty<string>();

        /// <summary>
        /// Convex deployment URL (e.g. <c>https://&lt;deployment&gt;.convex.cloud</c>)
        /// the optional realtime sync plugin connects to.
        /// </summary>
        /// <remarks>
        /// Hand-edited for now; a later phase syncs it from the export bundle the
        /// same way <see cref="runtimeOAuthClientId"/> is (see
        /// <c>specs/convex-realtime-sync.md</c>). Empty means realtime sync stays
        /// off regardless of plugin registration.
        /// </remarks>
        public string convexUrl = "";

        /// <summary>
        /// Master switch for cloud save sync. <b>Developer-owned, not synced.</b>
        /// </summary>
        /// <remarks>
        /// Sync may seed it <c>true</c> the first time a runtime OAuth client becomes
        /// available, but it is never force-overwritten thereafter, so a developer can
        /// flip it off (or on) per build. When <c>false</c> the runtime never touches
        /// the network for saves — fully local-only — even if every other field is
        /// populated. When <c>true</c> and the credential the configured scopes
        /// require is missing, the runtime logs a debug warning and falls back to
        /// local-only saves rather than throwing.
        /// </remarks>
        public bool enableOAuthCloudSync;

        /// <summary>
        /// True once a developer has hand-edited <see cref="runtimeOAuthClientId"/> or
        /// <see cref="runtimeOAuthScopes"/> in the editor. While set, project
        /// synchronization leaves those fields alone instead of overwriting them from
        /// the export bundle, so a manual override sticks. Cleared by "Reset override".
        /// </summary>
        public bool runtimeOAuthOverridden;

        public bool HasProject => !string.IsNullOrWhiteSpace(projectId);

        /// <summary>
        /// True when a runtime OAuth client id is present — i.e. the credential the
        /// save scopes require for cloud sync is configured.
        /// </summary>
        public bool HasRuntimeOAuthClient => !string.IsNullOrWhiteSpace(runtimeOAuthClientId);

        /// <summary>
        /// True when cloud save sync is requested and the credential it needs (the
        /// runtime OAuth client id) is present, so the runtime can wire it without
        /// falling back to local-only.
        /// </summary>
        public bool IsCloudSaveSyncConfigured => enableOAuthCloudSync && HasRuntimeOAuthClient;

        /// <summary>
        /// Builds <see cref="NeoAuthenticationOptions"/> from the synced runtime OAuth
        /// config, or returns false when a required field is missing (no project id,
        /// no client id, or no scopes) so the caller can fall back to local-only.
        /// </summary>
        public bool TryBuildAuthenticationOptions(out NeoAuthenticationOptions? options)
        {
            options = null;
            if (string.IsNullOrWhiteSpace(projectId)) return false;
            if (!HasRuntimeOAuthClient) return false;

            var scopes = string.Join(
                " ",
                runtimeOAuthScopes
                    .Where(scope => !string.IsNullOrWhiteSpace(scope))
                    .Select(scope => scope.Trim()));
            if (string.IsNullOrWhiteSpace(scopes)) return false;

            options = new NeoAuthenticationOptions(
                apiBaseUrl,
                projectId,
                runtimeOAuthClientId,
                scopes);
            return true;
        }

        /// <summary>
        /// Surfaces a pre-ship configuration gap for cloud save sync: returns a
        /// warning message (and true) when <see cref="enableOAuthCloudSync"/> is on
        /// but the credential a configured scope requires is missing. Returns false
        /// (no warning) when sync is off or fully configured.
        /// </summary>
        /// <remarks>
        /// Save scopes (<c>save:*</c>) are authorized by the runtime OAuth client;
        /// runtime-data / secure-channel scopes (<c>runtime:*</c>) are authorized by
        /// the runtime API key (now stored in the gitignored
        /// <see cref="NeoComposeRuntimeSecret"/>, so it is passed in via
        /// <paramref name="runtimeApiKey"/> rather than read off the committed
        /// config). The runtime degrades to local-only at runtime, but the editor
        /// surfaces this warning so the gap is caught before shipping. Pure and
        /// engine-free so it can be unit tested.
        /// </remarks>
        public bool TryGetCloudSaveSyncWarning(string? runtimeApiKey, out string? warning)
        {
            warning = null;
            if (!enableOAuthCloudSync) return false;

            bool requiresOAuthClient = false;
            bool requiresApiKey = false;
            foreach (var scope in runtimeOAuthScopes)
            {
                if (string.IsNullOrWhiteSpace(scope)) continue;
                if (scope.Contains(":save:")) requiresOAuthClient = true;
                if (scope.Contains(":runtime:")) requiresApiKey = true;
            }

            // No scopes synced yet but sync is requested: the save credential is the
            // baseline expectation, so treat a missing client id as the gap.
            if (!requiresOAuthClient && !requiresApiKey) requiresOAuthClient = true;

            if (requiresOAuthClient && !HasRuntimeOAuthClient)
            {
                warning =
                    "Cloud save sync is enabled but no runtime OAuth client id is configured. " +
                    "Enable runtime OAuth for the project and synchronize, or disable cloud save sync.";
                return true;
            }
            if (requiresApiKey && string.IsNullOrWhiteSpace(runtimeApiKey))
            {
                warning =
                    "Cloud save sync requests a runtime-data scope but no runtime API key is " +
                    "configured. Set the project runtime API key, or remove the runtime scope.";
                return true;
            }

            return false;
        }

        public static NeoComposeConfig? LoadDefault()
        {
            return Resources.Load<NeoComposeConfig>(NeoComposeDefaults.ConfigResourcePath);
        }

        public NeoLocalizationOptions ToLocalizationOptions()
        {
            return new NeoLocalizationOptions
            {
                localeOverride = string.IsNullOrWhiteSpace(localeOverride) ? null : localeOverride.Trim(),
                preloadSystemLocale = preloadSystemLocale,
                useStreamingAssetsForNonMainLocales = useStreamingAssetsForNonMainLocales,
                streamingAssetsRelativePath = LocalizationStreamingAssetsRelativePath(),
            };
        }

        public void SelectProject(
            string id,
            string name,
            string? namespaceForGeneratedTypes = null,
            bool singleton = NeoComposeDefaults.Singleton)
        {
            if (projectId != id)
            {
                targetReleaseChannelId = "";
                versionId = "";
            }

            projectId = id;
            projectName = name;
            this.namespaceForGeneratedTypes = string.IsNullOrWhiteSpace(namespaceForGeneratedTypes)
                ? NeoComposeDefaults.NamespaceForGeneratedTypes
                : namespaceForGeneratedTypes;
            this.singleton = singleton;
        }

        public void ClearProject()
        {
            projectId = "";
            projectName = "";
            targetReleaseChannelId = "";
            versionId = "";
        }

        private string LocalizationStreamingAssetsRelativePath()
        {
            const string prefix = "Assets/StreamingAssets/";
            var normalized = localizationStreamingAssetsDirectory.Replace('\\', '/').Trim('/');
            return normalized.StartsWith(prefix, System.StringComparison.Ordinal)
                ? normalized.Substring(prefix.Length)
                : NeoComposeDefaults.LocalizationStreamingAssetsRelativePath;
        }
    }
}
