// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        private readonly HelloWorldUi ui = new();

        protected void Start()
        {
            LoadClient();
        }

        public void LoadClient()
        {
            string json = File.ReadAllText(NeoAssetsFilePath);
            neo = HelloWorldNeo.Load(json, OnLoadSave, OnHandleSave);
        }

        public string HelloWorldText => neo.Assets.computed.fullText;
        public Planet World => neo.Save.world;
        public IReadOnlyList<PlanetVisitSaved> VisitedPlanets => neo.Save.visited;

        public void Visit(Planet planet)
        {
            neo.Save.world = planet;
            neo.Save.visited.Add(
                PlanetVisit.factory(
                    neo.Runtime,
                    planet,
                    CurrentUnixTime
                )
            );
        }

        public void Save()
        {
            neo.Runtime.Save();
        }

        public void ResetSave()
        {
            if (File.Exists(SaveFilePath)) File.Delete(SaveFilePath);
            LoadClient();
        }

        protected void OnGUI()
        {
            if (neo is null) return;
            ui.Render(HelloWorldText, World, VisitedPlanets, Visit, Save, ResetSave);
        }

        protected string OnLoadSave()
        {
            return File.ReadAllText(SaveFilePath);
        }

        protected void OnHandleSave(string content)
        {
            File.WriteAllText(SaveFilePath, content);
        }

        // ──────────────────────────────────────────────
        // Static file loading
        // ──────────────────────────────────────────────

        private static readonly string FixturesRoot = "Assets/Scripts";
        private static readonly string FileName = "project.json";

        private static string NeoAssetsFilePath => Path.Combine(FixturesRoot, FileName);

        private static string SaveFilePath => $"{Application.persistentDataPath}/save1.json";

        private static int CurrentUnixTime => (int)System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
