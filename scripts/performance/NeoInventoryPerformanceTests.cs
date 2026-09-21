using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Scripts.Neo;
using Assets.Scripts.Neo.Menu;
using Assets.Scripts.Neo.UI;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public class NeoInventoryPerformanceTests
{
    [UnityTest, Timeout(600000)]
    public IEnumerator RealWorldInventoryActions()
    {
        yield return new EnterPlayMode();
        string directory = Path.Combine(Path.GetTempPath(), "neo-inventory-perf-" + Guid.NewGuid());
        void Configure(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        { if (scene.name == "NeoMenu") Object.FindAnyObjectByType<NeoMenuFlow>().SaveDirectory = directory; }
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += Configure;
        yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Assets/Scenes/NeoMenu.unity");
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= Configure;
        var flow = Object.FindAnyObjectByType<NeoMenuFlow>();
        yield return Wait(flow, NeoMenuState.Main);
        flow.NewGame();
        yield return Wait(flow, NeoMenuState.World);
        var ui = Object.FindAnyObjectByType<WorldUIController>();
        var inventory = flow.Client.Save.Player.Inventory;
        var wood = flow.Client.Assets.Items.Index.Slug["wood"];
        var axe = flow.Client.Assets.Items.Index.Slug["axe.stone"];
        var rows = new List<string>();
        for (int trial = 0; trial < 3; trial++)
        {
            yield return Measure("menu.open", trial, ui.OpenMainMenu, rows);
            Assert.That(ui.GetComponentsInChildren<InventoryPanel>().Length, Is.EqualTo(1));
            foreach (var tab in new[] { "Friends", "Crafting", "Dev Tools", "Inventory" })
                yield return Measure("menu.tab." + tab, trial, () => SelectTab(ui, tab), rows);
            yield return Measure("dev.add.wood.menu", trial, () => ui.Commands.RunCommand("add wood 1"), rows);
            ui.Close(); yield return null; yield return null;
            yield return Measure("dev.add.wood.world", trial, () => ui.Commands.RunCommand("add wood 1"), rows);
            yield return Measure("inventory.add.wood", trial,
                () => Assert.That(inventory.AddStack(new InventoryItemStack(wood, 1)), Is.Zero), rows);
            if (trial == 0)
                yield return Measure("inventory.add.tool", trial,
                    () => Assert.That(inventory.AddStack(new InventoryItemStack(axe, 1)), Is.Zero), rows);
            int toolIndex = Enumerable.Range(0, inventory.Stacks.Count).Single(i => inventory.Stacks[i]?.Item.Slug == "axe.stone");
            yield return Measure("equip.tool", trial, () => inventory.SetEquippedItemIndex(toolIndex), rows);
            Assert.That(inventory.EquippedItemStack.Item.Slug, Is.EqualTo("axe.stone"));
            yield return Measure("equip.wood", trial, () => inventory.SetEquippedItemIndex(0), rows);
            Assert.That(inventory.EquippedItemStack.Item.Slug, Is.EqualTo("wood"));
        }
        Assert.That(inventory.Stacks[0].Quantity.Value, Is.EqualTo(9));
        var firstButton = ui.GetComponentInChildren<InventoryToolbar>()
            .GetComponentsInChildren<InventoryToolbarButton>().Single(b => b.name == "InventoryToolbarButton-0");
        Assert.That(firstButton.GetComponentsInChildren<TMPro.TMP_Text>(true)
            .Single(t => t.name == "StackCountText").text, Is.EqualTo("9"));
        string report = Environment.GetEnvironmentVariable("NEO_INVENTORY_PERF_REPORT")
            ?? Path.Combine(Path.GetTempPath(), "neo-inventory-performance.csv");
        File.WriteAllLines(report, new[] { "action,trial,actionMs,allocatedBytes,maxFrameMs" }.Concat(rows));
        Debug.Log("NEO_PERF_REPORT " + report);
        flow.ReturnToMenu(); yield return Wait(flow, NeoMenuState.Main);
        yield return new ExitPlayMode();
        Directory.Delete(directory, true);
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator OriginalWorldEquipmentActions()
    {
        yield return new EnterPlayMode();
        Assets.Scripts.Saves.SaveSystem.EnableTestMode();
        try
        {
            Assets.Scripts.Saves.SaveSystem.Clear();
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("WorldScene");
            double deadline = Time.realtimeSinceStartupAsDouble + 30;
            while (GameController.Instance?.Player == null && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
            Assert.That(GameController.Instance?.Player, Is.Not.Null);
            Assets.Scripts.DevTools.DevTools.RunCommand("add wood 1");
            Assets.Scripts.DevTools.DevTools.RunCommand("add axe.stone 1");
            var inventory = Assets.Scripts.Saves.SaveSystem.Data.Player.Inventory;
            int toolIndex = inventory.Stacks.FindIndex(s => s?.Item.Id == "axe.stone");
            int woodIndex = inventory.Stacks.FindIndex(s => s?.Item.Id == "wood");
            Assert.That(toolIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(woodIndex, Is.GreaterThanOrEqualTo(0));
            var rows = new List<string>();
            for (int trial = 0; trial < 3; trial++)
            {
                yield return Measure("equip.tool", trial, () => inventory.SetEquippedItemIndex(toolIndex), rows);
                yield return Measure("equip.wood", trial, () => inventory.SetEquippedItemIndex(woodIndex), rows);
            }
            File.WriteAllLines(Path.Combine(Path.GetTempPath(), "original-inventory-performance.csv"),
                new[] { "action,trial,actionMs,allocatedBytes,maxFrameMs" }.Concat(rows));
        }
        finally { Assets.Scripts.Saves.SaveSystem.DisableTestMode(); }
        yield return new ExitPlayMode();
    }

    [UnityTest, Timeout(600000)]
    public IEnumerator EquipmentVisualsStayLiveAcrossToolRodAndEmptyHands()
    {
        yield return new EnterPlayMode();
        string directory = Path.Combine(Path.GetTempPath(), "neo-inventory-perf-" + Guid.NewGuid());
        void Configure(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        { if (scene.name == "NeoMenu") Object.FindAnyObjectByType<NeoMenuFlow>().SaveDirectory = directory; }
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += Configure;
        yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Assets/Scenes/NeoMenu.unity");
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= Configure;
        var flow = Object.FindAnyObjectByType<NeoMenuFlow>();
        yield return Wait(flow, NeoMenuState.Main);
        flow.NewGame();
        yield return Wait(flow, NeoMenuState.World);
        var ui = Object.FindAnyObjectByType<WorldUIController>();
        var inventory = flow.Client.Save.Player.Inventory;
        var wood = flow.Client.Assets.Items.Index.Slug["wood"];
        var axe = flow.Client.Assets.Items.Index.Slug["axe.stone"];
        var body = Object.FindObjectsByType<Assets.Scripts.Neo.CharacterCreator.BodyAnimator>()
            .Single(b => b.Config.Slug == "character.player");
        body.GetComponent<Assets.Scripts.Neo.CharacterCreator.PlayerController>().enabled = false;
        body.HandleDirectionUpdate(Vector2.up);
        var attack = body.Body.Children.OfType<AttackItemSprite>().Single();
        var rod = body.Body.Children.OfType<FishingRodSprite>().Single();
        var attackRenderer = body.transform.Find("AttackItem").GetComponent<SpriteRenderer>();
        var rodRenderer = body.transform.Find("FishingRod").GetComponent<SpriteRenderer>();
        var torso = body.transform.Find("Torso/Torso").GetComponent<SpriteRenderer>();
        inventory.AddStack(new InventoryItemStack(wood, 1));
        var tools = flow.Client.Assets.Items.Where(i => i is IHasHeldItem h && h.Asset is IReadOnlyAttackItemAsset).ToArray();
        var fishing = flow.Client.Assets.Items.First(i => i is IHasHeldItem h && h.Asset is IReadOnlyFishingRodAsset);
        Assert.That(tools.Any(item => item.Slug == "axe.stone"), Is.True);
        foreach (var item in tools.Concat(new[] { fishing })) inventory.AddStack(new InventoryItemStack(item, 1));
        foreach (var item in tools.Concat(new[] { fishing }).Concat(tools.Reverse()))
        {
            int index = Enumerable.Range(0, inventory.Stacks.Count).Single(i => inventory.Stacks[i]?.Item.Slug == item.Slug);
            inventory.SetEquippedItemIndex(index);
            // The EditMode runner does not schedule WaitForSeconds while in PlayMode.
            // Wait explicitly for two 8 fps animation ticks.
            float deadline = Time.time + .26f;
            while (Time.time < deadline) yield return null;
            var asset = ((IHasHeldItem)item).Asset;
            Assert.That(body.Config.HeldItem?.valueId, Is.EqualTo(asset.valueId), "Player config must follow inventory.");
            Assert.That(attack.Config.HeldItem?.valueId, Is.EqualTo(asset.valueId), "Retained attack layer must use the live config.");
            bool attacking = asset is IReadOnlyAttackItemAsset;
            if (attacking) Assert.That(((IReadOnlyAttackItemSprite)attack).ToolArt.valueId, Is.EqualTo(((IReadOnlyAttackItemAsset)asset).Item.valueId), "ToolArt getter must follow config.");
            var frames = attacking ? ((IReadOnlyAttackItemAsset)asset).Item.Idle.Up.Frames
                : ((IReadOnlyFishingRodAsset)asset).Item.Idle.Up.Frames;
            Assert.That((attacking ? (BodyLayerSprite)attack : rod).FlipX, Is.False, "Idle up model flip: " + item.Slug);
            Assert.That((attacking ? attackRenderer : rodRenderer).flipX, Is.False, "Idle up renderer flip: " + item.Slug);
            foreach (var direction in new[] { Vector2.down, Vector2.left, Vector2.up, Vector2.right, Vector2.up })
            {
                body.HandleDirectionUpdate(direction);
                for (float ready = Time.time + .4f; Time.time < ready;) yield return null;
                bool expectedFlip = direction == Vector2.left;
                Assert.That((attacking ? (BodyLayerSprite)attack : rod).FlipX, Is.EqualTo(expectedFlip), "Idle turn model " + direction + ": " + item.Slug);
                Assert.That((attacking ? attackRenderer : rodRenderer).flipX, Is.EqualTo(expectedFlip), "Idle turn renderer " + direction + ": " + item.Slug);
            }
            foreach (bool moving in new[] { true, false })
            {
                body.SetIsMoving(moving);
                for (float ready = Time.time + .4f; Time.time < ready;) yield return null;
                Assert.That((attacking ? (BodyLayerSprite)attack : rod).FlipX, Is.EqualTo(moving), "Up model after moving=" + moving + ": " + item.Slug);
                Assert.That((attacking ? attackRenderer : rodRenderer).flipX, Is.EqualTo(moving), "Up renderer after moving=" + moving + ": " + item.Slug);
            }
            Assert.That(attack.Enabled, Is.EqualTo(attacking));
            Assert.That(rod.Enabled, Is.EqualTo(!attacking));
            Assert.That(frames.Select(frame => frame.Value), Does.Contain((attacking ? attackRenderer : rodRenderer).sprite), item.Slug);
            Assert.That(body.transform.Find("Torso/Torso").GetComponent<SpriteRenderer>(), Is.SameAs(torso));
            Assert.That(body.transform.Find("AttackItem").GetComponent<SpriteRenderer>(), Is.SameAs(attackRenderer));
            inventory.SetEquippedItemIndex(0);
            yield return null; yield return null;
            Assert.That(attack.Enabled, Is.False);
            Assert.That(rod.Enabled, Is.False);
        }
        flow.ReturnToMenu(); yield return Wait(flow, NeoMenuState.Main);
        yield return new ExitPlayMode();
        Directory.Delete(directory, true);
    }

    private static void SelectTab(WorldUIController ui, string name)
        => ui.GetComponentsInChildren<Button>().Single(b => b.name.StartsWith("TabButton-") &&
            b.GetComponentsInChildren<TMPro.TMP_Text>().Any(t => t.text == name)).onClick.Invoke();

    private static IEnumerator Measure(string name, int trial, Action action, List<string> rows)
    {
        yield return null;
        using var recorder = new ProfilerRecorder(ProfilerCategory.Memory, "GC Allocated In Frame", 1,
            ProfilerRecorderOptions.WrapAroundWhenCapacityReached | ProfilerRecorderOptions.SumAllSamplesInFrame);
        Assert.That(recorder.Valid, Is.True);
        recorder.Start();
        long before = recorder.CurrentValue;
        double start = Time.realtimeSinceStartupAsDouble;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using (new ProfilerMarker("NeoInventory." + name).Auto()) action();
        watch.Stop();
        long allocated = recorder.CurrentValue - before;
        recorder.Stop();
        double previous = start, maximum = 0;
        for (int frame = 0; frame < 3; frame++)
        {
            yield return null;
            double now = Time.realtimeSinceStartupAsDouble;
            maximum = Math.Max(maximum, (now - previous) * 1000);
            previous = now;
        }
        string row = FormattableString.Invariant($"{name},{trial},{watch.Elapsed.TotalMilliseconds:F3},{allocated},{maximum:F3}");
        rows.Add(row);
        Debug.Log("NEO_PERF " + row);
    }

    private static IEnumerator Wait(NeoMenuFlow flow, NeoMenuState state)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 120;
        while (flow.State != state && Time.realtimeSinceStartupAsDouble < deadline)
        { Assert.That(flow.State, Is.Not.EqualTo(NeoMenuState.Error), flow.Message); yield return null; }
        Assert.That(flow.State, Is.EqualTo(state), flow.Message);
    }
}
