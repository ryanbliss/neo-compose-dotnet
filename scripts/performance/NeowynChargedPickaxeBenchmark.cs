// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.
#if NEO_COMPOSE_NEOWYN_TESTS
using System;
using System.Collections;
using System.IO;
using System.Linq;
using Assets.Scripts;
using Assets.Scripts.Menu;
using Assets.Scripts.Neo;
using Assets.Scripts.WorldSandbox;
using NeoCompose.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class NeowynChargedPickaxeBenchmark
{
    private FixedStepTestClock simulation;
    private string directory;
    private readonly System.Collections.Generic.List<string> errors = new();
    private void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            return;
        // The densely filled test pattern blocks Brett's authored mine waypoint.
        if (message.StartsWith("[NPCController] Invalid schedule for NPC 'Brett'") && message.Contains("waypoint is not on the NavMesh"))
            return;
        errors.Add(message + "\n" + stack);
    }
    [UnityTest, Timeout(600000)]
    public IEnumerator ChargedMineSwing()
    {
        Application.logMessageReceived += OnLog;
        LogAssert.ignoreFailingMessages = true;
        directory = Path.Combine(Path.GetTempPath(), "neo-pickaxe-" + Guid.NewGuid());
        yield return TestRunEnvironment.EnterPlayMode();
        simulation = new FixedStepTestClock(1f / 60f);
        void Configure(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (scene.name == "Menu")
                TestProjectSource.ConfigureMenu(Object.FindAnyObjectByType<MenuFlow>(), directory);
        }
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += Configure;
        try
        {
            yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Assets/Scenes/Menu.unity");
        }
        finally
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= Configure;
        }
        var flow = Object.FindAnyObjectByType<MenuFlow>();
        yield return Wait(flow, MainMenuState.Main);
        flow.NewGame();
        yield return Wait(flow, MainMenuState.World);
        var neo = flow.Client;
        var placement = GameController.Instance.Placement;
        var door = placement.Content.ForegroundPrimaryObjects.GetObjects<PlacedLocationItem>()
            .Select(item => item.GetFirstDoor()).First(item => item?.TargetArea?.Slug == "area.TestMine");
        Assert.That(door.TryInteract(), Is.True);
        double deadline = Time.realtimeSinceStartupAsDouble + 90;
        while ((GameController.Instance.State != GameState.FreeRoam || neo.Save.World.CurrentArea.Slug != "area.TestMine") && Time.realtimeSinceStartupAsDouble < deadline)
            yield return null;
        Assert.That(neo.Save.World.CurrentArea.Slug, Is.EqualTo("area.TestMine"));
        Assert.That(GameController.Instance.State, Is.EqualTo(GameState.FreeRoam));
        placement = GameController.Instance.Placement;
        var inventory = neo.Save.Player.Inventory;
        neo.RunTransaction(() =>
        {
            for (int i = 0; i < inventory.Stacks.Count; i++)
                inventory.Stacks[i] = null;
        });
        inventory.AddStack(new InventoryItemStack(neo.Assets.Items.Index.Slug["pickaxe.Wyn"], 1));
        inventory.SetEquippedItemIndex(0);
        ((PatternStackData)inventory.Stacks[0].Data).PatternIndex = 4;
        GameController.Instance.Player.Body.Config.Facing = Facing.Up;
        var target = new Vector2Int(0, 0);
        var tool = (GridToolValidator)placement.Validator;
        var cells = tool.Cells(target).ToArray();
        Assert.That(cells.Length, Is.EqualTo(35));
        foreach (var cell in cells)
        {
            foreach (var old in placement.Content.ForegroundPrimaryObjects.GetObjects<PlacedItem>(cell).ToArray())
                old.Despawn(placement.Content);
            var rock = DestructablePlacedItem.Variants.Vein.Iron.Full.Initialize();
            Assert.That(placement.Content.ForegroundPrimaryObjects.TrySpawn(cell - rock.Anchor, rock).Ok, Is.True);
        }
        flow.ReturnToMenu();
        yield return Wait(flow, MainMenuState.Main);
        flow.Continue();
        yield return Wait(flow, MainMenuState.World);
        neo = flow.Client;
        placement = GameController.Instance.Placement;
        inventory = neo.Save.Player.Inventory;
        inventory.SetEquippedItemIndex(0);
        ((PatternStackData)inventory.Stacks[0].Data).PatternIndex = 4;
        GameController.Instance.Player.Body.Config.Facing = Facing.Up;
        for (int i = 0; i < 3; i++)
            yield return null;
        tool = (GridToolValidator)placement.Validator;
        var rocks = cells.Select(cell => placement.Content.ForegroundPrimaryObjects.GetObject<DestructablePlacedItem>(cell)).ToArray();
        Assert.That(rocks.All(rock => rock != null), Is.True);
        var sprites = rocks.Select(rock => rock.OfType<AnimationSpriteObject>().First().Sprite.Value).ToArray();
        UnityEngine.Random.InitState(314159);
        neo.Client.Random.Seed = 314159;
        for (int swing = 0; swing < 2; swing++)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            tool.Use(target);
            timer.Stop();
            TestContext.WriteLine($"PICKAXE_SCENE swing={swing} count={cells.Length} ms={timer.Elapsed.TotalMilliseconds:F3}");
            if (swing == 0)
            {
                for (int i = 0; i < rocks.Length; i++)
                {
                    Assert.That(rocks[i].Health, Is.GreaterThan(0));
                    Assert.That(rocks[i].OfType<AnimationSpriteObject>().First().Sprite.Value, Is.Not.SameAs(sprites[i]));
                    Assert.That(rocks[i].TryGetGameObject(out var visual), Is.True);
                    var expected = rocks[i].OfType<AnimationSpriteObject>().First().Sprite.Value;
                    Assert.That(visual.GetComponentsInChildren<SpriteRenderer>().Any(renderer => renderer.sprite == expected), Is.True);
                }
            }
            else
            {
                foreach (var cell in cells)
                    Assert.That(placement.Content.ForegroundPrimaryObjects.GetObject<DestructablePlacedItem>(cell), Is.Null);
            }
            for (int i = 0; i < 3; i++)
                yield return null;
        }
        flow.ReturnToMenu();
        yield return Wait(flow, MainMenuState.Main);
        flow.Continue();
        yield return Wait(flow, MainMenuState.World);
        foreach (var cell in cells)
            Assert.That(GameController.Instance.Placement.Content.ForegroundPrimaryObjects.GetObject<DestructablePlacedItem>(cell), Is.Null);
        Assert.IsEmpty(errors, string.Join("\n", errors));
        yield return new ExitPlayMode();
    }
    private static IEnumerator Wait(MenuFlow flow, MainMenuState state)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 180;
        while (flow.State != state && flow.State != MainMenuState.Error && Time.realtimeSinceStartupAsDouble < deadline)
            yield return null;
        Assert.That(flow.State, Is.EqualTo(state), flow.Message);
    }
    [UnityTearDown]
    public IEnumerator Cleanup()
    {
        Application.logMessageReceived -= OnLog;
        LogAssert.ignoreFailingMessages = false;
        simulation?.Dispose();
        if (UnityEditor.EditorApplication.isPlaying)
            yield return new ExitPlayMode();
        if (directory != null && Directory.Exists(directory))
            Directory.Delete(directory, true);
    }
}

#endif
