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
        public void FlareClock_TicksPerHop_WithOuterSystemSurcharge()
        {
            var gameplay = Spawn(LoadedStore().CreateNew());
            foreach (var outpost in gameplay.Outposts) outpost.Save.Unlocked = true;
            var inner = gameplay.Outposts.First(o => o.Planet == Planet.mars);
            var outer = gameplay.Outposts.First(o => o.Planet == Planet.neptune);

            gameplay.OnVisitOutpost(inner);
            Assert.AreEqual(1, QuestClock(gameplay), "inner hop costs 1");

            gameplay.OnVisitOutpost(outer);
            Assert.AreEqual(3, QuestClock(gameplay), "outer hop costs 2 without the Gyro Stabilizer");
        }

        [Test]
        public void FlareClock_GyroWaivesOuterSurcharge_ParasolShieldsFirstHops()
        {
            var gameplay = Spawn(LoadedStore().CreateNew());
            foreach (var outpost in gameplay.Outposts) outpost.Save.Unlocked = true;
            var outer = gameplay.Outposts.First(o => o.Planet == Planet.neptune);
            var inner = gameplay.Outposts.First(o => o.Planet == Planet.mars);

            GiveItem(gameplay, "Cloudsilk Parasol");
            gameplay.OnVisitOutpost(inner);
            Assert.AreEqual(0, QuestClock(gameplay), "the parasol shields the first hops entirely");
            gameplay.OnVisitOutpost(inner);
            Assert.AreEqual(0, QuestClock(gameplay));

            GiveItem(gameplay, "Gyro Stabilizer");
            ForceClock(gameplay, 5);
            gameplay.OnVisitOutpost(outer);
            Assert.AreEqual(6, QuestClock(gameplay), "the gyro waives the outer-system surcharge");
        }

        [Test]
        public void LoopEnding_ErasesTheSaveAndExitsToMenu()
        {
            var store = LoadedStore();
            var synchronizer = store.CreateNew();
            var customId = synchronizer.CustomId;
            var gameplay = Spawn(synchronizer);
            string erased = null;
            var exited = false;
            gameplay.OnEraseSave += id => erased = id;
            gameplay.OnExitToMenu += () => exited = true;

            SetQuest(gameplay, QuestStage.ended, WorldEnding.helloWorld);
            gameplay.OnDialogueFinish();

            Assert.AreEqual(customId, erased, "the Loop ending erases this save");
            Assert.IsTrue(exited);
        }

        [Test]
        public void OtherEndings_KeepTheSave()
        {
            var gameplay = Spawn(LoadedStore().CreateNew());
            string erased = null;
            gameplay.OnEraseSave += id => erased = id;

            SetQuest(gameplay, QuestStage.ended, WorldEnding.goodbyeWorld);
            gameplay.OnDialogueFinish();

            Assert.IsNull(erased, "only the Loop ending wipes the save");
        }

        private static int QuestClock(HelloWorldGameplay gameplay)
        {
            return GameplayNeo(gameplay).Save.Quest.FlareClock;
        }

        private static void ForceClock(HelloWorldGameplay gameplay, int value)
        {
            GameplayNeo(gameplay).Save.Quest.FlareClock = value;
        }

        private static void SetQuest(HelloWorldGameplay gameplay, QuestStage stage, WorldEnding ending)
        {
            var neo = GameplayNeo(gameplay);
            neo.Save.Quest.Stage = stage;
            neo.Save.Quest.Ending = ending;
        }

        private static void GiveItem(HelloWorldGameplay gameplay, string itemName)
        {
            var neo = GameplayNeo(gameplay);
            var item = neo.Assets.Items.First(candidate => candidate.Name == itemName);
            neo.Save.Inventory.Add(item);
        }

        [Test]
        public void AudioAssets_GroupedUnderAssetsAudio_ResolveSynchronizedClips()
        {
            // The whole audio pipeline in one assertion set: authored project
            // files -> Assets.Audio schema references -> synced Resources ->
            // generated AudioClip properties. A missing/unsynced clip throws.
            var gameplay = Spawn(LoadedStore().CreateNew());
            var audio = GameplayNeo(gameplay).Assets.Audio;

            Assert.IsNotNull(audio.DialogOpenSfx);
            Assert.IsNotNull(audio.DialogNextSfx);
            Assert.IsNotNull(audio.DialogCloseSfx);
            Assert.IsNotNull(audio.BitsGainSfx);
            Assert.IsNotNull(audio.BitsSpendSfx);
            Assert.IsNotNull(audio.ItemGetSfx);
            Assert.IsNotNull(audio.RocketThrustSfx);
            Assert.Greater(audio.RocketThrustSfx.length, 1f, "thrust loop should be the long clip");
        }

        [Test]
        public void ArtAssets_GroupedUnderAssetsArt_KeepAnimationsAndSprites()
        {
            // Regression for the Assets root cleanup: the sprites/animations
            // moved under Assets.Art and must still resolve their synced data.
            var gameplay = Spawn(LoadedStore().CreateNew());
            var art = GameplayNeo(gameplay).Assets.Art;

            Assert.IsNotNull(art.ShipAnimation);
            Assert.Greater(art.ShipAnimation.Frames.Count, 0);
            Assert.IsNotNull(art.FlareAnimation);
            Assert.Greater(art.FlareAnimation.Frames.Count, 0);
            Assert.IsNotNull(art.ShipSprite);
            Assert.IsNotNull(art.VaultPlaqueSprite);
        }

        private static HelloWorldNeo GameplayNeo(HelloWorldGameplay gameplay)
        {
            var field = typeof(HelloWorldGameplay).GetField(
                "neo",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (HelloWorldNeo)field.GetValue(gameplay);
        }

        [Test]
        public void EveryIntroDialogue_PlaysEveryFirstPathWithoutActionErrors()
        {
            // Field repro harness: walk each outpost's intro start-to-finish,
            // always choosing the FIRST selectable option, and fail on any
            // dialogue action error (the class of crash dryrun can't see).
            var gameplay = Spawn(LoadedStore().CreateNew());
            var neo = GameplayNeo(gameplay);
            foreach (var outpost in gameplay.Outposts) outpost.Save.Unlocked = true;

            foreach (var outpost in gameplay.Outposts)
            {
                if (!neo.Dialogues.Outposts.Introductions.TryTrigger(outpost, out NeoDialogue dialogue))
                {
                    continue;
                }
                System.Exception error = null;
                NeoDialogueTextNode current = null;
                dialogue.OnError += ex => error = ex;
                dialogue.OnShow += node => current = node;
                dialogue.Start();
                for (var step = 0; step < 60 && error == null; step++)
                {
                    if (current == null) break;
                    var node = current;
                    current = null;
                    if (node.Options.Count > 0)
                    {
                        var option = node.Options.FirstOrDefault(o => o.Selectable);
                        if (option == null) break;
                        option.Select();
                    }
                    else
                    {
                        node.Next();
                    }
                }
                Assert.IsNull(error, $"{outpost.Name}: {error}");
            }
        }

        [Test]
        public void QuestHint_EvaluatesAtEveryStage_AndNamesOutposts()
        {
            // Regression for the AssignInstruction crash: NextHint is a
            // push-compiled getter with local-variable reassignment, so it
            // must EVALUATE live at every stage — and per playtest feedback
            // it must name outposts, not unlabeled planets/moons.
            var gameplay = Spawn(LoadedStore().CreateNew());
            var quest = GameplayNeo(gameplay).Save.Quest;

            StringAssert.Contains("Capitol OG", quest.NextHint);

            quest.Stage = QuestStage.followTheWakes;
            StringAssert.Contains("Mercurial", quest.NextHint);
            StringAssert.Contains("Iowan", quest.NextHint);

            quest.Stage = QuestStage.threePaths;
            StringAssert.Contains("Ursa Major", quest.NextHint);
            quest.EvidenceArchive = true;
            StringAssert.Contains("Pour Lords", quest.NextHint);
            quest.EvidenceLedger = true;
            StringAssert.Contains("Capitol OG", quest.NextHint);

            quest.Stage = QuestStage.vaultOpen;
            StringAssert.Contains("Cave Lantern", quest.NextHint);

            quest.Stage = QuestStage.endgame;
            StringAssert.Contains("final output", quest.NextHint);

            quest.Stage = QuestStage.ended;
            StringAssert.Contains("ended", quest.NextHint);
        }

        [Test]
        public void StageTransitions_AreMonotonic_RegardlessOfVisitOrder()
        {
            // The stuck-save bug: Iowan's intro advances to threePaths, but
            // Mercurial's intro used to unconditionally reset the stage to
            // followTheWakes when visited afterwards. The guards must keep
            // progression forward-only in ANY visit order.
            var gameplay = Spawn(LoadedStore().CreateNew());
            var neo = GameplayNeo(gameplay);
            foreach (var outpost in gameplay.Outposts) outpost.Save.Unlocked = true;

            var iowan = gameplay.Outposts.First(o => o.Name == "Iowan");
            var mercurial = gameplay.Outposts.First(o => o.Name == "Mercurial");

            WalkIntro(neo, iowan);
            Assert.AreEqual(QuestStage.threePaths, neo.Save.Quest.Stage,
                "Iowan's intro advances arrival -> threePaths");

            WalkIntro(neo, mercurial);
            Assert.AreEqual(QuestStage.threePaths, neo.Save.Quest.Stage,
                "Mercurial's intro must never regress the stage");
        }

        [Test]
        public void FlareOverflow_RebootsWorld_KeepsCargo_AndColdBootGreets()
        {
            var gameplay = Spawn(LoadedStore().CreateNew());
            var neo = GameplayNeo(gameplay);
            foreach (var outpost in gameplay.Outposts) outpost.Save.Unlocked = true;

            // Earn some cargo first (Iowan's intro grants Storm Corn).
            var iowan = gameplay.Outposts.First(o => o.Name == "Iowan");
            WalkIntro(neo, iowan);
            var cargoBeforeCrash = neo.Save.Inventory.Count;
            Assert.Greater(cargoBeforeCrash, 0, "intro should have granted cargo");

            neo.Save.Quest.FlareClock = HelloWorldGameplay.FlareOverflowThreshold;
            gameplay.RebootAfterCrash();

            Assert.AreEqual(1, neo.Save.Quest.Reruns, "a crash is a rerun");
            Assert.AreEqual(0, neo.Save.Quest.FlareClock, "the reboot clears the clock");
            Assert.AreEqual(Planet.earth, gameplay.World, "reboots wake up at the Capitol");
            Assert.AreEqual(
                cargoBeforeCrash,
                neo.Save.Inventory.Count,
                "cargo impossibly persists across the reboot — that's the clue");
        }

        private static void WalkIntro(HelloWorldNeo neo, ReadOnlyOutpost outpost)
        {
            Assert.IsTrue(
                neo.Dialogues.Outposts.Introductions.TryTrigger(outpost, out NeoDialogue dialogue),
                $"{outpost.Name}: intro should trigger");
            System.Exception error = null;
            NeoDialogueTextNode current = null;
            dialogue.OnError += ex => error = ex;
            dialogue.OnShow += node => current = node;
            dialogue.Start();
            for (var step = 0; step < 60 && error == null; step++)
            {
                if (current == null) break;
                var node = current;
                current = null;
                if (node.Options.Count > 0)
                {
                    var option = node.Options.FirstOrDefault(o => o.Selectable);
                    if (option == null) break;
                    option.Select();
                }
                else
                {
                    node.Next();
                }
            }
            Assert.IsNull(error, $"{outpost.Name}: {error}");
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
