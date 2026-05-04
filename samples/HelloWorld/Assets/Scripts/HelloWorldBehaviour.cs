// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
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
        private readonly HelloWorldUi ui = new();

        protected void Start()
        {
            LoadClient();
        }

        public void LoadClient()
        {
            string json = File.ReadAllText(NeoAssetsFilePath);
            neo = HelloWorldNeo.Load(json, OnLoadSave, OnHandleSave);
            Debug.Log(neo.Assets.LookupContainer.Lookup.Name);
        }

        public string HelloWorldText => neo.Assets.Computed.fullText;
        public Planet World => neo.Save.World;
        public NeoList<PlanetVisit> VisitedPlanets => neo.Save.Visited;

        public void Visit(Planet planet)
        {
            neo.Save.World = planet;
            neo.Save.Visited.Add(
                PlanetVisit.factory(
                    neo,
                    planet,
                    CurrentUnixTime
                )
            );
        }

        public void Save()
        {
            neo.Commit();
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

        private static readonly string NeoResourcesRoot = "Assets/Resources/Neo";
        private static readonly string FileName = "project.json";

        private static string NeoAssetsFilePath => Path.Combine(NeoResourcesRoot, FileName);

        private static string SaveFilePath => $"{Application.persistentDataPath}/save1.json";

        private static int CurrentUnixTime => (int)System.DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
