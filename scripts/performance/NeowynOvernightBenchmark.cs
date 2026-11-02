// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

#if NEO_COMPOSE_NEOWYN_TESTS
// Copy into Assets/Tests/Editor of an isolated Neowyn checkout.
// NEO_OVERNIGHT_SEED must name a populated local save directory. Never use
// the developer's active save directory: this probe copies it before loading.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Scripts;
using Assets.Scripts.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public class NeowynOvernightBenchmark
{
    private FixedStepTestClock simulation;
    private string trial;
    private readonly List<string> errors = new();

    [UnityTest, Timeout(600000)]
    public IEnumerator Overnight()
    {
        string seed = Environment.GetEnvironmentVariable("NEO_OVERNIGHT_SEED");
        Assert.IsTrue(Directory.Exists(seed), "Set NEO_OVERNIGHT_SEED to a crowded local save directory.");
        trial = Path.Combine(Path.GetTempPath(), "neo-overnight-" + Guid.NewGuid());
        Directory.CreateDirectory(trial);
        foreach (string file in Directory.GetFiles(seed, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(trial, Path.GetRelativePath(seed, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(file, destination);
        }
        yield return TestRunEnvironment.EnterPlayMode();
        simulation = new FixedStepTestClock(1f / 60f);
        // The crowded fixture blocks Shopkeeper's authored morning waypoint.
        // Permit that known navigation error, but fail on every other error.
        Application.logMessageReceived += OnLog;
        LogAssert.ignoreFailingMessages = true;
        void Configure(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (scene.name == "Menu")
                TestProjectSource.ConfigureMenu(Object.FindAnyObjectByType<MenuFlow>(), trial);
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
        flow.Continue();
        yield return Wait(flow, MainMenuState.World);
        UnityEngine.Random.InitState(314159);
        flow.Client.Client.Random.Seed = 314159;
        var watch = new System.Diagnostics.Stopwatch();
        for (int night = 1; night <= 5; night++)
        {
            int before = GameController.Instance.Placement.Content.ForegroundPrimaryObjects.GetObjects().Count();
            watch.Restart();
            flow.Client.Save.World.ProcessSleep();
            watch.Stop();
            double sleepMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            flow.Client.Save.World.Time.SetToNextMorning();
            watch.Stop();
            int after = GameController.Instance.Placement.Content.ForegroundPrimaryObjects.GetObjects().Count();
            TestContext.WriteLine($"NEO_OVERNIGHT night={night} before={before} after={after} sleepMs={sleepMs:F3} morningMs={watch.Elapsed.TotalMilliseconds:F3}");
            for (int frame = 0; frame < 3; frame++)
                yield return null;
        }
        int savedCount = GameController.Instance.Placement.Content.ForegroundPrimaryObjects.GetObjects().Count();
        flow.ReturnToMenu();
        yield return Wait(flow, MainMenuState.Main);
        flow.Continue();
        yield return Wait(flow, MainMenuState.World);
        Assert.AreEqual(savedCount, GameController.Instance.Placement.Content.ForegroundPrimaryObjects.GetObjects().Count(),
            "Overnight spawns must survive save/reload.");
        TestContext.WriteLine($"NEO_OVERNIGHT reloadedObjects={savedCount}");
        Assert.IsEmpty(errors, string.Join("\n", errors));
        yield return new ExitPlayMode();
    }

    private void OnLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            return;
        if (message.StartsWith("[NPCController] Invalid schedule for NPC 'Shopkeeper'")
            && message.Contains("waypoint is not on the NavMesh"))
            return;
        errors.Add(message + "\n" + stack);
    }

    private static IEnumerator Wait(MenuFlow flow, MainMenuState state)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 300;
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
        if (trial != null && Directory.Exists(trial))
            Directory.Delete(trial, true);
    }
}
#endif
