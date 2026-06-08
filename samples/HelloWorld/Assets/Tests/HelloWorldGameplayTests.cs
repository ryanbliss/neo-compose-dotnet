// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelloWorld.Assets.Scripts;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace HelloWorld.Assets.Tests
{
    /// <summary>
    /// Drives <see cref="HelloWorldGameplay"/> directly over a save synchronizer the
    /// same way the menu does — a brand-new save from <c>CreateNew</c> or an existing
    /// one reopened by its <c>customId</c> from <c>Open</c> (no hardcoded save id).
    /// Covers visiting outposts, saving + reloading, and resetting unsaved changes.
    /// </summary>
    public class HelloWorldGameplayTests
    {
        private const string SampleProjectJson = "Assets/Resources/Neo/project.json";

        private string saveDirectory;
        private readonly List<GameObject> spawned = new();

        [SetUp]
        public void SetUp()
        {
            saveDirectory = Path.Combine(Path.GetTempPath(), "neo-gameplay-" + Path.GetRandomFileName());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in spawned)
            {
                if (go != null) Object.DestroyImmediate(go);
            }
            spawned.Clear();
            if (Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, recursive: true);
        }

        /// <summary>A loaded local store over this test's temp save folder.</summary>
        private NeoProjectStore LoadedStore()
        {
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(File.ReadAllText(SampleProjectJson)),
                localStore: new NeoFileLocalSaveStore(saveDirectory));
            store.LoadAsync().GetAwaiter().GetResult();
            return store;
        }

        /// <summary>Spawns a gameplay screen over the given save (as the menu's Continue / Create do).</summary>
        private HelloWorldGameplay Spawn(NeoSaveSynchronizer synchronizer)
        {
            var go = new GameObject("HelloWorld Gameplay");
            spawned.Add(go);
            var gameplay = go.AddComponent<HelloWorldGameplay>();
            gameplay.EnterAsync(synchronizer).GetAwaiter().GetResult();
            return gameplay;
        }

        [Test]
        public void VisitOutpost_UpdatesLocationGeneratedTextAndVisitCounts()
        {
            var gameplay = Spawn(LoadedStore().CreateNew());

            Assert.AreEqual(HelloText(Planet.earth), gameplay.HelloWorldText);
            Assert.AreEqual(Planet.earth, gameplay.World);
            var startingOutpost = gameplay.CurrentOutpost;
            CollectionAssert.AreEqual(new[] { Planet.earth }, VisitedPlanets(gameplay));

            var destination = gameplay.Outposts.First(outpost =>
                outpost.valueId != startingOutpost.valueId);
            destination.Save.Unlocked = true;
            var startingVisitCount = destination.Save.VisitCount;

            gameplay.OnVisitOutpost(destination);

            Assert.AreEqual(destination.valueId, gameplay.CurrentOutpost.valueId);
            Assert.AreEqual(destination.Planet, gameplay.World);
            Assert.AreEqual(HelloText(destination.Planet), gameplay.HelloWorldText);
            Assert.AreEqual(startingVisitCount, destination.Save.VisitCount);
            CollectionAssert.AreEqual(
                new[] { Planet.earth, destination.Planet },
                VisitedPlanets(gameplay));
        }

        [Test]
        public void VisitedPlanetsPanelUsesLocalizedPlanetText()
        {
            var gameplay = Spawn(LoadedStore().CreateNew());

            var texts = Object.FindObjectsByType<UnityEngine.UI.Text>(FindObjectsSortMode.None)
                .Select(text => text.text)
                .ToArray();

            Assert.Contains(Planet.earth.Text, texts);
            Assert.IsNotNull(gameplay);
        }

        [Test]
        public void ResetSave_DiscardsUnsavedVisit()
        {
            var gameplay = Spawn(LoadedStore().CreateNew());

            var destination = gameplay.Outposts.First(outpost =>
                outpost.valueId != gameplay.CurrentOutpost.valueId);
            destination.Save.Unlocked = true;
            gameplay.OnVisitOutpost(destination);
            Assert.AreEqual(HelloText(destination.Planet), gameplay.HelloWorldText);

            gameplay.ResetAsync().GetAwaiter().GetResult();

            Assert.AreEqual(HelloText(Planet.earth), gameplay.HelloWorldText);
            Assert.AreEqual(Planet.earth, gameplay.World);
            CollectionAssert.AreEqual(new[] { Planet.earth }, VisitedPlanets(gameplay));
        }

        [Test]
        public void Save_PersistsVisitAndReopensByCustomId()
        {
            // Create + play + save a brand-new save (dynamic customId, as the menu does).
            var synchronizer = LoadedStore().CreateNew();
            var customId = synchronizer.CustomId;
            var gameplay = Spawn(synchronizer);
            var destination = gameplay.Outposts.First(outpost =>
                outpost.valueId != gameplay.CurrentOutpost.valueId);
            destination.Save.Unlocked = true;
            gameplay.OnVisitOutpost(destination);
            gameplay.SaveAsync().GetAwaiter().GetResult();

            // Reopen that same save by its id from a fresh store, as the menu's
            // Continue does — the played state is restored.
            var reloaded = Spawn(LoadedStore().Open(customId));

            Assert.AreEqual(HelloText(destination.Planet), reloaded.HelloWorldText);
            Assert.AreEqual(destination.Planet, reloaded.World);
            Assert.AreEqual(destination.valueId, reloaded.CurrentOutpost.valueId);
            CollectionAssert.AreEqual(
                new[] { Planet.earth, destination.Planet },
                VisitedPlanets(reloaded));
        }

        [Test]
        public void VisitOutpost_IgnoresLockedOutpost()
        {
            var gameplay = Spawn(LoadedStore().CreateNew());

            var startingOutpost = gameplay.CurrentOutpost;
            var lockedDestination = gameplay.Outposts.First(outpost =>
                outpost.valueId != startingOutpost.valueId);
            lockedDestination.Save.Unlocked = false;
            var startingVisitCount = lockedDestination.Save.VisitCount;

            gameplay.OnVisitOutpost(lockedDestination);

            Assert.AreEqual(startingOutpost.valueId, gameplay.CurrentOutpost.valueId);
            Assert.AreEqual(Planet.earth, gameplay.World);
            Assert.AreEqual(startingVisitCount, lockedDestination.Save.VisitCount);
            CollectionAssert.AreEqual(new[] { Planet.earth }, VisitedPlanets(gameplay));
        }

        private static Planet[] VisitedPlanets(HelloWorldGameplay gameplay)
        {
            var planets = new List<Planet>();
            foreach (var visit in gameplay.VisitedPlanets)
            {
                planets.Add(visit.World);
            }
            return planets.ToArray();
        }

        private static string HelloText(Planet planet)
        {
            var greeting = HelloWorldNeo.Instance.Assets.Computed.baseText;
            return $"{greeting} {planet.Text}!";
        }
    }
}
