// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using NeoCompose.Runtime.Json;
using Newtonsoft.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Loads Neo types
    /// </summary>
    public class NeoLoader
    {
        public NeoClient Load(
            string projectJson,
            NeoClient.LoadSave loadSave,
            NeoClient.HandleSave handleSave,
            NeoAssetDatabase? assetDatabase = null,
            NeoClient.BuildSaveName? buildSaveName = null)
        {
            return Load(
                projectJson,
                loadSave,
                handleSave,
                assetDatabase,
                null,
                null,
                buildSaveName);
        }

        public NeoClient Load(
            string projectJson,
            NeoClient.LoadSave loadSave,
            NeoClient.HandleSave handleSave,
            NeoAssetDatabase? assetDatabase,
            NeoLocalizationOptions? localizationOptions,
            INeoLocalizationLocaleFileSource? localizationFileSource,
            NeoClient.BuildSaveName? buildSaveName = null)
        {
            ProjectData data = JsonConvert.DeserializeObject<ProjectData>(projectJson)
                ?? throw new System.InvalidOperationException("Neo Compose project JSON could not be deserialized.");
            NeoProjectDataValidator.Validate(data);
            localizationOptions ??= NeoComposeConfig.LoadDefault()?.ToLocalizationOptions();
            var localization = NeoLocalization.LoadMain(
                data.localization,
                localizationFileSource ?? new NeoResourcesLocalizationLocaleFileSource(),
                localizationOptions);
            return new(data, loadSave, handleSave, assetDatabase ?? NeoAssetDatabase.LoadDefault(), localization, buildSaveName);
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
