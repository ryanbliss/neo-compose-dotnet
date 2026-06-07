// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace NeoCompose.Unity.Editor
{
    /// <summary>
    /// Bakes the runtime API key from the <c>NEO_COMPOSE_RUNTIME_API_KEY</c>
    /// environment variable into the gitignored secret asset just for the build, then
    /// restores the prior value afterward. This is the CI path: a fresh checkout has
    /// no committed key, so the env var supplies it at build time. Local builds (no
    /// env var) ship whatever the developer pasted into the asset, untouched.
    /// </summary>
    public sealed class NeoComposeRuntimeSecretBuildProcessor
        : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        private static bool stashed;
        private static string stashedKey = "";

        public void OnPreprocessBuild(BuildReport report)
        {
            var envKey = Environment.GetEnvironmentVariable(
                NeoComposeEditorDefaults.RuntimeApiKeyEnvVar);
            if (string.IsNullOrWhiteSpace(envKey)) return;

            var secret = NeoComposeRuntimeSecretProvider.EnsureAssetAndGitignore();
            stashedKey = secret.RuntimeApiKey;
            stashed = true;
            secret.RuntimeApiKey = envKey.Trim();
            NeoComposeRuntimeSecretProvider.Save(secret);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (!stashed) return;
            stashed = false;

            var secret = NeoComposeRuntimeSecretProvider.Find();
            if (secret == null) return;
            secret.RuntimeApiKey = stashedKey;
            NeoComposeRuntimeSecretProvider.Save(secret);
            stashedKey = "";
        }
    }
}
