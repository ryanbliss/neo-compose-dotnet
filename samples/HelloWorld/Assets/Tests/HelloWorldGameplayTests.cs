// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HelloWorld.Assets.Scripts;
using HelloWorld.Assets.Scripts.Neo;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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

        private static readonly string SampleProjectSourceJson =
            File.ReadAllText(SampleProjectJson);

        private string saveDirectory;
        private readonly List<GameObject> spawned = new();
        private readonly List<HelloWorldNeo> clients = new();
        private readonly List<NeoProjectStore> stores = new();

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
            foreach (var client in clients) client.Dispose();
            clients.Clear();
            foreach (var store in stores) store.Dispose();
            stores.Clear();
            if (Directory.Exists(saveDirectory)) Directory.Delete(saveDirectory, recursive: true);
        }

        /// <summary>A loaded local store over this test's temp save folder.</summary>
        private NeoProjectStore LoadedStore()
        {
            var store = new NeoProjectStore(
                dataSource: new NeoJsonProjectDataSource(SampleProjectSourceJson),
                localStore: new NeoFileLocalSaveStore(saveDirectory));
            store.LoadAsync().GetAwaiter().GetResult();
            stores.Add(store);
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

        /// <summary>Loads the generated client without constructing the sample UI.</summary>
        private HelloWorldNeo LoadedClient()
        {
            var client = HelloWorldNeo.Load(LoadedStore().CreateNew())
                .GetAwaiter()
                .GetResult();
            clients.Add(client);
            return client;
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

        [UnityTest]
        public IEnumerator OldConsoleLanding_EasterEggOpensGenerated2DWorldScene()
        {
            var gameplay = Spawn(LoadedStore().CreateNew());

            Assert.IsFalse(gameplay.OldConsoleLandingOpen);

            gameplay.OpenOldConsoleLanding();

            Assert.IsTrue(gameplay.OldConsoleLandingOpen);
            NeoTileGridRenderer renderer = null;
            for (int frame = 0; frame < 120; frame += 1)
            {
                renderer = Object.FindFirstObjectByType<NeoTileGridRenderer>();
                if (renderer != null
                    && renderer.TryGetObjectRoot<PlayerSpawnObject>(out _, out _))
                {
                    break;
                }
                yield return null;
            }

            Assert.IsNotNull(renderer);
            Assert.IsTrue(
                renderer.TryGetObjectRoot<PlayerSpawnObject>(out var playerRoot, out _),
                "The SDK renders the player spawn object under the object layer; gameplay moves it by writing its Session-storage Position.");
            Assert.IsNotNull(playerRoot);

            gameplay.CloseOldConsoleLanding();

            Assert.IsFalse(gameplay.OldConsoleLandingOpen);
        }

        [UnityTest]
        public IEnumerator OldConsoleLanding_BarrierClearUpdatesGameplayCacheFromTileDelta()
        {
            var gameplay = Spawn(LoadedStore().CreateNew());
            var neo = GameplayNeo(gameplay);
            LandingSceneGameplay landing = null;

            try
            {
                landing = LandingSceneGameplay.Open(neo, new TestLandingHost());

                yield return WaitForLandingSceneLoad(landing);

                var content = neo.Assets.Worlds.OldConsoleLanding.Content;
                var blocked = neo.Assets.Worlds.OldConsoleLanding.GetChild<BlockedPath>();
                Assert.IsNotNull(blocked);

                // The link's own grid-space query matches what it projects
                // onto the Collisions layer.
                var blockerCells = blocked!.GetTiles()
                    .Select(tile => tile.Cell)
                    .ToArray();
                Assert.Greater(blockerCells.Length, 0);
                foreach (var cell in blockerCells)
                {
                    Assert.AreEqual(
                        blocked.valueId,
                        content.Collisions.GetTile(cell)?.SourceTileLayerLinkId);
                }

                SetLandingPlayerCell(
                    landing,
                    FindWalkableNeighbor(content, blockerCells));
                InvokeLandingUpdatePrompt(landing);
                StringAssert.Contains("vault seal", landing.PromptText);

                blocked.Tiles.Clear();
                yield return null;

                Assert.IsFalse(
                    landing.PromptText.Contains("vault seal"),
                    "Clearing the model tiles should update the gameplay barrier cache through the collision delta.");
                foreach (var cell in blockerCells)
                {
                    Assert.IsNull(content.Collisions.GetTile(cell));
                    Assert.IsNull(blocked.GetTile(cell));
                }
            }
            finally
            {
                if (landing != null) Object.DestroyImmediate(landing.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator OldConsoleLanding_InteractWithBootGlyphDoesNotLoopTileLookup()
        {
            var gameplay = Spawn(LoadedStore().CreateNew());
            var neo = GameplayNeo(gameplay);
            LandingSceneGameplay landing = null;
            var triggered = false;

            try
            {
                landing = LandingSceneGameplay.Open(neo, new TestLandingHost
                {
                    OnTriggerDialogue = (_, onFinish) =>
                    {
                        triggered = true;
                        for (int tick = 0; tick < 8; tick += 1)
                        {
                            neo.Save.Quest.FlareClock += 1;
                        }
                        onFinish?.Invoke();
                        return true;
                    },
                });

                yield return WaitForLandingSceneLoad(landing);

                var content = neo.Assets.Worlds.OldConsoleLanding.Content;
                var bootGlyphCell = content.Background.GetTiles()
                    .First(tile => tile.Info is BootGlyphTile)
                    .Cell;
                SetLandingPlayerCell(landing, bootGlyphCell);
                InvokeLandingUpdatePrompt(landing);
                StringAssert.Contains("boot glyph", landing.PromptText);

                InvokeLandingInteract(landing);
                yield return null;

                Assert.IsTrue(triggered);
                Assert.IsInstanceOf<BootGlyphTile>(
                    content.Background.GetTile(bootGlyphCell)?.Info);
            }
            finally
            {
                if (landing != null) Object.DestroyImmediate(landing.gameObject);
            }
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
            var audio = LoadedClient().Assets.Audio;

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
            var art = LoadedClient().Assets.Art;

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

        private sealed class TestLandingHost : ILandingSceneHost
        {
            public System.Func<NeoDialogueReference, System.Action, bool> OnTriggerDialogue { get; set; } =
                (_, __) => false;

            public bool DialogueIsOpen => false;

            public void CloseLandingScene()
            {
            }

            public async Awaitable SaveProgressAsync()
            {
                await Awaitable.NextFrameAsync();
            }

            public bool TryTriggerDialogue(NeoDialogueReference reference, System.Action onFinish)
            {
                return OnTriggerDialogue(reference, onFinish);
            }
        }

        private static IEnumerator WaitForLandingSceneLoad(LandingSceneGameplay landing)
        {
            for (var frame = 0; frame < 120; frame += 1)
            {
                if (landing.StatusText.StartsWith("WASD moves."))
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("Landing scene did not finish loading.");
        }

        private static Vector2Int FindWalkableNeighbor(
            ReadOnlyOldConsoleLandingGridContent content,
            IReadOnlyCollection<Vector2Int> targetCells)
        {
            var collisionCells = new HashSet<Vector2Int>(
                content.Collisions.GetTiles().Select(tile => tile.Cell));

            foreach (var cell in content.Background.GetTiles().Select(tile => tile.Cell))
            {
                if (collisionCells.Contains(cell)) continue;
                if (targetCells.Any(target => Mathf.Abs(target.x - cell.x) + Mathf.Abs(target.y - cell.y) <= 1))
                {
                    return cell;
                }
            }

            Assert.Fail("No walkable cell was adjacent to the blocked path.");
            return default;
        }

        private static void SetLandingPlayerCell(LandingSceneGameplay landing, Vector2Int cell)
        {
            var setter = typeof(LandingSceneGameplay)
                .GetProperty(nameof(LandingSceneGameplay.PlayerCell))
                .GetSetMethod(nonPublic: true);
            setter.Invoke(landing, new object[] { cell });
        }

        private static void InvokeLandingUpdatePrompt(LandingSceneGameplay landing)
        {
            var method = typeof(LandingSceneGameplay).GetMethod(
                "UpdatePrompt",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method.Invoke(landing, null);
        }

        private static void InvokeLandingInteract(LandingSceneGameplay landing)
        {
            var method = typeof(LandingSceneGameplay).GetMethod(
                "Interact",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            method.Invoke(landing, null);
        }

        [Test]
        public void EveryIntroDialogue_PlaysEveryFirstPathWithoutActionErrors()
        {
            // Field repro harness: walk each outpost's intro start-to-finish,
            // always choosing the FIRST selectable option, and fail on any
            // dialogue action error (the class of crash dryrun can't see).
            var neo = LoadedClient();
            foreach (var outpost in neo.Assets.Outposts) outpost.Save.Unlocked = true;
            var triggeredCount = 0;

            foreach (var outpost in neo.Assets.Outposts)
            {
                if (!neo.Dialogues.Outposts.Introductions.TryTrigger(outpost, out NeoDialogue dialogue))
                {
                    continue;
                }
                triggeredCount += 1;
                WalkDialogue(dialogue, outpost.Name, preferFirstOption: true);
            }
            Assert.Greater(triggeredCount, 0, "The sample should expose at least one intro dialogue.");
        }

        [Test]
        public void QuestHint_EvaluatesAtEveryStage_AndNamesOutposts()
        {
            // Regression for the AssignInstruction crash: NextHint is a
            // push-compiled getter with local-variable reassignment, so it
            // must EVALUATE live at every stage — and per playtest feedback
            // it must name outposts, not unlabeled planets/moons.
            var quest = LoadedClient().Save.Quest;

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
            StringAssert.Contains("Abyssal Lantern", quest.NextHint);

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

        [Test]
        public void EveryQuestDialogue_PlaysEveryPathWithoutActionErrors()
        {
            // The act-2/3 dialogues only fire with the right stage, items,
            // reputation, and bits — dryrun can't reach them from a fresh
            // save, so this is where their actions (stage self-heals, item
            // gives, evidence writes) get exercised for real. Each entry is
            // walked twice: preferring the FIRST selectable option (the
            // "give" path) and the LAST (the "decline" path).
            // Quest dialogues are once-per-save (occurrenceLimit 1), so each
            // preference pass plays on a FRESH save.
            foreach (var preferFirst in new[] { true, false })
            {
                // This fixture intentionally uses the gameplay host: entering a
                // save performs the sample's initial dialogue/memory setup before
                // later quest starts are exercised.
                var gameplay = Spawn(LoadedStore().CreateNew());
                var neo = GameplayNeo(gameplay);
                foreach (var outpost in gameplay.Outposts) outpost.Save.Unlocked = true;
                neo.Save.Bits = 900;

                var scenarios = new (string outpost, string expectFlag, System.Action setup)[]
                {
                    ("Ursa Major", "archive", () => { }),
                    ("Pour Lords", "ledger", () =>
                    {
                        GrantItem(neo, "Helium-3 Flask");
                        OutpostByName(neo, "Pour Lords").Save.Reputation = 2;
                    }),
                    ("Caelus Anchorpoint", "ledger", () => GrantItem(neo, "Smuggler's Manifest")),
                    ("Mercurial", "faith", () => GrantItem(neo, "Cryo Salve")),
                    ("Venusian", "faith", () => { }),
                };
                foreach (var (outpostName, flag, setup) in scenarios)
                {
                    neo.Save.Quest.Stage = QuestStage.threePaths;
                    neo.Save.Quest.EvidenceArchive = flag != "archive";
                    neo.Save.Quest.EvidenceLedger = flag != "ledger";
                    neo.Save.Quest.EvidenceFaith = flag != "faith";
                    setup();
                    WalkVisit(neo, OutpostByName(neo, outpostName), preferFirst);
                    bool flagSet = flag == "archive"
                        ? neo.Save.Quest.EvidenceArchive
                        : flag == "ledger" ? neo.Save.Quest.EvidenceLedger : neo.Save.Quest.EvidenceFaith;
                    Assert.IsTrue(flagSet, $"{outpostName} should set its evidence flag (preferFirst={preferFirst})");
                }

                // Act 3 at the Capitol: greeter -> vault (lantern) -> finale.
                var capitol = OutpostByName(neo, "Capitol OG");
                neo.Save.Quest.Stage = QuestStage.threePaths;
                WalkVisit(neo, capitol, preferFirstOption: true);
                Assert.AreEqual(QuestStage.vaultOpen, neo.Save.Quest.Stage, "the greeter opens the vault");

                GrantItem(neo, "Abyssal Lantern");
                WalkVisit(neo, capitol, preferFirstOption: true);
                Assert.AreEqual(QuestStage.endgame, neo.Save.Quest.Stage, "the console reaches the endgame");
                Assert.IsFalse(HasItemNamed(neo, "Abyssal Lantern"), "the lantern burns out below");

                WalkVisit(neo, capitol, preferFirstOption: true);
                Assert.AreEqual(QuestStage.ended, neo.Save.Quest.Stage, "the finale ends the run");
            }

            // Self-heal: chain starts must pull a followTheWakes save forward.
            var healNeo = LoadedClient();
            foreach (var outpost in healNeo.Assets.Outposts) outpost.Save.Unlocked = true;
            healNeo.Save.Quest.Stage = QuestStage.followTheWakes;
            healNeo.Save.Quest.EvidenceLedger = true;
            healNeo.Save.Quest.EvidenceFaith = true;
            WalkVisit(healNeo, OutpostByName(healNeo, "Ursa Major"), preferFirstOption: true);
            Assert.AreEqual(QuestStage.threePaths, healNeo.Save.Quest.Stage, "evidence scenes self-heal the stage");
        }

        private static void GrantItem(HelloWorldNeo neo, string itemName)
        {
            if (HasItemNamed(neo, itemName)) return;
            neo.Save.Inventory.Add(neo.Assets.Items.First(item => item.Name == itemName));
        }

        private static bool HasItemNamed(HelloWorldNeo neo, string itemName)
        {
            return neo.Save.Inventory.Any(item => item.Name == itemName);
        }

        private static IReadOnlyOutpost OutpostByName(HelloWorldGameplay gameplay, string name)
        {
            return gameplay.Outposts.First(o => o.Name == name);
        }

        private static IReadOnlyOutpost OutpostByName(HelloWorldNeo neo, string name)
        {
            return neo.Assets.Outposts.First(o => o.Name == name);
        }

        /// <summary>Walks a Visits-group dialogue start to finish, failing on any action error.</summary>
        private static void WalkVisit(HelloWorldNeo neo, IReadOnlyOutpost outpost, bool preferFirstOption)
        {
            // These scenarios exercise the Visits group. Intro actions and their
            // real first-landing flow have dedicated exhaustive tests above, so seed
            // the return-trip precondition instead of replaying every intro here.
            if (outpost.Save.VisitCount == 0) outpost.Save.VisitCount = 1;
            bool triggered = neo.Dialogues.Outposts.Visits.TryTrigger(
                outpost, out NeoDialogueTriggerResult result);
            if (!triggered)
            {
                var detail = result.Error?.ToString() ?? "(no error)";
                foreach (var warning in result.Warnings) detail += $" | {warning.Message}";
                Assert.Fail(
                    $"{outpost.Name}: a visit dialogue should trigger " +
                    $"(preferFirst={preferFirstOption}, valueId={outpost.valueId}, " +
                    $"visitCount={outpost.Save.VisitCount}, stage={neo.Save.Quest.Stage}, " +
                    $"archive={neo.Save.Quest.EvidenceArchive}, " +
                    $"ledger={neo.Save.Quest.EvidenceLedger}, " +
                    $"faith={neo.Save.Quest.EvidenceFaith}) — {detail}");
            }
            WalkDialogue(result.Dialogue, outpost.Name, preferFirstOption);
        }

        private static void WalkDialogue(NeoDialogue dialogue, string label, bool preferFirstOption)
        {
            System.Exception error = null;
            NeoDialogueTextNode current = null;
            var finished = false;
            dialogue.OnError += ex => error = ex;
            dialogue.OnShow += node => current = node;
            dialogue.OnPause += pause => pause.Resume();
            dialogue.OnFinish += () => finished = true;
            try
            {
                dialogue.Start();
                for (var step = 0; step < 80 && error == null && !finished; step++)
                {
                    Assert.IsNotNull(current, $"{label}: dialogue stalled before finishing");
                    var node = current;
                    current = null;
                    if (node.Options.Count > 0)
                    {
                        var selectable = node.Options.Where(o => o.Selectable).ToArray();
                        Assert.IsNotEmpty(selectable, $"{label}: node has no selectable option");
                        (preferFirstOption ? selectable.First() : selectable.Last()).Select();
                    }
                    else
                    {
                        node.Next();
                    }
                }
                Assert.IsNull(error, $"{label}: {error}");
                Assert.IsTrue(finished, $"{label}: dialogue exceeded the 80-step limit");
            }
            finally
            {
                dialogue.Dispose();
            }
        }

        private static void WalkIntro(HelloWorldNeo neo, IReadOnlyOutpost outpost)
        {
            Assert.IsTrue(
                neo.Dialogues.Outposts.Introductions.TryTrigger(outpost, out NeoDialogue dialogue),
                $"{outpost.Name}: intro should trigger");
            System.Exception error = null;
            NeoDialogueTextNode current = null;
            var finished = false;
            dialogue.OnError += ex => error = ex;
            dialogue.OnShow += node => current = node;
            dialogue.OnPause += pause => pause.Resume();
            dialogue.OnFinish += () => finished = true;
            try
            {
                dialogue.Start();
                for (var step = 0; step < 60 && error == null && !finished; step++)
                {
                    Assert.IsNotNull(current, $"{outpost.Name}: dialogue stalled before finishing");
                    var node = current;
                    current = null;
                    if (node.Options.Count > 0)
                    {
                        var option = node.Options.FirstOrDefault(o => o.Selectable);
                        Assert.IsNotNull(option, $"{outpost.Name}: node has no selectable option");
                        option.Select();
                    }
                    else
                    {
                        node.Next();
                    }
                }
                Assert.IsNull(error, $"{outpost.Name}: {error}");
                Assert.IsTrue(finished, $"{outpost.Name}: dialogue exceeded the 60-step limit");
            }
            finally
            {
                dialogue.Dispose();
            }
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
