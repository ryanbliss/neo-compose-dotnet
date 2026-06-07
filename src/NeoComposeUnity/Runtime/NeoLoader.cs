// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using UnityEngine;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Loads Neo types
    /// </summary>
    public class NeoLoader
    {
        /// <summary>
        /// Builds a <see cref="NeoClient"/> over an <see cref="INeoSaveLoader"/>
        /// (normally a <see cref="NeoSaveSynchronizer"/> from a
        /// <see cref="NeoProjectStore"/>). The project schema comes from the loader's
        /// owning store — there is no <c>projectJson</c> argument. Resolves the active
        /// save's content asynchronously (conflict / migration / clone handled by the
        /// loader) before constructing the client.
        /// </summary>
        public async Awaitable<NeoClient> Load(
            INeoSaveLoader loader,
            NeoAssetDatabase? assetDatabase = null,
            NeoLocalizationOptions? localizationOptions = null,
            INeoLocalizationLocaleFileSource? localizationFileSource = null,
            NeoSaveOptions? saveOptions = null)
        {
            if (loader == null) throw new ArgumentNullException(nameof(loader));
            ProjectData data = loader.Schema
                ?? throw new InvalidOperationException("Neo Compose save loader has no project schema.");
            NeoProjectDataValidator.Validate(data);
            localizationOptions ??= NeoComposeConfig.LoadDefault()?.ToLocalizationOptions();
            var localization = NeoLocalization.LoadMain(
                data.localization,
                localizationFileSource ?? new NeoResourcesLocalizationLocaleFileSource(),
                localizationOptions);
            string? content = await loader.LoadSaveContentAsync();
            return new NeoClient(
                loader,
                content,
                assetDatabase ?? NeoAssetDatabase.LoadDefault(),
                localization,
                saveOptions);
        }
    }

    internal static class NeoProjectDataValidator
    {
        public static void Validate(ProjectData data)
        {
            if (data.dialogues == null) return;
            foreach (var dialogueEntry in data.dialogues)
            {
                var dialogue = dialogueEntry.Value;
                if (dialogue.nodes == null) continue;
                foreach (var nodeEntry in dialogue.nodes)
                {
                    if (nodeEntry.Value is not DialogueActionsNode actionsNode) continue;
                    foreach (var action in actionsNode.actions ?? Array.Empty<DialogueAction>())
                    {
                        if (action is not DialoguePauseAction pauseAction) continue;
                        ValidatePauseAction(dialogue.id, actionsNode.id, pauseAction);
                    }
                }
            }
        }

        private static void ValidatePauseAction(
            string dialogueId,
            string nodeId,
            DialoguePauseAction action)
        {
            string context =
                $"dialogue '{dialogueId}', actions node '{nodeId}', action '{action.id}'";
            if (action.reason == null)
            {
                throw new InvalidOperationException(
                    $"Invalid pause dialogue action in {context}: field 'reason' is required.");
            }
            if (action.autoResumeDurationSeconds is double duration)
            {
                if (double.IsNaN(duration) || double.IsInfinity(duration))
                {
                    throw new InvalidOperationException(
                        $"Invalid pause dialogue action in {context}: field 'autoResumeDurationSeconds' must be finite.");
                }
                if (duration < 0)
                {
                    throw new InvalidOperationException(
                        $"Invalid pause dialogue action in {context}: field 'autoResumeDurationSeconds' must be greater than or equal to zero.");
                }
            }
        }
    }
}
