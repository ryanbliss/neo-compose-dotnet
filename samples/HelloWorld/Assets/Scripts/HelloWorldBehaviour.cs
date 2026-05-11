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
        private HelloWorldUi CoreUI;
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
            Debug.Log($"Added: {neo.Save.Inventory.LastOrDefault()?.Name}");
        }
        
        protected void OnBitsChanged(int bits)
        {
            Debug.Log($"Bits changed {bits}");
        }

        public void TriggerDialogue()
        {
            TriggerDialogue(neo.Save.Location);
        }

        public void TriggerDialogue(ReadOnlyOutpost outpost)
        {
            if (neo.Dialogues.Outposts.Introductions.TryTrigger(outpost, out NeoDialogue dialogue))
            {
                ShowDialogue(dialogue);
            }
        }

        public void ShowDialogue(NeoDialogue dialogue)
        {
            dialogue.OnShow += OnDialogueShow;
            dialogue.OnFinish += OnDialogueFinish;
            dialogue.OnError += OnDialogueError;
            dialogue.Start();
            activeDialogue = dialogue;
        }

        public void OnDialogueShow(NeoDialogueTextNode node)
        {
            PrepareUI();
            DialogueUI.Show(
                SpeakerLabel(node),
                node.Text
            );
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

        public void OnDialogueFinish()
        {
            CurrentOutpost.Save.VisitCount += 1;
            Debug.Log($"Inventory {neo.Save.Inventory.Ids.Count}");
            ClearDialogue();
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

        protected void Update()
        {
            CoreUI.Render(
                HelloWorldText, CurrentOutpost, Outposts, VisitedPlanets,
                OnVisitOutpost, OnSave, OnResetSave
            );
        }

        protected void OnDestroy()
        {
            ClearDialogue();
            DialogueUI?.Dispose();
            CoreUI?.Dispose();
            bitsSubscription?.Dispose();
        }

        private void ClearDialogue()
        {
            if (activeDialogue != null)
            {
                activeDialogue.Dispose();
                activeDialogue = null;
            }

            DialogueUI?.Reset();
        }

        private void PrepareUI()
        {
            if (CoreUI == null) CoreUI = new();
            if (DialogueUI == null) DialogueUI = new();
        }

        private static string SpeakerLabel(NeoDialogueTextNode node)
        {
            if (node.Primary is ReadOnlyOutpost outpost)
            {
                return $"{outpost.FullDisplayText}";
            }
            return "Dialogue";
        }

        // ──────────────────────────────────────────────
        // Static file loading
        // ──────────────────────────────────────────────

        private static readonly string ProjectJsonPath = Path.Combine("Assets/Resources/Neo", "project.json");

        private static string SaveJsonPath => $"{Application.persistentDataPath}/save1.json";

        private static int CurrentUnixTime => (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
