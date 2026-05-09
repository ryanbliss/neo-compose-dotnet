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
            // reference lookup to one of the "Hello World" outputs in `neo.Assets.LookupContainer.LookupList`
            Debug.Log(neo.Assets.LookupContainer.Lookup?.Name ?? "Lookup not selected");
            TriggerDialogue();
        }

        public string HelloWorldText => neo.Assets.Computed.fullText;
        public Planet World => neo.Save.World;
        public NeoList<PlanetVisit> VisitedPlanets => neo.Save.Visited;

        public void OnVisit(Planet planet)
        {
            neo.Save.World = planet;
            neo.Save.Visited.Add(new PlanetVisit(planet, CurrentUnixTime));
        }

        protected void OnInventoryChanged()
        {
            Debug.Log("Inventory changed");
        }
        
        protected void OnBitsChanged(int bits)
        {
            Debug.Log($"Bits changed {bits}");
        }

        public void TriggerDialogue()
        {
            var outpost = neo.Save.Location;
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
                node.Text,
                node.SaveChoice ? "Choice will be remembered" : "Choose a response"
            );
            if (node.Options.Count > 0)
            {
                foreach (NeoDialogueTextOption option in node.Options)
                {
                    DialogueUI.PrepareOptionButton(
                        buttonText: option.Text,
                        onClick: option.Select,
                        rememberChoice: node.SaveChoice
                    );
                }
                return;
            }

            DialogueUI.PrepareOptionButton(
                buttonText: "Continue",
                onClick: node.Next
            );
        }

        public void OnDialogueFinish()
        {
            Debug.Log($"Inventory {string.Join(",", neo.Save.Inventory.Ids)}");
            ClearDialogue();
            string ifIWereBlueDialogueId = "6efd8f7b-7491-4646-b4cc-05589bca92ab";
            if (neo.Dialogues.VisitCount(ifIWereBlueDialogueId) < 2)
            {
                if (neo.Dialogues.TryTrigger(ifIWereBlueDialogueId, out NeoDialogue dialogue))
                {
                    ShowDialogue(dialogue);
                }
            }
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
                HelloWorldText, World, VisitedPlanets,
                OnVisit, OnSave, OnResetSave
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
                return $"{outpost.Name} - {outpost.Planet}";
            }
            return "Dialogue";
        }

        // ──────────────────────────────────────────────
        // Static file loading
        // ──────────────────────────────────────────────

        private static readonly string ProjectJsonPath = Path.Combine("Assets/Resources/Neo", "project.json");

        private static string SaveJsonPath => $"{Application.persistentDataPath}/save1.json";

        private static int CurrentUnixTime => (int)System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
