// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NeoCompose.Runtime;
using UnityEngine;

namespace NeoCompose.Unity.Editor
{
    internal static class NeoTileGridAuthoringPreviewRefresher
    {
        internal static async Awaitable RefreshBindingsAsync(
            string projectId,
            string versionId,
            CancellationToken cancellationToken)
        {
            foreach (var binding in FindMatchingBindings(projectId, versionId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await binding.RefreshPreviewAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    string bindingValueId = string.IsNullOrWhiteSpace(binding.valueId)
                        ? "<missing>"
                        : binding.valueId;
                    throw new InvalidOperationException(
                        $"TileGrid authoring preview refresh failed for value " +
                        $"'{bindingValueId}' in project '{projectId}' version " +
                        $"'{versionId}': {exception.Message}",
                        exception);
                }
            }
        }

        internal static IReadOnlyList<NeoTileGridAuthoringBinding> FindMatchingBindings(
            string projectId,
            string versionId)
        {
            if (string.IsNullOrWhiteSpace(projectId))
            {
                throw new ArgumentException(
                    "A project id is required to refresh TileGrid authoring previews.",
                    nameof(projectId));
            }
            if (string.IsNullOrWhiteSpace(versionId))
            {
                throw new ArgumentException(
                    "A version id is required to refresh TileGrid authoring previews.",
                    nameof(versionId));
            }

            return Resources.FindObjectsOfTypeAll<NeoTileGridAuthoringBinding>()
                .Where(binding => binding != null)
                .Where(binding => binding.gameObject.scene.IsValid())
                .Where(binding => string.Equals(
                    binding.projectId,
                    projectId,
                    StringComparison.Ordinal))
                .Where(binding => string.Equals(
                    binding.versionId,
                    versionId,
                    StringComparison.Ordinal))
                .OrderBy(binding => binding.GetEntityId())
                .ToArray();
        }
    }
}
