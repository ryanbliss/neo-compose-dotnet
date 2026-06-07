// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Linq;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using UnityEngine;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// The gameplay screen: owns one <see cref="HelloWorldNeo"/> client (loaded from a
    /// <see cref="NeoSaveSynchronizer"/>), drives the dialogue flow, and renders the
    /// game HUD via <see cref="CoreUI"/> / <see cref="DialogueUI"/>.
    /// </summary>
    /// <remarks>
    /// Spawned by <see cref="HelloWorldMenu"/> for the selected save and torn down when
    /// the player returns to the menu. Its lifecycle is self-contained: <see cref="Enter"/>
    /// loads the client and <see cref="OnDestroy"/> disposes it, so destroying the
    /// gameplay GameObject is the only thing the menu has to do — no cross-object
    /// disposal bookkeeping.
    /// </remarks>
    public sealed class HelloWorldGameplay : MonoBehaviour
    {
        private HelloWorldNeo neo;
        private NeoSaveSynchronizer synchronizer;
        private CoreUI coreUI;
        private DialogueUI dialogueUI;
        private NeoDialogue activeDialogue;
        private IDisposable bitsSubscription;

        /// <summary>Raised when the player chooses to return to the save-file menu.</summary>
        public event Action OnExitToMenu;

        /// <summary>
        /// Loads the active save and starts gameplay. Awaitable because loading a
        /// cloud save hits the network — never block the main thread on it (that
        /// deadlocks Unity, since the web request needs the main loop to progress).
        /// The menu fires this and forgets; tests await it.
        /// </summary>
        public Awaitable EnterAsync(NeoSaveSynchronizer synchronizer)
        {
            this.synchronizer = synchronizer ?? throw new ArgumentNullException(nameof(synchronizer));
            coreUI = new CoreUI();
            dialogueUI = new DialogueUI();
            OutpostFunctionHandler.AnimationPlayer = dialogueUI;
            return LoadClientAsync();
        }

        private async Awaitable LoadClientAsync()
        {
            var loaded = await HelloWorldNeo.Load(synchronizer);
            if (this == null)
            {
                // Torn down while the (possibly cloud) load was in flight.
                loaded.Dispose();
                return;
            }

            neo = loaded;
            neo.Save.Inventory.OnChanged += OnInventoryChanged;
            bitsSubscription = neo.Save.OnChanged(Save.Fields.Bits, OnBitsChanged);
            TriggerDialogue();
        }

        public string HelloWorldText => neo.Assets.Computed.fullText;
        public Planet World => neo.Save.World;
        public ReadOnlyOutpost CurrentOutpost => neo.Save.Location;
        public NeoReadOnlyList<ReadOnlyOutpost> Outposts => neo.Assets.Outposts;
        public NeoList<PlanetVisit> VisitedPlanets => neo.Save.Visited;

        public void OnVisitOutpost(ReadOnlyOutpost outpost)
        {
            if (!outpost.Save.Unlocked) return;

            neo.Save.Location = outpost;
            neo.Save.World = outpost.Planet;
            neo.Save.Visited.Add(new PlanetVisit(outpost.Planet, CurrentUnixTime));
            TriggerDialogue(outpost);
        }

        /// <summary>Commits the current save through the synchronizer (local + cloud when signed in).</summary>
        public Awaitable SaveAsync()
        {
            return neo.CommitAsync();
        }

        /// <summary>Discards unsaved changes by reloading the client from the last commit.</summary>
        public async Awaitable ResetAsync()
        {
            DisposeClient();
            await LoadClientAsync();
        }

        public void TriggerDialogue()
        {
            TriggerDialogue(neo.Save.Location);
        }

        public void TriggerDialogue(ReadOnlyOutpost outpost)
        {
            UpdateUI();
            if (neo.Dialogues.Outposts.Introductions.TryTrigger(outpost, out NeoDialogue introDialogue))
            {
                ShowDialogue(introDialogue);
            }
            else if (neo.Dialogues.Outposts.Visits.TryTrigger(outpost, out NeoDialogue visitDialogue))
            {
                ShowDialogue(visitDialogue);
            }
        }

        public void ShowDialogue(NeoDialogue dialogue)
        {
            if (dialogue.Primary is ReadOnlyOutpost outpost)
            {
                dialogueUI.Show(outpost.FullDisplayText, outpost.Image, $"Traveling to {outpost.Planet.Text}...");
            }

            dialogue.OnShow += OnDialogueShow;
            dialogue.OnPause += OnDialoguePause;
            dialogue.OnFinish += OnDialogueFinish;
            dialogue.OnError += OnDialogueError;
            dialogue.Start();
            activeDialogue = dialogue;
        }

        public void OnDialogueShow(NeoDialogueTextNode node)
        {
            if (node.Primary is not ReadOnlyOutpost outpost)
                throw new Exception($"Expected linked type of {typeof(ReadOnlyOutpost)}");

            dialogueUI.Show(outpost.FullDisplayText, outpost.Image, node.Text);

            if (node.Options.Count > 0)
            {
                foreach (NeoDialogueTextOption option in node.Options)
                {
                    bool alreadyChosen = node.SaveChoice && option.HasChosen();
                    dialogueUI.PrepareOptionButton(
                        buttonText: option.Text,
                        selectable: option.Selectable,
                        onClick: option.Select,
                        alreadyChosen: alreadyChosen
                    );
                }
                return;
            }

            dialogueUI.PrepareOptionButton(
                buttonText: "Continue",
                selectable: true,
                onClick: node.Next
            );
        }

        public void OnDialoguePause(NeoDialoguePauseAction action)
        {
            if (action.AutoResumeDurationSeconds is not null)
            {
                Debug.Log($"pause action w/ reason {action.Reason}, will auto resume in {action.AutoResumeDurationSeconds}");
            }
            else
            {
                Debug.Log($"pause action w/ reason {action.Reason}, no auto resume");
                action.Resume();
            }
        }

        public void OnDialogueFinish()
        {
            CurrentOutpost.Save.VisitCount += 1;
            ClearDialogue();
            UpdateUI();
        }

        public void OnDialogueError(Exception exception)
        {
            Debug.LogError(exception);
            dialogueUI.Reset();
        }

        private void OnInventoryChanged() => UpdateUI();

        private void OnBitsChanged(int bits) => UpdateUI();

        private void UpdateUI()
        {
            coreUI.Render(
                HelloWorldText, CurrentOutpost, Outposts, VisitedPlanets,
                neo.Save.Bits, neo.Save.Inventory.ToArray(),
                OnVisitOutpost,
                onSave: OnSaveClicked,
                onReset: () => Run(ResetAsync()),
                onMenu: () => OnExitToMenu?.Invoke()
            );
        }

        private void OnSaveClicked() => Run(SaveWithIndicatorAsync());

        /// <summary>Commits while showing the Save button's "Saving…" state.</summary>
        private async Awaitable SaveWithIndicatorAsync()
        {
            coreUI.SetSaving(true);
            try
            {
                await SaveAsync();
            }
            finally
            {
                if (this != null) coreUI.SetSaving(false);
            }
        }

        private void DisposeClient()
        {
            // neo is set after the (async) load completes, so it may be null if we are
            // torn down mid-load — guard rather than assume.
            if (neo == null) return;
            ClearDialogue();
            bitsSubscription?.Dispose();
            bitsSubscription = null;
            neo.Dispose();
            neo = null;
        }

        /// <summary>
        /// Fire-and-forget a UI-triggered task on the Unity loop without blocking the
        /// main thread (so cloud network calls can progress), logging any failure.
        /// </summary>
        private static async void Run(Awaitable task)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // Expected when the developer stops Play mode mid-request — not an error.
            }
            catch (Exception exception)
            {
                Debug.LogError(exception);
            }
        }

        private void ClearDialogue()
        {
            activeDialogue?.Dispose();
            activeDialogue = null;
            dialogueUI.Reset();
        }

        internal void OnDestroy()
        {
            DisposeClient();
            OutpostFunctionHandler.AnimationPlayer = null;
            dialogueUI.Dispose();
            coreUI.Dispose();
        }

        private static int CurrentUnixTime => (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
