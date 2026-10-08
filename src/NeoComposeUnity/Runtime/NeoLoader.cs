// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#nullable enable

using System;
using UnityEngine;
using NeoCompose.Runtime.Json;

namespace NeoCompose.Runtime
{
    /// <summary>
    /// Loads Neo classes
    /// </summary>
    public class NeoLoader
    {
        /// <summary>
        /// Builds a <see cref="NeoClient"/> over an <see cref="INeoSaveLoader"/>
        /// (normally a <see cref="NeoSaveSynchronizer"/> from a
        /// <see cref="NeoProjectStore"/>). The project schema comes from the loader's
        /// owning store — there is no <c>projectJson</c> argument. Resolves the active
        /// save's content asynchronously (conflict / migration / clone handled by the
        /// loader) before constructing the client. Initialization replays constructor
        /// defaults across frames on the Unity main thread.
        /// The client is only published after replay and validation complete.
        /// </summary>
        public async Awaitable<NeoClient> Load(
            INeoSaveLoader loader,
            NeoAssetDatabase? assetDatabase = null,
            NeoLocalizationOptions? localizationOptions = null,
            INeoLocalizationLocaleFileSource? localizationFileSource = null,
            NeoSaveOptions? saveOptions = null,
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (loader == null)
                throw new ArgumentNullException(nameof(loader));
            ProjectData data = loader.Schema
                ?? throw new InvalidOperationException("Neo Compose save loader has no project schema.");
            NeoProjectDataValidator.Validate(data);
            NeoClient? userClient = await ResolveUserClientAsync(loader, data, cancellationToken);
            localizationOptions ??= NeoComposeConfig.LoadDefault()?.ToLocalizationOptions();
            var localization = NeoLocalization.LoadMain(
                data.localization,
                localizationFileSource ?? new NeoResourcesLocalizationLocaleFileSource(),
                localizationOptions);
            string? content = await loader.LoadSaveContentAsync();
            cancellationToken.ThrowIfCancellationRequested();
            assetDatabase ??= NeoAssetDatabase.LoadDefault();
            return await NeoClient.CreateAsync(loader, content, assetDatabase, localization,
                saveOptions, cancellationToken, userClient: userClient);
        }

        /// <summary>
        /// The user client a save of <paramref name="data"/> reads User data
        /// through (P104 §4.3), once the current store's load finishes, or
        /// null when the save's store is a tooling store.
        /// </summary>
        private static async Awaitable<NeoClient?> ResolveUserClientAsync(
            INeoSaveLoader loader,
            ProjectData data,
            System.Threading.CancellationToken cancellationToken)
        {
            if (loader is NeoSaveSynchronizer { Core: { LoadsUserFile: false } })
                return null;
            var store = NeoProjectStore.Current
                ?? throw new InvalidOperationException(
                    "Load a `NeoProjectStore` before loading a save. User data needs its user file.");
            if (loader is NeoSaveSynchronizer synchronizer && !store.Opened(synchronizer))
            {
                throw new InvalidOperationException(
                    "This save's store isn't `NeoProjectStore.Current`, so it has no user file.");
            }
            NeoClient? userClient = await store.LoadedUserClientAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (store.IsDisposed)
            {
                throw new InvalidOperationException(
                    "`NeoProjectStore.Current` was disposed while this save waited for its user file.");
            }
            if (userClient == null && store.State == NeoProjectStoreState.Errored)
            {
                throw new InvalidOperationException(
                    "`NeoProjectStore.Current` failed to load, so it has no user file. Retry its `LoadAsync` first.");
            }
            if (!ReferenceEquals(data, store.Schema))
            {
                throw new InvalidOperationException(
                    "A custom save loader's `Schema` must be `NeoProjectStore.Current.Schema`.");
            }
            return userClient
                ?? throw new InvalidOperationException("`NeoProjectStore.Current` finished loading without a user client.");
        }
    }

    internal static class NeoProjectDataValidator
    {
        public static void Validate(ProjectData data)
        {
            if (data.dialogues == null)
                return;
            foreach (var dialogueEntry in data.dialogues)
            {
                var dialogue = dialogueEntry.Value;
                if (dialogue.nodes == null)
                    continue;
                foreach (var nodeEntry in dialogue.nodes)
                {
                    if (nodeEntry.Value is not DialogueActionsNode actionsNode)
                        continue;
                    foreach (var action in actionsNode.actions ?? Array.Empty<DialogueAction>())
                    {
                        if (action is not DialoguePauseAction pauseAction)
                            continue;
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
