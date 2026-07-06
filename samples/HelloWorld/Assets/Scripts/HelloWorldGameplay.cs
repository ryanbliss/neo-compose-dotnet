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
    public sealed class HelloWorldGameplay : MonoBehaviour, ILandingSceneHost
    {
        private HelloWorldNeo neo;
        private NeoSaveSynchronizer synchronizer;
        private CoreUI coreUI;
        private DialogueUI dialogueUI;
        private LandingSceneGameplay landingSceneGameplay;
        private NeoDialogue activeDialogue;
        private IDisposable bitsSubscription;
        private IDisposable inventorySubscription;
        private IDisposable saveSubscription;
        private readonly GameAudio gameAudio = new();
        private int lastBits;
        private int lastInventoryCount;
        private bool dialogueOpenSoundPlayed;
        private bool activeDialogueCountsAsOutpostVisit;
        private Action activeDialogueCompletion;

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
            lastBits = neo.Save.Bits;
            lastInventoryCount = neo.Save.Inventory.Count;
            inventorySubscription = neo.Save.Inventory.OnChanged(OnInventoryChanged);
            bitsSubscription = neo.Save.OnChanged(Save.Fields.Bits, OnBitsChanged);
            // Live save sessions are wired inside the client: a co-editor (the
            // web tool) patching this play session's save raises the same typed
            // change events as local writes, with NeoChangeSource.External. The
            // catch-all below re-renders the HUD for external edits to fields
            // we don't subscribe to individually (location, quest, etc.).
            saveSubscription = neo.Save.OnChanged(OnSaveChanged);
            TriggerDialogue();
        }

        public string HelloWorldText => neo.Assets.Computed.fullText;
        public Planet World => neo.Save.World;
        public IReadOnlyOutpost CurrentOutpost => neo.Save.Location;
        public NeoReadOnlyList<IReadOnlyOutpost> Outposts => neo.Assets.Outposts;
        public NeoList<PlanetVisit> VisitedPlanets => neo.Save.Visited;
        public bool OldConsoleLandingOpen => landingSceneGameplay != null;

        public void OnVisitOutpost(IReadOnlyOutpost outpost)
        {
            if (!outpost.Save.Unlocked) return;

            AdvanceFlareClock(outpost);
            neo.Save.Location = outpost;
            neo.Save.World = outpost.Planet;
            neo.Save.Visited.Add(new PlanetVisit(outpost.Planet, CurrentUnixTime));
            if (neo.Save.Quest.FlareClock >= FlareOverflowThreshold
                && neo.Save.Quest.Stage != QuestStage.ended)
            {
                // Too slow: the world overflows and reboots. The save keeps
                // the player's cargo — the eeriest clue there is.
                UpdateUI();
                coreUI.ShowCrash(RebootAfterCrash);
                return;
            }
            TriggerDialogue(outpost);
        }

        internal const int FlareOverflowThreshold = 12;

        internal void RebootAfterCrash()
        {
            neo.Save.Quest.Reruns += 1;
            neo.Save.Quest.FlareClock = 0;
            var capitol = Outposts.First(o => o.Planet == Planet.earth);
            neo.Save.Location = capitol;
            neo.Save.World = Planet.earth;
            Run(SaveWithIndicatorAsync());
            // The greeter meets you for the "first" time — and recognizes
            // your cargo ("Capitol: cold boot", gated on Quest.Reruns).
            TriggerDialogue(capitol);
        }

        /// <summary>
        /// Urgency: every hop heats the dying runtime (hello-world-plot.md §4).
        /// Items earn their keep here — the Cloudsilk Parasol shields the first
        /// hops entirely, the Gyro Stabilizer waives the outer-system surcharge.
        /// </summary>
        internal void AdvanceFlareClock(IReadOnlyOutpost destination)
        {
            if (neo.Save.Quest.Stage == QuestStage.ended) return;
            bool hasParasol = HasItem("Cloudsilk Parasol");
            bool hasGyro = HasItem("Gyro Stabilizer");
            int clock = neo.Save.Quest.FlareClock;
            if (hasParasol && clock < 2) return;

            int cost = 1;
            if (IsOuterSystem(destination.Planet) && !hasGyro) cost += 1;
            neo.Save.Quest.FlareClock = clock + cost;
        }

        internal bool HasItem(string itemName)
        {
            return neo.Save.Inventory.Any(item => item.Name == itemName);
        }

        private static bool IsOuterSystem(Planet planet)
        {
            return planet == Planet.saturn
                || planet == Planet.uranus
                || planet == Planet.neptune
                || planet == Planet.pluto;
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

        public void TriggerDialogue(IReadOnlyOutpost outpost)
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

        public void OpenOldConsoleLanding()
        {
            if (neo == null) return;
            if (!CanOpenOldConsoleLanding) return;

            ClearDialogue();
            coreUI.SetVisible(false);
            landingSceneGameplay = LandingSceneGameplay.Open(neo, host: this);
        }

        bool ILandingSceneHost.DialogueIsOpen => activeDialogue != null;

        void ILandingSceneHost.CloseLandingScene() => CloseOldConsoleLanding();

        Awaitable ILandingSceneHost.SaveProgressAsync() => SaveWithIndicatorAsync();

        bool ILandingSceneHost.TryTriggerDialogue(NeoDialogueReference dialogueReference, Action onFinish) =>
            TriggerLandingDialogue(dialogueReference, onFinish);

        public void CloseOldConsoleLanding()
        {
            DestroyLandingScene();
            coreUI.SetVisible(true);
            UpdateUI();
        }

        public void ShowDialogue(NeoDialogue dialogue)
        {
            ShowDialogue(dialogue, countAsOutpostVisit: true, onFinish: null);
        }

        private void ShowDialogue(
            NeoDialogue dialogue,
            bool countAsOutpostVisit,
            Action onFinish)
        {
            if (dialogue.Primary is Outpost outpost)
            {
                dialogueUI.Show(outpost.FullDisplayText, outpost.Image, $"Traveling to {outpost.Planet.Text}...");
            }

            dialogue.OnShow += OnDialogueShow;
            dialogue.OnPause += OnDialoguePause;
            dialogue.OnFinish += OnDialogueFinish;
            dialogue.OnError += OnDialogueError;
            dialogueOpenSoundPlayed = false;
            activeDialogueCountsAsOutpostVisit = countAsOutpostVisit;
            activeDialogueCompletion = onFinish;
            activeDialogue = dialogue;
            dialogue.Start();
        }

        private bool TriggerLandingDialogue(NeoDialogueReference reference, Action onFinish)
        {
            ClearDialogue();
            if (!reference.TryTrigger(out NeoDialogue dialogue))
            {
                return false;
            }

            ShowDialogue(dialogue, countAsOutpostVisit: false, onFinish);
            return true;
        }

        public void OnDialogueShow(NeoDialogueTextNode node)
        {
            // First node of a dialogue gets the "open" chirp; later nodes the
            // softer "next" tick.
            gameAudio.Play(dialogueOpenSoundPlayed
                ? neo.Assets.Audio.DialogNextSfx
                : neo.Assets.Audio.DialogOpenSfx);
            dialogueOpenSoundPlayed = true;

            if (node.Primary is Outpost outpost)
            {
                dialogueUI.Show(outpost.FullDisplayText, outpost.Image, node.Text);
            }
            else
            {
                dialogueUI.Show(
                    node.Name ?? activeDialogue?.Name ?? "Old Console Landing",
                    null,
                    node.Text);
            }

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
            bool countAsOutpostVisit = activeDialogueCountsAsOutpostVisit;
            Action completion = activeDialogueCompletion;

            gameAudio.Play(neo.Assets.Audio.DialogCloseSfx);
            if (countAsOutpostVisit)
            {
                CurrentOutpost.Save.VisitCount += 1;
            }
            ClearDialogue();
            completion?.Invoke();
            if (neo.Save.Quest.Ending == WorldEnding.helloWorld)
            {
                // The Loop ending: the player erases the only persistent thing —
                // themselves. The save never commits; the world goes back to
                // factory innocence and the menu greets a brand-new run.
                LoopEnding();
                return;
            }
            if (neo.Save.Quest.Stage == QuestStage.ended)
            {
                // Every other ending preserves the run's final state.
                Run(SaveWithIndicatorAsync());
            }
            UpdateUI();
        }

        /// <summary>
        /// Raised by the Loop ending: the menu (which owns the save store)
        /// archives this save so the next boot starts from factory innocence.
        /// </summary>
        public event Action<string> OnEraseSave;

        private void LoopEnding()
        {
            OnEraseSave?.Invoke(synchronizer.CustomId);
            OnExitToMenu?.Invoke();
        }

        public void OnDialogueError(Exception exception)
        {
            Debug.LogError(exception);
            ClearDialogue();
        }

        private void OnInventoryChanged(
            NeoReadOnlyLookupSet<IReadOnlyItem> items,
            NeoChangeSource source)
        {
            int count = items.Count;
            if (count > lastInventoryCount)
            {
                gameAudio.Play(neo.Assets.Audio.ItemGetSfx);
            }
            lastInventoryCount = count;
            UpdateUI();
        }


        private void OnBitsChanged(int bits, NeoChangeSource source)
        {
            if (bits > lastBits)
            {
                gameAudio.Play(neo.Assets.Audio.BitsGainSfx);
            }
            else if (bits < lastBits)
            {
                gameAudio.Play(neo.Assets.Audio.BitsSpendSfx);
            }
            lastBits = bits;
            UpdateUI();
        }

        private void OnSaveChanged(NeoChangedArgs<Save.Fields> args)
        {
            if (args.Source == NeoChangeSource.External) UpdateUI();
        }

        private void UpdateUI()
        {
            coreUI.Render(
                HelloWorldText,
                neo.Save.Quest.NextHint,
                neo.Save.Quest.FlareClock,
                knowsStormExact: HasItem("Storm Corn"),
                neo.Assets.Art.ShipAnimation,
                neo.Assets.Art.FlareAnimation,
                neo.Assets.Art.SunSprite,
                neo.Assets.Audio.RocketThrustSfx,
                ParentPlanetSprite,
                CurrentOutpost, Outposts,
                neo.Save.Bits, neo.Save.Inventory.ToArray(),
                HasDialogueAvailable,
                OnVisitOutpost,
                CanOpenOldConsoleLanding,
                OpenOldConsoleLanding,
                onSave: OnSaveClicked,
                onReset: () => Run(ResetAsync()),
                onMenu: () => OnExitToMenu?.Invoke()
            );
        }

        private bool CanOpenOldConsoleLanding => CurrentOutpost.Name == "Capitol OG";

        /// <summary>
        /// Map art for worlds whose outposts are moons. Authored sprites from
        /// the schema (Assets.Art) — null for worlds that ARE the outpost.
        /// </summary>
        private Sprite ParentPlanetSprite(string planetOptionId)
        {
            if (planetOptionId == Planet.jupiter.optionId) return neo.Assets.Art.JupiterSprite;
            if (planetOptionId == Planet.saturn.optionId) return neo.Assets.Art.SaturnSprite;
            return null;
        }

        /// <summary>
        /// Would visiting this outpost start a conversation right now? Trigger
        /// evaluation is pure until <c>Start()</c>, so peeking is free — this
        /// is what feeds the map's "something to do here" badges.
        /// </summary>
        private bool HasDialogueAvailable(IReadOnlyOutpost outpost)
        {
            if (neo == null) return false;
            if (neo.Dialogues.Outposts.Introductions.TryTrigger(outpost, out NeoDialogue intro))
            {
                intro.Dispose();
                return true;
            }
            if (neo.Dialogues.Outposts.Visits.TryTrigger(outpost, out NeoDialogue visit))
            {
                visit.Dispose();
                return true;
            }
            return false;
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
            inventorySubscription?.Dispose();
            inventorySubscription = null;
            saveSubscription?.Dispose();
            saveSubscription = null;
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
            activeDialogueCountsAsOutpostVisit = false;
            activeDialogueCompletion = null;
            dialogueUI.Reset();
        }

        internal void OnDestroy()
        {
            DisposeClient();
            OutpostFunctionHandler.AnimationPlayer = null;
            gameAudio.Dispose();
            DestroyLandingScene();
            dialogueUI.Dispose();
            coreUI.Dispose();
        }

        private void DestroyLandingScene()
        {
            var landing = landingSceneGameplay;
            landingSceneGameplay = null;
            if (landing != null) SampleUI.DestroyObject(landing.gameObject);
        }

        private static int CurrentUnixTime => (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
