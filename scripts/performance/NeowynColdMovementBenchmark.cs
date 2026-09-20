// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

// Copy into Assets/Tests/Editor in an isolated Neowyn checkout.
// Measures first input without warming position, facing, or animation setters.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Assets.Scripts.Neo.Menu;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Body = Assets.Scripts.Neo.CharacterCreator.BodyAnimator;
using Object = UnityEngine.Object;

public class NeowynColdMovementBenchmark
{
    [UnityTest]
    public IEnumerator FirstMovement()
    {
        yield return new EnterPlayMode();
        UnityEditorInternal.ProfilerDriver.enabled = false;
        Application.runInBackground = true;
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        string directory = Path.Combine(Path.GetTempPath(), "neo-cold-" + Guid.NewGuid());
        void Configure(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (scene.name == "NeoMenu") Object.FindAnyObjectByType<NeoMenuFlow>().SaveDirectory = directory;
        }
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += Configure;
        yield return UnityEngine.SceneManagement.SceneManager.LoadSceneAsync("Assets/Scenes/NeoMenu.unity");
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= Configure;
        var flow = Object.FindAnyObjectByType<NeoMenuFlow>();
        yield return Wait(flow, NeoMenuState.Main);
        flow.NewGame();
        yield return Wait(flow, NeoMenuState.World);
        var body = Object.FindAnyObjectByType<Body>();
        Assert.IsTrue(body.CanAnimate);
        var pad = InputSystem.AddDevice<Gamepad>();
        yield return null;
        try
        {
            for (int trial = 0; trial < 3; trial++)
            {
                var startPosition = body.transform.position;
                var frames = new List<double>();
                double start = Time.realtimeSinceStartupAsDouble;
                double previous = start;
                InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = trial % 2 == 0 ? Vector2.right : Vector2.left });
                while (Time.realtimeSinceStartupAsDouble - start < (trial == 0 ? 10 : 2))
                {
                    yield return null;
                    double now = Time.realtimeSinceStartupAsDouble;
                    frames.Add((now - previous) * 1000);
                    previous = now;
                }
                float distance = Vector3.Distance(startPosition, body.transform.position);
                Debug.Log($"NEO_COLD trial={trial} frames={frames.Count} maxMs={frames.Max():F3} meanMs={frames.Average():F3} distance={distance:F3} firstFrames=" + string.Join(",", frames.Take(12).Select(v => v.ToString("F3"))));
                Assert.Greater(distance, 0f);
                InputSystem.QueueStateEvent(pad, new GamepadState());
                start = Time.realtimeSinceStartupAsDouble;
                while (Time.realtimeSinceStartupAsDouble - start < .5) yield return null;
            }
        }
        finally { InputSystem.RemoveDevice(pad); }
        flow.ReturnToMenu();
        yield return Wait(flow, NeoMenuState.Main);
        yield return new ExitPlayMode();
        Directory.Delete(directory, true);
    }
    private static IEnumerator Wait(NeoMenuFlow flow, NeoMenuState state)
    {
        double deadline = Time.realtimeSinceStartupAsDouble + 120;
        while (flow.State != state && Time.realtimeSinceStartupAsDouble < deadline)
        {
            Assert.AreNotEqual(NeoMenuState.Error, flow.State, flow.Message);
            yield return null;
        }
        Assert.AreEqual(state, flow.State, flow.Message);
    }
}
