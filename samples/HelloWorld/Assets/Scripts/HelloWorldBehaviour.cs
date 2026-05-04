// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.IO;
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
        private HelloWorldUi ui;

        protected void Awake()
        {
            LoadClient();
            ui = new();
        }

        public void LoadClient()
        {
            string json = File.ReadAllText(ProjectJsonPath);
            neo = HelloWorldNeo.Load(json, OnLoadSave, OnCommitSave);
            // reference lookup to one of the "Hello World" outputs in `neo.Assets.LookupContainer.LookupList`
            Debug.Log(neo.Assets.LookupContainer.Lookup.Name);
        }

        public string HelloWorldText => neo.Assets.Computed.fullText;
        public Planet World => neo.Save.World;
        public NeoList<PlanetVisit> VisitedPlanets => neo.Save.Visited;

        public void OnVisit(Planet planet)
        {
            neo.Save.World = planet;
            neo.Save.Visited.Add(new PlanetVisit(planet, CurrentUnixTime));
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

        protected void Update()
        {
            ui.Render(
                HelloWorldText, World, VisitedPlanets,
                OnVisit, OnSave, OnResetSave
            );
        }

        protected string OnLoadSave()
        {
            return File.ReadAllText(SaveJsonPath);
        }

        protected void OnCommitSave(string content)
        {
            File.WriteAllText(SaveJsonPath, content);
        }

        protected void OnDestroy()
        {
            ui?.Dispose();
        }

        // ──────────────────────────────────────────────
        // Static file loading
        // ──────────────────────────────────────────────

        private static readonly string ProjectJsonPath = Path.Combine("Assets/Resources/Neo", "project.json");

        private static string SaveJsonPath => $"{Application.persistentDataPath}/save1.json";

        private static int CurrentUnixTime => (int)System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
