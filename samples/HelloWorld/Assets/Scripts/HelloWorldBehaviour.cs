// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using UnityEngine;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// Minimal MonoBehaviour that loads generated Neo Compose types and
    /// renders a tiny immediate-mode UI for the sample project.
    /// </summary>
    public class HelloWorldBehaviour : MonoBehaviour
    {
        protected HelloWorldNeo neo;
        private CoreUI CoreUI;
        private DialogueUI DialogueUI;
        private NeoDialogue activeDialogue;
        private IDisposable bitsSubscription;

        protected void Awake()
        {
            PrepareUI();
            LoadClient();
        }

        public void LoadClient()
        {
            PrepareUI();
            ClearDialogue();
            string json = File.ReadAllText(ProjectJsonPath);
            neo = HelloWorldNeo.Load(json, OnLoadSave, OnCommitSave);
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

        protected void OnInventoryChanged()
        {
            UpdateUI();
        }

        protected void OnBitsChanged(int bits)
        {
            UpdateUI();
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
                PrepareUI();
                DialogueUI.Show(outpost.FullDisplayText, outpost.Image, $"Traveling to {outpost.Planet.Text}...");
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

            DialogueUI.Show(outpost.FullDisplayText, outpost.Image, node.Text);

            if (node.Options.Count > 0)
            {
                foreach (NeoDialogueTextOption option in node.Options)
                {
                    bool alreadyChosen = node.SaveChoice && option.HasChosen();
                    DialogueUI.PrepareOptionButton(
                        buttonText: option.Text,
                        selectable: option.Selectable,
                        onClick: option.Select,
                        alreadyChosen: alreadyChosen
                    );
                }
                return;
            }

            DialogueUI.PrepareOptionButton(
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
            DialogueUI.Reset();
        }

        public void OnSave()
        {
            neo.Commit();
        }

        public void OnResetSave()
        {
            if (File.Exists(SaveJsonPath)) File.Delete(SaveJsonPath);
            LoadClient();
        }

        protected string OnLoadSave()
        {
            return File.ReadAllText(SaveJsonPath);
        }

        protected void OnCommitSave(string content)
        {
            File.WriteAllText(SaveJsonPath, content);
        }

        protected void UpdateUI()
        {
            CoreUI.Render(
                HelloWorldText, CurrentOutpost, Outposts, VisitedPlanets,
                neo.Save.Bits, neo.Save.Inventory.ToArray(),
                OnVisitOutpost, OnSave, OnResetSave
            );
        }

        protected void OnDestroy()
        {
            ClearDialogue();
            OutpostFunctionHandler.AnimationPlayer = null;
            DialogueUI?.Dispose();
            CoreUI?.Dispose();
            bitsSubscription?.Dispose();
            neo.Dispose();
        }

        private void ClearDialogue()
        {
            activeDialogue?.Dispose();
            activeDialogue = null;

            DialogueUI?.Reset();
        }

        private void PrepareUI()
        {
            CoreUI ??= new();
            DialogueUI ??= new();
            OutpostFunctionHandler.AnimationPlayer = DialogueUI;
        }

        // ──────────────────────────────────────────────
        // Static file loading
        // ──────────────────────────────────────────────

        private static readonly string ProjectJsonPath = Path.Combine("Assets/Resources/Neo", "project.json");

        private static string SaveJsonPath => $"{Application.persistentDataPath}/save1.json";

        private static int CurrentUnixTime => (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
