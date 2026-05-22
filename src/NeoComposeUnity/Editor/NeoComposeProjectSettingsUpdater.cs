// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Threading.Tasks;
using NeoCompose.Runtime;

namespace NeoCompose.Unity.Editor
{
    public sealed class NeoComposeProjectSettingsUpdateResult
    {
        public bool success;
        public string message = "";

        public static NeoComposeProjectSettingsUpdateResult Success(string message)
        {
            return new NeoComposeProjectSettingsUpdateResult
            {
                success = true,
                message = message,
            };
        }

        public static NeoComposeProjectSettingsUpdateResult Failure(string message)
        {
            return new NeoComposeProjectSettingsUpdateResult
            {
                success = false,
                message = message,
            };
        }
    }

    public sealed class NeoComposeProjectSettingsUpdater
    {
        private readonly INeoComposeEditorApiClient apiClient;
        private readonly INeoComposeEditorAssetService assets;

        public NeoComposeProjectSettingsUpdater(
            INeoComposeEditorApiClient apiClient,
            INeoComposeEditorAssetService assets)
        {
            this.apiClient = apiClient;
            this.assets = assets;
        }

        public async Task<NeoComposeProjectSettingsUpdateResult> UpdateUnityExportSettingsAsync(NeoComposeConfig config)
        {
            if (string.IsNullOrWhiteSpace(config.apiBaseUrl))
            {
                return NeoComposeProjectSettingsUpdateResult.Failure("API base URL cannot be empty.");
            }

            if (!config.HasProject)
            {
                return NeoComposeProjectSettingsUpdateResult.Failure("No Neo Compose project is selected.");
            }

            if (string.IsNullOrWhiteSpace(config.versionId))
            {
                return NeoComposeProjectSettingsUpdateResult.Failure("No Neo Compose project version is selected.");
            }

            try
            {
                var response = await apiClient.UpdateProjectExportSettingsAsync(
                    config.apiBaseUrl,
                    config.projectId,
                    config.versionId,
                    config.namespaceForGeneratedTypes,
                    config.singleton);

                config.SelectProject(
                    response.project.id,
                    response.project.name,
                    response.project.UnityNamespaceOrDefault(),
                    response.project.UnitySingletonOrDefault());
                assets.SaveConfig(config);
                return NeoComposeProjectSettingsUpdateResult.Success("Unity export settings saved.");
            }
            catch (Exception exception)
            {
                return NeoComposeProjectSettingsUpdateResult.Failure(exception.Message);
            }
        }
    }
}
