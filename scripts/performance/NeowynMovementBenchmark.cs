// Copyright (c) Ryan Bliss and contributors. All rights reserved.
// Licensed under the MIT License.

// Copy into Assets/Tests/Editor in an isolated copy of Neowyn. Run with
// unity test <copy> --mode EditMode --filter NeowynMovementBenchmark
// Uses the bundled export and a temporary local save; never contacts a deployment.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Assets.Scripts.Neo.Menu;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Body = Assets.Scripts.Neo.CharacterCreator.BodyAnimator;
using Object = UnityEngine.Object;

public class NeowynMovementBenchmark
{
    [UnityTest]
    public IEnumerator Movement()
    {
        yield return new EnterPlayMode();
        Application.runInBackground = true;
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        string directory = Path.Combine(Path.GetTempPath(), "neo-movement-" + Guid.NewGuid());
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
        yield return null;
        var body = Object.FindAnyObjectByType<Body>();
        Assert.IsTrue(body.CanAnimate);
        var origin = body.Body.Position.Value;
        var facing = body.Config.Facing;
        var sw = new System.Diagnostics.Stopwatch();
        var samples = new List<double>();
        for (int i = 0; i < 32; i++)
        {
            sw.Restart();
            body.Body.Position = origin + new Vector3(i % 2 == 0 ? .001f : 0, 0, 0);
            sw.Stop();
            if (i >= 2) samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        Report("position", samples);
        samples.Clear();
        for (int i = 0; i < 12; i++)
        {
            sw.Restart();
            body.Config.Facing = i % 2 == 0 ? Assets.Scripts.Neo.Facing.Left : Assets.Scripts.Neo.Facing.Right;
            sw.Stop();
            if (i >= 2) samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        Report("facing", samples);
        body.Config.Facing = facing;
        samples.Clear();
        body.SetIsMoving(true);
        var clip = body.Body.Walk;
        var tick = clip.GetType().GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
        for (int i = 0; i < 32; i++)
        {
            sw.Restart(); tick.Invoke(clip, new object[] { .125f }); sw.Stop();
            if (i >= 2) samples.Add(sw.Elapsed.TotalMilliseconds);
        }
        Report("animation", samples);
        body.SetIsMoving(false);
        for (int i = 0; i < 10; i++) yield return null;
        var pad = InputSystem.AddDevice<Gamepad>();
        var seen = new HashSet<string>();
        bool diagnostics = Environment.GetEnvironmentVariable("NEO_MOVEMENT_DIAGNOSTICS") == "1";
        using var mainThread = diagnostics ? ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread") : default;
        using var playerLoop = diagnostics ? ProfilerRecorder.StartNew(ProfilerCategory.Internal, "PlayerLoop") : default;
        using var editorLoop = diagnostics ? ProfilerRecorder.StartNew(ProfilerCategory.Internal, "EditorLoop") : default;
        var detailRecorders = new List<(string name, ProfilerRecorder recorder)>();
        if (diagnostics)
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var handle in handles)
            {
                string name = ProfilerRecorderHandle.GetDescription(handle).Name;
                if (name.StartsWith("GC.Collect") || name.StartsWith("NeoCompose.") || name.Contains("Behaviour") || name.Contains("ScriptRun")
                    || name.Contains("DelayedCall") || name.Contains("Garbage") || name.Contains("Physics2D"))
                    detailRecorders.Add((name, new ProfilerRecorder(handle, 1,
                        ProfilerRecorderOptions.Default | ProfilerRecorderOptions.StartImmediately)));
            }
        }
        try
        {
            foreach (bool moving in new[] { false, true })
            {
                samples.Clear();
                double start = Time.realtimeSinceStartupAsDouble;
                double previous = start;
                int collections = GC.CollectionCount(0);
                var slowFrames = new List<string>();
                float distance = 0;
                var last = Object.FindAnyObjectByType<Body>().transform.position;
                while (Time.realtimeSinceStartupAsDouble - start < (diagnostics ? 30 : 8))
                {
                    double elapsed = Time.realtimeSinceStartupAsDouble - start;
                    InputSystem.QueueStateEvent(pad, new GamepadState { leftStick = moving
                        ? new Vector2(((int)(elapsed / .6) % 2 == 0) ? 1 : -1, 0) : Vector2.zero });
                    yield return null;
                    var current = Object.FindAnyObjectByType<Body>();
                    seen.Add(current.GetEntityId().ToString());
                    distance += Vector3.Distance(last, current.transform.position);
                    last = current.transform.position;
                    double now = Time.realtimeSinceStartupAsDouble;
                    if (now - start > 1) samples.Add((now - previous) * 1000);
                    if (diagnostics)
                    {
                        int nextCollections = GC.CollectionCount(0);
                        if (now - previous > .01)
                        {
                            slowFrames.Add($"NEO_MOVEMENT slowFrame moving={moving} elapsed={now - start:F3} wallMs={(now - previous) * 1000:F3} gcCollections={nextCollections - collections} heapMB={GC.GetTotalMemory(false) / 1048576.0:F1} mainMs={mainThread.LastValue / 1e6:F3} playerMs={playerLoop.LastValue / 1e6:F3} editorMs={editorLoop.LastValue / 1e6:F3}");
                            slowFrames.Add("NEO_MOVEMENT markers " + string.Join("; ", detailRecorders
                                .Select(item => (item.name, value: item.recorder.LastValue))
                                .Where(item => item.value > 1000000).OrderByDescending(item => item.value).Take(8)
                                .Select(item => $"{item.name}={item.value / 1e6:F3}")));
                        }
                        collections = nextCollections;
                    }
                    previous = now;
                }
                Report(moving ? "walking-frame" : "idle-frame", samples);
                Debug.Log($"NEO_MOVEMENT moving={moving} distance={distance:F3} bodyInstances={seen.Count}");
                foreach (string slowFrame in slowFrames) Debug.Log(slowFrame);
                if (moving) Assert.Greater(distance, 1f, "The walking benchmark must actually move the player.");
            }
        }
        finally
        {
            InputSystem.RemoveDevice(pad);
            foreach (var item in detailRecorders) item.recorder.Dispose();
        }
        flow.ReturnToMenu();
        yield return Wait(flow, NeoMenuState.Main);
        yield return new ExitPlayMode();
        Directory.Delete(directory, true);
    }

    private static void Report(string operation, List<double> values)
    {
        values.Sort();
        Debug.Log($"NEO_MOVEMENT operation={operation} samples={values.Count} meanMs={values.Average():F4} medianMs={values[values.Count / 2]:F4} p95Ms={values[(int)((values.Count - 1) * .95)]:F4} p99Ms={values[(int)((values.Count - 1) * .99)]:F4} maxMs={values.Last():F4} meanFps={1000 / values.Average():F1}");
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
