// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.IO;
using NeoCompose.Runtime;
using UnityEngine;

namespace HelloWorld.Assets.Scripts
{
    /// <summary>
    /// Minimal MonoBehaviour that proves the sample can reference and
    /// instantiate types from the `com.ryanbliss.neocompose` package.
    /// Drop on a GameObject and watch for the Start log line.
    /// </summary>
    public class HelloWorldBehaviour : MonoBehaviour
    {
        protected NeoClient client;

        protected void Start()
        {
            var loader = new NeoLoader();
            client = loader.Load(
                File.ReadAllText(NeoAssetsFilePath),
                OnLoadSave,
                OnHandleSave
            );
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

        private static readonly string FixturesRoot = "Assets/Neo";
        private static readonly string FileName = "project-example.json";

        private static string NeoAssetsFilePath
        {
            get => Path.Combine(FixturesRoot, FileName);
        }

        private static string SaveFilePath
        {
            get
            {
                string fileName = "/save1.json";
                return Application.persistentDataPath + fileName;
            }
        }
    }
}
